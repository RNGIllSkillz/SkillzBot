import { useState } from 'react'
import { del, get, post, put } from '../api'
import type { QuizRow } from '../types'
import { Card, Confirm, Notice, useAsync } from '../components/ui'

export default function Quiz() {
  const list = useAsync(() => get<QuizRow[]>('/api/quiz'), [])
  const [edit, setEdit] = useState<Partial<QuizRow> | null>(null)
  const [note, setNote] = useState<string | null>(null)
  const save = async () => {
    if (!edit?.question?.trim() || !edit?.answer?.trim()) { setNote('Нужны вопрос и ответ.'); return }
    try {
      if (edit.id) await put(`/api/quiz/${edit.id}`, edit); else await post('/api/quiz', edit)
      setEdit(null); setNote('Сохранено.'); list.reload()
    } catch (e: any) { setNote(`Ошибка: ${e.message}`) }
  }
  return (
    <>
      <div className="page-title"><h1>Викторина</h1>
        <div className="toolbar" style={{ margin: 0 }}>
          <Confirm text="Запустить викторину в чате сейчас?" onYes={() => post('/api/actions/quiz').then(() => setNote('Викторина запущена.')).catch(e => setNote(e.message))}>Запустить сейчас</Confirm>
          <button className="primary" onClick={() => setEdit({ question: '', answer: '', prize: 1 })}>+ Вопрос</button>
        </div>
      </div>
      {note && <Notice>{note}</Notice>}
      {edit && (
        <Card title={edit.id ? `Вопрос #${edit.id}` : 'Новый вопрос'}>
          <div className="grid" style={{ gridTemplateColumns: '1fr 1fr 100px auto auto', alignItems: 'end' }}>
            <label>Вопрос<input style={{ width: '100%' }} value={edit.question ?? ''} onChange={e => setEdit({ ...edit, question: e.target.value })} /></label>
            <label>Ответ<input style={{ width: '100%' }} value={edit.answer ?? ''} onChange={e => setEdit({ ...edit, answer: e.target.value })} /></label>
            <label>Приз<input type="number" min={0} style={{ width: '100%' }} value={edit.prize ?? 1} onChange={e => setEdit({ ...edit, prize: Number(e.target.value) })} /></label>
            <button className="primary" onClick={save}>Сохранить</button><button onClick={() => setEdit(null)}>Отмена</button>
          </div>
        </Card>
      )}
      <Card>
        <table><thead><tr><th>#</th><th>Вопрос</th><th>Ответ</th><th className="num">Приз</th><th></th></tr></thead>
          <tbody>{(list.data ?? []).map(q => (
            <tr key={q.id}><td className="muted">{q.id}</td><td>{q.question}</td><td>{q.answer}</td><td className="num">{q.prize}</td>
              <td style={{ whiteSpace: 'nowrap' }}><button onClick={() => setEdit(q)}>Изменить</button>{' '}
                <Confirm className="danger" text={`Удалить вопрос #${q.id}?`} onYes={() => del(`/api/quiz/${q.id}`).then(() => list.reload())}>Удалить</Confirm></td></tr>))}</tbody></table>
      </Card>
    </>
  )
}
