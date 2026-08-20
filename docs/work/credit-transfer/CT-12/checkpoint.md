# CT-12 — Team Onboarding and External Corpus Resolution

## Purpose

Make SeqDoc reproducible for teammates and their coding agents: document the exact CreditTransfer command/configuration,
resolve supplied projects from the standard sibling repository, establish one tool-neutral contribution workflow, and
publish an executable parallel roadmap without copying supplied source into the public repository.

## Target paths

- `tests/SeqDoc.Testing/**`
- `tests/SeqDoc.AcceptanceTests/**`
- `tests/SeqDoc.Analysis.Tests/**`
- `docs/usage.md`
- `docs/examples/**`
- `docs/project/**`
- `docs/roadmap.md`
- `docs/contributing.md`
- `README.md`
- `AGENTS.md`
- `CLAUDE.md`
- `.gemini/**`
- `.github/**`
- `docs/work/credit-transfer/CT-12/**`

## Accepted design

1. External tests resolve `SEQDOC_TEST_PROJECTS_ROOT` first, otherwise the sibling `../SeqDoc-TestProjects`, always
   relative to the discovered SeqDoc repository root rather than process working directory.
2. `Provided` and `OpenSource` are explicit corpus groups. Absence of the entire external root produces an explicit
   test skip; an installed corpus missing an expected project/file fails with the exact expected path.
3. Migrate existing `samples/Provided` assumptions to one shared test-only resolver. Do not copy external source,
   configuration, build outputs, or credentials into SeqDoc.
4. `docs/usage.md` contains prerequisites, build/catalog/analyze commands, argument explanations, troubleshooting, and
   the exact verified CreditTransfer invocation. `docs/examples/credit-transfer.yaml` contains the six exact roots and
   custom logger exclusion that reproduce the current eight-diagram hardened suite from the sister-folder checkout.
5. One concise public `AGENTS.md` is canonical for project-wide agent behavior. `CLAUDE.md` imports it; Gemini is
   configured to load it. Detailed procedures live in project docs so always-loaded context remains small.
6. Contributors work from forks. Parent workstream issues provide meaningful ownership; sub-issues and dependencies
   create reviewable PRs. The contributor/agent plans, implements, tests, inspects the actual diff, self-reviews, and
   repairs review findings on the same PR before merge. Canonical roadmap/status documentation remains maintainer-owned.
7. Public planning may name CreditTransfer, FraudManagement, SMSGateway, TicketReservation, and the training corpus,
   but does not publish proprietary source, configuration values, or detailed behavioral findings.
8. The roadmap prioritizes essential functionality: depthless cycle-safe traversal under Mermaid/output budgets,
   nested topology, framework semantics, whole-solution composition, and useful acceptance diagrams. CI, packaging,
   broad platform matrices, and nonessential operational hardening are not near-term work.

## Non-goals

- No product traversal/framework implementation, CI workflow, packaging/release automation, collaborator write access,
  or external-project source changes.
- No machine-specific absolute path in public usage instructions.

## Risks and existing coverage

- Current external tests silently pass or fail from `samples/Provided`; working-directory dependence masks missing
  acceptance coverage.
- Environment overrides are process-global and test isolation must remain deterministic.
- Exact CreditTransfer MethodIds must remain checkout-path independent; the command has already produced byte-identical
  output from the standard sister checkout.
- Agent instructions can consume context or conflict across tools; keep one canonical concise source.

Soft budget: at most ten path-resolution claims plus migration of existing external acceptance tests. Documentation is
verified by executing the published command and comparing all generated files to the hardened baseline.

## Focused command

```powershell
dotnet test tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release --filter "FullyQualifiedName~ExternalTestProjects|FullyQualifiedName~ProgramIndexBuilder" && dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter "FullyQualifiedName~ExternalTestProjects|FullyQualifiedName~Corpus|FullyQualifiedName~BehaviorDocumentation"
```

## Final gate

```powershell
dotnet test tests/SeqDoc.Analysis.Tests/SeqDoc.Analysis.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~ExternalCorpusResolverTests|FullyQualifiedName~ProgramIndexBuilderTests.TicketReservation" && dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --no-build --no-restore
```

Then execute the documented CreditTransfer command from a clean repository root and compare all 18 generated files to
the accepted hardened output.
