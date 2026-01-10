using Microsoft.Extensions.Logging; 
using SkillzBot.Interfaces;
using SkillzBot.MODELS;
using SkillzBot.Services.Infrastructure;
using SkillzBot.IllConfiguration; 
using System;
using System.Threading.Tasks;

namespace SkillzBot.SubUtils
{
    internal class SubCall
    {
        private readonly ITtvIRCClient _ircClient;
        private readonly BotConfigModel _config;
        private readonly SubscriptionService _subscriptionService;
        private readonly ILogger<SubCall> _logger; 

        public SubCall(
            ITtvIRCClient ircClient,
            BotConfigModel config,
            SubscriptionService subscriptionService,
            ILogger<SubCall> logger)
        {
            _ircClient = ircClient;
            _config = config;
            _subscriptionService = subscriptionService;
            _logger = logger;
        }

        public async Task PostDataProcess(apiPost model)
        {
            if (string.IsNullOrEmpty(model?.value)) return;

            string[] words = model.value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (words.Length < 9)
            {
                _logger.LogWarning("Invalid Post Data: {Value}", model.value);
                return;
            }

            if (int.TryParse(words[5], out int amount))
            {
                // Reconstruct sender name from specific array positions (based on original logic)
                string sender = words[7] + " " + words[8];

                _logger.LogInformation("SubCall Sender: {Sender}, Amount: {Amount} RUB", sender, amount);

                int rate;
                if ((sender.Contains("Владислав", StringComparison.OrdinalIgnoreCase)))
                {
                    rate = 10000;
                    await PurchaseProcess(amount, rate);
                }
                else if ((sender.Contains("Людмила", StringComparison.OrdinalIgnoreCase)))
                {
                    rate = 500;
                    await PurchaseProcess(amount, rate);
                }
            }
            else
            {
                _logger.LogError("Cant unmarshal type int data = {Word}", words[5]);
            }
        }

        private async Task PurchaseProcess(int amount, int rate)
        {
            // Call the service method we created in Step 1
            var response = await _subscriptionService.AddSubscriptionAsync(amount, rate);

            // CheckSubscriptionAsync is redundant because AddSubscriptionAsync calls it internally,
            // but keeping it doesn't hurt.

            await _ircClient.SendMessage($"@{_config.ChannelName} {response}");
        }
    }
}