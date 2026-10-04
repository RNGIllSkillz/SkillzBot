import { useState } from 'react'
import { get, patch } from '../api'
import type { BotState, ConfigView, Me, PredictionsStatus } from '../types'
import { Card, Notice, Toggle, useAsync } from '../components/ui'

const FLAGS: [keyof BotState, string, string][] = [
  ['isSubActive', 'Бот активен', 'следует за подпиской (дашборд): при заданной дате окончания ручное значение перезапишется в течение 5 минут'],
  ['isSilent', 'Тихий режим', 'бот ничего не пишет в чат'],
  ['autoPred', 'Автоставки', 'ставка на каждую игру стримера'],
  ['predictionPollEnabled', 'Опрос о типе ставки', 'раз в 3-5 часов эфира после игры'],
  ['vipAutoRotate', 'Авторотация VIP', '!addvip снимает самого давнего VIP при полном списке'],
  ['wisEnabled', 'WIS', ''],
  ['debug', 'Debug-лог', 'уровень Debug в логах (много строк)'],
  ['performanceDebugMode', 'Perf-режим', 'время обработки сообщений root в чат'],
  ['godMode', 'God mode', 'root без кулдаунов и ограничений'],
]

export default function Settings({ me }: { me: Me }) {
  const state = useAsync(() => get<BotState>('/api/state'), [])
  const config = useAsync(() => get<ConfigView>('/api/config'), [])
  const pred = useAsync(() => get<PredictionsStatus>('/api/predictions/status'), [])
  const [note, setNote] = useState<string | null>(null)
  const [restart, setRestart] = useState(false)
  const [edits, setEdits] = useState<Record<string, string>>({})

  const setState = async (key: string, value: unknown) => {
    try { const r = await patch<{ state: BotState }>('/api/state', { [key]: value }); state.setData(r.state); setNote(null) }
    catch (e: any) { setNote(`Ошибка: ${e.message}`) }
  }
  const saveConfig = async (key: string) => {
    const raw = edits[key]; if (raw === undefined) return
    const current = config.data?.values[key]
    const value: unknown = typeof current === 'number' ? Number(raw) : typeof current === 'boolean' ? raw === 'true' : raw
    try {
      const r = await patch<{ restartRequired: boolean }>('/api/config', { [key]: value })
      if (r.restartRequired) setRestart(true)
      setEdits(e => { const n = { ...e }; delete n[key]; return n }); config.reload(); setNote(`Ключ ${key} сохранен.`)
    } catch (e: any) { setNote(`Ошибка: ${e.message}`) }
  }
  const s = state.data
  const kinds = (pred.data?.kinds ?? '').split(',').map(k => k.trim()).filter(Boolean)
  return (
    <>
      <div className="page-title"><h1>Настройки</h1></div>
      {note && <Notice>{note}</Notice>}
      {restart && <Notice>Изменение конфига применится после перезапуска бота (страница «Система»).</Notice>}
      <div className="grid two">
        <Card title="Состояние бота">
          {s && FLAGS.map(([key, label, hint]) => <Toggle key={key} label={label} hint={hint} value={Boolean(s[key])} onChange={v => setState(key, v)} />)}
          {s && (
            <>
              <div className="toggle"><div className="lbl"><span>Уровень фильтра чата</span><small>0 ничего · 1 удалить · 2 удалить и оповестить модов · 3 таймаут сутки · 4 неделя · 5 бан</small></div>
                <select value={s.chatFilterLvl} onChange={e => setState('chatFilterLvl', Number(e.target.value))}>{[0, 1, 2, 3, 4, 5].map(n => <option key={n} value={n}>{n}</option>)}</select></div>
              <div className="toggle"><div className="lbl"><span>Антибот для викторины</span><small>0 выкл · 1 только писавшие в чат · 2 плюс сброс списка после каждой викторины</small></div>
                <select value={s.antiBotProtectionLvl} onChange={e => setState('antiBotProtectionLvl', Number(e.target.value))}>{[0, 1, 2].map(n => <option key={n} value={n}>{n}</option>)}</select></div>
              <div className="toggle"><div className="lbl"><span>Следующая ставка</span><small>тип ставки на следующую игру, затем снова вин/луз</small></div>
                <select value={s.nextPredictionKey ?? 'winlose'} onChange={e => setState('nextPredictionKey', e.target.value === 'winlose' ? null : e.target.value)}>
                  <option value="winlose">вин/луз</option>{kinds.map(k => <option key={k} value={k}>{k}</option>)}</select></div>
            </>
          )}
          {state.error && <Notice kind="bad">{state.error}</Notice>}
        </Card>
        <Card title={me.role === 'root' ? 'Конфиг канала' : 'Настройки бота в конфиге'}>
          <p className="muted">{me.role === 'root'
            ? 'Полный конфиг виден только root. Секреты (токены, пароли, ProxyUrl) не показываются и не меняются через панель; большинство ключей требует перезапуска.'
            : 'Системный конфиг канала доступен только root. Здесь только настройки бота: ' + (config.data?.editorKeys.join(', ') ?? '')}</p>
          {config.data && (
            <table><tbody>
              {Object.entries(config.data.values).map(([k, v]) => (
                <tr key={k}><td className="mono" style={{ whiteSpace: 'nowrap' }}>{k}</td>
                  <td><span className="toolbar" style={{ margin: 0 }}><input style={{ flex: 1, minWidth: 120 }} value={edits[k] ?? String(v ?? '')} onChange={e => setEdits({ ...edits, [k]: e.target.value })} />
                    {edits[k] !== undefined && edits[k] !== String(v ?? '') && <button className="primary" onClick={() => saveConfig(k)}>Сохранить</button>}</span></td></tr>))}
              {config.data.secretKeys.map(k => <tr key={k}><td className="mono muted">{k}</td><td className="muted">{config.data!.secretKeysSet.includes(k) ? '●●●●● задан' : 'не задан'}</td></tr>)}
            </tbody></table>
          )}
          {config.error && <Notice kind="bad">{config.error}</Notice>}
        </Card>
      </div>
    </>
  )
}
