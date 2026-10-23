using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using ZombieSLG.Application;
using ZombieSLG.Domain;

namespace ZombieSLG.Tests
{
    public class CloudSyncTests
    {
        private sealed class Clock : IClock { public double Now { get; set; } }

        private sealed class Repo : ISaveRepository
        {
            public GameSave Stored;
            public int Saves;
            public GameSave Load() => Stored;
            public void Save(GameSave save) { Stored = save; Saves++; }
            public void Delete() => Stored = null;
        }

        private sealed class FakeCloud : ICloudSave
        {
            public bool IsAvailable { get; set; } = true;
            public GameSave Remote;
            public bool FailNext;
            public readonly List<double> Pushed = new List<double>();

            public Task<GameSave> PullAsync()
            {
                if (FailNext) { FailNext = false; return Task.FromException<GameSave>(new Exception("net")); }
                return Task.FromResult(Remote);
            }

            public Task PushAsync(GameSave save)
            {
                if (FailNext) { FailNext = false; return Task.FromException(new Exception("net")); }
                Pushed.Add(save.LastTick);
                Remote = save;
                return Task.CompletedTask;
            }
        }

        private static GameSave At(double tick, int night = 1) => new GameSave { LastTick = tick, Night = night };

        [Test]
        public void Resolver_PicksTheSaveThatWasPlayedLast()
        {
            Assert.IsFalse(SaveResolver.CloudIsNewer(At(100), null));
            Assert.IsTrue(SaveResolver.CloudIsNewer(null, At(100)), "fresh install restores the cloud save");
            Assert.IsTrue(SaveResolver.CloudIsNewer(At(100), At(200)));
            Assert.IsFalse(SaveResolver.CloudIsNewer(At(200), At(100)));
            Assert.IsFalse(SaveResolver.CloudIsNewer(At(100), At(100.5)), "within tolerance keeps local");
        }

        [Test]
        public void Pull_NewerCloud_ReplacesLocal()
        {
            var repo = new Repo { Stored = At(100, night: 2) };
            var cloud = new FakeCloud { Remote = At(500, night: 7) };
            var sync = new CloudSyncService(cloud, repo, new Clock());
            Assert.AreEqual(SyncSource.Cloud, sync.PullAsync().Result);
            Assert.AreEqual(7, repo.Stored.Night);
        }

        [Test]
        public void Pull_OlderCloud_KeepsLocal()
        {
            var repo = new Repo { Stored = At(500, night: 7) };
            var cloud = new FakeCloud { Remote = At(100, night: 2) };
            var sync = new CloudSyncService(cloud, repo, new Clock());
            Assert.AreEqual(SyncSource.Local, sync.PullAsync().Result);
            Assert.AreEqual(7, repo.Stored.Night);
            Assert.AreEqual(0, repo.Saves);
        }

        [Test]
        public void Pull_OfflineOrError_FallsBackToLocal()
        {
            var repo = new Repo { Stored = At(100) };
            var offline = new CloudSyncService(new FakeCloud { IsAvailable = false, Remote = At(999) }, repo, new Clock());
            Assert.AreEqual(SyncSource.Offline, offline.PullAsync().Result);

            var failing = new CloudSyncService(new FakeCloud { FailNext = true, Remote = At(999) }, repo, new Clock());
            Assert.AreEqual(SyncSource.Offline, failing.PullAsync().Result);
            Assert.AreEqual(100, repo.Stored.LastTick, 0.001);
            Assert.IsNotNull(failing.LastError);
        }

        [Test]
        public void Push_IsThrottled_AndAlwaysSendsTheLatestSave()
        {
            var clock = new Clock { Now = 1000 };
            var cloud = new FakeCloud();
            var sync = new CloudSyncService(cloud, new Repo(), clock);

            sync.MarkDirty(At(1));
            sync.Tick();
            Assert.AreEqual(1, cloud.Pushed.Count, "first save goes up right away");

            clock.Now += 10;
            sync.MarkDirty(At(2));
            sync.MarkDirty(At(3));
            sync.Tick();
            Assert.AreEqual(1, cloud.Pushed.Count, "throttled");

            clock.Now += CloudSyncService.PushIntervalSeconds;
            sync.Tick();
            Assert.AreEqual(2, cloud.Pushed.Count);
            Assert.AreEqual(3, cloud.Pushed[1], 0.001, "only the latest pending save is sent");

            sync.Tick();
            Assert.AreEqual(2, cloud.Pushed.Count, "nothing pending");
        }

        [Test]
        public void Push_Failure_IsRetried()
        {
            var clock = new Clock { Now = 1000 };
            var cloud = new FakeCloud { FailNext = true };
            var sync = new CloudSyncService(cloud, new Repo(), clock);

            sync.MarkDirty(At(5));
            sync.PushNowAsync().Wait();
            Assert.AreEqual(0, cloud.Pushed.Count);
            Assert.IsNotNull(sync.LastError);

            sync.PushNowAsync().Wait();
            Assert.AreEqual(1, cloud.Pushed.Count);
            Assert.IsNull(sync.LastError);
        }

        [Test]
        public void BaseService_Saved_FeedsTheSync()
        {
            var clock = new Clock { Now = 1_000_000 };
            var cloud = new FakeCloud();
            var repo = new Repo();
            var sync = new CloudSyncService(cloud, repo, clock);
            var service = new BaseService(clock, repo, new EventBus(), TestData.Data);
            service.Saved += sync.MarkDirty;

            service.Load(); // saves once
            sync.Tick();
            Assert.AreEqual(1, cloud.Pushed.Count);
            Assert.AreSame(repo.Stored, cloud.Remote);
        }
    }
}
