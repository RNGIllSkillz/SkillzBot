import { useEffect, useRef, useState } from 'react'
import { apiUrl, fmtTime, get, post } from '../api'
import type { ChatMessage, MessageRow, Paged } from '../types'
import { Card, Pager } from '../components/ui'

export default function Chat() {
  const [tab, setTab] = useState<'live' | 'history'>('live')
  return (
    <>
      <div className="page-title"><h1>Чат</h1>
        <div className="toolbar" style={{ margin: 0 }}>
          <button className={tab === 'live' ? 'primary' : ''} onClick={() => setTab('live')}>Живой чат</button>
          <button className={tab === 'history' ? 'primary' : ''} onClick={() => setTab('history')}>История</button>
        </div>
      </div>
      {tab === 'live' ? <Live /> : <History />}
    </>
  )
}

function Live() {
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [text, setText] = useState('')
  const [stick, setStick] = useState(true)
  const box = useRef<HTMLDivElement>(null)
  useEffect(() => {
    get<ChatMessage[]>('/api/chat/recent?limit=300').then(setMessages).catch(() => {})
    const es = new EventSource(apiUrl('/api/chat/stream'))
    es.addEventListener('chat', (ev: MessageEvent) => {
      try { const m = JSON.parse(ev.data) as ChatMessage; setMessages(prev => [...prev.slice(-499), m]) } catch { }
    })
    return () => es.close()
  }, [])
  useEffect(() => { if (stick && box.current) box.current.scrollTop = box.current.scrollHeight }, [messages, stick])
  const onScroll = () => { const el = box.current; if (el) setStick(el.scrollHeight - el.scrollTop - el.clientHeight < 40) }
  const send = async (e: React.FormEvent) => { e.preventDefault(); if (!text.trim()) return; await post('/api/chat/send', { text }); setText('') }
  return (
    <>
      <div className="chat" ref={box} onScroll={onScroll}>
        {messages.map(m => (
          <div key={m.id} className={`msg ${m.fromBot ? 'bot' : ''}`}>
            <span className="t">{new Date(m.time).toLocaleTimeString('ru-RU')}</span>
            <span className="u" style={!m.fromBot && m.color ? { color: m.color } : undefined}>{m.displayName || m.login}</span>
            <span className="flags">{m.isBroadcaster && '📺'}{m.isMod && '🗡'}{m.isVip && '💎'}{m.isSub && '★'}</span>
            <span className="x">{m.text}</span>
          </div>
        ))}
      </div>
      <form className="toolbar" style={{ marginTop: 10 }} onSubmit={send}>
        {!stick && <button type="button" onClick={() => { setStick(true); if (box.current) box.current.scrollTop = box.current.scrollHeight }}>↓ к новым</button>}
        <input style={{ flex: 1 }} value={text} onChange={e => setText(e.target.value)} placeholder="Написать от имени бота…" maxLength={500} />
        <button className="primary" type="submit" disabled={!text.trim()}>Отправить</button>
      </form>
    </>
  )
}

function History() {
  const [user, setUser] = useState(''); const [q, setQ] = useState(''); const [from, setFrom] = useState(''); const [to, setTo] = useState('')
  const [page, setPage] = useState(1)
  const [data, setData] = useState<Paged<MessageRow> | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const load = (p = page) => {
    const qs = new URLSearchParams({ page: String(p), pageSize: '100' })
    if (user) qs.set('user', user); if (q) qs.set('q', q); if (from) qs.set('from', new Date(from).toISOString()); if (to) qs.set('to', new Date(to).toISOString())
    get<Paged<MessageRow>>(`/api/messages?${qs}`).then(d => { setData(d); setErr(null) }).catch(e => setErr(e.message))
  }
  useEffect(() => { load(1) }, [])
  return (
    <Card>
      <form className="toolbar" onSubmit={e => { e.preventDefault(); setPage(1); load(1) }}>
        <input placeholder="логин" value={user} onChange={e => setUser(e.target.value)} />
        <input placeholder="текст содержит" value={q} onChange={e => setQ(e.target.value)} />
        <input type="datetime-local" value={from} onChange={e => setFrom(e.target.value)} />
        <input type="datetime-local" value={to} onChange={e => setTo(e.target.value)} />
        <button className="primary" type="submit">Искать</button>
        <span className="muted">без логина и даты ищется за последние 7 дней</span>
      </form>
      {err && <div className="banner bad">{err}</div>}
      <table>
        <thead><tr><th>Время</th><th>Пользователь</th><th>Сообщение</th></tr></thead>
        <tbody>{(data?.items ?? []).map(m => <tr key={m.id}><td className="mono">{fmtTime(m.time)}</td><td>{m.name}</td><td>{m.message}</td></tr>)}</tbody>
      </table>
      {data && <Pager page={data.page} pageSize={data.pageSize} total={data.total} onPage={p => { setPage(p); load(p) }} />}
    </Card>
  )
}
