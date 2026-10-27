using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ZombieSLG.Application
{
    public sealed class RankEntry
    {
        /// <summary>1-based.</summary>
        public int Rank;
        public string Name;
        public int Score;
        public bool IsMe;
    }

    public sealed class RankPage
    {
        public readonly List<RankEntry> Top = new List<RankEntry>();
        /// <summary>The player's own entry, or null if they have no score yet.</summary>
        public RankEntry Me;
    }

    /// <summary>Remote leaderboard (Unity Leaderboards in production, a fake in tests).</summary>
    public interface ILeaderboard
    {
        bool IsAvailable { get; }
        Task SubmitAsync(int score);
        Task<RankPage> GetPageAsync(int limit);
    }

    /// <summary>
    /// "Longest survival" ranking: submits the best night once it improves (and once per session, to
    /// recover scores earned offline). Failures are kept and retried on the next submit.
    /// </summary>
    public sealed class RankingService
    {
        public const string LeaderboardId = "best_night";

        private readonly ILeaderboard _board;
        private readonly Action<string> _log;
        private int _submitted = -1;
        private int _pending = -1;
        private bool _busy;

        public bool IsOnline => _board.IsAvailable;
        public int LastSubmitted => _submitted;

        public RankingService(ILeaderboard board, Action<string> log = null)
        {
            _board = board ?? throw new ArgumentNullException(nameof(board));
            _log = log;
        }

        /// <summary>Queues a score if it beats what was already sent; sends it unless a send is in flight.</summary>
        public Task ReportAsync(int bestNight)
        {
            if (bestNight <= 0 || bestNight <= _submitted || bestNight <= _pending) return Task.CompletedTask;
            _pending = bestNight;
            return FlushAsync();
        }

        public async Task FlushAsync()
        {
            if (_busy || _pending <= _submitted || !_board.IsAvailable) return;
            int score = _pending;
            bool ok = false;
            _busy = true;
            try
            {
                await _board.SubmitAsync(score);
                _submitted = Math.Max(_submitted, score);
                ok = true;
            }
            catch (Exception e)
            {
                _log?.Invoke("순위 등록 실패: " + e.Message);
            }
            finally
            {
                _busy = false;
            }
            if (ok && _pending > _submitted) await FlushAsync(); // a better score arrived meanwhile
        }

        /// <summary>Null when offline or on error.</summary>
        public async Task<RankPage> LoadAsync(int limit = 20)
        {
            if (!_board.IsAvailable) return null;
            try
            {
                return await _board.GetPageAsync(limit);
            }
            catch (Exception e)
            {
                _log?.Invoke("순위 불러오기 실패: " + e.Message);
                return null;
            }
        }
    }
}
