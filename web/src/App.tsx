import { useEffect, useState } from 'react'
import { NavLink, Navigate, Route, Routes, useLocation } from 'react-router-dom'
import { ApiError, get, post } from './api'
import type { Me, Status } from './types'
import Dashboard from './pages/Dashboard'
import Chat from './pages/Chat'
import Users from './pages/Users'
import Stats from './pages/Stats'
import Vips from './pages/Vips'
import Filters from './pages/Filters'
import Quiz from './pages/Quiz'
import Settings from './pages/Settings'
import System from './pages/System'

const AUTH_ERRORS: Record<string, string> = {
  forbidden: 'Этот Twitch-аккаунт не входит в список редакторов. Стример или root может добавить его командой !editor add <login>.',
  denied: 'Вход через Twitch отменен.',
  state: 'Сессия входа устарела, попробуй еще раз.',
  token: 'Twitch не выдал токен. Проверь TApiClientSecret и Redirect URL приложения.',
  validate: 'Не удалось проверить токен Twitch.',
  error: 'Ошибка входа, подробности в логе бота.',
}

export default function App() {
  const [me, setMe] = useState<Me | null | undefined>(undefined)
  const [loginConfigured, setLoginConfigured] = useState(true)
  const location = useLocation()

  useEffect(() => {
    get<Me>('/api/auth/me').then(setMe).catch((e: ApiError) => {
      setMe(null)
      // the 401 body says whether login is configured at all
      setLoginConfigured(e.body?.loginConfigured !== false)
    })
  }, [])

  if (me === undefined) return <div className="login"><div className="muted">Загрузка…</div></div>
  if (me === null) return <Login configured={loginConfigured} />

  return (
    <Routes>
      <Route element={<Layout me={me} />}>
        <Route path="/" element={<Dashboard me={me} />} />
        <Route path="/chat" element={<Chat />} />
        <Route path="/users" element={<Users />} />
        <Route path="/stats" element={<Stats />} />
        <Route path="/vips" element={<Vips />} />
        <Route path="/filters" element={<Filters />} />
        <Route path="/quiz" element={<Quiz />} />
        <Route path="/settings" element={<Settings me={me} />} />
        <Route path="/system" element={<System me={me} />} />
        <Route path="*" element={<Navigate to="/" replace state={{ from: location }} />} />
      </Route>
    </Routes>
  )
}

function Login({ configured }: { configured: boolean }) {
  const params = new URLSearchParams(window.location.search)
  const err = params.get('auth')
  const returnTo = window.location.pathname === '/' ? '/' : window.location.pathname
  return (
    <div className="login">
      <div className="card">
        <h1>SkillzBot</h1>
        <p className="muted">Панель управления ботом. Вход для стримера, root и редакторов.</p>
        {err && <div className="banner bad" style={{ textAlign: 'left' }}>{AUTH_ERRORS[err] ?? `Ошибка входа: ${err}`}</div>}
        {configured
          ? <a className="primary" href={`/api/auth/login?returnTo=${encodeURIComponent(returnTo)}`}><button className="primary">Войти через Twitch</button></a>
          : <div className="banner">Вход не настроен: в конфиге канала нужны ApiPublicUrl и TApiClientSecret.</div>}
      </div>
    </div>
  )
}

import { Outlet } from 'react-router-dom'
function Layout({ me }: { me: Me }) {
  const [open, setOpen] = useState(false)
  const [status, setStatus] = useState<Status | null>(null)
  const location = useLocation()
  useEffect(() => { setOpen(false) }, [location.pathname])
  useEffect(() => {
    // one shared SSE connection keeps the sidebar indicator live on every page
    const es = new EventSource('/api/chat/stream')
    es.addEventListener('health', (ev: MessageEvent) => { try { setStatus(JSON.parse(ev.data)) } catch { } })
    return () => es.close()
  }, [])
  const logout = () => post('/api/auth/logout').then(() => window.location.assign('/'))
  const items: [string, string][] = [['/', 'Дашборд'], ['/chat', 'Чат'], ['/users', 'Пользователи'], ['/stats', 'Статистика'], ['/vips', 'VIP'], ['/filters', 'Фильтры'], ['/quiz', 'Викторина'], ['/settings', 'Настройки'], ['/system', 'Система']]
  const healthy = !!status && status.ircConnected && status.eventSubConnected && status.dbOk
  return (
    <div className="layout">
      <aside className={`sidebar ${open ? 'open' : ''}`}>
        <div className="brand"><span className={`dot ${healthy ? 'on' : ''}`} title={healthy ? 'все подключения в норме' : 'есть проблемы'} />SkillzBot</div>
        <nav className="nav">{items.map(([to, label]) => <NavLink key={to} to={to} end={to === '/'}>{label}</NavLink>)}</nav>
        <div className="me">
          <span>{me.login} <span className="badge accent">{me.role}</span></span>
          {status && <span className="muted">{status.online ? 'стрим онлайн' : 'офлайн'}{status.inMatch ? ' · в матче' : ''}</span>}
          <button onClick={logout}>Выйти</button>
        </div>
      </aside>
      <main className="content">
        <button className="menu-btn" onClick={() => setOpen(o => !o)}>☰ Меню</button>
        <Outlet context={status} />
      </main>
    </div>
  )
}
