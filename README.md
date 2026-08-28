# Web Test Toolkit

A local toolkit that records a web flow by inspection and turns it into a runnable Selenium +
Reqnroll BDD test suite — then runs it, reports on it, explains failures with an LLM, repairs
broken locators when the app changes, and exports the flow as human-readable test case
documentation.

**Architecture, implementation status, and the phase roadmap live in
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)** — that document is the single source of truth;
this file is just the quick start.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 20+](https://nodejs.org/)
- Google Chrome (the toolkit drives it via Selenium; Selenium Manager fetches a matching
  `chromedriver` automatically on first run — this needs outbound internet)
- **Windows.** The toolkit is Windows-only by design: it stores the Groq API key at rest using
  Windows DPAPI. See `AssemblyInfo.cs` in `WebTestToolkit.Api`.

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

Open `http://localhost:5173`. A Groq API key is optional — every feature that uses one degrades
gracefully without it (see **Settings** in the app, or §3 of the architecture doc).

## Build and test

```powershell
# Whole solution
dotnet build WebTestToolkit.sln
dotnet test WebTestToolkit.sln

# Frontend
cd frontend
npm run lint
npm run build
```

`dotnet test` skips the four browser-driving Inspector tests by default (they're marked
`[Explicit]` — they open real Chrome windows and aren't meant for an unattended run). Run them
on demand with:

```powershell
dotnet test backend/WebTestToolkit.Inspector.Tests --filter "Category=Browser"
```

If a build or test run seems to hang or hold a file lock on Windows, it's usually a stale
MSBuild worker node — see the `MSBUILDDISABLENODEREUSE` note in
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Repository layout

```
backend/    ASP.NET Core Web API + the libraries it composes (Contracts, CodeGenerator, Llm,
            Inspector, Execution, Export), each with a matching .Tests project
frontend/   React + Vite + TypeScript SPA
tests/      WebTestToolkit.GeneratedTests — a standalone Reqnroll+Selenium project. This is the
            *output* of the toolkit, not part of it; nothing in backend/ references it.
docs/       ARCHITECTURE.md — architecture, status, phase roadmap, API surface, risks
```

## CI

[`.github/workflows/ci.yml`](.github/workflows/ci.yml) builds and tests both sides on every push
and pull request to `main`.
