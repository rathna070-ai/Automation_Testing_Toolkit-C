import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { getLlmStatus } from '../api/client'
import {
  connectInspectFeed,
  deleteInspectStep,
  getInspectFlow,
  setCapture,
  startInspect,
  stopInspect,
  suggestStepLabel,
  updateInspectStep,
  type ActionType,
  type InspectorEvent,
  type InspectorSessionInfo,
} from '../api/inspect'

type Phase = 'idle' | 'starting' | 'active' | 'error'

// Per-step editing state, separate from the InspectorEvent the live feed pushes. The feed
// can update a step at any moment (a retyped field corrects its InputValue), but that must
// never clobber text the user is mid-edit on — so a draft is seeded once from the first
// event at a given Sequence and is the user's from then on, until they explicitly discard it.
interface StepDraft {
  label: string
  locatorKey: string
  actionType: ActionType
}

const ACTION_TYPES: ActionType[] = ['navigate', 'click', 'type', 'assertText', 'assertVisible']

function draftFrom(event: InspectorEvent): StepDraft {
  return { label: event.suggestedLabel, locatorKey: event.locatorKey, actionType: event.actionType }
}

function isDirty(draft: StepDraft, event: InspectorEvent): boolean {
  return draft.label !== event.suggestedLabel || draft.locatorKey !== event.locatorKey || draft.actionType !== event.actionType
}

export function InspectPage() {
  const navigate = useNavigate()
  const [phase, setPhase] = useState<Phase>('idle')
  const [error, setError] = useState('')
  const [sendingToGenerate, setSendingToGenerate] = useState(false)

  const [name, setName] = useState('')
  const [startUrl, setStartUrl] = useState('')
  const [headless, setHeadless] = useState(false)

  const [session, setSession] = useState<InspectorSessionInfo | null>(null)
  const [steps, setSteps] = useState<InspectorEvent[]>([])
  const [drafts, setDrafts] = useState<Record<number, StepDraft>>({})
  const [savingSequence, setSavingSequence] = useState<number | null>(null)
  const [suggestingSequence, setSuggestingSequence] = useState<number | null>(null)
  const [suggestNotes, setSuggestNotes] = useState<Record<number, string>>({})

  const [llmAvailable, setLlmAvailable] = useState(false)
  const feedCleanup = useRef<(() => Promise<void>) | null>(null)

  useEffect(() => {
    getLlmStatus()
      .then((s) => setLlmAvailable(s.apiKeyConfigured))
      .catch(() => setLlmAvailable(false))

    // Only relevant if the component unmounts mid-session (navigating away without
    // stopping) — closes the SignalR connection, not the browser session itself.
    return () => {
      void feedCleanup.current?.()
    }
  }, [])

  function upsertStep(event: InspectorEvent) {
    setSteps((prev) => {
      const index = prev.findIndex((s) => s.sequence === event.sequence)
      if (index === -1) return [...prev, event].sort((a, b) => a.sequence - b.sequence)
      const next = prev.slice()
      next[index] = event
      return next
    })
    // Seed a draft the first time this Sequence is seen only — see StepDraft's doc comment.
    setDrafts((prev) => (prev[event.sequence] ? prev : { ...prev, [event.sequence]: draftFrom(event) }))
  }

  async function handleStart() {
    if (!name.trim() || !startUrl.trim()) {
      setError('A flow name and a start URL are both required.')
      return
    }

    setPhase('starting')
    setError('')

    try {
      const started = await startInspect({ name: name.trim(), startUrl: startUrl.trim(), headless })
      setSession(started.session)
      setSteps(started.steps)
      setDrafts(Object.fromEntries(started.steps.map((s) => [s.sequence, draftFrom(s)])))

      feedCleanup.current = await connectInspectFeed(started.session.id, {
        onStep: upsertStep,
        onState: setSession,
      })

      setPhase('active')
    } catch (e) {
      setError(String(e))
      setPhase('error')
    }
  }

  async function togglePause() {
    if (!session) return
    const updated = await setCapture(session.id, session.state !== 'running')
    setSession(updated)
  }

  async function handleStop() {
    if (!session) return
    const stopped = await stopInspect(session.id)
    setSession(stopped.session)
    setSteps(stopped.steps)
    await feedCleanup.current?.()
    feedCleanup.current = null
  }

  // The actual handoff. The backend's ToFlow() (not this page's local InspectorEvent[] state)
  // is the source of truth for the final TestFlow shape — it renumbers Order and resolves the
  // committed Label/LocatorKey, so this re-fetches rather than converting local state itself.
  async function sendToGenerate() {
    if (!session) return
    setSendingToGenerate(true)
    setError('')
    try {
      const flow = await getInspectFlow(session.id)
      navigate('/flows', { state: { flow } })
    } catch (e) {
      setError(String(e))
      setSendingToGenerate(false)
    }
  }

  function handleStartOver() {
    setSession(null)
    setSteps([])
    setDrafts({})
    setSuggestNotes({})
    setPhase('idle')
  }

  function updateDraft(sequence: number, patch: Partial<StepDraft>) {
    setDrafts((prev) => ({ ...prev, [sequence]: { ...prev[sequence], ...patch } }))
  }

  async function saveStep(sequence: number) {
    if (!session) return
    const draft = drafts[sequence]
    if (!draft) return

    setSavingSequence(sequence)
    try {
      const updated = await updateInspectStep(session.id, sequence, {
        label: draft.label,
        locatorKey: draft.locatorKey,
        actionType: draft.actionType,
      })
      setSteps(updated.steps)
      // The server is the source of truth for what actually got saved (e.g. AssertText
      // seeds ExpectedText from the element when it was empty) — resync the draft to match.
      const saved = updated.steps.find((s) => s.sequence === sequence)
      if (saved) setDrafts((prev) => ({ ...prev, [sequence]: draftFrom(saved) }))
    } catch (e) {
      setError(String(e))
    } finally {
      setSavingSequence(null)
    }
  }

  async function removeStep(sequence: number) {
    if (!session) return
    const updated = await deleteInspectStep(session.id, sequence)
    setSteps(updated.steps)
    setDrafts((prev) => {
      const next = { ...prev }
      delete next[sequence]
      return next
    })
  }

  async function requestSuggestion(sequence: number) {
    if (!session) return
    setSuggestingSequence(sequence)
    setSuggestNotes((prev) => ({ ...prev, [sequence]: '' }))
    try {
      const result = await suggestStepLabel(session.id, sequence)
      if (result.available && result.label) {
        updateDraft(sequence, { label: result.label })
      } else {
        setSuggestNotes((prev) => ({ ...prev, [sequence]: result.unavailableReason ?? 'Not available.' }))
      }
    } catch (e) {
      setSuggestNotes((prev) => ({ ...prev, [sequence]: String(e) }))
    } finally {
      setSuggestingSequence(null)
    }
  }

  const isLive = session?.state === 'running' || session?.state === 'paused'
  const isDone = session?.state === 'stopped' || session?.state === 'faulted'

  return (
    <div>
      <h1>Inspect</h1>
      <p>
        Point this at a page, then click through the flow you want tested in the browser
        window that opens. Every click and typed value is captured live, on the left below —
        edit labels, locator keys, or delete a step before you're done. Once stopped, send the
        captured flow straight to Generate.
      </p>

      {phase === 'idle' && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem', maxWidth: 480, margin: '1rem 0' }}>
          <label>
            Flow name
            <input
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="Login"
              style={{ width: '100%' }}
            />
          </label>
          <label>
            Start URL
            <input
              type="text"
              value={startUrl}
              onChange={(e) => setStartUrl(e.target.value)}
              placeholder="https://the-internet.herokuapp.com/login"
              style={{ width: '100%' }}
            />
          </label>
          <label>
            <input type="checkbox" checked={headless} onChange={(e) => setHeadless(e.target.checked)} /> Headless
            (no visible Chrome window)
          </label>
          <button onClick={handleStart}>Start Inspect</button>
          {error && <p style={{ color: '#cf222e' }}>{error}</p>}
        </div>
      )}

      {phase === 'starting' && <p>Opening Chrome…</p>}

      {phase === 'error' && (
        <>
          <p style={{ color: '#cf222e' }}>{error}</p>
          <button onClick={handleStartOver}>Try again</button>
        </>
      )}

      {phase === 'active' && session && (
        <div>
          <div style={{ display: 'flex', gap: '1rem', alignItems: 'center', margin: '1rem 0', flexWrap: 'wrap' }}>
            <strong>{session.name}</strong>
            <span>state: {session.state}</span>
            {session.currentUrl && <span style={{ opacity: 0.7 }}>{session.currentUrl}</span>}

            {isLive && (
              <>
                <button onClick={togglePause}>{session.state === 'running' ? 'Pause' : 'Resume'}</button>
                <button onClick={handleStop}>Stop Inspect</button>
              </>
            )}
            {isDone && <button onClick={handleStartOver}>Start a new session</button>}
          </div>

          {session.state === 'faulted' && (
            <p style={{ color: '#cf222e' }}>
              The browser session ended unexpectedly: {session.faultReason ?? 'unknown reason'}. Steps captured
              before that are still below.
            </p>
          )}

          {isDone && (
            <p>
              {steps.length} step(s) captured.{' '}
              <button onClick={sendToGenerate} disabled={sendingToGenerate || steps.length === 0}>
                {sendingToGenerate ? 'Sending…' : 'Send to Generate →'}
              </button>
            </p>
          )}

          {!llmAvailable && (
            <p style={{ opacity: 0.7, fontSize: '0.9em' }}>
              No Groq API key configured — label suggestions are unavailable, and the deterministic
              labels below are what generation will use as-is. Set a key on the Settings page to
              enable "✨ Suggest".
            </p>
          )}

          <table style={{ borderCollapse: 'collapse', width: '100%', marginTop: '1rem' }}>
            <thead>
              <tr>
                {['#', 'Action', 'Label', 'Page', 'Locator key', 'Locator', ''].map((h) => (
                  <th key={h} style={{ textAlign: 'left', padding: '4px 8px', borderBottom: '1px solid #999' }}>
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {steps.map((step) => {
                const draft = drafts[step.sequence] ?? draftFrom(step)
                const dirty = isDirty(draft, step)
                return (
                  <tr key={step.sequence} style={{ verticalAlign: 'top' }}>
                    <td style={{ padding: '4px 8px' }}>{step.sequence}</td>
                    <td style={{ padding: '4px 8px' }}>
                      <select
                        value={draft.actionType}
                        onChange={(e) => updateDraft(step.sequence, { actionType: e.target.value as ActionType })}
                      >
                        {ACTION_TYPES.map((t) => (
                          <option key={t} value={t}>
                            {t}
                          </option>
                        ))}
                      </select>
                    </td>
                    <td style={{ padding: '4px 8px', minWidth: 240 }}>
                      <input
                        type="text"
                        value={draft.label}
                        onChange={(e) => updateDraft(step.sequence, { label: e.target.value })}
                        style={{ width: '100%' }}
                      />
                      <div style={{ display: 'flex', gap: '0.5rem', marginTop: '2px', alignItems: 'center' }}>
                        <button
                          onClick={() => requestSuggestion(step.sequence)}
                          disabled={!llmAvailable || suggestingSequence === step.sequence}
                          style={{ fontSize: '0.85em' }}
                        >
                          {suggestingSequence === step.sequence ? '…' : '✨ Suggest'}
                        </button>
                        {suggestNotes[step.sequence] && (
                          <span style={{ fontSize: '0.8em', opacity: 0.7 }}>{suggestNotes[step.sequence]}</span>
                        )}
                      </div>
                    </td>
                    <td style={{ padding: '4px 8px' }}>{step.pageName}</td>
                    <td style={{ padding: '4px 8px' }}>
                      <input
                        type="text"
                        value={draft.locatorKey}
                        onChange={(e) => updateDraft(step.sequence, { locatorKey: e.target.value })}
                        style={{ width: '100%' }}
                      />
                    </td>
                    <td style={{ padding: '4px 8px', fontSize: '0.85em' }}>
                      {step.element?.bestLocator ? (
                        <span style={{ color: step.locatorScore < 60 ? '#bc4c00' : 'inherit' }}>
                          {step.element.bestLocator.strategy}={step.element.bestLocator.value} ({step.locatorScore})
                        </span>
                      ) : (
                        <span style={{ opacity: 0.6 }}>—</span>
                      )}
                    </td>
                    <td style={{ padding: '4px 8px' }}>
                      <button onClick={() => saveStep(step.sequence)} disabled={!dirty || savingSequence === step.sequence}>
                        {savingSequence === step.sequence ? 'Saving…' : 'Save'}
                      </button>{' '}
                      <button onClick={() => removeStep(step.sequence)}>✕</button>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>

          {steps.length === 0 && isLive && <p style={{ opacity: 0.7 }}>No steps captured yet — go click around the browser window.</p>}
        </div>
      )}
    </div>
  )
}
