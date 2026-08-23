# PR 26 — Maintainer Integration Repair

## Purpose

Preserve the contributor's compiler projection, hosted-service model, Timer identity, fixtures, and vertical consumer
work while resolving the remaining bounded evidence-chain findings after two repair rounds. Finish the original PR
without strengthening static registration into observed runtime behavior or silently dropping unsupported placement.

## Target paths

- `src/SeqDoc.Analysis.Scenarios/ScenarioGraphBuilder.cs`
- `src/SeqDoc.Analysis.Roslyn/Frameworks/FrameworkAnalysisRequestProjector.cs` (final-gate generic-type regression repair only)
- `src/SeqDoc.Application/Documentation/DocumentationPlanner.cs`
- `tests/SeqDoc.Analysis.Tests/HostedWorkerProductionProjectionTests.cs`
- `tests/SeqDoc.Analysis.Tests/packages.lock.json` (required lock update for the contributor's existing Scenario test-project reference)
- `tests/SeqDoc.FrameworkModels.Tests/Workers/HostedWorkerAndSchedulerModelTests.cs` (final-gate analyzer repair only)
- `tests/SeqDoc.Scenarios.Tests/HostedWorkerScenarioTests.cs`
- `tests/fixtures/PassC/HostedWorkers/Worker.cs`
- `docs/work/workers/P26/**`
- Orchestrator-owned `docs/project/execution.json` and `docs/project/status.md`

Changing another product, test, fixture, build, or workflow path requires Orchestrator approval.

## Accepted design

1. Keep every accepted contributor contract, extractor, model, stable identity, fixture, and registration-to-worker join.
   Do not rewrite the package or broaden Issues #14/#15.
2. Scheduler placement requires Framework facts, Behavior, and Program Index to carry exact matching profile/snapshot
   identity. Missing or foreign Behavior identity withholds Timer presentation and records one stable evidence-backed
   Scenario diagnostic.
3. Classify each recognized exact Scheduler fact placement before projection. Missing/ambiguous operation anchors,
   direct control dependence, and non-root regions remain unsupported. Withhold the Timer node and emit one canonical
   diagnostic naming the boundary class, backed by Scheduler plus available Method Flow evidence and weakest certainty.
4. Preserve the exact unconditional root-region anchor as the only currently admitted Timer placement. This repair does
   not attempt guarded composition; Issue #22 owns compiler-proven nested topology.
5. Describe hosted lifecycle as the registered framework lifecycle slots in Start, Execute, Stop order. Do not claim
   that a runtime host actually invoked, completed, scheduled, or successfully executed a method. Timer wording remains
   registration-only.
6. Add a genuine exact hosted-worker type that is not registered. Production extraction may recognize its lifecycle
   capability, but Scenario admission and observable documentation must exclude it because no exact registration joins.
7. Keep deterministic identities/order, cancellation evidence, exact `IHostedService` versus `BackgroundService`
   member slots, unsupported callback diagnostics, profile isolation, and weakest certainty unchanged.

## Non-goals

- No polling, retry, recovery, callback execution, runtime timing, concurrency, configuration inference, supplied-project
  acceptance, guarded-callee composition, new worker/scheduler forms, build configuration, CLI behavior, or unrelated
  cleanup.

## Risk inventory

1. Foreign or missing Behavior identity authorizes a coincident Timer operation anchor.
2. Guarded, exception-region, missing, or duplicate anchors disappear without an observable boundary.
3. Diagnostics omit affected evidence, strengthen certainty, collide across boundary classes, or become input-order
   dependent.
4. Lifecycle wording still reads as observed runtime execution or loses required Start/Execute/Stop chronology.
5. A genuine but unregistered worker becomes a Scenario root because lifecycle capability is mistaken for admission.
6. The repair changes accepted framework facts, stable identities, existing unguarded Timer output, or contributor scope.
7. Generic operation projection attempts a stable symbol identity for anonymous/error type arguments and breaks unrelated
   Minimal API extraction.

## Existing coverage and soft budget

The contributor tests already prove exact object creation, `AddHostedService<T>`, direct versus background lifecycle
slots, unsupported callback diagnostics, registration admission, framework profile/fingerprint rejection, weakest
certainty, guarded omission, deterministic lifecycle order, and source-to-Documentation Timer registration wording.

Add approximately 5–7 focused claims: foreign and missing Behavior confinement; guarded and non-root-region diagnostic
partitions; missing/ambiguous anchor diagnostic partition; registration-only lifecycle wording; and a real unregistered
worker negative through Scenario/Documentation. Do not duplicate framework-model or callback-overload tests.

## Focused command

```powershell
dotnet build tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release && dotnet build tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release && dotnet build tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release && dotnet test tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~HostedWorkerProductionProjection" && dotnet test tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~HostedWorker" && dotnet test tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~HostedWorker"
```

## Final gate

```powershell
dotnet test tests/SeqDoc.FrameworkModels.Tests/SeqDoc.FrameworkModels.Tests.csproj -c Release --no-build --no-restore && dotnet test tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release --no-build --no-restore && dotnet test tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release --no-build --no-restore && dotnet test tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release --no-build --no-restore && dotnet test tests/SeqDoc.Rendering.Tests/SeqDoc.Rendering.Tests.csproj -c Release --no-build --no-restore
```
