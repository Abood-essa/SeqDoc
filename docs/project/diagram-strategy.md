# Diagram Strategy

The measured implementation order for current presentation and traversal gaps is maintained in
`docs/project/presentation-and-traversal-plan.md`.

## Authority and inspiration

The current buildable CreditTransfer solution is the primary acceptance truth. The historical diagram set numbered
1–12 is design inspiration only: it demonstrates the usefulness, decomposition, chronology, and uncertainty handling
the product should approach, but its application-specific claims must never be copied into current output or product
matching rules. The historical comprehensive diagram numbered 13 is not a primary target.

## Target philosophy

SeqDoc should generate a linked suite of bounded diagrams rather than one universal call-graph dump. Each diagram
answers one technical question and retains links to parent/child views. The generic target hierarchy is:

1. solution/process context and deployable boundaries;
2. entry adapters and external request boundaries;
3. service/facade operations;
4. orchestration and validation flows;
5. persistence, compensation, and state lifecycles;
6. worker, polling, retry, and recovery flows;
7. operation variants; and
8. explicit external, metadata-only, missing, or unresolved boundaries.

Chronology is authoritative. Source order, calls, returns, decisions, loops, throws/catches, compensation, persistence,
and later recovery must remain ordered. `alt`, `opt`, `break`, and `loop` are emitted only from typed, evidence-backed
topology. Failure is first-class behavior: pre-side-effect rejection, partial success, compensation, durable failure
state, caller-visible outcome, and later retry remain distinguishable when proven.

## Presentation contract

- Put the sequence diagram before behavior prose and technical fallback in each generated Markdown document.
- Never show generic `Condition`, `Continue`, `Continue evaluating condition`, or `Path terminates` as if they convey
  useful behavior. Prefer an exact compiler-evidenced predicate/outcome; otherwise withhold the fragment/note and keep
  the unsupported boundary in technical fallback.
- Logging/telemetry is not primary sequence behavior. Hide recognized logging-framework calls by default and support
  exact configured exclusions for custom logging wrappers; retain an auditable diagnostic/coverage record rather than
  silently changing analysis facts.
- Configured method roots do not invent a `Caller` participant. Begin with the selected method and its proven calls
  unless an actual caller is composed later.
- Participant labels use the shortest deterministic unique type/member role. Avoid redundant namespace/type repetition
  and reuse the root participant for exact self-calls instead of creating a duplicate participant.
- Keep complete identities in evidence/behavior details, not necessarily in participant aliases. Wrap long labels at
  deterministic semantic boundaries when Mermaid supports it; otherwise prefer a concise unique alias over a full
  signature containing only parameter types.
- Repeated calls need distinguishing evidence-backed arguments or meaning. Project exact constant/parameter arguments
  when available; collapse only adjacent calls proven semantically identical, never merely because member names match.
- Initial labels may be exact code, type, member, enum, route, status, or predicate text.
- Solid/current, unresolved/external, and inferred/configured relationships must remain visually and semantically
  distinct; missing behavior ends at an explicit boundary.
- Every participant, message, branch, state transition, and qualification retains evidence and certainty.
- Overview diagrams summarize linked child flows instead of repeating their internals.
- Renderers serialize Diagram Plan mechanically; source interpretation stays in compiler/framework analysis.
- Styling, colors, natural-language questions, and business-friendly names are later refinements and never carry the
  only statement of meaning.

## Accelerated capability sequence

### Short term — first useful technical suite

1. Complete compiler-evidenced predicate, arm, early-terminal, and supported rejoin presentation so guarded calls can
   appear safely in `alt`, `opt`, and `break` fragments.
2. Add configurable exact method roots for controller, service operation, engine, worker, and diagnostic methods.
3. Add bounded `DirectExact` source-call traversal with cycle, depth, node, profile, and source-availability budgets.
4. Follow compatible source bodies across project references without mixing profiles or target frameworks.
5. Regenerate a first technical suite: context, HTTP adapter, service facade, main engine flow, recovery worker, and
   explicit missing/external boundaries.

### Medium term — useful whole-solution behavior

1. Add generic CoreWCF/WCF contract, service-operation, generated-client, and fault/response interpretation.
2. Add EF6/EDMX context, query, mutation, save, and persisted-state facts.
3. Add worker lifecycle, polling, retry, batch-abort, and recovery-state projection.
4. Add outbound SOAP/HTTP boundaries and exact response/outcome propagation.
5. Add narrow conventional DI only where exact registration and constructor evidence compose source calls.
6. Add automatic overview/child decomposition from call depth, topology complexity, participant boundaries, repeated
   subflows, and state transitions.
7. Account for every admitted root as complete/useful, partial, diagnostic-only, or uncovered.

### Long term — generic repository-scale explanation

1. Link related roots through exact contract/state evidence without pretending network calls are in-process calls.
2. Derive evidence-backed state-machine views from enum comparisons, persisted assignments, query predicates,
   retry counters, and terminal outcomes.
3. Broaden modern HTTP, dispatch, worker/message, service-contract, persistence, and external-boundary models from
   measured corpus gaps.
4. Continuously run unrelated supplied and open-source applications as generalization and false-positive gates.
5. Add deterministic solution-level context and coverage reports that scale to large repositories.

### Very long term — production scale and refinement

Persist later graph stages only after their contracts survive varied projects. Then add incremental invalidation,
changed-flow regeneration, explanation/search, caller/callee/evidence queries, corpus automation, performance and
platform lanes, packaging/release hardening, configurable terminology, natural-language refinement, and consistent
visual themes.

## Never infer

- Callee behavior from an invocation whose source body was not traversed.
- DI implementation from names, nearby registrations, or first-candidate selection.
- Network topology from project/type/proxy names or endpoint strings alone.
- Runtime deployment, configuration values, authentication success, or external availability from static source.
- External response content, status meaning, atomicity, compensation success, or persistence effect without facts.
- Worker chronology, retry count, eventual success, or concurrency from a loop or method name alone.
- Missing legacy implementations or state transitions.
- Success, failure, terminal, or rejoin meaning when control topology is ambiguous.

## Delivery discipline

Optimize for visible semantic progress: no premature prose/theme checkpoint, no exhaustive framework emulation, no
duplicate tests, and no giant diagram milestone. Keep one semantic boundary per checkpoint, but approve bounded
sequences and regenerate the primary target at every user-visible step. Push only reviewed, proportionately verified,
stable points.

## Current acceptance feedback

The first CT-4/CT-5/CT-6 CreditTransfer suite proves substantial semantic progress but is not yet presentation-ready.
Eight diagrams and 97 messages expose useful engine/service chronology, while also exposing low-signal logging,
placeholder branch labels, generic termination notes, overlong signatures, redundant participants, unexplained
repeated calls, and very small diagnostic-only roots. Treat these as generic product acceptance failures, not
CreditTransfer-specific wording requests. Improve signal and readability before adding enough traversal to amplify the
noise. Small diagrams remain valid when the source evidence is genuinely small, but they must be classified as
partial/diagnostic rather than presented as useful coverage.
