# Delegated Contribution Workflow

Delegated changes remain candidates until the maintainer verifies their complete behavior. Preserve the submitted
branch, record its base revision, inspect the actual diff, and classify each area as accepted, repairable, or rejected.
Return bounded findings to human contributors with file/line evidence, risk, expected behavior, and one focused
verification command; review only the repair delta plus affected risks when they return. After two unsuccessful repair
rounds, reject, split, or explicitly take ownership of the repair.

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
