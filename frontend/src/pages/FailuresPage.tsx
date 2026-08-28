import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { analyzeFailure, getLlmStatus, type AnalyzeFailureResponse } from '../api/client'
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
