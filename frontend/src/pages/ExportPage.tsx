import { useEffect, useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { FlowPicker } from '../components/FlowPicker'
import {
  getSavedFlow,
  listSavedFlows,
  type SavedFlowSummary,
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

  // No `?? SAMPLE_FLOW`. This page is reachable from the nav bar as well as from the Flows
  // page's "Export" link, so arriving without router state used to silently substitute the
  // built-in sample — and Preview → Download then produced a plausible document full of the
  // wrong steps. Null is now a real state the UI has to handle, which makes exporting the
  // wrong flow impossible rather than merely warned about.
  const [flow, setFlow] = useState<TestFlow | null>(() => flowFromLocationState(location.state))
  const isSample = flow === SAMPLE_FLOW

  const [useLlm, setUseLlm] = useState(true)
  const [state, setState] = useState<'idle' | 'running' | 'done' | 'error'>('idle')
  const [suite, setSuite] = useState<TestCaseSuite | null>(null)
  const [error, setError] = useState('')
  const [downloading, setDownloading] = useState<'xlsx' | 'xml' | null>(null)

  // Other saved flows, offered as extra test cases in the same document. The backend has
  // accepted edgeCaseFlows since P22 — TestCaseSuiteBuilder emits them as
  // TestCaseSource.EdgeCase at High priority — but nothing ever sent it, so the feature was
  // unreachable from the UI. Accepting an edge case on the Flows page now saves it as a flow,
  // which is what makes it selectable here.
  const [otherFlows, setOtherFlows] = useState<SavedFlowSummary[]>([])
  const [includedNames, setIncludedNames] = useState<string[]>([])

  useEffect(() => {
    listSavedFlows()
      .then(setOtherFlows)
      .catch(() => setOtherFlows([]))
  }, [])

  function toggleIncluded(name: string) {
    setIncludedNames((prev) => (prev.includes(name) ? prev.filter((n) => n !== name) : [...prev, name]))
    setSuite(null)
  }

  // Resolved at request time rather than held in state: the picker can change the main flow
  // underneath a stale selection, and a flow can be deleted between ticking and exporting.
  async function resolveEdgeCaseFlows(): Promise<TestFlow[]> {
    const names = includedNames.filter((n) => n !== flow?.name)
    return Promise.all(names.map(getSavedFlow))
  }

  async function handlePreview() {
    if (!flow) return
    setState('running')
    setError('')
    try {
      const result = await previewTestCases({ flow, useLlm, edgeCaseFlows: await resolveEdgeCaseFlows() })
      setSuite(result)
      setState('done')
    } catch (e) {
      setError(String(e))
      setState('error')
    }
  }

  async function handleDownload(format: 'xlsx' | 'xml') {
    if (!flow) return
    setDownloading(format)
    setError('')
    try {
      const request = { flow, useLlm, edgeCaseFlows: await resolveEdgeCaseFlows() }
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
        {flow ? (
          <>
            Showing <strong>{flow.name}</strong> ({flow.steps.length} step(s))
            {isSample && ' — the built-in sample'}.
          </>
        ) : (
          <>Pick a saved flow below, or record one on the <Link to="/inspect">Inspect</Link> page.</>
        )}
      </p>

      <FlowPicker
        selected={flow}
        onSelect={(f) => {
          setFlow(f)
          setSuite(null)
          setState('idle')
        }}
        onUseSample={() => {
          setFlow(SAMPLE_FLOW)
          setSuite(null)
          setState('idle')
        }}
        sampleName={SAMPLE_FLOW.name}
      />

      {flow && otherFlows.filter((f) => f.name !== flow.name).length > 0 && (
        <section style={{ margin: '1rem 0' }}>
          <strong>Also include as edge cases</strong>
          <p style={{ opacity: 0.7, fontSize: '0.9em', margin: '0.25rem 0' }}>
            Other saved flows to document alongside this one — exported as edge cases, above the
            recorded path in priority. Accepting an edge-case suggestion on the{' '}
            <Link to="/flows">Flows</Link> page saves it here.
          </p>
          {otherFlows
            .filter((f) => f.name !== flow.name)
            .map((f) => (
              <label key={f.name} style={{ display: 'block' }}>
                <input
                  type="checkbox"
                  checked={includedNames.includes(f.name)}
                  onChange={() => toggleIncluded(f.name)}
                />{' '}
                {f.name} <span style={{ opacity: 0.7 }}>({f.stepCount} steps)</span>
              </label>
            ))}
        </section>
      )}

      <div style={{ display: 'flex', gap: '1rem', alignItems: 'center', margin: '1rem 0', flexWrap: 'wrap' }}>
        <label>
          <input type="checkbox" checked={useLlm} onChange={(e) => setUseLlm(e.target.checked)} />{' '}
          Use AI wording (falls back to deterministic templates if unavailable)
        </label>
        <button onClick={handlePreview} disabled={state === 'running' || !flow}>
          {state === 'running' ? 'Working…' : 'Preview'}
        </button>
        <button onClick={() => handleDownload('xlsx')} disabled={downloading !== null || !flow}>
          {downloading === 'xlsx' ? 'Downloading…' : 'Download .xlsx'}
        </button>
        <button onClick={() => handleDownload('xml')} disabled={downloading !== null || !flow}>
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
