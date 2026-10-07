import React, { useEffect, useMemo, useState } from 'react'
import ReactDOM from 'react-dom/client'
import { BrowserRouter, Route, Routes, useParams } from 'react-router-dom'
import App from './App'
import HubApp from './HubApp'
import { ApiError, get, setApiBase } from './api'
import type { Me } from './types'
import './styles.css'

/** A channel panel behind the hub: everything it asks for goes to /c/<login>/api/... */
function ChannelRoot() {
  const { login } = useParams()
  const base = `/c/${(login ?? '').toLowerCase()}`
  useMemo(() => setApiBase(base), [base])
  return <App key={base} base={base} />
}

/** The site root is the hub when a hub answers there, otherwise a standalone single-channel bot. */
function Root() {
  const [probe, setProbe] = useState<{ hub: boolean; me: Me | null; error: ApiError | null } | undefined>(undefined)
  useEffect(() => {
    setApiBase('')
    get<Me & { hub?: boolean }>('/api/auth/me')
      .then(me => setProbe({ hub: me.hub === true, me, error: null }))
      .catch((e: ApiError) => setProbe({ hub: e.body?.hub === true, me: null, error: e }))
  }, [])
  if (probe === undefined) return <div className="login"><div className="muted">Загрузка…</div></div>
  return probe.hub ? <HubApp /> : <App base="" initial={{ me: probe.me, error: probe.error }} />
}

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <BrowserRouter>
      <Routes>
        <Route path="/c/:login/*" element={<ChannelRoot />} />
        <Route path="/*" element={<Root />} />
      </Routes>
    </BrowserRouter>
  </React.StrictMode>,
)
