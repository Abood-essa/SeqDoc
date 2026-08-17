# Contributing

Thank you for considering a contribution to SeqDoc. This page describes how to build, test, and
submit focused changes.

## Build and test

```powershell
dotnet restore SeqDoc.slnx
dotnet build SeqDoc.slnx -c Release
dotnet test SeqDoc.slnx -c Release
```

The repository requires the .NET SDK declared in `global.json`. All warnings are treated as errors.

## Code style

- Follow `.editorconfig`; formatting and analyzer rules are enforced during build.
- Use file-scoped namespaces, explicit stable ordering, and immutable records where appropriate.
- Comments should explain intent, invariants, compatibility constraints, or non-obvious failure
  protection.

## Tests and fixtures

- Unit and component tests run through the solution. Compiler and CLI process integration tests live
  under `tests/` and run separately when changing those surfaces.
- Compiler fixtures live under `tests/fixtures/` and are referenced by relative path from tests.
- Semantic changes should add regression tests with realistic fixtures rather than relying on
  implementation details.
- Acceptance assertions should target observable wording, structure, and determinism.

## Submitting changes

1. Fork the public repository.
2. Create a focused branch from `main` in your fork.
3. Make focused changes with tests.
4. Run the build and relevant test commands above.
5. Open a pull request against SeqDoc's `main` branch describing the problem, change, and
   verification performed.

By submitting a contribution, you represent that you have the right to submit it and agree that it
is licensed under the [Mozilla Public License 2.0](../LICENSE), the same license as the project.

## Review process

- Changes are reviewed for correctness, evidence fidelity, and determinism.
- Behavior changes must be backed by tests and, where relevant, documentation updates.
- Keep unrelated refactoring out of a single pull request.
- Direct pushes to `main` are restricted; all external contributions use pull requests.
