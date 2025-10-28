using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SolutionBot
{
    internal sealed class ScheduleConfig
    {
        public string Timezone { get; set; } = "Eastern Standard Time"; // Windows TZ id; "America/New_York" also supported
        public ScheduleDefaults Defaults { get; set; } = new();
        public List<ScheduledEvent> Events { get; set; } = new();
    }

    internal sealed class ScheduleDefaults
    {
        // Show countdown starting this many days before the event if not overridden per event
        public int AnnounceDaysBefore { get; set; } = 7;

        // Playing | Watching | Listening | Competing
        public string ActivityType { get; set; } = "Playing";

        // Template for the activity text
        // Available placeholders: {type} {title} {description} {dd} {hh} {mm} {days} {hours} {minutes}
        public string Template { get; set; } = "{type}: {title} in {dd}d {hh}h {mm}m";

        // Optional fallback text when no event is within the window; leave empty to clear presence
        public string? FallbackStatus { get; set; } = null;
    }

    internal sealed class ScheduledEvent
    {
        public string Type { get; set; } = "Event"; // e.g., Quiz or Test
        public string Title { get; set; } = "";
        // Local event start date-time in the configured Timezone (no offset). Example: 2025-11-18T09:00:00
        public string StartsAt { get; set; } = "";
        public int? AnnounceDaysBefore { get; set; }
        public string? Description { get; set; }
    }

    internal static class ScheduleConfigProvider
    {
        private static ScheduleConfig? _cached;

        public static ScheduleConfig Get()
        {
            if (_cached is not null) return _cached;

            var baseDir = AppContext.BaseDirectory;
            var path = Path.Combine(baseDir, "schedule.json");
            if (!File.Exists(path))
                throw new FileNotFoundException("schedule.json not found.", path);

            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<ScheduleConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            }) ?? throw new InvalidDataException("Invalid schedule.json content.");

            // Basic validation
            cfg.Events = (cfg.Events ?? new()).Where(e =>
                !string.IsNullOrWhiteSpace(e.Title) &&
                !string.IsNullOrWhiteSpace(e.StartsAt)
            ).ToList();

            if (cfg.Events.Count == 0)
                throw new InvalidDataException("schedule.json contains no valid events.");

            _cached = cfg;
            return _cached;
        }
    }

    internal static class ScheduleTime
    {
        // Resolves either Windows ("Eastern Standard Time") or IANA ("America/New_York")
        public static TimeZoneInfo ResolveTimeZone(string? id)
        {
            var candidate = string.IsNullOrWhiteSpace(id) ? "Eastern Standard Time" : id.Trim();

            // Try as-is
            try { return TimeZoneInfo.FindSystemTimeZoneById(candidate); } catch { }

            // If given a common alias, attempt reasonable fallbacks
            var aliases = new[] { "America/New_York", "US/Eastern", "EST", "EDT", "Eastern Standard Time" };
            foreach (var alias in aliases.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(alias); } catch { }
            }

            // Last-resort: UTC (won't shift for DST, but prevents crashes)
            return TimeZoneInfo.Utc;
        }

        // Parses an ISO-like local date string (no offset) and returns the UTC DateTime for the configured zone.
        public static DateTime ParseLocalToUtc(string localIso, TimeZoneInfo tz)
        {
            if (!DateTime.TryParse(localIso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
                throw new FormatException($"Invalid date/time format: '{localIso}'. Use e.g. 2025-11-18T09:00:00");

            if (local.Kind != DateTimeKind.Unspecified)
            {
                // Force treat as local-in-zone clock time
                local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            }

            // Convert the unspecified local time in the given tz to UTC
            return TimeZoneInfo.ConvertTimeToUtc(local, tz);
        }
    }
}