using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SkillzBot.IllSkillzBot.Predictions
{
    public enum PredictionScope
    {
        /// <summary>Classic win/lose of the streamer's game.</summary>
        WinLose,
        /// <summary>Streamer against the enemy in the same lane position.</summary>
        Lane,
        /// <summary>Five champions of the streamer's team.</summary>
        Team,
        /// <summary>All ten champions in the game.</summary>
        All
    }

    public enum PredictionMetric { None, Kills, Cs, Gold, Damage, Kda }

    /// <summary>One selectable prediction type. Titles respect Twitch limits: 45 chars for a prediction title, 25 for poll choices and outcomes.</summary>
    public sealed record PredictionKind(string Key, PredictionScope Scope, PredictionMetric Metric, int Weight, string PollLabel, string Title);

    /// <summary>Per-player numbers from a finished match, decoupled from the Riot client types.</summary>
    public sealed record ParticipantStats(string Puuid, int ChampionId, int TeamId, string TeamPosition,
        int Kills, int Deaths, int Assists, int Cs, int Gold, int Damage)
    {
        public double Kda => Deaths == 0 ? Kills + Assists : Math.Round((Kills + Assists) / (double)Deaths, 2);
    }

    public enum LaneOutcome { StreamerWins, OpponentWins, Tie, RoleError, StreamerMissing }

    public sealed record LaneResult(LaneOutcome Outcome, double StreamerValue, double OpponentValue, int OpponentChampionId);

    public sealed record GroupResult(bool IsTie, int WinnerChampionId, double WinnerValue);

    public static class PredictionCatalog
    {
        public const int MaxTitleLength = 45;
        public const int MaxOutcomeLength = 25;
        public const string OpponentLabel = "Оппонент на лайне";
        public const string PollTitle = "Какую некст ставку запустить?";

        public static readonly PredictionKind WinLose =
            new PredictionKind("winlose", PredictionScope.WinLose, PredictionMetric.None, 0, "вин/луз", "Вин или луз?");

        /// <summary>Weights are the original random chances of each prediction type.</summary>
        public static readonly IReadOnlyList<PredictionKind> Alternatives = new[]
        {
            new PredictionKind("kills2",  PredictionScope.Lane, PredictionMetric.Kills,  15, "Киллы: игрок vs лайн", "Кто сделает больше убийств?"),
            new PredictionKind("cs2",     PredictionScope.Lane, PredictionMetric.Cs,     20, "CS: игрок vs лайн",    "У кого будет больше CS?"),
            new PredictionKind("gold2",   PredictionScope.Lane, PredictionMetric.Gold,   20, "Голда: игрок vs лайн", "Кто заработает больше золота?"),
            new PredictionKind("dmg2",    PredictionScope.Lane, PredictionMetric.Damage, 20, "Урон: игрок vs лайн",  "Кто нанесет больше урона?"),
            new PredictionKind("kda2",    PredictionScope.Lane, PredictionMetric.Kda,    30, "KDA: игрок vs лайн",   "У кого будет выше KDA?"),

            new PredictionKind("kills5",  PredictionScope.Team, PredictionMetric.Kills,  25, "Киллы в команде",        "Команда игрока: у кого больше киллов?"),
            new PredictionKind("cs5",     PredictionScope.Team, PredictionMetric.Cs,     20, "CS в команде",           "Команда игрока: у кого больше CS?"),
            new PredictionKind("gold5",   PredictionScope.Team, PredictionMetric.Gold,   15, "Голда в команде",        "Команда игрока: у кого больше золота?"),
            new PredictionKind("dmg5",    PredictionScope.Team, PredictionMetric.Damage, 18, "Урон в команде",         "Команда игрока: у кого больше урона?"),
            new PredictionKind("kda5",    PredictionScope.Team, PredictionMetric.Kda,    20, "KDA в команде",          "Команда игрока: у кого выше KDA?"),

            new PredictionKind("kills10", PredictionScope.All,  PredictionMetric.Kills,  5,  "Киллы среди 10",         "Все 10 игроков: у кого больше киллов?"),
            new PredictionKind("cs10",    PredictionScope.All,  PredictionMetric.Cs,     5,  "CS среди 10",            "Все 10 игроков: у кого больше CS?"),
            new PredictionKind("gold10",  PredictionScope.All,  PredictionMetric.Gold,   4,  "Голда среди 10",         "Все 10 игроков: у кого больше золота?"),
            new PredictionKind("dmg10",   PredictionScope.All,  PredictionMetric.Damage, 4,  "Урон среди 10",          "Все 10 игроков: у кого больше урона?"),
            new PredictionKind("kda10",   PredictionScope.All,  PredictionMetric.Kda,    5,  "KDA среди 10",           "Все 10 игроков: у кого выше KDA?"),
        };

        public static IEnumerable<PredictionKind> All => new[] { WinLose }.Concat(Alternatives);

        public static PredictionKind Find(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            return All.FirstOrDefault(k => k.Key.Equals(key.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public static PredictionKind FindByPollLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return null;
            return All.FirstOrDefault(k => k.PollLabel.Equals(label.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Weighted sampling without replacement: rarer prediction types show up in polls less often.</summary>
        public static List<PredictionKind> PickPollOptions(int count, Random rng)
        {
            var pool = Alternatives.ToList();
            var picked = new List<PredictionKind>();
            while (picked.Count < count && pool.Count > 0)
            {
                int total = pool.Sum(k => k.Weight);
                int roll = rng.Next(total);
                int acc = 0;
                PredictionKind chosen = pool[^1];
                foreach (var k in pool)
                {
                    acc += k.Weight;
                    if (roll < acc) { chosen = k; break; }
                }
                picked.Add(chosen);
                pool.Remove(chosen);
            }
            return picked;
        }

        public static double Value(ParticipantStats p, PredictionMetric metric) => metric switch
        {
            PredictionMetric.Kills => p.Kills,
            PredictionMetric.Cs => p.Cs,
            PredictionMetric.Gold => p.Gold,
            PredictionMetric.Damage => p.Damage,
            PredictionMetric.Kda => p.Kda,
            _ => 0,
        };

        public static string FormatValue(double value, PredictionMetric metric) => metric == PredictionMetric.Kda
            ? value.ToString("0.##", CultureInfo.InvariantCulture)
            : ((long)value).ToString(CultureInfo.InvariantCulture);

        /// <summary>Streamer against the single enemy sharing their lane position.</summary>
        public static LaneResult ResolveLane(IReadOnlyList<ParticipantStats> players, string streamerPuuid, PredictionMetric metric)
        {
            var streamer = players.FirstOrDefault(p => string.Equals(p.Puuid, streamerPuuid, StringComparison.OrdinalIgnoreCase));
            if (streamer == null) return new LaneResult(LaneOutcome.StreamerMissing, 0, 0, 0);
            if (string.IsNullOrEmpty(streamer.TeamPosition)) return new LaneResult(LaneOutcome.RoleError, 0, 0, 0);

            var opponents = players.Where(p => p.TeamId != streamer.TeamId &&
                                               string.Equals(p.TeamPosition, streamer.TeamPosition, StringComparison.OrdinalIgnoreCase)).ToList();
            if (opponents.Count != 1) return new LaneResult(LaneOutcome.RoleError, 0, 0, 0);

            var opponent = opponents[0];
            double a = Value(streamer, metric), b = Value(opponent, metric);
            var outcome = a > b ? LaneOutcome.StreamerWins : a < b ? LaneOutcome.OpponentWins : LaneOutcome.Tie;
            return new LaneResult(outcome, a, b, opponent.ChampionId);
        }

        /// <summary>Single best player of a group; a shared maximum is a tie.</summary>
        public static GroupResult ResolveGroup(IEnumerable<ParticipantStats> group, PredictionMetric metric)
        {
            var scored = group.Select(p => (p.ChampionId, Value: Value(p, metric))).ToList();
            if (scored.Count == 0) return new GroupResult(true, 0, 0);
            double max = scored.Max(s => s.Value);
            var leaders = scored.Where(s => s.Value == max).ToList();
            return leaders.Count == 1
                ? new GroupResult(false, leaders[0].ChampionId, max)
                : new GroupResult(true, 0, max);
        }

        public static string Truncate(string text, int max) =>
            string.IsNullOrEmpty(text) || text.Length <= max ? text : text.Substring(0, max);
    }
}
