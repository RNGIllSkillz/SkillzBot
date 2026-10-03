import { useEffect, useState } from 'react'
import { fmtNum, fmtTime, get } from '../api'
import type { MessageRow, Paged, UserRow } from '../types'
import { Badge, Card, Pager } from '../components/ui'

const SORTS: [string, string][] = [['messageCon', 'сообщений'], ['Points', 'баллов'], ['QuizPoints', 'баллов викторины'], ['QuizTotal', 'викторина всего'], ['roulettCon', 'стрик рулетки'], ['banCount', 'банов'], ['UvalCon', 'увалов'], ['UpdatedAt', 'активность'], ['Name', 'имя']]

export default function Users() {
  const [q, setQ] = useState(''); const [sort, setSort] = useState('messageCon'); const [page, setPage] = useState(1)
  const [data, setData] = useState<Paged<UserRow> | null>(null)
  const [sel, setSel] = useState<{ user: UserRow; messages: Paged<MessageRow> } | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const load = (p = page, s = sort, query = q) => get<Paged<UserRow>>(`/api/users?q=${encodeURIComponent(query)}&sort=${s}&page=${p}&pageSize=50`).then(d => { setData(d); setErr(null) }).catch(e => setErr(e.message))
  useEffect(() => { load(1) }, [])
  const open = (login: string) => get<{ user: UserRow; messages: Paged<MessageRow> }>(`/api/users/${encodeURIComponent(login)}`).then(setSel).catch(e => setErr(e.message))
  return (
    <>
      <div className="page-title"><h1>Пользователи</h1></div>
      {err && <div className="banner bad">{err}</div>}
      <div className="grid two">
        <Card>
          <form className="toolbar" onSubmit={e => { e.preventDefault(); setPage(1); load(1) }}>
            <input placeholder="логин начинается с…" value={q} onChange={e => setQ(e.target.value)} />
            <select value={sort} onChange={e => { setSort(e.target.value); setPage(1); load(1, e.target.value) }}>{SORTS.map(([k, l]) => <option key={k} value={k}>по {l}</option>)}</select>
            <button className="primary" type="submit">Найти</button>
          </form>
          <table>
            <thead><tr><th>Логин</th><th>Роли</th><th className="num">Сообщ.</th><th className="num">Баллы</th><th className="num">Викторина</th><th className="num">Рулетка</th><th className="num">Баны</th></tr></thead>
            <tbody>{(data?.items ?? []).map(u => (
              <tr key={u.twitchID} style={{ cursor: 'pointer' }} onClick={() => open(u.name)}>
                <td>{u.name} {u.isOnline ? <span title="в чате" style={{ color: 'var(--good)' }}>●</span> : null}</td>
                <td>{u.isBroadcaster ? <Badge tone="accent">стример</Badge> : null} {u.isMod ? <Badge>мод</Badge> : null} {u.isVip ? <Badge>vip</Badge> : null} {u.isSub ? <Badge>саб</Badge> : null}</td>
                <td className="num">{fmtNum(u.messageCon)}</td><td className="num">{fmtNum(u.points)}</td><td className="num">{u.quizPoints} / {u.quizTotal}</td><td className="num">{u.roulettCon}</td><td className="num">{u.banCount}</td>
              </tr>))}</tbody>
          </table>
          {data && <Pager page={data.page} pageSize={data.pageSize} total={data.total} onPage={p => { setPage(p); load(p) }} />}
        </Card>
        <Card title={sel ? `@${sel.user.name}` : 'Пользователь'}>
          {!sel ? <p className="muted">Выбери пользователя в таблице.</p> : (
            <>
              <dl className="kv">
                <dt>Twitch ID</dt><dd className="mono">{sel.user.twitchID}</dd>
                <dt>ID в базе</dt><dd>{sel.user.dbID}</dd>
                <dt>Сообщений</dt><dd>{fmtNum(sel.user.messageCon)}</dd>
                <dt>Баллы</dt><dd>{fmtNum(sel.user.points)} · викторина {sel.user.quizPoints} (всего {sel.user.quizTotal})</dd>
                <dt>Рулетка</dt><dd>стрик {sel.user.roulettCon}</dd>
                <dt>Увалы / баны</dt><dd>{sel.user.uvalCon} / {sel.user.banCount}</dd>
              </dl>
              <h3 style={{ marginTop: 16 }}>Последние сообщения</h3>
              <table><tbody>{sel.messages.items.map(m => <tr key={m.id}><td className="mono" style={{ whiteSpace: 'nowrap' }}>{fmtTime(m.time)}</td><td>{m.message}</td></tr>)}</tbody></table>
            </>
          )}
        </Card>
      </div>
    </>
  )
}
