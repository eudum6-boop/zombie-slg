using System;

namespace ZombieSLG.Domain
{
    /// <summary>First-session guide. Order matters: each step waits for one signal.</summary>
    public enum TutorialStep
    {
        Welcome = 0,
        SelectHq = 1,
        UpgradeHq = 2,
        AssignWorker = 3,
        StartNight = 4,
        MoveHero = 5,
        CastSkill = 6,
        SurviveNight = 7,
        Recruit = 8,
        Done = 9,
    }

    /// <summary>Things the game reports to the tutorial.</summary>
    public enum TutorialSignal
    {
        Next,
        PlotSelected,
        UpgradeStarted,
        WorkersChanged,
        NightStarted,
        HeroMoved,
        SkillCast,
        NightEnded,
        Recruited,
        Skip,
    }

    /// <summary>Player inputs the tutorial can hold back while it waits for a specific one.</summary>
    public enum TutorialAction
    {
        SelectPlot,
        ClosePanel,
        Upgrade,
        ChangeWorkers,
        BuildingAction,
        OpenHeroes,
        OpenRecruit,
        Recruit,
        OpenRanking,
        StartNight,
        HeroMove,
        Skill,
        DebugKey,
    }

    /// <summary>
    /// Tutorial progress as a tiny state machine over <see cref="GameSave.TutorialStep"/>. Pure rules,
    /// so the order and the "which signal completes which step" table are unit-tested.
    /// </summary>
    public sealed class Tutorial
    {
        private readonly GameSave _s;

        public Tutorial(GameSave save) => _s = save ?? throw new ArgumentNullException(nameof(save));

        public TutorialStep Step => _s.TutorialDone ? TutorialStep.Done : (TutorialStep)Math.Max(0, Math.Min(_s.TutorialStep, (int)TutorialStep.Done));
        public bool IsActive => Step != TutorialStep.Done;

        /// <summary>Night steps are shown during battle; the rest only by day.</summary>
        public static bool IsBattleStep(TutorialStep step) =>
            step == TutorialStep.MoveHero || step == TutorialStep.CastSkill || step == TutorialStep.SurviveNight;

        /// <summary>Plot the step points at (BasePlots id), or -1.</summary>
        public static int TargetPlot(TutorialStep step)
        {
            switch (step)
            {
                case TutorialStep.SelectHq:
                case TutorialStep.UpgradeHq: return BasePlots.Headquarters;
                case TutorialStep.AssignWorker: return BasePlots.Farm;
                default: return -1;
            }
        }

        /// <summary>
        /// Feeds a signal. Returns true when it completed the current step (so the UI can react).
        /// <paramref name="plotId"/> is only used with <see cref="TutorialSignal.PlotSelected"/>.
        /// </summary>
        public bool Signal(TutorialSignal signal, int plotId = -1)
        {
            if (!IsActive) return false;
            if (signal == TutorialSignal.Skip)
            {
                Finish();
                return true;
            }
            if (!Completes(Step, signal, plotId)) return false;
            Advance();
            return true;
        }

        private static bool Completes(TutorialStep step, TutorialSignal signal, int plotId)
        {
            switch (step)
            {
                case TutorialStep.Welcome: return signal == TutorialSignal.Next;
                case TutorialStep.SelectHq: return signal == TutorialSignal.PlotSelected && plotId == BasePlots.Headquarters;
                // Next: the UI offers it only when the upgrade is blocked (no resources, queue busy, max level).
                case TutorialStep.UpgradeHq: return signal == TutorialSignal.UpgradeStarted || signal == TutorialSignal.Next;
                case TutorialStep.AssignWorker: return signal == TutorialSignal.WorkersChanged || signal == TutorialSignal.Next;
                case TutorialStep.StartNight: return signal == TutorialSignal.NightStarted;
                case TutorialStep.MoveHero: return signal == TutorialSignal.HeroMoved;
                case TutorialStep.CastSkill: return signal == TutorialSignal.SkillCast;
                case TutorialStep.SurviveNight: return signal == TutorialSignal.NightEnded;
                case TutorialStep.Recruit: return signal == TutorialSignal.Recruited || signal == TutorialSignal.Next;
                default: return false;
            }
        }

        /// <summary>
        /// Whether an input is allowed right now. While the guide runs, only what the current step asks for
        /// (plus closing panels where that helps) goes through, so stray taps can't derail it.
        /// <paramref name="plotId"/> is the building involved, for plot-specific actions.
        /// </summary>
        public bool Allows(TutorialAction action, int plotId = -1)
        {
            if (!IsActive) return true;
            switch (Step)
            {
                case TutorialStep.Welcome:
                    return false;
                case TutorialStep.SelectHq:
                    return action == TutorialAction.SelectPlot && plotId == BasePlots.Headquarters;
                case TutorialStep.UpgradeHq:
                    return (action == TutorialAction.SelectPlot || action == TutorialAction.Upgrade) && plotId == BasePlots.Headquarters
                           || action == TutorialAction.ClosePanel;
                case TutorialStep.AssignWorker:
                    return (action == TutorialAction.SelectPlot || action == TutorialAction.ChangeWorkers) && plotId == BasePlots.Farm
                           || action == TutorialAction.ClosePanel;
                case TutorialStep.StartNight:
                    return action == TutorialAction.StartNight || action == TutorialAction.ClosePanel;
                case TutorialStep.MoveHero:
                    return action == TutorialAction.HeroMove;
                case TutorialStep.CastSkill:
                case TutorialStep.SurviveNight:
                    return action == TutorialAction.HeroMove || action == TutorialAction.Skill;
                case TutorialStep.Recruit:
                    return action == TutorialAction.OpenRecruit || action == TutorialAction.Recruit || action == TutorialAction.ClosePanel;
                default:
                    return true;
            }
        }

        /// <summary>
        /// The night can end before the player tries the hero or the skill; never leave the guide stuck on a
        /// battle step once back at base.
        /// </summary>
        public void OnReturnedToBase()
        {
            if (IsActive && IsBattleStep(Step)) _s.TutorialStep = (int)TutorialStep.Recruit;
        }

        /// <summary>
        /// The game always starts by day. If the app was closed during the practice night, go back to the
        /// "start the night" step instead of waiting for a battle input that can't happen.
        /// </summary>
        public void OnSessionStart()
        {
            if (IsActive && IsBattleStep(Step)) _s.TutorialStep = (int)TutorialStep.StartNight;
        }

        private void Advance()
        {
            int next = (int)Step + 1;
            if (next >= (int)TutorialStep.Done) Finish();
            else _s.TutorialStep = next;
        }

        private void Finish()
        {
            _s.TutorialStep = (int)TutorialStep.Done;
            _s.TutorialDone = true;
        }
    }
}
