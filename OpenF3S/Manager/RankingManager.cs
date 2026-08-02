using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fortress3PaewangServerTest.Manager
{
    internal class RankingManager
    {
        // Key: GameId (캐릭터명), Value: (Rank, Tier)
        private static ConcurrentDictionary<string, (int Rank, int Tier)> _rankCache = new ConcurrentDictionary<string, (int, int)>();

        // 백그라운드 갱신 태스크 시작
        public static void StartRankingUpdateLoop()
        {
            // 서버 시작 시 최초 1회 즉시 정렬
            UpdateRankings();

            // 백그라운드에서 1시간마다 갱신
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    // 1시간(3600000ms) 대기 후 업데이트
                    await Task.Delay(3600000);
                    UpdateRankings();
                }
            });
        }

        public static void UpdateRankings()
        {
            try
            {
                // 1. 모든 유저를 점수순으로 내림차순 정렬
                var sortedAccounts = DatabaseManager.Data.GameAccounts.Values
                    .OrderByDescending(a => a.ServerScore)
                    .ToList();

                int totalUsers = sortedAccounts.Count;
                if (totalUsers == 0) return;

                // 2. 순위와 계급 계산
                for (int i = 0; i < totalUsers; i++)
                {
                    int rank = i + 1;
                    int score = sortedAccounts[i].ServerScore;
                    int tier = CalculateTier(rank, score, totalUsers);

                    // 3. 캐시에 저장 (AddOrUpdate로 스레드 안전하게 덮어쓰기)
                    _rankCache.AddOrUpdate(sortedAccounts[i].GameId, (rank, tier), (key, oldValue) => (rank, tier));
                }

                Console.WriteLine($"[Ranking] 총 {totalUsers}명의 랭킹 및 계급 산정이 완료되었습니다.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Ranking 오류] 랭킹 갱신 실패: {ex.Message}");
            }
        }

        public static (int Rank, int Tier) GetRankAndTier(string gameId)
        {
            if (_rankCache.TryGetValue(gameId, out var data))
            {
                return data;
            }

            // 캐시에 없으면 신규 유저이거나 점수가 아직 산정되지 않음 (기본 해골 반환)
            return (0, 19);
        }

        // 원작 고증 혹은 자체 규칙에 따른 계급 계산 로직 (숫자가 낮을수록 높은 계급)
        // 0: 황금관, 1: 금관, 2: 은관 ... 19: 해골
        private static int CalculateTier(int rank, int score, int totalUsers)
        {
            // 1. 점수 커트라인 기반 (기본 점수 미만은 얄짤없이 해골)
            if (score < 1000) {
                return 19;
            }

            // 2. 등수 기반 (최상위권 절대 평가)
            if (rank == 1) { return 0; }  // 황금관 (1위)
            if (rank == 2) { return 1; }  // 금관 (2위)
            if (rank == 3) { return 2; }  // 은관 (3위)

            // 3. 비율 기반 (일반 유저 상대 평가)
            double percent = (double)rank / totalUsers * 100.0;
            // 훈장 급
            if (percent <= 1.0)
            {
                return 3;
            }
            if (percent <= 2.0) 
            {
                return 4;
            }
            if (percent <= 4.0)
            {
                return 5;
            }
            // 메달 급
            if (percent <= 6.0)
            {
                return 6;
            }
            if (percent <= 10.0)
            {
                return 7;
            }
            if (percent <= 15.0)
            {
                return 8;
            }
            // 별 급
            if (percent <= 20.0)
            {
                return 9;
            }
            if (percent <= 27.5)
            {
                return 10;
            }
            if (percent <= 35.0)
            {
                return 11;
            }
            if (percent <= 42.5)
            {
                return 12;
            }
            //  미사일 급
            if (percent <= 50.0)
            {
                return 13;
            }
            if (percent <= 60.0)
            {
                return 14;
            }
            if (percent <= 70.0)
            {
                return 15;
            }
            // 총알 급
            if (percent <= 80.0)
            {
                return 16;
            }
            if (percent <= 90.0)
            {
                return 17;
            }

            return 18; // 기본 동총알
        }
    }
}
