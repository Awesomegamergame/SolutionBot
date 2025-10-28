using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;

namespace SolutionBot
{
    internal static class PresenceUpdater
    {
        private static string? _lastText;
        private static ActivityType _lastType;

        public static async Task RunAsync(DiscordClient client, CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await UpdateOnceAsync(client, cancellationToken);
                }
                catch
                {
                    // Swallow errors to keep loop alive; consider logging if you have a logger.
                }

                // Update roughly once per minute to avoid rate limits and churn
                try { await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken); }
                catch (TaskCanceledException) { }
            }
        }

        private static async Task UpdateOnceAsync(DiscordClient client, CancellationToken ct)
        {
            ScheduleConfig cfg;
            try
            {
                cfg = ScheduleConfigProvider.Get();
            }
            catch
            {
                // Failed to load config; clear presence once
                await ClearIfChangedAsync(client, ct);
                return;
            }

            var tz = ScheduleTime.ResolveTimeZone(cfg.Timezone);
            var nowUtc = DateTime.UtcNow;

            var defaults = cfg.Defaults ?? new ScheduleDefaults();
            var windowDaysDefault = Math.Max(0, defaults.AnnounceDaysBefore);

            var candidates = cfg.Events
                .Select(e =>
                {
                    DateTime eventUtc;
                    try
                    {
                        eventUtc = ScheduleTime.ParseLocalToUtc(e.StartsAt, tz);
                    }
                    catch
                    {
                        return (evt: e, valid: false, diff: TimeSpan.Zero, windowDays: 0);
                    }

                    var diff = eventUtc - nowUtc;
                    var windowDays = Math.Max(0, e.AnnounceDaysBefore ?? windowDaysDefault);
                    var valid = diff > TimeSpan.Zero && diff <= TimeSpan.FromDays(windowDays);
                    return (evt: e, valid, diff, windowDays);
                })
                .Where(x => x.valid)
                .OrderBy(x => x.diff)
                .ToList();

            if (candidates.Count == 0)
            {
                var fallback = string.IsNullOrWhiteSpace(defaults.FallbackStatus) ? null : defaults.FallbackStatus;
                var at = ParseActivityType(defaults.ActivityType);
                await SetIfChangedAsync(client, fallback, at, ct);
                return;
            }

            var top = candidates.First();
            var text = RenderActivityText(defaults, top.evt, top.diff);
            var type = ParseActivityType(defaults.ActivityType);

            await SetIfChangedAsync(client, text, type, ct);
        }

        private static string RenderActivityText(ScheduleDefaults defaults, ScheduledEvent evt, TimeSpan until)
        {
            // Normalize non-negative components
            if (until < TimeSpan.Zero) until = TimeSpan.Zero;
            var dd = (int)Math.Floor(until.TotalDays);
            var hh = until.Hours;
            var mm = until.Minutes;

            var template = string.IsNullOrWhiteSpace(defaults.Template)
                ? "{type}: {title} in {dd}d {hh}h {mm}m"
                : defaults.Template;

            return template
                .Replace("{type}", evt.Type ?? "Event")
                .Replace("{title}", evt.Title ?? "")
                .Replace("{description}", evt.Description ?? "")
                .Replace("{dd}", dd.ToString())
                .Replace("{hh}", hh.ToString("00"))
                .Replace("{mm}", mm.ToString("00"))
                .Replace("{days}", dd.ToString())
                .Replace("{hours}", ((int)Math.Floor(until.TotalHours)).ToString())
                .Replace("{minutes}", ((int)Math.Floor(until.TotalMinutes)).ToString());
        }

        private static ActivityType ParseActivityType(string? s)
        {
            return (s ?? "Playing").Trim().ToLowerInvariant() switch
            {
                "watching" => ActivityType.Watching,
                "listening" => ActivityType.ListeningTo,
                "competing" => ActivityType.Competing,
                // Streaming and Custom are intentionally not mapped here
                _ => ActivityType.Playing
            };
        }

        private static async Task SetIfChangedAsync(DiscordClient client, string? text, ActivityType type, CancellationToken ct)
        {
            if (string.Equals(text, _lastText, StringComparison.Ordinal) && type == _lastType)
                return;

            if (string.IsNullOrWhiteSpace(text))
            {
                await client.UpdateStatusAsync(activity: null, userStatus: UserStatus.Online);
                _lastText = null;
                _lastType = type;
                return;
            }

            var activity = new DiscordActivity(text, type);
            await client.UpdateStatusAsync(activity, UserStatus.Online);
            _lastText = text;
            _lastType = type;
        }

        private static async Task ClearIfChangedAsync(DiscordClient client, CancellationToken ct)
        {
            if (_lastText is null) return;
            await client.UpdateStatusAsync(activity: null, userStatus: UserStatus.Online);
            _lastText = null;
        }
    }
}