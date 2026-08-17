# CT-6 — Bounded DirectExact Call Traversal

## Purpose

Expand configured method roots through a small deterministic prefix of compiler-proven same-project source calls.
CT-5 can expose any exact method but presents only its local calls; CT-6 follows those calls far enough to reveal
orchestration structure without introducing a general call-graph IR, DI guessing, or cross-project composition.

## Target paths

- `src/SeqDoc.Core/Identity/IdentityDescriptors.cs`
- `src/SeqDoc.Core/Identity/StableIdentity.cs`
- `src/SeqDoc.Core/ScenarioGraph/ScenarioGraphContracts.cs`
- `src/SeqDoc.Analysis.Scenarios/ScenarioGraphBuilder.cs`
- `src/SeqDoc.Application/Documentation/DocumentationPlanner.cs`
- `tests/SeqDoc.Scenarios.Tests/**`
- `tests/SeqDoc.Wording.Tests/**`
- `docs/work/credit-transfer/CT-6/**`

## Accepted design

1. Apply traversal only to typed `ConfiguredMethod` roots. Existing HTTP, Minimal API, dispatch, and configured-root
   local output remain unchanged when no traversable child body exists.
2. Add one generic immutable `ScenarioDirectCallExpansion` contract to Scenario Graph. Each step records stable step
   identity, parent step identity, depth, caller/target exact `MethodId`, invocation `OperationId`, projected
   `ScenarioNodeId`, source ordinal, evidence, and certainty. It is a bounded projection, not a new general call graph.
3. Use fixed first-version budgets: maximum call depth 2 (root-local calls are depth 1) and maximum 64 projected call
   nodes per configured graph. Configuration of budgets is later work.
4. Admit every step only through the complete CT-3 predicate: one canonical source-backed invocation, exact target and
   evidence, one complete `DirectExact` CallSite candidate, non-platform, non-dynamic, non-delegate/event,
   non-constructor, and outside nested functions.
5. Expand a target body only when the exact target `ProgramMethod` has a body fingerprint, one matching Method Flow
   exists in the active profile/fingerprint, and caller/target belong to the same Program Index project. Keep the
   exact call node visible when expansion stops at an unavailable, cross-project, depth, cycle, or budget boundary.
6. Use path/call-site identity, not target-method identity, so two source invocations of the same callee remain two
   chronological expansions. Maintain an active method path; a recursive/cyclic call is visible once at its call site
   but is never re-entered.
7. Order depth-first by parent call chronology. Within each method use block ordinal, evaluation ordinal, then stable
   invocation identity. Parent messages precede a contiguous child expansion, independent of input order or scheduling.
8. Inherit every exact caller-side Scenario arm membership from a parent call node to its descendant call nodes. This
   proves that descendants execute only when the parent invocation occurs.
9. Admit a nested call only when it has no local control-dependence membership in the callee Method Flow. Locally
   guarded nested calls are withheld with an explicit diagnostic until a later checkpoint adds per-method nested
   topology composition; they must never fall back to unconditional messages.
10. Materialize existing `MethodCall` nodes and `Call` edges. A nested edge connects the parent call node to its child
    call node. Planner source participants derive from the parent call's exact target containing type; root calls still
    originate at the selected method participant. No callee returns, assignments, queries, outcomes, or prose are
    invented.
11. Emit deterministic graph diagnostics for cycle, body-unavailable, cross-project, depth, node-budget, and guarded
    nested boundaries. The expansion has `IsComplete = false` whenever an otherwise exact source call cannot be
    expanded or presented; already proven prefix steps remain visible.

## Non-goals

- No YAML/CLI traversal budgets, arbitrary depth, whole call graph, RTA/CHA, virtual dispatch, DI resolution, or
  generated-client/service correlation.
- No cross-project body traversal; compatible project-reference traversal is the next checkpoint.
- No local callee branch fragment, loop, switch, catch/finally, return, throw, query, mutation, state, or outcome
  composition.
- No shared-callee global memoization that changes call-site chronology and no collapsing distinct invocations.
- No change to Program Index, Method Flow, Call Graph, persistence, renderer, or external projects.
- No project-specific names, signatures, routes, or business rules.

## Risk inventory

1. An ambiguous/CHA/platform/dynamic/delegate/constructor/nested-function call could be traversed as exact.
2. Recursive or mutually recursive methods could loop, exceed resources, or produce schedule-dependent prefixes.
3. Depth/node exhaustion could truncate silently or label a partial expansion complete.
4. Two calls to a shared callee could collide, deduplicate incorrectly, or reorder their child messages.
5. A nested guarded call could appear unconditional or inherit the wrong root arm.
6. A child could be expanded from a foreign project/profile, stale fingerprint, missing body, or mismatched Method Flow.
7. Nested edges could still render from the root action rather than the exact parent method participant.
8. Step/node/evidence identity could depend on traversal order, checkout path, or unstable collection iteration.
9. Existing CT-3 root calls, CT-4 fragments, CT-5 neutral roots, and dispatch-specific expansion could regress.

## Existing relevant coverage

- Call Graph has deterministic exact `DirectExact` resolution, canonical CallSite identity, candidates, completeness,
  evidence, and profile-scoped Program Index fingerprint.
- CT-3 covers root-local exact admission, compiler order, duplicate anchors, overload identity, material negatives,
  branch membership, deterministic participants, and no callee traversal.
- CT-4 covers inherited caller guard safety at the root, unsupported topology withholding, and one-time Diagram Plan
  placement.
- CT-5 covers exact configured roots, body-unavailable degradation, neutral presentation, deterministic root identity,
  and root-local guarded calls through Diagram Plan.
- Selected dispatch expansion proves one-level nested ordering and participant identity, but remains dispatch-specific
  and is not reused as generic semantics.

## Soft test budget

At most twelve distinct claims: valid depth-2 traversal/order; deterministic reversed construction; depth and node
budgets; direct and mutual recursion; repeated/shared-callee call-site identity; exact-target/material negatives;
body-unavailable and cross-project boundaries; inherited root membership; locally guarded child withholding; nested
participant source; and no regression to configured local/HTTP/dispatch paths. Use theories for equivalent boundary
diagnostics and one realistic fixture for the complete path.

## Focused verification command

```powershell
dotnet test SeqDoc.slnx -c Release --filter "FullyQualifiedName~DirectExactTraversal|FullyQualifiedName~NestedCall|FullyQualifiedName~RootDirectCall|FullyQualifiedName~DocumentationPlanner"
```

## Final gate

```powershell
dotnet test tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release && dotnet test tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release
```

CreditTransfer regeneration remains pending because local command policy denies `dotnet run`. CT-6 acceptance is the
typed/focused repository fixture plus the declared gate; external regeneration resumes as soon as policy permits.
