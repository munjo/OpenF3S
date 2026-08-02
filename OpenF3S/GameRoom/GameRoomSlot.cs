using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static System.Collections.Specialized.BitVector32;

namespace Fortress3PaewangServerTest
{
    internal class GameRoomSlot
    {
        public bool IsHost { get; set; } // 방장인지 아닌지 여부

        public int Index { get; set; }

        public ClientSession Session { get; } // 유저의 소켓과 정보가 담긴 세션

        public int Team { get; set; }

        public int Tank { get; set; }
        public UserState UserState { get; set; }
        public bool IsBot { get; set; }

        public int Delay { get; set; }

        // 해당 유저가 마지막으로 보고한 턴 카운트 번호 (80번 패킷 동기화용)
        public int LastReportedTurnCount { get; set; }

        // 이번 라운드(사이클)에서 턴을 소모했는지 여부
        public bool IsTurnUsed { get; set; }

        public GameRoomSlot(ClientSession session, int index)
        {
            Session = session;
            Index = index;
        }

        public void StateChange(int slotState, int teamIndex, int tankIndex)
        {
            if (!IsHost)
            {
                if (slotState == 1)
                {
                    UserState = UserState.Idle;
                }
                else if (slotState == 3)
                {
                    UserState = UserState.Ready;
                }
            }

            // 대기 상태일때만 팀, 탱크 변경
            if (UserState == UserState.Idle)
            {
                Team = teamIndex;
                Tank = tankIndex;
            }
        }

        public int GetSlotState()
        {
            switch (UserState)
            {
                case UserState.Idle:
                    if (IsHost)
                    {
                        return 4;
                    }
                    return 1;

                case UserState.Shoping:
                    if (IsHost)
                    {
                        return 6;
                    }
                    return 2;

                case UserState.Ready:
                    if (IsHost)
                    {
                        return 4;
                    }
                    return 3;

                case UserState.Loading:
                case UserState.InGameAlive:
                case UserState.InGameDead:
                    return 5;

                default:
                    return 0;
            }
        }

        public int TurnDelay { get; set; }
    }
}

enum UserState
{
    Idle, Shoping, Ready, Loading, InGameAlive, InGameDead
}