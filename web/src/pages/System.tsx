import { useEffect, useState } from 'react'
import { del, fmtTime, get, post } from '../api'
import type { EditorRow, Me, SystemInfo } from '../types'
import { Card, Confirm, Notice, useAsync } from '../components/ui'

export default function System({ me }: { me: Me }) {
  const info = useAsync(() => get<SystemInfo>('/api/system/info'), [])
  const [file, setFile] = useState<'bot' | 'errors'>('bot')
  const [lines, setLines] = useState(300)
  const [auto, setAuto] = useState(false)
  const log = useAsync(() => get<{ lines: string[] }>(`/api/logs?file=${file}&lines=${lines}`), [file, lines])
  const [note, setNote] = useState<string | null>(null)
  useEffect(() => { if (!auto) return; const t = setInterval(() => log.reload(), 5000); return () => clearInterval(t) }, [auto, file, lines])
  const restart = async () => {
    try { await post('/api/system/restart'); setNote('Бот завершает работу; лаунчер поднимет его заново через несколько секунд. Страница обновится сама.'); setTimeout(() => window.location.reload(), 20000) }
    catch (e: any) { setNote(`Ошибка: ${e.message}`) }
  }
  return (
    <>
      <div className="page-title"><h1>Система</h1>
        <Confirm className="danger" text="Перезапустить бота? Чат, ставки и опросы продолжат работу после старта (активная ставка восстановится из сохраненного состояния)." onYes={restart}>Перезапустить бота</Confirm>
      </div>
      {note && <Notice>{note}</Notice>}
      <div className="grid two">
        <Card title="Процесс">
          {info.data && <dl className="kv">
            <dt>Версия</dt><dd>{info.data.version}</dd>
            <dt>Среда</dt><dd>{info.data.runtime}</dd>
            <dt>Запущен</dt><dd>{fmtTime(info.data.startedUtc)}</dd>
            <dt>Канал</dt><dd>{info.data.channel}</dd>
            <dt>Данные</dt><dd className="mono">{info.data.dataPath}</dd>
            <dt>API</dt><dd>порт {info.data.apiPort} · {info.data.publicUrl || 'ApiPublicUrl не задан'}</dd>
          </dl>}
        </Card>
        {me.role === 'admin' && <Editors />}
      </div>
      <Card title="Лог" className="">
        <div className="toolbar">
          <button className={file === 'bot' ? 'primary' : ''} onClick={() => setFile('bot')}>bot</button>
          <button className={file === 'errors' ? 'primary' : ''} onClick={() => setFile('errors')}>errors</button>
          <select value={lines} onChange={e => setLines(Number(e.target.value))}>{[100, 300, 1000, 3000].map(n => <option key={n} value={n}>{n} строк</option>)}</select>
          <button onClick={() => log.reload()}>Обновить</button>
          <label className="muted"><input type="checkbox" checked={auto} onChange={e => setAuto(e.target.checked)} /> каждые 5 с</label>
          <span className="spacer" />
          <button onClick={() => navigator.clipboard.writeText((log.data?.lines ?? []).join('\n'))}>Скопировать</button>
        </div>
        <div className="log mono">{(log.data?.lines ?? []).map((l, i) => <div key={i} className={l.includes('[ERR]') || l.includes('[FTL]') ? 'err' : l.includes('[WRN]') ? 'wrn' : ''}>{l}</div>)}</div>
      </Card>
    </>
  )
}

function Editors() {
  const editors = useAsync(() => get<EditorRow[]>('/api/editors'), [])
  const [login, setLogin] = useState('')
  const [err, setErr] = useState<string | null>(null)
  const add = async (e: React.FormEvent) => {
    e.preventDefault()
    try { await post('/api/editors', { login }); setLogin(''); setErr(null); editors.reload() } catch (ex: any) { setErr(ex.message) }
  }
  return (
    <Card title="Редакторы панели">
      <p className="muted">Стример и root входят всегда. Остальные - по этому списку (то же, что !editor add в чате).</p>
      {err && <Notice kind="bad">{err}</Notice>}
      <form className="toolbar" onSubmit={add}><input placeholder="twitch login" value={login} onChange={e => setLogin(e.target.value)} /><button className="primary" type="submit" disabled={!login.trim()}>Добавить</button></form>
      <table><thead><tr><th>Логин</th><th>Добавил</th><th>Когда</th><th></th></tr></thead>
        <tbody>{(editors.data ?? []).map(e => <tr key={e.twitchId}><td>{e.login}</td><td>{e.addedBy}</td><td>{fmtTime(e.addedAt)}</td>
          <td><Confirm className="danger" text={`Убрать ${e.login} из редакторов?`} onYes={() => del(`/api/editors/${e.twitchId}`).then(() => editors.reload())}>Убрать</Confirm></td></tr>)}</tbody></table>
    </Card>
  )
}
