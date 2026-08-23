# Issue 22 — Guarded Nested Callee Topology

## Purpose

Compose exact decision, terminal, and rejoin topology for each admitted direct-call occurrence so locally guarded deeper
calls render once inside the correct nested fragment. Preserve root guards inherited across traversal, and withhold any
placement that Method Flow cannot prove instead of leaving guarded work unconditional.

## Target paths

- `src/SeqDoc.Analysis.Scenarios/ScenarioGraphBuilder.cs`
- `src/SeqDoc.Core/ScenarioGraph/ScenarioGraphContracts.cs`
- `src/SeqDoc.Core/Identity/IdentityDescriptors.cs`
- `src/SeqDoc.Core/Identity/StableIdentity.cs`
- `src/SeqDoc.Application/Documentation/DocumentationPlanner.cs`
- `tests/SeqDoc.Core.Tests/StableIdentityTests.cs`
- `tests/SeqDoc.Scenarios.Tests/DirectExactTraversalFixture.cs`
- `tests/SeqDoc.Scenarios.Tests/DirectExactTraversalTests.cs`
- `tests/SeqDoc.Scenarios.Tests/ScenarioTestFactory.cs`
- `tests/SeqDoc.Scenarios.Tests/ScenarioTopologyTests.cs`
- `tests/SeqDoc.Wording.Tests/FragmentScenarioTestFactory.cs`
- `tests/SeqDoc.Wording.Tests/FragmentPlannerTests.cs`
- narrowly focused existing Rendering fragment/validator tests when needed for the real emitted shape
- `docs/work/traversal/I22/**`

Changing another path requires Orchestrator approval and evidence that the accepted boundary cannot be implemented in
the listed owner.

## Accepted design

1. Reuse Program Index, Method Flow, call graph, predicate wording, Scenario topology, and Diagram Plan fragments. No
   Roslyn or source-text scan is added outside the compiler pipeline.
2. Remove the blanket `SC-DIRECT-GUARDED` stop. A complete admitted expansion occurrence may compose the unique loaded
   target Method Flow's ordinary decision topology and exact child-call operation anchors.
3. Scope callee decisions to the direct-call occurrence, not only the target method. Repeated and diamond-shaped calls
   to the same method receive distinct occurrence topology and render in their own chronological call-tree positions.
   Root topology identities and all existing unguarded traversal identities remain unchanged.
4. A child call controlled by one or more exact decisions receives every proven local arm membership plus inherited
   root/caller memberships. Nested decisions retain exact polarity. The call appears exactly once in the deepest valid
   fragment and never also appears flat or under a sibling occurrence.
5. Preserve existing terminal/rejoin classification and predicate wording gates. Exact represented return, throw,
   rethrow, and normal rejoin shapes may produce existing `break`, `alt`, and `opt` plans. A terminating arm contains no
   continuation from that callee occurrence.
6. Loop-back, switch, catch, filter, finally, irreducible, mixed terminal/rejoin, duplicate-anchor, missing predicate,
   and ambiguous topology remain fail-closed through existing `SC011`/`SC012`/`SC013` and `DP002`/`DP005` boundaries.
   This checkpoint does not broaden those shapes or suppress diagnostics.
7. Occurrence-scoped identity is derived only from profile, configured root, direct-call occurrence, containing method,
   controlling flow node, and semantic polarity. Labels, source text, checkout paths, budgets, input enumeration, and
   visual order never participate.
8. Evidence is the canonical union of the expansion call, decision/arm, control dependence, traversed edge, and terminal
   boundary contributors used by the claim. Certainty is the least confident contributor at every join.
9. Canonical DFS call chronology remains unchanged. Reversed Method Flow/call graph input produces identical Scenario
   debug projection, Diagram Plan sequence, diagnostics, and Mermaid.
10. Cycles, work/output budgets, unavailable source/body/flow, profile isolation, argument safety, cancellation, and
    previous-valid-state behavior remain unchanged.

## Non-goals

- No new loop or switch fragments; catch/filter/finally or exception-flow composition; async/concurrency/runtime
  ordering; inter-root correlation; DI admission; framework-specific semantics; persistence behavior; external-project
  coverage measurement (#21); automatic decomposition (#23); fragment-depth policy change; or project vocabulary.

## Risk inventory

1. A locally guarded child remains unconditional, is omitted despite exact support, or appears twice.
2. True/false polarity, nested parentage, inherited root guard, or source chronology is reversed.
3. A repeated shared callee reuses one topology identity and combines unrelated call occurrences.
4. A terminal arm leaks continuation calls after return/throw, or a normal rejoin is rendered as termination.
5. Unsupported loop/switch/exception/mixed topology is accidentally admitted or its boundary diagnostic disappears.
6. Occurrence identity depends on traversal order or changes existing root/unguarded identities.
7. Membership evidence omits the call, dependence, decision, traversed edge, or terminal boundary; certainty strengthens.
8. Planner nesting loses or duplicates message references, exceeds fragment depth silently, or emits invalid Mermaid.
9. Depthless traversal cycles/budgets and repeated-callee accounting regress while guarded children become admissible.
10. PR #26/#27 overlap in Scenario/Documentation files obscures this issue's isolated diff; the local candidate must be
    rebased after contributor merge decisions and reverified before publication.

## Existing coverage and soft budget

Scenario topology tests already pin exact polarity, nested root decisions, returns/throws/rejoins, weakest certainty,
duplicate anchors, loop/switch/exception/mixed fail-closed behavior, and stable debug order. Direct traversal tests pin
DFS chronology, repeated shared callees, cycles, budgets, root-arm inheritance, and the current guarded stop. Fragment
planner and renderer tests pin `alt`/`opt`/`break`, nested membership, one-time reference coverage, predicate wording,
depth fallback, and Mermaid validation.

Add approximately 10–15 distinct claims: one guarded callee; nested local decisions; inherited root plus local guard;
terminal and rejoin placement; repeated occurrence scope; reversed-input determinism; weakest evidence/certainty;
unsupported loop/switch/exception equivalence partitions; no duplicate/flat references; and one rendered Mermaid
acceptance shape. Replace the obsolete guarded-stop assertion. Do not duplicate existing root-only topology or generic
renderer contract tests.

## Focused command

```powershell
dotnet build tests/SeqDoc.Core.Tests/SeqDoc.Core.Tests.csproj -c Release && dotnet build tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release && dotnet build tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release && dotnet build tests/SeqDoc.Rendering.Tests/SeqDoc.Rendering.Tests.csproj -c Release && dotnet test tests/SeqDoc.Core.Tests/SeqDoc.Core.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~Identity" && dotnet test tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~DirectExactTraversal|FullyQualifiedName~ScenarioTopology" && dotnet test tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~FragmentPlanner" && dotnet test tests/SeqDoc.Rendering.Tests/SeqDoc.Rendering.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~Fragment|FullyQualifiedName~MermaidValidator"
```

## Final gate

```powershell
dotnet test tests/SeqDoc.Core.Tests/SeqDoc.Core.Tests.csproj -c Release --no-build --no-restore && dotnet test tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release --no-build --no-restore && dotnet test tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release --no-build --no-restore && dotnet test tests/SeqDoc.Rendering.Tests/SeqDoc.Rendering.Tests.csproj -c Release --no-build --no-restore
```

After the final gate, render the focused generated nested-fragment Mermaid with Mermaid CLI. External supplied-project
coverage and metrics remain #21; this checkpoint needs only syntax/layout proof for its accepted topology shape.
