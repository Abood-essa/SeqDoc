# CT-5 — Configurable Exact Method Roots

## Purpose

Allow a user to select exact source methods from the existing method catalog as additional scenario roots. This opens
service, engine, worker, recovery, and diagnostic methods without application-name matching and without waiting for
framework entry-point models. CT-5 presents only each selected method's existing root-local Method Flow, exact calls,
predicates, and supported terminal/rejoin topology; callee traversal remains the next boundary.

## Target paths

- `src/SeqDoc.Configuration/ConfigurationResolution.cs`
- `src/SeqDoc.Configuration/YamlConfigurationDocument.cs`
- `src/SeqDoc.Configuration/YamlConfigurationResolver.cs`
- `src/SeqDoc.Cli/CliHost.cs`
- `src/SeqDoc.Cli/AggregateAnalysisBuilder.cs`
- `src/SeqDoc.Core/Identity/IdentityDescriptors.cs`
- `src/SeqDoc.Core/Identity/StableIdentity.cs`
- `src/SeqDoc.Core/Frameworks/AspNetCoreFacts.cs`
- `src/SeqDoc.Core/ScenarioGraph/ScenarioGraphContracts.cs`
- `src/SeqDoc.Analysis.Scenarios/ScenarioGraphBuilder.cs`
- `src/SeqDoc.Application/Documentation/DocumentationPlanner.cs`
- `tests/SeqDoc.Configuration.Tests/YamlConfigurationResolverTests.cs`
- `tests/SeqDoc.Scenarios.Tests/**`
- `tests/SeqDoc.Wording.Tests/**`
- `tests/SeqDoc.Cli.Tests/**`
- `docs/work/credit-transfer/CT-5/**`

## Accepted design

1. Add optional schema-v1 YAML `selection.roots`, a case-sensitive, duplicate-free, canonically sorted list of complete
   `MethodId.Value` strings. Users discover values with the existing `catalog --kind method` output. Names,
   signatures, routes, paths, line numbers, prefixes, globs, and fuzzy matching never select roots.
2. Carry the immutable configured root set through resolved configuration into the aggregate analysis builder. With no
   roots, existing HTTP/Minimal API admission and output remain byte-for-byte unchanged.
3. For one explicitly selected compilation profile/framework, resolve every configured ID against that profile's
   Program Index before candidate activation. Any malformed, unknown, stale, or foreign-profile ID fails the whole
   request as `InvalidInput`; no partial root set, cache activation, or output replacement occurs.
4. Reject `selection.roots` with `--all-frameworks` in this checkpoint rather than guessing which profile owns each
   root. Multi-profile root routing is a later explicit contract.
5. Configured roots are additive to framework-admitted roots. If the same root method is already admitted by an HTTP
   or Minimal API fact, retain only the existing framework graph; never emit a duplicate generic graph.
6. Add a typed `ConfiguredMethod` root/action discriminator to Scenario Graph. Create its deterministic entry identity
   from compilation profile plus exact method identity. Do not encode a fake route, HTTP verb, controller, client, or
   framework claim in user-facing output.
7. Use the selected `ProgramMethod` compiler evidence and exact typed containing type, member name, and display
   signature for neutral wording and participants: a caller invokes the selected method, and the selected method
   executes. Existing HTTP wording and participants remain unchanged.
8. For a configured root with an analyzable Method Flow, reuse CT-3/CT-4 root-local exact call projection, source order,
   predicate/arm membership, supported fragments, evidence, certainty, and deterministic identities. Do not attempt
   controller-to-service DI composition or emit `SC001` for a generic method root.
9. A selected source method without an analyzable body still produces one explicit diagnostic-only generic graph that
   identifies the unavailable body; it never invents calls or behavior.

## Non-goals

- No callee-body traversal, recursion, depth/node budgets, cross-project body loading, RTA, or call-chain composition.
- No name/signature/path-based selector, automatic root discovery beyond existing framework entries, or application
  vocabulary.
- No generic root routing under `--all-frameworks`, profile merging, or target-framework fallback.
- No DI resolution, service/client correlation, CoreWCF/WCF, EF6/EDMX, worker semantics, catch/finally/switch/loop
  presentation, state-machine inference, or outcome propagation.
- No persistence of Scenario Graph/Diagram Plan and no renderer changes.
- Existing inert `selection.include`, `exclude`, and `critical` semantics remain unchanged.

## Risk inventory

1. A short name, signature, prefix, overload, or foreign-profile method could select the wrong root.
2. One missing root could be silently ignored while a partial candidate activates or replaces previous output.
3. Configured roots could be duplicated, ordered by YAML input/hash iteration, or collide with an admitted HTTP root.
4. Generic roots could be mislabeled as HTTP/controller/service behavior or inherit an irrelevant `SC001` diagnostic.
5. A body-unavailable method could be presented as analyzed, while a valid source method could be withheld silently.
6. Root-local calls could lose source order, branch polarity, evidence/certainty, or conservative material filtering.
7. Adding root discriminators or constructor defaults could change existing graph identity, wording, diagrams, or
   serialized contracts when no roots are configured.
8. `--all-frameworks` could cross-contaminate profiles or make output depend on profile scheduling.
9. Invalid configuration could alter the active cache or previously valid generated documentation.

## Existing relevant coverage

- Program Index exposes deterministic exact `MethodId`, containing type, method name, display signature, parameters,
  evidence, and profile-scoped identity; method catalog output already prints IDs and signatures.
- YAML tests cover schema-v1 strict fields, unique string sets, malformed values, named profiles, and provenance.
- Pass A activates profile candidates atomically and persistence tests cover previous-valid-state behavior on failure.
- CT-3 tests cover exact root-local call admission, source order, overload identity, material negatives, deterministic
  participants, and retained control membership.
- CT-4 tests cover plain-Try branch presentation, exception-boundary rejection, and Method Flow through Diagram Plan
  placement without `DP002`.
- CLI tests cover exact/ambiguous output entry selection, invalid input, output preservation, profile/framework options,
  catalog output, and deterministic documentation generation.

## Soft test budget

At most twelve distinct claims across configuration, Scenario/planner, and CLI tests: parse/provenance and duplicate
rejection; exact ID/overload safety; malformed/unknown/foreign rejection; `--all-frameworks` rejection; deterministic
multi-root order; HTTP-root de-duplication; neutral graph/wording/participants; root-local guarded-call reuse; explicit
body-unavailable degradation; no-root backward compatibility; and previous cache/output preservation. Use one
integration claim for the complete valid pipeline and equivalence partitions for invalid IDs.

## Focused verification command

```powershell
dotnet test SeqDoc.slnx -c Release --filter "FullyQualifiedName~Configured|FullyQualifiedName~SelectionRoots|FullyQualifiedName~YamlConfigurationResolver|FullyQualifiedName~DocumentationPlanner"
```

## Final gate

```powershell
dotnet test tests/SeqDoc.Configuration.Tests/SeqDoc.Configuration.Tests.csproj -c Release && dotnet test tests/SeqDoc.Scenarios.Tests/SeqDoc.Scenarios.Tests.csproj -c Release && dotnet test tests/SeqDoc.Wording.Tests/SeqDoc.Wording.Tests.csproj -c Release && dotnet test tests/SeqDoc.Cli.Tests/SeqDoc.Cli.Tests.csproj -c Release
```

After the final gate, use `catalog --kind method` on the disposable CreditTransfer checkout to select exact engine and
worker methods, then regenerate the first non-HTTP-root diagrams when local command policy permits CLI execution.
