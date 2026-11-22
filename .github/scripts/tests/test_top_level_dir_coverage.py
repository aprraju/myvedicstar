#!/usr/bin/env python3
"""Tests for check-top-level-dir-coverage.py (api#90).

Two kinds of test, and the distinction matters:

* Synthetic tests drive the checker over a scratch repository (a temporary directory with its own
  .github/workflows and its own top-level directories). They are the red-first proof: a new directory
  with no matching paths: filter makes the checker exit 1 and name the directory, and adding the filter
  turns it green with no other change. The before/after differ by exactly the thing under test.

* The real-repository test runs the checker against THIS checkout. It asserts the repository is green
  today, so a workflow that loses a directory shows up as a failing test rather than as silence.

Run: python3 .github/scripts/tests/test_top_level_dir_coverage.py
"""
import importlib.util
import os
import subprocess
import sys
import tempfile
import textwrap
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
CHECKER = REPO_ROOT / ".github" / "scripts" / "check-top-level-dir-coverage.py"


def load_checker():
    """Import the checker by path: it is a hyphenated script, not an importable module name."""
    spec = importlib.util.spec_from_file_location("coverage_checker", CHECKER)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


CHECKER_MODULE = load_checker()


class ScratchRepo:
    """A throwaway repository: a directory, some top-level directories, and workflow files."""

    def __init__(self, directories, workflows):
        self._tmp = tempfile.TemporaryDirectory()
        self.root = Path(self._tmp.name)
        (self.root / ".github" / "workflows").mkdir(parents=True)
        for directory in directories:
            (self.root / directory).mkdir(parents=True, exist_ok=True)
        for name, content in workflows.items():
            (self.root / ".github" / "workflows" / name).write_text(
                textwrap.dedent(content), encoding="utf-8")

    def run(self, argv_extra=()):
        """Runs the real script, as CI does, with the scratch root substituted for the repository root."""
        env = dict(os.environ)
        env["COVERAGE_CHECKER_ROOT"] = str(self.root)
        return subprocess.run(
            [sys.executable, str(CHECKER), *argv_extra],
            capture_output=True, text=True, env=env, timeout=120)

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        self._tmp.cleanup()


SRC_WORKFLOW = """\
    name: src ci
    on:
      pull_request:
        paths:
          - 'src/**'
    """


class NewTopLevelDirectoryGoesRedFirst(unittest.TestCase):
    """The failure the guard exists for, reproduced on a scratch directory."""

    def test_a_new_directory_with_no_filter_is_red_and_named(self):
        with ScratchRepo(["src", "brandnew"], {"src-ci.yml": SRC_WORKFLOW}) as repo:
            result = repo.run()

        self.assertEqual(1, result.returncode,
                         f"expected a red run, got {result.returncode}: {result.stdout}{result.stderr}")
        self.assertIn("brandnew/", result.stdout,
                      "the failing run must name the directory, or the report is not actionable")
        self.assertNotIn("  - src/", result.stdout,
                         "a covered directory must not be reported as uncovered")

    def test_adding_the_filter_turns_the_same_scratch_repo_green(self):
        # Same directories, one edited workflow. This is the only difference between red and green, which is
        # what makes the check evidence rather than a coincidence.
        workflow = """\
            name: src ci
            on:
              pull_request:
                paths:
                  - 'src/**'
                  - 'brandnew/**'
            """
        with ScratchRepo(["src", "brandnew"], {"src-ci.yml": workflow}) as repo:
            result = repo.run()

        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn("2 checked", result.stdout,
                      "the green run must state how many directories it checked, so a skipped scan is visible")

    def test_the_probe_is_a_file_inside_the_directory_not_the_directory_itself(self):
        # 'dir/**' matches 'dir/probe'. Without this, a checker that probed 'dir' itself would call a
        # prefix-only pattern uncovered and pass for the wrong reason.
        self.assertTrue(CHECKER_MODULE.matches("brandnew/**", "brandnew/probe"))
        self.assertFalse(CHECKER_MODULE.matches("brandnew/*", "brandnew/deep/probe"))


class TheDocumentedGlobDialectIsWhatIsEnforced(unittest.TestCase):
    """api#179's matcher, exercised from this suite too (api#90).

    These expectations are the DOCUMENTED GitHub dialect, not whatever the checker happened to do before:
    "**" spans directory separators, "*" does not, and a single-segment filter must not be read as coverage
    for a nested change. If a future change narrows any of these, this suite goes red alongside api#179's own.
    """

    def test_double_star_spans_directories_and_single_star_does_not(self):
        self.assertTrue(CHECKER_MODULE.matches("brandnew/**", "brandnew/a/b/probe.md"))
        self.assertFalse(CHECKER_MODULE.matches("brandnew/*", "brandnew/a/b/probe.md"))

    def test_a_single_star_filter_is_red_at_depth_2_even_though_it_covers_depth_1(self):
        # Depth 1 matches, depth 2 does not - so the directory is NOT covered. This is the #179 finding, asserted
        # through the whole script rather than the matcher alone.
        workflow = """\
            name: shallow only
            on:
              pull_request:
                paths:
                  - 'astronomy/*'
            """
        with ScratchRepo(["astronomy"], {"ci.yml": workflow}) as repo:
            result = repo.run()

        self.assertEqual(1, result.returncode, result.stdout + result.stderr)
        self.assertIn("astronomy/ (uncovered at depth 2)", result.stdout,
                      "the report must name the depth, or the author cannot tell what to widen")


class CoverageNeedsAPathFilterNotAnyMention(unittest.TestCase):
    """A directory named in a workflow is not covered unless a paths: filter would match a change in it."""

    def test_paths_ignore_does_not_count_as_coverage(self):
        workflow = """\
            name: ignores
            on:
              pull_request:
                paths:
                  - 'src/**'
                paths-ignore:
                  - 'brandnew/**'
            """
        with ScratchRepo(["src", "brandnew"], {"ci.yml": workflow}) as repo:
            result = repo.run()

        self.assertEqual(1, result.returncode,
                         "paths-ignore means the workflow does NOT run for that directory; it cannot be coverage")
        self.assertIn("brandnew/", result.stdout)

    def test_a_directory_named_only_in_a_comment_or_job_name_is_not_covered(self):
        workflow = """\
            name: brandnew build
            jobs:
              build:
                # brandnew/ is built elsewhere
                runs-on: ubuntu-latest
                steps:
                  - run: echo brandnew/**
            """
        with ScratchRepo(["brandnew"], {"ci.yml": workflow}) as repo:
            result = repo.run()

        self.assertEqual(1, result.returncode)
        self.assertIn("brandnew/", result.stdout)

    def test_a_deep_literal_path_does_not_cover_the_directory(self):
        # 'src/OnlyProject/**' matches nothing in a sibling top-level directory, and must not be read as 'src/'.
        workflow = """\
            name: partial
            on:
              pull_request:
                paths:
                  - 'src/OnlyProject/**'
            """
        with ScratchRepo(["src"], {"ci.yml": workflow}) as repo:
            result = repo.run()

        self.assertEqual(1, result.returncode, "a single project path is not coverage for its parent directory")


class TheCheckerRefusesToPassByNotRunning(unittest.TestCase):
    """A guard that cannot inspect anything must fail loudly, never read as green."""

    def test_no_workflows_is_an_error_not_a_pass(self):
        with ScratchRepo(["src"], {}) as repo:
            result = repo.run()

        self.assertEqual(2, result.returncode, "an uninspectable repository is an error, not a pass")

    def test_no_paths_filters_anywhere_is_red_and_lists_every_directory(self):
        # Workflows exist but none filters on paths: every directory is uncovered. That is the strongest finding,
        # and it must be red - not an error code that a workflow could misread, and not a pass.
        workflow = """\
            name: no filters
            on:
              pull_request:
            """
        with ScratchRepo(["src", "docs"], {"ci.yml": workflow}) as repo:
            result = repo.run()

        self.assertEqual(1, result.returncode, result.stdout + result.stderr)
        self.assertIn("  - src/", result.stdout)
        self.assertIn("  - docs/", result.stdout)
        self.assertIn("No paths: filters", result.stdout,
                      "the red run must say WHY every directory is covered by nothing")

    def test_empty_directory_set_is_an_error(self):
        with ScratchRepo([], {"src-ci.yml": SRC_WORKFLOW}) as repo:
            result = repo.run()

        self.assertEqual(2, result.returncode, "a scan that found no directories did not scan")


class TheRealRepositoryIsGreen(unittest.TestCase):
    """The check as CI runs it, against this checkout."""

    def test_this_repository_passes(self):
        result = subprocess.run([sys.executable, str(CHECKER)],
                                capture_output=True, text=True, timeout=120)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main(verbosity=2)
