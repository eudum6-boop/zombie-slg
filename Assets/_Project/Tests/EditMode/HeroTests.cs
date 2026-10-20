using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ZombieSLG.Domain;

namespace ZombieSLG.Tests
{
    public class GachaTests
    {
        private static (GameSave save, HeroRoster roster) NewRoster(int seed, int tickets)
        {
            var save = SaveMigrator.NewGame(1000, TestData.Data);
            save.GachaSeed = seed;
            save.RecruitTickets = tickets;
            return (save, new HeroRoster(save, TestData.Data));
        }

        [Test]
        public void RarityRates_SumToOne_AndDisclosureCoversEveryHero()
        {
            var d = TestData.Data;
            Assert.AreEqual(1.0, d.Gacha.RarityRates.Sum(), 1e-9);
            var disclosed = d.Gacha.Disclose(d.Heroes);
            Assert.AreEqual(d.Heroes.Count, disclosed.Count);
            Assert.AreEqual(1.0, disclosed.Sum(x => x.Probability), 1e-9);
        }

        [Test]
        public void SameSeed_SameResults()
        {
            var a = NewRoster(42, 100).roster;
            var b = NewRoster(42, 100).roster;
            var ra = new List<GachaResult>();
            var rb = new List<GachaResult>();
            for (int i = 0; i < 5; i++)
            {
                a.Pull(10, ra);
                b.Pull(10, rb);
                CollectionAssert.AreEqual(ra.Select(r => r.HeroId).ToList(), rb.Select(r => r.HeroId).ToList());
            }
        }

        [Test]
        public void Pity_GuaranteesLegendaryWithinLimit()
        {
            int pity = TestData.Data.Gacha.PityLegendary;
            for (int seed = 1; seed <= 20; seed++)
            {
                var (_, roster) = NewRoster(seed, 100000);
                var results = new List<GachaResult>();
                int sinceLegendary = 0;
                for (int i = 0; i < 1000; i++)
                {
                    roster.Pull(1, results);
                    if (results[0].Rarity == HeroRarity.Legendary) sinceLegendary = 0;
                    else sinceLegendary++;
                    Assert.Less(sinceLegendary, pity, $"seed {seed}: {sinceLegendary} pulls without a legendary");
                }
            }
        }

        [Test]
        public void PityPull_IsFlaggedAndResetsCounter()
        {
            var (save, roster) = NewRoster(7, 10);
            save.GachaPity = TestData.Data.Gacha.PityLegendary - 1;
            var results = new List<GachaResult>();
            roster.Pull(1, results);
            Assert.AreEqual(HeroRarity.Legendary, results[0].Rarity);
            Assert.IsTrue(results[0].FromPity);
            Assert.AreEqual(0, roster.PityCounter);
        }

        [Test]
        public void TenPull_AlwaysHasRareOrBetter()
        {
            var (_, roster) = NewRoster(3, 100000);
            var results = new List<GachaResult>();
            for (int i = 0; i < 500; i++)
            {
                roster.Pull(10, results);
                Assert.AreEqual(10, results.Count);
                Assert.IsTrue(results.Any(r => r.Rarity >= HeroRarity.Rare), $"10-pull #{i} had no Rare+");
            }
        }

        [Test]
        public void ObservedRates_MatchTable()
        {
            // Pity and the 10-pull guarantee only add to higher rarities, so observed >= table for legendary.
            var (_, roster) = NewRoster(11, 100000);
            var results = new List<GachaResult>();
            var counts = new int[4];
            const int pulls = 40000;
            for (int i = 0; i < pulls; i++)
            {
                roster.Pull(1, results);
                counts[(int)results[0].Rarity]++;
            }
            var rates = TestData.Data.Gacha.RarityRates;
            Assert.AreEqual(rates[(int)HeroRarity.Common], counts[0] / (double)pulls, 0.02);
            Assert.AreEqual(rates[(int)HeroRarity.Rare], counts[1] / (double)pulls, 0.02);
            Assert.AreEqual(rates[(int)HeroRarity.Epic], counts[2] / (double)pulls, 0.015);
            double legendary = counts[3] / (double)pulls;
            Assert.GreaterOrEqual(legendary, rates[(int)HeroRarity.Legendary] * 0.9);
            Assert.Less(legendary, rates[(int)HeroRarity.Legendary] * 2.0, "pity should lift the rate, not dominate it");
        }

        [Test]
        public void Tickets_AreSpentAndChecked()
        {
            var (save, roster) = NewRoster(5, 9);
            var results = new List<GachaResult>();
            Assert.AreEqual(PullBlock.NotEnoughTickets, roster.Pull(10, results));
            Assert.AreEqual(9, save.RecruitTickets);
            Assert.AreEqual(PullBlock.InvalidCount, roster.Pull(3, results));
            Assert.AreEqual(PullBlock.None, roster.Pull(1, results));
            Assert.AreEqual(8, save.RecruitTickets);
        }

        [Test]
        public void Duplicates_GiveShards_NewHeroesJoinAtOneStar()
        {
            var (save, roster) = NewRoster(9, 1000);
            var results = new List<GachaResult>();
            for (int i = 0; i < 30; i++) roster.Pull(10, results);

            var d = TestData.Data;
            foreach (var h in save.Heroes) Assert.GreaterOrEqual(h.Stars, 1);
            Assert.AreEqual(save.Heroes.Count, save.Heroes.Select(h => h.Id).Distinct().Count(), "no duplicate entries");
            int expectedShards = 0;
            // Re-run from scratch to sum shards from results.
            var (save2, roster2) = NewRoster(9, 1000);
            for (int i = 0; i < 30; i++)
            {
                roster2.Pull(10, results);
                foreach (var r in results)
                {
                    if (r.IsNew) Assert.AreEqual(0, r.ShardsGained);
                    else Assert.AreEqual(d.HeroRules.DuplicateShards(r.Rarity), r.ShardsGained);
                    expectedShards += r.ShardsGained;
                }
            }
            Assert.AreEqual(expectedShards, save2.Heroes.Sum(h => h.Shards));
        }
    }

    public class HeroRosterTests
    {
        private static (GameSave save, HeroRoster roster) Fresh()
        {
            var save = SaveMigrator.NewGame(1000, TestData.Data);
            return (save, new HeroRoster(save, TestData.Data));
        }

        private static HeroDef FirstOfRole(HeroRole role) => TestData.Data.Heroes.First(h => h.Role == role);

        [Test]
        public void NewGame_HasStarterAsLeader()
        {
            var (save, roster) = Fresh();
            var starter = TestData.Data.StarterHero;
            Assert.IsTrue(roster.Owns(starter.Id));
            Assert.AreEqual(starter.Id, save.LeaderHero);
            Assert.AreEqual(starter.Id, roster.Leader.Id);
            Assert.AreEqual(TestData.Data.Gacha.StartTickets, roster.Tickets);
        }

        [Test]
        public void Promote_SpendsShardsUntilMaxStars()
        {
            var (save, roster) = Fresh();
            var rules = TestData.Data.HeroRules;
            string id = TestData.Data.StarterHero.Id;
            var h = roster.Get(id);

            Assert.AreEqual(PromoteBlock.NotEnoughShards, roster.Promote(id));
            h.Shards = 1000;
            int spent = 0;
            while (roster.CanPromote(id) == PromoteBlock.None)
            {
                spent += rules.CostToReach(h.Stars + 1);
                Assert.AreEqual(PromoteBlock.None, roster.Promote(id));
            }
            Assert.AreEqual(rules.MaxStars, h.Stars);
            Assert.AreEqual(1000 - spent, h.Shards);
            Assert.AreEqual(PromoteBlock.MaxStars, roster.Promote(id));
            Assert.AreEqual(PromoteBlock.NotOwned, roster.Promote("nobody"));
        }

        [Test]
        public void Stars_ScaleCombatStats()
        {
            var (_, roster) = Fresh();
            string id = TestData.Data.StarterHero.Id;
            var one = roster.CombatStats(id);
            roster.Get(id).Stars = 3;
            var three = roster.CombatStats(id);
            Assert.Greater(three.Damage, one.Damage);
            Assert.Greater(three.MaxHp, one.MaxHp);
            Assert.Less(three.FireInterval, one.FireInterval);
        }

        [Test]
        public void Slots_OnlyAcceptMatchingRole()
        {
            var (save, roster) = Fresh();
            var econ = FirstOfRole(HeroRole.Economy);
            var scout = FirstOfRole(HeroRole.Explore);

            Assert.AreEqual(AssignBlock.NotOwned, roster.Assign(HeroSlot.Manager, econ.Id));
            save.Heroes.Add(new HeroSave { Id = econ.Id, Stars = 1 });
            save.Heroes.Add(new HeroSave { Id = scout.Id, Stars = 1 });

            Assert.AreEqual(AssignBlock.WrongRole, roster.Assign(HeroSlot.Leader, econ.Id));
            Assert.AreEqual(AssignBlock.WrongRole, roster.Assign(HeroSlot.Manager, scout.Id));
            Assert.AreEqual(AssignBlock.None, roster.Assign(HeroSlot.Manager, econ.Id));
            Assert.AreEqual(AssignBlock.None, roster.Assign(HeroSlot.Scout, scout.Id));
            Assert.AreEqual(econ.EconBonus, roster.ManagerBonus, 1e-9);
            Assert.AreEqual(scout.ScoutBonus, roster.ScoutBonus, 1e-9);
        }

        [Test]
        public void ManagerHero_RaisesBaseProduction()
        {
            var (save, roster) = Fresh();
            var state = new BaseState(save, TestData.Data);
            var farm = state.Get(BasePlots.Farm);
            double before = state.EffectiveRatePerHour(farm);

            var econ = FirstOfRole(HeroRole.Economy);
            save.Heroes.Add(new HeroSave { Id = econ.Id, Stars = 1 });
            roster.Assign(HeroSlot.Manager, econ.Id);

            Assert.AreEqual(before * (1 + econ.EconBonus), state.EffectiveRatePerHour(farm), 1e-6);
        }

        [Test]
        public void ScoutBonus_RaisesNightResources()
        {
            var rewards = TestData.Data.Rewards;
            var plain = rewards.Calculate(1, true, 100, 1f);
            var scouted = rewards.Calculate(1, true, 100, 1f, 0.25);
            Assert.Greater(scouted.Food, plain.Food);
            Assert.AreEqual(plain.Tickets, scouted.Tickets, "scouting boosts resources, not tickets");
        }
    }

    public class SaveV2MigrationTests
    {
        [Test]
        public void V1Save_GetsTicketsStarterAndSeed()
        {
            var v1 = new GameSave { Version = 1, Night = 4, HeroShards = 7, Survivors = 6, Morale = 80, LastTick = 500 };
            v1.Heroes = null; // field didn't exist in v1 JSON
            var s = SaveMigrator.Migrate(v1, 1000, TestData.Data);

            Assert.AreEqual(GameSave.CurrentVersion, s.Version);
            Assert.AreEqual(7 + TestData.Data.Gacha.StartTickets, s.RecruitTickets);
            Assert.AreEqual(0, s.HeroShards);
            Assert.AreNotEqual(0, s.GachaSeed);
            Assert.AreEqual(TestData.Data.StarterHero.Id, s.LeaderHero);
            Assert.AreEqual(4, s.Night, "progress kept");
        }

        [Test]
        public void MigratingTwice_DoesNotGrantTicketsAgain()
        {
            var v1 = new GameSave { Version = 1, HeroShards = 3, Survivors = 4, Morale = 70, LastTick = 1 };
            var once = SaveMigrator.Migrate(v1, 10, TestData.Data);
            int tickets = once.RecruitTickets;
            var twice = SaveMigrator.Migrate(once, 20, TestData.Data);
            Assert.AreEqual(tickets, twice.RecruitTickets);
        }

        [Test]
        public void HeroesRemovedFromTable_AreDropped()
        {
            var s = SaveMigrator.NewGame(1, TestData.Data);
            s.Heroes.Add(new HeroSave { Id = "deleted_hero", Stars = 2 });
            s.ManagerHero = "deleted_hero";
            SaveMigrator.Migrate(s, 2, TestData.Data);
            Assert.IsFalse(s.Heroes.Any(h => h.Id == "deleted_hero"));
            Assert.IsNull(s.ManagerHero);
        }
    }
}
