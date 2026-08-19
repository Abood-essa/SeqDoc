# Delegated Candidate Integration Review

## Outcome

An externally produced candidate implemented participant cleanup, logging suppression, placeholder removal, direct-call
compatibility, constant arguments, and cross-project traversal. Its original branch remains local and unchanged. The
candidate was hardened on an isolated branch and only the verified final tree is eligible for publication.

Delegation was worthwhile: the incompatible body-fingerprint comparison was diagnosed correctly, traversal and
argument plumbing were reusable, and CreditTransfer proved materially deeper output. Rewriting those components would
have discarded useful work. However, the submitted quality was below SeqDoc's merge standard and required substantial
maintainer work before it was acceptable: semantic contracts were unsafe, visual output was broken, test evidence was
incomplete, and execution documentation overstated completion. The candidate was not mergeable without intervention.

## Reused

- configured-root caller removal and self-participant reuse;
- exact built-in logging recognition;
- conservative unsupported-predicate withholding;
- removal of the incompatible source-text/behavior fingerprint equality;
- bounded cross-project direct-call traversal scaffolding;
- constant-argument flow and presentation plumbing;
- several focused fixtures and regression tests.

## Required repairs

- replaced misuse of flow-selection exclusions with dedicated documentation participant/call exclusions;
- filtered interactions before participant and fragment construction to prevent orphan logger actors;
- added structural-root validation, canonical pattern validation, deterministic counts, and filtered evidence;
- removed generic control labels from planner, renderer, validator, and legacy fixtures;
- preserved concise collision-safe labels and exact canonical identities separately;
- added the missing HTTP action-member producer fact;
- restored exact loaded-source/generated-source boundaries and unique target-flow diagnostics;
- preserved Roslyn parameter ordinals, typed null, contiguous all-or-nothing argument meaning, escaping, and credential
  suppression;
- replaced unsupported execution/review claims with canonical checkpoint state and independent findings;
- restored fixture dependencies and updated only deterministic golden fingerprints affected by the additive contracts.

## Verification and acceptance

- CT-8 final gate: 47 Configuration, 105 Wording, 45 Rendering, and 19 CLI tests.
- CT-9 final evidence: 181 Analysis, 63 Behavior, 149 Scenario, 105 Wording, and 25 locally available Acceptance tests;
  four external-corpus tests were unavailable because their repositories are not installed.
- CT-10 final gate: 150 Scenario and 110 Wording tests.
- CT-11 final gate: 54 Rendering tests plus real Mermaid CLI SVG rendering for all eight diagrams and PNG visual
  inspection of the main engine flow.
- Fresh CreditTransfer: eight diagrams, 63 messages, zero logging messages/participants, zero generic control
  placeholders, zero direct mismatch/cross-project stops, successful concise HTTP/configured-root labels, and no
  collapsed empty-fragment or visible-alias-quote layout defect.

The exercise validates the workflow in `delegated-contribution-workflow.md`: preserve submissions, return bounded
findings to available contributors, repair reusable code in place when necessary, verify realistic output, and publish
only clean ordered integration commits. The retained implementation saved work, but review and repair were still
material; future delegations should receive narrower contracts and realistic rendering/acceptance requirements before
implementation begins.
