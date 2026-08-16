# Testing Policy

SeqDoc selects tests from observable risks rather than raw branch, row, or coverage counts.

## Principles

1. Start each product checkpoint with a risk inventory and identify existing coverage first.
2. Use the smallest reliable layer: pure contract tests, compiler-boundary tests, then real-project acceptance.
3. Test semantic outcomes, evidence/certainty, false-positive boundaries, deterministic identity/output, failure
   preservation, and concrete regressions rather than implementation shape.
4. Avoid duplicating the same assertion across layers unless each layer has a distinct failure mode.
5. Keep tests deterministic, isolated, order-independent, and self-checking.
6. Real applications prove integration and breadth; they never justify project-specific production rules.

## Soft budget

A routine checkpoint should normally add approximately 5–12 distinct claims. More than 15 requires a written
risk-by-risk justification. The budget is soft and never suppresses a genuinely distinct high-impact risk.

## Test Writer trigger

Dispatch the Test Writer for new compiler or intermediate-representation semantics; exact-symbol, overload, or
false-positive risk; evidence/certainty degradation; persistence or previous-valid-state behavior; deterministic
identity/output; a concrete regression signature; or acceptance-critical scenario, wording, or diagram behavior.
Routine mechanical and documentation work does not justify a separate Test Writer.

## Verification lanes

- **Focused:** the smallest changed test surface or affected project build.
- **Checkpoint:** affected small tests plus one relevant boundary proof and the declared final gate.
- **Milestone:** Release build/tests, applicable deterministic and repository checks, named real applications, and
  manual output inspection.
- **Corpus/release:** broad applications, performance/resources, platform lanes, and persistence equivalence when
  explicitly required.

No completion claim may rely on stale evidence. A failed command may be rerun after a relevant repair; do not rerun
a successful command against an unchanged candidate.
