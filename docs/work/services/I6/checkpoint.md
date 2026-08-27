# Issue 6 — WCF Client Outbound Boundaries, Faults, and Responses

## Purpose

Admit an exact source/generated `ClientBase<TContract>` invocation through the production compiler/model/scenario/planner pipeline to one protocol-neutral outbound client message and a conservative response/fault declaration. Retain exact evidence, weakest contributing certainty, profile/project/fingerprint joins, placement, multiplicity, and deterministic order. No network call is executed; no runtime response or runtime fault is claimed — only compiler-evidenced declaration and conservative claim transitions.

## Target paths

- `src/SeqDoc.Core/Frameworks/CoreWcfFacts.cs`
- `src/SeqDoc.FrameworkModels/CoreWcf/CoreWcfServiceModel.cs`
- `src/SeqDoc.Analysis.Roslyn/Frameworks/CoreWcfClientInvocationScanner.cs` (new)
- `src/SeqDoc.Core/Frameworks/IFrameworkBehaviorModel.cs` (additive `FrameworkClientInvocationShapeDescriptor` + `OperationDescriptor.ClientInvocationShape` field)
- `src/SeqDoc.Analysis.Roslyn/Frameworks/FrameworkAnalysisRequestProjector.cs` (additive wiring only)
- `src/SeqDoc.Core/ScenarioGraph/ScenarioGraphContracts.cs` (new `ScenarioNodeKind.ClientOperationInvocation` + additive `ScenarioNodePresentation` fields)
- `src/SeqDoc.Analysis.Scenarios/ScenarioGraphBuilder.cs` (new `AddServiceClientInvocations` join)
- `src/SeqDoc.Application/Documentation/DocumentationPlanner.cs` (wording/diagram-plan glue for the new node kind)
- `tests/SeqDoc.Analysis.Tests/CoreWcfClientInvocationProjectionTests.cs` (new)
- `tests/SeqDoc.FrameworkModels.Tests/CoreWcf/CoreWcfClientInvocationModelTests.cs` (new)
- `tests/SeqDoc.Scenarios.Tests/CoreWcfClientInvocationScenarioTests.cs` (new)
- `tests/SeqDoc.Wording.Tests/CoreWcfClientInvocationWordingTests.cs` (new)
- `tests/fixtures/PassC/CoreWcfServices/ClientCallers.cs` (new)
- `docs/work/services/I6/**`

Changing another path requires Orchestrator approval and evidence that the accepted boundary cannot be implemented in the listed owner.

## Accepted design

1. Exact `ClientBase<TContract>` invocation admission through Roslyn projection from source or generated code, with strict symbol identity checking to exclude foreign symbols, wrong assembly/version, mixed CoreWCF/`System.ServiceModel` families, and unrelated constructed contracts.
2. Conservative classification of result claims (`Discarded`/`ResultAssigned`/`ResultReturned`/`Unclaimed`, `IsAwaited`) from call-site syntax without network execution or runtime observation.
3. Conservative fault declaration as declaration-only evidence, not thrown/caught/transported/observed, subject to the existing `ServiceFaultContractFact` semantics from service-side admission.
4. Rejection of ambiguous interface-typed receivers and methods inherited from types other than the receiver's own exact static type; fallback to diagnostic on metadata-only or unclassified boundaries.
5. Production of one `ScenarioNodeKind.ClientOperationInvocation` node per admitted invocation, distinct from the generic `MethodCall` node and not colliding with the reserved "client" participant key.
6. Preservation of exact evidence and weakest-contributor certainty across the join chain: Roslyn → model → scenario → planner.
7. Deterministic output under reversed input order and stable multiplicity/chronology for repeated calls to the same operation.
8. Reuse of Program Index, Method Flow, call graph, predicate wording, Scenario topology, and Diagram Plan fragments. No new Roslyn scan outside the compiler pipeline.

## Non-goals

Network execution, runtime fault/response observation, retries/timeouts, transport behavior, broad HTTP support, generalized dependency injection, remote service composition, callee traversal beyond the existing bounded contract, application-specific rules, and any change to the existing `ServiceCall` node kind semantics (DI-resolved same-process dispatch remains untouched).

## Risk inventory

1. An invocation is admitted from a same-shaped foreign symbol, wrong assembly/version, mixed CoreWCF/`System.ServiceModel` family, or unrelated constructed contract.
2. An ambiguous interface-typed receiver, or a method inherited from a type other than the receiver's own exact static type, is wrongly admitted.
3. A metadata-only/unclassified client boundary (or no boundary at all) still admits an outbound message instead of falling back to a conservative unsupported-invocation diagnostic.
4. Result-claim classification (`Discarded`/`ResultAssigned`/`ResultReturned`/`Unclaimed`, `IsAwaited`) misclassifies a call site's own syntax, or overstates it into a response/network claim.
5. A declared fault is presented as thrown, caught, transported, or observed rather than declaration-only.
6. Evidence/certainty is strengthened rather than preserved/weakened across the join, or the weakest-contributor rule is violated.
7. The new node duplicates the existing generic `MethodCall` node for the same call site (double message), or collides with the reserved "client" participant key.
8. Multiplicity/chronology is lost for repeated calls to the same operation, or output is not byte-deterministic under reversed input.
9. Credential/secret leakage from constant arguments or generated metadata.
10. The new `ScenarioNodeKind.ClientOperationInvocation` is confused with `ScenarioNodeKind.ServiceCall` (DI-resolved same-process dispatch), misrepresenting an outbound call as in-process dispatch.

## Existing coverage and soft budget

`CoreWcfServiceModelProjectionTests`, `CoreWcfServiceModelTests`, and the merged #5/#7 service-side admission tests already pin `ServiceClientBoundaryFact` classification, `ServiceFaultContractFact` declaration-only evidence, and exact contract/operation identity. This checkpoint adds four new sibling test files (does not extend the existing four, which stay scoped to service-side admission) plus a new fixture file `ClientCallers.cs` with positive result-claim shapes (Discarded/ResultAssigned/ResultReturned/Unclaimed, SourceClient/GeneratedClient, fault-declaring operation, repeated-call multiplicity) and negative lookalikes (ambiguous interface-typed receiver, mismatched-contract client, field store, discard assignment, argument pass-through).

Approximately 26 distinct claims are present across the four new test files (6 Analysis projection, 12 FrameworkModels model, 5 Scenarios join, 3 Wording) — higher than the routine 5–12 guidance because this is a full producer-to-first-observable vertical slice across four layers with a wide negative-admission matrix. Record this as an accepted deviation subject to reviewer confirmation that no assertions are duplicative.

## Focused command

```powershell
dotnet restore tests/fixtures/PassC/CoreWcfServices/CoreWcfServices.csproj --locked-mode
dotnet restore tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj
dotnet restore tests/SeqDoc.FrameworkModels.Tests/SeqDoc.FrameworkModels.Tests.csproj
dotnet restore tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj
dotnet restore tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj
dotnet test tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release --filter "FullyQualifiedName~CoreWcfClientInvocation"
dotnet test tests/SeqDoc.FrameworkModels.Tests/SeqDoc.FrameworkModels.Tests.csproj -c Release --filter "FullyQualifiedName~CoreWcf"
dotnet test tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release --filter "FullyQualifiedName~CoreWcf"
dotnet test tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release --filter "FullyQualifiedName~CoreWcf"
```

The four test projects' own restore step is required before the first `dotnet test` run in a clean or
detached worktree — without it, `dotnet test ... --no-restore` fails with `NETSDK1004` because those
projects' restore assets (`obj/project.assets.json`) never existed yet. Once restored, subsequent runs
against an unchanged tree may add `--no-restore` back for speed.

All five commands passed during orchestrator triage on 2026-08-26: Analysis 6/6, FrameworkModels 43/43,
Scenarios 13/13, Wording 4/4; no failures. Re-verified from a clean worktree during the PR #36 repair
round on 2026-08-27 using the restore-then-test sequence above: Analysis 8/8, FrameworkModels 43/43,
Scenarios 18/18, Wording 4/4; no failures (test counts increased by the repair round's new B1–B5
coverage; see PR #36 review responses for the finding-to-test map).

## Final gate

```powershell
dotnet test tests/SeqDoc.Core.Tests/SeqDoc.Core.Tests.csproj -c Release --no-restore
dotnet test tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release --no-restore
dotnet test tests/SeqDoc.FrameworkModels.Tests/SeqDoc.FrameworkModels.Tests.csproj -c Release --no-restore
dotnet test tests/SeqDoc.Behavior.Tests/SeqDoc.Behavior.Tests.csproj -c Release --no-restore
dotnet test tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release --no-restore
dotnet test tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release --no-restore
dotnet test tests/SeqDoc.Rendering.Tests/SeqDoc.Rendering.Tests.csproj -c Release --no-restore
```

Not yet run; execute once after independent review findings are resolved, per `docs/project/workflow.md`.

## Closure

Independent review (reviewer-medium) found no Blocking or Major findings; two Minor/Observation findings were deferred as non-blocking follow-up items (see `state.json`). The final gate passed for all seven declared suites except a pre-existing, unrelated environmental SDK-registration flake in 10 Analysis.Tests relocation/determinism tests (confirmed unrelated by isolated reproduction with zero CoreWCF involvement). This checkpoint is closed. The work remains uncommitted on branch `issue-6-wcf-client-outbound-boundaries`; committing and opening a pull request against GitHub Issue #6 is an owner decision, not performed automatically.

## Post-closure sync with upstream/main

After this checkpoint's closure on 2026-08-26, `upstream/main` advanced by four commits (PR #27: EF Core persistence projection, closing issues #9 and #10). The I6 branch was merged with `upstream/main` at commit `99f8bd2e0c0f24f9bc782d098584ce6e47421fd2` using a true non-fast-forward merge (not a rebase) to preserve the already-open PR's history intact.

Three production files with overlapping changes were manually inspected and confirmed to retain both the CoreWcf client-invocation feature code (`ClientOperationInvocation` markers) and the EF Core persistence feature code (`EntityFramework`/`EfCore` markers) intact with no shadowing or duplication. During re-verification, the GetMeaning fixture was discovered to require its own locked-mode restore (`dotnet restore tests/fixtures/BehaviorDocumentation/GetMeaning/GetMeaning.csproj --locked-mode`) as a previously-undocumented prerequisite to correctly resolve EntityFrameworkCore.Relational extension methods. Full verification was then run against the merged tree: the declared focused CoreWcf command set passed identically (Analysis 6/6, FrameworkModels 43/43, Scenarios 13/13, Wording 4/4), and the final gate passed with identical CoreWcf-scoped pass counts (Core 91/91, FrameworkModels 292/292, Behavior 63/63, Scenarios 213/213, Wording 116/116, Rendering 60/60) and the same 10 pre-existing, unrelated Analysis.Tests SD1102 environmental failures (ConfigurationSemanticProjectionTests, ConditionalDependencyInjectionProjectionTests, CallbackBoundaryProjectionTests, BehaviorDeterminismTests, ProgramIndexBuilderTests — each class appearing twice in the failure set) as confirmed before the merge. No regression found. This checkpoint remains **Closed** and the branch is now synced with current `upstream/main` and ready for the owner's PR-publication step.

## Maintainer review round (PR #36)

Bilaltariq41 reviewed the pushed candidate on GitHub PR #36 and requested changes with 5 Major findings (missing producer-to-observable proof for the full pipeline; silently-admitted conflicting client-boundary anchors; duplicate nodes possible for one call site from duplicate/conflicting invocation facts; an unrealistic hand-built negative standing in for a real signature/ref-kind producer boundary test; and unproven repeated-call chronology/determinism at the observable layer) and 1 Minor finding (the declared focused command is not reproducible in a clean worktree because the four test projects are never restored before `--no-restore`). The checkpoint is reopened and a coherent repair round is in progress, with full finding text in `state.json`'s `maintainerReview` field.

All 6 findings (B1–B6) were repaired in one coherent round with real production fixes and fixture-driven tests (see the repair trace in `docs/project/delegated-contribution-workflow.md` for the full finding-to-test map); independent re-review found no remaining Blocking or Major findings, and two Minor/Observation follow-up items (I6-F-review-2, I6-F-review-3) were also fixed. The candidate is now pending a final-gate rerun before being pushed back to the maintainer on PR #36. B5's residual same-operation/straight-line gap noted above was subsequently verified empirically (not assumed) with a new test proving the existing code was already correct; see the updated repair trace in `docs/project/delegated-contribution-workflow.md`.
