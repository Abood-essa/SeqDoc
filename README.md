# SeqDoc

SeqDoc is a .NET static-analysis command-line tool that turns Roslyn compiler facts into
evidence-backed behavioral documentation. It analyzes source code, builds typed intermediate
representations, and renders deterministic Markdown and Mermaid for the behaviors it can prove.

## Highlights

- **Evidence-backed output.** Every documented fact carries its evidence and an explicit certainty
  level. When compiler evidence is incomplete, SeqDoc says so instead of inventing behavior.
- **A typed pipeline.** Program Index, Method Flow, Scenario Graph, and Diagram Plan are separate,
  validated stages with stable ordering and fingerprints.
- **Deterministic.** The same source produces the same documentation independent of schedule or
  checkout location.
- **Framework interpretation.** ASP.NET Core controllers, Minimal APIs, MediatR dispatch, and Entity
  Framework Core data access are interpreted into behavior-level graphs, including HTTP outcomes and
  guarded data interactions.
- **Current coverage.** The strongest supported flows include selected Minimal API and MediatR shapes,
  exact handler calls, bounded loops, and nested returns. Unsupported roots, call shapes, and framework
  conventions remain explicit limitations rather than guessed behavior.
- **Configuration and composition.** Configuration reads, dependency-injection registrations, and
  callback boundaries are derived from compiler evidence with conservative uncertainty.
- **Safe persistence.** Analysis state is stored in SQLite with activation that preserves the previous
  valid state when a new analysis fails.
- **Readable output.** Documentation is rendered as Markdown with Mermaid diagrams from an explicit
  diagram plan, using typed user-facing wording.

## Quick start

```powershell
dotnet restore SeqDoc.slnx
dotnet build SeqDoc.slnx -c Release
dotnet test SeqDoc.slnx -c Release
```

Run the CLI against a project or solution:

```powershell
dotnet run --project src/SeqDoc.Cli -- analyze <target>
```

See [Using SeqDoc](docs/usage.md) for complete arguments, exact-root cataloging, troubleshooting, external test-project
setup, and the reproducible CreditTransfer example.

## Layout

- `src/` – production projects for analysis, application, CLI, persistence, and rendering.
- `tests/` – unit and component tests, compiler integration tests, and compiler fixtures.
- `tools/` – deterministic repository tooling and test hosts.
- `docs/` – architecture, decisions, changelog, contributing, and roadmap.

## Documentation

- [Architecture](docs/architecture.md)
- [Decisions](docs/decisions.md)
- [Changelog](docs/changelog.md)
- [Contributing](docs/contributing.md)
- [Using SeqDoc](docs/usage.md)
- [Roadmap](docs/roadmap.md)
- [Parallel workstreams](docs/project/parallel-workstreams.md)

## License

Copyright (c) 2026 Bilaltariq41.

SeqDoc is licensed under the [Mozilla Public License 2.0](LICENSE). Modifications to MPL-covered
files that are distributed must remain available in source form under the MPL. The license does not
require private, undistributed modifications to be published.

SeqDoc does not claim ownership of source repositories it analyzes. Rights in generated
documentation depend on the analyzed source, the generated content, and applicable law.
