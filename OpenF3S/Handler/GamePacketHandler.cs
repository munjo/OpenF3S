using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using static System.Collections.Specialized.BitVector32;

namespace Fortress3PaewangServerTest
{
    internal static class GamePacketHandler
    {
        // 커맨드 ID(int)와 그에 맞는 처리 함수(Action)를 연결해주는 딕셔너리
        private static readonly Dictionary<int, Action<ClientSession, ClientPacketParser>> _handlers
            = new Dictionary<int, Action<ClientSession, ClientPacketParser>>();

        private static readonly Encoding eucKr = Encoding.GetEncoding("euc-kr");

        // 정적 생성자: 서버가 켜질 때 딱 한 번 커맨드와 함수를 매핑합니다.
        static GamePacketHandler()
        {
            _handlers.Add(30, HandleLogin);             // 0x1e: 게임 서버 로그인
            _handlers.Add(31, HandleGoToLobby);         // 0x1f: 게임 대기실로 이동
            _handlers.Add(43, HandleRoomInfo);          // 0x2b: 방 정보 전송
            _handlers.Add(45, HandleNotice);            // 0x2d: 공지사항 화면으로 이동
            _handlers.Add(50, HandleCreateRoom);        // 0x32: 게임방 만들기 결과 전송
            _handlers.Add(51, HandleRoomList);          // 0x33: 게임방 목록 전송
            _handlers.Add(52, HandleRoomJoin);          // 0x34: 게임방 입장 전송 (54번 동시 전송)
            _handlers.Add(53, HandleRoomLeave);         // 0x35: 게임방 나가기
            _handlers.Add(54, HandleRoomUserInfo);
            _handlers.Add(57, HandleSlotUpdate);        // 0x39: 슬롯 업데이트
            _handlers.Add(58, HandleOptionRequest);     // 0x3a: 옵션 데이터 요청
            _handlers.Add(59, HandleOptionChange);      // 0x3b: 옵션 데이터 변경 요청
            _handlers.Add(64, HandleRoomUpdate);        // 0x40: 방 정보 갱신
            _handlers.Add(65, HandleRoomTitleChange);   // 0x41: 게임방 제목 변경
            _handlers.Add(66, HandleRoomGameStart);     // 0x42: 게임 시작 요청
            _handlers.Add(68, HandleRoomGamePlay);     // 0x44: 게임 시작 알림
            _handlers.Add(70, HandleRoomChat);          // 0x46: 게임방 채팅 입력 전송
            _handlers.Add(73, HandleInGameNormalInfo);          // 0x49: 일반전 정보 전송
            _handlers.Add(80, HandleInGameNormalTurnEnd);        // 0x50: 턴종료
            _handlers.Add(84, HandleGameIdList);        // 0x54: 게임 아이디 리스트 요청
            _handlers.Add(85, HandleGameIdManage);      // 0x55: 게임 아이디 생성/삭제 요청
            _handlers.Add(157, HandleTournamentInfo);   // 0x9d: 토너먼트 정보 전송
            _handlers.Add(159, HandleTournamentApply);  // 0x9f: 토너먼트 공성 진출 신청
            _handlers.Add(256, HandleCostumeRoom);      // 0x100: 코스튬 관리로 입장
        }

        // 외부(GameServer)에서 호출할 단일 진입점
        public static void ProcessPacket(ClientSession session, ClientPacketParser packet)
        {
            if (_handlers.TryGetValue(packet.CommandId, out var handler))
            {
                handler.Invoke(session, packet);
            }
            else
            {
                Console.WriteLine($"[GamePacketHandler] 처리되지 않은 커맨드: {packet.CommandId}");
                HandleUnknownCommand(session, packet);
            }
        }

        // ---------------------------------------------------------------------
        // 브로드캐스트 유틸리티 (규칙 3. 핸들러가 조립한 바이트를 매니저에게 부탁해 전송)
        // ---------------------------------------------------------------------
        public static void BroadcastToRoom(IEnumerable<ClientSession> sessions, ServerPacketBuilder packetBuilder)
        {
            if (sessions == null) { return; }
            foreach (var targetSession in sessions)
            {
                targetSession.SendPacket(packetBuilder);
            }
        }

        // ---------------------------------------------------------------------
        // 개별 커맨드 로직들
        // ---------------------------------------------------------------------
        private static void HandleLogin(ClientSession session, ClientPacketParser packet)
        {
            byte[] payloadBytes = packet.PayloadBytes.ToArray();
            string id = eucKr.GetString(payloadBytes, 4, 12).TrimEnd('\0');

            ServerPacketBuilder serverPacket = new ServerPacketBuilder
            {
                PacketSizeGroup = 0,
                CommandId = 30
            };

            // 서버가 있는지 확인용으로 사용되는 아이디라면 차단
            if (id == "0xFFFFFFFFFF")
            {
                serverPacket.PacketData = new byte[1];
                serverPacket.BitOffset = 0;
                serverPacket.WriteBits(4, 15);
            }
            else
            {
                string gameId = eucKr.GetString(payloadBytes, 17, 12).TrimEnd('\0');
                int findId = 2; // 실패

                // 세션 메모리에서 유저 검색 (이미 HandleGameIdList에서 DB로부터 불러와 채워져 있음)
                for (int i = 0; i < session.GameAccounts.Count; i++)
                {
                    if (session.GameAccounts[i].GameId == gameId)
                    {
                        session.SelectedCharacter = session.GameAccounts[i];
                        findId = 0; // 성공
                        break;
                    }
                }

                serverPacket.PacketData = new byte[155];
                serverPacket.BitOffset = 0;
                serverPacket.WriteBits(4, findId);

                if (findId == 0 || findId == 1)
                {
                    serverPacket.WriteBits(5, session.SelectedCharacter.GameTier);
                    serverPacket.WriteBits(4, /*session.SelectedCharacter.UnkValue1*/0);
                    serverPacket.WriteBits(3, /*session.SelectedCharacter.UnkValue2*/0);

                    serverPacket.ByteOffset = 2;
                    serverPacket.WriteInt(/*session.SelectedCharacter.UnkValue3*/0);
                    serverPacket.WriteInt(/*session.SelectedCharacter.UnkValue4*/0);

                    byte[] guildName = eucKr.GetBytes(session.MyGuildName ?? string.Empty);
                    serverPacket.WriteBytes(12, guildName);

                    serverPacket.WriteInt(session.SelectedCharacter.ServerScore);
                    serverPacket.WriteInt(session.SelectedCharacter.ServerRank);
                    serverPacket.WriteInt(session.SelectedCharacter.GameCount);
                    serverPacket.WriteInt(session.SelectedCharacter.GameWins);
                    serverPacket.WriteInt(/*session.SelectedCharacter.UnkValue5*/0);
                    serverPacket.WriteInt(/*session.SelectedCharacter.UnkValue6*/0);
                    serverPacket.WriteInt(session.SelectedCharacter.MyCring);

                    serverPacket.BitOffset = 1232;
                    serverPacket.WriteBits(1, session.IsFemale ? 1 : 0);
                    serverPacket.WriteBits(4, session.MyGuildNameColor);
                }
            }

            session.SendPacket(serverPacket);
        }

        private static void HandleGoToLobby(ClientSession session, ClientPacketParser packet)
        {
            ServerPacketBuilder serverPacket = new ServerPacketBuilder
            {
                PacketSizeGroup = 3,
                CommandId = 31,
                PacketData = new byte[3]
            };

            serverPacket.WriteByte(1); // 이동 결과
            serverPacket.WriteShort(0); // 로비 값?

            session.SendPacket(serverPacket);
        }

        private static void HandleRoomInfo(ClientSession session, ClientPacketParser packet)
        {
            int roomIndex = (int)BitStream.ReadBits(packet.PayloadBytes.ToArray(), 32, 9);
            RoomManager.SendRoomInfo(session, roomIndex);
        }

        private static void HandleNotice(ClientSession session, ClientPacketParser packet)
        {
            ServerPacketBuilder serverPacket = new ServerPacketBuilder
            {
                PacketSizeGroup = 0,
                CommandId = 45,
                PacketData = new byte[21]
            };

            serverPacket.WriteByte(1);
            serverPacket.WriteInt(session.SelectedCharacter.ServerScore);
            serverPacket.WriteInt(session.SelectedCharacter.ServerRank);
            serverPacket.WriteInt(session.SelectedCharacter.GameCount);
            serverPacket.WriteInt(session.SelectedCharacter.GameWins);
            serverPacket.WriteInt(session.SelectedCharacter.MyCring);

            session.SendPacket(serverPacket);
        }

        private static void HandleCreateRoom(ClientSession session, ClientPacketParser packet)
        {
            byte[] pb = packet.PayloadBytes.ToArray();
            int gameType = (int)BitStream.ReadBits(pb, 32, 3);
            int maxUsers = (int)BitStream.ReadBits(pb, 35, 7);
            int titleLength = (int)BitStream.ReadBits(pb, 42, 6);
            string roomTitle = GetStringFromBytes(pb, 6, titleLength);
            int passwordLength = (int)pb[6 + titleLength];
            string roomPassword = GetStringFromBytes(pb, 7 + titleLength, passwordLength);

            // 비즈니스 로직 및 패킷 전송을 몽땅 RoomManager에게 위임
            RoomManager.CreateRoom(session, roomTitle, roomPassword, gameType, maxUsers);
        }

        private static void HandleRoomList(ClientSession session, ClientPacketParser packet)
        {
            byte[] payloadBytes = packet.PayloadBytes.ToArray();
            int startRoomIndex = (int)BitStream.ReadBits(payloadBytes, 32, 9); // 시작할 방번호 인덱스
            int roomType = (int)BitStream.ReadBits(payloadBytes, 41, 3); // 요청할 게임방 타입(4:전체 5:진짜 길드전 6:토너먼트)
            int roomStateFilter = (int)BitStream.ReadBits(payloadBytes, 44, 3);  // 요청할 방 목록(3: 대기중인 방만, 6: 전체 목록)
            bool privRooms = BitStream.ReadBits(payloadBytes, 47, 1) != 0 ? true : false; // 이전 방 요청

            RoomManager.SendRoomList(session, startRoomIndex, roomType, roomStateFilter, privRooms);
        }

        private static void HandleRoomJoin(ClientSession session, ClientPacketParser packet)
        {
            byte[] payloadBytes = packet.PayloadBytes.ToArray();
            int roomIndex = (int)BitStream.ReadBits(payloadBytes, 32, 10);
            int pwdLength = (int)BitStream.ReadBits(payloadBytes, 42, 6);
            string roomPassword = GetStringFromBytes(payloadBytes, 6, Math.Min(pwdLength, 10));

            RoomManager.JoinRoom(session, roomIndex, roomPassword);
        }

        private static void HandleRoomLeave(ClientSession session, ClientPacketParser packet)
        {
            RoomManager.LeaveRoom(session);

            // 유저가 방에 없었더라도 방을 나가는 신호를 보내면 53 커맨드 신호를 보냄
            // 이는 강퇴당한 유저는 클라이언트에서 자동으로 53 커맨드를 보내기 때문임
            session.CurrentRoomIndex = -1;
            session.SendPacket(new ServerPacketBuilder { PacketSizeGroup = 3, CommandId = 53, PacketData = new byte[0] });
        }

        private static void HandleRoomUserInfo(ClientSession session, ClientPacketParser packet)
        {
            RoomManager.SendRoomUserInfo(session);
        }

        private static void HandleSlotUpdate(ClientSession session, ClientPacketParser packet)
        {
            byte[] payloadBytes = packet.PayloadBytes.ToArray();
            int slotIndex = (int)BitStream.ReadBits(payloadBytes, 32, 5);
            int slotState = (int)BitStream.ReadBits(payloadBytes, 37, 3); // 0: 없음, 1: 대기 중, 2: 상점 이용 중, 3: 준비 중, 4: 방장, 5: 게임 중, 방장 상점 이용 중
            int targetSlot = (int)BitStream.ReadBits(payloadBytes, 40, 5);
            int slotCommand = (int)BitStream.ReadBits(payloadBytes, 45, 3); // 0: 이동 또는 상태변경, 1: 슬롯 닫기, 2: 슬롯 열기, 3: 해당 슬롯 유저 강퇴
            int teamIndex = (int)BitStream.ReadBits(payloadBytes, 48, 4);
            int tankIndex = (int)BitStream.ReadBits(payloadBytes, 52, 9); // 0: 카타펄트, 1: 캐논, 2: 크로스보우, 3: 캐롯 탱크,
                                                                          // 4: 듀크 탱크, 5: 마인랜더, 6: 미사일, 7: 멀티 미사일,
                                                                          // 8: 레이저 탱크, 9: 포세이돈, 10: 세크윈드, 11: 이온 어태커
                                                                          // 12: 아이언 해머, 13: 블래이저, 14: 윈드 블로우, 15: 워키토키,
                                                                          // 16: 솔라탱크, 17: 레인보우 쉘, 20: 슈퍼 탱크, 22: 드래곤 마스터,
                                                                          // 23: 캐논 마스터, 31: 랜덤


            if (8 <= teamIndex) { teamIndex &= 7; }
            if (18 <= tankIndex && tankIndex != 31) { tankIndex = 31; }

            RoomManager.UpdateSlot(session, slotIndex, slotState, targetSlot, slotCommand, teamIndex, tankIndex);
        }

        private static void HandleOptionRequest(ClientSession session, ClientPacketParser packet)
        {
            ServerPacketBuilder serverPacket = new ServerPacketBuilder();
            serverPacket.PacketSizeGroup = 0;
            serverPacket.CommandId = 58;
            serverPacket.Flags = 0;

            serverPacket.PacketData = new byte[525];
            //short customPacketSize = (short)((535 + 3) & ~3);
            //Array.Copy(BitConverter.GetBytes(customPacketSize), 0, data, 0, 2);

            serverPacket.WriteByte(1); // 옵션 데이터 여부(0: 없음, 1: 있음)
            for (int i = 0; i < 8; i++)
            {
                byte[] macroChatBytes = eucKr.GetBytes(session.SelectedCharacter.AccountSetting.MacroChat[i] ?? string.Empty);
                serverPacket.WriteBytes(59, macroChatBytes);
            }

            byte[] roomTitleBytes = eucKr.GetBytes(session.SelectedCharacter.AccountSetting.DefaultRoomTitle ?? string.Empty);
            serverPacket.WriteBytes(40, roomTitleBytes);

            serverPacket.ByteOffset = 515;
            serverPacket.WriteByte((byte)session.SelectedCharacter.AccountSetting.BGMVolume);
            serverPacket.WriteByte((byte)session.SelectedCharacter.AccountSetting.SFXVolume);
            serverPacket.WriteByte((byte)session.SelectedCharacter.AccountSetting.ScreenBrightness);
            serverPacket.WriteByte((byte)(session.SelectedCharacter.AccountSetting.IsHighGraphic ? 1 : 0));
            serverPacket.WriteByte((byte)session.SelectedCharacter.AccountSetting.QuickMathPlayer);
            serverPacket.WriteByte((byte)session.SelectedCharacter.AccountSetting.QuickMathStage);
            serverPacket.WriteByte((byte)session.SelectedCharacter.AccountSetting.QuickMathTierRestriction);

            session.SendPacket(serverPacket);
        }

        private static void HandleOptionChange(ClientSession session, ClientPacketParser packet)
        {
            byte[] payloadBytes = packet.PayloadBytes.ToArray();
            for (int i = 0; i < 8; i++)
            {
                string macroChat = GetStringFromBytes(payloadBytes, 6 + (i * 59), 59);
                session.SelectedCharacter.AccountSetting.MacroChat[i] = macroChat;
            }
            string roomTitle = GetStringFromBytes(payloadBytes, 478, 40);
            session.SelectedCharacter.AccountSetting.DefaultRoomTitle = roomTitle;

            session.SelectedCharacter.AccountSetting.BGMVolume = payloadBytes[520];
            session.SelectedCharacter.AccountSetting.SFXVolume = payloadBytes[521];
            session.SelectedCharacter.AccountSetting.ScreenBrightness = payloadBytes[522];
            session.SelectedCharacter.AccountSetting.IsHighGraphic = payloadBytes[523] != 0;
            session.SelectedCharacter.AccountSetting.QuickMathPlayer = payloadBytes[524];
            session.SelectedCharacter.AccountSetting.QuickMathStage = payloadBytes[525];
            session.SelectedCharacter.AccountSetting.QuickMathTierRestriction = payloadBytes[526];

            // --- [DB 연동] 데이터베이스 업데이트 ---
            AccountRepository.UpdateGameAccountSetting(session.SelectedCharacter);

            ServerPacketBuilder serverPacket = new ServerPacketBuilder();
            serverPacket.PacketSizeGroup = 3;
            serverPacket.CommandId = 59;
            serverPacket.Flags = 0;

            session.SendPacket(serverPacket);
        }

        private static void HandleRoomUpdate(ClientSession session, ClientPacketParser packet)
        {
            byte[] payloadBytes = packet.PayloadBytes.ToArray();
            int u1 = (int)BitStream.ReadBits(payloadBytes, 32, 4);
            int u2 = (int)BitStream.ReadBits(payloadBytes, 36, 4);
            int u3 = (int)BitStream.ReadBits(payloadBytes, 40, 4);
            int u4 = (int)BitStream.ReadBits(payloadBytes, 44, 4);
            int u5 = (int)BitStream.ReadBits(payloadBytes, 48, 4);
            int mapIndex = (int)BitStream.ReadBits(payloadBytes, 52, 7);
            int minTier = (int)BitStream.ReadBits(payloadBytes, 59, 5);

            RoomManager.UpdateRoom(session, mapIndex, minTier, u1, u2, u3, u4, u5);
        }

        private static void HandleRoomTitleChange(ClientSession session, ClientPacketParser packet)
        {
            byte[] pb = packet.PayloadBytes.ToArray();
            RoomManager.UpdateRoomTitle(session, GetStringFromBytes(pb, 7, Math.Min((int)pb[6], 40)));
        }

        private static void HandleRoomGameStart(ClientSession session, ClientPacketParser packet)
        {
            RoomManager.SetGameLoading(session);
        }

        private static void HandleRoomGamePlay(ClientSession session, ClientPacketParser packet)
        {
            RoomManager.SetGamePlaying(session);
        }

        private static void HandleRoomChat(ClientSession session, ClientPacketParser packet)
        {
            byte[] pb = packet.PayloadBytes.ToArray();
            int unkValue1 = (int)BitStream.ReadBits(pb, 48, 4);
            int userIndex = (int)BitStream.ReadBits(pb, 52, 5);
            string chatMsg = GetStringFromBytes(pb, 8, 58);

            RoomManager.BroadcastRoomChat(session, unkValue1, userIndex, chatMsg);
        }

        private static void HandleInGameNormalInfo(ClientSession session, ClientPacketParser packet)
        {
            byte[] payloadBytes = packet.PayloadBytes.ToArray();
            int slotIndex = (int)BitStream.ReadBits(payloadBytes, 32, 5);
            int actionType = (int)BitStream.ReadBits(payloadBytes, 37, 4);
            int xPos = (int)BitStream.ReadBits(payloadBytes, 41, 12);
            int yPos = (int)BitStream.ReadBits(payloadBytes, 53, 11);
            int power = (int)BitStream.ReadBits(payloadBytes, 64, 8);
            int angle = (int)BitStream.ReadBits(payloadBytes, 72, 9);
            int itemIndex = (int)BitStream.ReadBits(payloadBytes, 81, 9);

            if (actionType == 1)
            {
                Console.WriteLine($"이동/각도 조절:\nslotIndex: {slotIndex} actionType: {actionType} xPos: {xPos} yPos: {yPos} 없음1: {power} 각도: {angle} 없음2: {itemIndex}");
            }
            else if (actionType == 2)
            {
                Console.WriteLine($"탄 발사 준비:\nslotIndex: {slotIndex} actionType: {actionType} xPos: {xPos} yPos: {yPos} 없음1: {power} 없음2: {angle} 좌측으로 발사: {itemIndex}");
            }
            else if (actionType == 3)
            {
                Console.WriteLine($"탄 발사:\nslotIndex: {slotIndex} actionType: {actionType} xPos: {xPos} yPos: {yPos} 파워: {power} 각도: {angle} 특수탄 여부: {itemIndex}");
            }
            else if (actionType == 5)
            {
                Console.WriteLine($"턴 넘기기:\nslotIndex: {slotIndex} actionType: {actionType} xPos: {xPos} yPos: {yPos} power: {power} angle: {angle} itemIndex: {itemIndex}");
            }
            else if (actionType == 7)
            {
                Console.WriteLine($"사망:\nslotIndex: {slotIndex} actionType: {actionType} xPos: {xPos} yPos: {yPos} power: {power} angle: {angle} itemIndex: {itemIndex}");
            }
            else if (actionType == 8)
            {
                Console.WriteLine($"아이템 사용:\nslotIndex: {slotIndex} actionType: {actionType} xPos: {xPos} yPos: {yPos} power: {power} angle: {angle} itemIndex: {itemIndex}");
            }
            else if (actionType == 9)
            {
                Console.WriteLine($"아이템 획득:\nslotIndex: {slotIndex} actionType: {actionType} xPos: {xPos} yPos: {yPos} power: {power} angle: {angle} itemIndex: {itemIndex}");
            }
            else if (actionType == 10)
            {
                Console.WriteLine($"탈락?:\nslotIndex: {slotIndex} actionType: {actionType} xPos: {xPos} yPos: {yPos} power: {power} angle: {angle} itemIndex: {itemIndex}");
            }
            else
            {
                Console.WriteLine($"알수없음:\nslotIndex: {slotIndex} actionType: {actionType} xPos: {xPos} yPos: {yPos} power: {power} angle: {angle} itemIndex: {itemIndex}");
            }

            RoomManager.ProcessGameNormalInfo(session, slotIndex, actionType, xPos, yPos, power, angle, itemIndex);
        }

        private static void HandleInGameNormalTurnEnd(ClientSession session, ClientPacketParser packet)
        {
            int roomIndex = session.CurrentRoomIndex;
            if (roomIndex == -1) { return; }

            byte[] payloadBytes = packet.PayloadBytes.ToArray();
            int slotIndex = (int)BitStream.ReadBits(payloadBytes, 32, 5);
            int unknown = (int)BitStream.ReadBits(payloadBytes, 37, 5);
            int turnCount = (int)BitStream.ReadBits(payloadBytes, 42, 11);
            int delay = (int)BitStream.ReadBits(payloadBytes, 53, 11);

            Console.WriteLine($"slotIndex: {slotIndex}, unknown: {unknown}, turnCount: {turnCount}, delay: {delay}");

            RoomManager.ProcessTurnEnd(session, unknown, turnCount, delay);
        }

        private static void HandleGameIdList(ClientSession session, ClientPacketParser packet)
        {
            byte[] payloadBytes = packet.PayloadBytes.ToArray();
            int nullIndex = Array.IndexOf(payloadBytes, (byte)'\0', 4) - 4;
            int length = (0 <= nullIndex) ? nullIndex : 12;

            string id = eucKr.GetString(payloadBytes, 4, length);

            ServerPacketBuilder serverPacket = new ServerPacketBuilder();
            serverPacket.PacketSizeGroup = 0;
            serverPacket.Flags = 0;

            if (id == "0xFFFFFFFFFF")
            {
                serverPacket.CommandId = 30;
                serverPacket.PacketData = new byte[4];
                //Array.Copy(BitConverter.GetBytes((short)168), 0, data, 0, 2);
                serverPacket.WriteBits(4, 15);
            }
            else
            {
                session.AccountId = id;

                // --- [DB 연동] 데이터베이스에서 로그인 계정 세부 정보 불러오기 ---
                AccountRepository.GetLoginAccount(session.AccountId, out string guildName, out int guildColor, out bool isFemale);
                session.MyGuildName = guildName;
                session.MyGuildNameColor = guildColor;
                session.IsFemale = isFemale;

                // --- [DB 연동] 데이터베이스에서 진짜 캐릭터 목록 불러오기 ---
                session.GameAccounts = AccountRepository.GetGameAccounts(session.AccountId);

                serverPacket.CommandId = 84;
                serverPacket.PacketData = new byte[1 + session.GameAccounts.Count * 13];

                serverPacket.WriteByte((byte)session.GameAccounts.Count);

                for (int i = 0; i < session.GameAccounts.Count; i++)
                {
                    //serverPacket.ByteOffset = 8 + (i * 13);
                    serverPacket.WriteBits(5, session.GameAccounts[i].GameTier);
                    byte[] gameIdName = eucKr.GetBytes(session.GameAccounts[i].GameId ?? string.Empty);
                    serverPacket.WriteBytes(12, gameIdName);
                }
            }

            session.SendPacket(serverPacket);
        }

        private static void HandleGameIdManage(ClientSession session, ClientPacketParser packet)
        {
            byte[] payloadBytes = packet.PayloadBytes.ToArray();
            int gameIdCommand = payloadBytes[6];

            ServerPacketBuilder serverPacket = new ServerPacketBuilder();
            serverPacket.PacketSizeGroup = 6;
            serverPacket.CommandId = 85;
            serverPacket.Flags = 0;

            serverPacket.PacketData = new byte[1];

            int createResult = 0;
            if (gameIdCommand == 1) // 생성
            {
                string gameId = GetStringFromBytes(payloadBytes, 7, 12);

                if (3 <= session.GameAccounts.Count)
                {
                    createResult = 0; // 슬롯 제한(3개) 초과 실패
                }
                else
                {
                    // --- [DB 연동] 데이터베이스에 새 캐릭터 저장 ---
                    bool isCreated = AccountRepository.CreateGameAccount(session.AccountId, gameId);
                    if (isCreated)
                    {
                        createResult = 1; // 생성 성공
                        // DB 변경 사항을 메모리(세션) 리스트와 동기화
                        session.GameAccounts = AccountRepository.GetGameAccounts(session.AccountId);
                    }
                    else
                    {
                        createResult = 0; // 이미 존재하는 닉네임 등 실패
                    }
                }
            }
            else if (gameIdCommand == 2) // 삭제
            {
                string gameId = GetStringFromBytes(payloadBytes, 7, 12);
                string password = GetStringFromBytes(payloadBytes, 20, 12);

                // --- [DB 연동] 비밀번호가 맞는지 DB에서 확인 후 캐릭터 삭제 ---
                bool isDeleted = AccountRepository.DeleteGameAccount(session.AccountId, gameId, password);

                if (isDeleted)
                {
                    createResult = 1; // 삭제 성공
                    session.GameAccounts = AccountRepository.GetGameAccounts(session.AccountId);
                }
                else
                {
                    createResult = 2; // 비밀번호 불일치 (포트리스 규격상 2번)
                }
            }

            serverPacket.WriteByte((byte)createResult);
            session.SendPacket(serverPacket);
        }

        private static void HandleTournamentInfo(ClientSession session, ClientPacketParser packet)
        {
            byte[] payloadBytes = packet.PayloadBytes.ToArray();

            ServerPacketBuilder serverPacket = new ServerPacketBuilder();
            serverPacket.CommandId = 157;
            serverPacket.Flags = 0;

            if (payloadBytes[4] == 1)
            {
                serverPacket.PacketSizeGroup = 0;

                serverPacket.PacketData = new byte[107];

                //short customPacketSize = (short)((117 + 3) & ~3);
                //Array.Copy(BitConverter.GetBytes(customPacketSize), 0, data, 0, 2);
                serverPacket.WriteByte(1);
                serverPacket.WriteByte(2);
                serverPacket.WriteByte(0);
            }
            else
            {
                serverPacket.PacketData = new byte[4];
                serverPacket.PacketSizeGroup = 3;
                serverPacket.WriteByte(0);
            }

            session.SendPacket(serverPacket);
        }

        private static void HandleTournamentApply(ClientSession session, ClientPacketParser packet)
        {
            byte[] payloadBytes = packet.PayloadBytes.ToArray();

            ServerPacketBuilder serverPacket = new ServerPacketBuilder();
            serverPacket.PacketSizeGroup = 3;
            serverPacket.CommandId = 159;
            serverPacket.Flags = 0;

            serverPacket.PacketData = new byte[5];

            if (payloadBytes[4] == 1)
            {
                serverPacket.WriteByte(6);
                serverPacket.WriteInt(0);
            }
            else if (payloadBytes[4] == 2)
            {
                serverPacket.WriteByte(1);
            }
            else if (payloadBytes[4] == 3)
            {
                serverPacket.WriteByte(7);
                serverPacket.WriteInt(0);
            }

            session.SendPacket(serverPacket);
        }

        // 아직 분석이 되지 않은 코스튬 이동 명령(256)
        private static void HandleCostumeRoom(ClientSession session, ClientPacketParser packet)
        {
            ServerPacketBuilder serverPacket = new ServerPacketBuilder();
            serverPacket.PacketSizeGroup = 0;
            serverPacket.CommandId = 256;
            serverPacket.Flags = 0;

            serverPacket.PacketData = new byte[3];

            session.SendPacket(serverPacket);
        }

        // Default 커맨드 처리
        private static void HandleUnknownCommand(ClientSession session, ClientPacketParser packet)
        {
            ServerPacketBuilder serverPacket = new ServerPacketBuilder();
            serverPacket.PacketSizeGroup = 3;
            serverPacket.CommandId = 0;
            serverPacket.Flags = 0;

            serverPacket.PacketData = new byte[2];

            session.SendPacket(serverPacket);
        }

        private static int GetSlotStete(UserState userState, bool isHost)
        {
            if (userState == UserState.Loading || userState == UserState.InGameAlive || userState == UserState.InGameDead)
            {
                return 5;
            }
            else if (isHost)
            {
                if (userState == UserState.Shoping)
                {
                    return 6;
                }
                else
                {
                    return 4;
                }
            }
            else
            {
                if (userState == UserState.Ready)
                {
                    return 3;
                }
                else if (userState == UserState.Shoping)
                {
                    return 2;
                }
                else
                {
                    return 1;
                }
            }
        }



        // 문자열 디코딩 시 Null 문자 뒤의 쓰레기 값을 완벽하게 제거하는 헬퍼 메서드
        private static string GetStringFromBytes(byte[] buffer, int offset, int maxLength)
        {
            // 1. 최대 길이 내에서 첫 번째 0x00(Null) 바이트의 위치를 찾습니다.
            int nullIndex = -1;
            for (int i = 0; i < maxLength; i++)
            {
                if (buffer[offset + i] == 0x00)
                {
                    nullIndex = i;
                    break;
                }
            }

            // 2. 널 문자가 발견되었다면 그 앞까지만 길이를 잡고, 아니면 최대 길이를 다 씁니다.
            int actualLength = (nullIndex >= 0) ? nullIndex : maxLength;

            // 3. 딱 필요한 길이만큼만 EUC-KR로 디코딩합니다!
            return eucKr.GetString(buffer, offset, actualLength);
        }
    }
}