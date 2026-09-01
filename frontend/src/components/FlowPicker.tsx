import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  deleteSavedFlow,
  getSavedFlow,
  listSavedFlows,
  type SavedFlowSummary,
  type TestFlow,
} from '../api/client'

interface FlowPickerProps {
  // The flow currently in use, or null when nothing has been chosen yet.
  selected: TestFlow | null
  onSelect: (flow: TestFlow) => void

  // Offered as a deliberate action rather than applied silently — see the note below.
  onUseSample: () => void
  sampleName: string
}

// One picker, used by both Flows and Export.
//
// It exists as a shared component rather than a copy on each page because the copy is exactly
// how this went wrong: P19 added flow persistence and wired the picker into FlowsPage only, so
// ExportPage kept falling back to the built-in sample and silently exported the wrong steps.
// A second copy would have been a second chance to miss one.
//
// The other half of that fix is what this component does *not* do: it never selects a flow on
// the caller's behalf. Both pages used to read `locationState ?? SAMPLE_FLOW`, so arriving via
// the nav bar (rather than the Flows → Export link) produced a plausible-looking document full
// of sample steps. Loading the sample is now a button someone has to press.
export function FlowPicker({ selected, onSelect, onUseSample, sampleName }: FlowPickerProps) {
  const [savedFlows, setSavedFlows] = useState<SavedFlowSummary[]>([])
  const [error, setError] = useState('')

  function refresh() {
    listSavedFlows()
      .then(setSavedFlows)
      .catch((e) => setError(String(e)))
  }

  useEffect(refresh, [])

  async function load(name: string) {
    setError('')
    try {
      onSelect(await getSavedFlow(name))
    } catch (e) {
      setError(String(e))
    }
  }

  async function remove(name: string) {
    setError('')
    try {
      await deleteSavedFlow(name)
      refresh()
    } catch (e) {
      setError(String(e))
    }
  }

  return (
    <section style={{ margin: '1rem 0' }}>
      <strong>Saved flows</strong>
      <p style={{ opacity: 0.7, fontSize: '0.9em', margin: '0.25rem 0' }}>
        Recordings are saved when an Inspect session stops, so they survive closing the tab and
        restarting the API. Pick one to work with it here.
      </p>

      {error && <p style={{ color: '#cf222e' }}>{error}</p>}

      {savedFlows.length === 0 ? (
        <p style={{ opacity: 0.7 }}>
          Nothing saved yet — capture a flow on the <Link to="/inspect">Inspect</Link> page.
        </p>
      ) : (
        <ul style={{ listStyle: 'none', padding: 0, margin: 0 }}>
          {savedFlows.map((f) => {
            const isCurrent = selected?.name === f.name
            return (
              <li
                key={f.name}
                style={{ display: 'flex', gap: '0.5rem', alignItems: 'center', padding: '2px 0' }}
              >
                <button onClick={() => load(f.name)} disabled={isCurrent}>
                  {isCurrent ? 'Loaded' : 'Load'}
                </button>
                <span>
                  <strong>{f.name}</strong> — {f.stepCount} step(s) ·{' '}
                  <span style={{ opacity: 0.7 }}>{f.startUrl}</span>{' '}
                  <span style={{ opacity: 0.55 }}>({new Date(f.savedUtc).toLocaleString()})</span>
                </span>
                <button onClick={() => remove(f.name)} style={{ fontSize: '0.85em' }}>
                  Delete
                </button>
              </li>
            )
          })}
        </ul>
      )}

      <p style={{ margin: '0.5rem 0 0' }}>
        <button onClick={onUseSample} style={{ fontSize: '0.85em' }}>
          Use the built-in sample flow ("{sampleName}")
        </button>
      </p>
    </section>
  )
}
