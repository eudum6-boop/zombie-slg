using System;
using System.Threading.Tasks;
using NUnit.Framework;
using ZombieSLG.Application;
using ZombieSLG.Domain;

namespace ZombieSLG.Tests
{
    public class SyncedClockTests
    {
        private double _wall = 1_000_000;
        private double _mono = 50;

        private SyncedClock NewClock() => new SyncedClock(() => _wall, () => _mono);

        private sealed class FixedServer : IServerTimeSource
        {
            public double Time;
            public bool Fail;
            public Task<double> FetchUnixSecondsAsync() =>
                Fail ? Task.FromException<double>(new Exception("offline")) : Task.FromResult(Time);
        }

        [Test]
        public void BeforeSync_UsesDeviceClock()
        {
            var c = NewClock();
            Assert.IsFalse(c.IsServerTime);
            Assert.AreEqual(_wall, c.Now, 1e-9);
        }

        [Test]
        public void AfterSync_IgnoresDeviceClockChanges()
        {
            var c = NewClock();
            c.Sync(_wall);
            _mono += 30;
            _wall += 86_400; // player moves the phone clock a day ahead
            Assert.AreEqual(1_000_030, c.Now, 1e-9);
            Assert.Greater(c.DeviceOffset, 80_000.0);
        }

        [Test]
        public void NeverRunsBackwards()
        {
            var c = NewClock();
            c.Sync(2_000_000);
            _mono += 100;
            double before = c.Now;
            c.Unsync(); // device clock is far behind the server
            Assert.AreEqual(before, c.Now, 1e-9);
            _wall = 3_000_000;
            Assert.AreEqual(3_000_000, c.Now, 1e-9);
        }

        [Test]
        public void TrySync_FailureKeepsDeviceClock()
        {
            var c = NewClock();
            Assert.IsFalse(c.TrySyncAsync(new FixedServer { Fail = true }).Result);
            Assert.IsFalse(c.IsServerTime);
            Assert.IsTrue(c.TrySyncAsync(new FixedServer { Time = 5_000_000 }).Result);
            Assert.AreEqual(5_000_000, c.Now, 1e-9);
        }

        [Test]
        public void TamperDetection_FlagsSavesFromTheFuture()
        {
            Assert.IsFalse(SyncedClock.LooksTampered(1000, 1000));
            Assert.IsFalse(SyncedClock.LooksTampered(1000 + SyncedClock.TamperToleranceSeconds, 1000));
            Assert.IsTrue(SyncedClock.LooksTampered(1000 + 3600, 1000));
        }

        [Test]
        public void ForwardedDeviceClock_CannotFinishBuildingsEarly()
        {
            var c = NewClock();
            c.Sync(_wall);
            var save = SaveMigrator.NewGame(c.Now, TestData.Data);
            var s = new BaseState(save, TestData.Data);
            save.Materials = save.Food = save.Fuel = 1_000_000;
            Assert.AreEqual(UpgradeBlock.None, s.StartUpgrade(BasePlots.Headquarters, c.Now));

            _wall += 7 * 86_400; // cheat
            _mono += 1;
            s.Advance(c.Now);
            Assert.AreEqual(1, s.HqLevel, "still building");
        }
    }
}
