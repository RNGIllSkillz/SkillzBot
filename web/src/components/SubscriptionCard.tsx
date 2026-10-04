import { useState } from 'react'
import { fmtDate, get, put } from '../api'
import type { Subscription } from '../types'
import { Badge, Card, Confirm, Notice, useAsync } from './ui'

const days = (n: number) => {
  const m10 = n % 10, m100 = n % 100
  const word = m100 >= 11 && m100 <= 14 ? 'дней' : m10 === 1 ? 'день' : m10 >= 2 && m10 <= 4 ? 'дня' : 'дней'
  return `${n} ${word}`
}

/** Subscription status for root and the broadcaster; the editing controls appear for root only. */
export default function SubscriptionCard({ canEdit }: { canEdit: boolean }) {
  const sub = useAsync(() => get<Subscription>('/api/subscription'), [])
  const [due, setDue] = useState('')
  const [note, setNote] = useState<string | null>(null)
  const s = sub.data
  const save = async (body: unknown, label: string) => {
    try { const r = await put<Subscription>('/api/subscription', body); sub.setData(r); setDue(''); setNote(label) }
    catch (e: any) { setNote(`Ошибка: ${e.message}`) }
  }
  const tone = !s || s.unlimited ? 'good' : !s.active ? 'bad' : (s.daysLeft ?? 0) <= 7 ? 'warn' : 'good'
  return (
    <Card title="Подписка">
      {note && <Notice>{note}</Notice>}
      {sub.error && <Notice kind="bad">{sub.error}</Notice>}
      {s && (
        <div className="kv">
          <dt>Статус</dt>
          <dd><Badge tone={tone}>{s.active ? 'активна' : 'истекла'}</Badge> {s.unlimited ? 'без ограничения срока' : s.active ? `осталось ${days(s.daysLeft ?? 0)}` : 'бот слушает чат, но не отвечает до продления'}</dd>
          <dt>Действует до</dt>
          <dd>{s.unlimited ? '—' : <>{fmtDate(s.due)} <span className="muted">включительно</span></>}</dd>
        </div>
      )}
      {canEdit && s && (
        <div className="toolbar" style={{ marginTop: 8 }}>
          <input type="date" value={due} onChange={e => setDue(e.target.value)} />
          <button className="primary" disabled={!due} onClick={() => save({ due }, `Подписка действует до ${new Date(due).toLocaleDateString('ru-RU')} включительно.`)}>Установить дату</button>
          <button onClick={() => save({ addMonths: 1 }, 'Добавлен один месяц.')}>+1 месяц</button>
          {!s.unlimited && <Confirm text="Снять ограничение срока? Бот будет активен без даты окончания." onYes={() => save({ due: null }, 'Ограничение срока снято.')}>Без срока</Confirm>}
        </div>
      )}
    </Card>
  )
}
