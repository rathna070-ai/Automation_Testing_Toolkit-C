import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  analyzeFailure,
  analyzeRun,
  getLlmStatus,
  type AnalyzeFailureResponse,
  type AnalyzeRunResponse,
} from '../api/client'
import { getLatestRun, type ScenarioResult } from '../api/execution'

type AnalyzeState = 'idle' | 'running' | 'done' | 'error'

export function FailuresPage() {
  const [phase, setPhase] = useState<'loading' | 'done' | 'empty'>('loading')
  const [failures, setFailures] = useState<ScenarioResult[]>([])
  const [note, setNote] = useState('')
  const [llmAvailable, setLlmAvailable] = useState(false)

  const [analyzeStates, setAnalyzeStates] = useState<Record<number, AnalyzeState>>({})
  const [analyzeResults, setAnalyzeResults] = useState<Record<number, AnalyzeFailureResponse>>({})
  const [analyzeErrors, setAnalyzeErrors] = useState<Record<number, string>>({})

  // Run-level triage, separate from the per-scenario analysis below. Six failures are usually
  // one problem hit six times, and only a call that sees them together can say so.
  const [runState, setRunState] = useState<AnalyzeState>('idle')
  const [runResult, setRunResult] = useState<AnalyzeRunResponse | null>(null)
  const [runError, setRunError] = useState('')

  async function triageRun() {
    setRunState('running')
    setRunError('')
    try {
      setRunResult(await analyzeRun())
      setRunState('done')
    } catch (e) {
      setRunError(String(e))
      setRunState('error')
    }
  }

  useEffect(() => {
    getLlmStatus()
      .then((s) => setLlmAvailable(s.apiKeyConfigured))
      .catch(() => setLlmAvailable(false))

    getLatestRun()
      .then((r) => {
        const failed = (r.summary?.scenarios ?? []).filter((s) => s.outcome === 'failed')
        setFailures(failed)
        setPhase(r.summary ? 'done' : 'empty')
        if (!r.summary) setNote(r.error ?? 'The last run did not produce a result.')
      })
      .catch(() => {
        // The normal first-visit case: no run has ever been started, so the API 404s.
        setPhase('empty')
      })
  }, [])

  async function handleAnalyze(index: number, scenario: ScenarioResult) {
    setAnalyzeStates((s) => ({ ...s, [index]: 'running' }))
    try {
      const result = await analyzeFailure({
        featureName: scenario.featureName,
        scenarioName: scenario.scenarioName,
        outcome: scenario.outcome,
        duration: scenario.duration,
        errorMessage: scenario.errorMessage,
        stackTrace: scenario.stackTrace,
        screenshotPath: scenario.screenshotPath,
      })
      setAnalyzeResults((r) => ({ ...r, [index]: result }))
      setAnalyzeStates((s) => ({ ...s, [index]: 'done' }))
    } catch (e) {
      setAnalyzeErrors((r) => ({ ...r, [index]: String(e) }))
      setAnalyzeStates((s) => ({ ...s, [index]: 'error' }))
    }
  }

  return (
    <div>
      <h1>Failures</h1>
      <p>
        Failed scenarios from the most recent <Link to="/run">Run</Link>, each with error, stack
        trace, and screenshot — plus a Groq root-cause explanation on request.
      </p>

      {phase === 'loading' && <p>Loading…</p>}

      {phase === 'empty' && (
        <p>
          {note || 'No test run yet.'} Go to <Link to="/run">Run</Link> to start one.
        </p>
      )}

      {phase === 'done' && failures.length === 0 && <p>No failures in the most recent run.</p>}

      {phase === 'done' && failures.length > 0 && (
        <>
          <section style={{ margin: '1rem 0', padding: '0.75rem', border: '1px solid #d0d7de', borderRadius: 4 }}>
            <div style={{ display: 'flex', gap: '1rem', alignItems: 'center' }}>
              <strong>Triage the whole run</strong>
              <button onClick={triageRun} disabled={runState === 'running' || !llmAvailable}>
                {runState === 'running' ? 'Grouping…' : `Group ${failures.length} failures by cause`}
              </button>
            </div>
            <p style={{ opacity: 0.7, fontSize: '0.9em', margin: '0.35rem 0 0' }}>
              Analysing failures one at a time cannot tell you that several share a cause. This
              looks at them together and reports how many distinct problems there actually are.
            </p>

            {runState === 'error' && <p style={{ color: '#cf222e' }}>{runError}</p>}

            {runState === 'done' && runResult && !runResult.available && (
              <p style={{ opacity: 0.8 }}>{runResult.unavailableReason}</p>
            )}

            {runState === 'done' && runResult?.analysis && (
              <div style={{ marginTop: '0.75rem' }}>
                <p style={{ fontWeight: 600 }}>{runResult.analysis.summary}</p>
                {runResult.analysis.groups.map((g, i) => (
                  <div
                    key={i}
                    style={{ margin: '0.5rem 0', padding: '0.5rem 0.75rem', borderLeft: '3px solid #0969da' }}
                  >
                    <div style={{ fontWeight: 600 }}>
                      {g.title}{' '}
                      <span style={{ fontWeight: 400, opacity: 0.7 }}>
                        — explains {g.scenarioNames.length} of {failures.length} · {g.category} ·
                        confidence {Math.round(g.confidence * 100)}%
                      </span>
                    </div>
                    <p style={{ margin: '0.25rem 0' }}>{g.rootCause}</p>
                    <p style={{ margin: '0.25rem 0' }}>
                      <strong>Fix:</strong> {g.suggestedFix}
                    </p>
                    {g.suggestedLocator && (
                      <p style={{ margin: '0.25rem 0' }}>
                        Suggested locator: <code>{g.suggestedLocator.page}.{g.suggestedLocator.key}</code> →{' '}
                        <code>{g.suggestedLocator.strategy}:{g.suggestedLocator.value}</code>{' '}
                        <Link
                          to="/autoheal"
                          state={{ page: g.suggestedLocator.page, key: g.suggestedLocator.key }}
                        >
                          apply it in Auto-heal
                        </Link>
                      </p>
                    )}
                    <details>
                      <summary style={{ cursor: 'pointer', opacity: 0.7 }}>Scenarios</summary>
                      <ul style={{ margin: '0.25rem 0' }}>
                        {g.scenarioNames.map((n) => (
                          <li key={n}>{n}</li>
                        ))}
                      </ul>
                    </details>
                  </div>
                ))}
              </div>
            )}
          </section>

          {!llmAvailable && (
            <p style={{ opacity: 0.75 }}>
              No Groq API key is configured — "Analyze with Groq" will say so rather than explain
              the failure. Add one on the <Link to="/settings">Settings</Link> page.
            </p>
          )}

          <div style={{ display: 'flex', flexDirection: 'column', gap: '1rem', marginTop: '1rem' }}>
            {failures.map((s, i) => {
              const state = analyzeStates[i] ?? 'idle'
              const result = analyzeResults[i]
              return (
                <section key={i} style={{ border: '1px solid #d0d7de', borderRadius: 6, padding: '0.75rem 1rem' }}>
                  <div
                    style={{
                      display: 'flex',
                      justifyContent: 'space-between',
                      alignItems: 'baseline',
                      gap: '1rem',
                      flexWrap: 'wrap',
                    }}
                  >
                    <strong>
                      {s.featureName} — {s.scenarioName}
                    </strong>
                    <span style={{ opacity: 0.7, fontSize: '0.85em' }}>{s.duration}</span>
                  </div>

                  {s.errorMessage && (
                    <p style={{ color: '#cf222e', margin: '0.5rem 0', overflowWrap: 'anywhere' }}>{s.errorMessage}</p>
                  )}

                  {s.stackTrace && (
                    <pre
                      style={{
                        background: '#f6f8fa',
                        padding: '0.5rem',
                        overflowX: 'auto',
                        fontSize: '0.8em',
                        maxHeight: 160,
                      }}
                    >
                      {s.stackTrace}
                    </pre>
                  )}

                  <p style={{ fontSize: '0.85em' }}>
                    Screenshot:{' '}
                    {s.screenshotPath ? (
                      <span title={s.screenshotPath}>{s.screenshotPath.split(/[\\/]/).pop()}</span>
                    ) : (
                      '—'
                    )}
                  </p>

                  <button onClick={() => handleAnalyze(i, s)} disabled={state === 'running'}>
                    {state === 'running' ? 'Analyzing…' : 'Analyze with Groq'}
                  </button>

                  {state === 'error' && <p>Request failed: {analyzeErrors[i]}</p>}

                  {state === 'done' && result && !result.available && <p>Not available: {result.unavailableReason}</p>}

                  {state === 'done' && result?.available && result.analysis && (
                    <div style={{ marginTop: '0.5rem', background: '#f6f8fa', padding: '0.5rem 0.75rem', borderRadius: 4 }}>
                      <p>
                        <strong>Category:</strong> {result.analysis.category}
                        {' · '}
                        <strong>Confidence:</strong> {(result.analysis.confidence * 100).toFixed(0)}%
                        {result.analysis.isLikelyApplicationBug && ' · likely an application bug, not a test bug'}
                      </p>
                      <p>
                        <strong>Root cause:</strong> {result.analysis.rootCause}
                      </p>
                      <p>
                        <strong>Suggested fix:</strong> {result.analysis.suggestedFix}
                      </p>
                      {result.analysis.suggestedLocator && (
                        <p>
                          <strong>Suggested locator:</strong> {result.analysis.suggestedLocator.strategy}=
                          {result.analysis.suggestedLocator.value} ({result.analysis.suggestedLocator.why})
                        </p>
                      )}
                    </div>
                  )}
                </section>
              )
            })}
          </div>
        </>
      )}
    </div>
  )
}
