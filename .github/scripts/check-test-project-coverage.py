#!/usr/bin/env python3
"""Fail when a test project is executed by no workflow.

Why this exists (api#172): CI runs tests through four disjoint runners - astronomy-ci.yml,
authorize-api.yml, myvedicday-ci.yml and shared-deploy.yml - and the union of what they execute
is written down nowhere. Every test project under tests/ is executed today, so this is not a
coverage hole; it is a VISIBILITY defect. A project can be moved, added, or dropped in a way
that leaves it executed by nothing and no check would notice.

The rule is deliberately fail-closed, in the same words as check-top-level-dir-coverage.py
(api#90): a project is executed only if some workflow's dotnet command would actually reach it.
"Somebody remembered" is not coverage.

Self-correcting by construction: the executed set is DERIVED from the workflow files, never
written down here. A workflow that stops running a solution shrinks the union, and the next PR
goes red; a new test project in no solution goes red. A new workflow or an added solution grows
the union with no edit to this file. A hand-maintained list would have the opposite property.

Deliberately does NOT shell out to `dotnet sln list`: that needs an SDK and a restore, and a
check that can fail because restore broke is a false signal in the one place people look for
true ones. Both solution formats are readable as text.

Exit: 0 covered, 1 uncovered project(s) found, 2 the checker itself could not run.
"""
import json
import re
import sys
from pathlib import Path
from xml.etree import ElementTree

ROOT = Path(__file__).resolve().parents[2]
WORKFLOWS = ROOT / ".github" / "workflows"
TESTS_DIR = ROOT / "tests"

# Each entry is {"project": "<repo-relative path>", "reason": "<why no workflow runs it>"}.
# Empty at introduction (api#172): all 23 test projects are executed today.
EXCEPTIONS_FILE = ROOT / ".github" / "test-project-coverage-exceptions.json"

# Solution folders in .sln and .slnx are grouping nodes, not projects. Their "path" is a name,
# never a .csproj, but they are named explicitly so a future format change is not read as a project.
SOLUTION_FOLDER_TYPE = "{2150E333-8FDC-42A3-9474-1A3956D46DE8}"

# `dotnet restore|build|test <path>` - the path is the first bare argument after the verb.
# Options may follow it; a command with no path (the package probe in astronomy-package.yml) has
# nothing to resolve and is skipped rather than treated as a wildcard.
DOTNET_COMMAND = re.compile(r"\bdotnet\s+(restore|build|test)\s+(\S+)")

# Arguments that can never be a path argument to one of those verbs.
NOT_A_PATH = {"--", "-", "||", "&&", ";", "|"}


def workflow_files():
    return sorted(WORKFLOWS.glob("*.yml")) + sorted(WORKFLOWS.glob("*.yaml"))


def executed_paths():
    """Every path named in a dotnet restore/build/test command, with the workflow that names it."""
    found = []
    for workflow in workflow_files():
        text = workflow.read_text(encoding="utf-8")
        for match in DOTNET_COMMAND.finditer(text):
            path = match.group(2).strip("'\"")
            if path in NOT_A_PATH or path.startswith("-"):
                continue
            # Trailing shell punctuation / redirection is not part of the path.
            path = re.split(r"[;&|<>]", path, maxsplit=1)[0]
            if not path:
                continue
            found.append((workflow.name, path))
    return found


def projects_in_slnx(path: Path):
    """<Project Path="..." /> elements. .slnx is XML, so read it as XML rather than by regex."""
    tree = ElementTree.parse(path)
    projects = set()
    for element in tree.getroot().iter("Project"):
        project_path = element.get("Path")
        if project_path:
            projects.add(project_path.replace("\\", "/"))
    return projects


def projects_in_sln(path: Path):
    """Project(...) = "name", "path", "{guid}" lines; solution folders are skipped."""
    projects = set()
    line_pattern = re.compile(r'^Project\("(\{[^}]+\})"\)\s*=\s*"([^"]*)",\s*"([^"]*)",')
    for raw in path.read_text(encoding="utf-8", errors="replace").splitlines():
        match = line_pattern.match(raw.strip())
        if not match:
            continue
        type_id, _name, project_path = match.groups()
        if type_id.upper() == SOLUTION_FOLDER_TYPE.upper():
            continue
        # A solution folder's path is a directory name; belt and braces alongside the GUID check.
        if not project_path.lower().endswith((".csproj", ".fsproj", ".vbproj")):
            continue
        projects.add(project_path.replace("\\", "/"))
    return projects


def resolve_named_path(named_path: str, named_in: str):
    """Resolve a workflow-named path to the .csproj files it causes CI to execute.

    Returns (projects, error). error is a message when the path cannot be resolved at all - the
    difference between "cannot run" and "uncovered" has to stay visible.
    """
    candidate = (ROOT / named_path).resolve()
    try:
        candidate.relative_to(ROOT)
    except ValueError:
        # Outside the checkout (e.g. a runner temp path); nothing in the repo to resolve.
        return set(), None

    if candidate.is_dir():
        return {p.resolve() for p in candidate.rglob("*.csproj")}, None

    if not candidate.exists():
        # A glob, a variable, or a path that only exists at runtime. Name it: silence here would
        # hide a workflow that stopped running a real solution.
        if any(token in named_path for token in ("*", "$", "{", "}")):
            return set(), None
        return None, f"{named_in} names '{named_path}', which does not exist in the repository"

    suffix = candidate.suffix.lower()
    if suffix == ".slnx":
        return {(candidate.parent / p).resolve() for p in projects_in_slnx(candidate)}, None
    if suffix in (".sln", ".slnf"):
        return {(candidate.parent / p).resolve() for p in projects_in_sln(candidate)}, None
    if suffix == ".csproj":
        return {candidate}, None

    # Some other file (a script, a props file): it is not a solution, so it executes no projects.
    return set(), None


def load_exceptions():
    if not EXCEPTIONS_FILE.exists():
        return {}
    data = json.loads(EXCEPTIONS_FILE.read_text(encoding="utf-8"))

    exceptions = {}
    for entry in data.get("exceptions", []):
        project = entry.get("project")
        reason = (entry.get("reason") or "").strip()
        if not project or not reason:
            raise ValueError(f"exception entry needs both 'project' and 'reason': {entry!r}")
        exceptions[(ROOT / project).resolve()] = reason
    return exceptions


def main() -> int:
    if not WORKFLOWS.is_dir():
        print(f"::error::No workflow directory at {WORKFLOWS}")
        return 2
    if not TESTS_DIR.is_dir():
        print(f"::error::No tests directory at {TESTS_DIR}, so the universe cannot be derived")
        return 2

    workflows = workflow_files()
    if not workflows:
        print(f"::error::No workflows found under {WORKFLOWS}")
        return 2

    universe = {path.resolve() for path in TESTS_DIR.rglob("*.csproj")}
    if not universe:
        print(f"::error::No test projects found under {TESTS_DIR}; the universe is empty, so this")
        print("::error::check cannot prove coverage and must not report success.")
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
        print("::error::No test project could be resolved to any workflow.")
        print("::error::Cannot distinguish 'uncovered' from 'unreadable' without input; failing closed as")
        print("::error::cannot-run (exit 2) rather than reporting projects as uncovered they may not be.")
        for message in unresolved:
            print(f"  - {message}")
        return 2

    try:
        exceptions = load_exceptions()
    except (ValueError, OSError) as error:
        print(f"::error::Cannot read the exceptions file {EXCEPTIONS_FILE}: {error}")
        return 2

    uncovered = sorted(universe - executed - set(exceptions))
    stale = sorted(set(exceptions) - universe)

    if not uncovered and not stale:
        print(
            f"Every test project is executed by a workflow "
            f"({len(universe)} checked, {len(executed)} resolved from {len(named)} dotnet command(s))."
        )
        return 0

    if uncovered:
        print("::error::These test projects are executed by NO workflow:")
        for project in uncovered:
            print(f"  - {project.relative_to(ROOT).as_posix()}")
        print("")
        print("Add the project to a solution a workflow runs, add it to a workflow directly, or add an")
        print(f"exemption to {EXCEPTIONS_FILE.relative_to(ROOT).as_posix()} with a reason.")
        print("Uncovered now: " + ", ".join(p.relative_to(ROOT).as_posix() for p in uncovered))

    if stale:
        print("::error::These exceptions name projects that no longer exist, so they exempt nothing:")
        for project in stale:
            print(f"  - {project.relative_to(ROOT).as_posix()}")
        print("Remove them; an exception that outlives its project hides the next one.")

    return 1


if __name__ == "__main__":
    sys.exit(main())
