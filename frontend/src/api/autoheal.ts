import type { InspectSessionResponse } from './inspect'

// Auto-heal's own two calls. Starting a session and watching it live reuse Inspect's
// existing types/feed wholesale (see AutoHealPage.tsx) — this file only adds what's
// genuinely new: listing locator files, and patching one entry.

export interface LocatorKey {
  key: string
  strategy: string
  value: string
}

export interface LocatorPage {
  page: string
  url: string
  keys: LocatorKey[]
}

export interface AutoHealStartRequest {
  page: string
  key: string
}

export interface AutoHealApplyRequest {
  page: string
  key: string
  strategy: string
  value: string
}

async function send<T>(path: string, method: string, body?: unknown): Promise<T> {
  const response = await fetch(path, {
    method,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })

  if (!response.ok) {
    const detail = await response.json().catch(() => null)
    throw new Error(detail?.error ?? `${method} ${path} returned ${response.status}`)
  }

  return (await response.json()) as T
}

export function listLocatorPages(): Promise<LocatorPage[]> {
  return send<LocatorPage[]>('/api/locators', 'GET')
}

export function startAutoHeal(request: AutoHealStartRequest): Promise<InspectSessionResponse> {
  return send<InspectSessionResponse>('/api/autoheal/start', 'POST', request)
}

export function applyAutoHeal(request: AutoHealApplyRequest): Promise<LocatorKey> {
  return send<LocatorKey>('/api/autoheal/apply', 'POST', request)
}
