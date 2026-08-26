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
dotnet test tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~CoreWcfClientInvocation"
dotnet test tests/SeqDoc.FrameworkModels.Tests/SeqDoc.FrameworkModels.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~CoreWcf"
dotnet test tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~CoreWcf"
dotnet test tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~CoreWcf"
```

All five commands passed during orchestrator triage on 2026-08-26: Analysis 6/6, FrameworkModels 43/43, Scenarios 13/13, Wording 4/4; no failures.

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
