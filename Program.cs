using DSharpPlus;
using DSharpPlus.Commands;
using System;
using System.IO;
using System.Threading.Tasks;

namespace SolutionBot
{
    internal static class Program
    {
        // Entry point
        private static async Task Main(string[] args)
        {
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

            var discord = builder.Build();

            // Wire up component interactions (for schedule pagination)
            //ScheduleService.WireUp(discord);

            Console.WriteLine("Registered slash commands globally (may take up to an hour to appear).");

            await discord.ConnectAsync();
            Console.WriteLine("Bot connected. Press Ctrl+C to exit.");
            await Task.Delay(-1);
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
