# CT-9 — Traversal and Argument Integrity Hardening

## Purpose

Harden the externally produced direct-call compatibility, cross-project traversal, and constant-argument candidate.
Retain its useful implementation while restoring exact positional meaning, safe rendering, and realistic profile and
project boundaries.

## Target paths

- `src/SeqDoc.Analysis.Roslyn/Behavior/RoslynBehaviorExtractor.cs`
- `src/SeqDoc.Analysis.Roslyn/Semantics/RoslynMinimalApiHandlerFactCollector.cs`
- `src/SeqDoc.Core/Behavior/BehaviorExtractionContracts.cs`
- `src/SeqDoc.Core/Behavior/MethodFlowContracts.cs`
- `src/SeqDoc.Analysis.Behavior/MethodFlowBuilder.cs`
- `src/SeqDoc.Core/ScenarioGraph/ScenarioGraphContracts.cs`
- `src/SeqDoc.Analysis.Scenarios/ScenarioGraphBuilder.cs`
- `src/SeqDoc.Application/Documentation/DocumentationPlanner.cs`
- `tests/SeqDoc.Behavior.Tests/**`
- `tests/SeqDoc.Analysis.Tests/**`
- `tests/SeqDoc.Scenarios.Tests/**`
- `tests/SeqDoc.Wording.Tests/**`
- `tests/SeqDoc.AcceptanceTests/**`
- `docs/work/credit-transfer/CT-9/**`

## Accepted design

1. Remove only the invalid equality between Program Index source-text body fingerprints and Method Flow behavior
   fingerprints. Preserve active profile and Program Index snapshot equality, exact `MethodId`, source body
   availability, complete `DirectExact` call-site identity, and exactly one target Method Flow.
2. Distinguish body unavailable, no flow, and ambiguous flow diagnostics. Never silently choose among flows.
3. Permit traversal across project identities only inside the same validated profile/snapshot when exact target source
   and one flow are available. Metadata-only, foreign-profile, unresolved, generated-boundary, cycle, guard, depth,
   and node limits remain explicit.
4. Preserve true compiler parameter ordinal for arguments, including named/reordered arguments. Argument labels are
   emitted only when every supplied argument can be mapped safely; otherwise use no argument summary rather than a
   misleading partial list.
5. Escape strings and control characters for renderer-neutral Diagram Plan text. Suppress sensitive-looking values
   and any unsupported expression. Constants, booleans, numerics, enums, strings, and nulls remain compiler-evidenced.
6. Argument identity/evidence remains deterministic and does not alter call identity or traversal.

## Non-goals

- No increased depth/node budget, DI/service correlation, framework-specific argument meaning, secret detection beyond
  conservative presentation suppression, or application vocabulary.

## Risks and coverage

Risks are stale/foreign flow traversal, ambiguous flow selection, cross-profile leakage, generated/metadata boundary
overreach, named-argument reordering, partial constants changing apparent position, and Mermaid injection. Reuse the
candidate traversal and argument tests plus CT-6 exact/cycle/budget coverage. Add at most nine distinct claims covering
the compatibility partitions, realistic two-project/profile isolation, complete/partial/named arguments, escaping,
sensitive suppression, and deterministic reversed input.

## Focused command

```powershell
dotnet test tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release --filter "FullyQualifiedName~InvocationArgumentExtraction" && dotnet test tests/SeqDoc.Behavior.Tests/SeqDoc.Behavior.Tests.csproj -c Release --filter "FullyQualifiedName~Argument|FullyQualifiedName~Invocation" && dotnet test tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release --filter "FullyQualifiedName~DirectExactTraversal|FullyQualifiedName~Argument" && dotnet test tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release --filter "FullyQualifiedName~Argument|FullyQualifiedName~NestedCall"
```

## Final gate

```powershell
dotnet test tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release && dotnet test tests/SeqDoc.Behavior.Tests/SeqDoc.Behavior.Tests.csproj -c Release && dotnet test tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release && dotnet test tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release && dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter "FullyQualifiedName!~ExternalCorpusTests"
```

The external-corpus class is excluded because its four configured repositories are not installed in this environment;
CreditTransfer regeneration is the required external acceptance lane for this checkpoint.
