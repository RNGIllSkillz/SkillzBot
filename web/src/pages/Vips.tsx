import { useState } from 'react'
import { fmtDate, fmtTime, get, patch, post } from '../api'
import type { BotState, VipsResponse } from '../types'
import { Badge, Card, Notice, Toggle, useAsync } from '../components/ui'

export default function Vips() {
  const vips = useAsync(() => get<VipsResponse>('/api/vips'), [])
  const [note, setNote] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const run = async (fn: () => Promise<unknown>, ok?: string) => {
    setBusy(true)
    try { const r: any = await fn(); setNote(ok ?? (r?.results ? r.results.join(' | ') : `Синхронизировано: ${r.total}, новых ${r.added}, убрано ${r.removed}`)); vips.reload() }
    catch (e: any) { setNote(`Ошибка: ${e.message}`) } finally { setBusy(false) }
  }
  const d = vips.data
  return (
    <>
      <div className="page-title"><h1>VIP</h1>
        <div className="toolbar" style={{ margin: 0 }}>
          {d && <span className="muted">{d.vips.length} / {d.limit} · синк {fmtTime(d.lastSyncUtc)}</span>}
          <button disabled={busy} onClick={() => run(() => post('/api/vips/sync'))}>Сверить с Twitch</button>
        </div>
      </div>
      {note && <Notice>{note}</Notice>}
      {vips.error && <Notice kind="bad">{vips.error}</Notice>}
      {d && (
        <Card>
          <Toggle label="Авторотация" hint="при полном списке !addvip снимает самого давнего незакрепленного VIP" value={d.autoRotate}
            onChange={v => run(() => patch<{ state: BotState }>('/api/state', { vipAutoRotate: v }), `Авторотация ${v ? 'включена' : 'выключена'}`)} />
          <p className="muted">Порядок - кандидаты на снятие сверху: сначала VIP без даты (были до учета, по стажу в базе бота), затем по дате выдачи.</p>
          <table>
            <thead><tr><th>#</th><th>Логин</th><th>VIP с</th><th>Источник</th><th>Закреплен</th><th></th></tr></thead>
            <tbody>{d.vips.map((v, i) => (
              <tr key={v.twitchId}>
                <td className="muted">{i + 1}</td>
                <td>{v.displayName || v.login}</td>
                <td>{v.since ? fmtDate(v.since) : <Badge tone="warn">неизвестно</Badge>}</td>
                <td className="muted">{v.source}</td>
                <td>{v.pinned ? <Badge tone="good">да</Badge> : <span className="muted">нет</span>}</td>
                <td style={{ whiteSpace: 'nowrap' }}>
                  <button disabled={busy} onClick={() => run(() => patch(`/api/vips/${encodeURIComponent(v.login)}`, { pinned: !v.pinned }))}>{v.pinned ? 'Открепить' : 'Закрепить'}</button>{' '}
                  <button disabled={busy} onClick={() => { const s = window.prompt(`Дата выдачи VIP для ${v.login} (ГГГГ-ММ-ДД)`, v.since ? v.since.slice(0, 10) : ''); if (s) run(() => patch(`/api/vips/${encodeURIComponent(v.login)}`, { since: s })) }}>Дата…</button>
                </td>
              </tr>))}</tbody>
          </table>
        </Card>
      )}
    </>
  )
}
