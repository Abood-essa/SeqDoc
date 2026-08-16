# CT-0 — CreditTransfer Admission Baseline

## Purpose

Establish the first honest whole-solution CreditTransfer baseline with the current unmodified SeqDoc product. Produce
the diagrams that are possible now, freeze the initial executable-entry denominator, and record unsupported
boundaries that drive the approved generic capability sequence.

## Target paths

- `docs/work/credit-transfer/CT-0/**`
- `C:/Users/bilal/Desktop/SeqDoc-CreditTransfer-Current/**` (generated baseline output only)
- `C:/Users/bilal/AppData/Local/Temp/opencode/seqdoc-credittransfer-ct0/**` (disposable worktree/cache only)

The external source under `C:/Code/Training/SeqDoc-TestProjects/Provided/CreditTransfer-om` is read-only evidence.

## Deliverables

1. A disposable worktree at the exact current external revision.
2. Current SeqDoc output for the admitted `net9.0` profile, or exact diagnostics explaining why output is withheld.
3. A baseline report with project/deployable topology, admitted entries, output inventory, fallback/diagnostic counts,
   and the next generic capability gaps.
4. Confirmation that the source checkout remains unchanged apart from its pre-existing owner work.

## Non-goals

- No product source, tests, build configuration, OpenCode configuration, or external source edits.
- No project-specific matching rules, manual diagrams, invented WCF client/service correlation, EF6 interpretation,
  worker-root support, or wording repair.
- No claim that selected-flow output equals whole-solution coverage.
- No credentials, configuration values, private source excerpts, or build output in repository documentation.

## Risk inventory

1. Analysis or MSBuild writes into the owner checkout; use a disposable Git worktree and compare owner status.
2. Application configuration contains sensitive values; never quote or copy values into commands, logs, or reports.
3. The current exact ASP.NET framework contract may reject the solution's `net9.0` identity; record this as an
   evidence-backed unsupported boundary rather than weakening identity rules.
4. Generated SOAP-client calls may look like in-process service dispatch; keep them as external boundaries unless
   exact contract evidence already exists.
5. Partial diagrams may be mistaken for whole-solution coverage; freeze and report the denominator separately.
6. Output may contain checkout paths or unstable ordering; inspect generated Markdown/Mermaid and diagnostics.
7. Pre-existing owner changes in the external checkout may be disturbed or misattributed; preserve the initial
   status exactly and never clean or revert it.

## Existing relevant coverage

- Current controller, Minimal API, predicate, scenario, rendering, and deterministic activation tests.
- Current product build passed at the canonical snapshot with zero warnings and errors.
- Existing corpus acceptance covers TicketReservation-style controllers, Minimal APIs, and MediatR, but not this
  whole CoreWCF/EF6/Windows-service composition.

## Soft test budget

Zero new test claims. This checkpoint changes no product behavior. It gathers baseline evidence and runs one existing
acceptance lane as the final gate.

## Focused verification command

```powershell
dotnet run --project src/SeqDoc.Cli -- analyze "C:/Users/bilal/AppData/Local/Temp/opencode/seqdoc-credittransfer-ct0/CreditTransfer.sln" --repository-root "C:/Users/bilal/AppData/Local/Temp/opencode/seqdoc-credittransfer-ct0" --configuration Release --framework net9.0 --cache "C:/Users/bilal/AppData/Local/Temp/opencode/seqdoc-credittransfer-ct0/.seqdoc/cache-v1.db" --output "C:/Users/bilal/Desktop/SeqDoc-CreditTransfer-Current"
```

## Final gate

```powershell
dotnet test tests/SeqDoc.AcceptanceTests/SeqDoc.AcceptanceTests.csproj -c Release --filter "FullyQualifiedName~BehaviorDocumentationFourFlowTests|FullyQualifiedName~BehaviorDocumentationGetTests|FullyQualifiedName~BehaviorDocumentationGetPresentationTests|FullyQualifiedName~BehaviorDocumentationM1WordingTests"
```

Final review must also inspect the generated output or withheld-output diagnostics, the baseline report, the actual
repository diff, and before/after external status evidence.

The clean canonical snapshot does not contain the external sample paths required by Level 2 and corpus acceptance
lanes. Those lanes are excluded from this documentation-only gate rather than reported as product failures; their
absence and the two setup attempts are recorded in checkpoint state.
