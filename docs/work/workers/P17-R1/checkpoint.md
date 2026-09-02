# P17-R1 — Hosted-worker callback boundary repair

## State

`ResolvingFindings`

## Authority and frozen state

- Authority: GitHub Issue #17 and readiness comment `5494748157`.
- Original implementation baseline: `a3def8c1bb33f4fa9df83298e63dc87e9478b824`.
- Candidate under repair: PR #64 at `11e433fec19f2b9666125da44c4b20e878c4fcca`.
- Current-main integration target: `24899f984ac5c845d810906422fbb3a75894a9e8`.
- Before repair, merge current main `24899f9` into the candidate. Use one contributor repair round and stop at
  `ReviewRequired` after focused verification is green. After two failed internal reruns, preserve the worktree,
  mark this checkpoint `Blocked`, and stop.
- The one independent review has already run; no second review is authorized.
- GH-17 now records checkpoint `P17-R1`, PR #64 association, branch
  `feature/issue-17-recovery-callbacks`, and `ResolvingFindings` lifecycle. It remains non-selected so I13 stays
  selected.

## Objective and contract

Join an exact source callback boundary to the admitted hosted-worker retry/loop/try context through the Scenario Graph
and Documentation/Mermaid. The join must remain compiler-evidenced, exact, profile- and fingerprint-confined,
deterministic, and conservative. It describes static structure only: it must not claim runtime invocation, invocation
count, recovery, persistence, delivery, success, timing, or scheduling.

The callback member operation belongs to the exact callback target body. Operations from the outer worker body must not
be reclassified as callback-local. Conditional and repeated callbacks retain their guards and conservative cardinality;
callback return rejoins the worker context rather than terminating it. Catch, filter, and finally placement must not be
flattened into an ordinary loop. Ambiguous or duplicate ownership fails closed with evidence and certainty preserved.

## Target paths (allowlist)

- `src/SeqDoc.Analysis.Scenarios/ScenarioGraphBuilder.cs`
- `src/SeqDoc.Application/Documentation/DocumentationPlanner.cs`
- `tests/SeqDoc.Analysis.Tests/CallbackBoundaryProjectionTests.cs`
- `tests/SeqDoc.Scenarios.Tests/HostedWorkerCallbackScenarioTests.cs`
- `tests/SeqDoc.Wording.Tests/HostedWorkerCallbackWordingTests.cs`
- `tests/SeqDoc.Rendering.Tests/HostedWorkerCallbackRenderingTests.cs`
- `tests/fixtures/PassC/HostedWorkers/Worker.cs`
- `docs/work/workers/P17-R1/**`

Core callback contracts/collector and every other path are read-only. PR #64 did not require changes to them. Changing
any path outside this allowlist requires explicit maintainer approval.

## Non-goals

- Persistence, durability, runtime delivery or invocation, counts, success, timing, scheduling, and recovery claims.
- A new framework model, build/package/workflow changes, external source, or application-specific matching.
- Unsupported filter, finally, or catch flattening.

## Risk inventory

1. Profile, fingerprint, or root leakage admits a callback from another analysis context.
2. Cardinality or trigger inference strengthens a conditional/repeated callback into an execution claim.
3. A callback member is collected without ownership by the exact callback body.
4. An outer-worker operation is reclassified as callback-local.
5. Catch, filter, or finally-contained callbacks flatten into an ordinary loop.
6. A callback return incorrectly terminates the worker context.
7. Ambiguous or duplicate operation ownership produces unstable or invented projection.
8. Evidence or weakest certainty is lost or strengthened at Scenario, wording, or rendering boundaries.
9. Ordering, identity, or output becomes nondeterministic.
10. Hand-built tests pass without proving a real producer reaches an observable consumer.

## Existing coverage and review findings

PR #64 focused coverage passed Analysis `38/38`, Scenarios `19/19`, Wording `13/13`, and Rendering `2/2`, in addition
to Issue #16 worker-control coverage. That coverage is retained and reused; do not duplicate its hand-built or
cross-layer assertions. The independent review findings are recorded as pending:

- **P17-R1-F1 — Pending:** conditional/repeated callbacks render unguarded.
- **P17-R1-F2 — Pending:** `MemberOperations` can capture existing outer-worker nodes without exact target-body
  ownership.
- **P17-R1-F3 — Pending:** catch-contained callbacks flatten into an ordinary loop.
- **P17-R1-F4 — Fixed:** canonical checkpoint/state now exists, and GH-17 records its checkpoint, PR association,
  branch, `ResolvingFindings` lifecycle, and non-selected status.

## Test assignment and budget

The Test Writer adds exactly three distinct producer-backed regression groups, within the existing authorized test and
fixture paths, for F1–F3:

1. F1: a conditional/repeated callback retains its guard/trigger and conservative cardinality through Scenario and
   observable wording/Mermaid.
2. F2: callback member operations are sourced only from the exact callback target body, excluding outer-worker
   operations, through the Analysis producer and first observable consumer.
3. F3: a catch-contained callback retains exception-region placement and is not flattened into an ordinary loop,
   with the result observed in the generated scenario/documentation output.

Use realistic `Worker.cs` source and the production extraction path. Do not add a hand-built cross-layer matrix or
duplicate the same assertion at multiple layers without a distinct failure mode. Soft budget: **3 tests/assertion
groups**; no budget exception is planned.

## Focused verification

Run locked restores for `CallbackBoundaries`, `HostedWorkers`, and `FusionCacheCallbacks`, build the affected test
projects once, then run the following filtered tests with Release `--no-restore` and fail-fast via `&&`.

```powershell
dotnet restore tests/fixtures/AdvancedAnalysis/CallbackBoundaries/CallbackBoundaries.csproj --locked-mode && dotnet restore tests/fixtures/PassC/HostedWorkers/HostedWorkers.csproj --locked-mode && dotnet restore tests/fixtures/AdvancedAnalysis/FusionCacheCallbacks/FusionCacheCallbacks.csproj --locked-mode && dotnet build tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release --no-restore && dotnet build tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release --no-restore && dotnet build tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release --no-restore && dotnet build tests/SeqDoc.Rendering.Tests/SeqDoc.Rendering.Tests.csproj -c Release --no-restore && dotnet test tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~CallbackBoundary|FullyQualifiedName~HostedWorker" && dotnet test tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~Callback|FullyQualifiedName~HostedWorker" && dotnet test tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~Callback|FullyQualifiedName~HostedWorker" && dotnet test tests/SeqDoc.Rendering.Tests/SeqDoc.Rendering.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~Callback|FullyQualifiedName~HostedWorker"
```

The focused result must be green before transitioning to `ReviewRequired`. Run `git diff --check` only as the declared
repository check.

## Final gate

Only after the pending findings are resolved and the review boundary is satisfied, run the Release build and complete
Core, Analysis, Behavior, Scenarios, Wording, and Rendering suites. External Acceptance is out of scope unless this
checkpoint is separately amended.

```powershell
dotnet build SeqDoc.sln -c Release --no-restore
dotnet test tests/SeqDoc.Core.Tests/SeqDoc.Core.Tests.csproj -c Release --no-build --no-restore
dotnet test tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release --no-build --no-restore
dotnet test tests/SeqDoc.Behavior.Tests/SeqDoc.Behavior.Tests.csproj -c Release --no-build --no-restore
dotnet test tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release --no-build --no-restore
dotnet test tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release --no-build --no-restore
dotnet test tests/SeqDoc.Rendering.Tests/SeqDoc.Rendering.Tests.csproj -c Release --no-build --no-restore
```
