import { useEffect, useRef, useState } from 'react'
import { useLocation } from 'react-router-dom'
import { applyAutoHeal, listLocatorPages, startAutoHeal, type LocatorPage } from '../api/autoheal'
import { connectInspectFeed, stopInspect, type InspectorEvent, type InspectorSessionInfo } from '../api/inspect'

type Phase = 'picking' | 'starting' | 'active' | 'applying' | 'done' | 'error'

// The locator the run-triage panel on the Failures page suggested, if the user arrived from
// there. Narrowed rather than trusted: router state is untyped and can be stale after a
// back/forward navigation.
function suggestionFromLocationState(state: unknown): { page: string; key: string } | null {
  if (!state || typeof state !== 'object') return null
  const { page, key } = state as { page?: unknown; key?: unknown }
  return typeof page === 'string' && typeof key === 'string' ? { page, key } : null
}

export function AutoHealPage() {
  const location = useLocation()
  const suggested = suggestionFromLocationState(location.state)

  const [phase, setPhase] = useState<Phase>('picking')
  const [error, setError] = useState('')

  const [pages, setPages] = useState<LocatorPage[]>([])
  const [pagesError, setPagesError] = useState('')
  // Seeded from the failure analysis when the user came via "apply it in Auto-heal". This
  // only pre-selects the locator — starting the session and applying the fix are still
  // deliberate clicks, because a healed locator that nobody looked at is exactly the
  // "silently masks a real regression" failure mode self-healing is criticised for.
  const [selectedPage, setSelectedPage] = useState(suggested?.page ?? '')
  const [selectedKey, setSelectedKey] = useState(suggested?.key ?? '')

  const [session, setSession] = useState<InspectorSessionInfo | null>(null)
  const [steps, setSteps] = useState<InspectorEvent[]>([])
  const [strategy, setStrategy] = useState('')
  const [value, setValue] = useState('')
  const [healed, setHealed] = useState<{ page: string; key: string; strategy: string; value: string } | null>(null)

  const feedCleanup = useRef<(() => Promise<void>) | null>(null)

  useEffect(() => {
    listLocatorPages()
      .then(setPages)
      .catch((e) => setPagesError(String(e)))

    return () => {
      void feedCleanup.current?.()
    }
  }, [])

  const page = pages.find((p) => p.page === selectedPage)
  const current = page?.keys.find((k) => k.key === selectedKey)

  function upsertStep(event: InspectorEvent) {
    setSteps((prev) => {
      const index = prev.findIndex((s) => s.sequence === event.sequence)
      if (index === -1) return [...prev, event].sort((a, b) => a.sequence - b.sequence)
      const next = prev.slice()
      next[index] = event
      return next
    })
  }

  async function handleStart() {
    if (!selectedPage || !selectedKey) {
      setError('Pick a page and a locator key first.')
      return
    }

    setPhase('starting')
    setError('')

    try {
      const started = await startAutoHeal({ page: selectedPage, key: selectedKey })
      setSession(started.session)
      setSteps(started.steps)

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

  // The overlay always records an initial `navigate` step (P7's own doing) — that's the
  // page opening, not the element the user came here to re-locate.
  const candidates = steps.filter((s) => s.actionType !== 'navigate' && s.element?.bestLocator)

  function pickCandidate(step: InspectorEvent) {
    const best = step.element!.bestLocator!
    setStrategy(best.strategy)
    setValue(best.value)
  }

  async function handleStopAndApply() {
    if (!session || !strategy.trim() || !value.trim()) {
      setError('Pick a captured element, or enter a strategy and value by hand, before applying.')
      return
    }

    setPhase('applying')
    setError('')

    try {
      await stopInspect(session.id)
      await feedCleanup.current?.()
      feedCleanup.current = null

      const result = await applyAutoHeal({
        page: selectedPage,
        key: selectedKey,
        strategy: strategy.trim(),
        value: value.trim(),
      })

      setHealed({ page: selectedPage, key: selectedKey, strategy: result.strategy, value: result.value })
      setPhase('done')
    } catch (e) {
      setError(String(e))
      setPhase('active')
    }
  }

  function handleHealAnother() {
    setSession(null)
    setSteps([])
    setStrategy('')
    setValue('')
    setHealed(null)
    setSelectedKey('')
    setError('')
    setPhase('picking')

    listLocatorPages()
      .then(setPages)
      .catch((e) => setPagesError(String(e)))
  }

  return (
    <div>
      <h1>Auto-heal</h1>
      <p>
        Pick a locator that's started failing, click the element it should now point at in the
        browser window that opens, and this rewrites that one entry in its <code>.locators.json</code>{' '}
        file — never the generated <code>.cs</code> code.
      </p>
      <p style={{ opacity: 0.7, fontSize: '0.9em' }}>
        Auto-heal handles a locator that changed on the same element; a structural change (the
        element removed, the form redesigned) needs a fresh Inspect recording instead.
      </p>

      {phase === 'picking' && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem', maxWidth: 480, margin: '1rem 0' }}>
          {pagesError && <p style={{ color: '#cf222e' }}>{pagesError}</p>}
          {!pagesError && pages.length === 0 && (
            <p style={{ opacity: 0.7 }}>No locator files yet — generate a flow first.</p>
          )}

          <label>
            Page
            <select
              value={selectedPage}
              onChange={(e) => {
                setSelectedPage(e.target.value)
                setSelectedKey('')
              }}
              style={{ width: '100%' }}
            >
              <option value="">Select a page…</option>
              {pages.map((p) => (
                <option key={p.page} value={p.page}>
                  {p.page}
                </option>
              ))}
            </select>
          </label>

          {page && (
            <label>
              Locator key
              <select value={selectedKey} onChange={(e) => setSelectedKey(e.target.value)} style={{ width: '100%' }}>
                <option value="">Select a key…</option>
                {page.keys.map((k) => (
                  <option key={k.key} value={k.key}>
                    {k.key} ({k.strategy}={k.value})
                  </option>
                ))}
              </select>
            </label>
          )}

          {current && (
            <p style={{ fontSize: '0.9em', opacity: 0.75 }}>
              Currently: {current.strategy}={current.value} on {page?.url}
            </p>
          )}

          <button onClick={handleStart} disabled={!selectedPage || !selectedKey}>
            Start re-inspect
          </button>
          {error && <p style={{ color: '#cf222e' }}>{error}</p>}
        </div>
      )}

      {phase === 'starting' && <p>Opening Chrome…</p>}

      {phase === 'error' && (
        <>
          <p style={{ color: '#cf222e' }}>{error}</p>
          <button onClick={handleHealAnother}>Start over</button>
        </>
      )}

      {(phase === 'active' || phase === 'applying') && session && (
        <div>
          <div style={{ display: 'flex', gap: '1rem', alignItems: 'center', margin: '1rem 0', flexWrap: 'wrap' }}>
            <strong>
              Healing {selectedPage}.{selectedKey}
            </strong>
            <span>state: {session.state}</span>
            {session.currentUrl && <span style={{ opacity: 0.7 }}>{session.currentUrl}</span>}
          </div>

          <p>Click the element in the browser window that should now be {selectedKey}.</p>

          {candidates.length > 0 && (
            <table style={{ borderCollapse: 'collapse', width: '100%', marginTop: '0.5rem' }}>
              <thead>
                <tr>
                  {['#', 'Action', 'Element', 'Locator', ''].map((h) => (
                    <th key={h} style={{ textAlign: 'left', padding: '4px 8px', borderBottom: '1px solid #999' }}>
                      {h}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {candidates.map((step) => (
                  <tr key={step.sequence} style={{ verticalAlign: 'top' }}>
                    <td style={{ padding: '4px 8px' }}>{step.sequence}</td>
                    <td style={{ padding: '4px 8px' }}>{step.actionType}</td>
                    <td style={{ padding: '4px 8px' }}>{step.suggestedLabel}</td>
                    <td style={{ padding: '4px 8px', fontSize: '0.85em' }}>
                      {step.element!.bestLocator!.strategy}={step.element!.bestLocator!.value}
                    </td>
                    <td style={{ padding: '4px 8px' }}>
                      <button onClick={() => pickCandidate(step)}>Use this</button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          {candidates.length === 0 && <p style={{ opacity: 0.7 }}>No element captured yet.</p>}

          <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem', maxWidth: 480, marginTop: '1rem' }}>
            <label>
              Strategy
              <select value={strategy} onChange={(e) => setStrategy(e.target.value)} style={{ width: '100%' }}>
                <option value="">—</option>
                <option value="id">id</option>
                <option value="css">css</option>
                <option value="xpath">xpath</option>
                <option value="name">name</option>
              </select>
            </label>
            <label>
              Value
              <input type="text" value={value} onChange={(e) => setValue(e.target.value)} style={{ width: '100%' }} />
            </label>
            <button onClick={handleStopAndApply} disabled={phase === 'applying' || !strategy || !value.trim()}>
              {phase === 'applying' ? 'Applying…' : 'Stop & apply'}
            </button>
            {error && <p style={{ color: '#cf222e' }}>{error}</p>}
          </div>
        </div>
      )}

      {phase === 'done' && healed && (
        <div>
          <p style={{ color: '#1a7f37' }}>
            ✓ Healed {healed.page}.{healed.key} → {healed.strategy}={healed.value}
          </p>
          <button onClick={handleHealAnother}>Heal another</button>
        </div>
      )}
    </div>
  )
}
