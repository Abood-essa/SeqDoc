# Issue readiness policy

The typed record under `docs/project/work-items/` is the sole current-state authority. Issue bodies remain the
specification authority. GitHub labels, `execution.json`, status, parallel topology, and checkpoint state are generated
projections or historical explanation; none is manual lifecycle authority.

## Readiness audit

Before assignment, a maintainer records `readiness audit: pass` and verifies the complete issue contract, work kind,
owner, track, dependencies, exact target-path allowlist, clean baseline revision, first consumer, focused command, final
gate, non-goals, and stop conditions. Open dependencies, missing decisions, or incomplete proof keep the record
`Blocked`. A `Ready` record permits the named contributor to branch from the frozen baseline; `Active` means work has
started. Only the selected record authorizes the local Orchestrator.

The audit must preserve the applicable proof below:

* **Semantic:** complete the semantic-delivery brief with exact operation/type identity, assembly and overload
  admission, registration and callback mapping, supported and unsupported forms, negative lookalikes, evidence chain,
  joins, certainty, placement, and the first user-visible or persisted consumer. Establish a clean-baseline red producer
  signature and candidate green observable assertion.
* **Acceptance-only:** link the frozen semantic contract and name the exact external revision, profile, configuration,
  root, baseline diagnostic/artifact, candidate artifact matrix, hashes or links, Mermaid output, and repeat-byte
  evidence. This substitutes for semantic producer proof but never permits `src/**` or contract repairs.
* **Mechanical:** state the exact invariant and allowed paths, provide a reproducible defect or explicit no-behavior-
  change evidence, and name focused verification. No semantic brief is required unless semantics change.

## Assignment, freeze, and amendment

Assignment freezes the contract revision, baseline, dependencies, allowlist, negatives, and proof rows. An amendment
requires explicit owner approval, a new contract or brief revision, a written reason, affected paths/tests/baseline
impact, and a repeated readiness audit. No contributor or agent may infer an amendment from a label, projection, or
checkpoint edit. The target-path allowlist is strict: build files, SDK selection, CLI behavior, public contracts, and
unrelated cleanup require separate authority.

## Complete delivery and review

The smallest acceptable unit is a complete producer-to-observable vertical slice. Real production input must reach the
typed producer and its first observable assertion; hand-built intermediate facts are supplementary, not completion
evidence. Claims may only preserve or weaken compiler evidence, and identity, profile, snapshot, chronology, guards,
boundaries, and deterministic ordering remain explicit.

Use the review epochs and repair limits in [collaboration-model.md](collaboration-model.md). DGP1 and the grandfathered
I13/P17-R1/QHTTP-B work retain their frozen one-review rules; new work adopts the prospective epochs only after policy
activation. After two failed repair rounds, block and preserve the work; any split, transfer, or takeover is a separately
authorized next checkpoint or decision.

## Status transitions and acceptance boundary

The lifecycle is `Draft` → `Blocked` → `Ready` → `Active` → `ReviewRequired` → `ResolvingFindings` → `Verifying` →
`Closed`, with only explicitly legal transitions and `Cancelled`/`Blocked` stop paths. Use
`tools/governance/work_state.py transition`; do not hand-edit projections. Lifecycle labels are remote projections and
generated execution is a local projection.

When closing or otherwise making the selected item unselectable, use `transition --id <target> --state <state>` to leave
the Orchestrator idle, or add `--select-id <existing-item>` for an atomic handoff. The recipient must already be
selectable and complete; do not hand-edit selection or lifecycle fields.

Acceptance-only work proves an already-approved contract against an exact external checkout and configuration. It may
prepare fixtures and inspect artifacts, but may not repair production semantics or rewrite the semantic brief. Any gap
becomes a separately approved issue or amendment. Closure records the baseline, exact commands/results, observable or
invariant evidence, remaining boundaries, approved amendments, and passing declared gates.
