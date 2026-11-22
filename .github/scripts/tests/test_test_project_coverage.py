#!/usr/bin/env python3
"""Tests for check-test-project-coverage.py (api#90 slice, on top of api#172).

Two questions, and this file answers both:

* Does the checker still FAIL for the right reasons? Scratch repositories, a temp tests/ tree, and the
  real exceptions file replaced for the duration - never the repository's own files.
* Is the exception list minimal? The production test runs the real checker against this checkout and
  asserts the property directly: no exception names a project that CI already reaches, and if the list is
  empty it says so as the provable minimum rather than as a claim in a comment.

Run: python3 .github/scripts/tests/test_test_project_coverage.py
"""
import importlib.util
import json
import os
import shutil
import subprocess
import sys
import tempfile
import textwrap
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
CHECKER = REPO_ROOT / ".github" / "scripts" / "check-test-project-coverage.py"


def load_checker():
    spec = importlib.util.spec_from_file_location("coverage_checker", CHECKER)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


CHECKER_MODULE = load_checker()


CSLIB = """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>"""


def lib(path, references=()):
    """A project at 'path' referencing the projects at the given repository-relative .csproj paths.

    References are resolved relative to the project file, as MSBuild does, so a fixture cannot silently
    produce a reference to nowhere - which would let a "transitive" test pass while reaching one hop.
    """
    here = Path(path).parent
    refs = ""
    for target in references:
        include = Path(os.path.relpath(Path(target), here)).as_posix()
        refs += f'<ProjectReference Include="{include}" />'
    return (
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
        "<TargetFramework>net10.0</TargetFramework></PropertyGroup>"
        f"<ItemGroup>{refs}</ItemGroup></Project>")


def workflow(paths, dotnet="dotnet test tests/UsesLib/UsesLib.csproj"):
    return textwrap.dedent(f"""\
        name: ci
        on:
          pull_request:
            paths:
              - 'src/**'
        jobs:
          build:
            runs-on: ubuntu-latest
            steps:
              - run: {dotnet}
        """)


class Scratch:
    """A throwaway repository with real .csproj files, so ProjectReference parsing is exercised as shipped."""

    def __init__(self, projects, workflows, exceptions=()):
        self._tmp = tempfile.TemporaryDirectory()
        self.root = Path(self._tmp.name)
        (self.root / ".github" / "workflows").mkdir(parents=True)
        for relative, content in projects.items():
            target = self.root / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(content, encoding="utf-8")
        for name, content in workflows.items():
            (self.root / ".github" / "workflows" / name).write_text(content, encoding="utf-8")
        (self.root / ".github" / "test-project-coverage-exceptions.json").write_text(
            json.dumps({"exceptions": list(exceptions)}), encoding="utf-8")

    def run(self):
        env = dict(os.environ)
        env["PROJECT_COVERAGE_CHECKER_ROOT"] = str(self.root)
        return subprocess.run([sys.executable, str(CHECKER)], capture_output=True, text=True,
                              env=env, timeout=120)

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        self._tmp.cleanup()


BASE_PROJECTS = {
    "src/Lib/Lib.csproj": lib("src/Lib/Lib.csproj"),
    "src/Other/Other.csproj": lib("src/Other/Other.csproj"),
    "tests/UsesLib/UsesLib.csproj": lib("tests/UsesLib/UsesLib.csproj", ["src/Lib/Lib.csproj"]),
    "tests/Orphan/Orphan.csproj": lib("tests/Orphan/Orphan.csproj"),
}


class ALibraryReachedThroughAReferenceIsCovered(unittest.TestCase):
    """The whole point: a test project CI builds indirectly is executed, and needs no exception."""

    def test_referenced_only_through_the_project_ci_runs_is_covered(self):
        # UsesLib is named by CI; it references Lib. Lib is therefore reached. Orphan is reached by nothing.
        with Scratch(BASE_PROJECTS, {"ci.yml": workflow(["src/**"])}) as repo:
            result = repo.run()

        self.assertEqual(1, result.returncode, result.stdout + result.stderr)
        self.assertIn("tests/Orphan/", result.stdout)
        self.assertNotIn("tests/UsesLib/", result.stdout,
                         "a project reachable through a ProjectReference is executed, not uncovered")

    def test_reachability_is_transitive(self):
        # A three-hop chain: the workflow runs tests/Runs, which reaches A -> B -> C. Every hop is a
        # ProjectReference, so a checker that read only direct references would report B and C uncovered.
        projects = {
            "src/A/A.csproj": lib("src/A/A.csproj", ["src/B/B.csproj"]),
            "src/B/B.csproj": lib("src/B/B.csproj", ["src/C/C.csproj"]),
            "src/C/C.csproj": lib("src/C/C.csproj"),
            "tests/Runs/Runs.csproj": lib("tests/Runs/Runs.csproj", ["src/A/A.csproj"]),
        }
        with Scratch(projects, {"ci.yml": workflow(["src/**"], "dotnet test tests/Runs/Runs.csproj")}) as repo:
            result = repo.run()

        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn("4 reachable", result.stdout)


class TheExceptionListCannotAccumulate(unittest.TestCase):
    """An exception that exempts nothing is reported, and an exception for a live project is not stale."""

    def test_an_exception_for_a_reachable_project_is_flagged_removable(self):
        # Orphan is exempted too, so the ONLY finding is the redundant exception - otherwise this test
        # would pass for the wrong reason when an unrelated project is uncovered.
        exceptions = [
            {"project": "tests/UsesLib/UsesLib.csproj", "reason": "api#172 legacy entry"},
            {"project": "tests/Orphan/Orphan.csproj", "reason": "needs a cluster, deliberately not run"},
        ]
        with Scratch(BASE_PROJECTS, {"ci.yml": workflow(["src/**"])}, exceptions) as repo:
            result = repo.run()

        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn("::warning::tests/UsesLib/UsesLib.csproj is now reachable from CI but still exempt",
                      result.stdout,
                      "a redundant exception must be named every run, not silently tolerated")

    def test_an_exception_for_a_missing_project_is_stale_and_red(self):
        exceptions = [{"project": "tests/Gone/Gone.csproj", "reason": "deleted"}]
        with Scratch(BASE_PROJECTS, {"ci.yml": workflow(["src/**"])}, exceptions) as repo:
            result = repo.run()

        self.assertEqual(1, result.returncode)
        self.assertIn("no longer exist", result.stdout)

    def test_an_exception_without_a_reason_is_refused(self):
        exceptions = [{"project": "tests/Orphan/Orphan.csproj", "reason": "   "}]
        with Scratch(BASE_PROJECTS, {"ci.yml": workflow(["src/**"])}, exceptions) as repo:
            result = repo.run()

        self.assertEqual(2, result.returncode, result.stdout + result.stderr)
        self.assertIn("needs both 'project' and 'reason'", result.stderr + result.stdout)

    def test_an_exception_with_a_reason_silences_only_that_project(self):
        exceptions = [{"project": "tests/Orphan/Orphan.csproj", "reason": "deliberately not run: needs a cluster"}]
        with Scratch(BASE_PROJECTS, {"ci.yml": workflow(["src/**"])}, exceptions) as repo:
            result = repo.run()

        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn("Exceptions (1): minimal", result.stdout)


class TheCheckerRefusesToPassByNotRunning(unittest.TestCase):
    def test_no_workflows_is_cannot_run(self):
        with Scratch(BASE_PROJECTS, {}) as repo:
            result = repo.run()
        self.assertEqual(2, result.returncode)

    def test_no_test_projects_is_cannot_run(self):
        projects = {"src/Lib/Lib.csproj": lib("src/Lib/Lib.csproj")}
        with Scratch(projects, {"ci.yml": workflow(["src/**"])}) as repo:
            result = repo.run()
        self.assertEqual(2, result.returncode, "an empty universe proves nothing")

    def test_no_dotnet_command_is_cannot_run(self):
        no_command = "name: ci\non:\n  pull_request:\n"
        with Scratch(BASE_PROJECTS, {"ci.yml": no_command}) as repo:
            result = repo.run()
        self.assertEqual(2, result.returncode, "the executed set cannot be derived, so no claim is possible")


class TheRealRepositoryIsMinimal(unittest.TestCase):
    """Against this checkout, as CI runs it."""

    def test_this_repository_passes_with_a_minimal_exception_list(self):
        result = subprocess.run([sys.executable, str(CHECKER)], capture_output=True, text=True, timeout=120)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)

        exceptions = json.loads(
            (REPO_ROOT / ".github" / "test-project-coverage-exceptions.json").read_text(encoding="utf-8")
        ).get("exceptions", [])
        if not exceptions:
            self.assertIn("Exceptions: none", result.stdout)
            return

        # If an entry ever returns, prove it is for a project CI genuinely does not reach.
        named = CHECKER_MODULE.executed_paths()
        executed = set()
        for workflow_name, path in named:
            projects, _error = CHECKER_MODULE.resolve_named_path(path, workflow_name)
            executed |= projects or set()
        reached = CHECKER_MODULE.closure(executed, {})
        for entry in exceptions:
            project = (REPO_ROOT / entry["project"]).resolve()
            self.assertNotIn(project, reached,
                             f"{entry['project']} is exempt but CI reaches it; remove the exception")


if __name__ == "__main__":
    unittest.main(verbosity=2)
