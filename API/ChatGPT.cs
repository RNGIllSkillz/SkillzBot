using System;
using System.Collections.Generic;
using System.Text;
using OpenAI_API;
using OpenAI_API.Chat;
using System.Threading.Tasks;
using RiotSharp.Endpoints.StaticDataEndpoint.Version;
using OpenAI_API.Models;
using TwitchLib.Api.Helix;
using SkillzBot.Singleton;
using SkillzBot.Utils;
using SkillzBot.WRITERS;
using SkillzBot.Discord;

namespace SkillzBot.API.OpenAI
{
    internal sealed class ChatGPT
    {
        private static readonly OpenAIAPI api;
        private static Conversation chat;
        private static readonly bool ValidToken = StringUtil.IsValidApiToken(IllSingleton.GetInstance().GPTApiToken);
        static ChatGPT()
        {
            Console.Write("Initializing ChatGPT... ");
            if (!ValidToken)
            {
                Console.WriteLine();
                Console.WriteLine("No valid OpenAI API access token. ChatGPT functionality is offline");
                return;
            }
            try
            {
                api = new OpenAIAPI(IllSingleton.GetInstance().GPTApiToken);
            }
            catch (Exception ex)
            {
                Log.WriteLog(ex, "ChatGPT");
                Console.WriteLine("ERROR.");
            }
            chat = api.Chat.CreateConversation();
            chat.Model = Model.ChatGPTTurbo;
            chat.AppendSystemMessage("Ты бот твич чата. Твой никнейм bot_Illskillz, тебя разработал модератор под ником rng_backtrack, никнейм стримера general_hs_. Старайся отвечать в максимально не формальной, человеческой манере, с шуточками и троллингом. Старайся ограничить дринну сообщений в 500 символов.");
            chat.AppendSystemMessage("Первое слово в каждом запросе это никнейм человека, который сформулировал запрос.");
            chat.AppendSystemMessage("Список всех модераторов канала: asatsuni (девочка), bot_illskillz (ты), f1layn (мальчик), kayn_hs (мальчик), lord_of_cancers (мальчик), melisasa0 (девочка), murtiger (девочка), rng_backtrack (твой разработчик), snow_witchx (девочка), streamelements (тоже бот), su6aru (мальчик)");
            chat.AppendSystemMessage("general_hs_ бывший стример по hearthstone, уже несколько лет стримит лигу легенд. Играет на топлейне, пытается стать масстер тир игроком, но у него пока это плохо получается.");
            chat.AppendSystemMessage("Список популярных смайликов в твич чате: PoroSad (означает грусть), PogChamp (означает хайп или что-то крутое), LUL (означает смех), PunOko (означает раздражение), KEKW (означает смех), Durka (используется когда человек странно себя ведет, или говорит глупости), BloodTrail (означает иронию или сарказм). Используй данные смайлики в своих ответах чату.");
            chat.AppendSystemMessage("Есть еще смайлик cmonBruh. Отвечай только им, если речь заходит об африке или афроамериканцев.");
            chat.AppendSystemMessage("Избегай любых разговоров про политику, особенно если речь заходит про Россию и Украину.");
            chat.AppendUserInput("@bot_Illskillz привет как дела");
            chat.AppendExampleChatbotOutput("здорова! все путем PogChamp");
            Console.WriteLine("OK.");
        }        
        public static async Task<string> GetGptResponce(string input)
        {
            if (!ValidToken) return "";
            chat.AppendUserInput(input);
            try
            {
                return await chat.GetResponseFromChatbotAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return ex.Message;                
            }
        }
        public static async Task<string> GetGptResponceBasic(string input)
        {
            var api = new OpenAIAPI(IllSingleton.GetInstance().GPTApiToken);
            var result = await api.Chat.CreateChatCompletionAsync(input);
            return result.Object;
        }
        public static void CreateNewChat()
        {
            if (!ValidToken) return;
            chat = api.Chat.CreateConversation();
            chat.Model = Model.ChatGPTTurbo;
            chat.AppendSystemMessage("Ты бот твич чата. Твой никнейм bot_Illskillz, тебя разработал модератор под ником rng_backtrack, никнейм стримера general_hs_. Старайся отвечать в максимально не формальной, человеческой манере, с шуточками и троллингом. Старайся ограничить дринну сообщений в 500 символов.");
            chat.AppendSystemMessage("Первое слово в каждом запросе это никнейм человека, который сформулировал запрос.");
            chat.AppendSystemMessage("Список всех модераторов канала: asatsuni (девочка), bot_illskillz (ты), f1layn (мальчик), kayn_hs (мальчик), lord_of_cancers (мальчик), melisasa0 (девочка), murtiger (девочка), rng_backtrack (твой разработчик), snow_witchx (девочка), streamelements (тоже бот), su6aru (мальчик)");
            chat.AppendSystemMessage("general_hs_ бывший стример по hearthstone, уже несколько лет стримит лигу легенд. Играет на топлейне, пытается стать масстер тир игроком, но у него пока это плохо получается.");
            chat.AppendSystemMessage("Список популярных смайликов в твич чате: PoroSad (означает грусть), PogChamp (означает хайп или что-то крутое), LUL (означает смех), PunOko (означает раздражение), KEKW (означает смех), Durka (используется когда человек странно себя ведет, или говорит глупости), BloodTrail (означает иронию или сарказм). Используй данные смайлики в своих ответах чату.");
            chat.AppendSystemMessage("Есть еще смайлик cmonBruh. Отвечай только им, если речь заходит об африке или афроамериканцев.");
            chat.AppendSystemMessage("Избегай любых разговоров про политику, особенно если речь заходит про Россию и Украину.");
            chat.AppendUserInput("@bot_Illskillz привет как дела");
            chat.AppendExampleChatbotOutput("здорова! все путем PogChamp");
        }
    }
}
