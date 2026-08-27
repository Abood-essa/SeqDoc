---
description: Run the pinned independent Reviewer once against a completed checkpoint. Pass the checkpoint id.
agent: review
subtask: true
---

# Review Request

Independently review checkpoint `$1` of SeqDoc.

Read `docs/project/execution.json` and locate the checkpoint under `docs/work/`. Use its capsule, state, baseline revision, actual candidate diff, non-goals, and verification evidence. Do not trust the builder's summary.

Report prioritized findings with stable ids (`$1-F<n>`), then open questions and residual testing risks. Do not edit files.

Additional scope from the caller:

$ARGUMENTS
