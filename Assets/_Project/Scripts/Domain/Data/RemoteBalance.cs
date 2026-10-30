using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ZombieSLG.Domain
{
    /// <summary>
    /// Server-side balance overrides (Remote Config key <see cref="ConfigKey"/>): a flat JSON object of
    /// balance.csv keys → numbers, e.g. {"night.hp_per_night": 0.08}. Overrides are applied to the table
    /// rows <b>before</b> <see cref="GameDataBuilder.Build"/>, so every derived rule sees them and the
    /// builder's min/max validation still guards against a bad server value.
    /// </summary>
    public static class RemoteBalance
    {
        public const string ConfigKey = "balance_overrides";

        private static readonly Regex Pair = new Regex(
            "\"([^\"\\\\]+)\"\\s*:\\s*(\"?)(-?[0-9]+(?:\\.[0-9]+)?(?:[eE][-+]?[0-9]+)?)\\2\\s*(?=[,}])",
            RegexOptions.CultureInvariant);

        /// <summary>Parses a flat {"key": number} object. Non-numeric entries are reported and skipped.</summary>
        public static Dictionary<string, double> Parse(string json, List<string> warnings)
        {
            var result = new Dictionary<string, double>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(json)) return result;
            string body = json.Trim();
            if (!body.StartsWith("{") || !body.EndsWith("}"))
            {
                warnings?.Add("서버 밸런스 값이 JSON 객체 형식이 아닙니다");
                return result;
            }

            int matched = 0;
            foreach (Match m in Pair.Matches(body))
            {
                matched++;
                if (double.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                    result[m.Groups[1].Value] = v;
            }
            int keys = CountKeys(body);
            if (keys > matched) warnings?.Add($"서버 밸런스 값 중 숫자가 아닌 항목 {keys - matched}개를 무시했습니다");
            return result;
        }

        /// <summary>Copy of <paramref name="tables"/> with balance rows replaced; unknown keys are reported.</summary>
        public static GameTables WithOverrides(GameTables tables, IReadOnlyDictionary<string, double> overrides,
            List<string> warnings, out int applied)
        {
            applied = 0;
            var copy = new GameTables
            {
                Zombies = tables.Zombies,
                Buildings = tables.Buildings,
                Hordes = tables.Hordes,
                Heroes = tables.Heroes,
                Troops = tables.Troops,
                Research = tables.Research,
                Balance = new List<BalanceRow>(tables.Balance.Count),
            };
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in tables.Balance)
            {
                known.Add(row.Key);
                if (overrides != null && overrides.TryGetValue(row.Key, out double v))
                {
                    copy.Balance.Add(new BalanceRow { Key = row.Key, Value = v, Note = row.Note });
                    applied++;
                }
                else copy.Balance.Add(row);
            }
            if (overrides != null)
                foreach (var k in overrides.Keys)
                    if (!known.Contains(k)) warnings?.Add($"서버 밸런스 키 '{k}'는 balance.csv에 없어 무시했습니다");
            return copy;
        }

        /// <summary>
        /// Builds game data with the overrides. If the overridden tables fail validation, falls back to the
        /// shipped tables (and reports why) so a bad server value can never stop the game.
        /// </summary>
        public static GameData Build(GameTables tables, string overridesJson, List<string> errors, List<string> warnings,
            out int applied)
        {
            var overrides = Parse(overridesJson, warnings);
            applied = 0;
            if (overrides.Count > 0)
            {
                var patched = WithOverrides(tables, overrides, warnings, out int n);
                var patchErrors = new List<string>();
                var data = GameDataBuilder.Build(patched, patchErrors);
                if (data != null && patchErrors.Count == 0)
                {
                    applied = n;
                    return data;
                }
                foreach (var e in patchErrors) warnings?.Add("서버 밸런스 값 거부: " + e);
            }
            return GameDataBuilder.Build(tables, errors);
        }

        private static int CountKeys(string body)
        {
            int n = 0;
            bool inString = false, escaped = false, isKey = true;
            for (int i = 0; i < body.Length; i++)
            {
                char c = body[i];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') { inString = true; if (isKey) { n++; isKey = false; } }
                else if (c == ',') isKey = true;
            }
            return n;
        }
    }
}
