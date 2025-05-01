using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore;
using Microsoft.Extensions.Hosting;
using SkillzBot.EventSub;
using Microsoft.Extensions.DependencyInjection;
using TwitchLib.EventSub.Websockets.Extensions;


namespace SkillzBot.Hosts
{
    internal class IHostBuilders
    {      
        private static IHostBuilder TTVEventSubHostBuilder() =>
           Host.CreateDefaultBuilder()
               .ConfigureServices((hostContext, services) =>
               {
                   services.AddTwitchLibEventSubWebsockets();
                   services.AddHostedService<TTVEventSub>();
               });

        public IHost EventSubHos()
        {
            return TTVEventSubHostBuilder().Build();
        }        
    }
    internal class IWebHostBuilders
    {
        private static IWebHostBuilder ILLApiHostBuilder() =>
        WebHost.CreateDefaultBuilder()
            .UseStartup<Startup>();
        public IWebHost ILLAPIHost()
        {
            return ILLApiHostBuilder().Build();
        }
    }
}
