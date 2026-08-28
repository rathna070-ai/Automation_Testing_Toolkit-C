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

### Implemented — P1 through P11

| Phase | Delivers | Key files |
|---|---|---|
<<<<<<< HEAD
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
=======
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
| **P11 — Failure analyzer UI** | `FailuresPage.tsx` — reads the same `GET /api/execution/runs/latest` P10 already built, filters `RunSummary.scenarios` to `outcome: 'failed'`, and renders each with its error, stack trace (scrollable, capped height), and screenshot filename. **Zero backend changes needed**: skill 7 (`FailureAnalysisSkill`) and `POST /api/failures/analyze` were already complete since P4 — this phase was exactly what §2's estimate table called it, UI wiring onto an existing skill | `frontend/src/pages/FailuresPage.tsx` |
| ↳ Per-failure "Analyze with Groq" | Calls the existing endpoint with that one `ScenarioResult`, on request rather than automatically for every failure — an LLM call has real latency/cost, and not every failure needs an explanation once the error message alone is enough. Same no-API-key discipline as skill 2/4: a `GET /api/llm/status` check on load shows an upfront note rather than letting the user discover unavailability only after clicking | `frontend/src/pages/FailuresPage.tsx` |
| ↳ Bug found and fixed while verifying live | The error-message `<p>` had no `overflow-wrap`, so a long unbroken token (a CSS selector inside a `NoSuchElementException` message, no spaces to break on) pushed the whole page into horizontal scroll instead of wrapping inside its own card — only visible once a *real* failure with a *real* Selenium error was rendered, not from static review. Fixed with `overflowWrap: 'anywhere'`; the identical latent bug existed in `ReportPage.tsx`'s error cell (same unwrapped text, just never exercised with a long enough error to notice) and was fixed there too | `frontend/src/pages/{FailuresPage,ReportPage}.tsx` |
>>>>>>> 33381b166eecb60841e9c8b590c8c073c40016ce

**Verified:** 137/137 backend tests, `dotnet build` clean (Debug + Release, 15 projects), frontend
`tsc`/`vite build`/`oxlint` clean. Every phase from P5 on was also exercised live in a real browser
against the running API before being marked done, including deliberately forcing real compiler
errors (P5) and real test failures (P10/P11) rather than relying on canned data.

> The deterministic generator (P1–P2) is not superseded by the LLM work — it's what makes the LLM
> work safe: the guaranteed-correct few-shot example in the codegen prompt, and the fallback when
> the LLM's output won't compile.

**Superseded:** the WPF app (`src/WebTestToolkit.App`) is retired, replaced by `frontend/` +
`WebTestToolkit.Api`; its planned windows became React pages (all built except auto-heal's, P12).
`Contracts` and `CodeGenerator` carried over untouched.

### Roadmap — not yet implemented

| Phase | Adds | Acceptance |
|---|---|---|
| **P12** | Auto-heal — locator picker, single-capture re-inspect session, `LocatorJsonPatcher` rewrites one JSON entry | Break a locator → heal it → `git diff` shows zero `.cs` changes → test passes |
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

<<<<<<< HEAD
### Effort and model estimates
=======
**P11 end-to-end, in a real browser, against a real failure:** the P10 walkthrough's demo
credentials happened to succeed both times, so verifying the Failures page needed a genuine failing
run — the `UsernameInput` locator was deliberately pointed at a nonexistent id, a real `dotnet test`
run produced two real `NoSuchElementException` failures with real stack traces and real screenshots,
and `/failures` correctly listed both, with the exact error text, a scrollable stack trace, and the
screenshot filename. Clicking "Analyze with Groq" (no key configured) returned the correct graceful
message inline, per-card, without disturbing the other card's state. The locator was reverted
immediately after and `git diff` confirmed clean before anything else touched that file.

Not yet exercised live: a *successful* Groq call (generation, edge-case suggestion, or analysis)
with a valid API key — that path is covered only by tests using stubbed/fixture responses in the
provider's real documented shape.
>>>>>>> 33381b166eecb60841e9c8b590c8c073c40016ce

Actuals for P3–P11: roughly 6 hrs for P1–P3; P4, P6, P8, P9, P10, P11 each finished within a single
session on **Sonnet 5** (P11 in under an hour on **Haiku 4.5** — pure UI wiring onto an existing
skill); P5 and P7 — the two phases integrating with something outside the model's control (a real
compiler, a live browser's JS engine) rather than a known API — used **Opus 5** and landed inside
their wider estimates. Pattern going forward: default to Sonnet 5; reach for Opus 5 only when a
phase is mostly "make an external, non-deterministic system behave," not "write code against a
known API"; Haiku 4.5 is viable for small, mechanical, additive changes matching an existing pattern.

<<<<<<< HEAD
| Phase | Effort (hrs) | Model |
|---|---|---|
| **P12** — Auto-heal | 6–8 | Sonnet 5 |
| **P13** — items 1/2/5 | 1–2 hrs each | Haiku 4.5 viable (mechanical, additive) |
| **P13** — item 4 (element-state capture) | 3–4 | Sonnet 5 (injected JS + 3 backend layers + browser test) |
| **P13** — item 3 (severity tiers) | 3–4 | Sonnet 5 (changes a shared contract + a gating condition) |
| **Deferred from P9/P10** — skills 3 & 5, `[AfterStep]` screenshots, screenshot preview | 10–14 | Sonnet 5 |
=======
### 🔄 Superseded (done)

| Item | Disposition |
|---|---|
| `src/WebTestToolkit.App` (WPF) | **Retired.** Replaced by `frontend/` + `WebTestToolkit.Api`. |
| Planned WPF windows (`InspectorWindow`, `ReportWindow`, `FailureAnalyzerWindow`, `SettingsWindow`) | Became React pages — `InspectPage`, `ReportPage`, `FailuresPage`, and `SettingsPage` are all built (P8/P10/P11/P4). Only `AutoHealPage`-equivalent (P12) remains a stub. |
| `DispatcherTimer` polling design | **Built (P7).** `InspectorBroadcastService` polls each session's JS queue and pushes to the frontend over SignalR. |
| "User labels every captured step manually" | Softened to "user *confirms or edits* an LLM-suggested label" — see §3, skill 2. Manual entry remains the fallback when no API key is configured. |

Nothing already built was wasted — `Contracts` and `CodeGenerator` carried over untouched into
`backend/`.

### ⬜ Not yet implemented

| # | Phase | What it adds | Acceptance |
|---|---|---|---|
| **P12** | **Auto-heal** | Locator picker, single-capture re-inspect session, `LocatorJsonPatcher` rewrites one JSON entry | Break a locator → heal it → `git diff` shows zero `.cs` changes → test passes |

P12 (auto-heal) is the last phase on the original roadmap, and the one that still needs new capture
machinery: a single-element re-inspect session, reusing most of P7's `InspectorSession` plumbing
rather than building a second capture path from scratch.

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
their rows above for exactly what was cut and why. P11 landed in under an hour on Haiku 4.5,
confirming the effort table's own call that it was "mostly UI wiring onto an existing skill" — the
only real work was the frontend page plus the wrap-overflow bug it surfaced. ETA below restarts
from "now".

Assumptions: "Effort" is focused build time (implementation + your review/testing), not wall-clock.
"ETA" is cumulative calendar time from now assuming a **part-time pace of ~2 sessions/week at 3–4
hours each (~7 hrs/week)** — rescale directly if your actual pace differs. "Tokens/session" is a
rough order-of-magnitude Claude budget for one typical work session on that phase (prompt + tool
output + iteration), not a hard cap. "Model" is which Claude model this session used to build that
phase.

| # | Phase | Effort (hrs) | ETA (cumulative) | Tokens/session | Model |
|---|---|---|---|---|---|
| **P12** | Auto-heal | 6–8 | Week 1 | ~150K | Sonnet 5 |
| **Deferred from P9/P10** | Skills 3 & 5 (assertion inference, Outline expansion + `TestFlow` schema work), `[AfterStep]` screenshots, inline screenshot serving on the Report page | 10–14 | Week 3 | ~200–250K | Sonnet 5 |
| | **Total remaining** | **~16–22 hrs** | **~3 weeks** | | |

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
>>>>>>> 33381b166eecb60841e9c8b590c8c073c40016ce

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

Auto-heal   GET    /api/locators                                  → pages + keys                [P12]
            POST   /api/autoheal/start         { page, key }      → single-capture session
            POST   /api/autoheal/apply         { page, key, locator }

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
7. **Auto-heal scope (P12)** — will handle "same element, changed locator" only; structural page
   changes still need a re-record.
8. **The hand-written sample suite depends on a live third-party site**
   (`the-internet.herokuapp.com`) and has been observed flaky (a `503` unrelated to any toolkit
   change). `Inspector.Tests` already solves this for its own suite with a local `TinyWebServer`
   fixture; the sample suite would benefit from the same fix but hasn't been converted.
9. **No `LICENSE` file** — defaults to "all rights reserved" under GitHub's terms. Fine for a
   private tool; a blocker if the repo is ever opened up. Owner's call, not made here.
