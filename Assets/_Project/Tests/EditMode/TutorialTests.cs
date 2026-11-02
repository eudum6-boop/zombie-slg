using System.Collections.Generic;
using NUnit.Framework;
using ZombieSLG.Domain;

namespace ZombieSLG.Tests
{
    public class TutorialTests
    {
        private static Tutorial Fresh(out GameSave save)
        {
            save = SaveMigrator.NewGame(1000, TestData.Data);
            return new Tutorial(save);
        }

        [Test]
        public void NewGame_StartsAtWelcome()
        {
            var t = Fresh(out _);
            Assert.IsTrue(t.IsActive);
            Assert.AreEqual(TutorialStep.Welcome, t.Step);
        }

        [Test]
        public void FullPath_InOrder()
        {
            var t = Fresh(out var save);
            Assert.IsTrue(t.Signal(TutorialSignal.Next));
            Assert.IsFalse(t.Signal(TutorialSignal.PlotSelected, BasePlots.Farm), "wrong building");
            Assert.IsTrue(t.Signal(TutorialSignal.PlotSelected, BasePlots.Headquarters));
            Assert.IsTrue(t.Signal(TutorialSignal.UpgradeStarted));
            Assert.IsTrue(t.Signal(TutorialSignal.WorkersChanged));
            Assert.IsFalse(t.Signal(TutorialSignal.HeroMoved), "out of order");
            Assert.IsTrue(t.Signal(TutorialSignal.NightStarted));
            Assert.IsTrue(t.Signal(TutorialSignal.HeroMoved));
            Assert.IsTrue(t.Signal(TutorialSignal.SkillCast));
            Assert.IsTrue(t.Signal(TutorialSignal.NightEnded));
            Assert.AreEqual(TutorialStep.Recruit, t.Step);
            Assert.IsTrue(t.Signal(TutorialSignal.Recruited));
            Assert.IsFalse(t.IsActive);
            Assert.IsTrue(save.TutorialDone);
        }

        [Test]
        public void Inputs_AreGatedToTheCurrentStep()
        {
            var t = Fresh(out var save);
            Assert.IsFalse(t.Allows(TutorialAction.SelectPlot, BasePlots.Headquarters), "welcome: only the coach buttons");

            save.TutorialStep = (int)TutorialStep.SelectHq;
            Assert.IsTrue(t.Allows(TutorialAction.SelectPlot, BasePlots.Headquarters));
            Assert.IsFalse(t.Allows(TutorialAction.SelectPlot, BasePlots.Farm));
            Assert.IsFalse(t.Allows(TutorialAction.OpenRecruit));
            Assert.IsFalse(t.Allows(TutorialAction.StartNight));

            save.TutorialStep = (int)TutorialStep.UpgradeHq;
            Assert.IsTrue(t.Allows(TutorialAction.Upgrade, BasePlots.Headquarters));
            Assert.IsFalse(t.Allows(TutorialAction.Upgrade, BasePlots.Farm));
            Assert.IsTrue(t.Allows(TutorialAction.ClosePanel));

            save.TutorialStep = (int)TutorialStep.AssignWorker;
            Assert.IsTrue(t.Allows(TutorialAction.ChangeWorkers, BasePlots.Farm));
            Assert.IsFalse(t.Allows(TutorialAction.ChangeWorkers, BasePlots.Workshop));

            save.TutorialStep = (int)TutorialStep.MoveHero;
            Assert.IsTrue(t.Allows(TutorialAction.HeroMove));
            Assert.IsFalse(t.Allows(TutorialAction.Skill));
            Assert.IsFalse(t.Allows(TutorialAction.DebugKey));

            save.TutorialStep = (int)TutorialStep.CastSkill;
            Assert.IsTrue(t.Allows(TutorialAction.Skill));

            t.Signal(TutorialSignal.Skip);
            Assert.IsTrue(t.Allows(TutorialAction.DebugKey), "everything opens once done");
        }

        [Test]
        public void PracticeNight_IsShortAndGentle()
        {
            var d = TestData.Data;
            var tw = d.TutorialNight.Wave;
            Assert.Less(tw.Duration, d.Wave.Duration);
            Assert.Greater(tw.RunnerUnlockTime, tw.Duration, "walkers only");
            Assert.Greater(tw.BruteUnlockTime, tw.Duration, "walkers only");
            Assert.LessOrEqual(tw.MaxAlive, d.Wave.MaxAlive);
            Assert.AreNotSame(d.Wave.Hordes, tw.Hordes, "normal night untouched");

            var director = new WaveDirector(tw, d.Night, 1, 42);
            var sink = new List<SpawnRequest>();
            while (!director.IsFinished) director.Tick(0.25f, 0, sink);
            Assert.IsTrue(sink.TrueForAll(r => r.Type == ZombieType.Walker));
            Assert.Less(director.TotalSpawned, ServerRules.MaxSpawns(d, 1));
        }

        [Test]
        public void QuitDuringPracticeNight_ResumesAtStartNight()
        {
            var t = Fresh(out var save);
            save.TutorialStep = (int)TutorialStep.CastSkill;
            t.OnSessionStart();
            Assert.AreEqual(TutorialStep.StartNight, t.Step);
            Assert.IsTrue(t.Allows(TutorialAction.StartNight));
        }

        [Test]
        public void Skip_EndsImmediately()
        {
            var t = Fresh(out var save);
            Assert.IsTrue(t.Signal(TutorialSignal.Skip));
            Assert.IsFalse(t.IsActive);
            Assert.IsTrue(save.TutorialDone);
            Assert.IsFalse(t.Signal(TutorialSignal.Next));
        }

        [Test]
        public void NightEndingEarly_DoesNotStrandBattleSteps()
        {
            var t = Fresh(out var save);
            save.TutorialStep = (int)TutorialStep.MoveHero;
            t.OnReturnedToBase();
            Assert.AreEqual(TutorialStep.Recruit, t.Step);
        }

        [Test]
        public void OptionalSteps_CanBeAcknowledged()
        {
            var t = Fresh(out var save);
            save.TutorialStep = (int)TutorialStep.UpgradeHq;
            Assert.IsTrue(t.Signal(TutorialSignal.Next), "blocked upgrade (no resources) → move on");
            save.TutorialStep = (int)TutorialStep.AssignWorker;
            Assert.IsTrue(t.Signal(TutorialSignal.Next), "no idle survivor → player can move on");
            save.TutorialStep = (int)TutorialStep.Recruit;
            Assert.IsTrue(t.Signal(TutorialSignal.Next), "no tickets → player can finish");
        }

        [Test]
        public void ExistingPlayers_SkipTutorialOnMigration()
        {
            var old = new GameSave { Version = 3, Night = 5, BestNight = 4, Survivors = 4, Morale = 70, LastTick = 1 };
            SaveMigrator.Migrate(old, 2, TestData.Data);
            Assert.IsTrue(old.TutorialDone);

            var fresh = new GameSave { Version = 3, Night = 1, Survivors = 4, Morale = 70, LastTick = 1 };
            SaveMigrator.Migrate(fresh, 2, TestData.Data);
            Assert.IsFalse(fresh.TutorialDone, "a v3 player still on night 1 gets the guide");
        }

        [Test]
        public void StarterResources_CanAffordTheTutorialUpgrade()
        {
            var save = SaveMigrator.NewGame(1000, TestData.Data);
            var s = new BaseState(save, TestData.Data);
            Assert.AreEqual(UpgradeBlock.None, s.CanUpgrade(BasePlots.Headquarters));
            Assert.Greater(s.IdleSurvivors, 0, "AssignWorker step needs an idle survivor");
        }
    }
}
