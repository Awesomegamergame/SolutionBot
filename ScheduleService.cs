using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace SolutionBot
{
    internal static class ScheduleService
    {
        private const int PageSize = 3;

        private static readonly JsonSerializerOptions ScheduleJsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private static string SchedulePath => Path.Combine(AppContext.BaseDirectory, "schedule.json");

        internal sealed class ScheduleItem
        {
            public string Id { get; set; } = NewId();
            public string Kind { get; set; } = "test";
            public string Title { get; set; } = "";
            public DateOnly Date { get; set; }
            public string? Description { get; set; }
        }

        public static void WireUp(DiscordClientBuilder builder)
        {
            builder.ConfigureEventHandlers(
                (configure) =>
                {
                    configure.HandleComponentInteractionCreated(HandlePaginationAsync);
                }
            );
        }

        private static async Task HandlePaginationAsync(DiscordClient sender, ComponentInteractionCreatedEventArgs e)
        {
            var id = e.Id;
            if (string.IsNullOrWhiteSpace(id) || !id.StartsWith("schedule|", StringComparison.Ordinal))
                return;

            try
            {
                var parts = id.Split('|');
                if (parts.Length != 5) return;

                var action = parts[1];
                var includePast = parts[2] == "1";
                if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var page))
                    return;
                if (!ulong.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out var ownerId))
                    return;

                if (e.User.Id != ownerId)
                {
                    await e.Interaction.CreateResponseAsync(
                        DiscordInteractionResponseType.ChannelMessageWithSource,
                        new DiscordInteractionResponseBuilder()
                            .WithContent("Only the original requester can use these buttons.")
                            .AsEphemeral(true));
                    return;
                }

                var newPage = action.Equals("next", StringComparison.OrdinalIgnoreCase) ? page + 1 :
                              action.Equals("back", StringComparison.OrdinalIgnoreCase) ? page - 1 : page;

                var (embed, components) = await BuildPageAsync(includePast, newPage, ownerId);

                var resp = new DiscordInteractionResponseBuilder().AddEmbed(embed);
                if (components.Length > 0)
                {
                    resp.AddActionRowComponent(components);
                }
                else
                {
                    // Replace any existing buttons with disabled nav to avoid stale interactions
                    var disabledBack = new DiscordButtonComponent(DiscordButtonStyle.Secondary, $"schedule|noop|0|0|{ownerId}", "Back", true);
                    var disabledNext = new DiscordButtonComponent(DiscordButtonStyle.Primary, $"schedule|noop|0|0|{ownerId}", "Next", true);
                    resp.AddActionRowComponent(disabledBack, disabledNext);
                }

                await e.Interaction.CreateResponseAsync(DiscordInteractionResponseType.UpdateMessage, resp);
            }
            catch (Exception ex)
            {
                await e.Interaction.CreateResponseAsync(
                    DiscordInteractionResponseType.ChannelMessageWithSource,
                    new DiscordInteractionResponseBuilder()
                        .WithContent($"Failed to update schedule: {ex.Message}")
                        .AsEphemeral(true));
            }
        }

        internal static async Task<(DiscordEmbed Embed, DiscordButtonComponent[] Components)> BuildPageAsync(bool includePast, int pageIndex, ulong userId)
        {
            var items = await LoadAsync();

            var today = DateOnly.FromDateTime(DateTime.Now);
            var filtered = items
                .Where(i => includePast || i.Date >= today)
                .OrderBy(i => i.Date)
                .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var total = filtered.Count;
            if (total == 0)
            {
                var emptyEmbed = new DiscordEmbedBuilder()
                    .WithTitle(includePast ? "Schedule" : "Upcoming Schedule")
                    .WithDescription(includePast ? "No scheduled items." : "No upcoming items.")
                    .WithColor(new DiscordColor(0x5865F2))
                    .WithTimestamp(DateTimeOffset.Now)
                    .Build();

                // Return no components; callers handle not adding components (or replacing with disabled)
                return (emptyEmbed, Array.Empty<DiscordButtonComponent>());
            }

            var totalPages = (int)Math.Ceiling(total / (double)PageSize);
            pageIndex = Math.Clamp(pageIndex, 0, Math.Max(0, totalPages - 1));

            var start = pageIndex * PageSize;
            var page = filtered.Skip(start).Take(PageSize).ToList();

            var embedBuilder = new DiscordEmbedBuilder()
                .WithTitle(includePast ? "Schedule" : "Upcoming Schedule")
                .WithDescription($"Showing {start + 1}–{start + page.Count} of {total} {(includePast ? "total" : "upcoming")} item(s). Page {pageIndex + 1}/{totalPages}.")
                .WithColor(new DiscordColor(0x5865F2))
                .WithFooter("Use /schedule-add to add items, /schedule-remove <id or index> to remove.", null)
                .WithTimestamp(DateTimeOffset.Now);

            for (int i = 0; i < page.Count; i++)
            {
                var it = page[i];
                var absoluteIndex = start + i + 1;
                var dateStr = it.Date.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);

                var kind = it.Kind switch
                {
                    "quiz" => "Quiz",
                    "mastering" => "Mastering",
                    _ => "Test"
                };

                var title = string.IsNullOrWhiteSpace(it.Title) ? "(no title)" : it.Title.Trim();

                var fieldName = $"{absoluteIndex}. {dateStr} – {kind}: {Truncate(title, 110)}";
                if (fieldName.Length > 256) fieldName = Truncate(fieldName, 256);

                var value = string.IsNullOrWhiteSpace(it.Description)
                    ? $"id: `{it.Id}`"
                    : $"{Truncate(it.Description!.Trim(), 900)}\n id: `{it.Id}`";

                embedBuilder.AddField(fieldName, value, inline: false);
            }

            var isFirst = pageIndex <= 0;
            var isLast = pageIndex >= totalPages - 1;

            var backId = $"schedule|back|{(includePast ? "1" : "0")}|{pageIndex}|{userId}";
            var nextId = $"schedule|next|{(includePast ? "1" : "0")}|{pageIndex}|{userId}";

            var backBtn = new DiscordButtonComponent(DiscordButtonStyle.Secondary, backId, "Back", isFirst);
            var nextBtn = new DiscordButtonComponent(DiscordButtonStyle.Primary, nextId, "Next", isLast);

            return (embedBuilder.Build(), new DiscordButtonComponent[] { backBtn, nextBtn });
        }

        internal static async Task<List<ScheduleItem>> LoadAsync()
        {
            if (!File.Exists(SchedulePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SchedulePath)!);
                await File.WriteAllTextAsync(SchedulePath, "[]");
                return new List<ScheduleItem>();
            }

            await using var fs = new FileStream(SchedulePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var items = await JsonSerializer.DeserializeAsync<List<ScheduleItem>>(fs, ScheduleJsonOptions);
            return items ?? new List<ScheduleItem>();
        }

        internal static async Task SaveAsync(List<ScheduleItem> items)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SchedulePath)!);

            var tmp = SchedulePath + ".tmp";
            await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(fs, items, ScheduleJsonOptions);
                await fs.FlushAsync();
            }

            if (File.Exists(SchedulePath))
            {
                try
                {
                    var backup = SchedulePath + ".bak";
                    File.Replace(tmp, SchedulePath, backup, ignoreMetadataErrors: true);
                    try { File.Delete(backup); } catch { }
                }
                catch
                {
                    File.Copy(tmp, SchedulePath, overwrite: true);
                }
                finally
                {
                    try { File.Delete(tmp); } catch { }
                }

                return;
            }

            File.Move(tmp, SchedulePath);
        }

        internal static string NewId()
        {
            Span<byte> buf = stackalloc byte[4];
            RandomNumberGenerator.Fill(buf);
            return Convert.ToHexString(buf).ToLowerInvariant();
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= max ? s : s.Substring(0, Math.Max(0, max - 1)) + "…";
        }
    }
}