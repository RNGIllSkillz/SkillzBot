export class ApiError extends Error {
  constructor(public status: number, message: string, public body?: any) { super(message) }
}

const headers = { 'X-Requested-With': 'SkillzBot', 'Content-Type': 'application/json' }

// A channel panel served by the hub lives under /c/<login>/ and its API under /c/<login>/api/; a standalone bot uses '' (the root).
let apiBase = ''
export function setApiBase(base: string) { apiBase = base.replace(/\/+$/, '') }
export const getApiBase = () => apiBase
export const apiUrl = (path: string) => apiBase + path

export type ApiInit = RequestInit & { absolute?: boolean }

export async function api<T>(path: string, init?: ApiInit): Promise<T> {
  const { absolute, ...rest } = init ?? {}
  const res = await fetch(absolute ? path : apiUrl(path), { credentials: 'same-origin', ...rest, headers: { ...headers, ...(rest.headers as Record<string, string> | undefined) } })
  if (res.status === 204) return undefined as T
  let body: any = null
  const text = await res.text()
  if (text) { try { body = JSON.parse(text) } catch { body = { error: text } } }
  if (!res.ok) throw new ApiError(res.status, body?.error || res.statusText || `HTTP ${res.status}`, body)
  return body as T
}

export const get = <T,>(path: string) => api<T>(path)
export const post = <T,>(path: string, body?: unknown) => api<T>(path, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) })
export const patch = <T,>(path: string, body: unknown) => api<T>(path, { method: 'PATCH', body: JSON.stringify(body) })
export const put = <T,>(path: string, body: unknown) => api<T>(path, { method: 'PUT', body: JSON.stringify(body) })
export const del = <T,>(path: string) => api<T>(path, { method: 'DELETE' })
// The hub's own API (login, channels) is always at the site root, whatever panel is open.
export const hubGet = <T,>(path: string) => api<T>(path, { absolute: true })
export const hubPost = <T,>(path: string, body?: unknown) => api<T>(path, { method: 'POST', absolute: true, body: body === undefined ? undefined : JSON.stringify(body) })
export const hubDel = <T,>(path: string) => api<T>(path, { method: 'DELETE', absolute: true })

export function fmtAge(seconds?: number | null): string {
  if (seconds === null || seconds === undefined) return '—'
  const s = Math.max(0, Math.floor(seconds))
  if (s < 60) return `${s}с`
  if (s < 3600) return `${Math.floor(s / 60)}м ${s % 60}с`
  if (s < 86400) return `${Math.floor(s / 3600)}ч ${Math.floor((s % 3600) / 60)}м`
  return `${Math.floor(s / 86400)}д ${Math.floor((s % 86400) / 3600)}ч`
}

export function fmtTime(iso?: string | null): string {
  if (!iso) return '—'
  const d = new Date(iso)
  return isNaN(d.getTime()) ? '—' : d.toLocaleString('ru-RU', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit' })
}

export function fmtDate(iso?: string | null): string {
  if (!iso) return '—'
  const d = new Date(iso)
  return isNaN(d.getTime()) ? '—' : d.toLocaleDateString('ru-RU')
}

export const fmtNum = (n?: number | null) => (n === null || n === undefined ? '—' : new Intl.NumberFormat('ru-RU', { maximumFractionDigits: 0 }).format(n))
