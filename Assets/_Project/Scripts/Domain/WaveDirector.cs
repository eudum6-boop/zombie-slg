using System;
using System.Collections.Generic;

namespace ZombieSLG.Domain
{
    public enum SpawnSide
    {
        North = 0,
        East = 1,
        West = 2,
    }

    public readonly struct SpawnRequest
    {
        public readonly ZombieType Type;
        public readonly SpawnSide Side;
        public readonly bool IsHorde;

        public SpawnRequest(ZombieType type, SpawnSide side, bool isHorde)
        {
            Type = type;
            Side = side;
            IsHorde = isHorde;
        }
    }

    public readonly struct HordeEvent
    {
        public readonly float Time;
        public readonly int Count;

        public HordeEvent(float time, int count)
        {
            Time = time;
            Count = count;
        }
    }

    /// <summary>Tunables for one night. Defaults are the M0 target values from the GDD (3-minute night).</summary>
    public sealed class WaveSettings
    {
        public float Duration = 180f;
        public float BaseRate = 1.3f;     // spawns per second at t=0
        public float PeakRate = 5.0f;     // spawns per second at RampEnd
        public float RampEnd = 145f;
        public float RunnerUnlockTime = 25f;
        public float BruteUnlockTime = 60f;
        public float RunnerWeight = 0.27f;
        public float BruteWeight = 0.08f;
        public int MaxAlive = 360;

        public HordeEvent[] Hordes =
        {
            new HordeEvent(50f, 30),
            new HordeEvent(105f, 45),
            new HordeEvent(150f, 60),
            new HordeEvent(172f, 75),
        };
    }

    /// <summary>
    /// Decides what spawns and when. Deterministic for a given seed so the same night can be replayed
    /// in tests (and later validated on the server).
    /// </summary>
    public sealed class WaveDirector
    {
        private readonly WaveSettings _settings;
        private readonly Random _rng;
        private readonly SpawnSide[] _hordeSides;
        private int _nextHorde;
        private double _budget;

        public int Night { get; }
        public float Elapsed { get; private set; }
        public float Duration => _settings.Duration;
        public float Remaining => Math.Max(0f, _settings.Duration - Elapsed);
        public bool IsFinished => Elapsed >= _settings.Duration;
        public int TotalSpawned { get; private set; }
        public int HordesTriggered => _nextHorde;

        public WaveDirector(WaveSettings settings, int night, int seed)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Night = NightScaling.Clamp(night);
            _rng = new Random(seed);

            int hordeCount = settings.Hordes?.Length ?? 0;
            _hordeSides = new SpawnSide[hordeCount];
            for (int i = 0; i < hordeCount; i++) _hordeSides[i] = (SpawnSide)_rng.Next(0, 3);
        }

        public float CurrentRate
        {
            get
            {
                float t = _settings.RampEnd <= 0f ? 1f : Math.Min(1f, Elapsed / _settings.RampEnd);
                float rate = _settings.BaseRate + (_settings.PeakRate - _settings.BaseRate) * t;
                return rate * NightScaling.SpawnRateMultiplier(Night);
            }
        }

        /// <summary>Advances time and appends spawn requests. Returns the number appended.</summary>
        public int Tick(float dt, int aliveCount, List<SpawnRequest> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (IsFinished || dt <= 0f) return 0;

            Elapsed = Math.Min(_settings.Duration, Elapsed + dt);
            int added = 0;
            int room = Math.Max(0, _settings.MaxAlive - aliveCount);

            // Hordes first: they are the dramatic beats and should not be starved by the trickle.
            var hordes = _settings.Hordes;
            while (hordes != null && _nextHorde < hordes.Length && Elapsed >= hordes[_nextHorde].Time)
            {
                int count = (int)Math.Round(hordes[_nextHorde].Count * NightScaling.HordeSizeMultiplier(Night));
                SpawnSide side = _hordeSides[_nextHorde];
                for (int i = 0; i < count && room > 0; i++, room--)
                {
                    output.Add(new SpawnRequest(PickType(), side, true));
                    added++;
                }
                _nextHorde++;
            }

            _budget += CurrentRate * dt;
            while (_budget >= 1.0 && room > 0)
            {
                output.Add(new SpawnRequest(PickType(), PickSide(), false));
                _budget -= 1.0;
                room--;
                added++;
            }
            if (room == 0 && _budget > 1.0) _budget = 1.0; // don't bank a burst while capped

            TotalSpawned += added;
            return added;
        }

        /// <summary>True when the next horde starts within <paramref name="window"/> seconds.</summary>
        public bool TryGetUpcomingHorde(float window, out float secondsLeft, out SpawnSide side)
        {
            secondsLeft = 0f;
            side = SpawnSide.North;
            var hordes = _settings.Hordes;
            if (hordes == null || _nextHorde >= hordes.Length) return false;

            float s = hordes[_nextHorde].Time - Elapsed;
            if (s <= 0f || s > window) return false;
            secondsLeft = s;
            side = _hordeSides[_nextHorde];
            return true;
        }

        private ZombieType PickType()
        {
            double r = _rng.NextDouble();
            float brute = Elapsed >= _settings.BruteUnlockTime ? _settings.BruteWeight : 0f;
            float runner = Elapsed >= _settings.RunnerUnlockTime ? _settings.RunnerWeight : 0f;
            if (r < brute) return ZombieType.Brute;
            if (r < brute + runner) return ZombieType.Runner;
            return ZombieType.Walker;
        }

        private SpawnSide PickSide()
        {
            double r = _rng.NextDouble();
            if (r < 0.6) return SpawnSide.North;
            return r < 0.8 ? SpawnSide.East : SpawnSide.West;
        }
    }
}
