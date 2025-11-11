using DSharpPlus;
using DSharpPlus.Commands;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SolutionBot
{
    internal static class Program
    {
        // Entry point
        private static async Task Main(string[] args)
        {
            // Cache build mode
            if (IsBuildCacheRequested(args))
            {
                try
                {
                    var (onlySource, force) = ParseCacheArgs(args);
                    await CacheService.BuildAllAsync(force: force, onlySource: onlySource);
                    Console.WriteLine("Cache build complete.");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Cache build failed: {ex.Message}");
                    Environment.ExitCode = 1;
                }
                return;
            }

            var token = Environment.GetEnvironmentVariable("DISCORD_TOKEN");
            if (string.IsNullOrWhiteSpace(token))
                token = ReadTokenFromFile();

            if (string.IsNullOrWhiteSpace(token))
            {
                Console.Error.WriteLine(
                    "Bot token not provided. Set DISCORD_TOKEN or provide a token file (set DISCORD_TOKEN_FILE or place 'discord_token.txt' next to the app).");
                return;
            }

            // Configure the client and register the Commands extension BEFORE building the client
            DiscordClientBuilder builder = DiscordClientBuilder.CreateDefault(token, DiscordIntents.AllUnprivileged);
            builder.UseCommands
            (
                (provider, extension) =>
                {
                    extension.AddCommands(typeof(Commands));
                }
            );

            // Wire up component interactions (for schedule pagination)
            ScheduleService.WireUp(builder);

            var discord = builder.Build();

            Console.WriteLine("Registered slash commands globally (may take up to an hour to appear).");

            await discord.ConnectAsync();
            Console.WriteLine("Bot connected. Press Ctrl+C to exit.");
            await Task.Delay(-1);
        }

        private static bool IsBuildCacheRequested(string[] args)
        {
            if (args is null || args.Length == 0) return false;
            return args.Any(a =>
                a.Equals("cache", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--build-cache", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("-c", StringComparison.OrdinalIgnoreCase));
        }

        private static (string? onlySource, bool force) ParseCacheArgs(string[] args)
        {
            string? onlySource = null;
            bool force = args.Any(a => a.Equals("--force", StringComparison.OrdinalIgnoreCase));

            // --source "<name>" or --source=<name>
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (a.StartsWith("--source=", StringComparison.OrdinalIgnoreCase))
                {
                    onlySource = a.Substring("--source=".Length).Trim().Trim('"');
                    break;
                }
                if (a.Equals("--source", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    onlySource = args[i + 1].Trim().Trim('"');
                    break;
                }
            }

            return (onlySource, force);
        }

        private static string? ReadTokenFromFile()
        {
            var pathFromEnv = Environment.GetEnvironmentVariable("DISCORD_TOKEN_FILE");
            if (!string.IsNullOrWhiteSpace(pathFromEnv))
            {
                if (File.Exists(pathFromEnv))
                    return File.ReadAllText(pathFromEnv).Trim();

                Console.Error.WriteLine($"Token file not found at: {pathFromEnv}");
            }

            var defaultPath = Path.Combine(AppContext.BaseDirectory, "discord_token.txt");
            if (File.Exists(defaultPath))
                return File.ReadAllText(defaultPath).Trim();

            return null;
        }
    }
}
