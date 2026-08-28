# Web Test Toolkit — Architecture & Status

**Last updated:** 2026-08-28

A local toolkit that records a web flow by inspection and turns it into a runnable Selenium +
Reqnroll BDD test suite. You point it at a web app, click through the flow you want tested, and it
writes the C# test code — then runs it, reports on it, explains failures with an LLM, repairs broken
locators when the app changes, and exports the flow as human-readable test case documentation.

---

## 1. Architecture

**Decision (2026-08-28):** the toolkit is a **local client/server web application**, not a desktop
app. The backend is an ASP.NET Core Web API that owns all the real work (driving the browser,
generating code, running tests, calling Groq). The frontend is a React single-page app that talks to
it over HTTP + SignalR.

Everything runs on `localhost` — this is a developer tool operating on a local repo and a local
browser, not a hosted multi-tenant service.

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

### Why the generated tests stay a separate, standalone project

`WebTestToolkit.GeneratedTests` is never referenced by the backend. The API writes files into it and
shells out to `dotnet test`. That isolation is deliberate: the generated suite must be runnable by
anyone who clones the repo — in CI, from Visual Studio, from the command line — with the toolkit
nowhere in the picture. The toolkit authors tests; it is not a runtime dependency of them.

### Target folder structure

```
WebTestToolkit/
├── backend/
│   ├── WebTestToolkit.Api/                 ASP.NET Core Web API + SignalR hubs
│   ├── WebTestToolkit.Contracts/           Shared models (zero dependencies)
│   ├── WebTestToolkit.CodeGenerator/       Deterministic TestFlow → .feature/.cs/.json
│   ├── WebTestToolkit.CodeGenerator.Tests/ Unit tests
│   ├── WebTestToolkit.Llm/                 Groq client + prompt skills
│   ├── WebTestToolkit.Inspector/           Selenium + injected JS capture overlay
│   ├── WebTestToolkit.Execution/           dotnet test runner + .trx parsing + run reports
│   └── WebTestToolkit.Export/              Test case docs → Excel / XML
├── frontend/                               React + Vite + TypeScript
│   ├── src/pages/                          Inspect · Flows · Run · Report · Failures · Export · Settings
│   ├── src/api/                            Typed fetch wrappers + SignalR client
│   └── src/components/
├── tests/
│   └── WebTestToolkit.GeneratedTests/      The output. Standalone.
└── docs/
```

Each backend library depends only on `Contracts`. `Api` references all of them. Nothing references
`GeneratedTests`.

---

## 2. Status

### ✅ Implemented

| Area | What exists | Where |
|---|---|---|
| **Sample test suite** | Hand-written Reqnroll + Selenium login test proving the target output shape: 2 scenarios, page object, JSON locators, step bindings | `tests/WebTestToolkit.GeneratedTests/` |
| ↳ Driver lifecycle | `DriverContext` — lazy `ChromeDriver`, one per scenario via Reqnroll context injection | `Support/DriverContext.cs` |
| ↳ Failure screenshots | `Hooks` — `[AfterScenario]` screenshot on `TestError`, saved to `Screenshots/` | `Support/Hooks.cs` |
| ↳ Locator indirection | `LocatorRepository` — loads `*.locators.json`, maps `id`/`css`/`xpath`/`name` → Selenium `By`. **This is what makes auto-heal a JSON edit, never a code edit.** | `Support/LocatorRepository.cs` |
| **Shared models** | `TestFlow`, `TestStep`, `CapturedElement` (now with DOM-context fields for future label/assertion suggestion), `LocatorCandidate`, `LocatorEntry`, `PageLocators`, `ScenarioResult`, `RunSummary`, `FailureAnalysis`, `AppSettings`, `ActionType`, `ScenarioOutcome` | `backend/WebTestToolkit.Contracts/Models/` |
| **Deterministic code generator** | `TestFlowCodeGenerator.Generate(flow)` → 4 files keyed by relative path | `backend/WebTestToolkit.CodeGenerator/` |
| ↳ Step planning | `GherkinStepPlanner` — assigns Given/When/Then + `And` continuation, builds binding regexes, derives method names | `GherkinStepPlanner.cs` |
| ↳ Four emitters | `FeatureFileGenerator`, `PageObjectGenerator`, `StepsGenerator`, `LocatorJsonGenerator` — plain string building, no templating engine | *(same folder)* |
| ↳ Verification | 5 unit tests, all passing; output verified by hand against the Phase 1 sample | `backend/WebTestToolkit.CodeGenerator.Tests/` |
| **P3 — restructure & scaffold** | Repo moved to `backend/` / `frontend/` / `tests/`; WPF app retired, its `dotnet test` shell-out and solution-root discovery salvaged into `Execution` (`DotnetCli`, `SolutionPaths`, with the blocking-read deadlock risk fixed) | `backend/WebTestToolkit.Execution/` |
| ↳ API skeleton | `WebTestToolkit.Api` (ASP.NET Core): `/api/health`, CORS opened for the Vite origin, a placeholder `PingHub` proving the SignalR pipeline | `backend/WebTestToolkit.Api/` |
| ↳ Empty backend projects | `Llm`, `Inspector` (+ Selenium.WebDriver 4.48.0), `Execution`, `Export` (+ ClosedXML 0.105.1) — scaffolded and wired to `Contracts`/`Api`, no feature code yet | `backend/WebTestToolkit.{Llm,Inspector,Execution,Export}/` |
| ↳ Frontend shell | React + Vite + TS, react-router, a stub page per planned feature area, typed API client, SignalR wrapper, dev-server proxy to the API | `frontend/` |
| ↳ `CapturedElement.BestLocator` bug fix | Was throwing on an element with zero locator candidates; now nullable, and `LocatorJsonGenerator` skips such elements instead of crashing | `Contracts` / `CodeGenerator` |
| **P4 — Groq foundation** | `GroqClient` (hand-rolled HTTP, no SDK) against Groq's OpenAI-compatible endpoint, strict `json_schema` structured outputs, per-request auth (never mutates shared client state) | `backend/WebTestToolkit.Llm/Transport/` |
| ↳ Skill pattern | `LlmSkill<TInput,TOutput>` base (prompt+schema → typed result, one shared deserialize/error path) and the first concrete skill, `FailureAnalysisSkill` | `Llm/Skills/` |
| ↳ Prompts & schemas | Embedded `.md`/`.json` resources (`WTT_PROMPT_DIR` env var overrides with loose files for fast iteration), loaded via `PromptLibrary` | `Llm/Prompts/`, `Llm/Schemas/` |
| ↳ Server-side key storage | `FileSettingsStore` — `%AppData%\WebTestToolkit\settings.json`, API key encrypted at rest via Windows DPAPI (CurrentUser scope), `GROQ_API_KEY` env var as fallback when nothing's saved | `Api/Services/FileSettingsStore.cs` |
| ↳ Endpoints | `GET/PUT /api/settings` (key never returned, only whether one's set), `GET /api/llm/status`, `POST /api/failures/analyze` (always 200 — `available:false` + a reason, never a 500, when no key/bad key/model hiccup) | `Api/Controllers/{Settings,Llm}Controller.cs` |
| ↳ Settings page | Real implementation: save key/model, live "Try it" panel that analyzes a canned failure through the actual pipeline | `frontend/src/pages/SettingsPage.tsx` |
| ↳ `FailureAnalysis` model extended | `Category` enum, `SuggestedLocatorFix`, `Confidence`, `IsLikelyApplicationBug`, `Model` — matches the strict schema Groq is asked to fill in | `Contracts/Models/FailureAnalysis.cs` |

**Verified working:** `dotnet build WebTestToolkit.sln` clean across all 11 projects (0 warnings, 0
errors); `dotnet test CodeGenerator.Tests` → 5/5, `dotnet test Llm.Tests` → 13/13 (transport request
shape, 401/429/truncated/malformed-body handling, no-key propagation, schema parsing — all against
stubbed HTTP, no real key needed); frontend type-checks and builds; and two live checks against the
running API: the no-key path (`POST /api/failures/analyze` → `200 {available:false, unavailableReason:
"No Groq API key is configured..."}`, never a 500) and, with a deliberately invalid real key saved
through `PUT /api/settings`, an actual round-trip to Groq that came back `401` and was mapped to the
same graceful shape. A real analysis (valid key → an actual root-cause explanation) hasn't been
exercised live yet — that needs an API key from the user.

> **The deterministic generator is not superseded by the LLM work — it is what makes the LLM work
> safe.** It now serves two further roles: the guaranteed-correct few-shot example inside the codegen
> prompt, and the fallback when the LLM's output won't compile. See §3.

### 🔄 Superseded (done)

| Item | Disposition |
|---|---|
| `src/WebTestToolkit.App` (WPF) | **Retired.** Replaced by `frontend/` + `WebTestToolkit.Api`. |
| Planned WPF windows (`InspectorWindow`, `ReportWindow`, `FailureAnalyzerWindow`, `SettingsWindow`) | Become React pages — stubs exist at `frontend/src/pages/`, not yet implemented. |
| `DispatcherTimer` polling design | Becomes a backend `BackgroundService` polling the JS queue and pushing to the frontend over SignalR — not yet built (P7). |
| "User labels every captured step manually" | Softened to "user *confirms or edits* an LLM-suggested label" — see §3, skill 2. Manual entry remains the fallback when no API key is configured. |

Nothing already built was wasted — `Contracts` and `CodeGenerator` carried over untouched into
`backend/`.

### ⬜ Not yet implemented

| # | Phase | What it adds | Acceptance |
|---|---|---|---|
| **P5** | **LLM script generation + self-repair** | Codegen skill, staging compile, compiler-error feedback retry, deterministic fallback, provenance reporting | A hand-authored `TestFlow` generates code that compiles; a deliberately broken prompt exercises repair, then falls back cleanly |
| **P6** | **Test case export** | `WebTestToolkit.Export`, Excel + XML writers, prose skill, export endpoint + UI | A hand-authored flow exports to a valid `.xlsx` that opens in Excel and an `.xml` that parses |
| **P7** | **Inspector backend** | `InspectorSession` (own `ChromeDriver`), injected JS overlay (hover-highlight, click-capture, idempotent re-injection for SPA navigations), `LocatorRanker` (id > data-testid > name > css > xpath), session manager, polling `BackgroundService` → SignalR | `POST /api/inspect/start` opens Chrome; clicking an element pushes a live event to a connected client |
| **P8** | **Inspect UI + label suggestions** | React inspect page, live step list over SignalR, label dialog pre-filled by LLM skill 2 | A capture session produces a labeled `TestFlow` in the browser; suggestions appear but stay editable, and absent-LLM still works |
| **P9** | **Generate end-to-end** | Wire Inspect → Generate, plus assertion inference, edge cases, and Scenario Outlines with their accept/reject review UI | Inspect the demo login page → Generate → files written → `dotnet build` green → test runs |
| **P10** | **Execution + Report** | `dotnet test --logger trx`, `TrxParser` → `RunSummary`, `[AfterStep]` screenshots, live console over SignalR, report page + CSV/HTML export | Run & Report shows correct pass/fail counts and exports openable files |
| **P11** | **Failure analyzer UI** | Failed-scenario list, error + stack trace + screenshot, Groq explanation (skill built in P4) | Analyzing a real failure returns a useful root cause in seconds |
| **P12** | **Auto-heal** | Locator picker, single-capture re-inspect session, `LocatorJsonPatcher` rewrites one JSON entry | Break a locator → heal it → `git diff` shows zero `.cs` changes → test passes |

P4–P6 all work off hand-authored `TestFlow` fixtures, so substantial LLM and export work lands
before any browser automation exists — the same trick that made the deterministic generator testable.

### Effort, ETA, and model estimates

**These are estimates, not measured data.** P1–P4 are the real data points so far — roughly 6 hours
for P1–P3, and P4 (this session) landed comfortably inside its 6–8 hr estimate: full transport +
skill layer + server-side key storage + two controllers + a real Settings page + 13 passing tests,
verified live against actual Groq responses (a genuine 401 included). Treat the remaining rows as a
planning input to keep revisiting, not a commitment — ETA below restarts from "now" since P4 is done.

Assumptions: "Effort" is focused build time (implementation + your review/testing), not wall-clock.
"ETA" is cumulative calendar time from now assuming a **part-time pace of ~2 sessions/week at 3–4
hours each (~7 hrs/week)** — rescale directly if your actual pace differs. "Tokens/session" is a
rough order-of-magnitude Claude budget for one typical work session on that phase (prompt + tool
output + iteration), not a hard cap. "Model" is which Claude model this session used to build that
phase.

| # | Phase | Effort (hrs) | ETA (cumulative) | Tokens/session | Model |
|---|---|---|---|---|---|
| **P5** | LLM codegen + self-repair | 12–16 | Week 2 | ~350–450K | **Opus 5** — staging-compile + retry-loop debugging, incl. Windows file-locking, is the riskiest logic in the plan |
| **P6** | Test case export | 4–6 | Week 3 | ~100K | Sonnet 5 |
| **P7** | Inspector backend | 10–14 | Week 5 | ~250–300K | **Opus 5** — JS-injection/Selenium interplay is fiddly to debug; Sonnet 5 is fine once the pattern is proven |
| **P8** | Inspect UI + label suggestions | 8–10 | Week 6 | ~150K | Sonnet 5 |
| **P9** | Generate end-to-end (+ assertions/edge cases/outlines) | 14–18 | Week 8 | ~300–400K | Sonnet 5 |
| **P10** | Execution + Report | 10–12 | Week 10 | ~200K | Sonnet 5 |
| **P11** | Failure analyzer UI | 4–6 | Week 11 | ~100K | Sonnet 5 (Haiku 4.5 viable — mostly UI wiring onto an existing skill) |
| **P12** | Auto-heal | 6–8 | Week 12 | ~150K | Sonnet 5 |
| | **Total remaining** | **~68–90 hrs** | **~12 weeks** | | |

Two things worth knowing about the model column: Sonnet 5 is the default here because it's what
this whole project has been built with and it's handled everything so far without trouble. The two
Opus 5 call-outs (P5, P7) aren't about raw capability — they're the two phases with the most
"integration with something outside your control" (a compiler, a live browser's JS engine) rather
than "write code against a known API," which is where the extra reasoning tends to pay off in fewer
debugging round-trips (P4 bore this out — a known-shape HTTP API, and Sonnet 5 built it inside
estimate with no stalls). If P6 stalls unexpectedly, escalating to Opus 5 for that session is a
reasonable move — none of this is a hard rule.

---

## 3. Groq integration

**Model:** `openai/gpt-oss-120b` — production on GroqCloud, 131,072-token context, ~500 tok/sec.
Verified against Groq's live docs on 2026-08-28. Facts that shape the design:

- OpenAI-compatible endpoint: `https://api.groq.com/openai/v1/chat/completions`.
- **Strict structured output supported:** `response_format: {type:"json_schema", json_schema:{name,
  strict:true, schema}}`. Strict mode requires every field marked `required` and
  `additionalProperties:false`. This is why codegen returns a validated `{files:[{path,content}]}`
  object rather than prose that has to be scraped for code blocks.
- **Structured outputs cannot stream.** Generation is a spinner with a status line, not a token feed.
- `reasoning_effort` = `low` | `medium` | `high` (default `medium`). `reasoning_format` is *not*
  supported on gpt-oss models — use `include_reasoning` instead.
- The 131k context is what makes the few-shot approach viable: the entire Phase 1 hand-written
  sample, the deterministic generator's output, and the captured flow all fit in one prompt.

### Seven jobs

Each is a typed "skill" over one shared `GroqClient` transport — one HTTP client, with per-job
prompt, JSON schema, and `reasoning_effort`. Not seven copy-pasted clients.

| # | Skill | Effort | Fallback when the LLM is unavailable |
|---|---|---|---|
| 1 | **Script generation** | high | Deterministic generator output |
| 2 | Step-label suggestion (live, during inspect) | low | User types the label manually |
| 3 | Assertion inference | medium | User captures assertion steps explicitly |
| 4 | Edge-case scenario generation | medium | Happy path only |
| 5 | Scenario Outline / `Examples` expansion | medium | Single non-parameterized scenario |
| 6 | Test-case prose (for the export) | low | Template text derived from labels |
| 7 | Failure analysis | medium | Raw error + stack trace shown as-is |

**Every skill degrades gracefully. The tool must remain fully usable with no API key configured** —
that path gets explicitly verified at every phase that touches Groq.

### The generate → validate → repair loop

```
captured TestFlow
      │
      ├─► deterministic generator ──► baseline output
      │                                    │
      │   ┌────────────────────────────────┘  used as few-shot reference
      ▼   ▼
  Groq gpt-oss-120b  ◄── prompt also carries: Phase 1 sample files,
      │                  LocatorRepository / DriverContext API surface,
      │                  allowed By strategies, exact package versions
      ▼
  {files:[{path,content}]}   (strict JSON schema)
      │
      ▼
  write to STAGING copy of the test project
      │
      ▼
  dotnet build ──FAIL──► feed compiler errors + failing code back to Groq
      │                  (retry, capped)
      │ PASS                     │ still failing after cap
      ▼                          ▼
  promote staging → tests/   fall back to deterministic output
                             (surfaced in the UI, never silent)
```

Two properties matter here:

- **Staging isolation.** A bad LLM attempt must never corrupt the real `tests/` project, and the
  compile must be realistic — the project's existing `Support/*.cs` and other flows' files have to be
  present for the build to mean anything. One staging directory is reused across generations so
  NuGet restore stays cached.
- **Provenance is visible.** The UI always shows which path produced the code: LLM, LLM-after-N-
  repairs, or deterministic fallback. Silently shipping fallback output would hide a real quality
  signal about the prompt.

**Speculative output gets a review step.** Edge-case scenarios and Scenario Outlines (skills 4 and 5)
are the model's inventions, not the user's recording. They are presented for accept/reject before
being written to disk — never written silently.

---

## 4. Test case export

Renders the same `TestFlow` to human-readable documentation instead of code — for manual testers,
test management tools, and compliance records. This is a second *renderer* of the captured flow, not
a separate capture path.

- **Project:** `backend/WebTestToolkit.Export/` → `Contracts`.
  NuGet: **ClosedXML 0.105.1** (MIT, .NET Standard 2.0, actively maintained) for `.xlsx`;
  `System.Xml.Linq` (BCL) for XML.
- **XML flavor:** a generic, readable custom schema — transformable into any tool's import format
  later with XSLT or a small script.

  ```xml
  <TestSuite name="Login">
    <TestCase id="TC-001" priority="High" source="Recorded">
      <Title>Successful login with valid credentials</Title>
      <Precondition>User is on the login page</Precondition>
      <Steps>
        <Step number="2">
          <Action>Enter the username</Action>
          <TestData>tomsmith</TestData>
          <ExpectedResult>Username field contains the value</ExpectedResult>
        </Step>
      </Steps>
    </TestCase>
  </TestSuite>
  ```

- **Excel layout:** `Test Case ID | Title | Precondition | Priority | Source | Step # | Action |
  Test Data | Expected Result | Last Run Status`, plus a summary sheet (flow name, URL, generated-at,
  counts).
- **Scope — all four enabled:**
  1. the recorded happy path;
  2. LLM-generated edge cases (reuses skill 4);
  3. Scenario Outline rows expanded to one test case per data row — what a manual tester actually
     executes, rather than a single parameterized case;
  4. a last-run status column populated from the most recent `RunSummary`, so the export doubles as
     an execution record.
- **Prose:** skill 6 writes proper manual-test-case wording and per-step expected results;
  deterministic templates derived from `TestStep.Label` + `ActionType` are the fallback.
- **New Contracts models:** `TestCaseStep` (Number/Action/TestData/ExpectedResult),
  `TestCaseDocument` (Id/Title/Precondition/Priority/Source/LastRunStatus/Steps), `TestCaseSuite`.

Depends only on `Contracts` + a `TestFlow`, so it is buildable and testable before the Inspector
exists.

---

## 5. Planned API surface

```
Inspect     POST   /api/inspect/start          { url }            → { sessionId }
            POST   /api/inspect/{id}/label     { label, actionType, value }
            GET    /api/inspect/{id}/suggest   { elementRef }     → suggested label + action (skill 2)
            POST   /api/inspect/{id}/stop
            HUB    /hubs/inspector             → ElementCaptured events

Flows       GET    /api/flows                                     → saved flows
            POST   /api/flows/generate         { TestFlow }       → files + provenance (LLM | repaired | fallback)
            POST   /api/flows/suggest-assertions { TestFlow }     → proposed assertions   (skill 3)
            POST   /api/flows/suggest-edge-cases { TestFlow }     → proposed scenarios    (skill 4)
            POST   /api/flows/suggest-outline    { TestFlow }     → outline + examples    (skill 5)

Export      GET    /api/flows/{name}/testcases                    → TestCaseSuite (preview)
            GET    /api/flows/{name}/export?format=xlsx|xml&scope=…  → file download

Run         POST   /api/run                    { flowNames? }     → { runId }
            GET    /api/run/{id}                                  → RunSummary
            GET    /api/run/{id}/export?format=csv|html
            HUB    /hubs/run                   → live console output

Failures    POST   /api/failures/analyze       { scenarioResult } → FailureAnalysis (skill 7)
            GET    /api/screenshots/{file}                        → image

Auto-heal   GET    /api/locators                                  → pages + keys
            POST   /api/autoheal/start         { page, key }      → single-capture session
            POST   /api/autoheal/apply         { page, key, locator }

Settings    GET    /api/settings               → model, key-is-set flag (never the key itself)
            PUT    /api/settings               → { groqApiKey?, groqModel }
```

**Security:** the Groq API key lives server-side only (`appsettings.json` / .NET user secrets /
`GROQ_API_KEY` env var). It is never sent to the browser — `GET /api/settings` returns whether a key
is configured, never its value.

---

## 6. Suggested enhancements

Beyond the committed roadmap above. Roughly ordered by value-for-effort within each group.

### Quick wins

- **Headless toggle + multi-browser** — Firefox/Edge alongside Chrome; headless for CI.
  `DriverContext` already centralizes driver creation, so this is small and contained.
- **Flow editor** — reorder, rename, or delete captured steps before generating. Today a misclick
  means re-recording the whole flow.
- **Richer assertions** — URL, page title, attribute value, element count, CSS property, element
  *absence*. Currently only text-contains and visibility.
- **Tags** — emit `@smoke` / `@regression` so subsets can be run with `--filter`.
- **Environment config** — dev/staging/prod base URLs so one generated suite runs anywhere. Locator
  JSON already stores a `url` per page; this generalizes it.
- **Element thumbnails on capture** — crop a screenshot of each captured element so the step list is
  visually verifiable, not just text.

### High value

- **Runtime self-healing** — `CapturedElement` already stores *ranked* candidates but only the best
  one is written. Persist the full list and have `FindVisible` fall through the chain at runtime,
  logging which fallback worked. Converts hard failures into warnings, and the log says exactly what
  to re-record.
- **iframe + Shadow DOM support** — a hard blocker on many real applications (payment widgets,
  embedded editors, most component libraries). The injected overlay needs to pierce both.
- **Session reuse** — save cookies after login so every test doesn't re-authenticate. Large speedup
  on any authenticated suite.
- **Round-trip import** — parse existing `.feature` files back into a `TestFlow` so tests can be
  edited in the UI rather than only ever created fresh.
- **Shared page objects** — a common header/nav/login page object reused across flows instead of
  regenerating one per flow.
- **Historical trends** — persist `RunSummary` per run (SQLite) to chart pass rate over time and flag
  newly-failing or flaky tests.

### AI / Groq — beyond the seven committed skills

- **LLM locator repair** — on failure, send the current DOM plus the broken locator and let the model
  propose a replacement. This is auto-heal *without* the manual re-inspect step, and is arguably the
  flagship feature the current design is one step away from.
- **Natural language → flow** — "log in as admin and verify the dashboard loads" drafts a flow you
  then confirm by inspecting.
- **Smart test data** — realistic names/emails/addresses and adversarial values (unicode, boundary
  lengths, injection strings) instead of hand-typed ones.
- **Run-level summary** — "7 failures, 6 share one root cause: the login endpoint is timing out"
  instead of seven separate per-scenario explanations.
- **Prompt/response caching** — codegen is the most expensive call in the tool; skip it entirely when
  the input flow is unchanged.

### Ambitious

- **Visual regression** — baseline screenshot diffing per step.
- **Accessibility scanning** — axe-core injected during runs, violations in the report.
- **API test generation** — capture network calls during inspect, generate API-level tests alongside
  UI ones.
- **Parallel execution** — NUnit parallelizable fixtures; needs per-scenario driver isolation
  (already true) and unique screenshot paths (already timestamped).
- **CI/CD scaffolding** — generate a GitHub Actions workflow that runs the suite on push.
- **Video recording** of runs, and Slack/email report delivery.

---

## 7. Known risks

1. **LLM emits code referencing methods that don't exist** — the entire reason for the
   compile-verify-repair loop. The prompt must carry the *actual* `LocatorRepository` /
   `DriverContext` API surface, not a prose description of it.
2. **Prompt injection from the app under test.** DOM text, element labels, and page content are fed
   into prompts, and a hostile or careless page could contain text that reads as instructions. Treat
   all captured DOM as untrusted data, fence it clearly in prompts, and never let model output decide
   file paths — the API decides where files land, not the model.
3. **`.trx` schema unverified** for this exact Reqnroll 3.3.4 / NUnit3TestAdapter 4.5.0 / .NET 8
   combination — inspect a real `results.trx` before writing `TrxParser`.
4. **Selenium Manager** needs outbound internet on first run and can lag Chrome releases. Two
   independent driver-creation sites (test execution and inspector) double the exposure. Wrap both in
   a friendly error.
5. **Orphaned Chrome processes** — a browser session held in server memory across HTTP requests needs
   an idle timeout and cleanup on client disconnect, or sessions accumulate.
6. **Groq model deprecation** — the model ID is a stored setting, not a constant, so this stays a
   config change rather than a code change.
7. **Cost and latency per generation** — a high-effort codegen call plus up to N repair attempts is
   the most expensive operation in the tool. Cache aggressively; don't re-call on unchanged input.
8. **Auto-heal scope** — handles "same element, changed locator" only. Structural page changes
   (added/removed fields) still need a re-record. Say so in the UI.
9. **New stack surface** — ASP.NET Core, React, TypeScript, Vite, and SignalR all arrive at once in
   P3. That is the deliberate trade for a real client/server architecture; expect that phase to be
   mostly learning curve rather than visible feature progress.
