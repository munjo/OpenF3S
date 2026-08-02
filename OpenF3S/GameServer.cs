using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace Fortress3PaewangServerTest
{
    internal class GameServer
    {
        public ushort Port { get; set; }
        public int Id { get; set; }
        public int Index { get; set; }
        public int Type { get; set; }
        public int CurrentUserCount { get => _currentUserCount; }
        public int MaxUserCount { get; set; }
        public string LordGuild { get; set; }
        public string LordName { get; set; }
        public bool Enabled { get; set; }

        private int _currentUserCount;

        private const uint SerEncKeySeed1 = 0;
        private const uint SerEncKeySeed2 = 0;
        private const uint CliEncKeySeed1 = 0;
        private const uint CliEncKeySeed2 = 0;

        // 이름을 미리 정의함
        public static readonly string[] GameServerName = {
            "카타펄트","크로스보우","캐논탱크","듀크탱크","캐롯탱크",
            "미사일탱크", "멀티미슬","포세이돈","레이저탱크","마인랜더",
            "아이온어태커","세크윈드","아이언해머","블레이저", "연습용서버",
            "테스트서버", "윈드블로우", "워키토키", "초보자서버"
        };

        public GameServer(ushort port)
        {
            Port = port;
        }

        // 비동기 Start 메서드
        public async Task StartAsync()
        {
            TcpListener listener = new TcpListener(IPAddress.Any, Port);
            listener.Start();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"게임서버: '{GameServerName[Index]}'게임 서버가 시작되었습니다. (포트: {Port})");
            Console.ResetColor();

            while (true)
            {
                try
                {
                    // 스레드를 막지 않고 클라이언트 접속을 비동기로 기다림
                    TcpClient client = await listener.AcceptTcpClientAsync();
                    string clientIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                    Console.WriteLine($"\n게임서버[{clientIp}:{Port}]: 클라이언트 접속: {clientIp}");

                    _ = HandleClientAsync(client, clientIp);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"게임서버: GameServer 오류({ex.Message})");
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, string clientIp)
        {
            // 1. 해당 클라이언트만을 위한 '독립된 세션 객체' 생성
            ClientSession session = new ClientSession(client, clientIp);
            //// 2. 테스트용 임시 계정 데이터 셋업 (나중에는 DB에서 불러오게 될 부분)
            //session.GameAccounts.Add(new GameAccountInfo("test01") { GameTier = 15, ServerScore = 100, ServerRank = 1, GameCount = 10, GameWins = 5, MyCring = 99999999 });
            //session.GameAccounts.Add(new GameAccountInfo("test02") { ServerRank = 2 });
            //session.MyGuildNameColor = 8; // 인덱스별 색상
            //                              // 00: #ec008c, 01: #f26522, 02: #f5989d, 03: #bd8c8d,
            //                              // 04: #a286c0, 05: #7da7d9, 06: #6dcff6, 07: #1cbbb4,
            //                              // 08: #7accc8, 09: #3cb878, 10: #a3d29c, 11: #fff799,
            //                              // 12: #a67c52, 13: #c7b29a, 14: #fbaf5d, 15: #fdc689

            StringBuilder sb = new StringBuilder();
            try
            {
                int bytesRead;
                bool first = true;

                // 세션 단위의 최초 암호화 키 초기화
                session.InitializeCryptionKey(0x4d2, 0x162e, 0x10e1, 0x2334);

                // 패킷 쪼개짐/뭉침 방어 루프 시작
                while ((bytesRead = await session.Stream.ReadAsync(session.ReceiveBuffer, session.BufferDataLength, session.ReceiveBuffer.Length - session.BufferDataLength)) != 0)
                {
                    // 이번에 받은 데이터만큼 누적 길이 증가
                    session.BufferDataLength += bytesRead;

                    // 2. 버퍼에 최소 헤더 크기(8바이트) 이상이 모였는지 확인
                    while (8 <= session.BufferDataLength)
                    {
                        byte[] currentBufferArray = session.ReceiveBuffer.ToArray();

                        // 3. 파서를 이용해 암호화된 헤더를 살짝 열어보고, 패킷의 '예상 전체 길이'를 가져옴
                        int expectedPacketSize = ClientPacketParser.GetPacketSize(currentBufferArray, 0, session.DecryptionKey);

                        if (expectedPacketSize == 0)
                        {
                            throw new Exception("잘못된 패킷 규격입니다. (매직넘버 불일치 또는 크기 0)");
                        }

                        // 4. [방어] 만약 패킷이 네트워크 렉으로 인해 쪼개져서 아직 덜 왔다면?
                        if (session.BufferDataLength < expectedPacketSize)
                        {
                            break; // 내부 while 문을 탈출하여 다음 stream.Read()에서 나머지 데이터가 오기를 얌전히 기다립니다.
                        }

                        // 5. 완벽하게 하나의 패킷이 버퍼에 조립되었으므로, 해당 크기만큼만 나눈다.
                        byte[] rawPacket = new byte[expectedPacketSize];
                        Array.Copy(session.ReceiveBuffer, 0, rawPacket, 0, expectedPacketSize);

                        // 처리가 끝난 패킷 데이터만큼 버퍼를 앞으로 당겨서 지워줌 (Sliding)
                        session.SlideBuffer(expectedPacketSize);

                        sb.Clear();
                        sb.AppendLine($"\n게임서버[{clientIp}:{Port}]: 수신({bytesRead} 바이트)");
                        foreach (byte b in rawPacket)
                        {
                            sb.AppendFormat("{0} ", b.ToString("X2"));
                        }
                        Console.WriteLine(sb.ToString());

                        // 6. 인게임 패킷 해독 및 처리
                        ClientPacketParser clientPacket = new ClientPacketParser();
                        bool result = clientPacket.ParseAuthRequest(rawPacket, session.DecryptionKey);

                        // 패킷 문제
                        if (!result)
                        {
                            throw new Exception("올바른 패킷이 아님");
                        }

                        // 클라이언트에서 처음으로 데이터를 받은 후 로그인 서버에서 받은 암호화 키로 변경
                        if (first)
                        {
                            // 일단 따로 암호화키를 생성하지 않으므로 0으로 통일
                            session.InitializeCryptionKey(0, 0, 0, 0);
                            first = false;
                        }

                        // [핵심] 하나의 패킷 처리가 끝났으므로 복호화 키를 다음 단계로 전진
                        session.UpdateDecryptionKey();

                        // 7. [라우팅] 거대한 switch문 대신 패킷 핸들러(라우터)에게 처리를 위임
                        GamePacketHandler.ProcessPacket(session, clientPacket);
                    }
                }
            }
            catch (Exception e)
            { 
                Console.WriteLine($"게임서버[{clientIp}:{Port}]: 통신 오류({e.Message})"); 
            }
            finally 
            {
                // 팅겼을 때 혹시 방에 있었다면 나가기 처리
                RoomManager.LeaveRoom(session);

                session.Close();
                Console.WriteLine($"게임서버[{clientIp}:{Port}]: 클라이언트 연결이 종료되었습니다.\n");
            }
        }
    }
}