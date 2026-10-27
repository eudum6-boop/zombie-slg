using System;
using System.Threading.Tasks;

namespace ZombieSLG.Application
{
    /// <summary>Authoritative current time (Cloud Code script in production).</summary>
    public interface IServerTimeSource
    {
        /// <summary>Server Unix time in seconds. Throws when unreachable.</summary>
        Task<double> FetchUnixSecondsAsync();
    }

    /// <summary>
    /// Game clock that follows the server once synced: Now = server time at sync + time elapsed on a
    /// monotonic timer since then. Changing the device clock during play has no effect.
    /// Before any sync (offline) it uses the device clock, but never runs backwards.
    /// </summary>
    public sealed class SyncedClock : IClock
    {
        /// <summary>A save stamped further in the future than this is treated as clock tampering.</summary>
        public const double TamperToleranceSeconds = 120;

        private readonly Func<double> _wall;
        private readonly Func<double> _monotonic;
        private bool _synced;
        private double _serverAtSync;
        private double _monoAtSync;
        private double _floor = double.MinValue;

        public SyncedClock(Func<double> wallClock, Func<double> monotonicSeconds)
        {
            _wall = wallClock ?? throw new ArgumentNullException(nameof(wallClock));
            _monotonic = monotonicSeconds ?? throw new ArgumentNullException(nameof(monotonicSeconds));
        }

        public bool IsServerTime => _synced;

        /// <summary>Device clock minus game clock, in seconds (positive = device runs ahead).</summary>
        public double DeviceOffset => _wall() - Now;

        public double Now
        {
            get
            {
                double t = _synced ? _serverAtSync + (_monotonic() - _monoAtSync) : _wall();
                if (t < _floor) t = _floor;
                _floor = t;
                return t;
            }
        }

        public void Sync(double serverUnixSeconds)
        {
            _serverAtSync = serverUnixSeconds;
            _monoAtSync = _monotonic();
            _synced = true;
        }

        /// <summary>Falls back to the device clock (e.g. resume without network); time still never goes backwards.</summary>
        public void Unsync() => _synced = false;

        /// <summary>
        /// Fetches server time and syncs. Returns false (clock unchanged) on failure or timeout, so the
        /// game keeps working offline.
        /// </summary>
        public async Task<bool> TrySyncAsync(IServerTimeSource source, int timeoutMs = 5000)
        {
            try
            {
                var fetch = source.FetchUnixSecondsAsync();
                var done = await Task.WhenAny(fetch, Task.Delay(timeoutMs));
                if (done != fetch) return false;
                Sync(await fetch);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// True when a save was last ticked in the future relative to trusted time, i.e. the device clock
        /// was pushed forward while playing offline.
        /// </summary>
        public static bool LooksTampered(double saveLastTick, double trustedNow) =>
            saveLastTick > trustedNow + TamperToleranceSeconds;
    }
}
