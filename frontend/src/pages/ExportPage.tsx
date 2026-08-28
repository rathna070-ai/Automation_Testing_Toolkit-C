import { useState } from 'react'
import { useLocation } from 'react-router-dom'
import {
  downloadTestCasesXlsx,
  downloadTestCasesXml,
  previewTestCases,
  type TestCaseSuite,
  type TestFlow,
} from '../api/client'
import { SAMPLE_FLOW } from './sampleFlow'

const SOURCE_LABELS: Record<string, string> = {
  recorded: 'Recorded',
  edgeCase: 'Edge case',
  outline: 'Outline row',
}

// Same narrowing as FlowsPage.tsx — location.state is untyped and may be stale or absent.
function flowFromLocationState(state: unknown): TestFlow | null {
  if (!state || typeof state !== 'object' || !('flow' in state)) return null
  const flow = (state as { flow: unknown }).flow
  return flow && typeof flow === 'object' && 'steps' in flow ? (flow as TestFlow) : null
}

export function ExportPage() {
  const location = useLocation()
  const flow = flowFromLocationState(location.state) ?? SAMPLE_FLOW
  const isSample = flow === SAMPLE_FLOW

  const [useLlm, setUseLlm] = useState(true)
  const [state, setState] = useState<'idle' | 'running' | 'done' | 'error'>('idle')
  const [suite, setSuite] = useState<TestCaseSuite | null>(null)
  const [error, setError] = useState('')
  const [downloading, setDownloading] = useState<'xlsx' | 'xml' | null>(null)

  async function handlePreview() {
    setState('running')
    setError('')
    try {
      const result = await previewTestCases({ flow, useLlm })
      setSuite(result)
      setState('done')
    } catch (e) {
      setError(String(e))
      setState('error')
    }
  }

  async function handleDownload(format: 'xlsx' | 'xml') {
    setDownloading(format)
    setError('')
    try {
      const request = { flow, useLlm }
      if (format === 'xlsx') await downloadTestCasesXlsx(request, flow.name)
      else await downloadTestCasesXml(request, flow.name)
    } catch (e) {
      setError(String(e))
    } finally {
      setDownloading(null)
    }
  }

  return (
    <div>
      <h1>Export</h1>
      <p>
        Renders a flow as manual test case documentation — for testers without an automation
        background, test management tools, or a compliance record — instead of code.{' '}
        {isSample ? (
          <>No flow was handed off, so this is the built-in sample flow ("{flow.name}").</>
        ) : (
          <>
            Showing <strong>{flow.name}</strong> ({flow.steps.length} step(s)).
          </>
        )}
      </p>

      <div style={{ display: 'flex', gap: '1rem', alignItems: 'center', margin: '1rem 0', flexWrap: 'wrap' }}>
        <label>
          <input type="checkbox" checked={useLlm} onChange={(e) => setUseLlm(e.target.checked)} />{' '}
          Use AI wording (falls back to deterministic templates if unavailable)
        </label>
        <button onClick={handlePreview} disabled={state === 'running'}>
          {state === 'running' ? 'Working…' : 'Preview'}
        </button>
        <button onClick={() => handleDownload('xlsx')} disabled={downloading !== null}>
          {downloading === 'xlsx' ? 'Downloading…' : 'Download .xlsx'}
        </button>
        <button onClick={() => handleDownload('xml')} disabled={downloading !== null}>
          {downloading === 'xml' ? 'Downloading…' : 'Download .xml'}
        </button>
      </div>

      {state === 'error' && <p style={{ color: '#cf222e' }}>Request failed: {error}</p>}
      {error && state !== 'error' && <p style={{ color: '#cf222e' }}>Download failed: {error}</p>}

      {suite && (
        <>
          <p>
            <strong>{suite.flowName}</strong> · {suite.testCases.length} test case(s) ·{' '}
            {suite.testCases.reduce((n, tc) => n + tc.steps.length, 0)} step(s) total · generated{' '}
            {new Date(suite.generatedAtUtc).toLocaleString()}
          </p>

          {suite.testCases.map((testCase) => (
            <div key={testCase.id} style={{ marginBottom: '1.5rem' }}>
              <h3 style={{ marginBottom: '0.25rem' }}>
                {testCase.id} — {testCase.title}
              </h3>
              <p style={{ margin: '0.25rem 0', opacity: 0.8 }}>
                {SOURCE_LABELS[testCase.source] ?? testCase.source} · Priority: {testCase.priority} · Last
                run: {testCase.lastRunStatus ?? 'not run'}
              </p>
              <p style={{ margin: '0.25rem 0' }}>
                <em>Precondition:</em> {testCase.precondition}
              </p>

              <table style={{ borderCollapse: 'collapse', width: '100%', fontSize: '0.9em' }}>
                <thead>
                  <tr>
                    {['#', 'Action', 'Test data', 'Expected result'].map((h) => (
                      <th key={h} style={{ textAlign: 'left', padding: '4px 8px', borderBottom: '1px solid #999' }}>
                        {h}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {testCase.steps.map((step) => (
                    <tr key={step.number}>
                      <td style={{ padding: '4px 8px' }}>{step.number}</td>
                      <td style={{ padding: '4px 8px' }}>{step.action}</td>
                      <td style={{ padding: '4px 8px' }}>{step.testData ?? '—'}</td>
                      <td style={{ padding: '4px 8px' }}>{step.expectedResult}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ))}
        </>
      )}
    </div>
  )
}
