using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ZombieSLG.Domain;

namespace ZombieSLG.Tests
{
    public class FlowFieldTests
    {
        private static FlowField Build(int w, int h, GridCoord target, params (int x, int y)[] blocked)
        {
            var f = new FlowField(w, h);
            foreach (var (x, y) in blocked) f.SetCost(x, y, FlowField.BlockedCost);
            f.Build(new[] { target });
            return f;
        }

        [Test]
        public void OpenGrid_PointsTowardTarget()
        {
            var f = Build(10, 10, new GridCoord(5, 5));
            f.GetDirection(0, 5, out int dx, out int dy);
            Assert.AreEqual((1, 0), (dx, dy));
            f.GetDirection(5, 9, out dx, out dy);
            Assert.AreEqual((0, -1), (dx, dy));
            f.GetDirection(0, 0, out dx, out dy);
            Assert.AreEqual((1, 1), (dx, dy), "diagonal approach on an open grid");
        }

        [Test]
        public void TargetCell_HasZeroIntegrationAndNoDirection()
        {
            var f = Build(5, 5, new GridCoord(2, 2));
            Assert.AreEqual(0, f.GetIntegration(2, 2));
            f.GetDirection(2, 2, out int dx, out int dy);
            Assert.AreEqual((0, 0), (dx, dy));
        }

        [Test]
        public void Wall_ForcesDetourThroughGap()
        {
            // Horizontal wall on row 5 with a single gap at x = 8. Target below the wall.
            var blocked = Enumerable.Range(0, 10).Where(x => x != 8).Select(x => (x, 5)).ToArray();
            var f = Build(10, 10, new GridCoord(2, 1), blocked);

            // Follow the field from the top-left; we must pass through the gap.
            int cx = 1, cy = 9;
            bool passedGap = false;
            for (int step = 0; step < 40 && !(cx == 2 && cy == 1); step++)
            {
                f.GetDirection(cx, cy, out int dx, out int dy);
                Assert.IsFalse(dx == 0 && dy == 0, $"stuck at ({cx},{cy})");
                cx += dx; cy += dy;
                Assert.IsFalse(f.IsBlocked(cx, cy), $"walked into a wall at ({cx},{cy})");
                if (cy == 5) { Assert.AreEqual(8, cx); passedGap = true; }
            }
            Assert.IsTrue(passedGap);
            Assert.AreEqual((2, 1), (cx, cy));
        }

        [Test]
        public void Diagonal_DoesNotCutCorners()
        {
            // Target at (1,1); cells (1,2) and (2,1) blocked. From (2,2) the diagonal would squeeze between them.
            var f = Build(4, 4, new GridCoord(1, 1), (1, 2), (2, 1));
            f.GetDirection(2, 2, out int dx, out int dy);
            Assert.AreNotEqual((-1, -1), (dx, dy));
        }

        [Test]
        public void EnclosedCell_IsUnreachable()
        {
            var f = Build(5, 5, new GridCoord(0, 0), (3, 4), (4, 3), (3, 3));
            Assert.AreEqual(FlowField.Unreachable, f.GetIntegration(4, 4));
            f.GetDirection(4, 4, out int dx, out int dy);
            Assert.AreEqual((0, 0), (dx, dy));
        }
    }

    public class WaveDirectorTests
    {
        // Simulates a full night where the defenders keep the alive count at zero.
        private static List<SpawnRequest> RunNight(int night, int seed)
        {
            var d = new WaveDirector(TestData.Data.Wave, TestData.Data.Night, night, seed);
            var all = new List<SpawnRequest>();
            var frame = new List<SpawnRequest>();
            while (!d.IsFinished)
            {
                frame.Clear();
                d.Tick(1f / 30f, 0, frame);
                all.AddRange(frame);
            }
            return all;
        }

        [Test]
        public void SameSeed_IsDeterministic()
        {
            var a = RunNight(1, 42);
            var b = RunNight(1, 42);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].Type, b[i].Type);
                Assert.AreEqual(a[i].Side, b[i].Side);
            }
        }

        [Test]
        public void Night_EndsAtDuration()
        {
            var w = TestData.Data.Wave.Clone();
            w.Duration = 10f;
            var d = new WaveDirector(w, TestData.Data.Night, 1, 1);
            var buf = new List<SpawnRequest>();
            for (int i = 0; i < 1000 && !d.IsFinished; i++) d.Tick(0.1f, 0, buf);
            Assert.IsTrue(d.IsFinished);
            Assert.AreEqual(10f, d.Elapsed, 1e-4f);
            int before = buf.Count;
            d.Tick(1f, 0, buf);
            Assert.AreEqual(before, buf.Count, "no spawns after the night ends");
        }

        [Test]
        public void Hordes_TriggerExactlyOnce()
        {
            var settings = TestData.Data.Wave;
            var spawns = RunNight(1, 7);
            int hordeSpawns = spawns.Count(s => s.IsHorde);
            int expected = settings.Hordes.Sum(h => h.Count);
            Assert.AreEqual(expected, hordeSpawns);
        }

        [Test]
        public void SpecialTypes_RespectUnlockTimes()
        {
            var d = new WaveDirector(TestData.Data.Wave, TestData.Data.Night, 1, 3);
            var buf = new List<SpawnRequest>();
            var settings = TestData.Data.Wave;
            while (d.Elapsed < settings.RunnerUnlockTime - 1f) d.Tick(0.1f, 0, buf);
            Assert.IsFalse(buf.Any(s => s.Type != ZombieType.Walker), "only walkers before the runner unlock");
        }

        [Test]
        public void MaxAlive_IsRespected()
        {
            var settings = TestData.Data.Wave.Clone();
            settings.MaxAlive = 10;
            var d = new WaveDirector(settings, TestData.Data.Night, 1, 5);
            var buf = new List<SpawnRequest>();
            d.Tick(60f, 10, buf);
            Assert.AreEqual(0, buf.Count);
            d.Tick(1f, 8, buf);
            Assert.LessOrEqual(buf.Count, 2);
        }

        [Test]
        public void LaterNights_SpawnMore()
        {
            Assert.Greater(RunNight(5, 11).Count, RunNight(1, 11).Count);
        }

        [Test]
        public void UpcomingHorde_IsAnnouncedWithinWindow()
        {
            var settings = TestData.Data.Wave;
            var d = new WaveDirector(settings, TestData.Data.Night, 1, 9);
            var buf = new List<SpawnRequest>();
            d.Tick(settings.Hordes[0].Time - 6f, 0, buf);
            Assert.IsFalse(d.TryGetUpcomingHorde(5f, out _, out _));
            d.Tick(2f, 0, buf);
            Assert.IsTrue(d.TryGetUpcomingHorde(5f, out float s, out _));
            Assert.AreEqual(4f, s, 1e-3f);
        }
    }

    public class CombatRulesTests
    {
        [Test]
        public void RollHit_CritBelowChance()
        {
            var crit = CombatRules.RollHit(10f, 0.2f, 2f, 0.1);
            Assert.IsTrue(crit.IsCritical);
            Assert.AreEqual(20f, crit.Damage, 1e-5f);

            var normal = CombatRules.RollHit(10f, 0.2f, 2f, 0.5);
            Assert.IsFalse(normal.IsCritical);
            Assert.AreEqual(10f, normal.Damage, 1e-5f);
        }

        [Test]
        public void AreaDamage_FallsOffToEdgeFactor()
        {
            Assert.AreEqual(100f, CombatRules.AreaDamage(100f, 0f, 4f), 1e-4f);
            Assert.AreEqual(50f, CombatRules.AreaDamage(100f, 4f, 4f), 1e-4f);
            Assert.AreEqual(75f, CombatRules.AreaDamage(100f, 2f, 4f), 1e-4f);
            Assert.AreEqual(0f, CombatRules.AreaDamage(100f, 4.01f, 4f));
        }

        [Test]
        public void NightRules_GrowAndClampBelowOne()
        {
            Assert.AreEqual(1f, TestData.Data.Night.HpMultiplier(0));
            Assert.AreEqual(1f, TestData.Data.Night.HpMultiplier(1));
            Assert.Greater(TestData.Data.Night.HpMultiplier(3), TestData.Data.Night.HpMultiplier(2));
        }
    }

    public class NightRewardTests
    {
        [Test]
        public void Defeat_GivesNoShardsAndReducedResources()
        {
            var win = TestData.Data.Rewards.Calculate(1, true, 100, 0.8f);
            var loss = TestData.Data.Rewards.Calculate(1, false, 100, 0f);
            Assert.AreEqual(0, loss.HeroShards);
            Assert.Less(loss.Food, win.Food);
        }

        [TestCase(0.9f, 3)]
        [TestCase(0.7f, 3)]
        [TestCase(0.5f, 2)]
        [TestCase(0.1f, 1)]
        public void Stars_ByRemainingHp(float ratio, int stars)
        {
            Assert.AreEqual(stars, TestData.Data.Rewards.Stars(true, ratio));
        }

        [Test]
        public void Rewards_ScaleWithNight()
        {
            var n1 = TestData.Data.Rewards.Calculate(1, true, 50, 1f);
            var n4 = TestData.Data.Rewards.Calculate(4, true, 50, 1f);
            Assert.Greater(n4.Food, n1.Food);
            Assert.Greater(n4.Fuel, n1.Fuel);
            Assert.Greater(n4.Materials, n1.Materials);
        }
    }
}
