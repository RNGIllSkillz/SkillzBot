import { useEffect, useState } from 'react'
import { get, post, put } from '../api'
import type { FilterList } from '../types'
import { Card, Notice, useAsync } from '../components/ui'

export default function Filters() {
  const lists = useAsync(() => get<FilterList[]>('/api/filters'), [])
  const [name, setName] = useState<string>('dic')
  const [text, setText] = useState('')
  const [dirty, setDirty] = useState(false)
  const [note, setNote] = useState<string | null>(null)
  const current = lists.data?.find(l => l.name === name)
  useEffect(() => { if (current) { setText(current.lines.join('\n')); setDirty(false) } }, [current?.name, lists.data])
  const save = async () => {
    try {
      await put(`/api/filters/${name}`, { lines: text.split('\n') })
      setNote(`Список «${current?.title}» сохранен и перечитан ботом.`); setDirty(false); lists.reload()
    } catch (e: any) { setNote(`Ошибка: ${e.message}`) }
  }
  return (
    <>
      <div className="page-title"><h1>Фильтры и словари</h1>
        <div className="toolbar" style={{ margin: 0 }}>
          <button onClick={() => post('/api/actions/filters/reload').then(() => setNote('Фильтры перечитаны из файлов.'))}>Перечитать файлы</button>
          <button className="primary" disabled={!dirty} onClick={save}>Сохранить</button>
        </div>
      </div>
      {note && <Notice>{note}</Notice>}
      {lists.error && <Notice kind="bad">{lists.error}</Notice>}
      <div className="grid" style={{ gridTemplateColumns: '260px 1fr' }}>
        <Card title="Списки">
          {(lists.data ?? []).map(l => (
            <div key={l.name} className="toggle" style={{ cursor: 'pointer' }} onClick={() => { if (!dirty || window.confirm('Есть несохраненные изменения, переключить?')) setName(l.name) }}>
              <div className="lbl"><span style={name === l.name ? { color: 'var(--accent)', fontWeight: 600 } : undefined}>{l.title}</span><small>{l.lines.length} строк · {l.shared ? 'общий для каналов' : 'этот канал'}</small></div>
            </div>))}
        </Card>
        <Card title={current?.title ?? ''}>
          <p className="muted">Одна запись на строку. Пустые строки и дубли убираются при сохранении; предыдущая версия файла остается рядом как .bak.</p>
          <textarea value={text} onChange={e => { setText(e.target.value); setDirty(true) }} spellCheck={false} />
        </Card>
      </div>
    </>
  )
}
