import { useEffect, useState } from 'react'
import { ApiError, fmtAge, fmtTime, hubDel, hubGet, hubPost } from './api'
import type { HubChannel, HubMe, HubStatus, TwitchTokenStatus } from './types'
import { Badge, Card, Confirm, Notice, useAsync } from './components/ui'

const AUTH_ERRORS: Record<string, string> = {
  denied: 'Вход через Twitch отменен.',
  state: 'Сессия входа устарела, попробуй еще раз.',
  token: 'Twitch не выдал токен. Проверь TApiClientSecret и Redirect URL приложения в hub.json.',
  error: 'Ошибка входа, подробности в логе хаба.',
}
const GRANT_MESSAGES: Record<string, string> = {
  'bot-ok': 'Готово: токен аккаунта бота сохранен, каналы получат его в течение минуты.',
  ok: 'Готово: токен стримера сохранен.',
  denied: 'Авторизация отменена на стороне Twitch.',
  state: 'Сессия авторизации устарела, попробуй еще раз.',
  token: 'Twitch не выдал токен. Проверь TApiClientSecret и Redirect URL приложения.',
  wrongbot: 'Авторизовался не аккаунт бота (BotTwitchName в hub.json).',
  wronguser: 'Авторизовался не владелец канала.',
  forbidden: 'Авторизовать аккаунт бота может только root.',
  unknown: 'Канал не найден в реестре хаба.',
  error: 'Ошибка авторизации, подробности в логе хаба.',
}
const STATE: Record<string, { label: string; tone?: 'good' | 'bad' | 'warn' }> = {
  running: { label: 'работает', tone: 'good' },
  starting: { label: 'запускается', tone: 'warn' },
  stopped: { label: 'остановлен' },
  disabled: { label: 'выключен' },
  crashed: { label: 'упал, перезапуск', tone: 'bad' },
  failed: { label: 'не запускается', tone: 'bad' },
}

/** The hub: login, the streamer's channels, onboarding of a new channel, and for root the bot account and the fleet. */
export default function HubApp() {
  const [me, setMe] = useState<HubMe | null | undefined>(undefined)
  const [configured, setConfigured] = useState(true)
  useEffect(() => {
    hubGet<HubMe>('/api/auth/me').then(setMe).catch((e: ApiError) => { setMe(null); setConfigured(e.body?.loginConfigured !== false) })
  }, [])
  if (me === undefined) return <div className="login"><div className="muted">Загрузка…</div></div>
  if (me === null) return <HubLogin configured={configured} />
  return <HubHome me={me} />
}

function HubLogin({ configured }: { configured: boolean }) {
  const params = new URLSearchParams(window.location.search)
  const err = params.get('auth')
  const grant = params.get('grant')
  return (
    <div className="login">
      <div className="card">
        <h1>SkillzBot</h1>
        <p className="muted">Бот для Twitch-каналов. Войди через Twitch, чтобы добавить бота на свой канал или открыть панель.</p>
        {err && <div className="banner bad" style={{ textAlign: 'left' }}>{AUTH_ERRORS[err] ?? `Ошибка входа: ${err}`}</div>}
        {grant && <div className="banner bad" style={{ textAlign: 'left' }}>{GRANT_MESSAGES[grant] ?? `Результат: ${grant}`}</div>}
        {configured
          ? <a className="primary" href="/api/auth/login?returnTo=%2F"><button className="primary">Войти через Twitch</button></a>
          : <div className="banner">Хаб не настроен: в Channels_Data/hub.json нужны ApiPublicUrl, ApiClientId и TApiClientSecret.</div>}
      </div>
    </div>
  )
}

function HubHome({ me }: { me: HubMe }) {
  const root = me.role === 'root'
  const channels = useAsync(() => hubGet<HubChannel[]>('/api/hub/channels'), [])
  const [note, setNote] = useState<{ text: string; bad?: boolean } | null>(null)
  useEffect(() => {
    const params = new URLSearchParams(window.location.search)
    const grant = params.get('grant'); const auth = params.get('auth')
    if (grant) setNote({ text: GRANT_MESSAGES[grant] ?? `Результат: ${grant}`, bad: !grant.endsWith('ok') })
    else if (auth) setNote({ text: AUTH_ERRORS[auth] ?? `Ошибка входа: ${auth}`, bad: true })
    if (grant || auth) window.history.replaceState(null, '', '/')
  }, [])
  // process states change on their own (restarts, crashes): refresh while the page is visible
  useEffect(() => {
    const t = setInterval(() => { if (document.visibilityState === 'visible') channels.reload() }, 5000)
    return () => clearInterval(t)
  }, [])
  const logout = () => hubPost('/api/auth/logout').then(() => window.location.assign('/'))
  const act = async (login: string, action: 'restart' | 'enable' | 'disable') => {
    try { await hubPost(`/api/hub/channels/${login}/${action}`); channels.reload(); setNote({ text: `${login}: ${action === 'restart' ? 'перезапуск запрошен' : action === 'enable' ? 'включен' : 'выключен'}.` }) }
    catch (e: any) { setNote({ text: `Ошибка: ${e.message}`, bad: true }) }
  }
  const remove = async (login: string) => {
    try { await hubDel(`/api/hub/channels/${login}`); channels.reload(); setNote({ text: `${login} убран из реестра; папка с данными осталась на диске.` }) }
    catch (e: any) { setNote({ text: `Ошибка: ${e.message}`, bad: true }) }
  }
  const mine = me.channels.length > 0
  return (
    <div className="layout">
      <aside className="sidebar open">
        <div className="brand"><span className="dot on" />SkillzBot <small className="muted">хаб</small></div>
        <nav className="nav">
          {me.channels.map(c => <a key={c.login} href={c.panelUrl}>{c.displayName || c.login}</a>)}
        </nav>
        <div className="me">
          <span>{me.login} <span className="badge accent">{me.role}</span></span>
          <button onClick={logout}>Выйти</button>
        </div>
      </aside>
      <main className="content">
        <div className="page-title"><h1>Каналы</h1><span className="muted">{root ? 'все каналы хаба' : 'твои каналы'}</span></div>
        {note && <Notice kind={note.bad ? 'bad' : 'good'}>{note.text}</Notice>}
        {channels.error && <Notice kind="bad">{channels.error}</Notice>}
        <div className="grid two">
          <Card title={mine ? 'Добавить еще один канал' : 'Добавить бота на мой канал'}>
            <p className="muted">Нажми кнопку, разреши приложению бота права на твоем канале (ставки, опросы, награды, модерация, чат), и бот появится в чате через несколько секунд. Канал создается для аккаунта, под которым выполнен вход в Twitch в этом браузере.</p>
            <p className="muted">Бот пишет в чат от аккаунта <b>{me.botTwitchName || '—'}</b>: выдай ему модератора на канале (/mod {me.botTwitchName || 'bot'}), иначе Twitch будет ограничивать его сообщения.</p>
            <div className="toolbar"><a href="/api/hub/onboard"><button className="primary">{mine ? 'Добавить канал' : 'Добавить бота на мой канал'}</button></a></div>
          </Card>
          {root && <BotCard />}
        </div>
        <Card title={root ? `Каналы (${channels.data?.length ?? 0})` : 'Твои каналы'}>
          {channels.data && channels.data.length === 0 && <p className="muted">Пока ни одного канала.</p>}
          {channels.data && channels.data.length > 0 && (
            <table>
              <thead><tr><th>Канал</th><th>Процесс</th><th>Добавлен</th>{root && <th>Порт</th>}<th></th></tr></thead>
              <tbody>{channels.data.map(c => {
                const st = STATE[c.process.state] ?? { label: c.process.state }
                return (
                  <tr key={c.login}>
                    <td><a href={c.panelUrl}><b>{c.displayName || c.login}</b></a>{!c.enabled && <> <Badge>выключен</Badge></>}</td>
                    <td><Badge tone={st.tone}>{st.label}</Badge> <span className="muted">
                      {c.process.pid ? `pid ${c.process.pid}` : ''}{c.process.startedUtc ? ` · с ${fmtTime(c.process.startedUtc)}` : ''}{c.process.restarts > 0 ? ` · перезапусков: ${c.process.restarts}` : ''}{c.process.lastExitCode !== null && c.process.state !== 'running' ? ` · код выхода ${c.process.lastExitCode}` : ''}</span></td>
                    <td className="muted">{fmtTime(c.createdUtc)}{c.addedBy ? ` · ${c.addedBy}` : ''}</td>
                    {root && <td className="mono">{c.apiPort}</td>}
                    <td className="toolbar">
                      <a href={c.panelUrl}><button>Открыть панель</button></a>
                      {c.enabled && <Confirm text={`Перезапустить бота на канале ${c.login}?`} onYes={() => act(c.login, 'restart')}>Перезапустить</Confirm>}
                      {root && (c.enabled
                        ? <Confirm text={`Выключить бота на канале ${c.login}? Процесс будет остановлен.`} onYes={() => act(c.login, 'disable')}>Выключить</Confirm>
                        : <button onClick={() => act(c.login, 'enable')}>Включить</button>)}
                      {root && <Confirm className="danger" text={`Убрать ${c.login} из хаба? Процесс остановится, папка Channels_Data/${c.login} останется.`} onYes={() => remove(c.login)}>Убрать</Confirm>}
                    </td>
                  </tr>
                )
              })}</tbody>
            </table>
          )}
        </Card>
        {root && <StatusCard />}
      </main>
    </div>
  )
}

/** Root only: the bot account's token, shared by every channel. */
function BotCard() {
  const bot = useAsync(() => hubGet<TwitchTokenStatus>('/api/hub/bot'), [])
  const [err, setErr] = useState<string | null>(null)
  const t = bot.data
  const tone = !t || t.source === 'none' || !t.valid ? 'bad' : t.missingScopes.length > 0 ? 'warn' : 'good'
  const remove = async () => { try { bot.setData(await hubDel<TwitchTokenStatus>('/api/hub/bot')) } catch (e: any) { setErr(e.message) } }
  return (
    <Card title="Аккаунт бота">
      {bot.error && <Notice kind="bad">{bot.error}</Notice>}
      {err && <Notice kind="bad">{err}</Notice>}
      {t && (
        <div className="kv">
          <dt>Состояние</dt>
          <dd><Badge tone={tone}>{t.source === 'none' ? 'нет токена' : !t.valid ? 'недействителен' : t.missingScopes.length > 0 ? 'не хватает прав' : 'в порядке'}</Badge></dd>
          <dt>Аккаунт</dt>
          <dd>{t.login ?? '—'}{t.expectedLogin && t.login !== t.expectedLogin ? <span className="muted"> (ожидается {t.expectedLogin})</span> : null}</dd>
          <dt>Истекает через</dt>
          <dd>{t.expiresInSeconds === null ? '—' : fmtAge(t.expiresInSeconds)}{t.lastRefreshUtc ? <span className="muted"> · обновлен {fmtTime(t.lastRefreshUtc)}</span> : null}</dd>
          <dt>Права</dt>
          <dd>{t.missingScopes.length === 0 ? `все ${t.requiredScopes.length} нужных scope есть` : <>не хватает: <span className="mono">{t.missingScopes.join(' ')}</span></>}</dd>
          {t.lastError && <><dt>Ошибка</dt><dd className="mono">{t.lastError}</dd></>}
        </div>
      )}
      <p className="muted">Один токен на все каналы: хаб обновляет его и раздает процессам каналов. Нажимать из браузера, где в Twitch выполнен вход под аккаунтом бота{t?.expectedLogin ? ` (${t.expectedLogin})` : ''}: Twitch покажет выбор аккаунта.</p>
      <div className="toolbar">
        <a href="/api/hub/bot/authorize"><button className="primary">{t?.source === 'oauth' ? 'Авторизовать заново' : 'Авторизовать аккаунт бота'}</button></a>
        {t?.source === 'oauth' && <Confirm className="danger" text="Отозвать и удалить токен аккаунта бота? Каналы потеряют чат, пока не выдан новый." onYes={remove}>Удалить токен</Confirm>}
      </div>
    </Card>
  )
}

function StatusCard() {
  const status = useAsync(() => hubGet<HubStatus>('/api/hub/status'), [])
  const s = status.data
  return (
    <Card title="Хаб">
      {status.error && <Notice kind="bad">{status.error}</Notice>}
      {s && (
        <div className="kv">
          <dt>Версия</dt><dd>{s.version} <span className="muted">· работает {fmtAge(s.uptimeSeconds)}</span></dd>
          <dt>Каналы</dt><dd>{s.running} из {s.channels} запущено</dd>
          <dt>Аккаунт бота</dt><dd className="mono">{s.bot}</dd>
          <dt>Публичный адрес</dt><dd className="mono">{s.publicUrl ?? '—'}</dd>
          {s.missing.length > 0 && <><dt>Не задано</dt><dd><Badge tone="bad">{s.missing.join(', ')}</Badge> <span className="muted">в Channels_Data/hub.json</span></dd></>}
        </div>
      )}
    </Card>
  )
}
