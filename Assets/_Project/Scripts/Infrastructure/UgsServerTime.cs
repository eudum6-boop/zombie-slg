using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using Unity.Services.CloudCode;
using ZombieSLG.Application;

namespace ZombieSLG.Infrastructure
{
    /// <summary>
    /// Trusted time from the Cloud Code script "server-time" (see CloudCode/server-time.js in the repo).
    /// Requires a signed-in player.
    /// </summary>
    public sealed class UgsServerTime : IServerTimeSource
    {
        public const string ScriptName = "server-time";

        public async Task<double> FetchUnixSecondsAsync()
        {
            // Non-generic call returns the script's return value as a string ("1730000000000").
            string raw = await CloudCodeService.Instance.CallEndpointAsync(ScriptName);
            return ParseMillis(raw) / 1000.0;
        }

        /// <summary>Accepts a bare number or a quoted number.</summary>
        public static double ParseMillis(string raw)
        {
            string s = (raw ?? "").Trim().Trim('"');
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double ms) || ms <= 0)
                throw new FormatException("server-time 응답을 읽을 수 없습니다: " + raw);
            return ms;
        }
    }

    /// <summary>Seconds on a monotonic timer (unaffected by the device clock).</summary>
    public static class MonotonicClock
    {
        private static readonly Stopwatch Watch = Stopwatch.StartNew();
        public static double Seconds => Watch.Elapsed.TotalSeconds;
    }
}
