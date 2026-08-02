using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Fortress3PaewangServerTest
{
    // JSON 직렬화를 위한 데이터 모델 (테이블 역할)
    public class DbRoot
    {
        // Key: 로그인 아이디(LoginId)
        public Dictionary<string, LoginAccountDto> LoginAccounts { get; set; } = new Dictionary<string, LoginAccountDto>();
        // Key: 캐릭터 아이디(GameId)
        public Dictionary<string, GameAccountDto> GameAccounts { get; set; } = new Dictionary<string, GameAccountDto>();
    }

    public class LoginAccountDto
    {
        public string LoginId { get; set; }
        public string Password { get; set; }
        public string GuildName { get; set; } = "";
        public int GuildColor { get; set; } = 0;
        public bool IsFemale { get; set; } = false;
    }

    public class GameAccountDto
    {
        public string GameId { get; set; }
        public string LoginId { get; set; }
        public int ServerScore { get; set; } = 0;
        public int GameCount { get; set; } = 0;
        public int GameWins { get; set; } = 0;
        public int MyCring { get; set; } = 100000;

        // 게임 설정 (Setting)
        public string[] MacroChat { get; } = new string[] {
            "안녕하세요!", "수고하셨습니다! GG", "좋아요!", "어려워요...",
            "죄송합니다, 실수했네요...", "고마워요!", "에고... 아깝네요!", "나이스 샷!"
        };
        public string DefaultRoomTitle { get; set; } = "포트3 패왕전 하실분!!";
        public int BGMVolume { get; set; } = 100;
        public int SFXVolume { get; set; } = 100;
        public int ScreenBrightness { get; set; } = 100;
        public bool IsHighGraphic { get; set; } = true;
        public int QuickMathPlayer { get; set; } = 3;
        public int QuickMathStage { get; set; } = 0;
        public int QuickMathTierRestriction { get; set; } = 0;
    }

    internal static class DatabaseManager
    {
        private static readonly string dbFileName = "f3s_data.json";

        // 메모리에 상주하는 데이터베이스 객체
        public static DbRoot Data { get; private set; } = new DbRoot();

        // 파일 읽기/쓰기 충돌 방지용 Lock
        private static readonly object _dbLock = new object();

        public static void InitializeDatabase()
        {
            lock (_dbLock)
            {
                if (File.Exists(dbFileName))
                {
                    try
                    {
                        string json = File.ReadAllText(dbFileName);
                        Data = JsonConvert.DeserializeObject<DbRoot>(json) ?? new DbRoot();
                        Console.WriteLine("[DB] 기존 JSON 데이터베이스 파일(f3s_data.json)을 성공적으로 불러왔습니다.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[DB 오류] JSON 파일 읽기 실패: {ex.Message}");
                        Data = new DbRoot(); // 실패 시 빈 데이터로 초기화
                    }
                }
                else
                {
                    Console.WriteLine("[DB] 새로운 JSON 데이터베이스 파일(f3s_data.json)을 생성합니다.");
                    SaveDatabase();
                }
            }
        }

        // 메모리에 변경된 사항을 JSON 파일로 저장합니다.
        public static void SaveDatabase()
        {
            lock (_dbLock)
            {
                try
                {
                    // 보기 좋게 들여쓰기 설정
                    string json = JsonConvert.SerializeObject(Data, Formatting.Indented);
                    File.WriteAllText(dbFileName, json);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DB 오류] JSON 파일 저장 실패: {ex.Message}");
                }
            }
        }
    }
}