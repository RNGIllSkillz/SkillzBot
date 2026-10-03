export interface Me { twitchId: number; login: string; role: 'admin' | 'editor' }

export interface Status {
  timeUtc: string; version: string; uptimeSeconds: number; ramMb: number; threads: number
  ircConnected: boolean; ircLastTrafficSeconds: number
  eventSubConnected: boolean; eventSubSinceSeconds?: number; eventSubLastEventSeconds?: number; eventSubReconnects: number
  chatPending: number; chatProcessed: number; chatBuffered: number; chatStalled: number; chatLastStall: string
  dbOk: boolean; dbFailures: number
  streamElementsFailures: number; streamElementsLastOkSeconds?: number
  proxy: string
  silent: boolean; subActive: boolean; filterLevel: number; autoPred: boolean; inMatch: boolean; online: boolean
}

export interface BotState {
  godMode: boolean; wisEnabled: boolean; inMatch: boolean; debug: boolean; autoPred: boolean; quizIsRunning: boolean
  broadcasterIsOnline: boolean; firstQuizOfTheDay: boolean; isSilent: boolean; isSubActive: boolean
  chatFilterLvl: number; antiBotProtectionLvl: number; performanceDebugMode: boolean
  nextPredictionKey: string | null; lastPredictionPollUtc: string; predictionPollEnabled: boolean
  liveSecondsBank: number; liveSinceUtc: string | null; nextPollAfterLiveSec: number
  activePrediction: { matchId: string; predictionId: string; kindKey: string; startedUtc: string } | null
  activePoll: { pollId: string; startedUtc: string; optionKeys: string[] } | null
  vipAutoRotate: boolean; vipLastSyncUtc: string | null; vipRegistrySeeded: boolean
}

export interface GameState { summonerName: string; summonerRegion: string; startLP: number; elo: string; earnedLP: number; numLosses: number; numWins: number; numGames: number; tier: string }

export interface ChatMessage { id: string; time: string; login: string; displayName: string; text: string; isMod: boolean; isVip: boolean; isSub: boolean; isBroadcaster: boolean; fromBot: boolean; color: string | null }

export interface Paged<T> { items: T[]; page: number; pageSize: number; total: number }

export interface UserRow {
  dbID: number; twitchID: number; name: string; isSub: number; isVip: number; isMod: number; isPartner: number; isBroadcaster: number
  uvalCon: number; messageCon: number; roulettCon: number; roulettCD: number; uvalTimer: number; banCount: number; points: number
  isOnline: number; quizPoints: number; quizTotal: number
}

export interface MessageRow { id: number; twitchId: number; name: string; message: string; time: string }
export interface ActivityBucket { time: string; messages: number; users: number }

export interface KindStat { kind: string; count: number; avgUsers: number; avgPoints: number }
export interface OutcomeStat { title: string; avgUsers: number; avgPoints: number; wins: number }
export interface Engagement {
  days: number; predictions: number; manualPredictions: number; winLose: number; winLoseAvgUsers: number; winLoseMaxUsers: number; winLoseAvgPoints: number
  other: number; otherAvgUsers: number; totalPoints: number; knownBettors: number; byKind: KindStat[]; winLoseOutcomes: OutcomeStat[]
  polls: number; pollAvgVotes: number; pollMaxVotes: number
}
export interface PredictionRow { predictionId: string; kind: string; source: string; title: string; matchId: string | null; startedAt: string | null; endedAt: string | null; status: string | null; totalUsers: number; totalPoints: number; outcomes: { title: string; color: string; users: number; channelPoints: number; isWinner: boolean }[] }
export interface PollRow { pollId: string; source: string; title: string; startedAt: string | null; endedAt: string | null; status: string | null; totalVotes: number; winnerTitle: string | null; choices: { title: string; kindKey: string | null; votes: number; channelPointsVotes: number }[] }

export interface VipRecord { twitchId: number; login: string; displayName: string; since: string | null; source: string; dbId: number | null; firstSeenUtc: string; pinned: boolean }
export interface VipsResponse { limit: number; autoRotate: boolean; lastSyncUtc: string | null; vips: VipRecord[] }

export interface FilterList { name: string; title: string; shared: boolean; lines: string[] }
export interface QuizRow { id: number; question: string; answer: string; prize: number }
export interface ConfigView { values: Record<string, string | number | boolean | null>; secretKeys: string[]; secretKeysSet: string[]; editorKeys: string[] }
export interface SystemInfo { version: string; runtime: string; startedUtc: string; dataPath: string; channel: string; apiPort: number; publicUrl: string | null }
export interface EditorRow { twitchId: number; login: string; addedBy: string | null; addedAt: string }
export interface PredictionsStatus { summary: string; active: BotState['activePrediction']; poll: BotState['activePoll']; nextKind: string | null; kinds: string }
