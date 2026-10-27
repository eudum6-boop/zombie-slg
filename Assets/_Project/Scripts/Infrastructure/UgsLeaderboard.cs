using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Leaderboards;
using ZombieSLG.Application;

namespace ZombieSLG.Infrastructure
{
    /// <summary>
    /// Unity Leaderboards. The board must exist in the dashboard with id <see cref="RankingService.LeaderboardId"/>,
    /// sort "Higher is better" and update type "Keep best".
    /// M3 later: route submissions through Cloud Code so the server checks the score.
    /// </summary>
    public sealed class UgsLeaderboard : ILeaderboard
    {
        private readonly UgsCloudSave _session;

        public UgsLeaderboard(UgsCloudSave session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public bool IsAvailable => _session.IsAvailable;

        public async Task SubmitAsync(int score)
        {
            await LeaderboardsService.Instance.AddPlayerScoreAsync(RankingService.LeaderboardId, score);
        }

        public async Task<RankPage> GetPageAsync(int limit)
        {
            string me = AuthenticationService.Instance.PlayerId;
            var page = new RankPage();
            var scores = await LeaderboardsService.Instance.GetScoresAsync(
                RankingService.LeaderboardId, new GetScoresOptions { Limit = limit });
            foreach (var e in scores.Results)
                page.Top.Add(Convert(e.Rank, e.PlayerName, e.Score, e.PlayerId == me));

            try
            {
                var mine = await LeaderboardsService.Instance.GetPlayerScoreAsync(RankingService.LeaderboardId);
                page.Me = Convert(mine.Rank, mine.PlayerName, mine.Score, true);
            }
            catch (Exception)
            {
                page.Me = null; // no score yet
            }
            return page;
        }

        private static RankEntry Convert(int zeroBasedRank, string name, double score, bool isMe) => new RankEntry
        {
            Rank = zeroBasedRank + 1,
            Name = ShortName(name),
            Score = (int)Math.Round(score),
            IsMe = isMe,
        };

        /// <summary>UGS auto names look like "BraveFox#1234"; the tag is noise in a list.</summary>
        private static string ShortName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "생존자";
            int hash = name.LastIndexOf('#');
            return hash > 0 ? name.Substring(0, hash) : name;
        }
    }
}
