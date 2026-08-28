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
│   ├── WebTestToolkit.CodeGenerator.Tests/
│   ├── WebTestToolkit.Llm/                 Groq client + prompt skills
│   ├── WebTestToolkit.Llm.Tests/
│   ├── WebTestToolkit.Inspector/           Selenium + injected JS capture overlay
│   ├── WebTestToolkit.Inspector.Tests/     + 4 opt-in tests that drive real Chrome
│   ├── WebTestToolkit.Execution/           dotnet test runner + .trx parsing + run reports
│   ├── WebTestToolkit.Execution.Tests/
│   ├── WebTestToolkit.Export/               Test case docs → Excel / XML
│   └── WebTestToolkit.Export.Tests/
├── frontend/                               React + Vite + TypeScript
│   ├── src/pages/                          Inspect · Flows · Run · Report · Failures · Export · Settings
│   ├── src/api/                            Typed fetch wrappers + SignalR client
│   └── src/components/
├── tests/
│   └── WebTestToolkit.GeneratedTests/      The output. Standalone.
└── docs/
```

Every backend library depends on `Contracts`; `Export` also depends on `Llm` (it calls the test-case
prose skill directly, the same way `Execution` calls the script-generation skill). `Api` references
all of them. Nothing references `GeneratedTests`. 13 `.csproj` in total (7 libraries + `Api`, all 7 with
a matching `.Tests` project — the last gap, `Export.Tests`, closed with P6).

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
| ↳ Error handling | `AddProblemDetails()` + `UseExceptionHandler()` (non-Development) / `UseDeveloperExceptionPage()` (Development) — an unhandled exception returns RFC 7807 JSON, never a bare stack trace, regardless of environment | `Api/Program.cs` |
| ↳ Empty backend projects (P3) | `Llm`, `Inspector` (+ Selenium.WebDriver 4.48.0), `Execution`, `Export` (+ ClosedXML 0.105.1) — scaffolded and wired to `Contracts`/`Api`. All four have since been filled in (P4/P5/P6/P7) | `backend/WebTestToolkit.{Llm,Inspector,Execution,Export}/` |
| ↳ Frontend shell | React + Vite + TS, react-router, a stub page per planned feature area, typed API client, SignalR wrapper, dev-server proxy to the API | `frontend/` |
| ↳ Repo hygiene | CI (`dotnet build`+`test` on Windows, `npm run lint`+`build` on Ubuntu, every push/PR to `main`), root `.editorconfig` (CRLF for `.cs` matching what's already committed, LF for the frontend), TypeScript `strict: true` (already clean — no code changes needed to turn it on) | `.github/workflows/ci.yml`, `.editorconfig`, `frontend/tsconfig.app.json` |
| ↳ `CapturedElement.BestLocator` bug fix | Was throwing on an element with zero locator candidates; now nullable, and `LocatorJsonGenerator` skips such elements instead of crashing | `Contracts` / `CodeGenerator` |
| **P4 — Groq foundation** | `GroqClient` (hand-rolled HTTP, no SDK) against Groq's OpenAI-compatible endpoint, strict `json_schema` structured outputs, per-request auth (never mutates shared client state) | `backend/WebTestToolkit.Llm/Transport/` |
| ↳ Skill pattern | `LlmSkill<TInput,TOutput>` base (prompt+schema → typed result, one shared deserialize/error path) and the first concrete skill, `FailureAnalysisSkill` | `Llm/Skills/` |
| ↳ Prompts & schemas | Embedded `.md`/`.json` resources (`WTT_PROMPT_DIR` env var overrides with loose files for fast iteration), loaded via `PromptLibrary` | `Llm/Prompts/`, `Llm/Schemas/` |
| ↳ Server-side key storage | `FileSettingsStore` — `%AppData%\WebTestToolkit\settings.json`, API key encrypted at rest via Windows DPAPI (CurrentUser scope), `GROQ_API_KEY` env var as fallback when nothing's saved | `Api/Services/FileSettingsStore.cs` |
| ↳ Endpoints | `GET/PUT /api/settings` (key never returned, only whether one's set), `GET /api/llm/status`, `POST /api/failures/analyze` (always 200 — `available:false` + a reason, never a 500, when no key/bad key/model hiccup) | `Api/Controllers/{Settings,Llm}Controller.cs` |
| ↳ Settings page | Real implementation: save key/model, live "Try it" panel that analyzes a canned failure through the actual pipeline | `frontend/src/pages/SettingsPage.tsx` |
| ↳ `FailureAnalysis` model extended | `Category` enum, `SuggestedLocatorFix`, `Confidence`, `IsLikelyApplicationBug`, `Model` — matches the strict schema Groq is asked to fill in | `Contracts/Models/FailureAnalysis.cs` |
| **P5 — LLM codegen + self-repair** | `HybridTestCodeGenerator` — deterministic baseline first (free, and the prompt's reference implementation), then LLM, then static validation, then a real sandbox compile, then compiler-error-fed repair turns, then deterministic fallback. Provenance recorded per attempt | `Execution/Generation/HybridTestCodeGenerator.cs` |
| ↳ Skills | `ScriptGenerationSkill` (high reasoning effort, 8192-token cap) and `ScriptRepairSkill` — repair is a genuine multi-turn continuation replaying the original request and the model's own prior answer | `Llm/Skills/` |
| ↳ Static guardrails | `StaticValidator` — path whitelist, **hardcoded-`By` ban** (the auto-heal invariant), locator-strategy enum, locator closure, forbidden patterns (`Thread.Sleep`, hooks, driver construction), Gherkin sanity, and `BindingIndex` conflict detection for ambiguous Reqnroll steps | `Execution/Generation/StaticValidator.cs` |
| ↳ Build sandbox | `BuildSandbox` — a persistent mirror under `%LOCALAPPDATA%`, outside the repo, so a bad candidate can never break the user's real suite. Incremental, restore cached, Windows file-locking handled | `Execution/Generation/BuildSandbox.cs` |
| ↳ Compiler feedback | `MsBuildErrorParser` — relative paths, dedupe, cap at 25, plus ±2 lines of source context around each error to make repairs land | `Execution/Generation/MsBuildErrorParser.cs` |
| ↳ Locator files | `LocatorFileBuilder` — the toolkit serializes `.locators.json`, never the model, so the shape stays byte-identical to what `LocatorRepository` and future auto-heal expect | `Execution/Generation/LocatorFileBuilder.cs` |
| ↳ Endpoints + UI | `POST /api/flows/preview` (full pipeline, writes nothing) and `/generate`; Flows page with provenance badge, attempts drawer, and a deterministic-vs-AI compare view. Ran against a hard-coded sample flow until P9 wired a real Inspect session through — see that row below | `Api/Controllers/FlowsController.cs`, `frontend/src/pages/FlowsPage.tsx` |
| **P7 — Inspector backend** | `InspectorSession` — one hand-driven Chrome window per session. Every WebDriver touch is serialized behind a semaphore (the session is reached from both HTTP requests and the polling service); a closed browser window is treated as a normal end, not a crash | `backend/WebTestToolkit.Inspector/InspectorSession.cs` |
| ↳ Injected overlay | `Overlay/inspector-overlay.js` (embedded resource) — hover highlight, capture-phase click/change listeners, idempotent re-injection after full page loads, and **a sessionStorage-backed queue so a click that navigates away is not lost**. It never calls `preventDefault`/`stopPropagation`: the user has to be able to walk the real flow while we watch | `Inspector/Overlay/` |
| ↳ Candidate proposal vs. ranking | The overlay *proposes* locators (only it can check uniqueness against the live DOM); `LocatorRanker` *scores* them (`id` 100 > `data-testid` 95 > `name` 85 > `aria-label` 78 > `placeholder` 72 > text-xpath 60 > **generated id 45** > css path 35 > absolute xpath 10). Framework-generated ids (`:r3:`, `ember512`, GUIDs) are detected and scored below real attributes. Strategies outside `id/css/xpath/name` are dropped, so `LocatorRepository.ToBy` can never throw | `Inspector/Capture/LocatorRanker.cs` |
| ↳ Deterministic naming | `StepLabeler` — page name from URL (skipping record ids: `/orders/48213/edit` → `OrdersEditPage`), locator keys unique per page (`RemoveButton`, `RemoveButton2`), and Gherkin-voice labels. Never echoes a password into step text. This is the no-API-key path; `StepLabelSuggestionSkill` (skill 2, P8) improves on it on request | `Inspector/Capture/StepLabeler.cs` |
| ↳ Session manager | `InspectorSessionManager` (singleton) — concurrency cap, idle timeout closing forgotten browsers, retention of stopped sessions so their steps stay readable, and disposal on host shutdown so Ctrl+C leaves no orphaned Chrome | `Inspector/InspectorSessionManager.cs` |
| ↳ Live feed | `InspectorBroadcastService` (`BackgroundService`) polls each session and pushes to `InspectHub` groups; SignalR's JSON protocol configured with the same camelCase enum converter as MVC, so the hub and REST no longer disagree on the wire shape | `Api/Services/InspectorBroadcastService.cs`, `Api/Hubs/InspectHub.cs` |
| ↳ Endpoints | `GET /api/inspect/sessions`, `POST /start`, `GET /{id}`, `POST /{id}/capture` (pause/resume), `POST /{id}/stop`, `PATCH`/`DELETE /{id}/steps/{n}`, `GET /{id}/flow`, `POST /{id}/steps/{n}/suggest-label`. Chrome failing to launch returns a 502 that says so, not an opaque 500 | `Api/Controllers/InspectController.cs` |
| ↳ Typed client | `frontend/src/api/inspect.ts` — REST wrappers plus `connectInspectFeed`, which re-sends `Subscribe` on reconnect (SignalR restores the connection but *not* group membership) | `frontend/src/api/inspect.ts` |
| ↳ Retype-broadcast bug fix (found building P8) | A retyped field's correction updated `InspectorSession`'s internal state but `Convert` returned `null` for it, so `PollCore` never included it in the batch `InspectorBroadcastService` pushes — a UI only listening live would show the typo forever. `Convert` now returns `(event, isUpdate)`; a correction reuses its original `Sequence` so a listener can upsert it. Proven with a browser test that asserts on `PollAsync`'s own return value, not just eventual internal state | `Inspector/InspectorSession.cs` |
| **P6 — Test case export** | `TestCaseSuiteBuilder` — deterministic prose always built first (the guaranteed no-API-key output), optionally enhanced by skill 6. No sandbox-compile machinery like P5's: wording can't fail to "compile", so a skill failure is a plain fall-back, not a repair loop | `backend/WebTestToolkit.Export/TestCaseSuiteBuilder.cs` |
| ↳ Prose skill | `TestCaseProseSkill` (skill 6, low effort) — given step action types, labels, and (for assertions) expected text, writes a title/precondition/per-step action+expected-result. **Never shown a real typed value** — `TestData` is filled in afterwards, mechanically, from `TestStep.InputValue`, so the model could not invent it even if it tried; a test asserts the prompt never contains it | `Llm/Skills/TestCaseProseSkill.cs`, `Llm/Prompts/test-case-prose.md` |
| ↳ Writers | `ExcelTestCaseWriter` (ClosedXML) — one row per step with case-level fields repeated, plus a Summary sheet; `XmlTestCaseWriter` (`System.Xml.Linq`) — the documented `<TestSuite><TestCase><Steps><Step>` schema. Both round-tripped in tests: the xlsx re-opens in ClosedXML's own reader, the xml re-parses with `XDocument.Load` and its declared encoding matches its actual bytes | `Export/ExcelTestCaseWriter.cs`, `Export/XmlTestCaseWriter.cs` |
| ↳ Endpoints | `POST /api/export/testcases/preview` (JSON, for a UI table), `/testcases/xlsx`, `/testcases/xml` (file downloads) — flow travels in the body, same convention as `/api/flows/preview`, since nothing persists flows by name yet | `Api/Controllers/ExportController.cs` |
| ↳ **Scope actually delivered vs. originally planned** | Ships **1 of the 4** originally-listed scope items: the recorded happy path, rendered as one `TestCaseDocument`. Skill 4 (edge cases) and `RunSummary` (last-run status) now both exist (P9/P10) but neither is wired into *this exporter* yet — that's a small remaining task, not a blocked one; Scenario Outline rows still wait on real Outline support in `TestFlow` itself. See §4 for the up-to-date per-item breakdown | `Contracts/Models/TestCaseModels.cs` |
| **P8 — Inspect UI** | `InspectPage.tsx` — start form (name/URL/headless) → live step table over the P7 SignalR feed, action-type/label/locator-key editing with dirty-tracking, per-step delete, pause/resume, stop. Verified in a real browser (Selenium driving the page itself, headless), not just `tsc`/lint | `frontend/src/pages/InspectPage.tsx` |
| ↳ Label suggestion | `StepLabelSuggestionSkill` (skill 2, low effort) — given only DOM context (tag, visible text, aria-label, associated `<label>`, ancestor context) and the deterministic label, proposes a nicer one. Read-only: the endpoint never writes the step, the suggestion lands in the (still-editable) label field for the user to accept or ignore, same review-before-write posture as P5's speculative skills. Never shown `InputValue` — same discipline as skill 6 | `Llm/Skills/StepLabelSuggestionSkill.cs`, `Api/Controllers/InspectController.cs` |
| ↳ No-API-key path | The Suggest button checks `GET /api/llm/status` once on load and disables itself with an explanation rather than round-tripping to a 200 that always says unavailable — deterministic labels are what the flow uses either way | `frontend/src/pages/InspectPage.tsx` |
| **P9 — Generate end-to-end (reduced scope, see below)** | **Inspect → Generate handoff, fully wired.** `InspectPage`'s "Send to Generate" (shown once a session is stopped) fetches the session's real `TestFlow` via `GET /{id}/flow` — not a client-side reconstruction — and hands it to `/flows` via router state; `FlowsPage` reads it if present, falling back to the built-in sample flow only when nothing was handed off. The same flow object also carries through to `/export` via a new link, so a captured session can go straight to code, edge cases, *or* documentation | `frontend/src/pages/{InspectPage,FlowsPage,ExportPage}.tsx` |
| ↳ Skill 4 — edge-case generation | `EdgeCaseGenerationSkill` (medium effort) — given only step structure (action type, label, page, and *whether* a step carries a value/expected-text — never the value itself, same discipline as skills 2/6), proposes 1–3 edge-case variants as **overrides on the existing steps**, never new ones: a new input value for a `type` step, a new expected outcome for an `assert*` step. `EdgeCaseFlowBuilder` (deterministic, no model call) turns one suggestion into a real, independently-generatable `TestFlow` — same locators and elements as the original, copied verbatim, so an edge case can never invent an element the way free-form generation could | `Llm/Skills/EdgeCaseGeneration{Skill,Models}.cs`, `Execution/Generation/EdgeCaseFlowBuilder.cs` |
| ↳ Edge-case review UI | `POST /api/flows/edge-cases` returns suggestions with each option's `TestFlow` already built; the Flows page lists them for review with **Preview / Accept & generate / Reject** per option — accepting just calls the existing `preview`/`generate` endpoints with that flow, so no new write/compile path was needed. Same review-before-write posture as every other speculative skill in this codebase | `Api/Controllers/FlowsController.cs` (`EdgeCases` action), `frontend/src/pages/FlowsPage.tsx` |
| ↳ **Not built — skills 3 and 5, deliberately deferred** | Assertion inference (skill 3) and Scenario Outline / `Examples` expansion (skill 5) are not built. Skill 3 has zero fallback risk today (assertions are already fully capturable by hand) and was the lowest-value of the three. Skill 5 needs a real `Examples`-table representation added to `TestFlow`/`CodeGenerator`/`StaticValidator`'s Gherkin handling first — a schema change, not just a new skill — and doing that properly is a bigger, separable unit of work than fit alongside the other P9 items this session. See the scope table below | — |
| **P10 — Execution + Report** | `TestRunner.RunAsync` shells `dotnet test <GeneratedTests project> --logger "trx;LogFileName=..."` via the existing `DotnetCli` (salvaged from the WPF app in P3), then hands the `.trx` to `TrxParser`. Success is judged by "did a `.trx` come back", never the process exit code — `dotnet test` exits non-zero on any failing scenario, which is a normal, useful result, not an operation failure | `backend/WebTestToolkit.Execution/{TestRunner,TrxParser}.cs` |
| ↳ `.trx` schema, verified against real output | The exact schema (`http://microsoft.com/schemas/VisualStudio/TeamTest/2010`, `Results/UnitTestResult` + `TestDefinitions/UnitTest/TestMethod/@className` for the feature name, `Output/ErrorInfo` for failures) was captured from two real `dotnet test` runs against `tests/WebTestToolkit.GeneratedTests` on this exact Reqnroll 3.3.4 / NUnit 3.14.0 / NUnit3TestAdapter 4.5.0 / .NET 8 stack — one passing, one with a deliberately forced failure (reverted afterward, `git diff` clean) — not guessed from documentation. This closes out the risk flagged in §7 | `Execution.Tests/TrxParserTests.cs` |
| ↳ Screenshot path, surfaced without a new artifact channel | `.trx` has no field for arbitrary per-test metadata, so `Support/Hooks.cs`'s existing `[AfterScenario]` failure screenshot (unchanged otherwise) now also does `Console.WriteLine($"[WTT_SCREENSHOT]{path}")` — VSTest already captures each test's console output into `Output/StdOut`, which is the only place left to smuggle it through. `TrxParser` regexes it back out | `tests/WebTestToolkit.GeneratedTests/Support/Hooks.cs` |
| ↳ Live console + tracking | `ExecutionController` (`POST /api/execution/run` → 202 + run id; `GET /runs/{id}`, `GET /runs/latest`) runs the test process as a background task and pushes each console line to `RunHub`'s `run:{id}` SignalR group as `DotnetCli`'s `IProgress<string>` callback fires — there's nothing to poll, unlike Inspector's browser-state case, since the output is already arriving synchronously. `TestRunSession` buffers every line so a client that subscribes a moment late, or reconnects, or just refreshes, still sees the full transcript and the final `RunSummary` via `GET`, not only the live push | `Api/{Controllers/ExecutionController,Hubs/RunHub,Services/TestRunSessionManager}.cs` |
| ↳ Run + Report pages | `RunPage.tsx` — trigger a run, watch console stream live (auto-scrolling), resumes watching an in-progress run on remount; `ReportPage.tsx` — pass/fail counts, per-scenario table (outcome, duration, error, screenshot filename), and CSV/HTML export generated **client-side** from the already-fetched `RunSummary` (no server round-trip needed — the data is already there) | `frontend/src/pages/{RunPage,ReportPage}.tsx`, `frontend/src/api/execution.ts` |
| ↳ **Scoped down from the original description** | Kept scenario-level failure screenshots (already correct, already working) rather than adding true `[AfterStep]` capture — a screenshot after *every* step of *every* scenario is a real perf/storage cost for a benefit `[AfterScenario]` mostly already covers (you see the page at the moment it broke). Screenshot *paths* are shown in the Report table; there's no inline preview yet, because serving them would mean guessing the generated-tests project's build-configuration-specific output directory from the API process — deferred rather than hardcoded and fragile. See §6 | — |

**Verified working:** `dotnet build WebTestToolkit.sln` clean across all 15 projects (0 warnings, 0
errors). Tests: `CodeGenerator.Tests` 5/5, `Llm.Tests` 26/26, `Execution.Tests` 54/54,
`Inspector.Tests` 35/35, `Export.Tests` 15/15, and the original Selenium suite 2/2 — **137 in
total**, plus 4 opt-in browser tests (`--filter "Category=Browser"`) that drive real Chrome.

The P5 orchestrator tests are the load-bearing ones: they fake only the model and drive the **real
compiler**, proving the first attempt failing to compile → compiler errors fed back → second attempt
compiling (`LlmRepaired`), a hardcoded `By` being rejected *before* a build is spent, and every
attempt failing still leaving the user with compiling deterministic output.

End-to-end, against the running API: a hand-authored flow generated four files, compiled in the
sandbox, was written to `tests/`, and **the generated Reqnroll/Selenium test then actually ran and
passed against the live practice site** — real Chrome, all five BDD steps executing. `POST
/api/flows/preview` was confirmed to write nothing. With no key configured, `useLlm:true` falls back
to deterministic with a clear reason rather than failing.

**P7 end-to-end, against the running API:** a SignalR client connected to `/hubs/inspect`,
`POST /api/inspect/start` opened Chrome on a local page, and the two interactions on that page
arrived as live `stepCaptured` events — correctly typed (`type` / `click`), named
(`EmailAddressInput`, `ContinueButton`), labeled ("I enter the email address") and ranked (score
100, `id`). `GET /{id}/flow` then fed straight into `POST /api/flows/preview`, which produced four
files that passed static validation with zero issues. `POST /{id}/stop` closed the browser, and no
`chromedriver.exe` survived the run.

The four opt-in browser tests cover what only a real browser can prove: a full type→type→click
login flow surviving the form submit that destroys the JS context; a retyped field collapsing into
one step rather than generating a typo-then-correction; pause suppressing capture without closing
the browser; and a user-initiated navigation being recorded while a click-caused one is *not*
(recording it would make the generated test navigate directly and skip the login itself).

**P6 end-to-end, against the running API:** `POST /api/export/testcases/preview` on a 5-step
login flow, with `useLlm:true` and no key configured, returned a full `TestCaseSuite` — confirming
the fallback engages silently and correctly rather than erroring. `POST .../testcases/xlsx` returned
`application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` bytes that `file` identifies as
a genuine "Microsoft Excel 2007+" document; `POST .../testcases/xml` returned the documented schema
exactly, `TestData` present only on the two steps that actually had it.

**P8 end-to-end, in a real browser:** a headless Selenium session drove the actual React page at
`/inspect` (not the API directly) — filled the start form, clicked Start Inspect, and watched the
live step table fill in from the same self-driving target page P7's check used. Edited a step's
label, clicked Save, and confirmed the Save button went back to disabled once the draft matched
what the server persisted. Clicked Stop Inspect and confirmed the "N step(s) captured" summary.
Screenshots taken at each stage confirm the page actually renders correctly, not just that the
assertions passed.

**P9 end-to-end, in a real browser, against the real practice login site:** started a real Inspect
session (headless), which auto-captured the initial `navigate` step with no clicks needed; stopped
it and clicked "Send to Generate"; landed on `/flows` showing the *handed-off* flow ("SmokeTestLogin
(1 step(s)), captured via Inspect") rather than the sample — confirming the full round trip through
`GET /{id}/flow` → router state → `FlowsPage` actually works, not just that each half type-checks.
Separately, clicked "Suggest edge cases" against the sample flow with no API key configured and
confirmed the exact graceful-unavailable message ("No Groq API key is configured...") renders in
place, rather than an error or a stuck spinner.

**P10 end-to-end, in a real browser:** clicked "Run tests" on `/run` and watched real `dotnet test`
console output stream in live (Reqnroll's own step-by-step narration, arriving over `/hubs/run` as
the process wrote each line) through to completion — "2/2 passed" — against the actual practice
login site (~44s total, two real Chrome sessions). `/report` then showed both scenarios with correct
feature name (`Login`, recovered from the `.trx`'s `LoginFeature` class name), scenario names,
outcomes, and durations, plus working `Export .csv`/`Export .html` buttons.

Not yet exercised live: a *successful* Groq call (generation, edge-case suggestion, or analysis)
with a valid API key, and a Report row for a *failed* scenario (the practice site's demo credentials
happened to succeed both times) — both paths are covered only by tests using stubbed/fixture
responses in the provider's or NUnit's real documented shape.

> **The deterministic generator is not superseded by the LLM work — it is what makes the LLM work
> safe.** It now serves two further roles: the guaranteed-correct few-shot example inside the codegen
> prompt, and the fallback when the LLM's output won't compile. See §3.

### 🔄 Superseded (done)

| Item | Disposition |
|---|---|
| `src/WebTestToolkit.App` (WPF) | **Retired.** Replaced by `frontend/` + `WebTestToolkit.Api`. |
| Planned WPF windows (`InspectorWindow`, `ReportWindow`, `FailureAnalyzerWindow`, `SettingsWindow`) | Become React pages — stubs exist at `frontend/src/pages/`, not yet implemented. |
| `DispatcherTimer` polling design | **Built (P7).** `InspectorBroadcastService` polls each session's JS queue and pushes to the frontend over SignalR. |
| "User labels every captured step manually" | Softened to "user *confirms or edits* an LLM-suggested label" — see §3, skill 2. Manual entry remains the fallback when no API key is configured. |

Nothing already built was wasted — `Contracts` and `CodeGenerator` carried over untouched into
`backend/`.

### ⬜ Not yet implemented

| # | Phase | What it adds | Acceptance |
|---|---|---|---|
| **P11** | **Failure analyzer UI** | Failed-scenario list, error + stack trace + screenshot, Groq explanation (skill built in P4) | Analyzing a real failure returns a useful root cause in seconds |
| **P12** | **Auto-heal** | Locator picker, single-capture re-inspect session, `LocatorJsonPatcher` rewrites one JSON entry | Break a locator → heal it → `git diff` shows zero `.cs` changes → test passes |

P11 is more contained than it looks: skill 7 (`FailureAnalysisSkill`) has been built and tested
since P4 — the phase is UI wiring onto an existing, working pipeline, plus screenshot linking now
that `ScenarioResult.ScreenshotPath` is actually populated end-to-end (P10). P12 (auto-heal) is the
one phase left that still needs new capture machinery: a single-element re-inspect session, reusing
most of P7's `InspectorSession` plumbing rather than building a second capture path from scratch.

### Effort, ETA, and model estimates

**These are estimates, not measured data.** P1–P8 are the real data points so far — roughly 6 hours
for P1–P3, P4 comfortably inside its 6–8 hr estimate, P5 (the phase flagged as riskiest) also landing
inside its 12–16 hr estimate on Opus 5, and P7 similarly inside range on Opus 5 despite being the
other "outside your control" phase (a live browser's JS engine, not a compiler). P6 and P8 both ran
under a single session each, in line with their estimates — P6 stayed simple by deliberately *not*
copying P5's sandbox-compile machinery (wording can't fail to "compile", so there's nothing to
repair), and P8 reused P7's plumbing wholesale rather than inventing new state management. P9 and
P10 also both landed in a single session on Sonnet 5, at reduced scope (P9 shipped the wiring plus
one of three new skills; P10 kept scenario-level screenshots instead of adding `[AfterStep]`) — see
their rows above for exactly what was cut and why. ETA below restarts from "now".

Assumptions: "Effort" is focused build time (implementation + your review/testing), not wall-clock.
"ETA" is cumulative calendar time from now assuming a **part-time pace of ~2 sessions/week at 3–4
hours each (~7 hrs/week)** — rescale directly if your actual pace differs. "Tokens/session" is a
rough order-of-magnitude Claude budget for one typical work session on that phase (prompt + tool
output + iteration), not a hard cap. "Model" is which Claude model this session used to build that
phase.

| # | Phase | Effort (hrs) | ETA (cumulative) | Tokens/session | Model |
|---|---|---|---|---|---|
| **P11** | Failure analyzer UI | 4–6 | Week 1 | ~100K | Sonnet 5 (Haiku 4.5 viable — mostly UI wiring onto an existing skill) |
| **P12** | Auto-heal | 6–8 | Week 2 | ~150K | Sonnet 5 |
| **Deferred from P9/P10** | Skills 3 & 5 (assertion inference, Outline expansion + `TestFlow` schema work), `[AfterStep]` screenshots, inline screenshot serving on the Report page | 10–14 | Week 4 | ~200–250K | Sonnet 5 |
| | **Total remaining** | **~20–28 hrs** | **~4 weeks** | | |

Two things worth knowing about the model column: Sonnet 5 is the default here because it's what
this whole project has been built with and it's handled everything so far without trouble. The two
Opus 5 call-outs (P5, P7) aren't about raw capability — they're the two phases with the most
"integration with something outside your control" (a compiler, a live browser's JS engine) rather
than "write code against a known API," which is where the extra reasoning tends to pay off in fewer
debugging round-trips (P4 bore this out — a known-shape HTTP API, and Sonnet 5 built it inside
estimate with no stalls). P6 and P8 reinforce the same pattern from the other side — both were
"write code against a known API" work (ClosedXML/System.Xml.Linq; React state management over an
already-proven backend) and both finished on Sonnet 5 without incident. If P9 or P10 stall
unexpectedly, escalating to Opus 5 for that session is a reasonable move — none of this is a hard
rule.

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

| # | Skill | Effort | Fallback when the LLM is unavailable | Status |
|---|---|---|---|---|
| 1 | **Script generation** | high | Deterministic generator output | ✅ P5 |
| 2 | Step-label suggestion (live, during inspect) | low | Deterministic `StepLabeler` output | ✅ P8 |
| 3 | Assertion inference | medium | User captures assertion steps explicitly | ⬜ deferred (see P9 scope note) |
| 4 | Edge-case scenario generation | medium | Happy path only | ✅ P9 |
| 5 | Scenario Outline / `Examples` expansion | medium | Single non-parameterized scenario | ⬜ deferred (needs a `TestFlow` schema change first) |
| 6 | Test-case prose (for the export) | low | Template text derived from labels | ✅ P6 |
| 7 | Failure analysis | medium | Raw error + stack trace shown as-is | ✅ P4 |

Five of seven built. Skill 4 follows the same never-show-a-real-value discipline the others already
established (see §2's P9 row) — it only ever sees step *structure*, and its edge-case values are its
own invention, never a captured one. Skill 3 was deprioritized deliberately: unlike 4 and 5 it has no
gap to fill (an assertion step is already fully capturable by hand today), so it was the easiest of
the three to defer without losing real capability.

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

## 4. Test case export — built (P6)

Renders the same `TestFlow` to human-readable documentation instead of code — for manual testers,
test management tools, and compliance records. This is a second *renderer* of the captured flow, not
a separate capture path.

- **Project:** `backend/WebTestToolkit.Export/` → `Contracts` and `Llm` (for skill 6 — the original
  plan assumed no dependency beyond `Contracts`; that held until a prose skill needed calling, at
  which point `Export` took the same dependency `Execution` already takes for the same reason).
  NuGet: **ClosedXML 0.105.1** (MIT, .NET Standard 2.0, actively maintained) for `.xlsx`;
  `System.Xml.Linq` (BCL) for XML.
- **XML flavor:** a generic, readable custom schema — transformable into any tool's import format
  later with XSLT or a small script. What's actually emitted (real output, not the illustrative
  example this replaces):

  ```xml
  <TestSuite name="Login" startUrl="..." generatedAtUtc="...">
    <TestCase id="TC-001" priority="medium" source="recorded" lastRunStatus="notRun">
      <Title>Login flow</Title>
      <Precondition>User starts at https://the-internet.herokuapp.com/login</Precondition>
      <Steps>
        <Step number="2">
          <Action>Enter the username.</Action>
          <TestData>tomsmith</TestData>
          <ExpectedResult>The field contains the entered value.</ExpectedResult>
        </Step>
      </Steps>
    </TestCase>
  </TestSuite>
  ```

- **Excel layout:** `Test Case ID | Title | Precondition | Priority | Source | Step # | Action |
  Test Data | Expected Result | Last Run Status`, one row per step with case-level fields repeated,
  plus a Summary sheet (flow name, URL, generated-at, counts by source).
- **Prose:** skill 6 (`TestCaseProseSkill`) writes the title, precondition, and per-step
  action/expected-result wording; a deterministic template derived from `TestStep.Label` +
  `ActionType` is the deployed fallback — not a description of one, verified live returning correct
  output with no API key configured.
- **Contracts models, built as planned:** `TestCaseStep` (Number/Action/TestData/ExpectedResult),
  `TestCaseDocument` (Id/Title/Precondition/Priority/Source/LastRunStatus/Steps), `TestCaseSuite`.
  `TestCaseSource` and `LastRunStatus` (reusing `ScenarioOutcome`, nullable) exist as designed.

### Scope actually delivered: 1 of the originally-planned 4

The plan called for four scope items enabled together. Only the first is real today; the other
three are schema-ready but populate nothing, because each depends on a phase that doesn't exist yet:

| # | Planned scope item | Status | Blocked on |
|---|---|---|---|
| 1 | The recorded happy path | ✅ Built | — |
| 2 | LLM-generated edge cases in the export | ⬜ Still not built *for export* | Skill 4 itself now exists (P9) and is wired into **Generate** (accept an edge case → get code) — but nothing yet turns an accepted edge case into a `TestCaseDocument` with `Source: EdgeCase` for **this** export path. That's a small, separable wiring task now that the hard part (the skill) is done |
| 3 | Scenario Outline rows, one per data row | ⬜ Not built | `TestFlow` still has no Outline/`Examples` representation — skill 5 and this both wait on the same schema work |
| 4 | Last Run Status from the most recent `RunSummary` | ⬜ Not built | `RunSummary` now exists and is fetchable (`GET /api/execution/runs/latest`, P10) but per-run, not persisted per-*flow-name* — export would need to match a suite back to "the run that covered it," which nothing does yet |

This was a deliberate scoping call, not an oversight, and it mostly still stands even with P9/P10
done: P9 built skill 4 and wired it into *Generate*, not into *this exporter* — connecting the two is
now a small task (the suggestion DTOs and `EdgeCaseFlowBuilder` are already reusable), just not one
that happened to get done this round. Item 3 still waits on real Outline/`Examples` support in
`TestFlow` itself, which nothing currently provides. `TestCaseSource.EdgeCase`/`.Outline` and the
nullable `LastRunStatus` field remain schema-ready in `Contracts` for when these land — today, every
suite still holds exactly one `TestCaseDocument`, `Source: Recorded`, `LastRunStatus: null`.

---

## 5. Planned API surface

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
            HUB    /hubs/inspect               Subscribe(sessionId)
                                               → stepCaptured (InspectorEvent), sessionState

Flows       POST   /api/flows/preview          { TestFlow, useLlm }  → files + provenance, writes nothing  [built]
  [built]   POST   /api/flows/generate         { TestFlow, useLlm }  → files + provenance, writes to tests/
            POST   /api/flows/edge-cases       { TestFlow }          → suggested edge cases (skill 4),
                                                                        each with its own already-built
                                                                        TestFlow ready for preview/generate
            (Still planned: suggest-assertions (skill 3) and suggest-outline (skill 5) — deferred, see
             §2's P9 row. No GET /api/flows — nothing persists flows by name yet; the flow travels in
             every request body, same convention Export below reuses.)

Export      POST   /api/export/testcases/preview { TestFlow, useLlm } → TestCaseSuite (JSON)   [built]
  [built]   POST   /api/export/testcases/xlsx     { TestFlow, useLlm } → .xlsx file download
            POST   /api/export/testcases/xml      { TestFlow, useLlm } → .xml file download

Execution   POST   /api/execution/run                                → 202 + { runId }, runs         [built]
  [built]          `dotnet test` against tests/WebTestToolkit.GeneratedTests as a background task
            GET    /api/execution/runs/{id}                         → RunResponse (status, console
                                                                        lines so far, RunSummary once done)
            GET    /api/execution/runs/latest                       → same, for the Report page /
                                                                        a page refresh with no id in hand
            HUB    /hubs/run                   Subscribe(runId)
                                               → consoleLine (string), runCompleted (RunResponse)

Failures    POST   /api/failures/analyze       { scenarioResult } → FailureAnalysis (skill 7)   [built]
            (Screenshot files are on disk with a path in ScenarioResult.ScreenshotPath, but nothing
             serves them yet — see §6's "Report screenshot preview" quick win.)

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

- **Report screenshot preview.** `ScenarioResult.ScreenshotPath` is populated end-to-end (P10) and
  shown in the Report table, but only as a filename with a tooltip — clicking it doesn't show the
  image. Serving it needs a static-file mapping from the API to the *generated-tests* project's own
  `Screenshots/` output folder, which lives under a build-configuration-specific path
  (`bin/Debug/net8.0/...` in dev) the API process doesn't currently know; deferred rather than
  hardcoding a path that breaks on a Release build.
- **Multi-browser for the Inspector; headless + multi-browser for generated-test execution.**
  Inspector headless is fully done, UI included — P8's start form has a working checkbox
  (`InspectorStartRequest` → `ChromeOptions`), confirmed live in a real browser walkthrough. Still
  missing: the *generated tests'* own driver (`DriverContext`) has no headless option at all, and
  neither driver-creation site supports anything but Chrome. `DriverContext` already centralizes its
  own creation, so Firefox/Edge there is small and contained.
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
3. ~~`.trx` schema unverified~~ **Resolved in P10.** Verified against two real `dotnet test` runs
   (one passing, one deliberately failed) on this exact Reqnroll 3.3.4 / NUnit3TestAdapter 4.5.0 /
   .NET 8 combination before `TrxParser` was written — see §2's P10 row.
4. **Selenium Manager** needs outbound internet on first run and can lag Chrome releases. Two
   independent driver-creation sites double the exposure. `InspectorSession.StartAsync` is wrapped —
   `POST /api/inspect/start` returns a 502 with the underlying message instead of an opaque 500.
   `DriverContext` (the *generated tests'* driver creation, `tests/.../Support/DriverContext.cs`) is
   not — a Selenium Manager failure there still surfaces as a raw `dotnet test` failure, which
   `TestRunner`/`TrxParser` (P10) will faithfully report as a build/run error with no `.trx` produced,
   but with no friendlier explanation than the raw console text `RunResponse.Error` carries.
5. **Orphaned Chrome processes — mitigated (P7).** `InspectorSessionManager` closes a session's
   browser after `IdleTimeout` (default 30 min) and disposes every live session on host shutdown, so
   Ctrl+C on the API doesn't leave Chrome running. Not yet covered: a hard process crash (not a
   graceful shutdown) still orphans whatever Chrome/`chromedriver` processes were open at the time.
6. **Groq model deprecation** — the model ID is a stored setting, not a constant, so this stays a
   config change rather than a code change.
7. **Cost and latency per generation** — a high-effort codegen call plus up to N repair attempts is
   the most expensive operation in the tool. Cache aggressively; don't re-call on unchanged input.
8. **Auto-heal scope** — handles "same element, changed locator" only. Structural page changes
   (added/removed fields) still need a re-record. Say so in the UI.
9. **New stack surface** — ASP.NET Core, React, TypeScript, Vite, and SignalR all arrive at once in
   P3. That is the deliberate trade for a real client/server architecture; expect that phase to be
   mostly learning curve rather than visible feature progress.
10. **The Phase 1 sample suite depends on a live third-party site** (`the-internet.herokuapp.com`) —
    confirmed flaky during this review: the site returned `503` and both scenarios failed on
    `#username` timing out, with zero relation to any toolkit change. `Inspector.Tests` already
    solves this for its own suite with a ~60-line `TinyWebServer` serving fixed local HTML
    (`backend/WebTestToolkit.Inspector.Tests/TinyWebServer.cs`) instead of reaching the network.
    The generated-tests sample predates that pattern and would benefit from the same fix, but it's
    a real (if small) undertaking — a local fixture page plus rewriting `LoginPage`'s locators and
    the `.feature` against it — not something to do silently as a drive-by.
11. **No `LICENSE` file.** The repo is on a personal GitHub account with no license declared, which
    defaults to "all rights reserved" under GitHub's terms — fine for a private tool, a blocker if
    the intent is ever to open it up. This is a choice for the repo owner, not something to pick on
    their behalf.
