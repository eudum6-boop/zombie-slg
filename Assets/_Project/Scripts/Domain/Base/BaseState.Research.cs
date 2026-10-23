using System;

namespace ZombieSLG.Domain
{
    public enum ResearchBlock
    {
        None = 0,
        UnknownResearch,
        NoLab,
        LabTooLow,
        RequiresOther,
        MaxLevel,
        Busy,
        NotEnoughResources,
    }

    /// <summary>Research at the lab: one project at a time; node tier is gated by lab level.</summary>
    public sealed partial class BaseState
    {
        public int LabLevel => HighestLevel(BuildingType.ResearchLab);
        public string ActiveResearch => _s.ResearchActive;
        public double ResearchRemaining(double now) => _s.ResearchActive == null ? 0 : Math.Max(0, _s.ResearchEnd - now);

        public int ResearchLevel(string id) => ResearchMath.Level(_s, id);

        /// <summary>Summed effect of completed research (reductions capped by research.max_reduction).</summary>
        public double ResearchBonus(ResearchEffect effect) => ResearchMath.Effect(_s, _data, effect);

        public ResearchBlock CanResearch(string id)
        {
            if (!_data.HasResearch(id)) return ResearchBlock.UnknownResearch;
            var def = _data.ResearchNode(id);
            int lab = LabLevel;
            if (lab <= 0) return ResearchBlock.NoLab;
            if (def.Tier > lab) return ResearchBlock.LabTooLow;
            if (def.Requires != null && ResearchLevel(def.Requires) <= 0) return ResearchBlock.RequiresOther;
            int target = ResearchLevel(id) + 1;
            if (target > def.MaxLevel) return ResearchBlock.MaxLevel;
            if (_s.ResearchActive != null) return ResearchBlock.Busy;
            if (!CanAfford(def.CostFor(target))) return ResearchBlock.NotEnoughResources;
            return ResearchBlock.None;
        }

        public ResearchBlock StartResearch(string id, double now)
        {
            Advance(now);
            var block = CanResearch(id);
            if (block != ResearchBlock.None) return block;
            var def = _data.ResearchNode(id);
            int target = ResearchLevel(id) + 1;
            Pay(def.CostFor(target));
            _s.ResearchActive = id;
            _s.ResearchEnd = now + def.SecondsFor(target);
            Events.Add(new BaseEvent(BaseEventKind.ResearchStarted, -1, target, id));
            return ResearchBlock.None;
        }

        private void CompleteResearch()
        {
            string id = _s.ResearchActive;
            _s.ResearchActive = null;
            ResearchSave row = null;
            foreach (var r in _s.Research) if (r.Id == id) { row = r; break; }
            if (row == null) { row = new ResearchSave { Id = id }; _s.Research.Add(row); }
            row.Level++;
            Events.Add(new BaseEvent(BaseEventKind.ResearchCompleted, -1, row.Level, id));
        }
    }
}
