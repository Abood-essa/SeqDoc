# Collaborator and owner setup runbook

No changes are performed by this document. Follow it only after the owner and applicable decision records authorize
adoption. The rules are in [collaboration-model.md](collaboration-model.md).

## Owner UI/API checklist

1. Invite `AhmadKrarha`, `Qhatahet`, and `Abood-essa` as personal-repository collaborators; do not look for a Write or
   Maintain selector. Verify each accepted invitation and can open a fork, branch, and draft PR.
2. Ask each account to confirm 2FA and recovery methods, fork/upstream setup, and the access acceptance sentence:
   **"I accept collaborator access to Bilaltariq41/SeqDoc, will use a fork and PR, protect credentials, preserve the
   frozen contract/allowlist, and record review receipts against exact SHAs."** Never place tokens in chat or files.
3. Propose CODEOWNERS: default all four accounts (or the three collaborators plus Bilal); `/.github/CODEOWNERS` only
   `@Bilaltariq41`; workflows, `AGENTS.md`, `docs/project/`, Core/identity, ScenarioGraphBuilder,
   DocumentationPlanner, extractor/model host, and CLI/release paths list all three collaborators plus Bilal. GitHub's
   any-one semantics mean one listed owner satisfies code-owner approval; T3 policy may require both peers.
4. Prepare one equivalent `main` ruleset: one peer approval, dismiss stale, require latest reviewable push approval,
   require code-owner review, require conversation resolution, require strict `validate` now. The current
    [`work-state.yml`](../../.github/workflows/work-state.yml) runs `validate` on every `pull_request`, making it
   suitable as the initial required check. Add `review-contract` only after stable; block force pushes/deletion; leave
   linear history off to preserve merge commits and attribution; defer signed commits; keep auto-merge off until
   `review-contract` exists. Give Repository admins (only Bilaltariq41) PR-only bypass. Direct pushes remain blocked.
5. Create and evaluate the ruleset first, inspect effective combined rules through settings/API read-only evidence, then
   activate it and retire the overlapping classic branch-protection rule. The 2026-09-03 audit found that the classic
   rule permitted admin bypass because administrator enforcement was false; do not present that dated finding as current
   state. A direct emergency requires Bilaltariq41 to deliberately amend the ruleset bypass to `Always`, post
   `OWNER-BYPASS v1`, restore PR-only, and audit the change.
6. Harden Actions: require full-SHA pins, protect workflow paths with CODEOWNERS, keep workflow approval disabled,
   least-privilege read token, hosted ephemeral runners, and no public self-hosted runner or `pull_request_target`.
7. Record a T4 decision once the mandatory human-peer/Reviewer receipt is adopted: disable Copilot `review_on_push`
   (recommended to reduce duplicate noise and compute), or explicitly retain it as supplemental cost. The current Copilot
   ruleset is supplemental and is not the independent Reviewer receipt; do not change it in this planning package.
8. Use the published issue/PR templates. They capture planning IDs, leases, receipts, SHAs, observables, and gates.

## Clone and fork setup

```powershell
git clone https://github.com/<account>/SeqDoc.git
cd SeqDoc
git remote add upstream https://github.com/Bilaltariq41/SeqDoc.git
git fetch upstream
git switch --create <checkpoint-branch> upstream/main
```

Use a fork PR to upstream; never store credentials in remotes, command history, chat, or repository files.

## Disposable smoke test and preflight

**A - unmerged probe PR.** Each collaborator opens a real disposable fork PR with a narrowly scoped probe. Verify
effective rules and bypass actors using settings/API read-only evidence. Prove `validate` runs on the `pull_request` and,
once adopted, `review-contract` also runs, with no repository secrets and a read-only token. A non-author human peer
invokes the Reviewer agent and posts an authenticated receipt containing the peer actor plus agent identity/version,
invocation, base/head, scope, findings/output hash/link, dispositions, and gates. Exercise stale approval after a new
push and a CODEOWNERS self-change PR that remains blocked. Close the probe PR unmerged.

**B - legitimate adoption rehearsal.** Use the first legitimate G-0/G-5 governance adoption PR as the compliant
collaborator merge rehearsal. Apply the normal peer review, receipt, checks, conversation resolution, and merge sequence;
do not merge no-op or junk documentation. Do not attempt a direct-main push or bypass dry run. Secret-using jobs are
trusted post-merge only, and no `pull_request_target` workaround is permitted.

## Rollback, offboarding, and emergency contact

Bilaltariq41 records the exact change, reason, affected paths, and follow-up before reverting permissions, protection,
rulesets, CODEOWNERS, or invitations. Revoke collaborator access, review open PRs and leases, rotate affected secrets
through the owner-controlled mechanism, and preserve receipts and attribution. A collaborator reports suspected
compromise or unsafe merge through the repository issue/PR and directly to `@Bilaltariq41`; they must not bypass rules or
delete evidence. Emergency changes use `OWNER-BYPASS v1` with exact SHA, risk, reason, and follow-up, then applicable
review and gate evidence.
