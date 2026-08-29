# Web Test Toolkit — Architecture & Status

**Last updated:** 2026-08-28

A local toolkit that records a web flow by inspection and turns it into a runnable Selenium +
Reqnroll BDD test suite. You point it at a web app, click through the flow you want tested, and it
writes the C# test code — then runs it, reports on it, explains failures with an LLM, repairs broken
locators when the app changes, and exports the flow as human-readable test case documentation.

---

## 1. Architecture

The toolkit is a **local client/server web application**, not a desktop app. The backend is an
ASP.NET Core Web API that owns all the real work (driving the browser, generating code, running
tests, calling Groq). The frontend is a React single-page app that talks to it over HTTP + SignalR.
Everything runs on `localhost` — a developer tool operating on a local repo and a local browser, not
a hosted multi-tenant service.

```
┌─────────────────────────────┐         ┌──────────────────────────────────────┐
│  frontend/  (React + Vite)  │  HTTP   │  backend/WebTestToolkit.Api          │
│  localhost:5173             │ ──────► │  localhost:5000                      │
│                             │         │                                      │
│  Inspect · Flows · Run      │ SignalR │  ┌────────────────────────────────┐  │
│  Report · Failures          │ ◄────── │  │ Inspector     → Selenium/Chrome│  │
│  Export · Settings          │  live   │  │ Llm           → Groq API       │  │
└─────────────────────────────┘  events │  │ CodeGenerator → deterministic  │  │
                                        │  │ Execution     → dotnet test/trx│  │
                                        │  │ Export        → xlsx / xml     │  │
                                        │  └────────────────────────────────┘  │
                                        └──────────────┬───────────────────────┘
                                                       │ file I/O + `dotnet test`
                                                       ▼
                                        ┌──────────────────────────────────────┐
                                        │  tests/WebTestToolkit.GeneratedTests │
                                        │  (standalone Reqnroll+Selenium proj) │
                                        └──────────────────────────────────────┘
```

`WebTestToolkit.GeneratedTests` is never referenced by the backend — the API writes files into it
and shells out to `dotnet test`. That isolation is deliberate: the generated suite must be runnable
by anyone who clones the repo (CI, Visual Studio, command line) with the toolkit nowhere in the
picture. The toolkit authors tests; it is not a runtime dependency of them.

```
WebTestToolkit/
├── backend/
│   ├── WebTestToolkit.Api/                 ASP.NET Core Web API + SignalR hubs
│   ├── WebTestToolkit.Contracts/           Shared models (zero dependencies)
│   ├── WebTestToolkit.CodeGenerator/       Deterministic TestFlow → .feature/.cs/.json
│   ├── WebTestToolkit.Llm/                 Groq client + prompt skills
│   ├── WebTestToolkit.Inspector/           Selenium + injected JS capture overlay
│   ├── WebTestToolkit.Execution/           dotnet test runner + .trx parsing + run reports
│   ├── WebTestToolkit.Export/              Test case docs → Excel / XML
│   └── WebTestToolkit.*.Tests/             One per library above, + Export.Tests
├── frontend/                               React + Vite + TypeScript
│   ├── src/pages/                          Inspect · Flows · Run · Report · Failures · Export · Settings
│   ├── src/api/                            Typed fetch wrappers + SignalR client
│   └── src/components/
├── tests/
│   └── WebTestToolkit.GeneratedTests/      The output. Standalone.
└── docs/
```

Every backend library depends only on `Contracts`; `Export` also depends on `Llm` (calls the
test-case prose skill directly, same reason `Execution` calls the script-generation skill). `Api`
references all of them. Nothing references `GeneratedTests`.

---

## 2. Current status

### Implemented — P1 through P12

| Phase | Delivers | Key files |
|---|---|---|
| **P1–P2** | Hand-written sample suite (the style reference every generator reproduces: feature, page object, JSON locators, steps, hooks); `Contracts` shared models; deterministic `TestFlowCodeGenerator` (`TestFlow` → feature/page-object/steps/locator-json, via `GherkinStepPlanner` + 4 emitters) | `tests/WebTestToolkit.GeneratedTests/`, `Contracts/Models/`, `CodeGenerator/` |
| **P3** | Restructure to `backend/`/`frontend/`/`tests/`; WPF retired, its `dotnet test` shell-out salvaged into `Execution` (`DotnetCli`); ASP.NET Core API skeleton with RFC 7807 error handling; React+Vite+TS shell; CI (`dotnet build`+`test` on Windows, `npm run lint`+`build` on Ubuntu) | `Api/`, `.github/workflows/ci.yml` |
| **P4** | Groq foundation — hand-rolled `GroqClient` (OpenAI-compatible, strict `json_schema` structured outputs), `LlmSkill<TIn,TOut>` pattern, embedded prompts/schemas, server-side key storage (Windows DPAPI), Settings page, first skill (failure analysis) | `Llm/Transport/`, `Api/Services/FileSettingsStore.cs` |
| **P5** | LLM codegen + self-repair — deterministic baseline → LLM → `StaticValidator` (hardcoded-`By` ban, forbidden patterns, Gherkin/binding sanity) → real sandbox compile (`BuildSandbox`, outside the repo) → compiler-error-fed repair turns → deterministic fallback. Provenance shown per attempt in the UI | `Execution/Generation/HybridTestCodeGenerator.cs`, `Execution/Generation/StaticValidator.cs` |
| **P6** | Test case export to Excel (ClosedXML) / XML, via `TestCaseSuiteBuilder` + prose skill 6. Ships the recorded happy path only — LLM edge cases, Scenario Outline rows, and last-run status are schema-ready in `Contracts` but not wired into this exporter yet (skill 4 feeds *Generate*, not this; `TestFlow` still has no Outline representation) | `Export/TestCaseSuiteBuilder.cs`, `Export/{Excel,Xml}TestCaseWriter.cs` |
| **P7** | Inspector backend — one hand-driven Chrome session per capture, injected JS overlay (hover-highlight, click/change capture, idempotent SPA re-injection), `LocatorRanker` scoring (`id` 100 > `data-testid` 95 > `name` 85 > `aria-label` 78 > `placeholder` 72 > text-xpath 60 > generated-id 45 > css 35 > absolute xpath 10), deterministic `StepLabeler`, live SignalR feed | `Inspector/InspectorSession.cs`, `Inspector/Capture/LocatorRanker.cs` |
| **P8** | Inspect UI — start form, live step table over the P7 feed, action-type/label/locator editing, pause/resume/stop; skill 2 (step-label suggestion, read-only, review-before-apply) | `frontend/src/pages/InspectPage.tsx`, `Llm/Skills/StepLabelSuggestionSkill.cs` |
| **P9** | Generate end-to-end — Inspect→Generate handoff fully wired (`GET /{id}/flow` → router state → Flows/Export pages); skill 4 (edge-case generation) proposes overrides on existing steps only, with a Preview/Accept/Reject review UI; `EdgeCaseFlowBuilder` builds a real `TestFlow` deterministically. Skills 3 (assertion inference) and 5 (Outline expansion) deliberately deferred — see P13 | `frontend/src/pages/FlowsPage.tsx`, `Llm/Skills/EdgeCaseGenerationSkill.cs` |
| **P10** | Execution + Report — `TestRunner`/`TrxParser` shell `dotnet test --logger trx` and parse the result (schema verified against two real runs, not guessed); live console over SignalR (`RunHub`, buffered for late subscribers); Run/Report pages; CSV/HTML export built client-side. Kept scenario-level failure screenshots rather than adding `[AfterStep]` capture | `Execution/{TestRunner,TrxParser}.cs`, `frontend/src/pages/{RunPage,ReportPage}.tsx` |
| **P11** | Failure analyzer UI — reads the P10 run summary, filters to failures, shows error/stack trace/screenshot with a per-scenario "Analyze with Groq" button. Zero backend changes needed (skill 7 and its endpoint were already complete since P4) | `frontend/src/pages/FailuresPage.tsx` |
| **P12** | Auto-heal — a locator picker (`GET /api/locators`, reading every `*.locators.json`) plus a single-capture re-inspect session that's an ordinary P7 `InspectorSession` under the hood (`/autoheal/start` opens Chrome at the broken locator's own page); the only new write is `LocatorJsonPatcher`, which rewrites exactly one key in one locator file, atomically, and never touches a `.cs` file | `Execution/Generation/LocatorJsonPatcher.cs`, `Api/Controllers/AutoHealController.cs`, `frontend/src/pages/AutoHealPage.tsx` |

**Verified:** 145/145 backend tests, `dotnet build` clean (Debug + Release, 15 projects), frontend
`tsc`/`vite build`/`oxlint` clean. Every phase from P5 on was also exercised live in a real browser
against the running API before being marked done, including deliberately forcing real compiler
errors (P5), real test failures (P10/P11), and — for P12 — a real broken locator healed back to a
passing test through the actual UI and API, not a mock. **P12's own acceptance line, run for real**:
`UsernameInput` was pointed at a nonexistent id, `dotnet test` was confirmed failing
(`NoSuchElementException`, 2/2 failed), the real `/autoheal/start` endpoint opened a real Chrome
window at the locator's page, `/autoheal/apply` patched the real `LoginPage.locators.json` back to
`id=username`, `git diff` showed only that JSON file changed — zero `.cs` diffs — and `dotnet test`
then passed 2/2. The React page itself was driven the same way (headless Selenium against
`/autoheal`): the picker loads real data from `GET /api/locators`, starting a session opens a real
backend-owned browser and the UI reflects `state: running` live, and the manual strategy/value entry
path (the fallback for when scripting a second, backend-opened window isn't possible) reached the
same "✓ Healed" confirmation.

> The deterministic generator (P1–P2) is not superseded by the LLM work — it's what makes the LLM
> work safe: the guaranteed-correct few-shot example in the codegen prompt, and the fallback when
> the LLM's output won't compile.

**Superseded:** the WPF app (`src/WebTestToolkit.App`) is retired, replaced by `frontend/` +
`WebTestToolkit.Api`; every planned window became a React page, including auto-heal's (P12).
`Contracts` and `CodeGenerator` carried over untouched.

### Roadmap — not yet implemented

| Phase | Adds | Acceptance |
|---|---|---|
| **P13** | Techniques adopted from a sibling project — see below | Each item lands as an isolated, tested addition to already-shipped code |

#### P13 — techniques adopted from a similar project

A sibling Chrome-extension project (also LLM-driven Selenium generation) was reviewed. Most of its
correctness-checking ideas are already implemented here, more strongly — a hard build-gate via
`StaticValidator`, not a UI-only warning — so **not** being re-adopted: the `Thread.Sleep` ban,
exact step-case matching, empty-assertion detection, DRY-helper guidance, locator design, DPAPI key
storage, and the manual "fix issues" button (P5's repair loop already automates that). Five items
are genuinely new:

1. **Given/When steps must also act** — new `StaticValidator` rule (`WTT152`) mirroring the
   existing empty-`Then` check, so a no-op action step can't silently pass. `StaticValidator.cs` +
   a matching prompt-rule addition.
2. **Distinguish "no binding" from "case-only mismatch"** — `WTT150` currently reports both
   identically; a case-insensitive retry with a distinct message makes repair feedback actionable
   instead of ambiguous. `StaticValidator.cs`.
3. **Advisory (non-blocking) issue severity** — add `IssueSeverity {Blocking, Advisory}` to
   `ValidationIssue`, plus a structural duplicated-interaction-block check emitted as `Advisory` so
   it doesn't burn repair attempts on a style nit. Touches `GenerationModels.cs`,
   `StaticValidator.cs`, `HybridTestCodeGenerator.cs`'s success gate, and the Generate page's issue
   rendering — the one item changing a shared contract, so schedule it last.
4. **Capture real element state during Inspect** — select options, checkbox/radio `checked`,
   `required`, `maxLength`. Today the LLM only has a raw HTML snippet to infer this from; a real bug
   in the sibling project (`.SendKeys()` called on a dropdown) motivated this. Touches the injected
   overlay JS, `RawCapture`, `CapturedElement`, `LocatorRanker` — and makes P9's deferred skills
   schema-ready for free.
5. **Show ranked locator alternatives in Inspect** — `CapturedElement.Candidates` already holds up
   to 8 ranked options; only the best is shown today. Add a rationale-per-strategy lookup and an
   expandable list in `InspectPage.tsx` so a manual override is informed, not a guess. Lowest risk —
   pure additive display.

Suggested order: 1 → 2 → 4 → 5 (independent, additive), then 3 last.

### Effort and model estimates

Actuals for P3–P12: roughly 6 hrs for P1–P3; P4, P6, P8, P9, P10, P11, P12 each finished within a
single session on **Sonnet 5** (P11 in under an hour on **Haiku 4.5** — pure UI wiring onto an
existing skill; P12 landed comfortably inside its 6–8 hr estimate, reusing P7's `InspectorSession`
wholesale meant the only genuinely new code was `LocatorJsonPatcher` and a thin controller/page
around it); P5 and P7 — the two phases integrating with something outside the model's control (a
real compiler, a live browser's JS engine) rather than a known API — used **Opus 5** and landed
inside their wider estimates. Pattern going forward: default to Sonnet 5; reach for Opus 5 only when
a phase is mostly "make an external, non-deterministic system behave," not "write code against a
known API"; Haiku 4.5 is viable for small, mechanical, additive changes matching an existing pattern.

| Phase | Effort (hrs) | Model |
|---|---|---|
| **P13** — items 1/2/5 | 1–2 hrs each | Haiku 4.5 viable (mechanical, additive) |
| **P13** — item 4 (element-state capture) | 3–4 | Sonnet 5 (injected JS + 3 backend layers + browser test) |
| **P13** — item 3 (severity tiers) | 3–4 | Sonnet 5 (changes a shared contract + a gating condition) |
| **Deferred from P9/P10** — skills 3 & 5, `[AfterStep]` screenshots, screenshot preview | 10–14 | Sonnet 5 |

---

## 3. Groq integration

**Model:** `openai/gpt-oss-120b` — production on GroqCloud, 131,072-token context, ~500 tok/sec,
OpenAI-compatible endpoint. Key facts that shape the design:

- **Strict structured output:** `response_format: {type:"json_schema", json_schema:{name,
  strict:true, schema}}` — every field `required`, `additionalProperties:false`. This is why codegen
  returns validated `{files:[{path,content}]}` rather than prose scraped for code blocks.
- **Structured outputs cannot stream** — generation is a spinner with a status line.
- `reasoning_effort` = `low`/`medium`/`high` (default `medium`); `include_reasoning` replaces the
  unsupported `reasoning_format`.
- The 131k context is what makes few-shot viable: the hand-written sample, the deterministic
  generator's output, and the captured flow all fit in one prompt.
- **Server-side key only** — never sent to the browser. `GET /api/settings` returns whether a key is
  configured, never its value.

### Seven skills

| # | Skill | Effort | Fallback when unavailable | Status |
|---|---|---|---|---|
| 1 | Script generation | high | Deterministic generator | ✅ P5 |
| 2 | Step-label suggestion | low | Deterministic `StepLabeler` | ✅ P8 |
| 3 | Assertion inference | medium | User captures assertions explicitly | ⬜ deferred |
| 4 | Edge-case generation | medium | Happy path only | ✅ P9 |
| 5 | Scenario Outline expansion | medium | Single non-parameterized scenario | ⬜ deferred (needs a `TestFlow` schema change) |
| 6 | Test-case prose | low | Template text from labels | ✅ P6 |
| 7 | Failure analysis | medium | Raw error + stack trace as-is | ✅ P4 |

Every skill degrades gracefully — the tool stays fully usable with no API key configured, verified
at every phase that touches Groq. Skills 3 and 5 are deferred rather than half-built: skill 3 has no
real gap to fill (assertions are already hand-capturable), and skill 5 needs real `Examples`-table
support in `TestFlow` first (see P13's item 3 for the unrelated-but-similar "don't half-build a
schema change" precedent from P6).

### Generate → validate → repair loop

```
captured TestFlow
      │
      ├─► deterministic generator ──► baseline output (also the few-shot reference)
      ▼
  Groq gpt-oss-120b  ◄── prompt carries: hand-written sample, LocatorRepository/DriverContext
      │                  API surface, allowed By strategies, exact package versions
      ▼
  {files:[{path,content}]}   (strict JSON schema)
      ▼
  write to STAGING copy of the test project (outside the repo — a bad attempt can't corrupt tests/)
      ▼
  dotnet build ──FAIL──► feed compiler errors + failing code back to Groq (retry, capped)
      │ PASS                                    │ still failing after cap
      ▼                                         ▼
  promote staging → tests/                deterministic fallback (surfaced in UI, never silent)
```

Edge-case scenarios and Scenario Outlines (skills 4 and 5) are the model's own inventions, not the
user's recording — presented for accept/reject before ever touching disk, never written silently.
Provenance (LLM / LLM-after-N-repairs / deterministic fallback) is always visible in the UI.

---

## 4. Test case export

Renders the same `TestFlow` as human-readable documentation instead of code — a second *renderer*
of the captured flow, not a separate capture path. `Export/` depends on `Contracts` and `Llm`
(skill 6). NuGet: ClosedXML 0.105.1 for `.xlsx`; `System.Xml.Linq` for XML.

XML: `<TestSuite><TestCase><Steps><Step>` with `Action`/`TestData`/`ExpectedResult` per step — a
generic schema transformable to any tool's import format later. Excel: `Test Case ID | Title |
Precondition | Priority | Source | Step # | Action | Test Data | Expected Result | Last Run Status`,
one row per step, plus a Summary sheet. `TestData` is filled in mechanically from `TestStep.
InputValue` after the prose skill runs — the model is never shown a real typed value and cannot
invent it.

Today every suite holds exactly one `TestCaseDocument`, `Source: Recorded`, `LastRunStatus: null` —
see §2's P6 row for what's schema-ready but not yet wired (edge cases, Outline rows, last-run
status).

---

## 5. API surface

```
Inspect     GET    /api/inspect/sessions                          → InspectorSessionInfo[]     [built]
  [built]   POST   /api/inspect/start          { name, startUrl }  → { session, steps }
            GET    /api/inspect/{id}                               → { session, steps }
            POST   /api/inspect/{id}/capture   { enabled }         pause / resume recording
            POST   /api/inspect/{id}/stop                          → { session, steps }, browser closed
            PATCH  /api/inspect/{id}/steps/{n} { label, actionType, inputValue,
                                                 locatorKey, locatorStrategy, locatorValue }
            DELETE /api/inspect/{id}/steps/{n}
            GET    /api/inspect/{id}/flow                          → TestFlow (feeds /api/flows/*)
            POST   /api/inspect/{id}/steps/{n}/suggest-label       → suggested label (skill 2), never applied
            HUB    /hubs/inspect               Subscribe(sessionId) → stepCaptured, sessionState

Flows       POST   /api/flows/preview          { TestFlow, useLlm }  → files + provenance, writes nothing  [built]
  [built]   POST   /api/flows/generate         { TestFlow, useLlm }  → files + provenance, writes to tests/
            POST   /api/flows/edge-cases       { TestFlow }          → suggested edge cases (skill 4),
                                                                        each with its own built TestFlow
            (Deferred: suggest-assertions / suggest-outline, skills 3/5 — see P13. No GET /api/flows;
             the flow travels in every request body, same convention Export reuses.)

Export      POST   /api/export/testcases/{preview,xlsx,xml}   { TestFlow, useLlm } → JSON / file download  [built]

Execution   POST   /api/execution/run                                → 202 + { runId }, background task   [built]
  [built]   GET    /api/execution/runs/{id}                          → RunResponse (status, console, summary)
            GET    /api/execution/runs/latest                        → same, for Report / refresh recovery
            HUB    /hubs/run                   Subscribe(runId)      → consoleLine, runCompleted

Failures    POST   /api/failures/analyze       { scenarioResult } → FailureAnalysis (skill 7)   [built]
            (Screenshot files are on disk with a path in ScenarioResult.ScreenshotPath; nothing
             serves them yet — no static-file mapping to the build-configuration-specific output dir.)

Auto-heal   GET    /api/locators                                  → pages + keys                [built]
  [built]   POST   /api/autoheal/start         { page, key }      → single-capture session (an
                                                                     ordinary inspect session)
            POST   /api/autoheal/apply         { page, key, strategy, value } → patched entry

Settings    GET    /api/settings               → model, key-is-set flag (never the key itself)   [built]
            PUT    /api/settings               → { groqApiKey?, groqModel }
```

---

## 6. Known risks

1. **LLM emits code referencing methods that don't exist** — the reason for the compile-verify-repair
   loop. The prompt carries the *actual* `LocatorRepository`/`DriverContext` API surface, not a
   description of it.
2. **Prompt injection from the app under test** — DOM text, labels, and page content are fed into
   prompts. Treat all captured DOM as untrusted, fence it clearly, and never let model output decide
   file paths — the API decides, not the model.
3. **Selenium Manager** needs outbound internet and can lag Chrome releases; two independent
   driver-creation sites (Inspector + generated-test execution) double the exposure. The Inspector
   side is wrapped (a 502 with the real message); `DriverContext`'s side is not — a failure there
   surfaces only as raw `dotnet test` console text.
4. **Orphaned Chrome processes** — mitigated for graceful shutdown (`InspectorSessionManager` closes
   idle/all sessions), not for a hard process crash.
5. **Groq model deprecation** — the model ID is a stored setting, not a constant, so this stays a
   config change.
6. **Cost and latency per generation** — a high-effort codegen call plus up to N repair attempts is
   the most expensive operation in the tool; nothing caches it yet.
7. **Auto-heal scope (P12)** — handles "same element, changed locator" only; structural page
   changes still need a re-record.
8. **The hand-written sample suite depends on a live third-party site**
   (`the-internet.herokuapp.com`) and has been observed flaky (a `503` unrelated to any toolkit
   change). `Inspector.Tests` already solves this for its own suite with a local `TinyWebServer`
   fixture; the sample suite would benefit from the same fix but hasn't been converted.
9. **No `LICENSE` file** — defaults to "all rights reserved" under GitHub's terms. Fine for a
   private tool; a blocker if the repo is ever opened up. Owner's call, not made here.
