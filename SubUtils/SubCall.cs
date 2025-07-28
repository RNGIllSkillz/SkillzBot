using SkillzBot.IRC;
using SkillzBot.MODELS;
using System;
using SkillzBot.Singleton;

namespace SkillzBot.SubUtils
{
    internal class SubCall
    {
        public void PostDataProcess(apiPost model)
        {
            string[] words = model.value.Split(' ');
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
                    PurchaseProcess(amount, rate);
                }
                if ((sender.Contains("Людмила", StringComparison.OrdinalIgnoreCase)))
                {
                    rate = 500;
                    PurchaseProcess(amount, rate);
                }
            }  
            else
            {
                Console.WriteLine($"Cant unmarshal type int data = {words[5]}");
            }
        }
        private void PurchaseProcess (int amount, int rate)
        {
            var responce = AddSub.NewPurchase(amount, rate);
            SubCheck.RunChecker();
            TtvIRCClient.SendMessage($"@{IllSingleton.Config.ChannelName} {responce}");
        }
    }
}
