# Exact direct outbound HTTP boundaries checkpoint

## State

`Building`

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

PR #59 adds 24 focused tests across Core, FrameworkModels, Analysis, Scenarios, Wording, and CLI, including real Roslyn,
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

Qais must merge current main, resolve the three current Copilot findings in one bounded repair, run the focused command,
and stop at `ReviewRequired`. The Orchestrator then inspects the complete candidate and invokes one independent Reviewer.
Record every disposition and run the final gate only after findings are resolved.

## Final gate

```powershell
dotnet build SeqDoc.slnx -c Release; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Core.Tests/SeqDoc.Core.Tests.csproj -c Release --no-build; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.FrameworkModels.Tests/SeqDoc.FrameworkModels.Tests.csproj -c Release --no-build; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release --no-build; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release --no-build; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release --no-build; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Rendering.Tests/SeqDoc.Rendering.Tests.csproj -c Release --no-build; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; dotnet test tests/SeqDoc.Cli.Tests/SeqDoc.Cli.Tests.csproj -c Release --no-build
```
