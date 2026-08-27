# Issue #23 optional overview/child decomposition

## Status: Closed

Baseline: `dd51706`. Candidate: complete `WORKTREE`, pending publication. One independent review ran; no second review.

This reconstruction applies the accepted `e3de73a` I23 content and the narrowly scoped P2R repairs
for atomic fitting, duplicate labels, guarded production decomposition, and compiler-backed forced
decomposition. Historical review dispositions remain provenance; independent publication review
(`review0`) is complete. No I23PUB, execution, or P2R records belong in this publication worktree.

## Evidence packet disposition

- Scope/baseline contamination: rejected; the packet inspected the wrong root. This worktree is clean
  against `dd51706` with the declared allowlist.
- Atomic irreducible fragments: accepted semantics withhold the entire unplaceable fragment as one
  conservative `DP-MERMAID-TRUNCATED` boundary; no partial fragment or message is emitted.
- CLI JSON provenance: rejected; the frozen contract requires the human visibility line only. JSON
  schema extension is unauthorized and out of scope.
- Producer-backed coverage: addressed by the guarded production plan and compiler-backed FourFlow test.

## Verification and audit

- Focused current-main verification: Rendering `77/77`, Configuration `61/61`, FourFlow `13/13`.
- Test Writer audit added `0` claims and mapped all declared risks to covered tests.
- Exact declared allowlist verified; no unresolved blocker.

## Final verification

- One independent review: `I23-F1` Medium fixed in repair rerun 1 by one exact CLI assertion; no second review.
- Release locked restore/build passed with 0 warnings/errors. The initial Analysis final invocation had `2/235`
  failures solely because standalone FusionCache restore was omitted; exact locked FusionCache restore corrected
  the verification command and Analysis then passed `235/235`. No candidate change was made.
- Final gate: Core `91/91`, Analysis `235/235`, Behavior `63/63`, Scenarios `218/218`, Wording `116/116`,
  Rendering `77/77`, Configuration `61/61`, CLI `20/20`. Acceptance `30/31`, with only the unchanged known
  MediatR baseline failure. `git diff --check` passed.

## External verification

- Disabled CreditTransfer, FraudManagement, SMSGateway, and TicketReservation lanes each ran twice, were
  byte-identical, and matched accepted I21 current-main artifacts. They produced respectively `8/36/15/4`
  Mermaid files, maxima `4821/1227/1907/484`, zero links, and stayed within `45000` characters.
- Enabled CreditTransfer ran twice byte-identically: `14` Mermaid / `30` total files, maximum `2568 <= 2600`,
  zero links, and 2 decomposition mentions. Enabled FraudManagement ran twice byte-identically: `42` Mermaid /
  `86` total files, maximum `819 <= 900`, zero links, and 4 decomposition mentions. Profiles and fingerprints
  remain the I21 values.
- The browser lane used pinned Mermaid CLI `11.16` and successfully rendered `114/119` current SVGs. After
  Puppeteer reported missing `chrome-headless-shell` following user-cache removal, no reinstall or reinvocation
  was performed. The five remaining current Fraud enabled `.mmd` files are SHA256 byte-identical to accepted
  historical I23 `.mmd` files with successful SVG evidence. Therefore `119/119` distinct Mermaid inputs have
  successful render evidence: 114 current executions plus 5 exact-byte inherited evidence; this does not claim
  119 current rerenders.
- Initial external relative-config failures (`SD3004`) were command path quoting errors; absolute config paths
  passed without candidate or configuration changes.

Final gate result: `PassWithKnownBaselineAndInheritedRenderEvidence`.

## Independent review and repair

- `I23-F1` (Medium): **Fixed** by the owner-approved smallest target amendment
  `tests/SeqDoc.Cli.Tests/DiagramBudgetCliTests.cs`, adding `decomposition: true` and the exact human
  line `Diagram decomposition: True (ConfigurationFile)`; no JSON schema change.
- Test Writer initial run failed with exit 3 solely because the standalone GetMeaning fixture was not
  restored. Repair rerun 1 restored GetMeaning with locked mode, followed by a clean Release CLI build
  with 0 warnings/errors; focused `DiagramBudgetCliTests` passed `1/1`.
- Existing focused Rendering `77/77`, Configuration `61/61`, and FourFlow `13/13` results remain passed
  and unchanged. All findings are disposed; final gate and external verification remain pending.
