# Issue 12 persisted-transition checkpoint

## State

`Closed`

Owner activation was granted on 2026-09-01. GitHub Issue #12 is the frozen implementation contract. Start from
`origin/main` at `ca5bc4b3be0f3ea97def06f053f7bbd1f7a40d2e`; stop for a readiness amendment if current compiler evidence cannot
satisfy a frozen admission or join.

PR #51 merged as `e145f3c3a5a872ea4369142d2d652ff018a1c0d0`, preserving Ahmad Krarha's commits
`cdd5f3347da09f6e7968061c0c592e18b34fec6b`, `252ce51403f267054d1d0c1b05658b1a7a8c73e8`, and
`b1b388b7154c8a2f7f5a06e2ed252d879155bc01` with attribution intact. The required current-main merge was
`74e3a1e874b754a56eb2deeed02cab89e2d0d580`.

One independent review ran; no second review ran. I12-F1 was rejected with evidence because exact generic assignment
facts remain available while only unproven persistence presentation is withheld. I12-F2 through I12-F4 and final-gate
regression S1 were fixed. Direct Analysis passed 1/1, focused Analysis 14/14, Scenarios 113/113, Wording 38/38, and
EfCoreScenarioProjection 25/25. FourFlows passed 12 tests with the one owner-accepted, baseline-identical
TicketReservation `SD1102` failure. The authorized final gate built Release with 0 warnings/errors; Core passed 91/91,
Scenarios 239/239, Wording 129/129, and Rendering 77/77. Remaining complete Analysis and Acceptance failures matched
the documented current-main, local SDK, or unavailable external-lane signatures. `git diff --check` passed.

## Objective

Project compiler-proven entity assignments, exact EF mutation requests, exact `SaveChanges` calls, and structural caller
outcomes through the production pipeline:

source -> Roslyn semantic facts -> Method Flow -> Scenario Graph -> Documentation Planner -> Markdown/Mermaid.

The first observable completion assertions use the internal `BehaviorDocumentation/FourFlows` fixture and cover
`WidgetService.ReserveAsync`, `WidgetService.CancelAsync`, and their existing controller callers. Output may say that
code assigns, adds or removes, calls `SaveChanges`, and returns or chooses a structural result. It must not claim that a
database write, transaction, commit, row change, or runtime outcome succeeded.

## Target paths

Production:

- `src/SeqDoc.Core/Semantics/NonGetSemanticFacts.cs`
- `src/SeqDoc.Core/Semantics/StructuralResultFacts.cs`
- `src/SeqDoc.Analysis.Roslyn/Semantics/RoslynNonGetSemanticFactCollector.cs`
- `src/SeqDoc.Analysis.Roslyn/Semantics/RoslynStructuralResultFactCollector.cs`
- `src/SeqDoc.Analysis.Scenarios/ScenarioGraphBuilder.cs`
- `src/SeqDoc.Core/ScenarioGraph/ScenarioGraphContracts.cs`, only if existing typed nodes and edges cannot represent the relationship
- `src/SeqDoc.Application/Documentation/DocumentationPlanner.cs`

Tests and fixture:

- `tests/SeqDoc.Analysis.Tests/NonGetSemanticCollectorNegativeTests.cs`
- existing structural-result Analysis tests only for a distinct uncovered producer failure
- `tests/SeqDoc.Scenarios.Tests/ScenarioGraphBuilderTests.cs`
- `tests/SeqDoc.Scenarios.Tests/ScenarioTopologyTests.cs`
- `tests/SeqDoc.Wording.Tests/DocumentationPlannerTests.cs`
- `tests/SeqDoc.AcceptanceTests/BehaviorDocumentationFourFlowTests.cs`
- existing `tests/fixtures/BehaviorDocumentation/FourFlows/Services/WidgetService.cs`
- existing `tests/fixtures/BehaviorDocumentation/FourFlows/Services/MutationProbeService.cs`
- the existing FourFlows result type and controller files only for Issue #12's named positive or negative additions

Checkpoint records:

- `docs/work/persistence/I12/**`
- `docs/project/execution.json`

An unlisted product, test, fixture, build, package, workflow, or configuration path requires owner approval before edit.

## Non-goals

- Runtime persistence, transaction, commit, row-count, database-content, or success claims.
- Broad state-machine or cross-root correlation.
- EF6/EDMX, persistence backend, SQLite, CLI, renderer, external-corpus, worker, recovery, or service semantics.
- New application-specific matching rules.
- New mutation or save overloads outside Issue #12's frozen admission table.
- Incidental public-contract, serialization, build, package, SDK, or workflow changes.

## Risk inventory

1. An ordinary DTO, local, computed, unresolved, or same-shaped lookalike assignment is strengthened into an entity
   transition.
2. A mutation and save from different methods, profiles, Program Index fingerprints, entities, guards, or chronology are
   joined.
3. A loop-local call site is presented with inferred runtime multiplicity rather than one compiler occurrence.
4. Guard polarity, terminal arms, rejoin, or exception-region boundaries leak a transition into another outcome arm.
5. Existing exact PR #27 EF mutation/save facts are broadened by name or shape.
6. Structural results are strengthened into runtime `IsSuccess`, database success, or an incompatible controller outcome.
7. Evidence is dropped or certainty is strengthened across fact, flow, graph, wording, or diagram stages.
8. Duplicate or conflicting anchors choose a first candidate, throw, or create partial output.
9. Reversed inputs, checkout paths, or unstable ordering change identities, diagnostics, or generated bytes.
10. Failed or invalid candidate construction partially replaces the previous valid analysis or documentation state.
11. PR #48 changes shared Scenario Graph or planner contracts while Issue #12 is in progress. Issue #12 must not consume
    that branch implicitly; re-audit and merge current `main` before requesting review, and stop if the frozen contract
    requires amendment.

## Existing coverage

- PR #27 accepted exact EF Core `DbSet<T>.Add`, `RemoveRange`, supported `Clear`, and exact `SaveChanges`/
  `SaveChangesAsync(CancellationToken)` facts.
- FourFlows already exercises Reserve and Cancel query, mutation, save, structural result, and controller outcome paths.
- Existing NonGet tests cover assignment value kinds and negatives.
- Existing structural-result tests cover exact factories, status switching, and return provenance.
- Scenario Graph tests cover exact profile/fingerprint joins, guard and terminal placement, topology, deterministic IDs,
  and conservative diagnostics.
- Documentation Planner and Acceptance tests cover typed wording, Mermaid chronology, evidence, certainty, and repeated
  output.
- Existing activation tests cover previous-valid-state preservation. Add checkpoint-specific coverage only if the new
  candidate introduces a distinct failure path.

## Test Writer assignment and soft budget

A Test Writer is required because this checkpoint adds compiler and intermediate-representation semantics, exact
association and chronology, false-positive boundaries, evidence/certainty propagation, profile/fingerprint isolation,
deterministic output, previous-valid-state behavior, and acceptance-critical generated documentation.

Uncovered risks are the eleven items above. Exact test targets are the listed test and FourFlows fixture paths. Reuse the
existing EF, assignment, structural-result, topology, wording, and acceptance coverage; do not duplicate generic host,
renderer, persistence-store, or unrelated framework tests. Runtime database behavior, external corpus, EF6, workers,
services, and broad state-machine behavior are non-goals.

Soft budget: approximately 10 to 14 distinct tests by failure boundary, including at least one real producer-to-generated
Markdown/Mermaid assertion. Hand-built facts may supplement conflict and placement tests but cannot close the producer
slice.

Focused Test Writer command:

```powershell
dotnet restore tests/fixtures/BehaviorDocumentation/FourFlows/FourFlows.csproj --locked-mode; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~NonGetSemantic|FullyQualifiedName~StructuralResult"; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ScenarioGraphBuilder|FullyQualifiedName~ScenarioTopology"; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~DocumentationPlanner"; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --no-restore --filter "FullyQualifiedName~BehaviorDocumentationFourFlow"
```

## Focused implementation command

```powershell
dotnet restore tests/fixtures/BehaviorDocumentation/FourFlows/FourFlows.csproj --locked-mode; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~NonGetSemantic|FullyQualifiedName~StructuralResult"; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ScenarioGraphBuilder|FullyQualifiedName~ScenarioTopology"; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~DocumentationPlanner"; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --no-restore --filter "FullyQualifiedName~BehaviorDocumentationFourFlow"
```

Record nonzero discovery and exact counts for every project. Start with a focused failing producer-to-observable
assertion; compile, fixture, restore, or zero-discovery failures are not the accepted red signature.

## Review boundary

Implementation stops at `ReviewRequired`. The Orchestrator inspects the actual complete diff, invokes one independent
Reviewer, and records every finding as Fixed, Rejected with evidence, or Deferred with explicit owner approval. Run the
final gate only after all findings are resolved. After two failed repair reruns, set the checkpoint to `Blocked`, preserve
the worktree, and stop.

## Final gate

Run once after review resolution:

```powershell
dotnet build SeqDoc.slnx -c Release; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Core.Tests/SeqDoc.Core.Tests.csproj -c Release --no-build --no-restore; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release --no-build --no-restore; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release --no-build --no-restore; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release --no-build --no-restore; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Rendering.Tests/SeqDoc.Rendering.Tests.csproj -c Release --no-build --no-restore; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --no-build --no-restore
```

Record exact counts. Any pre-existing or unavailable external-corpus failure requires the identical command,
preparation, and failure signature on a clean baseline; it never permits weakening Issue #12 assertions.
