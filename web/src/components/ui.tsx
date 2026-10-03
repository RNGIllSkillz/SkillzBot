import React, { useEffect, useState } from 'react'

export function Card({ title, children, className = '' }: { title?: string; children: React.ReactNode; className?: string }) {
  return <div className={`card ${className}`}>{title && <h3>{title}</h3>}{children}</div>
}

export function Stat({ title, value, sub, tone }: { title: string; value: React.ReactNode; sub?: React.ReactNode; tone?: 'good' | 'bad' | 'warn' }) {
  return (
    <Card title={title}>
      <div className="stat" style={tone ? { color: `var(--${tone})` } : undefined}>{value}{sub && <small>{sub}</small>}</div>
    </Card>
  )
}

export function Badge({ children, tone }: { children: React.ReactNode; tone?: 'good' | 'bad' | 'warn' | 'accent' }) {
  return <span className={`badge ${tone ?? ''}`}>{children}</span>
}

export function OnOff({ on, yes = 'ок', no = 'нет' }: { on: boolean; yes?: string; no?: string }) {
  return <Badge tone={on ? 'good' : 'bad'}>{on ? yes : no}</Badge>
}

export function Toggle({ label, hint, value, onChange, disabled }: { label: string; hint?: string; value: boolean; onChange: (v: boolean) => void; disabled?: boolean }) {
  return (
    <div className="toggle">
      <div className="lbl"><span>{label}</span>{hint && <small>{hint}</small>}</div>
      <button type="button" className={`switch ${value ? 'on' : ''}`} aria-pressed={value} aria-label={label} disabled={disabled} onClick={() => onChange(!value)} />
    </div>
  )
}

export function Pager({ page, pageSize, total, onPage }: { page: number; pageSize: number; total: number; onPage: (p: number) => void }) {
  const pages = Math.max(1, Math.ceil(total / pageSize))
  return (
    <div className="pager">
      <span className="muted">{total} записей, стр. {page} из {pages}</span>
      <button disabled={page <= 1} onClick={() => onPage(page - 1)}>←</button>
      <button disabled={page >= pages} onClick={() => onPage(page + 1)}>→</button>
    </div>
  )
}

export function useAsync<T>(fn: () => Promise<T>, deps: unknown[]) {
  const [data, setData] = useState<T | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [tick, setTick] = useState(0)
  useEffect(() => {
    let alive = true
    setLoading(true)
    fn().then(d => { if (alive) { setData(d); setError(null) } }).catch(e => { if (alive) setError(e.message) }).finally(() => { if (alive) setLoading(false) })
    return () => { alive = false }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, tick])
  return { data, error, loading, reload: () => setTick(t => t + 1), setData }
}

export function Confirm({ text, onYes, children, className }: { text: string; onYes: () => void; children: React.ReactNode; className?: string }) {
  return <button className={className} onClick={() => { if (window.confirm(text)) onYes() }}>{children}</button>
}

export function Notice({ kind, children }: { kind?: 'bad' | 'good'; children: React.ReactNode }) {
  return <div className={`banner ${kind ?? ''}`}>{children}</div>
}
