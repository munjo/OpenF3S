using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using static System.Collections.Specialized.BitVector32;

namespace Fortress3PaewangServerTest
{
    internal class GameRoom
    {
        public uint RoomId { get; }             // 방 고유 ID번호
        public string Title { get; private set; }          // 방 제목
        public string Password { get; private set; }       // 방 비밀번호
        public GameType GameType { get; private set; }          // 게임 타입 (일반전, 보스전 등)
        public int MaxUsers { get; }          // 최대 입장 가능 인원

        // 핸들러가 빠르게 접근할 수 있도록 IReadOnlyList로 직접 노출!
        public IReadOnlyList<GameRoomSlot> Slots => _slots;
        private List<GameRoomSlot> _slots = new List<GameRoomSlot>();      // 방 안의 슬롯(유저) 배열

        public bool[] IsSlotOpened { get; }

        public int MinTier { get => MinTierToInt(_minTier); } // 입장 가능 최소 계급
 
        MinTierToEnterRoom _minTier;

        public int CurrentTurnSlotIndex { get; private set; } // 현재 턴인 슬롯 인덱스

        public bool IsFirstSyncDone { get; set; } // 80번 패킷의 첫 동기화 완료 여부

        public int MapIndex { get; private set; }

        // 게임의 첫 번째 라운드(팀 교대 턴)인지 확인하는 플래그
        public bool IsFirstRound { get; private set; }

        // 나중에 IsFirstRound같은 변수를 삭제 후 아래의 리스트를 새로 갱신하는 걸로 변경하기
        public List<int> RoundTurnOrder { get; private set; } = new List<int>();

        public int State
        {
            get
            {
                // 열린 슬롯이상 유저가 있다면 꽉참
                int slotOpenedCount = IsSlotOpened.Count(u => u == true);
                if (slotOpenedCount <= _slots.Count || MaxUsers <= _slots.Count)
                {
                    return 4;
                }
                else if (IsPlayed)
                {
                    return 5;
                }
                else
                {
                    return 3;
                }
            }
        }

        public bool IsPlayed { get; private set; } // 시작한 상태

        public GameRoom(string title, string password, int maxUsers, int gameType)
        {
            RoomId = BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0);
            Title = title;
            Password = password;
            MaxUsers = maxUsers;
            _minTier = MinTierToEnterRoom.Skull;
            CurrentTurnSlotIndex = -1;

            IsSlotOpened = new bool[18];
            GameType = Enum.IsDefined(typeof(GameType), gameType) ? (GameType)gameType : GameType.Normal;

            // 게임 타입에 따라 열어둘 슬롯변경
            if (GameType == GameType.Group || GameType == GameType.Guild)
            {
                int halfUsers = maxUsers / 2;
                for (int i = 0; i < halfUsers; i++)
                {
                    IsSlotOpened[i] = true;
                    IsSlotOpened[i + 9] = true;
                }
            }
            else
            {
                for (int i = 0; i < maxUsers; i++)
                {
                    IsSlotOpened[i] = true;
                }
            }
        }

        public int EnsureHost()
        {
            // 먼저 호스트를 찾기
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].IsHost)
                {
                    return i;
                }
            }

            // 호스트가 없다면 새로운 호스트를 정하기
            // 만약 봇(BOT) 알고리즘을 추가한다면 아래의 코드를 수정해야 함(봇은 방장이 되지 않도록 넘어가기)
            for (int i = 0; i < _slots.Count; i++)
            {
                if (!_slots[i].IsBot)
                {
                    _slots[i].IsHost = true;
                    return i;
                }
            }

            // 아무도 없는 빈 방
            return -1;
        }

        public int JoinUser(ClientSession session)
        {
            // 이미 게임중이거나 유저가 꽉 찼다면 입장불가
            if (IsPlayed || MaxUsers <= _slots.Count)
            {
                return -1;
            }

            // 이미 본인이 입장해 있는지 확인
            if (_slots.Any(s => s.Session == session))
            {
                return -1;
            }

            List<GameRoomSlot> sortSlots = _slots.OrderBy(r => r.Index).ToList();
            int listIndex = 0;
            int i;
            for (i = 0; i < MaxUsers && listIndex < sortSlots.Count; i++)
            {
                // 슬롯이 닫혀있다면 건너뜀
                if (!IsSlotOpened[i]) { continue; }

                // 해당 인덱스에 슬롯이 비어있는지 확인
                if (sortSlots[listIndex].Index != i)
                {
                    GameRoomSlot addSlot = new GameRoomSlot(session, i);
                    _slots.Add(addSlot);
                    return i;
                }
                listIndex++;
            }

            // 리스트가 끝났다면 빈슬롯만 찾기
            for (; i < MaxUsers; i++)
            {
                // 슬롯이 열려있는지 확인
                if (IsSlotOpened[i])
                {
                    GameRoomSlot addSlot = new GameRoomSlot(session, i);
                    _slots.Add(addSlot);
                    return i;
                }
            }

            return -1;
        }

        public int LeaveUser(ClientSession session)
        {
            int index = -1;
            for (int i = 0; i < _slots.Count; i++)
            {
                // 일치하는 슬롯 찾기
                if (_slots[i].Session == session)
                {
                    index = _slots[i].Index;
                    _slots.RemoveAt(i);
                    break;
                }
            }
            return index;
        }

        public bool ChangeInfo(ClientSession session, string newTitle, int mapIndex = -1, int minTier = -1)
        {
            // 이미 게임중이라면 무시
            if (IsPlayed) { return false; }

            // 본인 요청이 아니라면 무시 | 방장이 아니라면 무시
            var slot = _slots.FirstOrDefault(s => s.Session == session);
            if (slot == null || !slot.IsHost) { return false; }

            Title = string.IsNullOrEmpty(newTitle) ? Title : newTitle;
            MapIndex = mapIndex < 0 ? MapIndex : mapIndex;
            _minTier = minTier < 0 ? _minTier : IntToMinTier(minTier);
            return true;
        }

        public bool SlotClose(ClientSession session, int targetSlot)
        {
            // 이미 게임중이라면 무시
            if (IsPlayed) { return false; }

            // 본인 요청이 아니라면 무시 | 방장이 아니라면 무시 | 타겟 슬롯이 잘못되었는지 확인
            var slot = _slots.FirstOrDefault(s => s.Session == session);
            if (slot == null || !slot.IsHost || targetSlot < 0 || MaxUsers <= targetSlot) { return false; }

            // 길드전이라면 다르게 작동함 예를 들어서 인덱스 0을 닫는다고 하면
            // 반대쪽 인덱스 0(9)도 닫아줘야 함
            if (GameType == GameType.Group || GameType == GameType.Guild)
            {
                int closeTargetLeft = targetSlot < 9 ? targetSlot : targetSlot - 9;
                int closeTargetRight = closeTargetLeft + 9;

                // 둘다 사람이 없다면 닫기 성공
                if (!_slots.Any(s => s.Index == closeTargetLeft || s.Index == closeTargetRight))
                {
                    IsSlotOpened[closeTargetLeft] = false;
                    IsSlotOpened[closeTargetRight] = false;
                    return true;
                }
                return false;
            }
            else
            {
                // 사람이 없다면 닫기 성공
                if (!_slots.Any(s => s.Index == targetSlot))
                {
                    IsSlotOpened[targetSlot] = false;
                    return true;
                }
                return false;
            }
        }

        public bool SlotOpen(ClientSession session, int targetSlot)
        {
            // 이미 게임중이라면 무시
            if (IsPlayed) { return false; }

            // 본인 요청이 아니라면 무시 | 방장이 아니라면 무시 | 타겟 슬롯이 잘못되었는지 확인
            var slot = _slots.FirstOrDefault(s => s.Session == session);
            if (slot == null || !slot.IsHost || targetSlot < 0 || MaxUsers <= targetSlot) { return false; }

            // 길드전이라면 다르게 작동함 예를 들어서 인덱스 0을 연다고 하면
            // 반대쪽 인덱스 0(9)도 열어줘야 함
            if (GameType == GameType.Group || GameType == GameType.Guild)
            {
                int closeTargetLeft = targetSlot < 9 ? targetSlot : targetSlot - 9;
                int closeTargetRight = closeTargetLeft + 9;

                // 둘다 사람이 없다면 열기 성공
                if (!_slots.Any(s => s.Index == closeTargetLeft || s.Index == closeTargetRight))
                {
                    IsSlotOpened[closeTargetLeft] = true;
                    IsSlotOpened[closeTargetRight] = true;
                    return true;
                }
                return false;
            }
            else
            {
                // 사람이 없다면 열기 성공
                if (!_slots.Any(s => s.Index == targetSlot))
                {
                    IsSlotOpened[targetSlot] = true;
                    return true;
                }
                return false;
            }
        }

        public bool KickUser(ClientSession session, int targetSlot, out ClientSession kickedSession)
        {
            kickedSession = null;

            // 이미 게임중이라면 무시
            if (IsPlayed) { return false; }

            // 요청한 세션이 방에 없다면 무시 | 방장이 아니라면 무시
            var requestSlot = _slots.FirstOrDefault(s => s.Session == session);
            if (requestSlot == null || !requestSlot.IsHost) { return false; }

            // 타겟을 찾고 삭제
            var target = _slots.FirstOrDefault(s => s.Index == targetSlot);
            // 자기 자신은 강퇴 불가능
            if (requestSlot == target) { return false; }

            if (target != null)
            {
                kickedSession = target.Session;
                _slots.Remove(target);
                return true;
            }
            return false;
        }

        public bool UserSlotChange(ClientSession session, int slotState, int teamIndex, int tankIndex)
        {
            // 이미 게임중이라면 무시
            if(IsPlayed) { return false; }

            // 본인 요청이 아니라면 무시
            var slot = _slots.FirstOrDefault(s => s.Session == session);
            if (slot == null) { return false; }

            slot.StateChange(slotState, teamIndex, tankIndex);
            return true;
        }

        public bool MoveUserSlot(ClientSession session, int targetSlot, int slotState, int teamIndex, int tankIndex)
        {
            // 이미 게임중이라면 무시
            if (IsPlayed) { return false; }

            // 본인 요청이 아니라면 무시 | 준비(Ready)중이 아닌 상태에서만 움직이기 | 타겟 슬롯이 잘못되었는지 확인 | 해당 슬롯이 닫혀있는지 확인
            var slot = _slots.FirstOrDefault(s => s.Session == session);
            if (slot == null || slot.UserState != UserState.Idle || targetSlot < 0 || 18 <= targetSlot || !IsSlotOpened[targetSlot])
            {
                return false;
            }

            // 사람이 없다면 이동 성공
            if (!_slots.Any(s => s.Index == targetSlot))
            {
                slot.Index = targetSlot;
                slot.StateChange(slotState, teamIndex, tankIndex);
                return true;
            }
            return false;
        }

        public bool SetGameLoading(ClientSession session)
        {
            // 이미 게임중이라면 무시
            if (IsPlayed) { return false; }

            // 본인 요청이 아니라면 무시 | 방장이 아니라면 무시
            var slot = _slots.FirstOrDefault(s => s.Session == session);
            if (slot == null || !slot.IsHost) { return false; }

            // 유저의 상태를 불러오는 중으로 변경
            for (int i = 0; i < _slots.Count; i++)
            {
                _slots[i].UserState = UserState.Loading;
                _slots[i].Delay = 600;
                _slots[i].IsTurnUsed = false;
                _slots[i].LastReportedTurnCount = -1;
            }
            IsPlayed = true;
            IsFirstRound = true; // 첫 라운드 시작
            IsFirstSyncDone = false; // 첫 동기화 대기

            // 게임에 참여하는(살아있는) 슬롯 인덱스만 뽑아서 턴 순서를 만듦
            // 일단은 간단히 슬롯 번호(Index) 오름차순으로 턴을 배정
            RoundTurnOrder = _slots
                .OrderBy(s => s.Index)
                .Select(s => s.Index)
                .ToList();

            if (RoundTurnOrder.Count > 0)
            {
                CurrentTurnSlotIndex = RoundTurnOrder[0];
            }

            return true;
        }

        public bool SetGameStart()
        {
            // 게임중이 아니라면 나가기
            if (!IsPlayed) { return false; }

            return _slots.Count(u => u.UserState == UserState.InGameAlive) == _slots.Count;
        }

        // 턴 동기화 처리 (모든 클라이언트가 80번을 보냈는지 확인)
        public bool SyncTurn(ClientSession session, int turnCount, int delay)
        {
            var slot = _slots.FirstOrDefault(s => s.Session == session);
            if (slot == null) { return false; }

            slot.Delay = delay;
            slot.LastReportedTurnCount = turnCount;

            // 게임에 참여 중인(살아있거나 죽은) 모든 활성 유저가 동일한 턴 카운트를 보고했는지 검사
            var activeSlots = _slots.Where(s => s.Session != null && (s.UserState == UserState.InGameAlive || s.UserState == UserState.InGameDead)).ToList();
            if (activeSlots.Count == 0) { return false; }

            return activeSlots.All(s => s.LastReportedTurnCount == turnCount);
        }

        // 다음 턴 계산 로직
        public void CalculateNextTurn()
        {
            var currentSlot = _slots.FirstOrDefault(s => s.Index == CurrentTurnSlotIndex);
            if (currentSlot != null)
            {
                currentSlot.IsTurnUsed = true;
            }

            var aliveSlots = _slots.Where(s => s.UserState == UserState.InGameAlive).ToList();
            if (aliveSlots.Count == 0) return;

            // 모든 인원이 한 번씩 턴을 썼으므로, 새로운 라운드를 시작!
            if (aliveSlots.All(s => s.IsTurnUsed))
            {
                foreach (var s in aliveSlots)
                {
                    s.IsTurnUsed = false;
                }
                IsFirstRound = false;
            }

            var remainingSlots = aliveSlots.Where(s => !s.IsTurnUsed).ToList();
            GameRoomSlot nextSlot = null;

            if (IsFirstRound && currentSlot != null)
            {
                // [Phase 1: 초기 팀 교대 라운드] 방금 쏜 사람과 다른 팀 우선
                nextSlot = remainingSlots.Where(s => s.Team != currentSlot.Team)
                                         .OrderBy(s => s.Index)
                                         .FirstOrDefault();
                if (nextSlot == null)
                {
                    nextSlot = remainingSlots.OrderBy(s => s.Index).FirstOrDefault();
                }
            }
            else
            {
                // [Phase 2: 일반 라운드] 아직 안 쏜 사람 중에서 딜레이가 가장 적은 사람 (동률이면 인덱스 순)
                nextSlot = remainingSlots.OrderBy(s => s.Delay).ThenBy(s => s.Index).FirstOrDefault();
            }

            if (nextSlot != null)
            {
                CurrentTurnSlotIndex = nextSlot.Index;
            }
        }

        // 게임 종료 조건 검사 (생존한 팀이 1개 이하인지 확인)
        public bool CheckGameEndCondition(out int winningTeam)
        {
            winningTeam = -1;
            if (!IsPlayed)
            {
                return false;
            }

            // 현재 살아있는 유저들의 소속 팀(Team) 추출
            var aliveTeams = _slots.Where(s => s.UserState == UserState.InGameAlive)
                                   .Select(s => s.Team)
                                   .Distinct()
                                   .ToList();

            if (aliveTeams.Count <= 1)
            {
                // 살아남은 팀이 1팀이거나 모두 전멸(0팀)한 경우
                winningTeam = aliveTeams.Count == 1 ? aliveTeams[0] : -1;
                IsPlayed = false; // 게임 상태 종료로 전환

                // 유저 상태를 대기로 초기화
                foreach (var s in _slots)
                {
                    s.UserState = UserState.Idle;
                }
                CurrentTurnSlotIndex = -1;
                return true;
            }

            return false;
        }

        public bool SetSlotInGameAlive(ClientSession session)
        {
            // 본인 요청이 아니라면 무시 | 상태가 게임 준비중이 아니었다면 무시
            var slot = _slots.FirstOrDefault(s => s.Session == session);
            if (slot == null || slot.UserState != UserState.Loading) { return false; }

            slot.UserState = UserState.InGameAlive;
            return true;
        }

        public bool SetSlotInGameDead(ClientSession session)
        {
            // 본인 요청이 아니라면 무시 | 상태가 인게임 생존 상태가 아니었다면 무시
            var slot = _slots.FirstOrDefault(s => s.Session == session);
            if (slot == null || slot.UserState != UserState.InGameAlive) { return false; }

            slot.UserState = UserState.InGameDead;
            return true;
        }

        private static int GetPlayableMapIndex(int value)
        {
            switch (value)
            {
                case 0:
                case 1:
                case 2:
                case 3:
                case 4:
                case 5:
                case 6:
                case 7:
                case 8:
                case 9:
                case 10:
                case 11:
                case 12:
                case 13:
                case 14:
                case 15:
                case 16:
                case 17:
                    return value;

                default:
                    return 0;
            }
        }

        private static MinTierToEnterRoom IntToMinTier(int value)
        {
            switch (value)
            {
                case 0:
                case 1:
                case 2:
                case 3:
                case 4:
                case 5:
                    return MinTierToEnterRoom.Decoration;

                case 6:
                case 7:
                case 8:
                    return MinTierToEnterRoom.Medal;

                case 9:
                case 10:
                case 11:
                case 12:
                    return MinTierToEnterRoom.Star;

                case 13:
                case 14:
                case 15:
                    return MinTierToEnterRoom.Missile;

                case 16:
                case 17:
                case 18:
                    return MinTierToEnterRoom.Bullet;

                default:
                    return MinTierToEnterRoom.Skull;
            }
        }

        private static int MinTierToInt(MinTierToEnterRoom tier)
        {
            switch (tier)
            {
                default:
                    return 19;

                case MinTierToEnterRoom.Bullet:
                    return 18;

                case MinTierToEnterRoom.Missile:
                    return 15;

                case MinTierToEnterRoom.Star:
                    return 12;

                case MinTierToEnterRoom.Medal:
                    return 8;

                case MinTierToEnterRoom.Decoration:
                    return 5;
            }
        }

        // 핸들러와 매니저가 안전하게 브로드캐스트 대상을 가져가기 위한 메서드
        public List<ClientSession> GetSessions()
        {
            var sessions = new List<ClientSession>(_slots.Count);
            foreach (var slot in _slots)
            {
                if (slot.Session != null) sessions.Add(slot.Session);
            }
            return sessions;
        }

        public GameRoomSlot GetSlotBySession(ClientSession session)
        {
            return _slots.Find(s => s.Session == session);
        }
    }

    public enum GameType
    {
        Normal, NoTurn, Group, Siege, Total, Guild, Tournament
    }
    public enum MinTierToEnterRoom
    {
        Skull,    // 해골
        Bullet,   // 총알
        Missile,  // 미사일
        Star,     // 별
        Medal,    // 메달
        Decoration // 훈장
    }
}
