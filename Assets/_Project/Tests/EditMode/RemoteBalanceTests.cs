using System.Collections.Generic;
using NUnit.Framework;
using ZombieSLG.Domain;

namespace ZombieSLG.Tests
{
    public class RemoteBalanceTests
    {
        [Test]
        public void Parse_ReadsFlatNumberObject()
        {
            var w = new List<string>();
            var o = RemoteBalance.Parse("{ \"night.hp_per_night\": 0.08, \"hq.max_hp\" : 3000, \"x\": -1.5e2 }", w);
            Assert.AreEqual(3, o.Count);
            Assert.AreEqual(0.08, o["night.hp_per_night"], 1e-12);
            Assert.AreEqual(3000, o["hq.max_hp"], 1e-12);
            Assert.AreEqual(-150, o["x"], 1e-12);
            Assert.AreEqual(0, w.Count);
        }

        [Test]
        public void Parse_AcceptsQuotedNumbers_SkipsOthers()
        {
            var w = new List<string>();
            var o = RemoteBalance.Parse("{\"a\":\"2.5\",\"b\":\"hello\",\"c\":true}", w);
            Assert.AreEqual(1, o.Count);
            Assert.AreEqual(2.5, o["a"], 1e-12);
            Assert.AreEqual(1, w.Count, string.Join(";", w));
        }

        [Test]
        public void Parse_EmptyOrGarbage_IsHarmless()
        {
            var w = new List<string>();
            Assert.AreEqual(0, RemoteBalance.Parse(null, w).Count);
            Assert.AreEqual(0, RemoteBalance.Parse("{}", w).Count);
            Assert.AreEqual(0, RemoteBalance.Parse("not json", w).Count);
            Assert.AreEqual(1, w.Count);
        }

        [Test]
        public void Build_AppliesOverridesToDerivedRules()
        {
            var errors = new List<string>();
            var w = new List<string>();
            var tables = TestData.Tables(errors);
            var data = RemoteBalance.Build(tables, "{\"night.hp_per_night\": 0.05, \"hq.max_hp\": 5000}", errors, w, out int applied);
            Assert.IsNotNull(data);
            Assert.AreEqual(2, applied);
            Assert.AreEqual(0.05f, data.Night.HpPerNight, 1e-6f);
            Assert.AreEqual(5000, data.Balance.Get("hq.max_hp"), 1e-9);
            Assert.AreEqual(0, w.Count, string.Join(";", w));
        }

        [Test]
        public void Build_DoesNotTouchTheShippedTables()
        {
            var errors = new List<string>();
            var tables = TestData.Tables(errors);
            double before = tables.Balance.Find(b => b.Key == "hq.max_hp").Value;
            RemoteBalance.Build(tables, "{\"hq.max_hp\": 1}", errors, new List<string>(), out _);
            Assert.AreEqual(before, tables.Balance.Find(b => b.Key == "hq.max_hp").Value, 1e-9);
        }

        [Test]
        public void Build_UnknownKeysAreReported()
        {
            var errors = new List<string>();
            var w = new List<string>();
            var data = RemoteBalance.Build(TestData.Tables(errors), "{\"zombie.laser\": 3}", errors, w, out int applied);
            Assert.IsNotNull(data);
            Assert.AreEqual(0, applied);
            Assert.IsTrue(w.Exists(m => m.Contains("zombie.laser")));
        }

        [Test]
        public void Build_InvalidServerValue_FallsBackToShippedData()
        {
            var errors = new List<string>();
            var w = new List<string>();
            // Gacha rates must sum to 1: a broken rate must not reach players.
            var data = RemoteBalance.Build(TestData.Tables(errors), "{\"gacha.rate_legendary\": 0.9}", errors, w, out int applied);
            Assert.IsNotNull(data, "game still starts");
            Assert.AreEqual(0, applied);
            Assert.AreEqual(0, errors.Count);
            Assert.IsTrue(w.Exists(m => m.Contains("거부")), string.Join(";", w));
            Assert.AreEqual(TestData.Data.Gacha.RarityRates[3], data.Gacha.RarityRates[3], 1e-12);
        }
    }
}
