# Architecture

SeqDoc is a compiler-evidence pipeline. Roslyn supplies symbols, syntax, control flow, value flow,
invocations, and source locations; typed stages preserve those facts, evidence, certainty, and stable
identity as behavior is translated into documentation.

## The pipeline

1. **Program Index.** A compiler-level inventory of projects, documents, types, methods, and
   invocations extracted from Roslyn. It also records controlled eligibility facts for framework
   interpretation and logical source locations.
2. **Method Flow.** Per-method control flow, local value flow, call resolution, exact handler calls,
   bounded loops, nested returns, and return behavior. Each flow element retains its producing evidence.
3. **Scenario Graph.** Behavior-level graphs that combine method flows with framework interpretation
   and semantic companion facts. Nodes represent queries, mutations, service calls, observations, and
   outcomes; edges carry evidence and explicit certainty.
4. **Diagram Plan.** The renderable plan derived from a scenario graph: structured messages and
   fragments (`alt`, `opt`, `break`, `loop`) with wording phrases and evidence references. It is the
   single contract consumed by renderers.

## Principles

- **Static analysis is authoritative.** SeqDoc reports what compiler evidence supports. Incomplete
  evidence produces conservative or unknown certainty rather than guesses.
- **Every user-facing fact has evidence and certainty.** This invariant is enforced in core types.
- **Determinism.** Ordering is explicit and stable; fingerprints do not depend on checkout location,
  scheduling, or timestamps.
- **Failed analysis preserves the previous valid state.** Persistence activates a new store only after
  validation succeeds.
- **Layered dependencies.** Core projects do not depend on Roslyn, MSBuild, SQLite, the command-line
  surface, or renderers. Compilation profiles and target frameworks stay separate.

## Current interpretation

ASP.NET Core controllers and selected Minimal API endpoints are recognized through controlled
eligibility rules, producing HTTP entry points, route bindings, and exact outcome helpers. Selected
MediatR dispatch shapes connect an exact request to an exact handler call. Entity Framework Core data
access is interpreted into queries, mutations, and saves. Coverage is selected-flow coverage: arbitrary
roots, unrestricted call graphs, every loop shape, and every framework convention are not implied.
Unsupported or ambiguous constructs state the limitation instead of guessing.

Configuration reads and dependency-injection registrations are collected as semantic facts and
resolved into graph-level composition when evidence proves an exact alternative. Callback boundaries
are represented with explicit flow edges and conservative wording where callback behavior is not
provable from the compiler.

## Rendering and persistence

The diagram plan is rendered to Markdown with Mermaid diagrams using typed wording. Analysis and
evidence are stored in SQLite with file-local identity and deterministic fingerprints.
