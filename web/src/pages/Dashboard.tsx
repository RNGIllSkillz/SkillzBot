import { useEffect, useState } from 'react'
import { useOutletContext } from 'react-router-dom'
import { Area, AreaChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { fmtAge, fmtNum, get, post } from '../api'
import type { ActivityBucket, GameState, Me, PredictionsStatus, Status } from '../types'
import { Badge, Card, Confirm, Notice, OnOff, Stat, useAsync } from '../components/ui'
import SubscriptionCard from '../components/SubscriptionCard'

export default function Dashboard({ me }: { me: Me }) {
  const live = useOutletContext<Status | null>()
  const first = useAsync(() => get<Status>('/api/status'), [])
  const status = live ?? first.data
  const game = useAsync(() => get<GameState>('/api/gamestate'), [])
  const pred = useAsync(() => get<PredictionsStatus>('/api/predictions/status'), [])
  const activity = useAsync(() => get<ActivityBucket[]>('/api/stats/activity?days=1&bucket=900'), [])
  const [msg, setMsg] = useState('')
  const [note, setNote] = useState<string | null>(null)
  useEffect(() => { const t = setInterval(() => pred.reload(), 30000); return () => clearInterval(t) }, [])

  const act = async (label: string, fn: () => Promise<unknown>) => {
    try { const r: any = await fn(); setNote(`${label}: ${typeof r === 'object' && r?.result ? r.result : 'ок'}`); pred.reload() }
    catch (e: any) { setNote(`${label}: ошибка — ${e.message}`) }
  }

  if (!status) return <p className="muted">{first.error ?? 'Загрузка…'}</p>
  return (
    <>
      <div className="page-title"><h1>Дашборд</h1><span className="muted">v{status.version} · аптайм {fmtAge(status.uptimeSeconds)} · {fmtNum(status.ramMb)} MB · {status.threads} потоков</span></div>
      {note && <Notice>{note}</Notice>}
      <div className="grid cards">
        <Stat title="Стрим" value={status.online ? 'онлайн' : 'офлайн'} sub={status.inMatch ? 'в матче' : undefined} tone={status.online ? 'good' : undefined} />
        <Stat title="IRC" value={status.ircConnected ? 'подключен' : 'НЕТ'} sub={`трафик ${fmtAge(status.ircLastTrafficSeconds)} назад`} tone={status.ircConnected ? 'good' : 'bad'} />
        <Stat title="EventSub" value={status.eventSubConnected ? 'подключен' : 'НЕТ'} sub={`событие ${fmtAge(status.eventSubLastEventSeconds)} назад · реконнектов ${status.eventSubReconnects}`} tone={status.eventSubConnected ? 'good' : 'bad'} />
        <Stat title="База данных" value={status.dbOk ? 'ок' : 'НЕДОСТУПНА'} sub={`сбоев ${status.dbFailures}`} tone={status.dbOk ? 'good' : 'bad'} />
        <Stat title="StreamElements" value={status.streamElementsFailures === 0 ? 'ок' : `сбоит x${status.streamElementsFailures}`} sub={`успех ${fmtAge(status.streamElementsLastOkSeconds)} назад`} tone={status.streamElementsFailures === 0 ? 'good' : 'warn'} />
        <Stat title="Очередь чата" value={status.chatPending} sub={`обработано ${fmtNum(status.chatProcessed)}${status.chatStalled ? ` · зависаний ${status.chatStalled}` : ''}`} tone={status.chatPending > 20 ? 'warn' : undefined} />
      </div>
      {(me.role === 'root' || me.role === 'admin') && <div style={{ marginTop: 12 }}><SubscriptionCard canEdit={me.role === 'root'} /></div>}
      <div className="grid two" style={{ marginTop: 12 }}>
        <Card title="Состояние">
          <div className="kv">
            <dt>Прокси</dt><dd className="mono">{status.proxy}</dd>
            <dt>Режим</dt><dd>{status.silent && <Badge tone="warn">silent</Badge>} <OnOff on={status.subActive} yes="бот активен" no="бот выключен (подписка)" /> <Badge>фильтр {status.filterLevel}</Badge> <OnOff on={status.autoPred} yes="автоставки" no="автоставки off" /></dd>
            {game.data && <><dt>Ранг</dt><dd>{game.data.summonerName} · {game.data.elo} {game.data.tier} · сегодня {game.data.numGames} игр ({game.data.numWins}/{game.data.numLosses}), LP {game.data.earnedLP >= 0 ? '+' : ''}{game.data.earnedLP}</dd></>}
            {pred.data && <><dt>Ставки</dt><dd>{pred.data.summary}</dd></>}
            {pred.data?.active && <><dt>Активная</dt><dd><Badge tone="accent">{pred.data.active.kindKey}</Badge> матч {pred.data.active.matchId}</dd></>}
          </div>
        </Card>
        <Card title="Быстрые действия">
          <div className="toolbar">
            <Confirm text="Запустить опрос о следующей ставке сейчас?" onYes={() => act('Опрос', () => post('/api/actions/poll'))}>Опрос сейчас</Confirm>
            <Confirm text="Запустить викторину?" onYes={() => act('Викторина', () => post('/api/actions/quiz'))}>Викторина</Confirm>
            <Confirm className="danger" text="Отменить текущую ставку на Twitch? Баллы вернутся зрителям." onYes={() => act('Отмена ставки', () => post('/api/actions/prediction/cancel'))}>Отменить ставку</Confirm>
          </div>
          <form onSubmit={e => { e.preventDefault(); if (msg.trim()) act('Сообщение', () => post('/api/chat/send', { text: msg })).then(() => setMsg('')) }} className="toolbar">
            <input style={{ flex: 1 }} placeholder="Сообщение в чат от бота" value={msg} onChange={e => setMsg(e.target.value)} maxLength={500} />
            <button className="primary" type="submit" disabled={!msg.trim()}>Отправить</button>
          </form>
        </Card>
      </div>
      <Card title="Сообщения за 24 часа (по 15 минут)" className="" >
        <div style={{ height: 220 }}>
          <ResponsiveContainer>
            <AreaChart data={(activity.data ?? []).map(b => ({ ...b, t: new Date(b.time).toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' }) }))}>
              <CartesianGrid stroke="var(--border)" strokeDasharray="3 3" />
              <XAxis dataKey="t" stroke="var(--muted)" fontSize={11} minTickGap={30} />
              <YAxis stroke="var(--muted)" fontSize={11} width={36} />
              <Tooltip contentStyle={{ background: 'var(--panel)', border: '1px solid var(--border)' }} />
              <Area type="monotone" dataKey="messages" name="сообщений" stroke="var(--accent)" fill="var(--accent)" fillOpacity={0.25} />
              <Area type="monotone" dataKey="users" name="чаттеров" stroke="var(--accent-2)" fill="var(--accent-2)" fillOpacity={0.15} />
            </AreaChart>
          </ResponsiveContainer>
        </div>
      </Card>
    </>
  )
}
