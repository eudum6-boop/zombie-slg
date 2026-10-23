using System;
using System.Threading.Tasks;
using ZombieSLG.Domain;

namespace ZombieSLG.Application
{
    /// <summary>Remote copy of the player's save (Unity Cloud Save in production, a fake in tests).</summary>
    public interface ICloudSave
    {
        /// <summary>True once the player is signed in and the service is reachable.</summary>
        bool IsAvailable { get; }
        /// <summary>Returns null when the player has no cloud save yet.</summary>
        Task<GameSave> PullAsync();
        /// <summary>Must snapshot <paramref name="save"/> before its first await (the caller keeps mutating it).</summary>
        Task PushAsync(GameSave save);
    }

    public enum SyncSource
    {
        /// <summary>Cloud unreachable; playing on the local save.</summary>
        Offline,
        /// <summary>Local save was newer (or equal); it will be uploaded.</summary>
        Local,
        /// <summary>Cloud save was newer (other device / reinstall) and replaced the local one.</summary>
        Cloud,
    }

    public static class SaveResolver
    {
        /// <summary>Seconds of clock difference treated as "the same moment".</summary>
        public const double Tolerance = 1.0;

        /// <summary>
        /// Picks the save that was played last. <see cref="GameSave.LastTick"/> moves forward every frame of
        /// play, so the larger one is the most recent session. A missing local save (fresh install) always
        /// takes the cloud copy.
        /// </summary>
        public static bool CloudIsNewer(GameSave local, GameSave cloud)
        {
            if (cloud == null) return false;
            if (local == null) return true;
            return cloud.LastTick > local.LastTick + Tolerance;
        }
    }

    /// <summary>
    /// Offline-first sync: the local file is always the working copy. On start the newer of
    /// local/cloud wins; afterwards local saves are uploaded at most every <see cref="PushIntervalSeconds"/>
    /// plus once more when the app pauses or quits.
    /// </summary>
    public sealed class CloudSyncService
    {
        public const double PushIntervalSeconds = 60;

        private readonly ICloudSave _cloud;
        private readonly ISaveRepository _local;
        private readonly IClock _clock;
        private readonly Action<string> _log;
        private GameSave _pending;
        private double _lastPush = double.MinValue;
        private bool _pushing;

        public bool IsOnline => _cloud.IsAvailable;
        public int PushCount { get; private set; }
        public string LastError { get; private set; }

        public CloudSyncService(ICloudSave cloud, ISaveRepository local, IClock clock, Action<string> log = null)
        {
            _cloud = cloud ?? throw new ArgumentNullException(nameof(cloud));
            _local = local ?? throw new ArgumentNullException(nameof(local));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _log = log;
        }

        /// <summary>
        /// Call before <see cref="BaseService.Load"/>. If the cloud copy is newer it is written to the local
        /// repository so the normal load path (migration, offline catch-up) applies to it.
        /// </summary>
        public async Task<SyncSource> PullAsync()
        {
            if (!_cloud.IsAvailable) return SyncSource.Offline;
            GameSave cloud;
            try
            {
                cloud = await _cloud.PullAsync();
            }
            catch (Exception e)
            {
                Fail("클라우드 불러오기 실패: " + e.Message);
                return SyncSource.Offline;
            }

            var local = _local.Load();
            if (SaveResolver.CloudIsNewer(local, cloud))
            {
                _local.Save(cloud);
                _log?.Invoke($"클라우드 세이브가 더 최신이라 가져왔습니다 (NIGHT {cloud.Night}).");
                return SyncSource.Cloud;
            }
            return SyncSource.Local;
        }

        /// <summary>Hook to <see cref="BaseService.Saved"/>.</summary>
        public void MarkDirty(GameSave save) => _pending = save;

        /// <summary>Call every frame; uploads the latest save when the interval has passed.</summary>
        public void Tick()
        {
            if (_pending == null || _pushing || !_cloud.IsAvailable) return;
            if (_clock.Now - _lastPush < PushIntervalSeconds) return;
            _ = PushNowAsync();
        }

        /// <summary>Uploads the pending save right away (pause / quit / after a night).</summary>
        public async Task PushNowAsync()
        {
            if (_pending == null || _pushing || !_cloud.IsAvailable) return;
            var save = _pending;
            _pending = null;
            _pushing = true;
            _lastPush = _clock.Now;
            try
            {
                await _cloud.PushAsync(save);
                PushCount++;
                LastError = null;
            }
            catch (Exception e)
            {
                if (_pending == null) _pending = save; // retry next interval
                Fail("클라우드 저장 실패: " + e.Message);
            }
            finally
            {
                _pushing = false;
            }
        }

        private void Fail(string message)
        {
            LastError = message;
            _log?.Invoke(message);
        }
    }
}
