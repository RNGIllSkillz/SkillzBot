using System;
using System.Net.Sockets;
using System.Net;
using System.Text.RegularExpressions;
using System.Linq;
using System.Threading.Tasks;
using SkillzBot.WRITERS;
using Microsoft.Extensions.Logging;
using SkillzBot.Hosts;
using SkillzBot.Readers;

namespace SkillzBot.Utils
{
    internal class NetUtil
    {
        private static readonly ILogger<NetUtil> _logger = IllServiceProvider.GetLogger<NetUtil>();
        private const string LinkPattern = @"((http|https):\/\/|(www\.)?)?[\w\-]+(\.[a-zA-Z]{2,})([\w.,@?^=%&:/~+#-]*[\w@?^=%&/~+#-])?";
        public static bool IsValidLink(string input)
        {
            var matches = Regex.Matches(input, LinkPattern);
            foreach (var match in matches.Cast<Match>())
            {
                string url = match.Value;
                if (IsUrlValid(url))                
                    return true;                
            }
            return false;
        }

        private static bool IsUrlValid(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                try
                {
                    IPAddress[] addresses = Dns.GetHostAddresses(uri.Host);
                    return addresses.Length > 0;
                }
                catch (SocketException)
                {
                    return false;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "IsUrlValid(1)");
                }
            }
            else if (Uri.IsWellFormedUriString(url, UriKind.RelativeOrAbsolute))
            {
                try
                {
                    IPAddress[] addresses = Dns.GetHostAddresses(url);
                    return addresses.Length > 0;
                }
                catch (SocketException)
                {
                    return false;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "IsUrlValid(2)");
                }
            }
            return false;
        }
    }
}
