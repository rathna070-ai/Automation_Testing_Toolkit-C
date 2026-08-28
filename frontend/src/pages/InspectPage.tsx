import { StubPage } from '../components/StubPage'

// The backend (P7) is built and proven: /api/inspect/* plus the live /hubs/inspect feed,
// with a typed client in src/api/inspect.ts. What is missing is this page.
export function InspectPage() {
  return <StubPage title="Inspect" phase="P8 — Inspect UI (backend ready: src/api/inspect.ts)" />
}
