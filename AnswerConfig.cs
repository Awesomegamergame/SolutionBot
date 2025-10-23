using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DiscordBot
{
    internal sealed class AnswerConfig
    {
        public string DefaultSource { get; set; } = "";
        public Dictionary<string, string> Sources { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    internal static class AnswerConfigProvider
    {
        private static AnswerConfig? _cached;

        public static AnswerConfig Get()
        {
            if (_cached is not null) return _cached;

            var baseDir = AppContext.BaseDirectory;
            var path = Path.Combine(baseDir, "sources.json");
            if (!File.Exists(path))
                throw new FileNotFoundException("sources.json not found.", path);

            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<AnswerConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            }) ?? throw new InvalidDataException("Invalid JSON content.");

            // Normalize dictionary to case-insensitive and trimmed keys
            var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in cfg.Sources ?? Enumerable.Empty<KeyValuePair<string, string>>())
            {
                if (string.IsNullOrWhiteSpace(kv.Key) || string.IsNullOrWhiteSpace(kv.Value)) continue;
                normalized[kv.Key.Trim()] = kv.Value.Trim();
            }
            cfg.Sources = normalized;

            if (string.IsNullOrWhiteSpace(cfg.DefaultSource))
                throw new InvalidDataException("defaultSource is missing.");
            if (cfg.Sources.Count == 0)
                throw new InvalidDataException("sources are empty.");
            if (!cfg.Sources.ContainsKey(cfg.DefaultSource))
                throw new InvalidDataException($"defaultSource '{cfg.DefaultSource}' not found in sources.");

            _cached = cfg;
            return _cached;
        }
    }
}