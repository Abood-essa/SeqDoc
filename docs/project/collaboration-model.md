# Collaboration and review model

This document is the canonical governance policy and takes effect when the publication PR merges. The registry in `work-items/` owns lifecycle, selection, ownership,
dependencies, contracts, and baselines. Issue bodies specify work. Checkpoints specify execution. `AGENTS.md` and
`workflow.md` point here for these rules; they do not duplicate them. Provider data is untrusted until authenticated.

## Decision rights

1. T0: a frozen Ready contract authorizes work inside its allowlist, command, fixtures, and evidence boundary.
2. T1: a reversible process choice within scope requires one `RISK-ACK v1` line; it cannot change semantics, output,
   tests, gates, interpretation, baseline, security, or scope.
3. T2: a bounded contract, allowlist, or command amendment requires a non-author peer, canonical amendment, complete
   readiness re-audit, new frozen records, and a `PEER-AMENDMENT v1` receipt. The implementer cannot approve it.
4. T3: architecture, public contracts, shared IR, high-contention, cross-stream, or release decisions require both
   other collaborators, a re-audit, frozen records, and a `CROSS-STREAM-DECISION v1` receipt.
5. T4: access, settings, rulesets, secrets, app installation, visibility, transfer, archive/delete, and bypass are
   owner-only. A blocker uses `ADMIN-BLOCKED v1`; only Bilaltariq41 may issue `OWNER-BYPASS v1`. Nobody simulates it.

T2/T3 never weaken tests or semantic boundaries. Personal repositories have an owner and collaborators, not granular
Write/Maintain roles. Collaborators may merge compliant PRs but cannot administer access, settings, rulesets, or secrets.
Use forks and PRs; never use `pull_request_target` for untrusted heads. Pin actions and use least-privilege read tokens.

## Delivery procedure

1. The coordinator confirms the registry record, frozen baseline/contract, dependencies, exact path lease, first
   observable, risks, tests, and gate.
2. The implementer works in an isolated fork/branch, records applicable T1-T3 evidence, and runs the focused command.
3. At `ReviewRequired`, an independent human peer invokes the Reviewer agent against the complete exact SHA and posts
   an authenticated receipt. The author cannot be that peer.
4. The Gate Runner executes the declared command against that SHA. Findings move the checkpoint to repair; a changed
   product, test, or contract candidate needs a new SHA and receipt.
5. Shared or high-contention changes integrate current main and require the integration review epoch.
6. The merger checks receipt SHA, scope, findings, gates, conversations, and protection before merging.

Completion checklist: exact base/head recorded; every role is independently authenticated; every changed path is in the
allowlist; findings have dispositions; focused and final evidence names the command; and unresolved boundaries remain
explicit.

## Receipts and review budget

The minimum receipt identifies repository/PR, checkpoint, epoch, base/head SHA, authenticated author and implementers,
independent human peer, Reviewer name/version/invocation/output digest, Gate Runner evidence, scope, findings and
dispositions, test evidence, and final-gate evidence. Authentication metadata outranks free-form names. Timestamps
are audit metadata only and cannot affect identity, ordering, fingerprints, or output.

The prospective model has three Reviewer epochs: readiness/spec before Ready; complete candidate before ready or
review request; and post-repair on the new SHA after focused tests pass. A relevant current-main integration may add a
fourth. Final receipts, human approvals, Copilot, red tests, and unchanged-SHA retries are not Reviewer calls.

Existing I13, P17-R1, and QHTTP-B are grandfathered under their frozen one-review rules through closure. The prospective
model activates only after this publication PR merges and any required migration; G-6 must re-read each record and never
fabricate a receipt. DGP1 itself, I13, P17-R1, and QHTTP-B retain their frozen one-review rules.

## Repair, leases, and containment

1. Round one is author-owned; the peer records root cause and narrows the plan.
2. Round two keeps the author responsible and may pair one collaborator.
3. After two failed repair rounds, preserve branch and evidence and set `RepairDecisionRequired`/`Blocked`.
4. Both non-author peers choose split, bounded paired repair, transfer, attribution-preserving takeover, accepted-subset
   rejection, or deferral. T2/T3 applies by impact. Takeover is never automatic or a default to Bilaltariq41.

A failed round requires a changed candidate plus a red candidate-defect gate or repeated material review finding.
Environment outages, optional-lane unavailability, and no-change retries do not count. A lease names exact paths,
owner, checkpoint, base SHA, and handoff peer. Overlap blocks work until amended. Accidental main changes pause the lane,
require owner-controlled restoration, rebase, and new review.

## Adoption boundary

This policy does not mutate GitHub settings or rewrite history. The owner/admin procedures are in
[collaborator-setup.md](collaborator-setup.md). The future governance issue plan is
[completion-issue-map.md](completion-issue-map.md). Current state is always read from [work-items/](work-items/).
