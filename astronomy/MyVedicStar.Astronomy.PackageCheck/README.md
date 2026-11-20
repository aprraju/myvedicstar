# MyVedicStar.Astronomy.PackageCheck

AST-11 (api#76). A deliberately solution-external consumer of the packaged astronomy engine.

- **Not** in `MyVedicStar.Astronomy.slnx`, and it references no project in that solution. It only has
  `PackageReference`s, resolved from a folder feed of the .nupkg files under test.
- CI runs it twice: once on the normal runner, and once from a directory **outside the checkout** so an artefact that
  reads the build tree fails here instead of in a container.
- `--expect-no-toolkit` mode runs in the toolkit-free runtime image and asserts that the engine **refuses**
  rather than silently degrading when the native library is absent.

Run locally after packing:

```bash
dotnet pack astronomy/MyVedicStar.Astronomy.Jpl.Provider -c Release -o /tmp/ast-pkgs -p:PackageVersion=0.2.0
dotnet run --project astronomy/MyVedicStar.Astronomy.PackageCheck -c Release \
  -p:ProbeFeed=/tmp/ast-pkgs -p:ProbePackageVersion=0.2.0
```
