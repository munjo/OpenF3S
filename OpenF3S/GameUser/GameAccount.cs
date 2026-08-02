using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fortress3PaewangServerTest
{
    internal class GameAccount
    {
        public string GameId { get; set; }
        public int GameTier { get; set; } // 5비트 서버 계급은 점수를 통해 계산하는 식으로 변경해야함
        //public int UnkValue1 { get; set; } // 4비트
        //public int UnkValue2 { get; set; } // 3비트

        //public int UnkValue3 { get; set; } // 4바이트
        //public int UnkValue4 { get; set; } // 4바이트

        public int ServerScore { get; set; } // 4바이트
        public int ServerRank { get; set; } // 4바이트 서버 랭킹은 점수를 통해 계산하는 식으로 변경해야함
        public int GameCount { get; set; } // 4바이트
        public int GameWins { get; set; } // 4바이트
        //public int UnkValue5 { get; set; } // 4바이트
        //public int UnkValue6 { get; set; } // 4바이트
        public int MyCring { get; set; } // 4바이트

        public GameAccountSetting AccountSetting { get; }

        public GameAccount(string gameId)
        {
            GameId = gameId;
            AccountSetting = new GameAccountSetting();
        }
    }
}
