// Supervisor version of the GigFilesChecker launcher used on TrueNAS.
// Differences from the original: the bot is restarted whenever it exits (the web panel's
// "restart" simply stops the bot process), files are re-synced from the host before every
// start so a new build dropped on the host is picked up by a restart, and a container stop
// (SIGTERM) is forwarded to the bot so it can save its state.
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

class Program
{
    static Process _bot;
    static volatile bool _stopping;

    static int Main()
    {
        string sourceDirectory = Environment.GetEnvironmentVariable("ENV_PATH_TO_HOST");
        string destinationDirectory = Environment.GetEnvironmentVariable("ENV_PATH_TO_VOL");
        string mainApp = Environment.GetEnvironmentVariable("ENV_APP_EXE_NAME");
        string appPath = Path.Combine(destinationDirectory, mainApp);

        AppDomain.CurrentDomain.ProcessExit += (_, _) => StopBot();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; StopBot(); };

        int quickExits = 0;
        while (!_stopping)
        {
            Console.WriteLine("Checking for files presence");
            DirectoryComparer.CopyMissingFiles(sourceDirectory, destinationDirectory);
            Console.WriteLine("Checking files integrity");
            HashChecker.CheckAndCopyFiles(sourceDirectory, destinationDirectory);

            var started = DateTime.UtcNow;
            Console.WriteLine($"Starting {appPath}");
            _bot = Process.Start(new ProcessStartInfo(appPath) { UseShellExecute = false, WorkingDirectory = destinationDirectory });
            _bot.WaitForExit();
            int code = _bot.ExitCode;
            _bot = null;
            if (_stopping) break;

            // Crash loop protection: after 5 exits within 30 s each, wait a minute before trying again.
            quickExits = (DateTime.UtcNow - started) < TimeSpan.FromSeconds(30) ? quickExits + 1 : 0;
            int delay = quickExits >= 5 ? 60 : 3;
            Console.WriteLine($"Bot exited with code {code}; restarting in {delay}s");
            Thread.Sleep(TimeSpan.FromSeconds(delay));
        }
        return 0;
    }

    static void StopBot()
    {
        _stopping = true;
        var bot = _bot;
        if (bot == null || bot.HasExited) return;
        try
        {
            // SIGTERM lets the bot run its graceful shutdown; kill if it ignores it.
            Process.Start("kill", $"-TERM {bot.Id}")?.WaitForExit();
            if (!bot.WaitForExit(15000)) bot.Kill(true);
        }
        catch { }
    }
}
