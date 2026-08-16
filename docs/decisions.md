# Decisions

This log records the significant architectural decisions made while building SeqDoc. Each entry
states the context, decision, and consequences.

## 1. Static analysis is the source of truth

Behavioral documentation must come from compiler facts, not heuristics, runtime observation, or
commentary. Incomplete evidence is reported with conservative or unknown certainty.

**Consequence:** every user-facing fact carries evidence and an explicit certainty level.

## 2. A typed pipeline with stable stages

The pipeline uses four distinct representations: Program Index, Method Flow, Scenario Graph, and
Diagram Plan. Each stage has a narrow contract, identity scheme, and deterministic ordering.

**Consequence:** results are inspectable and testable at each boundary.

## 3. Deterministic output

Documentation, fingerprints, and file names are identical for the same source regardless of schedule,
checkout location, timestamps, or unstable iteration.

**Consequence:** output can be diffed and regenerated reliably.

## 4. Separate compilation profiles and target frameworks

Compilation profiles and target frameworks remain separate concepts. Analysis is scoped to an explicit
profile, and framework interpretation is driven by evidence within that profile.

**Consequence:** multi-target projects do not cross-contaminate analysis results.

## 5. Evidence and certainty on every fact

Behavior, scenario, wording, and diagram elements carry evidence references and certainty. If the
compiler cannot prove a behavior, output says so.

**Consequence:** documentation is auditable and does not overstate source behavior.

## 6. Failed analysis preserves the previous valid state

Persistence uses a staged SQLite store that activates only after validation succeeds. If analysis
fails, the previously active state remains intact.

**Consequence:** failed analysis does not leave a corrupted or partial state active.

## 7. Framework interpretation is explicit and controlled

ASP.NET Core controllers, selected Minimal API and MediatR shapes, and Entity Framework Core behavior
are recognized through controlled eligibility projection. Exact handler calls are required for dispatch
projection; unsupported roots, call shapes, and framework conventions are reported rather than guessed.

**Consequence:** framework behavior is evidence-backed and unsupported cases degrade gracefully.

## 8. Rendering is driven by a diagram plan

Rendering consumes an explicit diagram plan with structured messages and fragments rather than
serializing internal analysis types directly.

**Consequence:** wording and diagram structure remain separate from rendering mechanics.

## 9. Layered dependencies

Core projects do not depend on Roslyn, MSBuild, SQLite, the command-line surface, or renderers.
Integration happens in dedicated edge projects.

**Consequence:** core remains lightweight, testable, and portable.

## 10. Natural-language presentation follows semantic planning

Natural-language wording is produced after compiler facts, method flows, scenario graphs, and diagram
plans are established. Wording cannot add behavior that earlier stages did not prove.

**Consequence:** readable documentation remains subordinate to evidence and can describe selected-flow
limits honestly.
