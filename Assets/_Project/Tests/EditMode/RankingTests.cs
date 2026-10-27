using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using ZombieSLG.Application;

namespace ZombieSLG.Tests
{
    public class RankingTests
    {
        private sealed class FakeBoard : ILeaderboard
        {
            public bool IsAvailable { get; set; } = true;
            public readonly List<int> Sent = new List<int>();
            public int FailCount;
            public TaskCompletionSource<bool> Gate;

            public async Task SubmitAsync(int score)
            {
                if (Gate != null) await Gate.Task;
                if (FailCount > 0) { FailCount--; throw new Exception("net"); }
                Sent.Add(score);
            }

            public Task<RankPage> GetPageAsync(int limit)
            {
                var p = new RankPage();
                p.Top.Add(new RankEntry { Rank = 1, Name = "A", Score = 9 });
                return Task.FromResult(p);
            }
        }

        [Test]
        public void OnlyImprovementsAreSent()
        {
            var board = new FakeBoard();
            var r = new RankingService(board);
            r.ReportAsync(3).Wait();
            r.ReportAsync(3).Wait();
            r.ReportAsync(2).Wait();
            r.ReportAsync(5).Wait();
            r.ReportAsync(0).Wait();
            CollectionAssert.AreEqual(new[] { 3, 5 }, board.Sent);
        }

        [Test]
        public void Failure_IsRetriedOnNextReport_WithoutLooping()
        {
            var board = new FakeBoard { FailCount = 5 };
            var r = new RankingService(board, _ => { });
            r.ReportAsync(4).Wait(); // fails once, must not spin
            Assert.AreEqual(4, board.FailCount);
            Assert.AreEqual(-1, r.LastSubmitted);

            board.FailCount = 0;
            r.FlushAsync().Wait();
            CollectionAssert.AreEqual(new[] { 4 }, board.Sent);
        }

        [Test]
        public void BetterScoreDuringSend_IsSentAfterwards()
        {
            var board = new FakeBoard { Gate = new TaskCompletionSource<bool>() };
            var r = new RankingService(board);
            var first = r.ReportAsync(2);
            var second = r.ReportAsync(6); // first still in flight
            board.Gate.SetResult(true);
            Task.WaitAll(first, second);
            CollectionAssert.AreEqual(new[] { 2, 6 }, board.Sent);
            Assert.AreEqual(6, r.LastSubmitted);
        }

        [Test]
        public void Offline_SendsNothing_AndLoadReturnsNull()
        {
            var board = new FakeBoard { IsAvailable = false };
            var r = new RankingService(board);
            r.ReportAsync(7).Wait();
            Assert.AreEqual(0, board.Sent.Count);
            Assert.IsNull(r.LoadAsync().Result);

            board.IsAvailable = true;
            r.FlushAsync().Wait();
            CollectionAssert.AreEqual(new[] { 7 }, board.Sent, "queued score goes out once online");
            Assert.AreEqual(1, r.LoadAsync().Result.Top.Count);
        }
    }
}
