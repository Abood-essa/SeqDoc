# Changelog

This changelog records the completed capabilities of SeqDoc by stable snapshot.

## Baseline

Initial public baseline: a static-analysis CLI that produces evidence-backed behavioral documentation.

### Compiler and analysis foundation

- Roslyn-based Program Index extraction: projects, documents, types, methods, and invocations.
- Per-method control flow, local value flow, call resolution, and return behavior.
- Compiler-evidenced predicate facts, exact dispatch facts, and Minimal API handler facts.
- Compilation profiles with separate target frameworks and deterministic fingerprints.
- Evidence-bearing core types with explicit certainty levels.

### Behavior and scenarios

- Method Flow construction with local value/control flow and call resolution.
- Scenario Graph construction over behavior and framework facts.
- Outcome derivation for success and failure paths, including HTTP status outcomes.
- Configuration reads and dependency-injection registrations captured as semantic facts.
- Callback boundaries captured with explicit flow edges and conservative uncertainty.
- Selected Minimal API entry points and MediatR dispatch projections, including exact handler calls.
- Bounded loop and nested-return behavior in admitted dispatch flows.

### Wording and rendering

- Typed user-facing wording phrases derived from scenario semantics.
- Diagram Plan with `alt`/`opt`/`break`/`loop` fragments and evidence references.
- Markdown and Mermaid rendering from the diagram plan.
- Deterministic, file-local identity and stable fingerprints.

### Framework interpretation

- ASP.NET Core controller eligibility, HTTP entry points, route bindings, and exact outcome helpers.
- Selected ASP.NET Core Minimal API eligibility and route handling.
- Selected MediatR request-to-handler projection with exact-call requirements.
- Entity Framework Core queries, mutations, and saves.
- Composition of dependency-injection alternatives into exact conditional arms.

### Configuration and persistence

- Configuration fact extraction with fail-closed validation.
- SQLite persistence with staged activation and previous-valid-state preservation.
- Deterministic regenerated evidence for acceptance behavior.

### Command-line surface

- Analysis, rendering, and verification commands with consistent exit behavior.
- Query options over produced documentation.

## Current limits

Coverage remains selected-flow coverage. Generic configurable roots, bounded cross-project exact-call
traversal, broader standard Microsoft DI, and a broad application corpus are planned next. Unsupported
or ambiguous constructs are retained as limitations rather than synthesized behavior.
