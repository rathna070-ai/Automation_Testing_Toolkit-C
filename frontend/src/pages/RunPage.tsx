import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { connectRunFeed, getLatestRun, startRun, type RunResponse } from '../api/execution'

type Phase = 'idle' | 'starting' | 'running' | 'done' | 'error'

export function RunPage() {
  const [phase, setPhase] = useState<Phase>('idle')
  const [lines, setLines] = useState<string[]>([])
  const [result, setResult] = useState<RunResponse | null>(null)
  const [error, setError] = useState('')
  const feedCleanup = useRef<(() => Promise<void>) | null>(null)
  const consoleRef = useRef<HTMLPreElement | null>(null)

  async function watch(runId: string) {
    feedCleanup.current = await connectRunFeed(runId, {
      onLine: (line) => setLines((prev) => [...prev, line]),
      onCompleted: (final) => {
        setResult(final)
        setPhase(final.status === 'Faulted' ? 'error' : 'done')
        if (final.status === 'Faulted') setError(final.error ?? 'The run did not complete.')
      },
    })
  }

  useEffect(() => {
    // Resume watching a run already in progress — e.g. the user started one, navigated away,
    // and came back. Failing to find any run at all is the normal first-visit case.
    getLatestRun()
      .then((latest) => {
        setLines(latest.consoleLines)
        if (latest.status === 'Running') {
          setPhase('running')
          void watch(latest.runId)
        } else {
          setResult(latest)
          setPhase(latest.status === 'Faulted' ? 'error' : 'done')
          if (latest.status === 'Faulted') setError(latest.error ?? 'The run did not complete.')
        }
      })
      .catch(() => {})

    return () => {
      void feedCleanup.current?.()
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    if (consoleRef.current) consoleRef.current.scrollTop = consoleRef.current.scrollHeight
  }, [lines])

  async function handleRun() {
    await feedCleanup.current?.()
    feedCleanup.current = null

    setPhase('starting')
    setError('')
    setResult(null)
    setLines([])
    try {
      const { runId } = await startRun()
      setPhase('running')
      await watch(runId)
    } catch (e) {
      setError(String(e))
      setPhase('error')
    }
  }

  const isBusy = phase === 'starting' || phase === 'running'

  return (
    <div>
      <h1>Run</h1>
      <p>
        Runs <code>dotnet test</code> against the generated suite
        (<code>tests/WebTestToolkit.GeneratedTests</code>) and streams the console live below. A
        small suite against a real site typically takes 20–40 seconds. Full per-scenario results,
        errors, and screenshots are on the <Link to="/report">Report</Link> page.
      </p>

      <button onClick={handleRun} disabled={isBusy}>
        {phase === 'running' ? 'Running…' : phase === 'starting' ? 'Starting…' : 'Run tests'}
      </button>

      {error && <p style={{ color: '#cf222e' }}>{error}</p>}

      {result?.summary && (
        <p style={{ fontWeight: 600, margin: '0.75rem 0' }}>
          {result.summary.passed}/{result.summary.total} passed
          {result.summary.failed > 0 ? `, ${result.summary.failed} failed` : ''} · {result.summary.duration}
          {'  '}
          <Link to="/report">See full report →</Link>
        </p>
      )}

      {lines.length > 0 && (
        <pre
          ref={consoleRef}
          style={{
            background: '#0d1117',
            color: '#c9d1d9',
            padding: '1rem',
            borderRadius: 4,
            maxHeight: '24rem',
            overflow: 'auto',
            fontSize: '0.8em',
            whiteSpace: 'pre-wrap',
            marginTop: '1rem',
          }}
        >
          {lines.join('\n')}
        </pre>
      )}
    </div>
  )
}
