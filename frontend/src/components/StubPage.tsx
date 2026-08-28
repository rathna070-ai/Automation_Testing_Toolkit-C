interface StubPageProps {
  title: string
  phase: string
}

// Placeholder for a page whose real implementation lands in a later phase.
// See docs/ARCHITECTURE.md for the phase table.
export function StubPage({ title, phase }: StubPageProps) {
  return (
    <div>
      <h1>{title}</h1>
      <p>Not implemented yet — see {phase} in docs/ARCHITECTURE.md.</p>
    </div>
  )
}
