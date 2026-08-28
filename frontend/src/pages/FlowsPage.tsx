import { useState } from 'react'
import {
  generateFlow,
  previewFlow,
  type GenerateFlowResponse,
  type GenerationSource,
} from '../api/client'
import { SAMPLE_FLOW } from './sampleFlow'

// The provenance badge. Which path produced the code is a real quality signal about the
// prompt — shipping a fallback silently would hide it.
const SOURCE_LABELS: Record<GenerationSource, { text: string; tone: string }> = {
  LlmVerified: { text: '✨ AI-generated · compiled ✓', tone: '#1a7f37' },
  LlmRepaired: { text: '✨ AI-generated · repaired · compiled ✓', tone: '#9a6700' },
  Deterministic: { text: '⚙ Deterministic (AI disabled)', tone: '#57606a' },
  DeterministicFallback: { text: '⚙ Deterministic fallback — the AI version did not pass', tone: '#bc4c00' },
  Failed: { text: '✗ Generation failed', tone: '#cf222e' },
}

export function FlowsPage() {
  const [useLlm, setUseLlm] = useState(true)
  const [state, setState] = useState<'idle' | 'running' | 'done' | 'error'>('idle')
  const [result, setResult] = useState<GenerateFlowResponse | null>(null)
  const [error, setError] = useState('')
  const [showAttempts, setShowAttempts] = useState(false)
  const [compareDeterministic, setCompareDeterministic] = useState(false)
  const [selectedFile, setSelectedFile] = useState<string | null>(null)

  async function run(write: boolean) {
    setState('running')
    setResult(null)
    setError('')
    try {
      const call = write ? generateFlow : previewFlow
      const response = await call({ flow: SAMPLE_FLOW, useLlm, maxRepairAttempts: 2 })
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
        Generates a Selenium + Reqnroll BDD suite from a recorded flow. The Inspector backend
        (P7) is live, but this page isn't wired to it yet (P8/P9) — it still runs a built-in
        sample flow against the practice login site.
      </p>

      <div style={{ display: 'flex', gap: '1rem', alignItems: 'center', margin: '1rem 0', flexWrap: 'wrap' }}>
        <label>
          <input type="checkbox" checked={useLlm} onChange={(e) => setUseLlm(e.target.checked)} />{' '}
          Use AI generation (falls back to the deterministic generator if it fails)
        </label>
        <button onClick={() => run(false)} disabled={state === 'running'}>
          {state === 'running' ? 'Working…' : 'Preview (writes nothing)'}
        </button>
        <button onClick={() => run(true)} disabled={state === 'running'}>
          Generate &amp; write
        </button>
      </div>

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
            {result.attempts.length} attempt(s) · {result.totalDurationMs} ms ·{' '}
            {result.totalPromptTokens + result.totalCompletionTokens} tokens
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
                      {a.issues.length === 0
                        ? '—'
                        : a.issues.slice(0, 5).map((i, n) => (
                            <div key={n}>
                              <code>{i.code}</code> {i.file ? `${i.file}:${i.line ?? '?'} ` : ''}
                              {i.message.slice(0, 160)}
                            </div>
                          ))}
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
              </div>

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
