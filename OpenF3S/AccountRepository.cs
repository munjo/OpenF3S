using Fortress3PaewangServerTest.Manager;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fortress3PaewangServerTest
{
    // 메모리에 상주하는 JSON 데이터를 다루는 전담 클래스입니다.
    internal static class AccountRepository
    {
        // 다중 접속 시 안전하게 데이터를 다루기 위한 Lock
        private static readonly object _repoLock = new object();

        // 0. 새로운 로그인 계정 생성 (서버 관리자용)
        public static bool RegisterLoginAccount(string loginId, string password)
        {
            lock (_repoLock)
            {
                // 이미 존재하는 아이디인지 검사
                if (DatabaseManager.Data.LoginAccounts.ContainsKey(loginId))
                {
                    return false;
                }

                var newAcc = new LoginAccountDto
                {
                    LoginId = loginId,
                    Password = password
                };

                DatabaseManager.Data.LoginAccounts.Add(loginId, newAcc);
                DatabaseManager.SaveDatabase(); // 파일에 즉시 저장
                return true;
            }
        }

        // 1. 로그인 인증 검증 (LoginServer 용)
        public static bool ValidateLogin(string loginId, string password)
        {
            lock (_repoLock)
            {
                if (DatabaseManager.Data.LoginAccounts.TryGetValue(loginId, out var acc))
                {
                    return acc.Password == password;
                }
                return false;
            }
        }

        public static void GetLoginAccount(string loginId, out string guildName, out int guildNameColor, out bool isFemale)
        {
            lock (_repoLock)
            {
                guildName = string.Empty;
                guildNameColor = 0;
                isFemale = false;

                if (DatabaseManager.Data.LoginAccounts.TryGetValue(loginId, out var acc))
                {
                    guildName = acc.GuildName;
                    guildNameColor = acc.GuildColor;
                    isFemale = acc.IsFemale;
                }
                return;
            }
        }

        // 2. 게임 아이디(캐릭터) 목록 불러오기 (GameServer 용)
        public static List<GameAccount> GetGameAccounts(string loginId)
        {
            var accounts = new List<GameAccount>();

            lock (_repoLock)
            {
                // 해당 로그인 계정에 속한 캐릭터들만 찾기
                var userGameAccounts = DatabaseManager.Data.GameAccounts.Values
                    .Where(a => a.LoginId == loginId)
                    .ToList();

                foreach (var dto in userGameAccounts)
                {
                    // 랭킹 매니저에게 동적으로 산정된 랭킹과 계급을 받아옴
                    var (rank, tier) = RankingManager.GetRankAndTier(dto.GameId);

                    var info = new GameAccount(dto.GameId)
                    {
                        GameTier = tier,
                        ServerScore = dto.ServerScore,
                        ServerRank = rank,
                        GameCount = dto.GameCount,
                        GameWins = dto.GameWins,
                        MyCring = dto.MyCring
                    };

                    // 세팅 값 불러오기
                    info.AccountSetting.DefaultRoomTitle = dto.DefaultRoomTitle;
                    info.AccountSetting.BGMVolume = dto.BGMVolume;
                    info.AccountSetting.SFXVolume = dto.SFXVolume;
                    info.AccountSetting.ScreenBrightness = dto.ScreenBrightness;
                    info.AccountSetting.IsHighGraphic = dto.IsHighGraphic;

                    for (int i = 0; i < 8 && i < dto.MacroChat.Length; i++)
                    {
                        info.AccountSetting.MacroChat[i] = dto.MacroChat[i];
                    }

                    accounts.Add(info);
                }
            }

            return accounts;
        }

        // 3. 새 캐릭터 생성
        public static bool CreateGameAccount(string loginId, string gameId)
        {
            lock (_repoLock)
            {
                // 닉네임 중복 검사
                if (DatabaseManager.Data.GameAccounts.ContainsKey(gameId))
                {
                    return false;
                }

                // 생성시 기본 정보 초기화
                var newGameAcc = new GameAccountDto
                {
                    GameId = gameId,
                    LoginId = loginId,
                };

                DatabaseManager.Data.GameAccounts.Add(gameId, newGameAcc);
                DatabaseManager.SaveDatabase();

                // RankingManager.UpdateRankings(); // 랭킹 즉시 갱신
                return true;
            }
        }

        // 4. 캐릭터 삭제
        public static bool DeleteGameAccount(string loginId, string gameId, string password)
        {
            // 삭제 전 계정 비밀번호가 맞는지 검증
            if (!ValidateLogin(loginId, password)) return false;

            lock (_repoLock)
            {
                if (DatabaseManager.Data.GameAccounts.TryGetValue(gameId, out var acc) && acc.LoginId == loginId)
                {
                    DatabaseManager.Data.GameAccounts.Remove(gameId);
                    DatabaseManager.SaveDatabase();
                    // RankingManager.UpdateRankings(); // 랭킹 즉시 갱신
                    return true;
                }
                return false;
            }
        }

        // 5. 캐릭터 설정 옵션(매크로 등) 업데이트
        public static void UpdateGameAccountSetting(GameAccount info)
        {
            lock (_repoLock)
            {
                if (DatabaseManager.Data.GameAccounts.TryGetValue(info.GameId, out var dto))
                {
                    for (int i = 0; i < 8; i++)
                    {
                        dto.MacroChat[i] = info.AccountSetting.MacroChat[i];
                    }
                    dto.DefaultRoomTitle = info.AccountSetting.DefaultRoomTitle;
                    dto.BGMVolume = info.AccountSetting.BGMVolume;
                    dto.SFXVolume = info.AccountSetting.SFXVolume;
                    dto.ScreenBrightness = info.AccountSetting.ScreenBrightness;
                    dto.IsHighGraphic = info.AccountSetting.IsHighGraphic;

                    DatabaseManager.SaveDatabase();
                }
            }
        }
    }
}