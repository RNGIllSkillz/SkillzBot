using System;
using System.Net.Sockets;
using System.Net;
using System.Text.RegularExpressions;
using System.Linq;
using System.Threading.Tasks;
using SkillzBot.WRITERS;

namespace SkillzBot.Utils
{
    internal class NetUtil
    {
        private const string LinkPattern = @"((http|https):\/\/|(www\.)?)?[\w\-]+(\.[a-zA-Z]{2,})([\w.,@?^=%&:/~+#-]*[\w@?^=%&/~+#-])?";        
        public static bool IsValidLink(string input)
        {
            Regex regex = new Regex(LinkPattern);
            MatchCollection matches = regex.Matches(input);
            foreach (Match match in matches.Cast<Match>())
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
                string host = uri.Host;
                try
                {
                    Task<IPAddress[]> task = Dns.GetHostAddressesAsync(host);
                    if (task.Wait(TimeSpan.FromSeconds(1)))
                    {
                        IPAddress[] addresses = task.Result;
                        return addresses.Length > 0;
                    }
                    else
                        return false;                    
                }
                catch (AggregateException ex)
                {
                    Exception actualException = ex.InnerException;
                    if (actualException is SocketException)                    
                        return false;                    
                    Log.WriteLog(ex, "IsUrlValid(1)");
                }
            }
            else if (Uri.IsWellFormedUriString(url, UriKind.RelativeOrAbsolute))
            {
                string host = url;
                try
                {
                    Task<IPAddress[]> task = Dns.GetHostAddressesAsync(host);
                    if (task.Wait(TimeSpan.FromSeconds(1)))
                    {
                        IPAddress[] addresses = task.Result;
                        return addresses.Length > 0;
                    }
                    else
                        return false;
                }
                catch (AggregateException ex)
                {
                    Exception actualException = ex.InnerException;
                    if (actualException is SocketException)
                        return false;
                    Log.WriteLog(ex, "IsUrlValid(2)");
                }
            }
            return false;
        }
    }
}
