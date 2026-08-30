import { useEffect, useState } from 'react'
import {
  analyzeFailure,
  getSettings,
  updateSettings,
  type AnalyzeFailureResponse,
  type SettingsResponse,
} from '../api/client'

const SAMPLE_FAILURE = {
  featureName: 'Login',
  scenarioName: 'Successful login with valid credentials',
  outcome: 'failed' as const,
  duration: '00:00:03',
  errorMessage:
    'NoSuchElementException: Unable to locate element: {"method":"id","selector":"username"}',
  stackTrace: 'at WebTestToolkit.GeneratedTests.PageObjects.LoginPage.FindVisible(String locatorKey)',
}

export function SettingsPage() {
  const [settings, setSettings] = useState<SettingsResponse | null>(null)
  const [model, setModel] = useState('')
  const [apiKeyInput, setApiKeyInput] = useState('')
  const [tokensPerMinute, setTokensPerMinute] = useState('')
  const [saveState, setSaveState] = useState<'idle' | 'saving' | 'saved' | 'error'>('idle')
  const [saveError, setSaveError] = useState('')

  const [analyzeState, setAnalyzeState] = useState<'idle' | 'running' | 'done' | 'error'>('idle')
  const [analyzeResult, setAnalyzeResult] = useState<AnalyzeFailureResponse | null>(null)
  const [analyzeError, setAnalyzeError] = useState('')

  useEffect(() => {
    getSettings().then((s) => {
      setSettings(s)
      setModel(s.groqModel)
      setTokensPerMinute(String(s.groqTokensPerMinute))
    })
  }, [])

  async function handleSave() {
    setSaveState('saving')
    try {
      const updated = await updateSettings({
        groqModel: model,
        // Empty input means "leave the stored key unchanged" — only send a value
        // when the user actually typed one, so re-saving the model doesn't wipe the key.
        groqApiKey: apiKeyInput.length > 0 ? apiKeyInput : undefined,
        // Non-numeric or empty means "leave unchanged"; the server also ignores <= 0.
        groqTokensPerMinute: Number(tokensPerMinute) > 0 ? Number(tokensPerMinute) : undefined,
      })
      setSettings(updated)
      setTokensPerMinute(String(updated.groqTokensPerMinute))
      setApiKeyInput('')
      setSaveState('saved')
    } catch (e) {
      setSaveError(String(e))
      setSaveState('error')
    }
  }

  async function handleTryIt() {
    setAnalyzeState('running')
    setAnalyzeResult(null)
    try {
      const result = await analyzeFailure(SAMPLE_FAILURE)
      setAnalyzeResult(result)
      setAnalyzeState('done')
    } catch (e) {
      setAnalyzeError(String(e))
      setAnalyzeState('error')
    }
  }

  return (
    <div>
      <h1>Settings</h1>

      <section style={{ marginBottom: '2rem' }}>
        <h2>Groq API key</h2>
        <p>
          Stored on this machine, encrypted with your Windows account (DPAPI) — not sent to the
          browser and not a substitute for a real secrets vault. Leave blank to keep the
          currently saved key.
        </p>
        <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem', maxWidth: 420 }}>
          <label>
            API key
            <input
              type="password"
              value={apiKeyInput}
              onChange={(e) => setApiKeyInput(e.target.value)}
              placeholder={settings?.apiKeyConfigured ? '•••••••• (already set)' : 'gsk_...'}
              style={{ width: '100%' }}
            />
          </label>
          <label>
            Model
            <input type="text" value={model} onChange={(e) => setModel(e.target.value)} style={{ width: '100%' }} />
          </label>
          <label>
            Tokens per minute (Groq plan allowance)
            <input
              type="number"
              min={1}
              value={tokensPerMinute}
              onChange={(e) => setTokensPerMinute(e.target.value)}
              style={{ width: '100%' }}
            />
          </label>
          <p style={{ opacity: 0.7, fontSize: '0.9em', margin: 0 }}>
            Groq counts a request's prompt plus its reserved response against this, so a whole
            request has to fit under it. The free tier allows 8,000 — less than one generation
            needs, which is why AI generation falls back to the deterministic generator. Groq's
            Developer tier is a free upgrade (card on file, pay-per-token) at roughly 250,000;
            set that here after upgrading and AI generation starts working.
          </p>
          <button onClick={handleSave} disabled={saveState === 'saving'}>
            {saveState === 'saving' ? 'Saving…' : 'Save'}
          </button>
          {saveState === 'saved' && (
            <p>
              Saved. Key configured: <strong>{settings?.apiKeyConfigured ? 'yes' : 'no'}</strong>
            </p>
          )}
          {saveState === 'error' && <p>Could not save: {saveError}</p>}
        </div>
      </section>

      <section>
        <h2>Try it — failure analysis</h2>
        <p>Sends a canned failure to Groq and shows the plain-English explanation. Proves the setup works end to end.</p>
        <button onClick={handleTryIt} disabled={analyzeState === 'running'}>
          {analyzeState === 'running' ? 'Analyzing…' : 'Analyze sample failure'}
        </button>

        {analyzeState === 'error' && <p>Request failed: {analyzeError}</p>}

        {analyzeState === 'done' && analyzeResult && !analyzeResult.available && (
          <p>Not available: {analyzeResult.unavailableReason}</p>
        )}

        {analyzeState === 'done' && analyzeResult?.available && analyzeResult.analysis && (
          <div style={{ marginTop: '1rem' }}>
            <p>
              <strong>Category:</strong> {analyzeResult.analysis.category}
              {' · '}
              <strong>Confidence:</strong> {(analyzeResult.analysis.confidence * 100).toFixed(0)}%
            </p>
            <p>
              <strong>Root cause:</strong> {analyzeResult.analysis.rootCause}
            </p>
            <p>
              <strong>Suggested fix:</strong> {analyzeResult.analysis.suggestedFix}
            </p>
            {analyzeResult.analysis.suggestedLocator && (
              <p>
                <strong>Suggested locator:</strong> {analyzeResult.analysis.suggestedLocator.strategy}=
                {analyzeResult.analysis.suggestedLocator.value} ({analyzeResult.analysis.suggestedLocator.why})
              </p>
            )}
          </div>
        )}
      </section>
    </div>
  )
}
