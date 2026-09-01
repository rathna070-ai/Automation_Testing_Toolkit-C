import { useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { FlowPicker } from '../components/FlowPicker'
import {
  downloadGeneratedFilesZip,
  generateFlow,
  previewFlow,
  saveFlow,
  suggestEdgeCases,
  type EdgeCaseOption,
  type GenerateFlowResponse,
  type GenerationSource,
  type TestFlow,
} from '../api/client'
import { SAMPLE_FLOW } from './sampleFlow'

// Per-edge-case generation state, keyed by nameSuffix — independent of the main flow's
// `result`, since accepting one edge case must not clobber (or be clobbered by) another.
interface EdgeCaseRunState {
  status: 'idle' | 'running' | 'done' | 'error'
  response?: GenerateFlowResponse
  error?: string
}

// The provenance badge. Two outcomes now that generation is deterministic: it compiled, or
// it did not. The three LLM sources went with the codegen path.
const SOURCE_LABELS: Record<GenerationSource, { text: string; tone: string }> = {
  Deterministic: { text: '⚙ Generated · compiled ✓', tone: '#1a7f37' },
  Failed: { text: '✗ Generation failed', tone: '#cf222e' },
}

// react-router-dom types location.state as `unknown` — narrow it defensively rather than
// trusting it, since it could be stale (browser back/forward) or absent (direct navigation).
function flowFromLocationState(state: unknown): TestFlow | null {
  if (!state || typeof state !== 'object' || !('flow' in state)) return null
  const flow = (state as { flow: unknown }).flow
  return flow && typeof flow === 'object' && 'steps' in flow ? (flow as TestFlow) : null
}

export function FlowsPage() {
  const location = useLocation()
  const handedOffFlow = flowFromLocationState(location.state)

  // No `?? SAMPLE_FLOW`: arriving here from the nav bar used to silently load the built-in
  // sample, which is how a sample suite got generated and committed by accident. Choosing the
  // sample is now a deliberate button in FlowPicker.
  const [flow, setFlow] = useState<TestFlow | null>(handedOffFlow)
  const [state, setState] = useState<'idle' | 'running' | 'done' | 'error'>('idle')
  const [result, setResult] = useState<GenerateFlowResponse | null>(null)
  const [error, setError] = useState('')
  const [showAttempts, setShowAttempts] = useState(false)
  const [compareDeterministic, setCompareDeterministic] = useState(false)
  const [downloadError, setDownloadError] = useState('')

  const [selectedFile, setSelectedFile] = useState<string | null>(null)

  const [edgeCaseState, setEdgeCaseState] = useState<'idle' | 'loading' | 'done' | 'error'>('idle')
  const [edgeCaseOptions, setEdgeCaseOptions] = useState<EdgeCaseOption[]>([])
  const [edgeCaseNote, setEdgeCaseNote] = useState('')
  const [edgeCaseRuns, setEdgeCaseRuns] = useState<Record<string, EdgeCaseRunState>>({})

  const isSample = flow === SAMPLE_FLOW

  function selectFlow(next: TestFlow) {
    setFlow(next)
    setResult(null)
    setState('idle')
  }

  async function loadEdgeCases() {
    if (!flow) return
    setEdgeCaseState('loading')
    setEdgeCaseNote('')
    setEdgeCaseOptions([])
    setEdgeCaseRuns({})
    try {
      const response = await suggestEdgeCases(flow)
      if (response.available) {
        setEdgeCaseOptions(response.edgeCases)
        if (response.edgeCases.length === 0) setEdgeCaseNote('No useful edge cases found for this flow.')
      } else {
        setEdgeCaseNote(response.unavailableReason ?? 'Edge-case suggestions are unavailable.')
      }
      setEdgeCaseState('done')
    } catch (e) {
      setEdgeCaseNote(String(e))
      setEdgeCaseState('error')
    }
  }

  // Accept = generate this one edge case exactly like the main flow, just scoped to its own
  // suffix so its result never overwrites another edge case's or the main flow's.
  //
  // Accepting also *saves* the edge case as a real flow. That is what makes it exportable:
  // the backend has taken edgeCaseFlows since P22, but accepted edge cases only ever lived in
  // this page's state, so they could not survive the trip to the Export page and the feature
  // was unreachable. Persisting them means Export can offer them like any other saved flow.
  async function runEdgeCase(option: EdgeCaseOption, write: boolean) {
    setEdgeCaseRuns((prev) => ({ ...prev, [option.nameSuffix]: { status: 'running' } }))
    try {
      const call = write ? generateFlow : previewFlow
      const response = await call({ flow: option.flow })

      if (write) {
        // Only on Accept & generate, not on Preview — previewing a suggestion should not
        // leave a saved flow behind for something the user may still reject.
        await saveFlow(option.flow)
      }

      setEdgeCaseRuns((prev) => ({ ...prev, [option.nameSuffix]: { status: 'done', response } }))
    } catch (e) {
      setEdgeCaseRuns((prev) => ({ ...prev, [option.nameSuffix]: { status: 'error', error: String(e) } }))
    }
  }

  function rejectEdgeCase(nameSuffix: string) {
    setEdgeCaseOptions((prev) => prev.filter((o) => o.nameSuffix !== nameSuffix))
    setEdgeCaseRuns((prev) => {
      const next = { ...prev }
      delete next[nameSuffix]
      return next
    })
  }

  async function run(write: boolean) {
    if (!flow) return
    setState('running')
    setResult(null)
    setError('')
    try {
      const call = write ? generateFlow : previewFlow
      const response = await call({ flow })
      setResult(response)
      setSelectedFile(response.files[0]?.relativePath ?? null)
      setState('done')
    } catch (e) {
      setError(String(e))
      setState('error')
    }
  }

  const shownFiles = result
    ? compareDeterministic
      ? result.deterministicFiles
      : result.files
    : []
  const activeFile = shownFiles.find((f) => f.relativePath === selectedFile) ?? shownFiles[0]

  return (
    <div>
      <h1>Flows</h1>
      <p>
        Generates a Selenium + Reqnroll BDD suite from a recorded flow.{' '}
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
        onSelect={selectFlow}
        onUseSample={() => selectFlow(SAMPLE_FLOW)}
        sampleName={SAMPLE_FLOW.name}
      />

      <div style={{ display: 'flex', gap: '1rem', alignItems: 'center', margin: '1rem 0', flexWrap: 'wrap' }}>
        <button onClick={() => run(false)} disabled={state === 'running' || !flow}>
          {state === 'running' ? 'Working…' : 'Preview (writes nothing)'}
        </button>
        <button onClick={() => run(true)} disabled={state === 'running' || !flow}>
          Generate &amp; write
        </button>
        {flow && (
          <Link to="/export" state={{ flow }}>
            Export as test case docs →
          </Link>
        )}
        <button onClick={loadEdgeCases} disabled={edgeCaseState === 'loading' || !flow}>
          {edgeCaseState === 'loading' ? 'Thinking…' : '✨ Suggest edge cases'}
        </button>
      </div>

      {edgeCaseNote && <p style={{ opacity: 0.7 }}>{edgeCaseNote}</p>}

      {edgeCaseOptions.length > 0 && (
        <div style={{ margin: '1rem 0', display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
          <strong>Suggested edge cases — review before accepting, nothing is written yet</strong>
          {edgeCaseOptions.map((option) => {
            const run = edgeCaseRuns[option.nameSuffix]
            return (
              <div
                key={option.nameSuffix}
                style={{ border: '1px solid #444', borderRadius: 4, padding: '0.75rem' }}
              >
                <div style={{ fontWeight: 600 }}>{option.title}</div>
                <div style={{ opacity: 0.8, margin: '0.25rem 0' }}>{option.rationale}</div>
                <div style={{ display: 'flex', gap: '0.5rem', alignItems: 'center', flexWrap: 'wrap' }}>
                  <button onClick={() => runEdgeCase(option, false)} disabled={run?.status === 'running'}>
                    Preview
                  </button>
                  <button onClick={() => runEdgeCase(option, true)} disabled={run?.status === 'running'}>
                    Accept &amp; generate
                  </button>
                  <button onClick={() => rejectEdgeCase(option.nameSuffix)}>Reject</button>
                  {run?.status === 'running' && <span>working…</span>}
                  {run?.status === 'error' && <span style={{ color: '#cf222e' }}>{run.error}</span>}
                  {run?.status === 'done' && run.response && (
                    <span style={{ color: SOURCE_LABELS[run.response.source]?.tone }}>
                      {SOURCE_LABELS[run.response.source]?.text ?? run.response.source}
                      {run.response.writtenPaths.length > 0
                        ? ` · ${run.response.writtenPaths.length} files written`
                        : ' · preview only'}
                    </span>
                  )}
                </div>
              </div>
            )
          })}
        </div>
      )}

      {state === 'running' && (
        <p>
          Generating… the deterministic baseline is instant; an AI attempt plus a sandbox
          compile usually takes 20–40 seconds.
        </p>
      )}

      {state === 'error' && <p style={{ color: '#cf222e' }}>Request failed: {error}</p>}

      {result && (
        <>
          <p style={{ color: SOURCE_LABELS[result.source]?.tone, fontWeight: 600 }}>
            {SOURCE_LABELS[result.source]?.text ?? result.source}
          </p>

          {result.fallbackReason && (
            <p style={{ color: '#bc4c00' }}>
              <strong>Why:</strong> {result.fallbackReason}
            </p>
          )}

          <p>
            {result.totalDurationMs} ms
            {result.writtenPaths.length > 0
              ? ` · ${result.writtenPaths.length} files written`
              : ' · nothing written (preview)'}
            {'  '}
            <button onClick={() => setShowAttempts((v) => !v)}>
              {showAttempts ? 'hide details' : 'see why'}
            </button>
          </p>

          {showAttempts && (
            <table style={{ borderCollapse: 'collapse', margin: '1rem 0', fontSize: '0.9em' }}>
              <thead>
                <tr>
                  {['#', 'Kind', 'Model', 'OK', 'ms', 'Tokens', 'Issues'].map((h) => (
                    <th key={h} style={{ textAlign: 'left', padding: '4px 10px', borderBottom: '1px solid #999' }}>
                      {h}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {result.attempts.map((a) => (
                  <tr key={a.number} style={{ verticalAlign: 'top' }}>
                    <td style={{ padding: '4px 10px' }}>{a.number}</td>
                    <td style={{ padding: '4px 10px' }}>{a.kind}</td>
                    <td style={{ padding: '4px 10px' }}>{a.model ?? '—'}</td>
                    <td style={{ padding: '4px 10px' }}>{a.succeeded ? '✓' : '✗'}</td>
                    <td style={{ padding: '4px 10px' }}>{a.durationMs}</td>
                    <td style={{ padding: '4px 10px' }}>{a.promptTokens + a.completionTokens}</td>
                    <td style={{ padding: '4px 10px' }}>
                      {a.issues.length === 0 ? (
                        '—'
                      ) : (
                        <>
                          {a.issues
                            .filter((i) => i.severity !== 'advisory')
                            .slice(0, 5)
                            .map((i, n) => (
                              <div key={n}>
                                <code>{i.code}</code> {i.file ? `${i.file}:${i.line ?? '?'} ` : ''}
                                {i.message.slice(0, 160)}
                              </div>
                            ))}
                          {/* Advisory issues never block the build — shown separately, muted,
                              so they read as a suggestion rather than a build error. */}
                          {a.issues
                            .filter((i) => i.severity === 'advisory')
                            .map((i, n) => (
                              <div key={`advisory-${n}`} style={{ opacity: 0.7, marginTop: n === 0 ? '4px' : 0 }}>
                                💡 <code>{i.code}</code> {i.message.slice(0, 160)}
                              </div>
                            ))}
                        </>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          {result.files.length > 0 && (
            <>
              <div style={{ display: 'flex', gap: '1rem', alignItems: 'center', margin: '1rem 0' }}>
                <strong>Generated files</strong>
                <label>
                  <input
                    type="checkbox"
                    checked={compareDeterministic}
                    onChange={(e) => setCompareDeterministic(e.target.checked)}
                  />{' '}
                  Show the deterministic version instead
                </label>
                <button
                  onClick={() => {
                    setDownloadError('')
                    downloadGeneratedFilesZip(shownFiles, flow?.name ?? 'generated').catch((e) =>
                      setDownloadError(String(e)),
                    )
                  }}
                >
                  Download as .zip
                </button>
              </div>
              {downloadError && <p style={{ color: '#cf222e' }}>{downloadError}</p>}

              <div style={{ display: 'flex', gap: '1rem', alignItems: 'flex-start' }}>
                <ul style={{ listStyle: 'none', padding: 0, margin: 0, minWidth: 260 }}>
                  {shownFiles.map((f) => (
                    <li key={f.relativePath}>
                      <button
                        onClick={() => setSelectedFile(f.relativePath)}
                        style={{
                          background: 'none',
                          border: 'none',
                          padding: '2px 0',
                          cursor: 'pointer',
                          fontWeight: activeFile?.relativePath === f.relativePath ? 700 : 400,
                          color: 'inherit',
                          textAlign: 'left',
                        }}
                      >
                        {f.relativePath}
                      </button>
                    </li>
                  ))}
                </ul>

                {activeFile && (
                  <pre
                    style={{
                      flex: 1,
                      overflow: 'auto',
                      maxHeight: '28rem',
                      padding: '1rem',
                      border: '1px solid #444',
                      borderRadius: 4,
                      fontSize: '0.8em',
                    }}
                  >
                    {activeFile.content}
                  </pre>
                )}
              </div>
            </>
          )}
        </>
      )}
    </div>
  )
}
