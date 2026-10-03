import { useState } from 'react'
import { Bar, BarChart, CartesianGrid, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { fmtNum, fmtTime, get } from '../api'
import type { ActivityBucket, Engagement, PollRow, PredictionRow } from '../types'
import { Badge, Card, Stat, useAsync } from '../components/ui'

export default function Stats() {
  const [days, setDays] = useState(30)
  const eng = useAsync(() => get<Engagement>(`/api/stats/engagement?days=${days}`), [days])
  const preds = useAsync(() => get<PredictionRow[]>(`/api/stats/predictions?days=${days}&limit=200`), [days])
  const polls = useAsync(() => get<PollRow[]>(`/api/stats/polls?days=${days}&limit=100`), [days])
  const activity = useAsync(() => get<ActivityBucket[]>(`/api/stats/activity?days=${days}`), [days])
  const e = eng.data
  const tooltip = { contentStyle: { background: 'var(--panel)', border: '1px solid var(--border)' } }
  return (
    <>
      <div className="page-title"><h1>Статистика</h1>
        <select value={days} onChange={ev => setDays(Number(ev.target.value))}>{[7, 14, 30, 90, 365].map(d => <option key={d} value={d}>за {d} дней</option>)}</select>
      </div>
      {eng.error && <div className="banner bad">{eng.error}</div>}
      {e && (
        <div className="grid cards">
          <Stat title="Ставок" value={e.predictions} sub={`вин/луз ${e.winLose} · другие ${e.other} · вручную ${e.manualPredictions}`} />
          <Stat title="Участников вин/луз" value={fmtNum(e.winLoseAvgUsers)} sub={`в среднем · макс ${e.winLoseMaxUsers}`} />
          <Stat title="Участников, другие типы" value={fmtNum(e.otherAvgUsers)} sub="в среднем" />
          <Stat title="Баллов за ставку" value={fmtNum(e.winLoseAvgPoints)} sub={`всего ${fmtNum(e.totalPoints)}`} />
          <Stat title="Опросов" value={e.polls} sub={`голосов ср. ${fmtNum(e.pollAvgVotes)} · макс ${e.pollMaxVotes}`} />
          <Stat title="Игроков в топах" value={e.knownBettors} sub="уникальных в топ-10 исходов" />
        </div>
      )}
      <div className="grid two" style={{ marginTop: 12 }}>
        <Card title="Вин/луз: участники и баллы по исходу (среднее)">
          <div style={{ height: 240 }}>
            <ResponsiveContainer>
              <BarChart data={e?.winLoseOutcomes ?? []}>
                <CartesianGrid stroke="var(--border)" strokeDasharray="3 3" /><XAxis dataKey="title" stroke="var(--muted)" /><YAxis yAxisId="u" stroke="var(--muted)" width={36} /><YAxis yAxisId="p" orientation="right" stroke="var(--muted)" width={56} />
                <Tooltip {...tooltip} /><Legend />
                <Bar yAxisId="u" dataKey="avgUsers" name="участников" fill="var(--accent)" /><Bar yAxisId="p" dataKey="avgPoints" name="баллов" fill="var(--accent-2)" />
              </BarChart>
            </ResponsiveContainer>
          </div>
        </Card>
        <Card title="Другие типы ставок">
          <table><thead><tr><th>Тип</th><th className="num">Ставок</th><th className="num">Участников</th><th className="num">Баллов</th></tr></thead>
            <tbody>{(e?.byKind ?? []).filter(k => k.kind !== 'winlose').map(k => <tr key={k.kind}><td>{k.kind}</td><td className="num">{k.count}</td><td className="num">{fmtNum(k.avgUsers)}</td><td className="num">{fmtNum(k.avgPoints)}</td></tr>)}</tbody></table>
        </Card>
      </div>
      <Card title={`Активность чата за ${days} дней`} className="" >
        <div style={{ height: 220, marginTop: 12 }}>
          <ResponsiveContainer>
            <BarChart data={(activity.data ?? []).map(b => ({ ...b, t: days <= 2 ? new Date(b.time).toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' }) : new Date(b.time).toLocaleDateString('ru-RU', { day: '2-digit', month: '2-digit' }) }))}>
              <CartesianGrid stroke="var(--border)" strokeDasharray="3 3" /><XAxis dataKey="t" stroke="var(--muted)" fontSize={11} minTickGap={24} /><YAxis stroke="var(--muted)" fontSize={11} width={40} />
              <Tooltip {...tooltip} /><Legend />
              <Bar dataKey="messages" name="сообщений" fill="var(--accent)" /><Bar dataKey="users" name="чаттеров" fill="var(--accent-2)" />
            </BarChart>
          </ResponsiveContainer>
        </div>
      </Card>
      <Card title="Ставки" className="">
        <table><thead><tr><th>Завершена</th><th>Тип</th><th>Заголовок</th><th>Статус</th><th className="num">Участн.</th><th className="num">Баллов</th><th>Исходы</th></tr></thead>
          <tbody>{(preds.data ?? []).map(p => (
            <tr key={p.predictionId}><td className="mono">{fmtTime(p.endedAt ?? p.startedAt)}</td><td><Badge tone={p.kind === 'winlose' ? undefined : p.kind === 'manual' ? 'warn' : 'accent'}>{p.kind}</Badge></td><td>{p.title}</td><td>{p.status}</td><td className="num">{p.totalUsers}</td><td className="num">{fmtNum(p.totalPoints)}</td>
              <td>{p.outcomes.map(o => <span key={o.title} className={`badge ${o.isWinner ? 'good' : ''}`} style={{ marginRight: 4 }}>{o.title}: {o.users} / {fmtNum(o.channelPoints)}</span>)}</td></tr>))}</tbody></table>
      </Card>
      <Card title="Опросы" className="">
        <table><thead><tr><th>Завершен</th><th>Источник</th><th>Вопрос</th><th>Статус</th><th className="num">Голосов</th><th>Победитель</th><th>Варианты</th></tr></thead>
          <tbody>{(polls.data ?? []).map(p => (
            <tr key={p.pollId}><td className="mono">{fmtTime(p.endedAt ?? p.startedAt)}</td><td>{p.source}</td><td>{p.title}</td><td>{p.status}</td><td className="num">{p.totalVotes}</td><td>{p.winnerTitle}</td>
              <td>{p.choices.map(c => <span key={c.title} className="badge" style={{ marginRight: 4 }}>{c.title}: {c.votes}</span>)}</td></tr>))}</tbody></table>
      </Card>
    </>
  )
}
