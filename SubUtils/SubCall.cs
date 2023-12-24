using SkillzBot.IRC;
using SkillzBot.MODELS;
using System;
using System.Collections.Generic;
using System.Text;

namespace SkillzBot.SubUtils
{
    internal class SubCall
    {
        public void PostDataProcess(apiPost model)
        {
            string[] words = model.value.Split(' ');
            int amount;
            string sender = words[7] + " " + words[8] + "."; 

            if (int.TryParse(words[5], out amount))
            {
                Console.WriteLine($"Amount: {amount} RUB");
                if (sender.Contains("Владислав"))
                {
                    var responce = AddSub.NewPurchase(amount);
                    TtvIRCClient.SendMessage($"@general_hs_ {responce}");
                }
            }            
        }
    }
}
