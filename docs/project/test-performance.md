# Test performance guidance

This is measured guidance, not a promise. Build once and restore required external fixtures once. Run independent
projects concurrently when isolation permits, then use `--no-build --no-restore` for reruns. Record external wall time
and keep broad Release/corpus gates separate from routine checkpoint checks.

The recorded baseline is: CLI warm command 3m27.9s wall; xUnit 3m25s; Rendering 33s; Scenarios 5s; Wording under 1s.
These measurements do not claim an optimization.

Later, a dedicated CLI test-performance checkpoint should move behavioral tests in-process through `CliHost.RunAsync`,
while retaining a minimal real-process contract suite and every assertion. That proposal must measure before and after;
it must not weaken coverage or replace the release/corpus lanes.

## Later checkpoint brief

Current cause evidence is repeated real `dotnet SeqDoc.Cli.dll` process launches, serial concentration in
`CliProcessTests`, repeated Roslyn/MSBuild analysis, and fixture restore outside the solution. Retain real-process
contract cases for argument parsing, exit codes, stdout/stderr, and one end-to-end artifact. Move behavioral cases
through `CliHost.RunAsync` as the measured candidate path, with isolated temp roots, configuration, persistence, and
environment state. Run independent projects concurrently only with separate checkout/cache/output identities.

Measure a cold and warm baseline and candidate with the same machine, revisions, fixture restore, test assertions,
process counts, and wall-clock method. Record external wall time and failures. Goals, not current facts, are a warm
four-project gate under 5 minutes and focused loops under 2 minutes. Acceptance requires every assertion retained,
the process-contract cases passing, deterministic output, and no cross-project contamination. No tests may be skipped
or weakened; broad release and corpus gates remain broad.
