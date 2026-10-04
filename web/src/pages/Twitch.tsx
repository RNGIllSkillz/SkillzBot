import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { del, fmtAge, fmtTime, get } from '../api'
import type { Me, TwitchTokenStatus } from '../types'
import { Badge, Card, Confirm, Notice, useAsync } from '../components/ui'

const GRANT_MESSAGES: Record<string, string> = {
  ok: 'Готово: токен сохранен, бот уже использует его.',
  denied: 'Авторизация отменена на стороне Twitch.',
  state: 'Сессия авторизации устарела, попробуй еще раз.',
  token: 'Twitch не выдал токен. Проверь TApiClientSecret и Redirect URL приложения.',
  validate: 'Не удалось проверить выданный токен.',
  wronguser: 'Авторизовался не владелец канала. Нужен аккаунт стримера.',
  wrongbot: 'Авторизовался не аккаунт бота (BotTwitchName).',
  error: 'Ошибка авторизации, подробности в логе бота.',
}
const SOURCE: Record<TwitchTokenStatus['source'], string> = { oauth: 'выдан через панель, обновляется сам', config: 'статический токен из конфига', none: 'нет токена' }

/** Twitch tokens of both identities: who authorized, which scopes, when it expires. Root and the broadcaster see it; the bot grant is root-only. */
export default function Twitch({ me }: { me: Me }) {
  const tokens = useAsync(() => get<TwitchTokenStatus[]>('/api/twitch/tokens'), [])
  const [params, setParams] = useSearchParams()
  const [note, setNote] = useState<string | null>(null)
  const grant = params.get('grant')
  useEffect(() => { if (grant) { setNote(GRANT_MESSAGES[grant] ?? `Результат: ${grant}`); setParams({}, { replace: true }) } }, [grant])
  const remove = async (identity: string) => {
    try { tokens.setData(await del<TwitchTokenStatus[]>(`/api/twitch/tokens/${identity}`)); setNote('Токен панели удален; бот вернулся к токену из конфига, если он задан.') }
    catch (e: any) { setNote(`Ошибка: ${e.message}`) }
  }
  const authorize = (identity: string) => window.location.assign(`/api/twitch/authorize?identity=${identity}`)
  return (
    <>
      <div className="page-title"><h1>Twitch</h1><span className="muted">токены бота и стримера</span></div>
      {note && <Notice kind={grant && grant !== 'ok' ? 'bad' : undefined}>{note}</Notice>}
      {tokens.error && <Notice kind="bad">{tokens.error}</Notice>}
      <p className="muted">Бот действует от двух аккаунтов. От имени стримера идут ставки, опросы, награды, VIP, модерация и подписки EventSub; от имени аккаунта бота - чат и шепот. Токены, выданные через эту страницу, обновляются автоматически; токены из конфига (TApiAccessToken, BotTwitchAuth) остаются запасным вариантом и однажды истекут.</p>
      <div className="grid two">
        {(tokens.data ?? []).map(t => {
          const isBot = t.identity === 'bot'
          const canGrant = !isBot || me.role === 'root'
          const tone = t.source === 'none' || !t.valid ? 'bad' : t.missingScopes.length > 0 ? 'warn' : 'good'
          return (
            <Card key={t.identity} title={isBot ? 'Аккаунт бота' : 'Стример'}>
              <div className="kv">
                <dt>Состояние</dt>
                <dd><Badge tone={tone}>{t.source === 'none' ? 'нет токена' : !t.valid ? 'недействителен' : t.missingScopes.length > 0 ? 'не хватает прав' : 'в порядке'}</Badge> <span className="muted">{SOURCE[t.source]}</span></dd>
                <dt>Аккаунт</dt>
                <dd>{t.login ? <>{t.login}{t.userId ? <span className="muted"> · id {t.userId}</span> : null}</> : '—'}{t.expectedLogin && t.login !== t.expectedLogin ? <span className="muted"> (ожидается {t.expectedLogin})</span> : null}</dd>
                <dt>Истекает через</dt>
                <dd>{t.expiresInSeconds === null ? '—' : t.expiresInSeconds > 365 * 86400 ? 'без срока' : fmtAge(t.expiresInSeconds)}{t.source === 'oauth' ? <span className="muted"> · обновляется за 20 минут до конца{t.lastRefreshUtc ? `, последний раз ${fmtTime(t.lastRefreshUtc)}` : ''}</span> : null}</dd>
                <dt>Права</dt>
                <dd>{t.missingScopes.length === 0
                  ? <span>все {t.requiredScopes.length} нужных scope есть</span>
                  : <span>не хватает {t.missingScopes.length} из {t.requiredScopes.length}: <span className="mono">{t.missingScopes.join(' ')}</span></span>}</dd>
              </div>
              {t.lastError && <Notice kind="bad">{t.lastError}</Notice>}
              {!t.refreshable && t.source === 'oauth' && <p className="muted">Обновление невозможно: в конфиге нужны ApiClientId и TApiClientSecret вашего приложения.</p>}
              <p className="muted">{isBot
                ? `Нажимать из браузера, где в Twitch выполнен вход под аккаунтом бота${t.expectedLogin ? ` (${t.expectedLogin})` : ''}: Twitch покажет выбор аккаунта.`
                : `Авторизоваться должен владелец канала${t.expectedLogin ? ` (${t.expectedLogin})` : ''}: он входит в панель под своим аккаунтом и нажимает кнопку.`}</p>
              <div className="toolbar">
                {canGrant && <button className="primary" onClick={() => authorize(t.identity)}>{t.source === 'oauth' ? 'Авторизовать заново' : 'Авторизовать'}</button>}
                {me.role === 'root' && t.source === 'oauth' && <Confirm className="danger" text="Отозвать и удалить токен панели? Бот вернется к токену из конфига, если он задан." onYes={() => remove(t.identity)}>Удалить токен</Confirm>}
              </div>
            </Card>
          )
        })}
      </div>
    </>
  )
}
