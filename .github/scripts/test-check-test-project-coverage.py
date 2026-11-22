#!/usr/bin/env python3
"""Prove check-test-project-coverage.py can fail (api#172, acceptance 6).

A checker that has never been seen to fail is not evidence. This builds throwaway repository
shapes, runs the real script against each, and asserts the exit code AND that the failure names
the offending project - a red that does not say which project is uncovered would send the reader
back to reading the script.

Each case is a complete miniature repository: .github/workflows/, tests/, and a solution. The
script resolves ROOT from its own location, so it is copied into each fixture and run from there.

Exit: 0 if every case behaves as asserted, 1 if any case does not, 2 if the fixture cannot be built.
"""
import shutil
import subprocess
import sys
import textwrap
from pathlib import Path

HERE = Path(__file__).resolve().parent
CHECKER = HERE / "check-test-project-coverage.py"

# Both templates use __PROJECTS__/__SOLUTION__ tokens rather than str.format: solution files are
# full of GUID braces, and .format reads those as replacement fields.
SLNX = """<Solution>
  <Folder Name="/tests/">
__PROJECTS__
  </Folder>
</Solution>
"""

SLN = """Microsoft Visual Studio Solution File, Format Version 12.00
Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "tests", "tests", "{0AB3BF05-4346-4AA6-1389-037BE0695223}"
EndProject
__PROJECTS__
Global
EndGlobal
"""

BACKSLASH = chr(92)
# Substituted with .replace rather than .format: solution files are full of GUID braces.
SLN_PROJECT = (
    'Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "__NAME__", '
    '"tests' + BACKSLASH + '__NAME__' + BACKSLASH + '__NAME__.csproj", '
    '"{D77EBD77-E1E5-4D96-8ADD-454615BA1430}"' + chr(10) + 'EndProject'
)

WORKFLOW = """name: fixture
on: pull_request
jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - run: dotnet restore __SOLUTION__
      - run: dotnet test __SOLUTION__ --configuration Release
"""

CSPROJ = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
</Project>
"""


def build_fixture(root: Path, solution_name: str, solution_text: str, project_names, workflow_solution=None):
    tests = root / "tests"
    tests.mkdir(parents=True)
    for name in project_names:
        project_dir = tests / name
        project_dir.mkdir()
        (project_dir / f"{name}.csproj").write_text(CSPROJ, encoding="utf-8")

    (root / solution_name).write_text(solution_text, encoding="utf-8")

    workflows = root / ".github" / "workflows"
    workflows.mkdir(parents=True)
    (workflows / "fixture.yml").write_text(
        WORKFLOW.replace("__SOLUTION__", workflow_solution or solution_name), encoding="utf-8"
    )

    scripts = root / ".github" / "scripts"
    scripts.mkdir(parents=True)
    shutil.copy2(CHECKER, scripts / CHECKER.name)
    return scripts / CHECKER.name


def run(checker: Path):
    result = subprocess.run(
        [sys.executable, str(checker)], capture_output=True, text=True, cwd=checker.parents[2]
    )
    return result.returncode, (result.stdout or "") + (result.stderr or "")


CASES = []


def case(name, expected_exit, must_mention=None):
    def register(builder):
        CASES.append((name, expected_exit, must_mention, builder))
        return builder

    return register


@case("slnx: every test project is in the solution -> covered", 0)
def _(root: Path):
    names = ["Alpha.Tests", "Beta.Tests"]
    return build_fixture(
        root,
        "All.slnx",
        SLNX.replace('__PROJECTS__', "\n".join(f'    <Project Path="tests/{n}/{n}.csproj" />' for n in names)),
        names,
    )


@case("slnx: a project missing from the solution -> uncovered, named", 1, "Gamma.Tests")
def _(root: Path):
    names = ["Alpha.Tests", "Beta.Tests", "Gamma.Tests"]
    return build_fixture(
        root,
        "All.slnx",
        SLNX.replace('__PROJECTS__', "\n".join(f'    <Project Path="tests/{n}/{n}.csproj" />' for n in names[:2])),
        names,
    )


@case("sln: solution folder is not read as a project, missing project named", 1, "Gamma.Tests")
def _(root: Path):
    names = ["Alpha.Tests", "Beta.Tests", "Gamma.Tests"]
    projects = "\n".join(SLN_PROJECT.replace("__NAME__", n) for n in names[:2])
    return build_fixture(root, "All.sln", SLN.replace('__PROJECTS__', projects), names)


@case("workflow names the .csproj directly -> covered", 0)
def _(root: Path):
    names = ["Alpha.Tests"]
    return build_fixture(
        root,
        "All.slnx",
        SLNX.replace('__PROJECTS__', ""),
        names,
        workflow_solution="tests/Alpha.Tests/Alpha.Tests.csproj",
    )


@case("workflow names nothing resolvable -> cannot run (2), not uncovered (1)", 2)
def _(root: Path):
    names = ["Alpha.Tests"]
    return build_fixture(root, "All.slnx", SLNX.replace('__PROJECTS__', ""), names, workflow_solution="./missing.slnx")


def main() -> int:
    failures = 0
    for name, expected_exit, must_mention, builder in CASES:
        base = Path("/tmp") / "api172-selfcheck" / name.split(":")[0].replace(" ", "-")
        if base.exists():
            shutil.rmtree(base)
        base.mkdir(parents=True)
        try:
            checker = builder(base)
        except OSError as error:
            print(f"::error::cannot build fixture for '{name}': {error}")
            return 2

        exit_code, output = run(checker)
        ok = exit_code == expected_exit and (must_mention is None or must_mention in output)
        status = "ok  " if ok else "FAIL"
        print(f"  {status} {name} (exit {exit_code}, expected {expected_exit})")
        if not ok:
            failures += 1
            print(textwrap.indent(output.strip() or "<no output>", "       "))

    if failures:
        print(f"::error::{failures} of {len(CASES)} self-check case(s) did not behave as asserted.")
        return 1

    print(f"Checker behaves as asserted in all {len(CASES)} cases.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
