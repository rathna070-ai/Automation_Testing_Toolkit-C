import { useEffect, useState } from 'react'
import { getHealth } from '../api/client'
import { pingHub } from '../api/pingHub'

type CheckState = 'pending' | 'ok' | 'error'

export function HomePage() {
  const [healthState, setHealthState] = useState<CheckState>('pending')
  const [healthDetail, setHealthDetail] = useState('')
  const [hubState, setHubState] = useState<CheckState>('pending')
  const [hubDetail, setHubDetail] = useState('')

  useEffect(() => {
    getHealth()
      .then((r) => {
        setHealthState('ok')
        setHealthDetail(`${r.status} @ ${r.utc}`)
      })
      .catch((e) => {
        setHealthState('error')
        setHealthDetail(String(e))
      })

    pingHub()
      .then((reply) => {
        setHubState('ok')
        setHubDetail(reply)
      })
      .catch((e) => {
        setHubState('error')
        setHubDetail(String(e))
      })
  }, [])

  return (
    <div>
      <h1>Web Test Toolkit</h1>
      <p>Backend connectivity check — confirms the API and SignalR wiring before anything else is built.</p>
      <ul>
        <li>
          <strong>GET /api/health:</strong> {healthState} — {healthDetail || '…'}
        </li>
        <li>
          <strong>SignalR /hubs/ping:</strong> {hubState} — {hubDetail || '…'}
        </li>
      </ul>
    </div>
  )
}
