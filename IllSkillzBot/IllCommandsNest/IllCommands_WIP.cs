/*using SkillzBot.API.Twitch;
using SkillzBot.IRC;
using SkillzBot.MODELS;
using SkillzBot.Utils;
using SkillzBot.WRITERS;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using SkillzBot.Writers;
using F23.StringSimilarity;
using SkillzBot.API.MMR;
using SkillzBot.API.StreamElements;
using SkillzBot.Readers;
using SkillzBot.TtvClient.TTVRewards;
using System.Globalization;
using SkillzBot.IllSTRINGS;
using IllSkillzBot;
using System.IO;
using SkillzBot.SubUtils;
using Camille.Enums;
using SkillzBot.API.RiotGames;
using SkillzBot.Interfaces;
using Microsoft.Extensions.Logging;
using SkillzBot.Hosts;
using SkillzBot.Singleton;

namespace SkillzBot.IllSkillzBot.IllCommandsNest
{
	internal class IllCommands
	{
		#region Fields & Helpers

		// Thread-safe queue for recent chat messages
		private static readonly ConcurrentQueue<string> popMessages = new ConcurrentQueue<string>();

		private static readonly IDatabaseService _databaseService = IllServiceProvider.Database;
		private static readonly ILogger<IllCommands> _logger = IllServiceProvider.GetLogger<IllCommands>();

		/// <summary>
		/// Generic helper to set integer levels with validation.
		/// Keeps behavior consistent with previous implementation,
		/// but reduces duplication for commands like SetChatfilterLvl and SetAntiBotLvl.
		/// </summary>
		private static async Task SetLevel(UserObject user, string[] input, int min, int max, Action<int> setAction, string successMsg)
		{
			if (input == null || input.Length != 2 || !int.TryParse(input[1], out int lvl) || lvl < min || lvl > max)
			{
				await TtvIRCClient.SendMessage(STRINGS.InputERROR);
				return;
			}

			setAction(lvl);
			SaveAppConfig();
			await TtvIRCClient.SendMessage(string.Format(successMsg, user.Name, lvl));
		}

		/// <summary>
		/// Generic helper to parse typed args with validation.
		/// Returns true if parsing succeeded.
		/// </summary>
		private static bool TryParseArg<T>(string arg, out T value)
		{
			value = default;
			try
			{
				if (typeof(T) == typeof(int))
				{
					if (int.TryParse(arg, out int r)) { value = (T)(object)r; return true; }
					return false;
				}
				if (typeof(T) == typeof(bool))
				{
					if (bool.TryParse(arg, out bool r)) { value = (T)(object)r; return true; }
					return false;
				}
				// fallback for string
				if (typeof(T) == typeof(string))
				{
					value = (T)(object)arg;
					return true;
				}
			}
			catch {  ignore  }
			return false;
		}

		private static void ClearPopMessages()
		{
			while (popMessages.TryDequeue(out _)) { }
		}

		// Safe wrapper to call an async action and swallow exceptions (to avoid bot-wide crashes).
		// We intentionally do not add logging as requested.
		private static async Task SafeInvoke(Func<Task> action)
		{
			try
			{
				if (action != null) await action();
			}
			catch
			{
				// swallow exceptions to keep bot alive; logging intentionally left out per request
			}
		}

		#endregion

		#region General Commands

		public static async Task Help(UserObject user)
		{
			await TtvIRCClient.SendMessage(string.Format(STRINGS.HelpMessage, user.Name));
		}

		public static async Task Ping()
		{
			await TtvIRCClient.SendMessage("pong");
		}

		public static async Task TypeInChat(string message)
		{
			if (string.IsNullOrEmpty(message)) return;

			popMessages.Enqueue(message);

			// Keep queue at most 10 items
			while (popMessages.Count > 10 && popMessages.TryDequeue(out _)) { }

			if (popMessages.Count == 10)
			{
				var jw = new NormalizedLevenshtein();
				var snapshot = popMessages.ToList();

				foreach (var popMessage in snapshot)
				{
					int popWeight = 0;
					foreach (var checkpop in snapshot)
					{
						var sim1 = jw.Distance(popMessage, checkpop);
						if (sim1 < 0.5)
						{
							popWeight++;
							if (popWeight >= 5)
							{
								// send and clear queue
								await SafeInvoke(() => TtvIRCClient.SendMessage(popMessage));
								ClearPopMessages();
								return;
							}
						}
					}
				}
			}
		}

		public static async Task TestingMethod(UserObject user)
		{
			Console.WriteLine("test");
			// reserved for tests
		}

		public static async Task<string> GetGPTResponce(string message, string userName = null)
		{
			// Disabled: placeholder kept for compatibility
			return null;
		}

		#endregion

		#region Points & Leaderboards

		public static async Task Points(UserObject user)
		{
			var taskList = new List<Task<int[]>>
			{
				_databaseService.GetUserPositionAsync(user.Name, "Points"),
				_databaseService.GetUserPositionAsync(user.Name, "QuizPoints"),
				_databaseService.GetUserPositionAsync(user.Name, "QuizTotal")
			};

			var results = await Task.WhenAll(taskList).ConfigureAwait(false);

			await TtvIRCClient.SendMessage(string.Format(
				STRINGS.PointsMessage,
				user.Name,
				user.Points,
				results[0][0],
				results[0][1],
				user.QuizPoints,
				results[1][0],
				results[1][1],
				user.QuizTotal,
				results[2][0],
				results[2][1]
			));
		}

		public static async Task RouletteTop(UserObject user) => await TopRulete();

		public static async Task TopRulete()
		{
			var result = await _databaseService.GetTopUsersAsync("rtop").ConfigureAwait(false);
			if (result != null && result.Count >= 3)
			{
				await TtvIRCClient.SendMessage(string.Format(
					STRINGS.Top3Roulette,
					result[0].Name, result[0].roulettCon, IntUtil.RulProbability(result[0].roulettCon, 80),
					result[1].Name, result[1].roulettCon, IntUtil.RulProbability(result[1].roulettCon, 80),
					result[2].Name, result[2].roulettCon, IntUtil.RulProbability(result[2].roulettCon, 80)
				));
			}
			else
			{
				_logger.LogError("Cant get 3 users at TopRulete");
			}
		}

		public static async Task GetTopChat(UserObject user)
		{
			var result = await _databaseService.GetTopUsersAsync("top").ConfigureAwait(false);
			if (result != null && result.Count >= 3)
			{
				await TtvIRCClient.SendMessage(string.Format(
					STRINGS.Top3Chat,
					result[0].Name, result[0].messageCon,
					result[1].Name, result[1].messageCon,
					result[2].Name, result[2].messageCon
				));
			}
			else
			{
				_logger.LogError("Cant get 3 users at GetTopChat");
			}
		}

		#endregion

		#region Riot Games / LP / MMR

		public static async Task<LP> GetLpAsync(string summoner = null, string region = null)
		{
			bool ranked = false;
			var rank = await RiotAPI.GetLeagueEntriesBySummonerAsync(summoner, region).ConfigureAwait(false);
			if (rank != null)
			{
				foreach (var mType in rank)
				{
					if (mType.QueueType == QueueType.RANKED_SOLO_5x5)
					{
						ranked = true;
						if (mType.MiniSeries != null)
						{
							var promo = new List<string>();
							foreach (var prog in mType.MiniSeries.Progress)
							{
								if (prog == 'L') promo.Add("❌");
								if (prog == 'W') promo.Add("✅");
								if (prog == 'N') promo.Add("➖");
							}

							string tier = StringUtil.ConvertRank(Convert.ToString(int.Parse(StringUtil.ConvertRank($"{mType.Tier} {mType.Rank}", true)) + 1), false);
							string[] subs = tier.Split(' ', StringSplitOptions.RemoveEmptyEntries);
							var promoString = string.Join(" ", promo);

							return new LP
							{
								RANK = "ПРОМО В " + subs[0],
								LPoints = promoString
							};
						}
						else
						{
							int WR = (int)Math.Ceiling(mType.Wins * 100 / (double)(mType.Wins + mType.Losses));
							return new LP
							{
								RANK = mType.Tier + " " + mType.Rank,
								LPoints = mType.LeaguePoints.ToString()
							};
						}
					}
				}
			}
			else
			{
				return new LP
				{
					RANK = "Riot API error",
					LPoints = null
				};
			}

			if (!ranked)
			{
				return new LP
				{
					RANK = "Калибровка",
					LPoints = null
				};
			}

			return new LP
			{
				RANK = "ERROR",
				LPoints = null
			};
		}

		public static async Task ShowLPAsync(string sender)
		{
			bool ranked = false;
			var rank = await RiotAPI.GetLeagueEntriesBySummonerAsync().ConfigureAwait(false);
			if (rank != null)
			{
				foreach (var mType in rank)
				{
					if (mType.QueueType == QueueType.RANKED_SOLO_5x5)
					{
						ranked = true;
						if (mType.MiniSeries != null)
						{
							var promo = new List<string>();
							foreach (var prog in mType.MiniSeries.Progress)
							{
								if (prog == 'L') promo.Add("❌");
								if (prog == 'W') promo.Add("✅");
								if (prog == 'N') promo.Add("➖");
							}

							string tier = StringUtil.ConvertRank(Convert.ToString(int.Parse(StringUtil.ConvertRank($"{mType.Tier} {mType.Rank}", true)) + 1), false);
							string[] subs = tier.Split(' ', StringSplitOptions.RemoveEmptyEntries);
							var promoString = string.Join(" ", promo);
							await TtvIRCClient.SendMessage(string.Format(STRINGS.ShowLPPromo, sender, IllSingleton.Game.SummonerName, subs[0], promoString));
						}
						else
						{
							int WR = (int)Math.Ceiling(mType.Wins * 100 / (double)(mType.Wins + mType.Losses));
							await TtvIRCClient.SendMessage(string.Format(STRINGS.ShowLP, sender, IllSingleton.Game.SummonerName, mType.Tier, mType.Rank, mType.LeaguePoints, WR, IllSingleton.Game.NumGames, IllSingleton.Game.NumWins, IllSingleton.Game.NumLosses, IllSingleton.Game.EarnedLP));
						}
					}
				}
			}
			else
			{
				await TtvIRCClient.SendMessage("Riot API error");
			}

			if (!ranked)
			{
				await TtvIRCClient.SendMessage(string.Format(STRINGS.ShowLPCalibration, sender, IllSingleton.Game.SummonerName, IllSingleton.Game.NumGames, IllSingleton.Game.NumWins, IllSingleton.Game.NumLosses, IllSingleton.Game.EarnedLP));
			}
		}

		public static async Task LpCommand(UserObject user, string[] command)
		{
			if (command.Length > 2)
			{
				if (!IllAccess.Mod(user)) return;

				if (IllSingleton.State.InMatch)
				{
					await TtvIRCClient.SendMessage(string.Format(STRINGS.LPInaMatch, user.Name));
					return;
				}

				var region = command.Last();
				if (region != "ru" && region != "euw" && region != "na")
				{
					await TtvIRCClient.SendMessage("Ошибка ввода (не указан регион). Поддерживаемые регионы - euw, ru, na");
					return;
				}

				var result = await RiotAPI.UpdateSummonerByNameAsync(command[1], command[2], region).ConfigureAwait(false);
				if (result == null)
				{
					IllSingleton.Game.SummonerName = command[1] + "#" + command[2];
					IllSingleton.Game.SummonerRegion = region;
					RiotAPI.UpdateConfig();

					var Rank = await RiotAPI.GetRankBySummonerAsync().ConfigureAwait(false);
					if (Rank != null)
					{
						if (int.TryParse(Rank[1], out int buffStartLP))
							IllSingleton.Game.StartLP = buffStartLP;
						else
							IllSingleton.Game.StartLP = 0;
						IllSingleton.Game.Elo = Rank[0];
						IllSingleton.Game.Tier = Rank[2];
					}
					SaveGameStats();
					SaveAppConfig();
					await ShowLPAsync(user.Name).ConfigureAwait(false);
				}
				else
				{
					await TtvIRCClient.SendMessage($"ERROR: {result}");
				}
			}
			else
			{
				await ShowLPAsync(user.Name).ConfigureAwait(false);
			}
		}

		public static async Task GetMMR(UserObject user)
		{
			var result = await MyLOLMMRApi.GetMMR(IllSingleton.Game.SummonerName).ConfigureAwait(false);
			if (result == null) return;
			if (result.Count == 2)
				await TtvIRCClient.SendMessage($"@{user.Name} {result[0]}: mmr:{result[1]}");
		}

		public static async Task OpGG(UserObject user)
		{
			await TtvIRCClient.SendMessage(string.Format(STRINGS.OpGGMessage, user.Name, IllSingleton.Game.SummonerName.Replace('#', '-')));
		}

		public static async Task GetMatchHistory(UserObject user)
		{
			// kept intentionally as "in dev" per original
			await TtvIRCClient.SendMessage("Команда в разработке. Верим.");
		}

		#endregion

		#region StreamElements / Music

		public static async Task GetTreck(UserObject user)
		{
			var result = await StreamElementsAPI.GetCurrentSong().ConfigureAwait(false);
			string output;
			if (result == null)
				output = string.Format(STRINGS.GetTrack404, user.Name);
			else
			{
				var userID = TempDataReader.GetUserIDByTreckID(result.VideoId);
				UserObject uUser = new UserObject();
				if (userID != -1)
				{
					uUser = await _databaseService.GetUserAsync(userID).ConfigureAwait(false);
				}
				else
					uUser.Name = "streamelements";
				output = string.Format(STRINGS.GetTrackShow, user.Name, result.Title, result.VideoId, uUser.Name);
			}
			await TtvIRCClient.SendMessage(output);
		}

		public static async Task GetTrackQueue(UserObject user)
		{
			var result = await StreamElementsAPI.GetQueue().ConfigureAwait(false);
			if (result == null)
				await TtvIRCClient.SendMessage(string.Format(STRINGS.GetTrack404, user.Name));
			else
				await TtvIRCClient.SendMessage(string.Join(", ", result.Select(v => v.Title)));
		}

		public static async Task CreateClip(UserObject user)
		{
			var response = await TtvAPI.CreateClip().ConfigureAwait(false);
			if (response != null && response.CreatedClips != null && response.CreatedClips.Length > 0)
			{
				var clipUrl = response.CreatedClips[0].EditUrl;
				// some edit urls have trailing characters; preserve original logic of removing last 5 if it exists and is safe
				if (!string.IsNullOrEmpty(clipUrl) && clipUrl.Length > 5)
					clipUrl = clipUrl.Remove(clipUrl.Length - 5);
				await TtvIRCClient.SendMessage(string.Format(STRINGS.CreateClipSuccess, user.Name, clipUrl));
			}
			else
			{
				await TtvIRCClient.SendMessage(string.Format(STRINGS.CreateClipERROR, user.Name, "ex"));
			}
		}

		public static async Task BanUserForTrack(UserObject user)
		{
			var history = await StreamElementsAPI.GetHistory().ConfigureAwait(false);
			if (history == null) return;

			int userID = TempDataReader.GetUserIDByTreckID(history.History[0].Song.VideoId);
			MediaBlackListWriter.Write(history.History[0].Song.VideoId);
			if (userID != -1)
			{
				var uUser = await _databaseService.GetUserAsync(userID).ConfigureAwait(false);
				await TtvAPI.TimeOutUser(uUser, 3600, STRINGS.TimeOutReason_Track).ConfigureAwait(false);
				UserBlackListWriter.Write(uUser.TwitchID.ToString());
				await TtvIRCClient.SendMessage(string.Format(STRINGS.BanUserForTrack_chatMessage, user.Name, uUser.Name));
			}
			else
			{
				await TtvIRCClient.SendMessage(string.Format(STRINGS.BanUserForTrack_DonatedTrack, user.Name));
			}
		}

		public static async Task GetTreckQueue(UserObject user)
		{
			// alias to GetTrackQueue
			await GetTrackQueue(user);
		}

		#endregion

		#region Rewards (Twitch Channel Points)

		public static async Task GetAllRewards(UserObject user)
		{
			var rewards = await TtvAPI.GetAllRewards().ConfigureAwait(false);
			if (rewards == null) return;
			int rewardsCount = rewards.Data.Length;
			string rewardsTitle = string.Join(" | ", rewards.Data.Select(r => r.Title));
			string message = string.Format(STRINGS.GetAllReward_chatMessage, rewardsCount, rewardsTitle);
			await TtvIRCClient.SendMessage(message);
		}

		public static async Task EnableReward(UserObject user, string[] args)
		{
			if (args.Length == 2)
			{
				string rewardID = args[1];
				await TtvIRCClient.SendMessage($"rewardID - {rewardID}");
				var reward = await TtvAPI.GetReward(rewardID).ConfigureAwait(false);
				if (reward == null)
					await TtvIRCClient.SendMessage("Error 404 - Награда не найденa");
				else
					await TtvAPI.UpdateReward(reward.Id, reward.Title, reward.Cost, reward.Prompt, true, reward.IsUserInputRequired).ConfigureAwait(false);
			}
			else if (args.Length == 3)
			{
				string title = args[1];
				string text = args[2];
				await TtvIRCClient.SendMessage($"Title - {title}");
				var reward = await TtvAPI.GetReward(title, text).ConfigureAwait(false);
				if (reward == null)
					await TtvIRCClient.SendMessage("Error 404 - Награда не найденa");
				else
					await TtvAPI.UpdateReward(reward.Id, reward.Title, reward.Cost, reward.Prompt, true, reward.IsUserInputRequired).ConfigureAwait(false);
			}
			else
			{
				await TtvIRCClient.SendMessage("Usage: !enablereward rewardID or !enablereward \"title\" \"text\"");
			}
		}

		public static async Task DisableReward(UserObject user, string[] input)
		{
			if (input.Length == 1)
			{
				await TtvIRCClient.SendMessage("usage - !disablereward|rewardID(string) or !disablereward|Title(string)|text(string)");
				return;
			}
			if (input.Length == 2)
			{
				await TtvIRCClient.SendMessage($"rewardID - {input[1]}");
				var reward = await TtvAPI.GetReward(input[1]).ConfigureAwait(false);
				if (reward == null)
					await TtvIRCClient.SendMessage("Error 404 - Награда не найденa");
				else
					await TtvAPI.UpdateReward(reward.Id, reward.Title, reward.Cost, reward.Prompt, false, reward.IsUserInputRequired).ConfigureAwait(false);
			}
		}

		public static async Task CreateReward(UserObject user, string[] args)
		{
			if (args.Length == 6)
			{
				string title = args[1];
				string costStr = args[2];
				string prompt = args[3];
				string enabledStr = args[4];
				string userinputStr = args[5];

				if (int.TryParse(costStr, out int cost) &&
					bool.TryParse(enabledStr, out bool enabled) &&
					bool.TryParse(userinputStr, out bool userinput))
				{
					await TtvIRCClient.SendMessage($"title - {title}, cost - {cost}, prompt - {prompt}, enabled - {enabled}, userinput - {userinput}");
					var response = await TtvAPI.CreateReward(title, cost, prompt, enabled, userinput).ConfigureAwait(false);
					if (response != null)
						await TtvIRCClient.SendMessage(response);
				}
				else
				{
					if (!int.TryParse(costStr, out _))
						await TtvIRCClient.SendMessage("Cost must be an integer.");
					if (!bool.TryParse(enabledStr, out _))
						await TtvIRCClient.SendMessage("Enabled must be a boolean (true or false).");
					if (!bool.TryParse(userinputStr, out _))
						await TtvIRCClient.SendMessage("Userinput must be a boolean (true or false).");
				}
			}
			else
			{
				await TtvIRCClient.SendMessage("Usage: !createreward \"title\" cost \"prompt\" enabled userinput");
			}
		}

		public static async Task UpdateReward(UserObject user, string[] args)
		{
			// Check if the total length is 7 (command name + 6 arguments)
			if (args.Length == 7)
			{
				string rewardID = args[1];
				string title = args[2];
				string costStr = args[3];
				string promt = args[4];
				string enabledStr = args[5];
				string userinputStr = args[6];

				if (int.TryParse(costStr, out int cost) &&
					bool.TryParse(enabledStr, out bool enabled) &&
					bool.TryParse(userinputStr, out bool userinput))
				{
					await TtvIRCClient.SendMessage($"rewardID - {rewardID}, title - {title}, cost - {cost}, promt - {promt}, enabled - {enabled}, userinput - {userinput}");
					await TtvAPI.UpdateReward(rewardID, title, cost, promt, enabled, userinput).ConfigureAwait(false);
				}
				else
				{
					await TtvIRCClient.SendMessage("Invalid parameters. Ensure cost is an integer and enabled/userinput are booleans.");
				}
			}
			else
			{
				await TtvIRCClient.SendMessage("Usage: !updatereward rewardID \"title\" cost \"promt\" enabled userinput");
			}
		}

		public static async Task DeleteReward(UserObject user, string[] input)
		{
			// kept blank intentionally (original was commented out)
		}

		#endregion

		#region Moderation & Blacklist / Whitelist

		public static async Task<UserObject> IllFilterTrigger(UserObject user, string messageID = null)
		{
			if (user == null) return user;

			if (user.banCount == 35)
			{
				await TtvAPI.BanUser(user.TwitchID.ToString(), STRINGS.PermaBanReason);
				user.banCount = 0;
				return user;
			}

			switch (IllSingleton.State.ChatFilterLvl)
			{
				case 0:
					break;
				case 1:
					if (messageID != null)
						await TtvAPI.DeleteMessage(messageID).ConfigureAwait(false);
					break;
				case 2:
					if (messageID != null)
						await TtvAPI.DeleteMessage(messageID).ConfigureAwait(false);
					string ModsZapMsg = $"Найдена запретка на канале {IllSingleton.Config.ChannelName} от пользователя @{user.Name}. Модерам на проверку";
					await IllModeratorsInteractions.IllAllModsNotification(ModsZapMsg).ConfigureAwait(false);
					break;
				case 3:
					await TtvAPI.TimeOutUser(user, 86400, STRINGS.TimeOut1wReason).ConfigureAwait(false);
					user.banCount++;
					break;
				case 4:
					await TtvAPI.TimeOutUser(user, 604800, STRINGS.TimeOut1wReason).ConfigureAwait(false);
					user.banCount++;
					break;
				case 5:
					await TtvAPI.BanUser(user.TwitchID.ToString(), STRINGS.PermaBanReason);
					user.banCount = 0;
					break;
				default:
					break;
			}
			return user;
		}

		public static async Task RemoveUserFromBlacklist(UserObject user, string[] input)
		{
			if (input.Length != 2)
			{
				await TtvIRCClient.SendMessage(STRINGS.InputERROR);
				return;
			}

			var UserToUnban = await _databaseService.GetUserAsync(input[1]).ConfigureAwait(false);
			if (UserToUnban.dbID == -404)
			{
				await TtvIRCClient.SendMessage(STRINGS.FindUser_ERROR404);
				return;
			}

			var path = IllSkillzBotMain.GetDataPath().uniquePath;
			path = Path.Combine(path, IllSingleton.Config.FilePaths.UserBlacklistFileName);
			if (FileManipulator.DeleteLineFromFile(path, UserToUnban.TwitchID.ToString()))
			{
				IllChatFilters.EditUserBlackList(UserToUnban.TwitchID.ToString());
				await TtvIRCClient.SendMessage($"Пользователь {UserToUnban.Name} удален из черного списка");
			}
			else
			{
				await TtvIRCClient.SendMessage($"Пользователь {UserToUnban.Name} не был найден в черном списке");
			}
		}

		public static async Task AddTowhiteList(UserObject user, string[] input)
		{
			if (input.Length != 2)
			{
				await TtvIRCClient.SendMessage(STRINGS.InputERROR);
				return;
			}

			var path = IllSkillzBotMain.GetDataPath().sharedPath;
			path = Path.Combine(path, IllSingleton.Config.FilePaths.DicWhiteListFileName);
			FileManipulator.AddLineToFile(path, input[1]);
			IllChatFilters.AddToWhiteList(input[1]);
		}

		public static async Task AddVIP(UserObject user, string[] UserInput)
		{
			if (UserInput.Length == 2)
			{
				var aUser = await _databaseService.GetUserAsync(UserInput[1]).ConfigureAwait(false);
				if (aUser.dbID != -404)
				{
					await TtvAPI.AddChannelVIP(aUser.TwitchID.ToString()).ConfigureAwait(false);
					await TtvIRCClient.SendMessage(string.Format(STRINGS.AddVIPSuccess, aUser.Name));
				}
				else
					await TtvIRCClient.SendMessage(string.Format(STRINGS.FindUser_ERROR404, user.Name, UserInput[1]));
			}
			else
				await TtvIRCClient.SendMessage(STRINGS.InputERROR);
		}

		public static async Task DeleteVIP(UserObject user, string[] UserInput)
		{
			if (UserInput.Length == 2)
			{
				var aUser = await _databaseService.GetUserAsync(UserInput[1]).ConfigureAwait(false);
				if (aUser.dbID != -404)
				{
					await TtvAPI.DeleteChannelVIP(aUser.TwitchID.ToString()).ConfigureAwait(false);
					await TtvIRCClient.SendMessage(string.Format(STRINGS.DeleteVIPSuccess, aUser.Name));
				}
				else
					await TtvIRCClient.SendMessage(string.Format(STRINGS.FindUser_ERROR404, user.Name, UserInput[1]));
			}
			else
				await TtvIRCClient.SendMessage(STRINGS.InputERROR);
		}

		public static async Task GetMods(UserObject user)
		{
			var mods = await TtvAPI.GetAllMods().ConfigureAwait(false);
			if (mods == null) return;
			string Moderators = string.Join(" ", mods.Select(m => $"{m.UserLogin}: {m.UserId}."));
			await TtvIRCClient.SendMessage(Moderators);
		}

		public static async Task Sheptun(UserObject user)
		{
			var mods = await TtvAPI.GetAllMods().ConfigureAwait(false);
			if (mods == null) return;
			foreach (var mod in mods)
			{
				await TtvAPI.SendWhisper(mod.UserId, "Тестовый шептун").ConfigureAwait(false);
				await Task.Delay(10).ConfigureAwait(false);
			}
		}

		#endregion

		#region Quizzes / Games

		public static async Task StartQuizz()
		{
			await IllGames.Quizz(true).ConfigureAwait(false);
		}

		public static async Task<UserObject> QuizzMediaReward(UserObject user, string[] UserInput)
		{
			if (user == null) return user;
			if (user.isMod == 1) return user;

			if (user.QuizPoints > 1)
			{
				if (UserInput.Length < 2)
				{
					await TtvIRCClient.SendMessage(STRINGS.InputERROR);
					return user;
				}
				if (await RewardsRedemption.ZakazTrekaReward(user.Name, string.Join(" ", UserInput.Skip(1)), null, null).ConfigureAwait(false))
				{
					user.QuizPoints -= 2;
				}
			}
			return user;
		}

		#endregion

		#region Subscriptions & Misc

		public static async Task AddSubscription(UserObject user)
		{
			await TtvIRCClient.SendMessage(AddSub.NewPurchase().ToString()).ConfigureAwait(false);
			SubCheck.RunChecker();
		}

		public static async Task CheckSubscription(UserObject user)
		{
			if (SubCheck.RunChecker())
				await TtvIRCClient.SendMessage("Valid!");
			else
				await TtvIRCClient.SendMessage("Expired!");
		}

		public static async Task GetTtvgg(UserObject user)
		{
			long currentUnixTime = DateTimeOffset.Now.ToUnixTimeSeconds();
			var taskList = new List<Task<int[]>>
			{
				_databaseService.GetUserPositionAsync(user.Name, "roulettCon"),
				_databaseService.GetUserPositionAsync(user.Name, "UvalCon"),
				_databaseService.GetUserPositionAsync(user.Name, "messageCon"),
				_databaseService.GetUserPositionAsync(user.Name, "Points"),
				_databaseService.GetUserPositionAsync(user.Name, "QuizPoints"),
				_databaseService.GetUserPositionAsync(user.Name, "QuizTotal")
			};
			var results = await Task.WhenAll(taskList).ConfigureAwait(false);
			var roulettCD = user.roulettCD - currentUnixTime;
			roulettCD = roulettCD < 0 ? 0 : roulettCD;
			TimeSpan time = TimeSpan.FromSeconds(roulettCD);

			await TtvIRCClient.SendMessage($"@{user.Name}, твой винстрик в рулетке {user.roulettCon} {IntUtil.CalculateTopPercentage(results[0])}, " +
				$"всего ты отправил {user.messageCon} сообщений {IntUtil.CalculateTopPercentage(results[2])}, " +
				$"ты был в увале {user.UvalCon} раз {IntUtil.CalculateTopPercentage(results[1])}, " +
				$"у тебя есть {user.QuizPoints} баллов квиза {IntUtil.CalculateTopPercentage(results[4])}, " +
				$"за все время ты набрал {user.QuizTotal} баллов квиза {IntUtil.CalculateTopPercentage(results[5])}, " +
				$"у тебя есть {user.Points} поинтов {IntUtil.CalculateTopPercentage(results[3])}, " +
				$"кулдаун у твоей рулетки продлится еще {time:hh\\:mm\\:ss}");
		}

		#endregion

		#region Cron / Background Tasks

		public static async Task StartCronTask(UserObject user, string input)
		{
			QuartzBackgroundTaskManager quartzBackgroundTaskManager = new QuartzBackgroundTaskManager();
			var inputParts = input.Split(' ');
			if (inputParts.Length <= 4)
			{
				await TtvIRCClient.SendMessage(STRINGS.StartCronTaskERROR);
			}
			else
			{
				var taskName = inputParts[1];
				var triggerName = inputParts[2];
				var cronExpression = string.Join(" ", inputParts.Skip(3));
				if (!quartzBackgroundTaskManager.IsCronExpressionValid(cronExpression))
				{
					await TtvIRCClient.SendMessage(STRINGS.StartCronTaskERROR2);
				}
				else
				{
					await quartzBackgroundTaskManager.UpdateJobSchedule(taskName, triggerName, cronExpression);
					await TtvIRCClient.SendMessage(STRINGS.StartCronTaskSuccess);
				}
			}
		}

		public static async Task GetAllJobs(UserObject user)
		{
			QuartzBackgroundTaskManager quartzBackgroundTaskManager = new QuartzBackgroundTaskManager();
			var jobs = await quartzBackgroundTaskManager.GetAllJobsNames().ConfigureAwait(false);
			await TtvIRCClient.SendMessage(jobs);
		}

		public static async Task getJobs(UserObject user)
		{
			await TtvIRCClient.SendMessage(await QuartzBackgroundTaskManager.GetRunningJobs().ConfigureAwait(false));
		}

		#endregion

		#region Admin / Unsafe removed

		// The original "InjectSQL" raw SQL execution block was removed for security reasons.
		// If you need admin SQL functionality, implement a safe, parameterized admin tool explicitly.

		#endregion

		#region Save Helpers

		public static void SaveGameStats()
		{
			GameStatsWriter.Write(
				$"{IllSingleton.Game.StartLP} " +
				$"{IllSingleton.Game.Elo} " +
				$"{IllSingleton.Game.EarnedLP} " +
				$"{IllSingleton.Game.NumLosses} " +
				$"{IllSingleton.Game.NumGames} " +
				$"{IllSingleton.Game.NumWins} " +
				$"{IllSingleton.Game.Tier}"
			);
		}

		public static void SaveAppConfig() => BotConfigWriter.Write();

		#endregion
	}
}

*/