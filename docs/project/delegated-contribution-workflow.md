# Delegated Contribution Workflow

Delegated changes remain candidates until the maintainer verifies their complete behavior. Preserve the submitted
branch, record its base revision, inspect the actual diff, and classify each area as accepted, repairable, or rejected.
Return bounded findings to human contributors with file/line evidence, risk, expected behavior, and one focused
verification command. The contributor re-reviews the complete candidate; the maintainer reviews the repair delta and
affected risks when it returns. After two unsuccessful repair rounds, reject, split, or explicitly take ownership.

For each finding, record a repair trace:

| Finding | Production repair | Producer/boundary test | Observable assertion | Residual boundary |
|---|---|---|---|---|

A repair is complete only when the trace reaches its required observable or persisted consumer and the contributor's
full-candidate self-review finds no regression outside the repair delta.

Use the same return-and-repair loop for an available implementation agent: resume the same agent session with exact
findings, require it to fix its own delta, and verify only changed risks. Do not have the maintainer silently rewrite
repairable delegated work. The maintainer takes over only when the contributor/agent is unavailable, repeated repair
fails, or the required architectural decision exceeds the delegated scope.

Automated or unavailable-author candidates may be hardened on a local branch based on the submission. Retain correct
code and repair only demonstrated defects. Before publication, compare the full candidate against canonical `main`,
run risk-focused tests and one realistic acceptance scenario, then squash the verified tree onto a clean branch from
`main`. Scratch branches, worktrees, copied fixtures, misleading execution records, and intermediate commits never
enter public history.

Canonical documentation is rewritten from verified evidence. For each integrated candidate record what was reused,
what was repaired or rejected, why, the verification performed, and whether delegation reduced total work. Optimize
the workflow from recurring defect categories rather than weakening review standards.

## Recurring semantic review failures

Recurring semantic failures are broken evidence chains, stronger downstream claims, fail-open identity, lost control
placement, and silent unsupported forms. Apply the five proof gates in `AGENTS.md`. Reviewers should search for fixtures
with no production-path test and hand-built facts with no producer or observable proof.

Scope drift is a separate recurring category. SDK/build files, CLI behavior, public contracts, and other paths outside
the issue allowlist require prior maintainer approval. A useful unrelated fix stays out of the candidate until it has
its own authority and coverage.

## Semantic delivery sequence

Use one primary contributor or coding agent for a semantic package from issue brief through production implementation,
focused tests, and complete-candidate self-review. The issue brief must name the exact compiler identity and operation
shape, registration or admission requirement, evidence chain to the first observable consumer, unsupported forms,
negative lookalikes, target paths, and one focused command. Do not split one semantic chain among layer-specific agents
unless the contract has already been reviewed and the paths are independent.

Invoke one independent reviewer only after the vertical candidate is complete. Batch findings by proof-gate failure,
repair the same branch and PR, and rerun affected focused tests before the declared final gate. After two unsuccessful
repair rounds, stop contributor churn: split or reject the candidate, or explicitly transfer ownership to a bounded
maintainer integration checkpoint. A takeover preserves the contributor's accepted implementation, records every
maintainer repair, publishes through the original PR when practical, and acknowledges both retained work and the final
verification. This sequence reduces handoffs; it does not weaken evidence, review, or test requirements.

## Delivery package sizing

Use sub-issues to model dependencies and acceptance, but avoid requiring maintainer review after every small internal
step. A parent workstream should group 1–3 cohesive sub-issues into an approved delivery package when they share target
paths and one vertical outcome. The contributor and agent may use multiple ordered commits inside one package PR; the
PR closes every included issue and receives one complete self-review.

Keep a contract and its first consumer together when that makes the abstraction demonstrably useful. Separate a shared
foundation when its review could materially redirect dependent work, and keep supplied-project acceptance separate
when it would obscure the semantic implementation diff. Never enlarge a package with unrelated cleanup merely to
reduce review count.

## Review latency and dependent work

Maintain at least one independent `ready` issue per contributor where practical. A contributor with a review-ready PR
may start one independent branch from `main`, keeping a maximum of two open implementation PRs. Blocked work remains
planning-only unless the maintainer explicitly authorizes a two-level stack.

Stack authorization is workflow permission, not code approval: retain the GitHub `blocked by` relationship, add
`stack-approved`, branch the dependent issue from the pending PR, and keep its PR draft with an explicit dependency.
Do not stack review-sensitive shared contracts unless pre-approved. When the base merges, rebase, verify the isolated
diff, rerun affected verification, and remove the label/draft state. If the base direction changes or fails review,
stop dependent implementation rather than preserving sunk work on a rejected foundation.

## Repair trace: PR #33 (`feature/issue-5-exact-contract-operation`, closes #5 and #7)

Base revision after sequencing: rebased onto `upstream/main` at `4c935c5` (PR #34 merged), new branch head `abb4b0f`
before this repair's commits. `.github/*` review-policy files confirmed untouched by the candidate diff.

**Scope authority.** The original issue-#5/#7 comments (2026-08-20) requested separate PRs, and an automated
pre-repair review flagged this PR as conflicting with that instruction. Owner authority (`@Bilaltariq41`, in PR
review) subsequently and explicitly grandfathered the combined package for this PR specifically: do not split it
merely for packaging, and both issues are correctly closed together here. This is a PR-review-level exception to
the general separate-PR instruction on the issues, not a change to the issues themselves or to the default
packaging policy in [Delivery package sizing](#delivery-package-sizing) above.

| Finding | Production repair | Producer/boundary test | Observable assertion | Residual boundary |
|---|---|---|---|---|
| Capability treated as execution: a `[ServiceContract]`/`[OperationContract]` match alone admitted a Scenario Graph root. | Split `CoreWcfServiceModel` into `ServiceOperationCapabilityFact` (attribute+body proof only) and `ServiceEndpointRegistrationFact` (exact `IServiceBuilder.AddServiceEndpoint<TService,TContract>(Binding,string)` proof); `ScenarioGraphBuilder.BuildServiceOperationEntries` joins the two by (implementation, contract) before admitting a root. | `CoreWcfServiceOperationScenarioTests.CapabilityWithoutMatchingRegistrationAdmitsNoRootAndProducesAConservativeDiagnostic`; `CoreWcfServiceModelProjectionTests.RegisteredCapabilityAdmitsARootAndUnregisteredCapabilityProducesAConservativeDiagramDiagnostic` | Unregistered `ExplicitCalculatorService` capability admits no graph and emits `SC-SERVICE-UNSUPPORTED-DISPATCH`; registered `CalculatorService` operations admit roots with `ServiceOperation` wording. | None open. |
| Attribute identity matched on `ProgramAttributeApplication.AttributeType` display strings, accepting same-named foreign-assembly or mixed CoreWCF/`System.ServiceModel` pairs. | Added `FrameworkAttributeApplicationIdentity`/`FrameworkInterfaceMemberIdentity.InterfaceTypeAttributes`/`InterfaceMethodAttributes`, resolved via `ProjectTypeIdentity(attribute.AttributeClass)` (assembly + version + metadata name); `TryGetAdmittedFamily` requires the ServiceContract/OperationContract pair to resolve to the same family. | `CoreWcfServiceModelTests.ForeignAssemblySameQualifiedNameAttributeNeverAdmitsCapability`, `MixedFamilyAttributesNeverAdmitCapability`; fixture `Lookalikes.cs` (`FakeService`, `MixedFamilyService`) through `CoreWcfServiceModelProjectionTests`. | Foreign-assembly and mixed-family lookalikes never produce a capability fact through the real producer. | None open. |
| Service facts bypassed `FrameworkFactsBound`, so a foreign profile or stale Program Index fingerprint could still contribute a root. | `ScenarioGraphBuilder` gates `BuildServiceOperationEntries` behind the same `FrameworkFactsBound(request)` guard `HostedWorker` roots already use. | `CoreWcfServiceOperationScenarioTests.ForeignProfileServiceFactsCannotAdmitARoot`, `MissingProgramIndexFingerprintCannotAdmitARoot` | Mismatched profile/fingerprint facts admit zero graphs and zero diagnostics. | None open. |
| Effective certainty was taken from the input symbol only, never lowered by the contributing evidence's own certainty. | `CoreWcfServiceModel.WeakestCertainty` takes the highest `CertaintyLevel` ordinal (weakest) across the input and every contributing evidence item; the Scenario join takes the weaker of capability and registration certainty. | `CoreWcfServiceModelTests.ClassicSystemServiceModelIdentityIsAlsoAdmitted` (certainty propagation); `CoreWcfServiceOperationScenarioTests.AdmittedRootCombinesCapabilityAndRegistrationEvidence` (evidence union). | Certainty never exceeds the weakest contributor; combined evidence contains both `service-operation-capability` and `service-endpoint-registration` artifacts. | Node-presentation certainty in `ScenarioGraphBuilder.CreateNodeWithPresentation` is a separate, pre-existing evidence-array minimum unrelated to this fact-level rule; left unchanged as out of scope. |
| Issue #5's declared fact boundary (source/generated clients, endpoint metadata, faults) was incomplete. | Added `ServiceClientBoundaryFact` (`ClientBase<T>` derivation, classified by the exact `GeneratedCodeAttribute` marker), `ServiceEndpointRegistrationFact` (endpoint metadata), `ServiceFaultContractFact` (`[FaultContract(typeof(X))]` per admitted operation). | `CoreWcfServiceModelProjectionTests.PassCFixtureCompilationAdmitsExactCapabilitiesFaultsAndClientBoundariesAndWithholdsNegativeBoundaries`, `PassCFixtureCompilationProducesTheExactAddServiceEndpointRegistrationFromStartup` | Real fixture compilation produces both client kinds, the endpoint registration, and the `SquareRoot` fault fact, all with `CertaintyLevel.Exact`. | None open. |
| Explicit-interface-implemented operations (`double ICalculatorService.Add(...)`) silently never admitted capability. | `IsEligibleServiceOperation` required `shape.IsOrdinary`, but Roslyn reports `MethodKind.ExplicitInterfaceImplementation` (never `Ordinary`) for an explicit implementation; relaxed to `shape.IsOrdinary \|\| member.IsExplicitImplementation`. Found only once the vertical producer test replaced the hand-built unit fixture, which had incorrectly hardcoded `IsOrdinary: true` for its explicit-implementation case. | `CoreWcfServiceModelTests.ExplicitInterfaceImplementationAdmitsCapabilityDespiteNonPublicMethod` (now asserts `isOrdinary: false`, matching real Roslyn), `NonOrdinaryImplicitMethodKindNeverAdmitsCapability`; fixture `ExplicitCalculatorService` through the real pipeline. | `ExplicitCalculatorService`'s five explicit-implementation operations all admit capability through the real Roslyn producer. | None open. |
| Every admitting method on a `ClientBase<T>` client type independently emitted `ServiceClientBoundaryFact` under the same `BehaviorFactId` (type-level anchor); `FrameworkModelHost` never deduplicates custom `BehaviorFact` subtypes by payload equality, so the repeated identity reported as a genuine conflict and both facts were discarded. | `BuildClientBoundaryFact`'s anchor now includes the triggering method's own `SymbolId`, so each admitting method emits an independently identified fact instead of colliding. | `CoreWcfServiceModelProjectionTests.PassCFixtureCompilationAdmitsExactCapabilitiesFaultsAndClientBoundariesAndWithholdsNegativeBoundaries` | `CalculatorSourceClient`/`CalculatorGeneratedClient` client-boundary facts survive `FrameworkModelHost` aggregation instead of being removed as conflicting. | None open. |
| `GeneratedCodeAttribute`'s admitted identity used its runtime assembly (`System.Private.CoreLib`), but Roslyn's compile-time `ContainingAssembly` resolves to the `System.Runtime` reference-assembly facade it is type-forwarded through, so real generated-client detection always failed closed to `SourceClient`. | Corrected `Identity.CoreLibAssembly` to `"System.Runtime"`, confirmed by reflecting `typeof(GeneratedCodeAttribute).Assembly.GetName()` from a real compilation under the pinned SDK and comparing against the Roslyn-projected identity; the test fixture's matching constant was equally wrong and is corrected together. | `CoreWcfServiceModelProjectionTests.PassCFixtureCompilationAdmitsExactCapabilitiesFaultsAndClientBoundariesAndWithholdsNegativeBoundaries` | `CalculatorGeneratedClient` (marked with the real `[GeneratedCodeAttribute]`) now produces `ServiceClientKind.GeneratedClient`. | None open. |
| Clean-checkout reproducibility for the switched `Microsoft.NET.Sdk.Web`/`CoreWCF.Http` fixture. | Regenerated `packages.lock.json`; verified `dotnet restore --locked-mode` succeeds standalone before any build step. | Declared verification command (fixture restore, then build/test each affected project with `--no-build --no-restore`). | `dotnet restore tests/fixtures/PassC/CoreWcfServices/CoreWcfServices.csproj --locked-mode` succeeds from a clean `obj`/`bin`. | None open. |

### Second Copilot pass (post-repair correctness gaps)

| Finding | Production repair | Producer/boundary test | Observable assertion | Residual boundary |
|---|---|---|---|---|
| `ScenarioTestFactory`'s service-operation tests anchored `RootMethod` to `ActionMethod` (`GadgetsController.GetById`, a controller action), so the tests never exercised a real service-implementation method ID or its own Method Flow — masking bugs in root identity, evidence propagation, and topology that depend on the actual admitted service method. | Added a dedicated `ServiceOperationMethod` (`CoreWcfServices.CalculatorService.Add`) with its own minimal Program Index entry and Method Flow (`CreateServiceBaseRequest`), independent of the GetMeaning controller/action fixture; `CreateServiceOperationRequest`/`CreateUnregisteredServiceCapabilityRequest` now build from it instead of `CreateGetRequest()`. | `CoreWcfServiceOperationScenarioTests` (all 5, rerun against the new anchor) | `ServiceOperationEntryPoint`/`RootMethod` resolve to the real service method identity; all 5 scenario tests and the full 170-test Scenarios.Tests suite still pass. | None open. |
| `BuildClientBoundaryFact` hardcoded the input certainty as `CertaintyLevel.Exact`, ignoring the triggering `SymbolDescriptor.Certainty`, so a Conservative/Heuristic symbol input could still emit an `Exact` `ServiceClientBoundaryFact`. | `BuildClientBoundaryFact` now takes the triggering method's `SymbolDescriptor.Certainty` as its weakest-certainty input, matching the rule used everywhere else in this model. | `CoreWcfServiceModelTests.ClientBoundaryCertaintyNeverExceedsTheTriggeringSymbolsCertainty` (new) | A `Conservative`-certainty triggering symbol produces a `Conservative`, never `Exact`, client-boundary fact. | None open. |
| `IsApplicable`'s doc comment claimed it detects an "exact admitted attribute identity," but the implementation only matches `ProgramAttributeApplication.AttributeType` metadata-name strings, which cannot distinguish a real CoreWCF/`System.ServiceModel` attribute from a same-qualified-name foreign-assembly lookalike. | Rewrote the doc comment to accurately describe `IsApplicable` as a coarse string-based pre-filter that only decides whether the model runs, with the real exact-identity decision made later per member in `AnalyzeMethod`. No behavior change — `IsApplicable` was never the admission decision, only the doc overstated it. | N/A (documentation-only; existing `ForeignAssemblySameQualifiedNameAttributeNeverAdmitsCapability`/`MixedFamilyAttributesNeverAdmitCapability` already prove the real, later admission decision fails closed on lookalikes). | Doc comment on `CoreWcfServiceModel.IsApplicable` no longer overstates its guarantee. | None open. |

Full declared verification command run after all repairs: `CoreWcfServiceModelProjection\|InterfaceMemberEligibility` (Analysis.Tests) 9/9, `CoreWcf` (FrameworkModels.Tests) 21/21, `CoreWcfServiceOperation` (Scenarios.Tests) 5/5, `CoreWcfServiceOperation` (Wording.Tests) 1/1. Full-suite regression check: FrameworkModels.Tests 252/252, Scenarios.Tests 170/170, Analysis.Tests 197/207 (the 10 failures are the pre-existing `SD1102` multi-SDK MSBuildLocator registration conflict on unrelated fixtures — confirmed present in isolation, independent of this candidate, and unrelated to Issues #5/#7).

### Third repair round: active host-chain proof (supersedes the earlier registration-shape-only technical instructions)

The owner's third review (2026-08-24T20:28:20Z) accepted the two rounds above but identified the real remaining gap:
`CoreWcfServiceModel` admitted an executable root from **any** exact `AddServiceEndpoint<TService,TContract>(Binding,string)`
invocation found anywhere in source, never proving it was reachable from an actively-running host. This round
supersedes the earlier, less specific registration-detection instructions with an exact 7-link host-chain
requirement, typed (symbol-level) identity through every fact and join, a protocol-neutral `ScenarioRootKind`,
deterministic multi-registration handling, and a deeper evidence chain.

Every required API's exact compiler identity was independently confirmed via reflection against the actually-restored
packages (not guessed) before implementation — see the PR description's admission table for the full identity list.

| Finding | Production repair | Producer/boundary test | Observable assertion | Residual boundary |
|---|---|---|---|---|
| Any exact `AddServiceEndpoint` invocation anywhere in source (dead helper, disconnected callback, unchained `AddService`/`AddServiceEndpoint` pair) was treated as dispatch evidence, never proving reachability from an active host. | New `CoreWcfHostChainScanner`: a single, non-heuristic structural scan (never matches `Main`/`Startup`/`Configure` by name) that proves the complete chain — `Host.CreateDefaultBuilder(args).ConfigureWebHostDefaults(w => w.UseStartup<TStartup>())`, `TStartup`'s own exact `Configure(IApplicationBuilder)`, its `UseServiceModel(Action<IServiceBuilder>)` call, and an `AddServiceEndpoint<TService,TContract>` call whose receiver is the exact matching `AddService<TService>()` call in the same lambda. `FrameworkServiceEndpointShapeDescriptor` gained `HostChainProven`/`HostChainEvidence`; `CoreWcfServiceModel.AnalyzeOperation` only emits `ServiceEndpointRegistrationFact` when `HostChainProven` is true. | `CoreWcfServiceModelTests.UnprovenHostChainNeverProducesARegistrationFactDespiteTheExactEndpointShape`; fixture `UnusedRegistrationHelper.NeverCalled` (dead helper) and `UnusedStartup` (disconnected callback) through `CoreWcfServiceModelProjectionTests.PassCFixtureCompilationProducesTheExactAddServiceEndpointRegistrationFromStartup` | The real fixture contains three source-identical `AddServiceEndpoint<CalculatorService,ICalculatorService>` shapes; exactly one (the real, admitted `Startup.Configure` chain) produces a registration fact. | Classic `System.ServiceModel` self-host registration (an `AddServiceEndpoint`-equivalent for that family) is not modeled; a classic-family capability without a CoreWCF registration correctly reports the conservative unsupported-dispatch diagnostic. |
| Facts and joins reduced service/contract/fault/client types to metadata-name strings, losing exact symbol/assembly/project identity and the constructed-generic-argument proof a `ClientBase<TContract>` derivation requires. | `FrameworkServiceEndpointShapeDescriptor`, `ServiceOperationCapabilityFact`, `ServiceEndpointRegistrationFact`, `ServiceFaultContractFact` (+ `FaultTypeIdentity`), and `ServiceClientBoundaryFact` all gained typed `SymbolId`/`FrameworkTypeIdentity` fields alongside their existing presentation strings. New additive `FrameworkTypeShape.BaseTypeChainWithArguments`/`FrameworkBaseTypeIdentity` carries each base type's constructed generic arguments (mirrors the existing `FrameworkAttributeApplicationIdentity` shape); `IsClientBaseDerivedForContract` now requires the *constructed* argument to equal the exact admitted contract, not just `ClientBase\`1`'s presence in the chain. `ScenarioGraphBuilder.BuildServiceOperationEntries` joins on `(ImplementationTypeSymbol, ServiceContractTypeSymbol)`, never metadata-name strings. | `CoreWcfServiceModelTests.ClientBaseConstructedForOneContractNeverEmitsABoundaryForAnUnrelatedAdmittedContract` (unit); fixture `MismatchedContractClient` (derives `ClientBase<ICalculatorService>`, separately implements admitted `IClassicEchoService`) through `CoreWcfServiceModelProjectionTests.ClientBaseConstructedForOneContractNeverEmitsABoundaryForASeparatelyImplementedContract` | `MismatchedContractClient` gets a client boundary only for `ICalculatorService` (the constructed argument); `IClassicEchoService`'s own operation admits ordinary capability instead. | Cross-project same-metadata-name collision and same-name-different-assembly fault types are covered at the producer-adjacent unit level (hand-built `ProjectId`s/`FrameworkTypeIdentity`s), not a second real fixture project, since adding one would be an unapproved solution change. |
| `SC-SERVICE-UNSUPPORTED-DISPATCH` hard-coded `CertaintyLevel.Exact`; `BuildClientBoundaryFact`'s evidence only covered `type.Evidence`, never the triggering symbol or the `GeneratedCodeAttribute` marker's own evidence; `ServiceFaultContractFact`'s evidence only wrapped operation-attribute evidence, never the `[FaultContract]` application's own evidence. | Diagnostic certainty is now the weakest of the capability's own certainty and evidence. `FrameworkAttributeApplicationIdentity` gained an additive `Evidence` field (populated from the same per-attribute source evidence Program Index already builds) so a model never re-derives attribute evidence through a separate target-symbol-plus-metadata-name lookup a foreign lookalike could contaminate; client-boundary and fault-contract evidence now union every real contributor. | New evidence-ID-level assertions in `CoreWcfServiceOperationScenarioTests`/`CoreWcfServiceModelTests` (not certainty-only) | Combined evidence contains every contributing artifact by ID, not just a certainty number. | None open. |
| `NormalizedEntry.RootKind` silently fell through to `ScenarioRootKind.HttpEntryPoint` for `ScenarioActionKind.ServiceOperation`. | Added `ScenarioRootKind.ServiceOperation` as the last member (preserves every prior numeric value: `HttpEntryPoint=0, ConfiguredMethod=1, HostedWorker=2, ServiceOperation=3`); mapped explicitly. | `ScenarioRootKindTests` (new, `SeqDoc.Core.Tests`); `CoreWcfServiceModelProjectionTests`/`CoreWcfServiceOperationScenarioTests` root-kind assertions updated | No persistence layer currently serializes `ScenarioGraphSet`/`ScenarioRootKind` (confirmed by repository-wide search), so the cheapest correct compatibility check is the int-stability test, not a round-trip fixture. | None open. |
| `BuildServiceOperationEntries` used `FirstOrDefault` for the matching registration, silently discarding additional matches and their evidence. | Collects every registration matching the exact `(ImplementationTypeSymbol, ServiceContractTypeSymbol)` pair, unions their evidence in stable ID order, and takes the weakest certainty across all of them — one root per exact pair, never one per endpoint. | `CoreWcfServiceOperationScenarioTests.TwoValidRegistrationsForTheSamePairAdmitExactlyOneRootWithUnionedEvidenceDeterministically` (forward and reversed input order) | Exactly one root; both registrations' evidence present; reversing the registration array's order produces byte-identical node/evidence/certainty identities. | No concrete, producible trigger was found for "candidates disagree on required identity" beyond what exact symbol matching already prevents by construction (a mismatched identity never matches, it does not produce a conflicting match), so no ambiguity diagnostic was added for that case. |
| Discovery note (not a review finding, found while wiring the scanner): an `AddServiceEndpoint` call nested inside a lambda argument (the `UseServiceModel`/`AddService` chain) is projected through a *second*, previously-unaudited `ProjectOperationDescriptor` call site in `RoslynCallbackBoundaryFactCollector` (the companion-operation path for anonymous/local callback bodies, which have no accepted extracted Method Flow), not only the main per-method operation walk. The host-chain proof was initially wired only to the main walk, so every registration silently reported unproven. | Threaded the scanner's proof (and the project id needed to resolve typed symbols) into `RoslynCallbackBoundaryFactCollector` too, via an additive `AddHostChainProof`/`CompanionTarget.Project`, merged per project the same way `AddProjectContext` already accumulates other per-project state. | `CoreWcfServiceModelProjectionTests.PassCFixtureCompilationProducesTheExactAddServiceEndpointRegistrationFromStartup` (real fixture, real lambda-nested call) | The real `Startup.Configure` → `UseServiceModel` → `AddService().AddServiceEndpoint()` chain (a lambda-nested invocation) now correctly reports `HostChainProven = true` end to end. | None open. |

Verification after this round: `CoreWcfServiceModelProjection\|InterfaceMemberEligibility` (Analysis.Tests) 10/10, `CoreWcf` (FrameworkModels.Tests) 23/23, `CoreWcfServiceOperation` (Scenarios.Tests) 6/6, `CoreWcfServiceOperation` (Wording.Tests) 1/1, `ScenarioRootKind` (Core.Tests) 2/2. Full-suite regression: FrameworkModels.Tests 254/254, Scenarios.Tests 171/171, Wording.Tests 113/113, Core.Tests 82/82, Analysis.Tests 198/208 (the same 10 pre-existing `SD1102` failures as the prior round, on the same unrelated fixtures, confirmed by identical error signatures).
