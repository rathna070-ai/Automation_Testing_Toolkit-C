// Thin fetch wrapper. Relative paths so the Vite dev-server proxy (see vite.config.ts)
// and a same-origin production deployment both work without changing this code.

export interface HealthResponse {
  status: string
  utc: string
}

async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(path)
  if (!response.ok) {
    throw new Error(`${path} returned ${response.status} ${response.statusText}`)
  }
  return (await response.json()) as T
}

export function getHealth(): Promise<HealthResponse> {
  return getJson<HealthResponse>('/api/health')
}
