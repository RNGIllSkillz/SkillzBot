using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SkillzBot.Writers
{
    public class SkillzLoggerOptions
    {
        public string LogFilePath { get; set; } = string.Empty;
        public bool WriteToFile { get; set; } = true;
        public bool WriteToConsole { get; set; } = true;
        public bool IncludeTimestamp { get; set; } = true;
        public bool AddEmptyLines { get; set; } = true;

        public string TraceSeparator { get; set; } = "···············································································";
        public string DebugSeparator { get; set; } = "───────────────────────────────────────────────────────────────────────────────────";
        public string InfoSeparator { get; set; } = "═══════════════════════════════════════════════════════════════════════════════════";
        public string WarningSeparator { get; set; } = "▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲▲";
        public string ErrorSeparator { get; set; } = "████████████████████████████████████████████████████████████████████████████████████";
        public string CriticalSeparator { get; set; } = "🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥🔥";

        public string DefaultSeparator { get; set; } = "═══════════════════════════════════════════════════════════════════════════════════";

        public string GetSeparatorForLevel(LogLevel level)
        {
            return level switch
            {
                LogLevel.Trace => TraceSeparator,
                LogLevel.Debug => DebugSeparator,
                LogLevel.Information => InfoSeparator,
                LogLevel.Warning => WarningSeparator,
                LogLevel.Error => ErrorSeparator,
                LogLevel.Critical => CriticalSeparator,
                _ => DefaultSeparator
            };
        }
    }

    public record LogEntry(
        DateTimeOffset Timestamp,
        LogLevel Level,
        string Message,
        Exception? Exception,
        string CategoryName
    );

    public class SkillzLoggerProvider : ILoggerProvider
    {
        private readonly SkillzLoggerOptions _options;
        private readonly SkillzLoggerProcessor _processor;

        public SkillzLoggerProvider(IOptions<SkillzLoggerOptions> options, SkillzLoggerProcessor processor)
        {
            _options = options.Value;
            _processor = processor;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new SkillzLogger(categoryName, _options, _processor);
        }

        public void Dispose()
        {
            _processor?.Dispose();
        }
    }

    public class SkillzLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly SkillzLoggerOptions _options;
        private readonly SkillzLoggerProcessor _processor;

        public SkillzLogger(string categoryName, SkillzLoggerOptions options, SkillzLoggerProcessor processor)
        {
            _categoryName = categoryName;
            _options = options;
            _processor = processor;
        }

        public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception);
            var logEntry = new LogEntry(DateTimeOffset.Now, logLevel, message, exception, _categoryName);

            _processor.EnqueueLogEntry(logEntry);
        }

        private class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();
            public void Dispose() { }
        }
    }

    public class SkillzLoggerProcessor : BackgroundService, IDisposable
    {
        private readonly Channel<LogEntry> _queue;
        private readonly ChannelWriter<LogEntry> _writer;
        private readonly ChannelReader<LogEntry> _reader;
        private readonly SkillzLoggerOptions _options;
        private readonly SemaphoreSlim _fileSemaphore = new(1, 1);

        public SkillzLoggerProcessor(IOptions<SkillzLoggerOptions> options)
        {
            _options = options.Value;

            var channelOptions = new BoundedChannelOptions(1000)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            };

            _queue = Channel.CreateBounded<LogEntry>(channelOptions);
            _writer = _queue.Writer;
            _reader = _queue.Reader;

            EnsureLogFileExists();
        }

        public void EnqueueLogEntry(LogEntry logEntry)
        {
            if (!_writer.TryWrite(logEntry))
            {
                WriteToConsole(logEntry);
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var logEntry in _reader.ReadAllAsync(stoppingToken))
            {
                await ProcessLogEntry(logEntry);
            }
        }

        private async Task ProcessLogEntry(LogEntry logEntry)
        {
            if (_options.WriteToConsole)
            {
                WriteToConsole(logEntry);
            }

            if (_options.WriteToFile && !string.IsNullOrEmpty(_options.LogFilePath))
            {
                await WriteToFileAsync(logEntry);
            }
        }

        private void WriteToConsole(LogEntry logEntry)
        {
            if (_options.AddEmptyLines)
            {
                Console.WriteLine();
            }

            if (_options.IncludeTimestamp)
            {
                SetConsoleColorForLevel(logEntry.Level);
                Console.WriteLine(logEntry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
                Console.ResetColor();
            }

            SetConsoleColorForLevel(logEntry.Level);
            Console.WriteLine($"[{logEntry.Level}] {logEntry.CategoryName}: {logEntry.Message}");
            Console.ResetColor();

            if (logEntry.Exception != null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Exception: {logEntry.Exception.Message}");
                Console.WriteLine($"StackTrace: {logEntry.Exception.StackTrace}");

                if (logEntry.Exception.InnerException != null)
                {
                    Console.WriteLine($"Inner Exception: {logEntry.Exception.InnerException.Message}");
                    Console.WriteLine($"Inner StackTrace: {logEntry.Exception.InnerException.StackTrace}");
                }
                Console.ResetColor();
            }

            SetConsoleColorForLevel(logEntry.Level);
            Console.WriteLine(_options.GetSeparatorForLevel(logEntry.Level));
            Console.ResetColor();
        }

        private async Task WriteToFileAsync(LogEntry logEntry)
        {
            await _fileSemaphore.WaitAsync();
            try
            {
                await using var fileStream = new FileStream(_options.LogFilePath, FileMode.Append, FileAccess.Write, FileShare.Read, bufferSize: 4096, useAsync: true);
                await using var writer = new StreamWriter(fileStream);

                if (_options.AddEmptyLines)
                {
                    await writer.WriteLineAsync();
                }

                if (_options.IncludeTimestamp)
                {
                    await writer.WriteLineAsync(logEntry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
                }

                await writer.WriteLineAsync($"[{logEntry.Level}] {logEntry.CategoryName}: {logEntry.Message}");

                if (logEntry.Exception != null)
                {
                    await writer.WriteLineAsync($"Exception: {logEntry.Exception.Message}");
                    await writer.WriteLineAsync($"StackTrace: {logEntry.Exception.StackTrace}");

                    if (logEntry.Exception.InnerException != null)
                    {
                        await writer.WriteLineAsync($"Inner Exception: {logEntry.Exception.InnerException.Message}");
                        await writer.WriteLineAsync($"Inner StackTrace: {logEntry.Exception.InnerException.StackTrace}");
                    }
                }

                await writer.WriteLineAsync(_options.GetSeparatorForLevel(logEntry.Level));
            }
            finally
            {
                _fileSemaphore.Release();
            }
        }

        private static void SetConsoleColorForLevel(LogLevel level)
        {
            Console.ForegroundColor = level switch
            {
                LogLevel.Trace => ConsoleColor.Gray,
                LogLevel.Debug => ConsoleColor.White,
                LogLevel.Information => ConsoleColor.Cyan,
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Error => ConsoleColor.Red,
                LogLevel.Critical => ConsoleColor.Magenta,
                _ => ConsoleColor.White
            };
        }

        private void EnsureLogFileExists()
        {
            if (!string.IsNullOrEmpty(_options.LogFilePath))
            {
                var directory = Path.GetDirectoryName(_options.LogFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }
        }

        public override void Dispose()
        {
            _writer.Complete();
            _fileSemaphore.Dispose();
            base.Dispose();
        }
    }

    public static class SkillzLoggingExtensions
    {
        public static IServiceCollection AddSkillzLogging(this IServiceCollection services, Action<SkillzLoggerOptions>? configure = null)
        {
            if (configure != null)
            {
                services.Configure(configure);
            }

            services.AddSingleton<SkillzLoggerProcessor>();
            services.AddSingleton<ILoggerProvider, SkillzLoggerProvider>();
            services.AddHostedService<SkillzLoggerProcessor>(provider => provider.GetRequiredService<SkillzLoggerProcessor>());

            return services;
        }

        public static ILoggingBuilder AddSkillzLogger(this ILoggingBuilder builder, Action<SkillzLoggerOptions>? configure = null)
        {
            if (configure != null)
            {
                builder.Services.Configure(configure);
            }

            builder.Services.AddSingleton<SkillzLoggerProcessor>();
            builder.Services.AddSingleton<ILoggerProvider, SkillzLoggerProvider>();
            builder.Services.AddHostedService<SkillzLoggerProcessor>(provider => provider.GetRequiredService<SkillzLoggerProcessor>());

            return builder;
        }
    }
}