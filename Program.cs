using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.SlashCommands;

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

            var discord = new DiscordClient(new DiscordConfiguration
            {
                Token = token,
                TokenType = TokenType.Bot,
                Intents = DiscordIntents.AllUnprivileged
            });

            var slash = discord.UseSlashCommands();

            // Register to a specific guild for instant availability if provided, otherwise register globally.
            var guildIdEnv = Environment.GetEnvironmentVariable("DISCORD_GUILD_ID");
            if (ulong.TryParse(guildIdEnv, NumberStyles.None, CultureInfo.InvariantCulture, out var guildId))
            {
                slash.RegisterCommands<AnswerCommands>(guildId);
                Console.WriteLine($"Registered slash commands to guild {guildId}.");
            }
            else
            {
                slash.RegisterCommands<AnswerCommands>();
                Console.WriteLine("Registered slash commands globally (may take up to an hour to appear).");
            }

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
