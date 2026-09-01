# Mutation testing (P21)

## Why

`dotnet test` tells you the rules **ran**. It does not tell you any test would notice if a rule
stopped working. That distinction is not academic here: `WTT151` spent several phases as
`stripped.Contains("Assert")` — a check that looked like assertion validation and would have
passed every test in the suite even after being weakened to nothing, because no test pinned down
what it was supposed to reject. `WTT153` exists because that gap was eventually found by reading
the code, not by the suite.

Mutation testing closes that loop mechanically: Stryker deliberately breaks the production code
(flips a comparison, drops a call, changes a constant) and re-runs the tests. A mutant that
**survives** is a change no test objected to — a rule nothing actually tests.

## Scope, and why it is narrow

`stryker-config.json` mutates `WebTestToolkit.CodeGenerator` only, driven by
`WebTestToolkit.CodeGenerator.Tests`. That is the deterministic generator — the code that decides
what every generated suite actually contains.

Two deliberate exclusions:

- **`Execution/Generation` (including `StaticValidator`) is not covered yet**, even though it is
  the other place the real rules live. Its test project is `[Category("SandboxBuild")]` and shells
  out to a real compiler per test, so mutating against it would take hours and would mostly
  measure MSBuild rather than the rules. Covering it properly needs a compiler-free test path for
  `StaticValidator` first — worth doing, and the obvious next step here.
- **Controllers, DTOs and thin wrappers**, which produce surviving mutants that are noise rather
  than signal and cost run time for nothing.

### A note on the config format

Stryker rejects unknown keys outright, so this config carries no inline `//` comment keys — that
is why the rationale lives in this file instead. It also needs `project` to name which referenced
project to mutate: without it, a `mutate` glob that looks reasonable can silently match nothing,
the run completes, and it reports "unable to calculate a mutation score" rather than failing.
That happened on the first attempt here.

**Verification status:** the corrected config has not yet been run to completion — a full run
takes roughly 8–9 minutes. The first run (with the broken globs) confirmed Stryker installs,
reads the config and executes; the fix to `project` has not itself been observed producing a
score. Worth doing once, locally, before trusting the scheduled job's output.

## Running it

```
dotnet tool install -g dotnet-stryker
dotnet stryker --config-file stryker-config.json
```

The HTML report lands in `StrykerOutput/<timestamp>/reports/`.

CI runs this **weekly**, not per-PR (`.github/workflows/ci.yml`, the `mutation` job, gated on the
`schedule`/`workflow_dispatch` triggers). A full run takes minutes to tens of minutes, which is
far too slow for a merge gate, and the standard guidance for mutation testing is a scheduled run
over the logic layer rather than a blocking one.

`break` is `0`, so the job **reports and never fails the build**. That is deliberate for now: the
first runs establish a baseline. Raising `break` to gate on a score is a decision to take once the
score has been looked at and the surviving mutants each have an explanation.

## Reading the result

A surviving mutant is a question, not automatically a bug. For each one, either:

- the behaviour genuinely is not tested → add the test, or
- the mutated code is equivalent (no observable difference) → it cannot be killed, and that is fine.

The target in the config is 70 (`low`) / 80 (`high`), matching common practice for a logic layer.
