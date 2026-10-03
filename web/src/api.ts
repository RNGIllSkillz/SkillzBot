export class ApiError extends Error {
  constructor(public status: number, message: string, public body?: any) { super(message) }
}

const headers = { 'X-Requested-With': 'SkillzBot', 'Content-Type': 'application/json' }

export async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(path, { credentials: 'same-origin', ...init, headers: { ...headers, ...(init?.headers as Record<string, string> | undefined) } })
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
