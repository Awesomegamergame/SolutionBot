using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Globalization;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using DSharpPlus.SlashCommands.Attributes;
using System.IO;

namespace SolutionBot
{
    public sealed class Commands : ApplicationCommandModule
    {
        [SlashCommand("answer", "Find and send a JPG image of the page containing the given problem number (e.g., 5-10).")]
        public async Task AnswerAsync(
            InteractionContext ctx,
            [Option("problem", "Problem number, e.g., 5-10")] string problem,
            [Option("source", "Optional source name from sources.json; defaults to the config default")]
            [Autocomplete(typeof(SourceNameAutocomplete))] string? source = null)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);

            AnswerConfig cfg;
            try
            {
                cfg = AnswerConfigProvider.Get();
            }
            catch (Exception ex)
            {
                await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                    .WithContent($"Failed to load sources.json: {ex.Message}"));
                return;
            }

            var sourceName = string.IsNullOrWhiteSpace(source) ? cfg.DefaultSource : source.Trim();
            if (!cfg.Sources.TryGetValue(sourceName, out var pdfPath) || string.IsNullOrWhiteSpace(pdfPath))
            {
                var available = string.Join(", ", cfg.Sources.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
                await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                    .WithContent($"Unknown source '{sourceName}'. Available: {available}"));
                return;
            }

            if (!File.Exists(pdfPath))
            {
                await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                    .WithContent($"PDF not found for '{sourceName}' at: {pdfPath}"));
                return;
            }

            var normalized = NormalizeProblem(problem);
            if (normalized is null)
            {
                await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                    .WithContent("Invalid problem format. Use something like 5-10 or 5.10."));
                return;
            }

            int? pageNumber;
            try
            {
                pageNumber = await Task.Run(() => PdfHelpers.FindFirstMatchingPage(pdfPath, normalized));
            }
            catch (Exception ex)
            {
                await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                    .WithContent($"Error while searching the PDF: {ex.Message}"));
                return;
            }

            if (pageNumber is null)
            {
                await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                    .WithContent($"Couldn't find '{normalized}' in '{sourceName}'."));
                return;
            }

            try
            {
                var outFile = await Task.Run(() => PdfHelpers.RenderPageToJpeg(pdfPath, pageNumber.Value));
                await using var fs = File.OpenRead(outFile);

                var srcSlug = string.Concat(sourceName.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')).Trim('_');

                var webhook = new DiscordWebhookBuilder()
                    .WithContent($"Answer page for {normalized} (page {pageNumber.Value}) from '{sourceName}'.")
                    .AddFile($"answer-{normalized}-{srcSlug}.jpg", fs);

                await ctx.EditResponseAsync(webhook);
            }
            catch (Exception ex)
            {
                await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                    .WithContent($"Failed to render/send the page image: {ex.Message}"));
            }
            finally
            {
                try
                {
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                    GC.WaitForPendingFinalizers();
                }
                catch { }
            }
        }

        private static string? NormalizeProblem(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            var trimmed = input.Trim();
            var m = Regex.Match(trimmed, "^\\s*(\\d+)\\s*[\\p{Pd}\\.]\\s*(\\d+)\\s*$");
            if (!m.Success) return null;
            return $"{m.Groups[1].Value}–{m.Groups[2].Value}";
        }

        // Schedule commands

        [SlashCommand("schedule", "Show upcoming tests and quizzes.")]
        public async Task ScheduleListAsync(
            InteractionContext ctx,
            [Option("includePast", "Include past dates as well")] bool includePast = false)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);

            try
            {
                var (embed, components) = await ScheduleService.BuildPageAsync(includePast, 0, ctx.User.Id);

                var builder = new DiscordWebhookBuilder().AddEmbed(embed);
                if (components.Length > 0) builder.AddComponents(components); // avoid empty-components error

                await ctx.EditResponseAsync(builder);
            }
            catch (Exception ex)
            {
                await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent($"Failed to build schedule: {ex.Message}"));
            }
        }

        [SlashCommand("schedule-add", "Add a test/quiz to the schedule.")]
        public async Task ScheduleAddAsync(
            InteractionContext ctx,
            [Option("kind", "Type of item")] [Choice("Test", "test")] [Choice("Quiz", "quiz")] string kind,
            [Option("title", "Short title or subject")] string title,
            [Option("date", "Date (YYYY-MM-DD)")] string date,
            [Option("description", "Optional description of topics")] string? description = null)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);

            if (!DateOnly.TryParseExact(date.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            {
                await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent("Invalid date. Use YYYY-MM-DD (e.g., 2025-11-03)."));
                return;
            }

            List<ScheduleService.ScheduleItem> items;
            try
            {
                items = await ScheduleService.LoadAsync();
            }
            catch (Exception ex)
            {
                await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent($"Failed to read schedule.json: {ex.Message}"));
                return;
            }

            var item = new ScheduleService.ScheduleItem
            {
                Id = ScheduleService.NewId(),
                Kind = (kind?.Trim().ToLowerInvariant() == "quiz") ? "quiz" : "test",
                Title = title?.Trim() ?? "",
                Date = d,
                Description = string.IsNullOrWhiteSpace(description) ? null : description!.Trim()
            };

            items.Add(item);

            try
            {
                await ScheduleService.SaveAsync(items);
            }
            catch (Exception ex)
            {
                await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent($"Failed to save schedule.json: {ex.Message}"));
                return;
            }

            var kindTitle = item.Kind == "quiz" ? "Quiz" : "Test";
            await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                .WithContent($"Added [{kindTitle}] {item.Date:MM/dd/yyyy} — {item.Title} (id: {item.Id})"));
        }

        [SlashCommand("schedule-remove", "Remove a scheduled item by id or list index.")]
        public async Task ScheduleRemoveAsync(
            InteractionContext ctx,
            [Option("idOrIndex", "The id shown in /schedule, or the number from the list")] string idOrIndex)
        {
            await ctx.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource);

            List<ScheduleService.ScheduleItem> items;
            try
            {
                items = await ScheduleService.LoadAsync();
            }
            catch (Exception ex)
            {
                await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent($"Failed to read schedule.json: {ex.Message}"));
                return;
            }

            if (items.Count == 0)
            {
                await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent("Schedule is empty."));
                return;
            }

            ScheduleService.ScheduleItem? toRemove = null;

            if (int.TryParse(idOrIndex.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx))
            {
                var ordered = items.OrderBy(i => i.Date).ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase).ToList();
                if (idx >= 1 && idx <= ordered.Count)
                    toRemove = ordered[idx - 1];
            }

            if (toRemove is null)
            {
                var key = idOrIndex.Trim();
                toRemove = items.FirstOrDefault(i => i.Id.Equals(key, StringComparison.OrdinalIgnoreCase));
            }

            if (toRemove is null)
            {
                await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent("No matching item found with that id or index."));
                return;
            }

            items.RemoveAll(i => i.Id.Equals(toRemove.Id, StringComparison.OrdinalIgnoreCase));

            try
            {
                await ScheduleService.SaveAsync(items);
            }
            catch (Exception ex)
            {
                await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent($"Failed to save schedule.json: {ex.Message}"));
                return;
            }

            var kindTitle = toRemove.Kind == "quiz" ? "Quiz" : "Test";
            await ctx.EditResponseAsync(new DiscordWebhookBuilder()
                .WithContent($"Removed [{kindTitle}] {toRemove.Date:MM/dd/yyyy} — {toRemove.Title} (id: {toRemove.Id})"));
        }
    }
}