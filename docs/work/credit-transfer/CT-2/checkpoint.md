# CT-2 — Exact ASP.NET Core 9 Controller Admission

## Purpose

Extend the existing exact ASP.NET Core controller model's version table to admit compiler-proven ASP.NET Core 9
controller roots and the already-supported direct outcome signatures. This is the fastest generic path to the first
CreditTransfer Web diagrams while configurable roots remain the broader next foundation.

## Target paths

- `src/SeqDoc.FrameworkModels/AspNetCore/AspNetCoreControllerModel.cs`
- `tests/SeqDoc.FrameworkModels.Tests/AspNetCore/AspNetCoreControllerModelEligibilityTests.cs`
- `tests/SeqDoc.FrameworkModels.Tests/AspNetCore/AspNetCoreControllerModelTests.cs`
- `docs/work/credit-transfer/CT-2/**`

## Accepted design

Replace the single ASP.NET Core assembly-version equality with one explicit deterministic supported-version table
containing only `9.0.0.0` and `10.0.0.0`. Reuse that exact check for controller-base eligibility and ControllerBase
outcome helpers. Keep assembly, containing metadata type, method, arity, parameter, return-type, attribute, route,
and certainty requirements unchanged. Do not infer compatibility ranges from version ordering.

## Non-goals

- No ASP.NET Core 8 or unversioned admission, Minimal API version change, compatibility range, or runtime probing.
- No configurable-root, traversal, Scenario, wording, rendering, binding, or outcome-overload expansion.
- No CreditTransfer names, routes, types, methods, source edits, or configuration values in production rules/tests.
- No weakening of exact-symbol, malformed-shape, lookalike, overload, or certainty partitions.

## Risk inventory

1. A broad major-version comparison accidentally admits unsupported ASP.NET Core releases.
2. Entry eligibility accepts version 9 while direct outcomes still silently reject the same exact framework version.
3. Lookalike assembly/type or version 8 begins producing exact roots/outcomes.
4. Existing version 10 identities, ordering, evidence, and generated output regress.
5. The external Web partition still yields no roots because another compiler identity differs; report that boundary
   rather than broadening scope without evidence.

## Existing relevant coverage

- Exact ASP.NET Core 10 controller eligibility, routes, bindings, direct outcomes, overloads, lookalikes, malformed
  shapes, and certainty tests.
- Existing negative tests deliberately reject version 9 because it was outside the old single-version table.
- CT-1 now permits the external net9 Web partition to activate, but it produces `SD4008` with no admitted flows.

## Soft test budget

At most four distinct claims: exact version 9 root admission, exact version 9 supported outcome admission, version 8
rejection, and unchanged version 10 behavior. Consolidate positives/negatives into existing theories where practical.

## Focused verification command

```powershell
dotnet test tests/SeqDoc.FrameworkModels.Tests/SeqDoc.FrameworkModels.Tests.csproj -c Release --filter "FullyQualifiedName~AspNetCoreControllerModel"
```

## Final gate

```powershell
dotnet test tests/SeqDoc.FrameworkModels.Tests/SeqDoc.FrameworkModels.Tests.csproj -c Release
```

After the final gate, rebuild the CLI and regenerate the disposable CreditTransfer Web partition as external
acceptance evidence. That observation does not expand the checkpoint's product targets.
