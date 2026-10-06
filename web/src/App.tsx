import { useEffect, useState } from 'react'
import { NavLink, Navigate, Route, Routes, useLocation } from 'react-router-dom'
import { ApiError, apiUrl, get, hubPost, post } from './api'
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
import Twitch from './pages/Twitch'

const AUTH_ERRORS: Record<string, string> = {
  forbidden: 'Этот Twitch-аккаунт не входит в список редакторов. Стример или root может добавить его командой !editor add <login>.',
  denied: 'Вход через Twitch отменен.',
  state: 'Сессия входа устарела, попробуй еще раз.',
  token: 'Twitch не выдал токен. Проверь TApiClientSecret и Redirect URL приложения.',
  validate: 'Не удалось проверить токен Twitch.',
  error: 'Ошибка входа, подробности в логе бота.',
}

/** The channel panel. `base` is '' for a standalone bot and /c/<login> behind the hub; `initial` is a probe the root already made. */
export default function App({ base, initial }: { base: string; initial?: { me: Me | null; error: ApiError | null } }) {
  const [me, setMe] = useState<Me | null | undefined>(initial ? initial.me : undefined)
  const [loginConfigured, setLoginConfigured] = useState(initial ? initial.error?.body?.loginConfigured !== false : true)
  const [managed, setManaged] = useState(initial ? initial.error?.body?.managed === true : false)
  const location = useLocation()

  useEffect(() => {
    if (initial) return
    get<Me>('/api/auth/me').then(setMe).catch((e: ApiError) => {
      setMe(null)
      // the 401 body says whether login is configured at all, and whether the hub does the login
      setLoginConfigured(e.body?.loginConfigured !== false)
      setManaged(e.body?.managed === true)
    })
  }, [])

  if (me === undefined) return <div className="login"><div className="muted">Загрузка…</div></div>
  if (me === null) return <Login configured={loginConfigured} managed={managed} base={base} />

  return (
    <Routes>
      <Route element={<Layout me={me} base={base} />}>
        <Route path="/" element={<Dashboard me={me} />} />
        <Route path="/chat" element={<Chat />} />
        <Route path="/users" element={<Users />} />
        <Route path="/stats" element={<Stats />} />
        <Route path="/vips" element={<Vips />} />
        <Route path="/filters" element={<Filters />} />
        <Route path="/quiz" element={<Quiz />} />
        <Route path="/settings" element={<Settings me={me} />} />
        <Route path="/system" element={<System me={me} />} />
        <Route path="/twitch" element={<Twitch me={me} />} />
        <Route path="*" element={<Navigate to={base + '/'} replace state={{ from: location }} />} />
      </Route>
    </Routes>
  )
}

function Login({ configured, managed, base }: { configured: boolean; managed: boolean; base: string }) {
  const params = new URLSearchParams(window.location.search)
  const err = params.get('auth')
  const returnTo = window.location.pathname || base + '/'
  // Behind the hub the hub does the Twitch login (one cookie for every panel); standalone the bot does it itself.
  const loginHref = (managed ? '' : apiUrl('')) + `/api/auth/login?returnTo=${encodeURIComponent(returnTo)}`
  const channel = base.startsWith('/c/') ? base.slice(3) : null
  return (
    <div className="login">
      <div className="card">
        <h1>SkillzBot{channel ? <span className="muted"> · {channel}</span> : null}</h1>
        <p className="muted">Панель управления ботом. Вход для стримера, root и редакторов.</p>
        {err && <div className="banner bad" style={{ textAlign: 'left' }}>{AUTH_ERRORS[err] ?? `Ошибка входа: ${err}`}</div>}
        {configured || managed
          ? <a className="primary" href={loginHref}><button className="primary">Войти через Twitch</button></a>
          : <div className="banner">Вход не настроен: в конфиге канала нужны ApiPublicUrl и TApiClientSecret.</div>}
        {managed && <p className="muted" style={{ marginTop: 12 }}><a href="/">← к списку каналов</a></p>}
      </div>
    </div>
  )
}

import { Outlet } from 'react-router-dom'
function Layout({ me, base }: { me: Me; base: string }) {
  const [open, setOpen] = useState(false)
  const [status, setStatus] = useState<Status | null>(null)
  const location = useLocation()
  useEffect(() => { setOpen(false) }, [location.pathname])
  useEffect(() => {
    // one shared SSE connection keeps the sidebar indicator live on every page
    const es = new EventSource(apiUrl('/api/chat/stream'))
    es.addEventListener('health', (ev: MessageEvent) => { try { setStatus(JSON.parse(ev.data)) } catch { } })
    return () => es.close()
  }, [])
  // behind the hub the session cookie belongs to the hub, so the hub ends it
  const logout = () => (me.managed ? hubPost('/api/auth/logout') : post('/api/auth/logout')).then(() => window.location.assign(me.managed ? '/' : base + '/'))
  const items: [string, string][] = [['/', 'Дашборд'], ['/chat', 'Чат'], ['/users', 'Пользователи'], ['/stats', 'Статистика'], ['/vips', 'VIP'], ['/filters', 'Фильтры'], ['/quiz', 'Викторина'], ['/settings', 'Настройки'], ['/system', 'Система']]
  if (me.role === 'root' || me.role === 'admin') items.push(['/twitch', 'Twitch'])
  const channel = base.startsWith('/c/') ? base.slice(3) : null
  const healthy = !!status && status.eventSubConnected && status.dbOk && !(status.chat ?? '').startsWith('NONE')
  return (
    <div className="layout">
      <aside className={`sidebar ${open ? 'open' : ''}`}>
        <div className="brand"><span className={`dot ${healthy ? 'on' : ''}`} title={healthy ? 'все подключения в норме' : 'есть проблемы'} />SkillzBot{channel ? <small className="muted"> {channel}</small> : null}</div>
        {me.managed && <nav className="nav"><a href="/">← Каналы</a></nav>}
        <nav className="nav">{items.map(([to, label]) => <NavLink key={to} to={base + to} end={to === '/'}>{label}</NavLink>)}</nav>
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
