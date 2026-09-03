# ADO Defect LLM Analysis

Pulls closed defects out of Azure DevOps, has an LLM classify root cause and flag
testing gaps, aggregates the results, and exports them for Power BI (or a standalone
Streamlit dashboard) — the "AI does judgment, human/BI tool does presentation"
companion to the Web Test Toolkit in this repo.

See [docs/PHASE-PLAN.md](docs/PHASE-PLAN.md) for what each phase does and its
current status.

## Setup

```bash
cd ado-defect-analysis
python -m venv .venv
source .venv/bin/activate        # Windows: .venv\Scripts\activate
pip install -r requirements-dev.txt

cp .env.example .env
# fill in ADO_ORGANIZATION / ADO_PROJECT / ADO_PAT and GROQ_API_KEY
```

## Run it

```bash
# one defect-analysis pass end to end
python -m ado_defect_analysis.cli run-all

# or step by step
python -m ado_defect_analysis.cli fetch
python -m ado_defect_analysis.cli categorize
python -m ado_defect_analysis.cli report
python -m ado_defect_analysis.cli export

# optional standalone dashboard
streamlit run dashboard/streamlit_app.py
```

Output lands in `data/`: `defects.db` (SQLite — raw pull + categorizations) and
`exports/` (`categorized_defects.csv`/`.xlsx`, `narrative_summary.json`). Both are
gitignored; regenerate them by re-running the pipeline.

## Tests

```bash
pytest
```

All tests run offline — the Groq HTTP layer is mocked (`responses`), and the LLM
pipeline stages take an injectable `LlmProvider`, so no API key is needed to run
the suite.

## LLM provider: Groq today, Copilot as a placeholder

`LLM_PROVIDER` in `.env` picks the backend. Every pipeline stage codes against the
`LlmProvider` interface (`src/ado_defect_analysis/llm/base.py`) — it never imports
Groq or Copilot directly — so switching providers is a config change:

- `LLM_PROVIDER=groq` (default) — implemented, calls Groq's OpenAI-compatible chat
  completions API. Needs `GROQ_API_KEY`.
- `LLM_PROVIDER=copilot` — a placeholder for a future GitHub Copilot / GitHub Models
  integration (`llm/copilot_provider.py`). It's wired into the factory and `Config`
  already has `COPILOT_API_KEY`/`COPILOT_MODEL` fields, but `complete_json` currently
  raises `LlmProviderError` explaining it isn't implemented yet. When Copilot exposes
  a usable inference endpoint, that one class is what needs writing — no other file
  in the pipeline changes.

## Repository layout

```
src/ado_defect_analysis/
  config.py           Env-driven settings (ADO connection, LLM provider + keys, paths)
  models.py           Defect / DefectCategorization dataclasses
  ado_client.py        Azure DevOps WIQL query + batched work-item fetch
  storage.py            SQLite persistence (defects, categorizations)
  llm/
    base.py             LlmProvider interface
    groq_provider.py     Groq implementation
    copilot_provider.py  Future-provider placeholder (see above)
    factory.py            LLM_PROVIDER -> LlmProvider
  prompts/               Markdown prompt templates
  schemas/                Expected JSON response shapes
  pipeline/
    fetch.py              Phase 1 — ADO -> SQLite
    categorize.py          Phase 2 — batch LLM categorization
    aggregate.py            Phase 3a — stats from categorized defects
    report.py                Phase 3b — LLM narrative summary
    export.py                 Phase 4 — CSV/Excel for Power BI
  cli.py                       Entrypoint: fetch / categorize / report / export / run-all
dashboard/streamlit_app.py     Phase 5 — optional standalone dashboard
tests/                          pytest suite, all offline
docs/PHASE-PLAN.md              Phase-by-phase plan and status
```
