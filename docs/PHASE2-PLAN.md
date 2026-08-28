# Phase 2 Plan — superseded

This document described the original Phase 2 plan, built around a **WPF desktop app** with phases
numbered 2.1–2.7 and Groq used only to explain test failures.

Two decisions on 2026-08-28 replaced it:

1. **The toolkit became a local client/server web app** — ASP.NET Core Web API backend + React
   frontend. The WPF app is retired.
2. **Groq moved into the generation loop.** Script generation now goes through
   `openai/gpt-oss-120b` with a compile-verify-repair cycle, and the LLM has seven jobs rather than
   one. A test case export (Excel / XML) was also added.

**See [ARCHITECTURE.md](ARCHITECTURE.md)** — it is now the single source of truth for architecture,
implementation status, the phase roadmap (P3–P12), the API surface, and known risks.

*This file is kept only so existing links don't break. It is safe to delete.*
