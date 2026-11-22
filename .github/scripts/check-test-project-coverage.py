#!/usr/bin/env python3
"""Fail when a test project is executed by no workflow, and prove the exception list is minimal.

api#172 introduced the visibility check and an exceptions file. api#90's remaining slice is the second
half: an exception that is never re-examined is how a coverage check quietly stops being true. This script
answers, for every project and on every run:

  * is it EXECUTED by a workflow?           (derived from the workflow files, never written here)
  * if not, is it REACHABLE from one that is? (transitive ProjectReference closure - a project the runner
    builds because something it executes depends on it is executed too)
  * if neither, does an exception exist, and does its reason still hold? (an exception for a project that
    IS reachable but not executed is a candidate for removal, reported as such)

The exception list is therefore provably minimal in the only sense that can be checked mechanically: every
entry names a project that no workflow would otherwise build. Re-measurement is not a promise in a comment.
"""
import json
import os
import re
import sys
from pathlib import Path
from xml.etree import ElementTree

# PROJECT_COVERAGE_CHECKER_ROOT lets the tests run THIS script against a scratch repository - the artefact CI
# runs, not a copy of its logic. Same mechanism, same reason, as check-top-level-dir-coverage.py.
ROOT = Path(os.environ.get("PROJECT_COVERAGE_CHECKER_ROOT") or Path(__file__).resolve().parents[2]).resolve()
WORKFLOWS = ROOT / ".github" / "workflows"
TESTS_DIR = ROOT / "tests"

EXCEPTIONS_FILE = ROOT / ".github" / "test-project-coverage-exceptions.json"

SOLUTION_FOLDER_TYPE = "{2150E333-8FDC-42A3-9474-1A3956D46DE8}"

# The path is the first bare argument after the verb, but options frequently precede it
# ("dotnet test --no-build tests/X/X.csproj"), and a continuation or another command can follow
# ("dotnet build a.csproj \\"). So the argument list is captured whole and scanned, rather than
# assuming the token immediately after the verb is a path.
DOTNET_COMMAND = re.compile(r"\bdotnet\s+(?:restore|build|test)\b([^\n&|;\\]*[^\n&|;\\ ]|)")

NOT_A_PATH = {"--", "-", "||", "&&", ";", "|"}


def workflow_files():
    return sorted(WORKFLOWS.glob("*.yml")) + sorted(WORKFLOWS.glob("*.yaml"))


def executed_paths():
    found = []
    for workflow in workflow_files():
        text = workflow.read_text(encoding="utf-8")
        for match in DOTNET_COMMAND.finditer(text):
            # First bare argument that looks like a path: not a flag, not a redirect, not shell punctuation,
            # and not another word that happens to follow (a shell continuation, an echo, a variable).
            for token in match.group(1).split():
                candidate = token.strip("'\"")
                if not candidate or candidate.startswith("-") or candidate in NOT_A_PATH:
                    continue
                candidate = re.split(r"[;&|<>]", candidate, maxsplit=1)[0]
                if not candidate:
                    continue
                if candidate.startswith(("..", "./")) or "/" in candidate or candidate.endswith(
                        (".sln", ".slnx", ".csproj", ".fsproj")):
                    found.append((workflow.name, candidate))
                    break
    return found


def _legacy_executed_paths():
    """Superseded by executed_paths(); kept only so the shape of the old bug is visible in history."""
    found = []
    for workflow in workflow_files():
        text = workflow.read_text(encoding="utf-8")
        for match in DOTNET_COMMAND.finditer(text):
            for token in match.group(1).split():
                candidate = token.strip("'\"")
                if not candidate or candidate.startswith("-") or candidate in NOT_A_PATH:
                    continue
                candidate = re.split(r"[;&|<>]", candidate, maxsplit=1)[0]
                if candidate and (candidate.startswith(("..", "./")) or "/" in candidate):
                    found.append((workflow.name, candidate))
                    break
    return found


def projects_in_slnx(path):
    tree = ElementTree.parse(path)
    return {e.get("Path").replace("\\", "/") for e in tree.getroot().iter("Project") if e.get("Path")}


def projects_in_sln(path):
    projects = set()
    line_pattern = re.compile(r'^Project\("(\{[^}]+\})"\)\s*=\s*"([^"]*)",\s*"([^"]*)",')
    for raw in path.read_text(encoding="utf-8", errors="replace").splitlines():
        match = line_pattern.match(raw.strip())
        if not match:
            continue
        type_id, _name, project_path = match.groups()
        if type_id.upper() == SOLUTION_FOLDER_TYPE.upper():
            continue
        if not project_path.lower().endswith((".csproj", ".fsproj", ".vbproj")):
            continue
        projects.add(project_path.replace("\\", "/"))
    return projects


def resolve_named_path(named_path, named_in):
    candidate = (ROOT / named_path).resolve()
    try:
        candidate.relative_to(ROOT)
    except ValueError:
        return set(), None
    if candidate.is_dir():
        return {p.resolve() for p in candidate.rglob("*.csproj")}, None
    if not candidate.exists():
        if any(token in named_path for token in ("*", "$", "{", "}")):
            return set(), None
        return None, f"{named_in} names '{named_path}', which does not exist"
    if candidate.suffix.lower() in (".sln", ".slnx"):
        listed = projects_in_slnx(candidate) if candidate.suffix.lower() == ".slnx" else projects_in_sln(candidate)
        return {(ROOT / p).resolve() for p in listed if (ROOT / p).exists()}, None
    if candidate.suffix.lower() == ".csproj":
        return {candidate}, None
    return set(), None


REFERENCE = re.compile(r'<ProjectReference\s+Include="([^"]+)"')


def references(project, cache):
    """Direct ProjectReference targets of a project, resolved and de-duplicated."""
    if project in cache:
        return cache[project]
    cache[project] = set()  # breaks a legitimate cycle before it recurses forever
    targets = set()
    try:
        text = project.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return targets
    for relative in REFERENCE.findall(text):
        target = (project.parent / relative.replace("\\", "/")).resolve()
        if target.exists():
            targets.add(target)
    cache[project] = targets
    return targets


def closure(roots, cache):
    """Every project reachable from the roots, roots included."""
    seen = set()
    stack = list(roots)
    while stack:
        project = stack.pop()
        if project in seen:
            continue
        seen.add(project)
        stack.extend(references(project, cache) - seen)
    return seen


def all_projects():
    """Every .csproj in the repository. The universe is not 'tests/': that assumption is what the
    exception list was used to paper over."""
    return {p.resolve() for p in ROOT.rglob("*.csproj") if "bin" not in p.parts and "obj" not in p.parts}


def load_exceptions():
    data = json.loads(EXCEPTIONS_FILE.read_text(encoding="utf-8"))
    exceptions = {}
    for entry in data.get("exceptions", []):
        project = entry.get("project")
        reason = (entry.get("reason") or "").strip()
        if not project or not reason:
            raise ValueError(f"exception entry needs both 'project' and 'reason': {entry!r}")
        exceptions[(ROOT / project).resolve()] = reason
    return exceptions


def rel(path):
    return path.relative_to(ROOT).as_posix()


def main():
    if not WORKFLOWS.is_dir():
        print(f"::error::No workflow directory at {WORKFLOWS}")
        return 2
    if not TESTS_DIR.is_dir():
        print(f"::error::No tests directory at {TESTS_DIR}, so the test universe cannot be derived")
        return 2

    workflows = workflow_files()
    if not workflows:
        print(f"::error::No workflows found under {WORKFLOWS}")
        return 2

    test_universe = {p.resolve() for p in TESTS_DIR.rglob("*.csproj")}
    if not test_universe:
        print("::error::No test projects found under tests/; the universe is empty, so this check cannot"
              " prove coverage and must not report success.")
        return 2

    named = executed_paths()
    if not named:
        print("::error::No 'dotnet restore|build|test <path>' command found in any workflow.")
        print("::error::The executed set cannot be derived, so this check cannot prove coverage.")
        return 2

    executed = set()
    unresolved = []
    for workflow_name, path in named:
        projects, error = resolve_named_path(path, workflow_name)
        if error:
            unresolved.append(error)
            continue
        executed |= projects

    if not executed:
        print("::error::No project could be resolved to any workflow.")
        print("::error::Cannot distinguish 'uncovered' from 'unreadable' without input; failing closed as")
        print("::error::cannot-run (exit 2) rather than reporting projects as uncovered they may not be.")
        for message in unresolved:
            print(f"  - {message}")
        return 2

    cache = {}
    reachable = closure(executed, cache)

    try:
        exceptions = load_exceptions()
    except (ValueError, OSError) as error:
        print(f"::error::Cannot read the exceptions file {EXCEPTIONS_FILE}: {error}")
        return 2

    universe = all_projects()
    uncovered_tests = sorted(test_universe - reachable - set(exceptions))
    stale = sorted(set(exceptions) - universe)

    # An exception whose project is now reachable exempts nothing. It is not a failure - the check is still
    # true - but it is dead weight, and the point of this slice is that the list cannot quietly accumulate.
    removable = sorted(p for p in exceptions if p in reachable)
    unused = sorted(set(exceptions) - test_universe - reachable)

    if not uncovered_tests and not stale:
        for project in removable:
            print(f"::warning::{rel(project)} is now reachable from CI but still exempt; the exception is "
                  "redundant and should be removed - an exception that exempts nothing is how the list grows.")
        print(f"Every test project is reachable from a workflow "
              f"({len(test_universe)} test project(s), {len(executed)} named by CI, "
              f"{len(reachable)} reachable through ProjectReferences).")
        if exceptions:
            print(f"Exceptions ({len(exceptions)}): minimal - no exception names a project a workflow reaches.")
        else:
            print("Exceptions: none. The file is empty, which is the provable minimum.")
        for project in unused:
            print(f"::warning::Exception for {rel(project)} names a project outside tests/, which this check"
                  " does not govern; remove it from this file.")
        return 0

    if uncovered_tests:
        print("::error::These test projects are executed by NO workflow:")
        for project in uncovered_tests:
            print(f"  - {rel(project)}")
        print("")
        print("Add the project to a solution a workflow runs, add it to a workflow directly, or add an")
        print(f"exemption to {rel(EXCEPTIONS_FILE)} with a reason.")
        print("Uncovered now: " + ", ".join(rel(p) for p in uncovered_tests))

    if stale:
        print("::error::These exceptions name projects that no longer exist, so they exempt nothing:")
        for project in stale:
            print(f"  - {rel(project)}")
        print("Remove them; an exception that outlives its project hides the next one.")

    for project in removable:
        print(f"::warning::{rel(project)} is now reachable from CI but still exempt; remove the exception.")

    return 1


if __name__ == "__main__":
    sys.exit(main())
