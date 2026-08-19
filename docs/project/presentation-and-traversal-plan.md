# Presentation and Traversal Plan

## Status and evidence

This plan records priorities accepted after reviewing generated CreditTransfer output. CT-8 through CT-10 complete
the immediate presentation and direct-traversal hardening: exact configurable exclusions, concise participants,
placeholder removal, compatible same-profile cross-project traversal, and safe constant-argument labels.

The hardened external suite contains eight diagrams and 63 Mermaid messages. It has no logging messages or logger
participant, no generic `Condition`/`Continue`/`Path terminates` labels, no `SC-DIRECT-MISMATCH` or cross-project stop,
and concise configured and HTTP root labels. Exact arguments distinguish supported repeated calls. Remaining limits
are explicit depth/node/guard/body boundaries, sparse data-oriented roots, and framework semantics not yet modeled.

## Immediate resume order

### 1. Cheap participant hygiene — complete

Bound this work tightly to changes that are likely to take no more than one focused checkpoint:

- remove the invented configured-root `Caller` and begin at the selected method;
- use concise deterministic root/type/member labels instead of full signatures;
- reuse the root participant for exact self-calls instead of creating a duplicate participant;
- preserve full identities in behavior/evidence details.

Do not pursue perfect shortest-unique naming, configurable abbreviations, or general line wrapping now. Assess Mermaid
semantic wrapping only if concise labels remain unreadable; defer it if renderer/validator behavior makes it more than
a small mechanical repair.

### 2. Remove low-signal presentation — complete

- Hide recognized logging-framework calls by default.
- Support exact configured exclusions for custom logging wrappers without hardcoding application vocabulary.
- Retain filtered facts in analysis/coverage evidence so presentation filtering is auditable.
- Never render generic `Condition`, `Continue`, `Continue evaluating condition`, or `Path terminates` labels as useful
  behavior. Present an exact compiler-evidenced predicate/return/throw/outcome or withhold the fragment/note and retain
  the unsupported boundary in technical fallback.

This precedes broader traversal so new messages do not amplify current noise.

### 3. Diagnose and repair `SC-DIRECT-MISMATCH` — complete

First distinguish no matching Method Flow, multiple matching flows, and body-fingerprint disagreement. Compare exact
`MethodId`, active profile, Program Index fingerprint, Program Method body fingerprint, and candidate Method Flow
fingerprints for representative external calls. Then either unify the shared body-fingerprint producer contract or,
if the fingerprints intentionally represent different artifacts, define a conservative compatibility contract using
the active snapshot, exact method identity, unique flow, and body availability. Never simply remove the check.

Expected effort is easy-to-medium: hours for diagnosis and roughly one to three focused days for the likely systemic
repair, with a larger three-to-five-day bound only if async/partial/generated method identity is involved.

### 4. Regenerate and measure fixed depth 2 — complete

Keep the current depth-2/64-node policy until mismatch repair reveals how much behavior it actually exposes. Compare
message count, useful participants, inherited guards, mismatch/boundary diagnostics, and diagram readability on
CreditTransfer and at least one unrelated project. Do not increase depth merely because changing the constant is easy.

### 5. Add exact call-argument presentation — complete for safe constants

Project compiler-proven constant, parameter, enum, and safe expression arguments so repeated calls such as `GetItem`
are distinguishable. Collapse only adjacent calls proven semantically identical; never collapse by member name alone.
Keep runtime values, secrets, and unsupported expressions unknown.

### 6. Add compatible cross-project traversal — complete for loaded exact source

Follow exact source bodies across project references only when profile, target framework, Program Index snapshot,
method identity, body compatibility, and source availability agree. Keep generated clients and network/service
boundaries separate until exact contract evidence connects them.

## Medium term

- Make depth/node budgets configurable or adaptive after fixed-depth evidence is useful.
- Compose per-method nested branch topology instead of withholding locally guarded child calls.
- Generate linked overview and child diagrams rather than one unrestricted call graph.
- Add generic CoreWCF/WCF contracts, operations, clients, faults, and response behavior.
- Add EF6/EDMX query, mutation, save, and persisted-state facts.
- Add worker lifecycle, polling, retry, batch-abort, and recovery behavior.
- Add outbound SOAP/HTTP boundaries and exact caller-visible outcomes.
- Add only narrow evidence-backed DI composition needed to connect exact implementations.
- Classify every admitted root as useful/complete, partial, diagnostic-only, or uncovered.

## Long term

- Link roots through exact contract/state evidence without pretending network boundaries are in-process calls.
- Derive state-machine views from enum comparisons, persisted assignments, query predicates, retry counters, and
  terminal outcomes.
- Generate deterministic solution/process context and coverage views for large repositories.
- Broaden modern HTTP, dispatch, service-contract, worker/message, persistence, and boundary models from measured gaps.
- Continuously run unrelated supplied and open-source applications as generalization and false-positive gates.

## Very long term

- Persist later graph stages only after their contracts survive varied projects.
- Add incremental invalidation and changed-flow regeneration.
- Add caller/callee/evidence search and explanation surfaces.
- Automate broad corpus, performance, platform, packaging, and release lanes.
- Add configurable terminology, refined natural language, and consistent visual themes after semantic usefulness.

## Depth policy

Raising a constant is easy; safe deep traversal is not. Unrestricted recursion can create cycles, repeated shared
callees, thousands of calls, false unconditional placement, and unreadable diagrams. The intended evolution is:

1. repair body/flow compatibility;
2. measure depth 2;
3. trial depth 3 under the existing node budget if evidence warrants it;
4. add configurable/adaptive budgets; and
5. decompose large behavior into linked diagrams.

Robust deep, cross-project, branch-aware traversal is a medium-term capability, not an immediate constant change.

## Resume gate

The owner paused product work after CT-7 because of model usage limits. Do not activate a new checkpoint until the
owner explicitly resumes SeqDoc work. On resume, re-read durable state, verify `main` still contains `14d0e13`, inspect
the worktree, and activate only the first immediate item that remains unfinished.
