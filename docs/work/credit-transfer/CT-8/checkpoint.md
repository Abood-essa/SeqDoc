# CT-8 — Presentation Integrity Hardening

## Purpose

Harden the externally produced presentation candidate without rewriting its usable implementation. Produce concise,
non-orphan participants and generic auditable exclusions, and remove placeholder control labels without inventing
behavior.

## Target paths

- `src/SeqDoc.Application/Documentation/DocumentationPlanner.cs`
- `src/SeqDoc.Configuration/ConfigurationResolution.cs`
- `src/SeqDoc.Configuration/YamlConfigurationDocument.cs`
- `src/SeqDoc.Configuration/YamlConfigurationResolver.cs`
- `src/SeqDoc.Cli/CliHost.cs`
- `src/SeqDoc.Rendering.Markdown/MermaidRenderer.cs`
- `src/SeqDoc.Rendering.Markdown/MermaidValidator.cs`
- `tests/SeqDoc.Configuration.Tests/**`
- `tests/SeqDoc.Wording.Tests/**`
- `tests/SeqDoc.Rendering.Tests/**`
- `tests/SeqDoc.Cli.Tests/**`
- `docs/work/credit-transfer/CT-8/**`

## Accepted design

1. Keep configured-root caller removal and exact self-call reuse, but never overwrite concise deterministic labels with
   canonical namespace-heavy identities.
2. Add dedicated `documentation.excludeParticipants` and `documentation.excludeCalls`; do not repurpose flow
   `selection.exclude`. Match exact case-sensitive canonical type/member identities, with only explicit `Type.*` call
   wildcard support.
3. Resolve exclusions before participant creation. Remove excluded interactions, presentation phrases, empty fragments,
   and participants with no surviving interaction. Preserve Scenario Graph facts and expose one deterministic filtered
   interaction count in technical fallback.
4. Built-in exact logging recognition uses the same filtering pipeline. Custom wrappers require configuration; no
   application vocabulary enters production matching.
5. Structural root/client participants cannot be excluded. Invalid attempts fail configuration rather than producing
   a meaningless graph.
6. Never emit generic `Condition`, `Continue`, `Continue evaluating condition`, or `Path terminates`. Use an exact
   predicate/return/throw/outcome or withhold the presentation element with technical fallback.
7. Configured roots never create outcome messages to an absent caller participant.

## Non-goals

- No argument extraction changes, traversal compatibility changes, deeper traversal, line wrapping, business naming,
  or application-specific logging rule.

## Risks

- Filtering after participant collection leaves orphan participants.
- Filtering guarded calls leaves empty or malformed fragments.
- Short-name collisions merge distinct types.
- Exclusions alter analysis facts or hide the root flow.
- Placeholder removal accidentally emits guarded messages unconditionally.

## Existing coverage and soft budget

Reuse candidate configuration/planner tests and existing fragment/rendering tests. Add at most eight distinct claims:
concise collision-safe labels; root/self reuse; exact participant and call exclusions; no orphan/empty fragments;
auditable count; invalid structural exclusion; configured outcome safety; and no placeholder tokens.

## Focused command

```powershell
dotnet test tests/SeqDoc.Configuration.Tests/SeqDoc.Configuration.Tests.csproj -c Release && dotnet test tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release && dotnet test tests/SeqDoc.Rendering.Tests/SeqDoc.Rendering.Tests.csproj -c Release
```

## Final gate

```powershell
dotnet test tests/SeqDoc.Configuration.Tests/SeqDoc.Configuration.Tests.csproj -c Release && dotnet test tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release && dotnet test tests/SeqDoc.Rendering.Tests/SeqDoc.Rendering.Tests.csproj -c Release && dotnet test tests/SeqDoc.Cli.Tests/SeqDoc.Cli.Tests.csproj -c Release
```
