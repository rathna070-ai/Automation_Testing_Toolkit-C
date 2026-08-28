import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { getLatestRun, type RunResponse, type RunSummary, type ScenarioResult } from '../api/execution'

const OUTCOME_STYLE: Record<string, { label: string; color: string }> = {
  passed: { label: '✓ Passed', color: '#1a7f37' },
  failed: { label: '✗ Failed', color: '#cf222e' },
  skipped: { label: '○ Skipped', color: '#9a6700' },
}

function csvField(value: string): string {
  return `"${value.replace(/"/g, '""')}"`
}

function toCsv(scenarios: ScenarioResult[]): string {
  const header = ['Feature', 'Scenario', 'Outcome', 'Duration', 'Error']
  const rows = scenarios.map((s) => [s.featureName, s.scenarioName, s.outcome, s.duration, s.errorMessage ?? ''])
  return [header, ...rows].map((row) => row.map(csvField).join(',')).join('\r\n')
}

function escapeHtml(value: string): string {
  return value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
}

// Self-contained (inline styles, no external assets) so the file is meaningful on its own —
// this is meant to be forwarded or archived, not just viewed in this app.
function toHtml(summary: RunSummary): string {
  const rows = summary.scenarios
    .map((s) => {
      const tone = OUTCOME_STYLE[s.outcome]?.color ?? '#57606a'
      return `<tr>
        <td>${escapeHtml(s.featureName)}</td>
        <td>${escapeHtml(s.scenarioName)}</td>
        <td style="color:${tone};font-weight:600">${escapeHtml(OUTCOME_STYLE[s.outcome]?.label ?? s.outcome)}</td>
        <td>${escapeHtml(s.duration)}</td>
        <td>${s.errorMessage ? escapeHtml(s.errorMessage) : ''}</td>
      </tr>`
    })
    .join('\n')

  return `<!doctype html>
<html><head><meta charset="utf-8"><title>Test Run Report</title>
<style>
  body { font-family: system-ui, sans-serif; margin: 2rem; color: #1f2328; }
  table { border-collapse: collapse; width: 100%; }
  th, td { text-align: left; padding: 6px 10px; border-bottom: 1px solid #d0d7de; vertical-align: top; }
  th { background: #f6f8fa; }
</style></head>
<body>
  <h1>Test Run Report</h1>
  <p>${new Date(summary.runAtUtc).toLocaleString()} · ${summary.passed}/${summary.total} passed
    ${summary.failed > 0 ? `· ${summary.failed} failed` : ''} · ${escapeHtml(summary.duration)}</p>
  <table>
    <thead><tr><th>Feature</th><th>Scenario</th><th>Outcome</th><th>Duration</th><th>Error</th></tr></thead>
    <tbody>${rows}</tbody>
  </table>
</body></html>`
}

function downloadText(content: string, filename: string, mimeType: string) {
  const blob = new Blob([content], { type: mimeType })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = filename
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}

export function ReportPage() {
  const [phase, setPhase] = useState<'loading' | 'done' | 'empty'>('loading')
  const [run, setRun] = useState<RunResponse | null>(null)
  const [note, setNote] = useState('')

  useEffect(() => {
    getLatestRun()
      .then((r) => {
        setRun(r)
        setPhase(r.summary ? 'done' : 'empty')
        if (!r.summary) setNote(r.error ?? 'The last run did not produce a result.')
      })
      .catch(() => {
        // The normal first-visit case: no run has ever been started, so the API 404s.
        setPhase('empty')
      })
  }, [])

  const summary = run?.summary

  return (
    <div>
      <h1>Report</h1>
      <p>
        The most recent <Link to="/run">Run</Link> result — pass/fail counts, per-scenario
        detail, and errors with a link to the failure screenshot when one was captured.
      </p>

      {phase === 'loading' && <p>Loading…</p>}

      {phase === 'empty' && (
        <p>
          {note || 'No test run yet.'} Go to <Link to="/run">Run</Link> to start one.
        </p>
      )}

      {phase === 'done' && summary && (
        <>
          <div style={{ display: 'flex', gap: '1.5rem', alignItems: 'center', margin: '1rem 0', flexWrap: 'wrap' }}>
            <span style={{ fontSize: '1.1em', fontWeight: 600 }}>
              {summary.passed}/{summary.total} passed
              {summary.failed > 0 ? `, ${summary.failed} failed` : ''}
            </span>
            <span style={{ opacity: 0.75 }}>
              {new Date(summary.runAtUtc).toLocaleString()} · {summary.duration}
            </span>
            <button onClick={() => downloadText(toCsv(summary.scenarios), 'test-run-report.csv', 'text/csv')}>
              Export .csv
            </button>
            <button onClick={() => downloadText(toHtml(summary), 'test-run-report.html', 'text/html')}>
              Export .html
            </button>
          </div>

          <table style={{ borderCollapse: 'collapse', width: '100%' }}>
            <thead>
              <tr>
                {['Feature', 'Scenario', 'Outcome', 'Duration', 'Error', 'Screenshot'].map((h) => (
                  <th key={h} style={{ textAlign: 'left', padding: '4px 8px', borderBottom: '1px solid #999' }}>
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {summary.scenarios.map((s, i) => {
                const style = OUTCOME_STYLE[s.outcome] ?? { label: s.outcome, color: 'inherit' }
                return (
                  <tr key={i} style={{ verticalAlign: 'top' }}>
                    <td style={{ padding: '4px 8px' }}>{s.featureName}</td>
                    <td style={{ padding: '4px 8px' }}>{s.scenarioName}</td>
                    <td style={{ padding: '4px 8px', color: style.color, fontWeight: 600 }}>{style.label}</td>
                    <td style={{ padding: '4px 8px' }}>{s.duration}</td>
                    <td style={{ padding: '4px 8px', maxWidth: 420, fontSize: '0.85em', overflowWrap: 'anywhere' }}>
                      {s.errorMessage ?? '—'}
                    </td>
                    <td style={{ padding: '4px 8px', fontSize: '0.85em' }}>
                      {s.screenshotPath ? (
                        <span title={s.screenshotPath}>{s.screenshotPath.split(/[\\/]/).pop()}</span>
                      ) : (
                        '—'
                      )}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </>
      )}
    </div>
  )
}
