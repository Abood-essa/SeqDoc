# CT-3 — Root Direct-Call Presentation

## Purpose

Make admitted entry diagrams useful before interprocedural traversal by projecting compiler-proven direct calls from
the entry root's own Method Flow into Scenario Graph and Diagram Plan. Preserve exact decision-arm membership so a
conditional call is never presented as unconditional. This should move the two CreditTransfer Web diagrams beyond
the sparse controller boundary while `SC001` continues to disclose unresolved service composition.

## Target paths

- `src/SeqDoc.Core/ScenarioGraph/ScenarioGraphContracts.cs`
- `src/SeqDoc.Analysis.Scenarios/ScenarioGraphBuilder.cs`
- `src/SeqDoc.Application/Documentation/DocumentationPlanner.cs`
- `tests/SeqDoc.Scenarios.Tests/**`
- `tests/SeqDoc.Wording.Tests/**`
- `docs/work/credit-transfer/CT-3/**`

## Accepted design

1. Add one generic Scenario node kind for an exact direct method call and typed presentation fields for the target
   containing type and member name. It must not imply DI, HTTP, service, business, or framework meaning.
2. When an admitted entry root cannot join to a service, inspect only the exact `MethodFlowSnapshot` whose method is
   `ScenarioGraph.RootMethod`.
3. Admit one call node per canonical invocation operation only when it has a compiler-resolved target, exact
   certainty, non-empty evidence, complete typed target names, a source-backed call site, and is neither platform nor
   nested-function work. Order by block ordinal, evaluation ordinal, then stable node identity.
4. Keep `SC001`. Build existing `ScenarioTopology` over the root flow and the new call nodes so exact control
   dependences place calls under branches; unsupported switch/exception/loop topology remains visibly degraded.
5. Plan one deterministic participant per target containing type and one evidence-backed action-to-target call
   message labelled with the exact member name. Reuse existing topology-driven branch fragments. Renderers remain
   mechanical and unchanged.

## Non-goals

- No configurable-root input, nested/interprocedural traversal, recursion, RTA, DI resolution, or cross-project body
  loading.
- No loop fragment, return/outcome, exception/catch, switch-case, payload, status, serialization, or business wording.
- No platform, unresolved, ambiguous, dynamic, delegate/event, constructor, or nested-function call presentation.
- No removal or suppression of `SC001`, project-specific matching, external source edit, or renderer inference.

## Risk inventory

1. A branch-controlled transfer/notification call appears unconditional because topology membership is absent.
2. Platform, ambiguous, nested, dynamic, constructor, or unresolved calls leak into user-facing diagrams.
3. Duplicate operation/await anchors produce duplicate call nodes or conflicting memberships.
4. Source ordering depends on hash/dictionary/input iteration rather than compiler ordinals.
5. Generic calls are mislabeled as services or imply that the callee body was traversed.
6. Existing DI-resolved, Minimal API, MediatR, predicate, and rendering paths regress.
7. Unsupported root flow topology suppresses exact independent calls instead of retaining them with diagnostics.

## Existing relevant coverage

- Method Flow carries exact invocation target identity, source/platform/nested flags, compiler ordinals, evidence, and
  certainty.
- Existing Scenario topology maps material operation anchors to exact decision arms and fails closed on conflicts.
- D0-2 selected-handler expansion proves source-order and loop/call presentation, but its contract is dispatch-specific
  and is not reused as generic root semantics.
- CT-2 generates two exact but sparse CreditTransfer Web diagrams ending at `SC001`.

## Soft test budget

At most ten distinct claims across Scenario and planner tests: exact admission/order, branch membership, deterministic
identity, duplicate suppression, material negatives, retained `SC001`, generic labels/participants/messages, and no
regression to existing service/dispatch paths. Consolidate negative flags into equivalence partitions.

## Focused verification command

```powershell
dotnet test SeqDoc.slnx -c Release --filter "FullyQualifiedName~RootDirectCall|FullyQualifiedName~ScenarioTopology|FullyQualifiedName~DocumentationPlanner"
```

## Final gate

```powershell
dotnet test tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release && dotnet test tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release
```

After the final gate, rebuild the CLI and regenerate the disposable CreditTransfer Web output for manual semantic and
visual inspection. Generated output remains external evidence and is never committed.
