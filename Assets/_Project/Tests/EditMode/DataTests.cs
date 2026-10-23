using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ZombieSLG.Domain;

namespace ZombieSLG.Tests
{
    public class CsvTests
    {
        private static CsvTable Parse(string text, List<string> errors) => CsvTable.Parse("t.csv", text, errors);

        [Test]
        public void HeaderCommentsBlankLinesAndBom()
        {
            var errors = new List<string>();
            var t = Parse("﻿# comment\n\nid,hp\r\n# another\nWalker,30\r\n\r\nRunner,18\n", errors);
            Assert.AreEqual(0, errors.Count, string.Join("; ", errors));
            Assert.AreEqual(2, t.Count);
            Assert.AreEqual("Walker", t.Records[0].Str("id"));
            Assert.AreEqual(18.0, t.Records[1].Num("hp", errors), 1e-9);
            Assert.AreEqual(5, t.Records[0].Line);
        }

        [Test]
        public void QuotedFieldsWithCommasQuotesAndNewlines()
        {
            var errors = new List<string>();
            var t = Parse("id,desc\nA,\"one, two\"\nB,\"say \"\"hi\"\"\"\nC,\"line1\nline2\"\nD,x\n", errors);
            Assert.AreEqual(4, t.Count);
            Assert.AreEqual("one, two", t.Records[0].Str("desc"));
            Assert.AreEqual("say \"hi\"", t.Records[1].Str("desc"));
            Assert.AreEqual("line1\nline2", t.Records[2].Str("desc"));
            Assert.AreEqual(6, t.Records[3].Line, "line numbers account for the embedded newline");
        }

        [Test]
        public void ColumnNamesAreCaseInsensitive()
        {
            var errors = new List<string>();
            var t = Parse("ID,Hp\nWalker,30\n", errors);
            Assert.AreEqual("Walker", t.Records[0].Str("id"));
            Assert.AreEqual(30.0, t.Records[0].Num("hp", errors), 1e-9);
        }

        [Test]
        public void BadNumbersReportFileLineAndColumn()
        {
            var errors = new List<string>();
            var t = Parse("id,hp\nWalker,abc\nRunner,\n", errors);
            t.Records[0].Num("hp", errors);
            t.Records[1].Num("hp", errors);
            t.Records[0].Num("speed", errors);
            Assert.AreEqual(3, errors.Count);
            Assert.IsTrue(errors[0].Contains("t.csv:2") && errors[0].Contains("abc"), errors[0]);
            Assert.IsTrue(errors[1].Contains("t.csv:3"), errors[1]);
            Assert.IsTrue(errors[2].Contains("speed"), errors[2]);
        }

        [Test]
        public void NumbersUseInvariantCulture()
        {
            var errors = new List<string>();
            var t = Parse("k,v\na,1.5\n", errors);
            Assert.AreEqual(1.5, t.Records[0].Num("v", errors), 1e-9);
            Assert.AreEqual(0, errors.Count);
        }
    }

    public class GameDataTests
    {
        [Test]
        public void ShippedTables_AreValid()
        {
            var errors = new List<string>();
            var data = GameDataBuilder.Build(TestData.Tables(errors), errors);
            Assert.IsTrue(data != null && errors.Count == 0, string.Join("\n", errors));
        }

        [Test]
        public void ShippedTables_CoverEveryTypeAndAreSane()
        {
            var d = TestData.Data;
            foreach (ZombieType t in System.Enum.GetValues(typeof(ZombieType)))
                Assert.Greater(d.Zombie(t).MaxHp, 0f);
            foreach (BuildingType t in System.Enum.GetValues(typeof(BuildingType)))
                Assert.Greater(d.Building(t).MaxLevel, 0);
            Assert.IsTrue(d.Building(BuildingType.Farm).IsProducer);
            Assert.AreEqual(ResourceType.Food, d.Building(BuildingType.Farm).Produces);
            Assert.IsFalse(d.Building(BuildingType.Shelter).IsProducer);
            Assert.Greater(d.Wave.Hordes.Length, 0);
        }

        [Test]
        public void EveryBalanceKeyIsUsed()
        {
            // Catches leftovers after renaming a key in code but not in balance.csv.
            var errors = new List<string>();
            var tables = TestData.Tables(errors);
            var known = new HashSet<string>(GameDataBuilder.PresentationKeys);
            foreach (var row in tables.Balance)
            {
                var trimmed = new GameTables { Zombies = tables.Zombies, Buildings = tables.Buildings, Hordes = tables.Hordes };
                trimmed.Balance = tables.Balance.Where(r => r.Key != row.Key).ToList();
                var e = new List<string>();
                GameDataBuilder.Build(trimmed, e);
                Assert.IsTrue(e.Any(m => m.Contains(row.Key)), $"balance.csv key '{row.Key}' is never read");
            }
        }

        [Test]
        public void MissingRowsAndUnknownIdsAreReported()
        {
            var errors = new List<string>();
            var tables = TestData.Tables(errors);
            tables.Zombies.RemoveAll(z => z.Id == "Brute");
            tables.Buildings[0].Id = "Castle";
            tables.Balance.RemoveAll(b => b.Key == "tower.damage");

            var e = new List<string>();
            Assert.IsNull(GameDataBuilder.Build(tables, e));
            Assert.IsTrue(e.Any(m => m.Contains("Brute")), "missing zombie");
            Assert.IsTrue(e.Any(m => m.Contains("Castle")), "unknown building id");
            Assert.IsTrue(e.Any(m => m.Contains("tower.damage")), "missing balance key");
        }

        [Test]
        public void TroopAndResearchProblemsAreReported()
        {
            var errors = new List<string>();
            var tables = TestData.Tables(errors);
            tables.Troops.RemoveAll(t => t.Id == "Vehicle");
            tables.Research.Find(r => r.Id == "eco_salvage").Requires = "eco_magic";
            tables.Research.Find(r => r.Id == "mil_drill").Effect = "laser_damage";
            tables.Research.Find(r => r.Id == "def_tactics").Branch = "Navy";

            var e = new List<string>();
            Assert.IsNull(GameDataBuilder.Build(tables, e));
            Assert.IsTrue(e.Any(m => m.Contains("Vehicle")), "missing troop");
            Assert.IsTrue(e.Any(m => m.Contains("eco_magic")), "unknown prerequisite");
            Assert.IsTrue(e.Any(m => m.Contains("laser_damage")), "unknown effect");
            Assert.IsTrue(e.Any(m => m.Contains("Navy")), "unknown branch");
        }

        [Test]
        public void ResearchCycleIsReported()
        {
            var errors = new List<string>();
            var tables = TestData.Tables(errors);
            tables.Research.Find(r => r.Id == "eco_farming").Requires = "eco_salvage";
            tables.Research.Find(r => r.Id == "eco_farming").Tier = 2;

            var e = new List<string>();
            Assert.IsNull(GameDataBuilder.Build(tables, e));
            Assert.IsTrue(e.Any(m => m.Contains("순환")), string.Join("\n", e));
        }

        [Test]
        public void OutOfRangeAndUnsortedValuesAreReported()
        {
            var errors = new List<string>();
            var tables = TestData.Tables(errors);
            tables.Balance.First(b => b.Key == "economy.morale_floor").Value = 1.5;
            tables.Hordes.Reverse();

            var e = new List<string>();
            Assert.IsNull(GameDataBuilder.Build(tables, e));
            Assert.IsTrue(e.Any(m => m.Contains("economy.morale_floor")));
            Assert.IsTrue(e.Any(m => m.Contains(GameTables.HordesFile)));
        }

        [Test]
        public void BalanceOverrides_OnlyReplaceKnownKeys()
        {
            var d = TestData.Fresh();
            int applied = d.Balance.ApplyOverrides(new[]
            {
                new KeyValuePair<string, double>("tower.damage", 99),
                new KeyValuePair<string, double>("not.a.key", 1),
            });
            Assert.AreEqual(1, applied);
            Assert.AreEqual(99.0, d.Balance.Get("tower.damage"), 1e-9);
            Assert.IsFalse(d.Balance.Has("not.a.key"));
        }

        [Test]
        public void ChangingATableValueChangesTheRule()
        {
            var errors = new List<string>();
            var tables = TestData.Tables(errors);
            tables.Balance.First(b => b.Key == "economy.storage_base").Value = 5000;
            var d = GameDataBuilder.Build(tables, errors);
            Assert.AreEqual(5000, d.Economy.StorageCap(1));
        }
    }
}
