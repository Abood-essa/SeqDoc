# Exact direct outbound HTTP boundaries checkpoint

## State

`Verifying`

Issue #54 is the frozen semantic contract. Owner continuation authorization and current-main readiness PASS are at
https://github.com/Bilaltariq41/SeqDoc/issues/54#issuecomment-5509514090. The accepted baseline is
`aec7cade98dcfe55e9e2b8eadf9c0d42055254ff`; PR #59 preserves Qais's candidate commit
`7a6dbde5b28bab4658c4ee0025b7847e368ca0c8`.

## Objective

Admit only exact compiler-selected `System.Net.Http.HttpClient.GetAsync(string)` and
`PostAsync(string, HttpContent)` calls for the frozen net9/net10 assembly identities and carry one root-local typed
outbound-request fact through Scenario Graph and Documentation Plan to conservative Markdown/Mermaid wording. Never
display URI/content/credentials or claim response, success, remote execution, retry, or matching server behavior.

## Target paths

The complete 21-path PR #59 file list is frozen:

- `src/SeqDoc.Analysis.Scenarios/ScenarioGraphBuilder.cs`
- `src/SeqDoc.Application/Documentation/DocumentationPlanner.cs`
- `src/SeqDoc.Cli/AggregateAnalysisBuilder.cs`
- `src/SeqDoc.Core/Frameworks/OutboundHttpFacts.cs`
- `src/SeqDoc.Core/ScenarioGraph/ScenarioGraphContracts.cs`
- `src/SeqDoc.FrameworkModels/OutboundHttp/HttpClientOutboundModel.cs`
- `src/SeqDoc.FrameworkModels/OutboundHttp/HttpClientOutboundModelDiagnostics.cs`
- `tests/SeqDoc.Analysis.Tests/OutboundHttpProjectionTests.cs`
- `tests/SeqDoc.Cli.Tests/OutboundHttpCliTests.cs`
- `tests/SeqDoc.Core.Tests/OutboundHttpContractCompatibilityTests.cs`
- `tests/SeqDoc.FrameworkModels.Tests/OutboundHttp/HttpClientOutboundModelTests.cs`
- `tests/SeqDoc.Scenarios.Tests/OutboundHttpScenarioTests.cs`
- `tests/SeqDoc.Wording.Tests/OutboundHttpWordingTests.cs`
- `tests/fixtures/BehaviorDocumentation/OutboundHttp/**`
- `tests/fixtures/BehaviorDocumentation/OutboundHttpLookalike/**`
- `docs/project/work-items/GH-52.json`
- `docs/project/work-items/GH-54.json`
- `docs/work/outbound-http/QHTTP-A/**`

No additional path is authorized without owner amendment.

## Non-goals

- No external acceptance (#53), URI/content/header extraction, response/status/outcome, DI/factory/resilience support,
  client/server correlation, persistence, worker, WCF/CoreWCF, MediatR, configuration, or persistence change.
- No application-name matching, public numeric renumbering, persistence/serialization change, or generic platform-call
  inclusion.
- No rewrite/rebase of Qais's existing commit.

## Risk inventory

1. Partial identity or missing admission fields produce a positive fact or unsupported diagnostic.
2. Wrong assembly/token/version, foreign same-qualified type, unsupported overload, or supplied ordinal crosses admission.
3. Diagnostic identity changes when only explanatory reason changes.
4. Duplicate/conflicting facts or incompatible profile/fingerprint/caller/operation joins emit a boundary.
5. Platform calls duplicate generic DirectExact presentation.
6. Guard/topology placement is invented or SC013 shape changes.
7. Evidence/certainty weakens, ordering becomes unstable, or cancellation is ignored.
8. URI, credential, content, response, status, success, retry, or remote-completion wording leaks.
9. Current-main Scenario/Planner shared-path behavior regresses.
10. Candidate tests prove hand-built facts without a real Roslyn-to-CLI observable path.

## Existing relevant coverage

PR #59 adds ~24 candidate `[Fact]` tests (the `FullyQualifiedName~OutboundHttp` focused filter additionally exercises pre-existing shared-lane tests, so it reports more), including real Roslyn,
foreign-assembly, unsupported overload/version, conflict, topology, credential, deterministic output, and contract
compatibility coverage. Existing current-main CoreWCF failures are baseline evidence only.

## Test Writer and soft budget

The existing candidate already includes the required semantic Test Writer coverage. Repair only the uncovered Copilot
risks: missing/default supplied ordinals, reason-independent diagnostic identity, and matching version-behavior docs.
Add at most 2 focused assertions/tests; do not duplicate the 24-test suite or edit unrelated tests.

## Focused verification command

```powershell
dotnet test tests/SeqDoc.Core.Tests/SeqDoc.Core.Tests.csproj -c Release --filter "FullyQualifiedName~OutboundHttp"; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.FrameworkModels.Tests/SeqDoc.FrameworkModels.Tests.csproj -c Release --filter "FullyQualifiedName~OutboundHttp"; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release --filter "FullyQualifiedName~OutboundHttp"; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release --filter "FullyQualifiedName~OutboundHttp"; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release --filter "FullyQualifiedName~OutboundHttp"; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Cli.Tests/SeqDoc.Cli.Tests.csproj -c Release --filter "FullyQualifiedName~OutboundHttp"
```

## Review boundary

Qais merged current main, resolved the three bounded findings, and ran the focused command. The Orchestrator inspected
the complete candidate and the independent Reviewer ran once; all QHTTP-A-F1..F3 dispositions are recorded below.
The declared final gate is now the next action.

## Final gate

```powershell
dotnet build SeqDoc.slnx -c Release; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Core.Tests/SeqDoc.Core.Tests.csproj -c Release --no-build; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.FrameworkModels.Tests/SeqDoc.FrameworkModels.Tests.csproj -c Release --no-build; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release --no-build; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release --no-build; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release --no-build; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Rendering.Tests/SeqDoc.Rendering.Tests.csproj -c Release --no-build; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Cli.Tests/SeqDoc.Cli.Tests.csproj -c Release --no-build
```

## Independent review repair (QHTTP-A-F1..F3)

State: `Building` after the bounded repair, focused verification, and one independent review; final-gate verification is pending. The earlier contributor-recorded review and
final-gate material below is retained as historical evidence only and is not authoritative for this checkpoint.

| Finding | Disposition | Evidence / repair | Test and observable assertion | Residual |
| --- | --- | --- | --- | --- |
| QHTTP-A-F1 Major: a DI-resolved root omitted its root-local outbound HTTP fact. | Fixed | `ScenarioGraphBuilder` now invokes `AddOutboundHttpRequests` on the resolved root's action node immediately before topology construction and finalization. The existing join remains root-local (`CallerMethod == entryPoint.RootMethod`) and therefore does not project callee-local facts or alter service/persistence joins. | Existing `ResolvedHttpRootRetainsItsRootLocalOutboundHttpFact` passes through the real graph builder and asserts one service node plus one outbound HTTP node. | none |
| QHTTP-A-F2 Medium: claimed isolation coverage did not exercise `ScenarioGraphBuilder`. | Fixed / preserved | The Test Writer's real cross-profile and fingerprint fail-closed graph test remains in `OutboundHttpProjectionTests`. | `ForeignFrameworkProfileAndFingerprintCannotJoinScenarioGraph` passes and asserts no outbound node joins the foreign framework facts. | none |
| QHTTP-A-F3 Medium: durable state and review/final-gate records were contradictory and premature. | Fixed | State is now coherent: `Building`; the historical record is explicitly non-authoritative. Final-gate verification remains pending, and no gate result is claimed or reused. | Capsule and GH-54 work-item state updated consistently after the one independent review. | Final gate remains pending. |

No additional tests were added for this repair; the existing focused assertions cover the distinct F1 and F2 failure
modes. No soft-budget exception.

Focused verification (the declared Analysis command): `SeqDoc.Analysis.Tests` — 7 passed, 0 failed, 0 skipped.
The declared final gate is pending and has not been run for this repair.

### Historical evidence (non-authoritative)

The prior contributor-recorded review, final-gate results, environment notes, and publication references remain below
for traceability only. They do not establish the current checkpoint state or authorize reuse of that final gate.

Historical contributor review recorded an accept-as-is verdict after the earlier repair round, with focused results of
Core 2/2, FrameworkModels 7/7, Analysis 5/5, Scenarios 4/4, Wording 4/4, and CLI 3/3; it also recorded a full build
with zero warnings/errors. The historical final gate recorded Core 93/93, FrameworkModels 325/325, Analysis 275/279
with four unchanged CoreWCF baseline failures, Scenarios 243/243, Wording 133/133, Rendering 77/77, and CLI 24/24.
It identified local SDK relocation noise and recorded candidate publication as `8b9e064`. These records are retained
as historical evidence only; this repair did not run or authorize the final gate.

## Maintainer-takeover final evidence

The owner authorized the maintainer takeover. Current main was merged at `38a4d30e31cbc93bda434ecf16ebbd76b951022b`.
The locked fixture restore lane passed `29/29` (27 baseline fixtures plus the two QHTTP fixtures), and the Release
build passed with `0` warnings/errors. Focused Analysis repair verification passed `7/7`.

The complete Analysis gate timed out after five non-QHTTP failures were observed: one unavailable TicketReservation
corpus lookup and the same four inherited CoreWCF failures. Downstream continuation passed Scenarios `243/243`,
Wording `133/133`, Rendering `77/77`, and CLI `24/24`. The worktree was clean at completion of takeover verification.

No QHTTP-scoped regression remained. The environment limitation is accepted on the exact named boundaries above and
the prior contributor complete lane. No further test rerun is required before merge.
