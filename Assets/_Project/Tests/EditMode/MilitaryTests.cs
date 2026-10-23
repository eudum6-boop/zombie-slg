using System.Collections.Generic;
using NUnit.Framework;
using ZombieSLG.Domain;

namespace ZombieSLG.Tests
{
    internal static class MilitaryFixture
    {
        public const double T0 = 2_000_000;

        /// <summary>New game with Barracks/Clinic/Lab at the given levels and plenty of resources.</summary>
        public static BaseState Base(out GameSave save, int barracks = 1, int clinic = 1, int lab = 1)
        {
            save = SaveMigrator.NewGame(T0, TestData.Data);
            foreach (var b in save.Buildings)
            {
                if (b.Type == BuildingType.Barracks) b.Level = barracks;
                if (b.Type == BuildingType.Clinic) b.Level = clinic;
                if (b.Type == BuildingType.ResearchLab) b.Level = lab;
            }
            save.Food = save.Fuel = save.Materials = 100_000;
            return new BaseState(save, TestData.Data);
        }
    }

    public class TroopTests
    {
        private const double T0 = MilitaryFixture.T0;
        private static GameData D => TestData.Data;

        [Test]
        public void NewGame_StartsWithInfantry_AndNoBarracks()
        {
            var save = SaveMigrator.NewGame(T0, D);
            var s = new BaseState(save, D);
            Assert.AreEqual(D.Troops.StartInfantry, s.Healthy(TroopType.Infantry));
            Assert.AreEqual(0, s.BarracksLevel);
            Assert.AreEqual(ArmyBlock.NoBuilding, s.CanTrain(TroopType.Infantry, 1));
        }

        [Test]
        public void Train_PaysAndArrivesAfterTrainTime()
        {
            var s = MilitaryFixture.Base(out var save);
            int before = s.Healthy(TroopType.Shooter);
            double food0 = save.Food;
            var cost = s.TrainCost(TroopType.Shooter, 3);
            double secs = s.TrainSeconds(TroopType.Shooter, 3);

            Assert.AreEqual(ArmyBlock.None, s.Train(TroopType.Shooter, 3, T0));
            Assert.AreEqual(food0 - cost.Food, save.Food, 0.001);
            s.Advance(T0 + secs - 1);
            Assert.AreEqual(before, s.Healthy(TroopType.Shooter), "not yet");
            s.Advance(T0 + secs);
            Assert.AreEqual(before + 3, s.Healthy(TroopType.Shooter));
            Assert.AreEqual(0, s.TrainingQueue.Count);
        }

        [Test]
        public void TrainingBatches_RunOneAfterAnother()
        {
            var s = MilitaryFixture.Base(out _);
            double a = s.TrainSeconds(TroopType.Infantry, 2);
            double b = s.TrainSeconds(TroopType.Vehicle, 1);
            s.Train(TroopType.Infantry, 2, T0);
            s.Train(TroopType.Vehicle, 1, T0);
            Assert.AreEqual(T0 + a + b, s.TrainingQueue[1].End, 0.001);

            s.Advance(T0 + a);
            Assert.AreEqual(0, s.Healthy(TroopType.Vehicle));
            s.Advance(T0 + a + b);
            Assert.AreEqual(1, s.Healthy(TroopType.Vehicle));
        }

        [Test]
        public void Capacity_CountsHealthyWoundedAndQueues()
        {
            var s = MilitaryFixture.Base(out var save);
            int cap = s.TroopCapacity;
            Assert.AreEqual(D.Troops.CapacityBase, cap);
            int room = cap - s.TroopsHoused;
            Assert.Greater(room, 0);
            Assert.AreEqual(ArmyBlock.OverCapacity, s.CanTrain(TroopType.Infantry, room + 1));
            Assert.AreEqual(ArmyBlock.None, s.Train(TroopType.Infantry, room, T0));
            Assert.AreEqual(ArmyBlock.OverCapacity, s.CanTrain(TroopType.Infantry, 1));

            save.Buildings.Find(b => b.Type == BuildingType.Barracks).Level = 2;
            Assert.AreEqual(cap + D.Troops.CapacityPerLevel, s.TroopCapacity);
        }

        [Test]
        public void Train_ChecksResources()
        {
            var s = MilitaryFixture.Base(out var save);
            save.Fuel = 0;
            Assert.AreEqual(ArmyBlock.NotEnoughResources, s.Train(TroopType.Vehicle, 1, T0));
            Assert.AreEqual(0, s.TrainingQueue.Count);
        }

        [Test]
        public void NightCasualties_SplitIntoDeadAndWounded_AndWoundedCanBeHealed()
        {
            var s = MilitaryFixture.Base(out var save);
            int start = s.Healthy(TroopType.Infantry);
            s.ApplyNightResult(true, new NightReward(0, 0, 0, 0), 0, new[] { 2, 0, 0 }, new[] { 4, 0, 0 });
            Assert.AreEqual(start - 6, s.Healthy(TroopType.Infantry));
            Assert.AreEqual(4, s.Wounded(TroopType.Infantry));

            Assert.AreEqual(ArmyBlock.NotEnoughWounded, s.CanHeal(TroopType.Infantry, 5));
            double secs = s.HealSeconds(TroopType.Infantry, 4);
            Assert.AreEqual(ArmyBlock.None, s.Heal(TroopType.Infantry, 4, T0));
            Assert.AreEqual(0, s.Wounded(TroopType.Infantry));
            s.Advance(T0 + secs);
            Assert.AreEqual(start - 2, s.Healthy(TroopType.Infantry), "the dead stay dead");
        }

        [Test]
        public void Casualties_NeverExceedWhatWasThere()
        {
            var s = MilitaryFixture.Base(out _);
            int start = s.Healthy(TroopType.Infantry);
            s.ApplyNightResult(false, new NightReward(0, 0, 0, 0), 0, new[] { start, 5, 5 }, new[] { 9, 9, 9 });
            Assert.AreEqual(0, s.Healthy(TroopType.Infantry));
            Assert.AreEqual(0, s.Wounded(TroopType.Infantry));
            Assert.AreEqual(0, s.Healthy(TroopType.Shooter));
        }

        [Test]
        public void HigherClinic_HealsFaster_ButNotBelowMinimum()
        {
            var s1 = MilitaryFixture.Base(out _, clinic: 1);
            var s5 = MilitaryFixture.Base(out _, clinic: 5);
            var s99 = MilitaryFixture.Base(out _, clinic: 99);
            Assert.Less(s5.HealSeconds(TroopType.Vehicle, 1), s1.HealSeconds(TroopType.Vehicle, 1));
            Assert.AreEqual(System.Math.Round(D.Troop(TroopType.Vehicle).HealSeconds * D.Troops.HealSpeedMin),
                s99.HealSeconds(TroopType.Vehicle, 1), 0.001);
        }

        [Test]
        public void Heal_NeedsClinic()
        {
            var s = MilitaryFixture.Base(out _, clinic: 0);
            s.ApplyNightResult(true, new NightReward(0, 0, 0, 0), 0, null, new[] { 1, 0, 0 });
            Assert.AreEqual(ArmyBlock.NoBuilding, s.CanHeal(TroopType.Infantry, 1));
        }
    }

    public class SquadTests
    {
        private static GameData D => TestData.Data;

        [Test]
        public void Damage_HitsFrontTypeFirst()
        {
            var sq = new SquadState(D, new[] { 2, 3, 1 }, 1f);
            sq.TakeDamage(D.Troop(TroopType.Infantry).Hp * 2);
            Assert.AreEqual(0, sq.Alive(TroopType.Infantry));
            Assert.AreEqual(1, sq.Alive(TroopType.Vehicle), "vehicle is next in line, untouched");
            Assert.AreEqual(3, sq.Alive(TroopType.Shooter), "shooters stand at the back");
        }

        [Test]
        public void PartialDamage_CarriesOver()
        {
            var sq = new SquadState(D, new[] { 1, 0, 0 }, 1f);
            float hp = D.Troop(TroopType.Infantry).Hp;
            Assert.AreEqual(0, sq.TakeDamage(hp * 0.6f));
            Assert.AreEqual(1, sq.TakeDamage(hp * 0.6f));
            Assert.IsTrue(sq.IsDefeated);
        }

        [Test]
        public void Fallen_SplitByWoundedRatio()
        {
            var sq = new SquadState(D, new[] { 100, 0, 0 }, 1f);
            sq.TakeDamage(D.Troop(TroopType.Infantry).Hp * 100);
            int w = sq.Wounded(TroopType.Infantry), d = sq.Dead(TroopType.Infantry);
            Assert.AreEqual(100, w + d);
            Assert.AreEqual(D.Troops.WoundedRatio * 100, w, 1.0);
        }

        [Test]
        public void HpMultiplier_MakesUnitsTougher()
        {
            var weak = new SquadState(D, new[] { 5, 0, 0 }, 1f);
            var tough = new SquadState(D, new[] { 5, 0, 0 }, 2f);
            float dmg = D.Troop(TroopType.Infantry).Hp * 3;
            weak.TakeDamage(dmg);
            tough.TakeDamage(dmg);
            Assert.Greater(tough.TotalAlive, weak.TotalAlive);
        }

        [Test]
        public void Counters_FollowTable()
        {
            var inf = D.Troop(TroopType.Infantry);
            var veh = D.Troop(TroopType.Vehicle);
            Assert.Greater(SquadMath.Volley(veh, 1, ZombieType.Brute, 1f), veh.Damage, "vehicles counter brutes");
            Assert.Greater(SquadMath.Volley(inf, 1, ZombieType.Runner, 1f), inf.Damage, "infantry counters runners");
            Assert.AreEqual(0f, SquadMath.Volley(inf, 0, ZombieType.Walker, 1f));
        }

        [Test]
        public void Split_SpreadsRemainderToFirstGates()
        {
            CollectionAssert.AreEqual(new[] { 4, 3, 3 }, SquadMath.Split(10, 3));
            CollectionAssert.AreEqual(new[] { 0, 0, 0 }, SquadMath.Split(0, 3));
        }
    }

    public class ResearchTests
    {
        private const double T0 = MilitaryFixture.T0;
        private static GameData D => TestData.Data;

        [Test]
        public void Table_HasThreeBranchesWithValidTree()
        {
            var branches = new HashSet<ResearchBranch>();
            foreach (var r in D.ResearchNodes)
            {
                branches.Add(r.Branch);
                if (r.Requires != null) Assert.IsTrue(D.HasResearch(r.Requires), r.Id);
            }
            Assert.AreEqual(3, branches.Count);
        }

        [Test]
        public void NeedsLab_Tier_AndPrerequisite()
        {
            var noLab = MilitaryFixture.Base(out _, lab: 0);
            Assert.AreEqual(ResearchBlock.NoLab, noLab.CanResearch("eco_farming"));

            var s = MilitaryFixture.Base(out _, lab: 2);
            Assert.AreEqual(ResearchBlock.RequiresOther, s.CanResearch("eco_salvage"));
            Assert.AreEqual(ResearchBlock.UnknownResearch, s.CanResearch("nope"));

            var tier3 = FirstOfTier(3);
            Assert.IsNotNull(tier3, "table has a tier-3 node");
            Assert.AreEqual(ResearchBlock.LabTooLow, s.CanResearch(tier3.Id));
        }

        [Test]
        public void OneProjectAtATime_ThenLevelUps()
        {
            var s = MilitaryFixture.Base(out _, lab: 2);
            var def = D.ResearchNode("eco_farming");
            Assert.AreEqual(ResearchBlock.None, s.StartResearch("eco_farming", T0));
            Assert.AreEqual(ResearchBlock.Busy, s.CanResearch("mil_drill"));
            s.Advance(T0 + def.SecondsFor(1));
            Assert.AreEqual(1, s.ResearchLevel("eco_farming"));
            Assert.IsNull(s.ActiveResearch);
            Assert.AreEqual(ResearchBlock.None, s.CanResearch("eco_salvage"), "prerequisite unlocked");
        }

        [Test]
        public void MaxLevel_Stops()
        {
            var s = MilitaryFixture.Base(out var save, lab: 9);
            var def = D.ResearchNode("mil_drill");
            save.Research.Add(new ResearchSave { Id = def.Id, Level = def.MaxLevel });
            Assert.AreEqual(ResearchBlock.MaxLevel, s.CanResearch(def.Id));
        }

        [Test]
        public void Effects_RaiseProductionAndStorage_AndCutBuildTime()
        {
            var s = MilitaryFixture.Base(out var save, lab: 9);
            var farm = s.Get(BasePlots.Farm);
            double rate0 = s.EffectiveRatePerHour(farm);
            int cap0 = s.StorageCap;
            double build0 = s.BuildSecondsFor(BuildingType.Headquarters, 2);

            save.Research.Add(new ResearchSave { Id = "eco_farming", Level = 2 });
            save.Research.Add(new ResearchSave { Id = "eco_storage", Level = 1 });
            save.Research.Add(new ResearchSave { Id = "eco_engineering", Level = 1 });

            double farmBonus = D.ResearchNode("eco_farming").PerLevel * 2;
            double m = s.ManagerBonus;
            Assert.AreEqual(rate0 / (1 + m) * (1 + m + farmBonus), s.EffectiveRatePerHour(farm), rate0 * 0.001 + 1e-9);
            Assert.Greater(s.StorageCap, cap0);
            Assert.Less(s.BuildSecondsFor(BuildingType.Headquarters, 2), build0);
        }

        [Test]
        public void Reductions_AreCapped()
        {
            var s = MilitaryFixture.Base(out var save, lab: 9);
            save.Research.Add(new ResearchSave { Id = "mil_bootcamp", Level = 999 });
            Assert.AreEqual(D.Research.MaxReduction, s.ResearchBonus(ResearchEffect.TrainSpeed), 1e-9);
        }

        [Test]
        public void OfflineJump_EqualsSmallSteps_WithResearchAndTraining()
        {
            GameSave Run(double step)
            {
                var s = MilitaryFixture.Base(out var save);
                save.Food = save.Fuel = save.Materials = 2_000;
                s.StartResearch("eco_farming", T0);
                s.Train(TroopType.Infantry, 3, T0);
                double end = T0 + 3 * 3600;
                for (double t = T0 + step; t < end; t += step) s.Advance(t);
                s.Advance(end);
                return save;
            }

            var jump = Run(3 * 3600);
            var steps = Run(1);
            Assert.AreEqual(jump.Food, steps.Food, 0.01);
            Assert.AreEqual(jump.Materials, steps.Materials, 0.01);
            Assert.AreEqual(ResearchMath.Level(jump, "eco_farming"), ResearchMath.Level(steps, "eco_farming"));
            Assert.AreEqual(jump.Troops[0].Healthy, steps.Troops[0].Healthy);
        }

        private static ResearchDef FirstOfTier(int tier)
        {
            foreach (var r in D.ResearchNodes) if (r.Tier == tier) return r;
            return null;
        }
    }

    public class SaveV3MigrationTests
    {
        [Test]
        public void V2Save_GetsTroopsAndNewPlots()
        {
            var data = TestData.Data;
            var v2 = new GameSave { Version = 2, Survivors = 5, Morale = 80, LastTick = 10, Night = 3 };
            v2.Troops = null; v2.Training = null; v2.Healing = null; v2.Research = null; // as JsonUtility leaves them for old files
            var s = SaveMigrator.Migrate(v2, 20, data);
            Assert.AreEqual(GameSave.CurrentVersion, s.Version);
            Assert.AreEqual(TroopRules.TypeCount, s.Troops.Count);
            Assert.AreEqual(data.Troops.StartInfantry, s.Troops[0].Healthy);
            Assert.IsTrue(s.Buildings.Exists(b => b.Type == BuildingType.Barracks));
            Assert.IsTrue(s.Buildings.Exists(b => b.Type == BuildingType.ResearchLab));

            SaveMigrator.Migrate(s, 30, data);
            Assert.AreEqual(data.Troops.StartInfantry, s.Troops[0].Healthy, "only granted once");
        }

        [Test]
        public void ResearchRemovedFromTable_IsDropped()
        {
            var s = SaveMigrator.NewGame(1, TestData.Data);
            s.Research.Add(new ResearchSave { Id = "gone", Level = 2 });
            s.ResearchActive = "gone";
            SaveMigrator.Migrate(s, 2, TestData.Data);
            Assert.AreEqual(0, s.Research.Count);
            Assert.IsNull(s.ResearchActive);
        }
    }
}
