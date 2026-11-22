#!/usr/bin/env python3
"""Self-tests for check-top-level-dir-coverage.py (api#125).

Why this file exists: the depth-1 bug was invisible because the checker's only customer was CI, and
CI only ever ran it against the repo as it is today. A green run proves the current configuration is
covered; it cannot prove the checker would notice a violation. These cases pin the falsification:
a single-star filter must be reported as a gap, and a /** filter must not be.

Run with the standard library only, so CI needs no new dependency:

    python3 .github/scripts/test_check_top-level-dir-coverage.py

Exit: 0 all cases pass, 1 a case failed.
"""
import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().with_name("check-top-level-dir-coverage.py")
spec = importlib.util.spec_from_file_location("coverage_check", SCRIPT)
coverage_check = importlib.util.module_from_spec(spec)
spec.loader.exec_module(coverage_check)

WORKFLOW_TEMPLATE = """name: probe
on:
  pull_request:
    paths:
      - '{pattern}'
"""


class DepthProbeTests(unittest.TestCase):
    def test_probe_path_shapes(self):
        # Every depth carries a file suffix, so a legitimate suffix filter is not mistaken for a gap.
        self.assertEqual("astronomy/probe.md", coverage_check.probe_path("astronomy", 1))
        self.assertEqual("astronomy/probe/probe.md", coverage_check.probe_path("astronomy", 2))
        self.assertEqual("astronomy/probe/probe/probe.md", coverage_check.probe_path("astronomy", 3))

    def test_single_star_filter_is_a_gap_at_depth_two(self):
        # The api#125 defect: 'astronomy/*' matches 'astronomy/probe' but nothing deeper. The old
        # depth-1-only probe reported this directory as covered.
        covers = ["astronomy/*"]
        self.assertTrue(coverage_check.matches("astronomy/*", "astronomy/probe"))
        self.assertFalse(coverage_check.matches("astronomy/*", "astronomy/probe/probe.md"))
        self.assertEqual(2, coverage_check.first_uncovered_depth("astronomy", covers))

    def test_gitignore_style_double_star_covers_every_depth(self):
        covers = ["astronomy/**"]
        self.assertIsNone(coverage_check.first_uncovered_depth("astronomy", covers))

    def test_glob_suffix_filter_covers_every_depth(self):
        # 'docs/**.md' is a legitimate deep filter and must not be reported as a gap.
        covers = ["docs/**.md"]
        self.assertIsNone(coverage_check.first_uncovered_depth("docs", covers))

    def test_unrelated_filter_reports_depth_one(self):
        covers = ["src/**"]
        self.assertEqual(1, coverage_check.first_uncovered_depth("astronomy", covers))

    def test_unrelated_filter_is_caught_at_depth_one(self):
        self.assertEqual(1, coverage_check.first_uncovered_depth("deep", ["other/**"]))

    def test_filter_naming_a_single_file_is_caught_at_depth_two(self):
        # A pattern that pins one exact file covers depth 1 only; the deeper probe is what catches it.
        covers = ["deep/probe.md"]
        self.assertEqual(2, coverage_check.first_uncovered_depth("deep", covers))


class EndToEndTests(unittest.TestCase):
    """Runs the checker as CI does, against a throwaway tree built for each case."""

    def run_checker(self, pattern):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / ".github" / "workflows").mkdir(parents=True)
            (root / "astronomy").mkdir()
            (root / ".github" / "workflows" / "probe.yml").write_text(
                WORKFLOW_TEMPLATE.format(pattern=pattern), encoding="utf-8")

            original = coverage_check.ROOT, coverage_check.WORKFLOWS
            coverage_check.ROOT = root
            coverage_check.WORKFLOWS = root / ".github" / "workflows"
            try:
                return coverage_check.main()
            finally:
                coverage_check.ROOT, coverage_check.WORKFLOWS = original

    def test_checker_is_red_for_a_nested_only_violation(self):
        # The falsification required by api#125: red on 'astronomy/*', which is green under the old probe.
        self.assertEqual(1, self.run_checker("astronomy/*"))

    def test_checker_is_green_for_double_star(self):
        self.assertEqual(0, self.run_checker("astronomy/**"))


if __name__ == "__main__":
    sys.exit(0 if unittest.main(exit=False).result.wasSuccessful() else 1)
