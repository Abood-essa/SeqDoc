# CT-4 — Try-Region Branch Presentation

## Purpose

Render exact root-local calls guarded by ordinary conditions inside a `try` body without implying catch/finally
behavior. Method Flow already carries the conditions, arm memberships, predicate wording, and normal control edges;
Scenario Graph currently classifies every non-loop decision in any non-root region as `Unknown`, producing `SC013`,
which makes Diagram Plan withhold CreditTransfer's guarded calls with `DP002`. Admit only the compiler-proven plain
`Try` containment shape so existing `alt`, `opt`, `break`, predicate, and `Otherwise` presentation can consume it.

## Target paths

- `src/SeqDoc.Analysis.Scenarios/ScenarioGraphBuilder.cs`
- `tests/SeqDoc.Scenarios.Tests/ScenarioTopologyTests.cs`
- `tests/SeqDoc.Scenarios.Tests/ScenarioTestFactory.cs`
- `tests/SeqDoc.Wording.Tests/FragmentPlannerTests.cs`
- `tests/SeqDoc.Wording.Tests/FragmentScenarioTestFactory.cs`
- `docs/work/credit-transfer/CT-4/**`

## Accepted design

1. A non-loop decision may be classified when every region containing the decision is `Root` or `Try`. `Try`
   containment alone does not mean the decision is an exception branch.
2. Catch, filter, finally, unknown, natural-loop, and irreducible-loop placement remains unsupported for a non-loop
   decision. Preserve the existing exact-own-header loop contract unchanged.
3. Walk only the existing supported normal, polarity, return, throw, and rethrow edges. Any exception-handler, filter,
   finally, loop-back, switch, missing, mixed, or operation-derived duplicate boundary remains `Unknown` with `SC013`.
4. Reuse existing represented return/throw and exact rejoin classification. Combine all traversed edge and boundary
   evidence and degrade certainty to the weakest contributor exactly as today.
5. Reuse existing exact predicate owner/subordinate mapping and complement formatter. A proven owner condition labels
   its semantic arm; an unsafe complement remains `Otherwise`. Do not derive wording from source strings in Scenario
   Graph or Diagram Plan.
6. Reuse existing Diagram Plan fragment admission. A newly supported guarded call appears exactly once in its
   evidence-backed `alt`/`opt` arm, no longer receives `DP002`, and `SC001` remains because no callee was composed or
   traversed.

## Non-goals

- No catch/fault/finally presentation, exception-flow composition, loop fragment, switch-case fragment, or generalized
  structured-exception-handling model.
- No configurable roots, interprocedural or cross-project traversal, DI resolution, service/client correlation, or
  outcome propagation.
- No new Core, Method Flow, Diagram Plan, renderer, persistence, configuration, or external-project contract.
- No project-specific matching, business wording, broad `SC013` suppression, or unconditional fallback for a guarded
  call.

## Risk inventory

1. Treating all exception-region decisions as plain conditions could present catch/filter/finally or loop behavior as
   normal execution.
2. Ignoring an exception-handler/filter/finally edge could classify a partial or mixed boundary as an exact rejoin.
3. A guarded call could appear unconditionally, twice, under the wrong polarity, or after a terminal arm.
4. A true/false complement could be invented when the typed predicate formatter cannot safely express it.
5. Evidence or certainty could omit a traversed edge/boundary or remain stronger than its weakest contributor.
6. Existing own-header loop, duplicate-terminal, foreign-loop, and catch/filter/finally fail-closed behavior could
   regress.
7. Existing service, dispatch, nested-fragment, and root-local call paths could change despite the narrow region rule.

## Existing relevant coverage

- Scenario topology tests cover represented early returns, rejoins, nested memberships, exact own-header loops,
  malformed loop snapshots, catch/filter/finally rejection, mixed boundaries, and duplicate operation anchors.
- Root-direct-call tests prove exact root-only admission, source order, arm membership, retained `SC001`, and material
  negative filtering.
- Fragment planner tests cover exact predicate owner labels, safe complements and `Otherwise`, `alt`, one-sided `opt`,
  terminal `break`, nested fragments, `SC013` fallback, and `DP002` withholding.
- Mermaid rendering is mechanical over Diagram Plan and already covers `alt`, `opt`, and `break`; no renderer change or
  duplicate renderer assertion is needed.
- Current external output is the concrete regression: five exact calls appear in behavior text for each Web root,
  while Mermaid shows only the HTTP entry because supported calls are owned by `SC013` decisions inside `try`.

## Soft test budget

At most eight distinct claims across Scenario and planner tests: plain nested `Try` admission; exact terminal/rejoin and
weakest certainty; catch/filter/finally and exception-edge rejection as equivalence partitions; exact guarded-call
membership/polarity; predicate/complement presentation; one-time fragment placement without `DP002`; retained `SC001`;
and unchanged own-header-loop behavior. Do not duplicate renderer structure tests.

## Focused verification command

```powershell
dotnet test SeqDoc.slnx -c Release --filter "FullyQualifiedName~TryRegion|FullyQualifiedName~RootDirectCall|FullyQualifiedName~ScenarioTopology|FullyQualifiedName~FragmentPlanner"
```

## Final gate

```powershell
dotnet test tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release && dotnet test tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release
```

After the final gate, rebuild the CLI and regenerate the disposable CreditTransfer Web output. Accept the milestone
only if supported exact calls are placed under compiler-evidenced fragments, unsupported exception shapes remain
explicit, and the external checkout is unchanged.
