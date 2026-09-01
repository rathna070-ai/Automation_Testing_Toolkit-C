# Web Test Toolkit

A local toolkit that records a web flow by inspection and turns it into a runnable Selenium +
Reqnroll BDD test suite — then runs it, reports on it, explains failures with an LLM, repairs
broken locators when the app changes, and exports the flow as human-readable test case
documentation.

**Architecture, implementation status, and the phase roadmap live in
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)** — that document is the single source of truth;
this file is just the quick start.

## Where the LLM is, and is not

Worth knowing up front, because it decides what you need to configure:

> **The LLM produces data a human reviews, or data a deterministic generator consumes. It never
> writes code that ships.**

**Generating test code never calls a model.** That path is entirely deterministic, so the core
record → generate → run loop works offline and with no API key at all. A Groq key enables four
features that read or write prose rather than code — failure analysis, test-case documents,
step-label suggestions, and edge-case suggestions — and each degrades gracefully without one.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 20+](https://nodejs.org/)
- Google Chrome (the toolkit drives it via Selenium; Selenium Manager fetches a matching
  `chromedriver` automatically on first run — this needs outbound internet)
- **Windows.** The toolkit is Windows-only by design: it drives a local Chrome, stores the Groq
  API key at rest using Windows DPAPI, and reaps orphaned browser processes with a Windows Job
  Object. See `AssemblyInfo.cs` in `WebTestToolkit.Api`.

## Run it locally

```powershell
# Backend — from the repo root
dotnet run --project backend/WebTestToolkit.Api
# API on http://localhost:5000, Swagger UI at /swagger

# Frontend — in a second terminal
cd frontend
npm install
npm run dev
# UI on http://localhost:5173, proxying /api and /hubs to the backend
```

Open `http://localhost:5173`. Record a flow on **Inspect**; it is saved when you stop the
session, so it survives closing the tab and restarting the API. **Flows** and **Export** then
both let you pick it from the saved list.

## Build and test

```powershell
# Whole solution — the filter is what CI uses; see "Test categories" below
dotnet build WebTestToolkit.sln
dotnet test WebTestToolkit.sln --filter "Category!=liveSite"

# Frontend
cd frontend
npm run lint
npm run build
```

### Test categories

Two groups are excluded from a plain run, both because they need something the machine cannot
guarantee:

| Category | What it is | Run it with |
|---|---|---|
| `Browser` | The 7 tests that drive a real Chrome window — 5 Inspector capture tests, plus 2 that pin how Chrome reports an overlay-blocked click and an open JS dialog. Marked `[Explicit]`, so a plain `dotnet test` already skips them. | `dotnet test WebTestToolkit.sln --filter "Category=Browser"` |
| `liveSite` | Generated suites recorded against real third-party sites, whose result depends on those sites being up. | `dotnet test tests/WebTestToolkit.GeneratedTests --filter "Category=liveSite"` |

Everything else runs unattended, including the generated sample suite — it targets a local
fixture server rather than the internet.

Run the `Browser` set deliberately whenever you touch driver options or the generated page objects:
it is the only place real Chrome behaviour is checked, and an `[Explicit]` suite that nobody runs is
how a stale assertion once survived a whole phase.

Mutation testing over the rule layer runs weekly in CI, not per-PR — see
[docs/MUTATION-TESTING.md](docs/MUTATION-TESTING.md).

If a build or test run seems to hang or hold a file lock on Windows, it's usually a stale
MSBuild worker node — see the `MSBUILDDISABLENODEREUSE` note in
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). A running API also holds locks on its own
assemblies; stop it before rebuilding.

## Repository layout

```
backend/    ASP.NET Core Web API + the libraries it composes: Contracts, CodeGenerator, Llm,
            Inspector, Execution, Export
frontend/   React + Vite + TypeScript SPA
tests/      One .Tests project per backend library, plus WebTestToolkit.GeneratedTests — a
            standalone Reqnroll+Selenium project. That one is the *output* of the toolkit, not
            part of it; nothing in backend/ references it, so it stays runnable on its own.
docs/       ARCHITECTURE.md — architecture, status, phase roadmap, API surface, risks
            MUTATION-TESTING.md — what the weekly Stryker run covers, and why it is scoped
```

## CI

[`.github/workflows/ci.yml`](.github/workflows/ci.yml) builds and tests both sides on every push
and pull request to `main`, and runs mutation testing on a weekly schedule.
