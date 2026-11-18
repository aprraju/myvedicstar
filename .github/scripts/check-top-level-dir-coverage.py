#!/usr/bin/env python3
"""Fail when a top-level directory is not covered by any workflow path filter.

Why this exists (api#90): path filters are hand-maintained. When a new top-level directory
appears (astronomy/ was the last one), nothing forced anyone to ask which workflows must
cover it. A change under src/MyVedicStar.Infrastructure.DependencyInjection once added a
using for a type in another assembly WITHOUT the matching ProjectReference; Day/Star CI did
not cover that path, so an uncompilable commit reached main.

The rule is deliberately fail-closed: a directory is covered only if at least one workflow's
paths: filter would actually match a file inside it. "Somebody remembered" is not coverage.

Exit: 0 covered, 1 uncovered directories found, 2 the checker itself could not run.
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORKFLOWS = ROOT / ".github" / "workflows"

# Directories that legitimately need no path filter of their own.
# .github is covered implicitly (every workflow triggers on its own file); .git, bin, obj are artifacts.
IGNORED = {".git", ".github", "bin", "obj"}


def path_filters(workflow_file: Path):
    """Every entry under any paths:/paths-ignore: block is returned with its kind."""
    text = workflow_file.read_text(encoding="utf-8")
    filters = []
    kind = None
    for raw in text.splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        if re.fullmatch(r"paths(-ignore)?:", line):
            kind = "ignore" if "ignore" in line else "paths"
            continue
        m = re.match(r"-\s*['\"]?([^'\"]+?)['\"]?\s*$", line)
        if kind and m and raw.startswith(("      ", "\t")):
            filters.append((kind, m.group(1)))
            continue
        # A non-list line ends the block.
        if kind and not raw.startswith("      "):
            kind = None
    return filters


def matches(pattern: str, candidate: str) -> bool:
    """GitHub path-filter matching for the subset this repo uses: literal prefixes, /** and /* globs."""
    if pattern == candidate:
        return True
    if pattern.endswith("/**"):
        prefix = pattern[:-3]
        return candidate == prefix or candidate.startswith(prefix + "/")
    if pattern.endswith("/*"):
        prefix = pattern[:-2]
        rest = candidate[len(prefix) + 1:] if candidate.startswith(prefix + "/") else None
        return rest is not None and "/" not in rest
    # Any other glob: treat * as a single segment wildcard, which is all this repo relies on.
    regex = "^" + re.escape(pattern).replace(r"\*", "[^/]+") + "$"
    return re.fullmatch(regex, candidate) is not None


def main() -> int:
    if not WORKFLOWS.is_dir():
        print(f"::error::No workflow directory at {WORKFLOWS}")
        return 2

    workflows = sorted(WORKFLOWS.glob("*.yml")) + sorted(WORKFLOWS.glob("*.yaml"))
    if not workflows:
        print(f"::error::No workflows found under {WORKFLOWS}")
        return 2

    filters = []
    for workflow in workflows:
        for kind, pattern in path_filters(workflow):
            filters.append((workflow.name, kind, pattern))

    covers = [pattern for name, kind, pattern in filters if kind == "paths"]
    if not covers:
        print("::error::No paths: filters found in any workflow, so this check cannot prove coverage.")
        return 2

    top_level = sorted(
        directory.name
        for directory in ROOT.iterdir()
        if directory.is_dir() and not directory.name.startswith(".") and directory.name not in IGNORED
    )

    uncovered = []
    for name in top_level:
        # A directory is covered when a file directly inside it would trigger some workflow.
        probe = f"{name}/probe"
        if not any(matches(pattern, probe) for pattern in covers):
            uncovered.append(name)

    if not uncovered:
        print(f"Every top-level directory is covered by a workflow path filter ({len(top_level)} checked).")
        return 0

    print("::error::These top-level directories are not matched by ANY workflow paths: filter:")
    for name in uncovered:
        print(f"  - {name}/")
    print("")
    print("Add the directory to the paths: filter of each workflow that must validate it, or add an")
    print("exemption here with a comment explaining why no workflow needs to run for it.")
    print("Uncovered now: " + ", ".join(uncovered))
    return 1


if __name__ == "__main__":
    sys.exit(main())
