using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using static System.Collections.Specialized.BitVector32;

namespace Fortress3PaewangServerTest
{
    // 3. 서버 내의 모든 방을 중앙 통제하는 매니저 클래스 (싱글톤 역할)
    internal static class RoomManager
    {
        // 서버에 열려있는 모든 방을 관리하는 스레드 안전(Thread-Safe) 딕셔너리
        private static readonly ConcurrentDictionary<int, GameRoom> _rooms = new ConcurrentDictionary<int, GameRoom>();

        // [추가] 동시에 여러 스레드가 방을 만들 때, 같은 번호를 부여받는 것을 막기 위한 생성 전용 자물쇠
        private static readonly object _roomCreationLock = new object();

        private static readonly Encoding eucKr = Encoding.GetEncoding("euc-kr");

        // 단일 방 가져오기
        public static GameRoom GetRoom(int roomIndex)
        {
            _rooms.TryGetValue(roomIndex, out GameRoom room);
            return room;
        }

        // [방 생성 - 50]
        public static void CreateRoom(ClientSession session, string title, string password, int gameType, int maxUsers)
        {
            GameRoom room = null;
            int newRoomIndex = -1;

            // [핵심] 인덱스 탐색부터 방 등록까지의 과정을 Lock으로 묶어서 동시성 문제를 완벽히 차단합니다.
            lock (_roomCreationLock)
            {
                // 클라이언트 규격에 맞추어 0번부터 509번 인덱스 중에서 빈 번호를 찾습니다.
                for (int i = 0; i < 510; i++)
                {
                    if (!_rooms.ContainsKey(i))
                    {
                        newRoomIndex = i; // 빈 인덱스를 찾음!
                        break;
                    }
                }

                // 510개의 방이 모두 가득 차서 더 이상 빈 인덱스가 없다면 생성을 차단합니다.
                if (newRoomIndex != -1)
                {
                    room = new GameRoom(title, password, maxUsers, gameType);
                    _rooms.TryAdd(newRoomIndex, room);
                    room.JoinUser(session);
                    room.EnsureHost();
                    session.CurrentRoomIndex = newRoomIndex;
                }
            }

            ServerPacketBuilder serverPacket = new ServerPacketBuilder { PacketSizeGroup = 3, CommandId = 50, PacketData = new byte[3] };
            if (newRoomIndex == -1 || gameType == 5)
            {
                serverPacket.WriteByte(0);
            }
            else
            {
                session.CachedLobbyRooms.Clear();
                serverPacket.WriteByte(1);
                serverPacket.WriteShort((short)newRoomIndex);
            }
            session.SendPacket(serverPacket);
            return;
        }

        // [방 목록 전송 - 51]
        public static void SendRoomList(ClientSession session, int startRoomIndex, int roomType, int roomStateFilter, bool privRooms)
        {
            session.CachedLobbyRooms.Clear();

            // 패킷 데이터를 직접 조립할 리스트
            var packetDataList = new List<byte[]>();

            // 전체 목록 요청
            if (roomStateFilter == 6)
            {
                if (privRooms) { startRoomIndex = Math.Max(0, startRoomIndex - 5); }
                for (int i = 0; i < 6; i++)
                {
                    int targetIndex = startRoomIndex + i;
                    _rooms.TryGetValue(targetIndex, out GameRoom realRoom);
                    if(realRoom == null)
                    {
                        byte[] data = new byte[47];
                        int bitOff = 0;
                        BitStream.WriteBits(data, ref bitOff, 9, targetIndex);
                        packetDataList.Add(data);
                    }
                    else
                    {
                        lock (realRoom)
                        {
                            packetDataList.Add(BuildRoomListEntry(realRoom, targetIndex, roomType, session));
                        }
                    }
                }
            }
            else
            {
                var allRooms = _rooms.OrderBy(kvp => kvp.Key).ToList();
                foreach (var kvp in allRooms)
                {
                    int rIdx = kvp.Key;
                    var room = kvp.Value;
                    lock (room)
                    {
                        if (rIdx < startRoomIndex || (roomType != 4 && (int)room.GameType != roomType) || roomStateFilter != room.State) { continue; }
                        packetDataList.Add(BuildRoomListEntry(room, rIdx, roomType, session));
                        if (12 <= packetDataList.Count) { break; }
                    }
                }
            }

            ServerPacketBuilder serverPacket = new ServerPacketBuilder { PacketSizeGroup = 0, CommandId = 51 };
            serverPacket.PacketData = new byte[1 + (packetDataList.Count * 47)];
            serverPacket.WriteByte((byte)packetDataList.Count);

            for (int i = 0; i < packetDataList.Count; i++)
            {
                serverPacket.ByteOffset = 1 + (i * 47);
                serverPacket.WriteBytes(47, packetDataList[i]);
            }
            session.SendPacket(serverPacket);
        }

        private static byte[] BuildRoomListEntry(GameRoom room, int roomIndex, int filterType, ClientSession session)
        {
            byte[] data = new byte[47];
            int bitOff = 0;

            if (room == null || (filterType != 4 && (int)room.GameType != filterType))
            {
                BitStream.WriteBits(data, ref bitOff, 9, roomIndex);
                BitStream.WriteBits(data, ref bitOff, 3, 0);
                BitStream.WriteBits(data, ref bitOff, 3, 0);
                return data;
            }

            bool hasPwd = !string.IsNullOrEmpty(room.Password);
            BitStream.WriteBits(data, ref bitOff, 9, roomIndex);
            BitStream.WriteBits(data, ref bitOff, 3, (int)room.GameType);
            BitStream.WriteBits(data, ref bitOff, 3, room.State);
            BitStream.WriteBits(data, ref bitOff, 5, room.Slots.Count);
            BitStream.WriteBits(data, ref bitOff, 5, room.MaxUsers);
            BitStream.WriteBits(data, ref bitOff, 3, hasPwd ? 1 : 0);
            BitStream.WriteBits(data, ref bitOff, 5, room.MinTier);
            Array.Copy(eucKr.GetBytes((room.Title ?? "").PadRight(40, '\0')), 0, data, (bitOff + 7) / 8, 40);
            session.CachedLobbyRooms[roomIndex] = room.RoomId;
            return data;
        }

        // [방 입장- 52, 54, 55, 64, 65]
        public static void JoinRoom(ClientSession session, int roomIndex, string password)
        {
            int result = 0;
            ServerPacketBuilder p52 = new ServerPacketBuilder {
                PacketSizeGroup = 0,
                CommandId = 52,
                PacketData = new byte[3]
            };

            List<ClientSession> otherSessions = null;
            ServerPacketBuilder p54 = null, p64 = null, p65 = null, p55 = null;

            // 1. 해당 번호의 방이 존재하는지 확인
            GameRoom room = GetRoom(roomIndex);
            // 방이 없음: 결과값 3
            if (room == null) { result = 3; }
            else
            {
                lock (room)
                {
                    // 캐시된 방의 아이디 값이 다름: 결과값 0
                    if (!session.CachedLobbyRooms.TryGetValue(roomIndex, out uint cacheId) || cacheId != room.RoomId) { result = 0; }
                    else
                    {
                        // 방의 비밀번호가 다름: 결과값 2
                        if (!string.IsNullOrEmpty(room.Password) && room.Password != password) { result = 2; }
                        // 방의 최소 입장 가능 계급보다 낮음(예: 별(12)<해골(19)) 또는 이미 게임중: 결과값 0
                        else if (room.MinTier < session.SelectedCharacter.GameTier || room.IsPlayed) { result = 0; }
                        else
                        {
                            if (0 <= room.JoinUser(session))
                            {
                                room.EnsureHost();
                                session.CurrentRoomIndex = roomIndex;
                                result = 1;

                                // 성공 시 전송할 바이트 배열들을 락 안에서 미리 조립
                                var mySlot = room.Slots.First(s => s.Session == session);

                                p52.BitOffset = 8;
                                p52.WriteBits(5, mySlot.Index);
                                p52.WriteBits(9, roomIndex);

                                // 게임방 전체 유저 정보 전송(54)
                                p54 = new ServerPacketBuilder
                                {
                                    PacketSizeGroup = 0, CommandId = 54,
                                    PacketData = BuildAllSlotsInfoBytes(room)
                                };

                                // 방 상태 정보 전송(64)
                                p64 = new ServerPacketBuilder
                                {
                                    // 64 커맨드 값은 지정된 크기로 보냄
                                    PacketSizeGroup = 3, CommandId = 64,
                                    PacketData = new byte[4],
                                };
                                p64.BitOffset = 20;
                                p64.WriteBits(7, room.MapIndex);
                                p64.WriteBits(5, room.MinTier);

                                // 방 제목 전송(65)(로비 화면과 방제목이 다를수도 있기에 전송)
                                byte[] tBytes = eucKr.GetBytes(room.Title);
                                p65 = new ServerPacketBuilder
                                {
                                    PacketSizeGroup = 0, CommandId = 65,
                                    PacketData = new byte[1 + tBytes.Length + 1]
                                };

                                p65.WriteByte((byte)(tBytes.Length + 1));
                                p65.WriteBytes(tBytes.Length, tBytes);

                                otherSessions = room.GetSessions().Where(s => s != session).ToList();

                                p55 = new ServerPacketBuilder
                                {
                                    PacketSizeGroup = 2, CommandId = 55,
                                    PacketData = BuildSlotInfoBytes(mySlot)
                                };
                            }
                            // 방 인원수로 인해 입장 실패: 결과값 4
                            else { result = 4; }
                        }
                    }
                }
            }

            // 결과값 쓰기
            p52.ByteOffset = 0;
            p52.WriteByte((byte)result);
            session.SendPacket(p52);

            if (result == 1)
            {
                session.SendPacket(p54);
                session.SendPacket(p64);
                session.SendPacket(p65);
                GamePacketHandler.BroadcastToRoom(otherSessions, p55);
                return;
            }

            ServerPacketBuilder p42 = new ServerPacketBuilder {
                PacketSizeGroup = 0, CommandId = 42,
                PacketData = new byte[47]
            };
            p42.WriteBits(9, roomIndex);
            // 방이 없다면 방이 없다는 데이터 전송
            if (room == null)
            {
                p42.WriteBits(4, 1); // 1: 방 상태만 전송
                session.CachedLobbyRooms.TryRemove(roomIndex, out _);
            }
            else
            {
                lock (room)
                {
                    p42.WriteBits(4, 7); // 7: 모든 데이터 전송
                    p42.BitOffset = 16;
                    p42.WriteBits(3, (int)room.GameType);
                    p42.WriteBits(3, room.State);
                    p42.WriteBits(5, room.Slots.Count);
                    p42.WriteBits(5, room.MaxUsers);
                    p42.WriteBits(3, string.IsNullOrEmpty(room.Password) ? 0 : 1);
                    p42.WriteBits(5, room.MinTier);
                    p42.WriteBytes(40, eucKr.GetBytes((room.Title ?? "").PadRight(40, '\0')));
                }
            }
            session.SendPacket(p42);
            return;
        }

        // [방 나가기 - 53, 56]
        public static void LeaveRoom(ClientSession session)
        {
            int roomIndex = session.CurrentRoomIndex;

            GameRoom room = GetRoom(roomIndex);
            List<ClientSession> roomSessions = null;
            ServerPacketBuilder p56 = null;

            // 유저가 속해있던 방을 찾음
            if (room != null)
            {
                lock (room)
                {
                    // 유저가 앉아있던 슬롯을 다시 비워줌(null)
                    int leftIndex = room.LeaveUser(session);
                    if (leftIndex != -1) {
                        // 방에 아무도 없는지 확인
                        if (room.EnsureHost() == -1)
                        {
                            _rooms.TryRemove(roomIndex, out _);
                        }
                        else
                        {
                            roomSessions = room.GetSessions();
                            p56 = new ServerPacketBuilder()
                            {
                                PacketSizeGroup = 3,
                                CommandId = 56,
                                PacketData = new byte[8],
                            };

                            p56.BitOffset = 2;
                            p56.WriteBits(5, leftIndex);
                            for (int i = 0, listIndex = 0; i < 18; i++)
                            {
                                if (listIndex < room.Slots.Count && room.Slots[listIndex].Index == i)
                                {
                                    p56.WriteBits(3, room.Slots[listIndex].GetSlotState());
                                    listIndex++;
                                }
                                else p56.WriteBits(3, 0);
                            }
                        }
                    }
                }
            }

            if(p56 != null)
            {
                GamePacketHandler.BroadcastToRoom(roomSessions, p56);
            }
        }

        // [게임방 정보 전송 - 43]
        public static void SendRoomInfo(ClientSession session, int roomIndex)
        {
            GameRoom room = GetRoom(roomIndex);
            ServerPacketBuilder serverPacket = new ServerPacketBuilder
            {
                PacketSizeGroup = 0, CommandId = 43
            };

            if (room == null)
            {
                serverPacket.PacketData = new byte[1];
                serverPacket.WriteByte(0);
            }
            else
            {
                lock (room)
                {
                    serverPacket.PacketData = new byte[1 + (room.Slots.Count * 31)];
                    serverPacket.WriteByte((byte)room.Slots.Count);
                    for (int i = 0; i < room.Slots.Count; i++)
                    {
                        serverPacket.BitOffset = 8 + (i * 248);
                        var slot = room.Slots[i];
                        serverPacket.WriteBits(5, slot.Session?.SelectedCharacter.GameTier ?? 0);
                        serverPacket.WriteBits(4, 0); serverPacket.WriteBits(3, 0);
                        serverPacket.WriteBits(4, slot.Session?.MyGuildNameColor ?? 0);
                        serverPacket.WriteBits(1, (slot.Session?.IsFemale ?? false) ? 1 : 0);
                        serverPacket.ByteOffset = 4 + (i * 31);
                        serverPacket.WriteInt(0);
                        serverPacket.WriteBytes(12, eucKr.GetBytes(slot.Session?.MyGuildName ?? ""));
                        serverPacket.WriteBytes(12, eucKr.GetBytes(slot.Session?.SelectedCharacter.GameId ?? ""));
                    }
                }
            }
            session.SendPacket(serverPacket);
        }

        // [게임방 유저 정보 전송 - 54]
        public static void SendRoomUserInfo(ClientSession session)
        {
            GameRoom room = GetRoom(session.CurrentRoomIndex);
            if (room == null) { return; }

            ServerPacketBuilder p54 = new ServerPacketBuilder
            {
                PacketSizeGroup = 0,
                CommandId = 54
            };

            lock (room)
            {
                p54.PacketData = BuildAllSlotsInfoBytes(room);
            }

            session.SendPacket(p54);
        }

        // [방 상태 정보 전송 - 64]
        public static void UpdateRoom(ClientSession session, int mapIndex, int minTier, int u1, int u2, int u3, int u4, int u5)
        {
            GameRoom room = GetRoom(session.CurrentRoomIndex);
            if (room == null) { return; }

            List<ClientSession> roomSessions = null;
            ServerPacketBuilder p64 = null;

            lock (room)
            {
                if (room.ChangeInfo(session, null, mapIndex, minTier))
                {
                    p64 = new ServerPacketBuilder
                    {
                        PacketSizeGroup = 3,
                        CommandId = 64,
                        PacketData = new byte[4]
                    };

                    p64.WriteBits(4, u1);
                    p64.WriteBits(4, u2);
                    p64.WriteBits(4, u3);
                    p64.WriteBits(4, u4);
                    p64.WriteBits(4, u5);
                    p64.WriteBits(7, room.MapIndex);
                    p64.WriteBits(5, room.MinTier);
                    roomSessions = room.GetSessions();
                }
            }

            if (p64 != null) 
            {
                GamePacketHandler.BroadcastToRoom(roomSessions, p64);
            }
            return;
        }

        // [방 제목 변경 결과 전송 - 65]
        public static void UpdateRoomTitle(ClientSession session, string newTitle)
        {
            GameRoom room = GetRoom(session.CurrentRoomIndex);
            if (room == null) { return; }

            List<ClientSession> roomSessions = null;

            ServerPacketBuilder p65 = null;
            lock (room)
            {
                if (room.ChangeInfo(session, newTitle) && room.Title == newTitle)
                {
                    byte[] rTitle = eucKr.GetBytes(room.Title);
                    p65 = new ServerPacketBuilder
                    {
                        PacketSizeGroup = 0,
                        CommandId = 65,
                        PacketData = new byte[1 + rTitle.Length + 1]
                    };
                    p65.WriteByte((byte)(rTitle.Length + 1));
                    p65.WriteBytes(rTitle.Length, rTitle);

                    roomSessions = room.GetSessions();
                }
            }
            if (p65 != null) { GamePacketHandler.BroadcastToRoom(roomSessions, p65); }
        }

        // [방 슬롯 상태 변경 전송 - 57, 56]
        public static void UpdateSlot(ClientSession session, int slotIndex, int slotState, int targetSlot, int slotCommand, int teamIndex, int tankIndex)
        {
            GameRoom room = GetRoom(session.CurrentRoomIndex);
            if (room == null) return;

            ServerPacketBuilder p57 = null, p56 = null;
            ClientSession kickedSession = null;
            List<ClientSession> sessions = null;

            lock (room)
            {
                bool result = false;
                // 이동, 상태 변경
                if (slotCommand == 0)
                {
                    result = (slotIndex != targetSlot) ?
                        room.MoveUserSlot(session, targetSlot, slotState, teamIndex, tankIndex) : room.UserSlotChange(session, slotState, teamIndex, tankIndex);
                }
                else if (slotCommand == 1) { result = room.SlotClose(session, targetSlot); }
                else if (slotCommand == 2) { result = room.SlotOpen(session, targetSlot); }
                else if (slotCommand == 3) { result = room.KickUser(session, targetSlot, out kickedSession); }

                if (!result) { return; }
                sessions = room.GetSessions();
                // 플레이어 강퇴
                if (slotCommand == 3)
                {
                    p57 = new ServerPacketBuilder
                    {
                        PacketSizeGroup = 3,
                        CommandId = 57,
                        PacketData = new byte[4]
                    };
                    p57.WriteBits(5, targetSlot);
                    p57.WriteBits(3, 0);
                    p57.WriteBits(5, targetSlot);
                    p57.WriteBits(3, 3);
                    p57.WriteBits(4, 0);
                    p57.WriteBits(9, 0);

                    // 나머지 슬롯의 상태를 전송하기 위해 슬롯을 찾고 상태를 씀
                    p56 = new ServerPacketBuilder
                    {
                        PacketSizeGroup = 3,
                        CommandId = 56,
                        PacketData = new byte[8]
                    };
                    p56.BitOffset = 2;
                    p56.WriteBits(5, targetSlot);
                    for (int i = 0, listIdx = 0; i < 18; i++)
                    {
                        if (listIdx < room.Slots.Count && room.Slots[listIdx].Index == i)
                        {
                            var s = room.Slots[listIdx];
                            p56.WriteBits(3, s.GetSlotState());
                            listIdx++;
                        }
                        else { p56.WriteBits(3, 0); }
                    }
                }
                else
                {
                    p57 = new ServerPacketBuilder
                    {
                        PacketSizeGroup = 3,
                        CommandId = 57,
                        PacketData = new byte[4]
                    };

                    var slot = room.Slots.FirstOrDefault(s => s.Index == targetSlot);
                    p57.WriteBits(5, slotCommand != 0 ? targetSlot : slotIndex);
                    p57.WriteBits(3, slotState);
                    p57.WriteBits(5, targetSlot);
                    p57.WriteBits(3, slotCommand);
                    p57.WriteBits(4, slot?.Team ?? 0);
                    p57.WriteBits(9, slot?.Tank ?? 0);
                }
            }

            if (slotCommand == 3 && kickedSession != null)
            {
                // 플레이어 강퇴시 해당 세션에만 패킷을 보낸다
                kickedSession.SendPacket(p57);
                kickedSession.CurrentRoomIndex = -1;
                // 플레이어가 나갔음을 알림
                GamePacketHandler.BroadcastToRoom(sessions, p56);
            }
            else if (p57 != null)
            {
                GamePacketHandler.BroadcastToRoom(sessions, p57);
            }
        }

        public static void BroadcastRoomChat(ClientSession session, int unkValue1, int userIndex, string chatMsg)
        {
            GameRoom room = GetRoom(session.CurrentRoomIndex);
            if (room == null) return;

            List<ClientSession> roomSessions = null;

            byte[] chatBytes = eucKr.GetBytes(chatMsg);
            ServerPacketBuilder p70 = p70 = new ServerPacketBuilder
            {
                PacketSizeGroup = 0,
                CommandId = 70,
                PacketData = new byte[2 + chatBytes.Length + 1]
            };
            p70.WriteBits(4, unkValue1);
            p70.WriteBits(5, userIndex);
            p70.ByteOffset = 2;
            p70.WriteBytes(chatBytes.Length, chatBytes);

            lock (room)
            {
                roomSessions = room.GetSessions();
            }

            GamePacketHandler.BroadcastToRoom(roomSessions, p70);

            // 특수 기능: 채팅으로 탱크 선택
            if (TryChatCharacterSelect(chatMsg, out int tankIndex))
            {
                var my = room.GetSlotBySession(session);
                if (my != null)
                {
                    UpdateSlot(session, my.Index, my.GetSlotState(), my.Index, 0, my.Team, tankIndex);
                }
            }
        }

        // 
        public static void SetGameLoading(ClientSession session)
        {
            GameRoom room = GetRoom(session.CurrentRoomIndex);
            if (room == null) return;

            ServerPacketBuilder p67 = null, p71 = null;
            List<ClientSession> sessions = null;

            lock (room)
            {
                if (room.SetGameLoading(session))
                {
                    Random rand = new Random();
                    p67 = new ServerPacketBuilder
                    {
                        PacketSizeGroup = 0,
                        CommandId = 67,
                        PacketData = new byte[4 + room.Slots.Count * 35]
                    };

                    p67.WriteBits(4, 0); // 아이템 모드 (0: 일반 아이템, 1: 아이템 없음, 2: 화력 아이템)
                    p67.WriteBits(4, 0); // 승패 모드?
                    p67.WriteBits(4, 0); // 탱크배치 모드?
                    p67.WriteBits(4, 0); // 게임 속도 모드 (0: 보통, 1: 터보)
                    p67.WriteBits(4, 0); // 데미지 모드 (0: 노멀, 1: 터보 데이지)
                    p67.WriteBits(7, room.MapIndex == 0 ? rand.Next(1, 17) : room.MapIndex); // 맵이 랜덤(0)이라면 1~16사이의 맵중 하나로 선택
                    for (int i = 0; i < room.Slots.Count; i++)
                    {
                        var s = room.Slots[i];
                        p67.BitOffset = 32 + (i * 280);
                        p67.WriteBits(5, s.Index);
                        p67.WriteBits(12, rand.Next(0, 2048)); // X좌표 위치
                        p67.WriteBits(1, 1); // ?
                        int tank = s.Tank;
                        if (tank == 21)
                        {
                            tank = 20;
                        }
                        p67.WriteBits(5, tank == 31 ? rand.Next(0, 18) : tank); // 케릭터(탱크)가 랜덤(31)이라면 0~17사이의 케릭터중 하나로 선택
                    }

                    p71 = new ServerPacketBuilder
                    {
                        PacketSizeGroup = 3,
                        CommandId = 71,
                        PacketData = new byte[5]
                    };
                    p71.WriteBits(5, room.CurrentTurnSlotIndex);
                    p71.WriteBits(5, 1);

                    sessions = room.GetSessions();
                }
            }

            if (p67 != null)
            {
                GamePacketHandler.BroadcastToRoom(sessions, p67);
                GamePacketHandler.BroadcastToRoom(sessions, p71);
            }
        }

        // 
        public static void SetGamePlaying(ClientSession session)
        {
            GameRoom room = GetRoom(session.CurrentRoomIndex);
            if (room == null) { return; }

            List<ClientSession> sessions = null;
            bool isStart = false;
            lock (room) { 
                if (room.SetSlotInGameAlive(session))
                {
                    isStart = room.SetGameStart();
                    sessions = room.GetSessions();
                }  
            }

            if (isStart)
            {
                GamePacketHandler.BroadcastToRoom(sessions,
                    new ServerPacketBuilder { PacketSizeGroup = 3, CommandId = 69, PacketData = new byte[0] });
            }
        }

        public static void ProcessGameNormalInfo(ClientSession session, int slotIndex, int actionType, int xPos, int yPos, int power, int angle, int itemIndex)
        {
            GameRoom room = GetRoom(session.CurrentRoomIndex);
            if (room == null) { return; }

            List<ClientSession> sessions = null;
            lock (room)
            {
                sessions = room.GetSessions();
                if (actionType == 7) // 사망 처리
                {
                    room.SetSlotInGameDead(session);
                }
            }

            ServerPacketBuilder p73 = new ServerPacketBuilder {
                PacketSizeGroup = 3, CommandId = 73, PacketData = new byte[8]
            };
            p73.WriteBits(5, slotIndex);
            p73.WriteBits(4, actionType);
            p73.WriteBits(12, xPos);
            p73.WriteBits(11, yPos);
            p73.WriteBits(8, power);
            p73.WriteBits(9, angle);
            p73.WriteBits(9, itemIndex);

            GamePacketHandler.BroadcastToRoom(sessions, p73);
        }

        //
        public static void ProcessTurnEnd(ClientSession session, int unknown, int turnCount, int delay)
        {
            GameRoom room = GetRoom(session.CurrentRoomIndex);
            if (room == null) return;

            ServerPacketBuilder p81 = null, p72 = null;
            List<ClientSession> sessions = null;

            lock (room)
            {
                if (!room.SyncTurn(session, turnCount, delay)) return;

                sessions = room.GetSessions();
                if (!room.IsFirstSyncDone) room.IsFirstSyncDone = true;
                else
                {
                    if (room.CheckGameEndCondition(out int winningTeam))
                    {
                        p81 = new ServerPacketBuilder
                        {
                            PacketSizeGroup = 3,
                            CommandId = 81,
                            PacketData = new byte[3]
                        };
                        p81.WriteBits(4, winningTeam);
                        p81.WriteBits(20, 0); // ?
                    }
                    else room.CalculateNextTurn();
                }

                if (p81 == null && room.CurrentTurnSlotIndex != -1)
                {
                    p72 = new ServerPacketBuilder
                    {
                        PacketSizeGroup = 3,
                        CommandId = 72,
                        PacketData = new byte[7]
                    };
                    p72.WriteBits(5, room.CurrentTurnSlotIndex); // 차례가 올 인덱스 번호
                    int wind = new Random().Next(0, 256);
                    p72.WriteBits(2, wind / 128); // 바람 방향(0: 왼쪽, 1이상: 오른쪽)
                    p72.WriteBits(7, wind % 128); // 바람 세기
                    p72.WriteBits(9, 0); // 상자 여부
                    p72.WriteBits(12, 300); // 상자 X좌표
                    p72.WriteBits(4, 0);
                    p72.WriteBits(4, 0);
                    p72.WriteBits(12, 0);
                }
            }

            if (p81 != null) { GamePacketHandler.BroadcastToRoom(sessions, p81); }
            else if (p72 != null) { GamePacketHandler.BroadcastToRoom(sessions, p72); }
        }

        private static byte[] BuildAllSlotsInfoBytes(GameRoom room)
        {
            int count = room.Slots.Count;
            byte[] p54 = new byte[3 + (count * 33)];
            int b54 = 0;
            // 슬롯이 열렸는지 여부 가져오기
            for (int i = 0; i < room.MaxUsers; i++) {
                BitStream.WriteBits(p54, ref b54, 1, room.IsSlotOpened[i] ? 1 : 0);
            }
            b54 = 19; BitStream.WriteBits(p54, ref b54, 5, count);

            for (int i = 0; i < count; i++)
            {
                var s = room.Slots[i];
                b54 = 24 + (i * 264);
                BitStream.WriteBits(p54, ref b54, 5, s.Index);
                BitStream.WriteBits(p54, ref b54, 3, s.GetSlotState());
                BitStream.WriteBits(p54, ref b54, 5, s.Session.SelectedCharacter.GameTier);
                BitStream.WriteBits(p54, ref b54, 7, 0);
                BitStream.WriteBits(p54, ref b54, 4, s.Team);
                BitStream.WriteBits(p54, ref b54, 9, s.Tank);
                BitStream.WriteBits(p54, ref b54, 4, s.Session.MyGuildNameColor);
                BitStream.WriteBits(p54, ref b54, 1, s.Session.IsFemale ? 1 : 0);

                int byteOff = 3 + (i * 33) + 5;
                Array.Copy(eucKr.GetBytes((s.Session.MyGuildName ?? "").PadRight(12, '\0')), 0, p54, byteOff + 4, 12);
                Array.Copy(eucKr.GetBytes((s.Session.SelectedCharacter.GameId ?? "").PadRight(12, '\0')), 0, p54, byteOff + 16, 12);
            }
            return p54;
        }

        private static byte[] BuildSlotInfoBytes(GameRoomSlot slot)
        {
            byte[] p55 = new byte[33];
            int b55 = 0;
            BitStream.WriteBits(p55, ref b55, 5, slot.Index);
            BitStream.WriteBits(p55, ref b55, 3, slot.GetSlotState());
            BitStream.WriteBits(p55, ref b55, 5, slot.Session.SelectedCharacter.GameTier);
            BitStream.WriteBits(p55, ref b55, 7, 0);
            BitStream.WriteBits(p55, ref b55, 4, slot.Team);
            BitStream.WriteBits(p55, ref b55, 9, slot.Tank);
            BitStream.WriteBits(p55, ref b55, 4, slot.Session.MyGuildNameColor);
            BitStream.WriteBits(p55, ref b55, 1, slot.Session.IsFemale ? 1 : 0);
            Array.Copy(eucKr.GetBytes((slot.Session.MyGuildName ?? "").PadRight(12, '\0')), 0, p55, 9, 12);
            Array.Copy(eucKr.GetBytes((slot.Session.SelectedCharacter.GameId ?? "").PadRight(12, '\0')), 0, p55, 21, 12);
            return p55;
        }

        private static bool TryChatCharacterSelect(string input, out int charIndex)
        {
            charIndex = 0;

            // 1. 빈 문자열이거나 공백이면 무시
            if (string.IsNullOrWhiteSpace(input)) { return false; }

            // 2. 공백을 기준으로 문자열 분리
            string[] parts = input.Split(' ', (char)StringSplitOptions.RemoveEmptyEntries);

            // 3. 앞선 접두사 규칙 확인 및 최소 3개 이상의 단어 확인 (!tan sel 숫자 구조)
            if (parts.Length < 3) return false;

            string prefix = parts[0];
            string action = parts[1];

            // 규칙 검사: "!tank", "!char", "!탱크", "!케릭" 중 하나로 시작하고 두 번째 단어가 "sel"인지 확인
            bool isValidPrefix = prefix == "!tank" || prefix == "!char" || prefix == "!탱크" || prefix == "!케릭";
            if (!isValidPrefix || action != "sel") return false;

            // 4. 맨 마지막 요소 추출
            string lastPart = parts[parts.Length - 1];

            if(int.TryParse(lastPart, out charIndex))
            {
                // 인덱스에 해당하지 않는 케릭터라면 31(랜덤)로 변경
                if(charIndex < 0 || (17 < charIndex && charIndex != 20 && charIndex != 22 && charIndex != 23))
                {
                    charIndex = 31;
                }
                // 슈퍼탱크는 클라이언트 상에서 구분을 위해 임의로 21로 변경(게임 시작시 20번으로 변경해야함)
                else if(charIndex == 20)
                {
                    charIndex = 21;
                }
                return true;
            }

            // 5. 숫자로 변환 가능한지 검사 (숫자가 아니거나 빈자리라면 false)
            return false;
        }
    }
}