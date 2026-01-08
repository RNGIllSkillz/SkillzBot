using SkillzBot.IRC;
using SkillzBot.MODELS;
using System;
using SkillzBot.Singleton;
using System.Threading.Tasks;
using SkillzBot.Hosts;
using SkillzBot.Interfaces;

namespace SkillzBot.SubUtils
{
    internal class SubCall
    {
        private readonly ITtvIRCClient _ircClient;

        public SubCall()
        {
            // Note: Since this is called from a Controller, we might need to resolve this from ServiceProvider
            // if SubCall isn't registered in DI. Using ServiceProvider for compatibility with Controller instantiation.
            _ircClient = IllServiceProvider.GetService<ITtvIRCClient>();
        }

        public async Task PostDataProcess(apiPost model)
        {
            if (string.IsNullOrEmpty(model?.value)) return;

            string[] words = model.value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (words.Length < 9)
            {
                Console.WriteLine($"Invalid Post Data: {model.value}");
                return;
            }

            int amount;
            string sender = words[7] + " " + words[8];
            int rate;
            if (int.TryParse(words[5], out amount))
            {
                Console.WriteLine($"Sender: {sender}");
                Console.WriteLine($"Amount: {amount} RUB");
                if ((sender.Contains("Владислав", StringComparison.OrdinalIgnoreCase)))
                {
                    rate = 10000;
                    await PurchaseProcess(amount, rate).ConfigureAwait(false);
                }
                if ((sender.Contains("Людмила", StringComparison.OrdinalIgnoreCase)))
                {
                    rate = 500;
                    await PurchaseProcess(amount, rate).ConfigureAwait(false);
                }
            }
            else
            {
                Console.WriteLine($"Cant unmarshal type int data = {words[5]}");
            }
        }
        private async Task PurchaseProcess(int amount, int rate)
        {
            var responce = AddSub.NewPurchase(amount, rate);
            SubCheck.RunChecker();
            await _ircClient.SendMessage($"@{IllSingleton.Config.ChannelName} {responce}").ConfigureAwait(false);
        }
    }
}