#!/usr/bin/env python3
"""Fail when a top-level directory is not covered by any workflow path filter.

Why this exists (api#90): path filters are hand-maintained. When a new top-level directory
appears (astronomy/ was the last one), nothing forced anyone to ask which workflows must
cover it. A change under src/MyVedicStar.Infrastructure.DependencyInjection once added a
using for a type in another assembly WITHOUT the matching ProjectReference; Day/Star CI did
not cover that path, so an uncompilable commit reached main.

The rule is deliberately fail-closed: a directory is covered only if at least one workflow's
paths: filter would actually match a file inside it. "Somebody remembered" is not coverage.

Depth (api#125). A directory is covered only if the filter matches a file at EVERY depth, not just
depth 1. The previous version probed a single synthetic path "<dir>/probe", which proves only depth 1:
a filter of "astronomy/*" matches "astronomy/probe" but NOT "astronomy/a/b/probe.md", so a nested
change would never trigger that workflow while this checker reported the directory covered. Depth 1
is also the depth at which a directory's files need coverage LEAST - a real change almost always
lands at depth 2 or deeper. Both a cheap fixed depth and an arbitrary depth are probed, so a filter
that covers depth 1 and 2 but not 5 is still caught.

Exit: 0 covered, 1 uncovered directories found, 2 the checker itself could not run.
"""
import os
import re
import sys
from pathlib import Path

# The repository root. COVERAGE_CHECKER_ROOT overrides it so the tests can run this exact script against a
# scratch repository - the same artefact CI runs, not a copy of its logic.
ROOT = Path(os.environ.get("COVERAGE_CHECKER_ROOT") or Path(__file__).resolve().parents[2]).resolve()
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
    """Path-filter matching, per GitHub's documented glob rules.

    '*' matches any characters except '/'. '**' matches any characters INCLUDING '/', so it is the
    only form that can span directories. '?' and '+' match zero-or-one and one-or-more of the
    PRECEDING character, not of any character - a distinction this function gets wrong is exactly the
    kind of plausible-looking approximation that makes a check lie (api#125).
    """
    if pattern == candidate:
        return True
    if pattern.endswith("/**"):
        # Documented: 'docs/**' covers every file under docs/, including the directory itself.
        prefix = pattern[:-3]
        return candidate == prefix or candidate.startswith(prefix + "/")
    if pattern.endswith("/*"):
        prefix = pattern[:-2]
        rest = candidate[len(prefix) + 1:] if candidate.startswith(prefix + "/") else None
        return rest is not None and "/" not in rest
    return re.fullmatch(_glob_to_regex(pattern), candidate) is not None


def _glob_to_regex(pattern: str) -> str:
    """Translate GitHub's glob dialect into a whole-path regex. Anchored: patterns match the whole path."""
    out = ["^"]
    i = 0
    while i < len(pattern):
        ch = pattern[i]
        if ch == "*":
            if pattern.startswith("**", i):
                out.append(".*")
                i += 2
                continue
            out.append("[^/]*")
        elif ch == "?":
            # Zero or one of the preceding character: 'jsx?' matches 'js' and 'jsx'.
            out.append("?")
        elif ch == "+":
            out.append("+")
        elif ch == "[":
            end = pattern.find("]", i + 1)
            if end == -1:
                out.append(re.escape(ch))
            else:
                out.append(pattern[i:end + 1])
                i = end + 1
                continue
        else:
            out.append(re.escape(ch))
        i += 1
    out.append("$")
    return "".join(out)


# Depths probed inside a top-level directory, measured from the top-level directory itself.
# 1 keeps the original depth-1 question; 2 is the shallowest depth a single-star filter fails at;
# the arbitrary DEPTH_LIMIT catches a filter that covers shallow depths but not deep ones.
PROBE_DEPTHS = (1, 2, 5)
DEPTH_LIMIT = 12


def probe_path(name: str, depth: int) -> str:
    """A synthetic file at the given depth below a top-level directory: depth 1 is 'name/probe'.

    The suffix is present at EVERY depth on purpose. Without it, a legitimate suffix filter such as
    'docs/**.md' looks like it misses depth 1 ('docs/probe' has no .md), and the checker would report
    a gap that does not exist. The probe must be a plausible file at each depth, not a bare name.
    """
    return "/".join([name] + ["probe"] * depth) + ".md"


def first_uncovered_depth(name: str, covers) -> int | None:
    """Lowest depth at which no paths: filter matches a file inside the directory, or None if covered."""
    for depth in PROBE_DEPTHS:
        probe = probe_path(name, depth)
        if not any(matches(pattern, probe) for pattern in covers):
            return depth

    # Fixed depths can be satisfied by a filter tuned to them, so also prove there is no abrupt cliff:
    # if the deepest probe matches via a /** style filter it holds for all deeper files, but a filter
    # relying on a limited number of single-segment wildcards would match depth 5 and stop soon after.
    for depth in range(max(PROBE_DEPTHS) + 1, DEPTH_LIMIT + 1):
        probe = probe_path(name, depth)
        if not any(matches(pattern, probe) for pattern in covers):
            return depth
    return None


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
    # It is legitimate for a repository to have workflows and no paths: filters yet. That is not "nothing to
    # prove" - it is the strongest possible finding: NO directory is covered, so every one of them is listed and
    # the run is red. Only the total absence of workflows is an error (see above).
    if not covers:
        print("::warning::No paths: filters were found in any workflow; every top-level directory is uncovered.")

    top_level = sorted(
        directory.name
        for directory in ROOT.iterdir()
        if directory.is_dir() and not directory.name.startswith(".") and directory.name not in IGNORED
    )

    if not top_level:
        # A scan that found nothing scanned nothing. Reading green here would be a guard that passes by not
        # running, which is the exact failure mode this file exists to remove.
        print(f"::error::No top-level directories found under {ROOT}, so this check proved nothing.")
        return 2

    uncovered = []
    for name in top_level:
        # A directory is covered only when a file at any depth inside it would trigger some workflow.
        # Probing depth 1 alone is what let a single-star filter pass as coverage (api#125).
        gap = first_uncovered_depth(name, covers)
        if gap is not None:
            uncovered.append((name, gap))

    if not uncovered:
        print(f"Every top-level directory is covered by a workflow path filter at every depth "
              f"({len(top_level)} checked, depths {', '.join(str(d) for d in PROBE_DEPTHS)}).")
        return 0

    print("::error::These top-level directories are not matched by ANY workflow paths: filter:")
    for name, depth in uncovered:
        print(f"  - {name}/ (uncovered at depth {depth})")
    print("")
    print("Add the directory to the paths: filter of each workflow that must validate it, or add an")
    print("exemption here with a comment explaining why no workflow needs to run for it.")
    print("Uncovered now: " + ", ".join(name for name, _ in uncovered))
    return 1


if __name__ == "__main__":
    sys.exit(main())
