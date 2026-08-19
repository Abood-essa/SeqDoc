# CT-10 — HTTP Root Label Hygiene

## Purpose

Apply the same cheap concise participant rule to HTTP roots after external acceptance showed their controller labels
still used namespace-heavy canonical identities.

## Target paths

- `src/SeqDoc.Application/Documentation/DocumentationPlanner.cs`
- `src/SeqDoc.Analysis.Scenarios/ScenarioGraphBuilder.cs`
- `tests/SeqDoc.Scenarios.Tests/**`
- `tests/SeqDoc.Wording.Tests/**`
- `docs/work/credit-transfer/CT-10/**`

## Accepted design

Use the namespace-free controller type plus exact action member for the HTTP action participant when both facts exist.
Retain collision-safe participant keys, full identities in evidence/debug output, exact self-call reuse, and all existing
Minimal API/configured-root behavior. No general shortest-name algorithm or wrapping.

## Focused command and final gate

```powershell
dotnet test tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release --filter "FullyQualifiedName~HttpActionPresentation" && dotnet test tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release --filter "FullyQualifiedName~Participant|FullyQualifiedName~ConfiguredMethod"
```

```powershell
dotnet test tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release && dotnet test tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release
```
