using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using static System.Collections.Specialized.BitVector32;

namespace Fortress3PaewangServerTest
{
    internal class LoginServer
    {
        public ServerDataBase[] GameServers { get => _gameServers; }

        private ServerDataBase[] _gameServers = new ServerDataBase[19];

        private int _port;

        // [신규] 생성된 서버 목록 패킷을 보관할 캐시 버퍼 (스레드 안전 보장)
        private byte[] _cachedServerListPacket;

        //private const uint SerEncKeySeed1 = 0x2a49c7b5;
        //private const uint SerEncKeySeed2 = 0xa1c6622a;
        //private const uint CliEncKeySeed1 = 0x52d7823e;
        //private const uint CliEncKeySeed2 = 0xd10d0d62;
        private const uint SerEncKeySeed1 = 0;
        private const uint SerEncKeySeed2 = 0;
        private const uint CliEncKeySeed1 = 0;
        private const uint CliEncKeySeed2 = 0;

        public LoginServer(int port)
        {
            _port = port;
            for(int i = 0; i < 19; i++)
            {
                _gameServers[i] = new ServerDataBase(i);
            }
        }

        public async Task StartAsync()
        {
            TcpListener listener = new TcpListener(IPAddress.Any, _port);
            listener.Start();
            Console.WriteLine($"[LoginServer] 로그인 서버가 시작되었습니다. (포트: {_port})");

            // 최초 1회 캐시 생성
            _cachedServerListPacket = GenerateServerListPacket();

            // 주기적으로 서버 목록 캐시를 갱신하는 백그라운드 스레드 실행 (3초 간격)
            _ = Task.Run(UpdateServerListCacheLoop);

            while (true)
            {
                try
                {
                    // AcceptTcpClientAsync를 사용하여 클라이언트 접속을 비동기적으로 대기합니다.
                    TcpClient client = await listener.AcceptTcpClientAsync();
                    string clientIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();

                    // 각 클라이언트 접속을 독립적인 Task로 처리하여 메인 루프가 막히지 않게 합니다.
                    _ = HandleClientAsync(client, clientIp);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LoginServer 오류] {ex.Message}");
                }
            }
        }

        public async Task HandleClientAsync(TcpClient client, string clientIp)
        {
            Console.WriteLine($"[{clientIp}] 로그인 서버에 연결되었습니다!");

            try
            {
                NetworkStream stream = client.GetStream();

                // 안전장치 1: 수신 타임아웃 설정 (5초)
                stream.ReadTimeout = 5000;

                byte[] buffer = new byte[1024];
                int bytesRead;

                // [최적화] string += 연산 대신 StringBuilder를 사용하여 메모리 할당을 최소화합니다.
                const int MaxAllowedBufferSize = 1024;
                StringBuilder requestBuffer = new StringBuilder();

                // [최적화] ReadAsync를 사용하여 데이터 수신을 비동기적으로 대기합니다.
                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                {
                    // 1. 수신된 데이터를 ASCII 문자열로 누적
                    requestBuffer.Append(Encoding.ASCII.GetString(buffer, 0, bytesRead));

                    // 안전장치 3: 누적된 버퍼 크기가 제한 값을 초과했는지 즉시 검사
                    if (MaxAllowedBufferSize < requestBuffer.Length)
                    {
                        Console.WriteLine($"[{clientIp}] 허용된 최대 버퍼 크기 초과로 연결을 차단합니다.");
                        break;
                    }

                    string currentRequest = requestBuffer.ToString();
                    // 2. HTTP 요청의 끝을 알리는 문자가 도착했는지 확인
                    if (!currentRequest.Contains("\r\n\r\n") && !currentRequest.Contains("\n\r\n\r"))
                    {
                        continue;
                    }

                    Console.WriteLine($"\n[{clientIp} 로그인 서버 수신] {currentRequest.Trim()}");

                    // 3. 단일 패킷 파싱 (ref string 대신 값 복사 후 반환하도록 구조를 변경할 수도 있지만, 여기선 기존 틀 유지)
                    int returnType = ExtractHexString(ref currentRequest);
                    byte[] hexResponseBytes;

                    switch (returnType)
                    {
                        case 1:
                            byte[] packetBytes = HexStringToByteArray(currentRequest);
                            byte[] payloadBytes = LoginRequest(packetBytes);
                            hexResponseBytes = Encoding.ASCII.GetBytes(GetHexString(payloadBytes));
                            break;
                        case 2:
                            hexResponseBytes = _cachedServerListPacket;
                            break;
                        case 3:
                            hexResponseBytes = new byte[] { 0x0, 0x1, 0x0, 0x0 };
                            break;
                        default:
                            hexResponseBytes = new byte[] { 0x0, 0x0, 0x0, 0x0 };
                            break;
                    }

                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine($"[{clientIp} 로그인 서버 송신] ");
                    foreach (byte b in hexResponseBytes)
                    {
                        sb.AppendFormat("{0} ", b.ToString("X2"));
                    }
                    Console.WriteLine(sb.ToString());

                    // 4. [최적화] 응답 전송도 비동기적으로 처리
                    await stream.WriteAsync(hexResponseBytes, 0, hexResponseBytes.Length);

                    // 5. 응답을 보냈으므로 클라이언트가 대기하지 않도록 즉시 루프를 탈출
                    break;
                }
            }
            catch (IOException ex) when (ex.InnerException is SocketException sx && sx.SocketErrorCode == SocketError.TimedOut)
            {
                Console.WriteLine($"[{clientIp}] 데이터 수신 타임아웃(5초) 조건 충족으로 연결을 강제 종료합니다.");
            }
            catch (Exception e)
            {
                Console.WriteLine($"[{clientIp}] 통신 오류: {e.Message}");
            }
            finally
            {
                client.Close();
                Console.WriteLine($"[{clientIp}] 서버가 연결을 종료했습니다.\n");
            }
        }

        private byte[] LoginRequest(byte[] packetBytes)
        {
            byte[] data = new byte[28];
            int serverCommandId;
            int errorCode = 0;

            // 2. 바이트 배열로 변환
            byte[] decKeyBytes;
            byte[] encKeyBytes;

            // 패킷이 최소한 키값(16바이트)은 포함하고 있는지 확인
            if (packetBytes.Length < 16)
            {
                Console.WriteLine("오류: 패킷 길이가 너무 짧습니다.");
                throw new ArgumentException("The packet length is too short. It must be at least 16 bytes.", nameof(packetBytes));
            }

            // 1. 클라이언트 -> 서버 (해독용 키 계산 : 오프셋 0 ~ 7)
            // 0~3바이트와 4~7바이트를 각각 32비트(4바이트) 정수로 변환하여 XOR 연산
            uint clientKeyPart1 = BitConverter.ToUInt32(packetBytes, 0);
            uint clientKeyPart2 = BitConverter.ToUInt32(packetBytes, 4);
            uint decryptionKey = clientKeyPart1 ^ clientKeyPart2;

            // 2. 서버 -> 클라이언트 (암호화용 키 계산 : 오프셋 8 ~ 15)
            // 8~11바이트와 12~15바이트를 각각 32비트(4바이트) 정수로 변환하여 XOR 연산
            uint serverKeyPart1 = BitConverter.ToUInt32(packetBytes, 8);
            uint serverKeyPart2 = BitConverter.ToUInt32(packetBytes, 12);
            uint encryptionKey = serverKeyPart1 ^ serverKeyPart2;

            // [확인용 출력] 계산된 키를 다시 바이트 배열로 바꿔서 찍어보기
            decKeyBytes = BitConverter.GetBytes(decryptionKey);
            encKeyBytes = BitConverter.GetBytes(encryptionKey);

            Console.WriteLine($"[클라이언트 해독 키] : {decKeyBytes[0]:X2} {decKeyBytes[1]:X2} {decKeyBytes[2]:X2} {decKeyBytes[3]:X2}");
            Console.WriteLine($"[서버 암호화 키] : {encKeyBytes[0]:X2} {encKeyBytes[1]:X2} {encKeyBytes[2]:X2} {encKeyBytes[3]:X2}\n");

            // 3. 실제 암호화된 페이로드(데이터) 분리 (오프셋 16부터 끝까지)
            int dataPacketLength = packetBytes.Length - 16;
            byte[] dataPacketBytes = new byte[dataPacketLength];
            Array.Copy(packetBytes, 16, dataPacketBytes, 0, dataPacketLength);

            ClientPacketParser clientPacket = new ClientPacketParser();
            bool result = clientPacket.ParseAuthRequest(dataPacketBytes, decryptionKey);

            if (result && clientPacket.CommandId == 18)
            {
                // 받아온 아이디와 비밀번호
                string id = Encoding.ASCII.GetString(clientPacket.PayloadBytes.ToArray(), 4, 12).TrimEnd('\0');
                string password = Encoding.ASCII.GetString(clientPacket.PayloadBytes.ToArray(), 16, 12).TrimEnd('\0');

                bool isDbValid = AccountRepository.ValidateLogin(id, password);

                if ((id == "0xFFFFFFFFFF" && password == "0xFFFFFFFFFF") || isDbValid)
                {
                    serverCommandId = 19;
                }
                else
                {
                    Console.WriteLine("오류: 전송받은 아이디가 존재하지 않거나 비밀번호가 다릅니다.");
                    serverCommandId = 32;
                    errorCode = 2;
                }

                /*
                 * 해당 위치에서 게임서버로 로그인서버에 로그인한 계정 정보 보내기
                 * 만약 게임서버에 중복된 로그인 계정이 있다면 게임서버에서 접속된 계정 종료시키기 위함
                */
            }
            else
            {
                serverCommandId = 32;
                errorCode = 4;
            }

            ServerPacketBuilder serverPacketBuilder = new ServerPacketBuilder();
            serverPacketBuilder.PacketSizeGroup = 5;
            serverPacketBuilder.CommandId = serverCommandId;

            if (serverCommandId == 19) // 9-2. 로그인 성공 시
            {
                // 1) 게임 서버에서 클라이언트와 서버와의 통신 때 사용할 암호화 키
                // 0~7바이트: 서버에서 암호화에 사용할 키, 8~15바이트: 클라이언트에서 암호화에 사용할 키
                serverPacketBuilder.PacketData = new byte[24];
                serverPacketBuilder.WriteInt((int)SerEncKeySeed1);
                serverPacketBuilder.WriteInt((int)SerEncKeySeed2);
                serverPacketBuilder.WriteInt((int)CliEncKeySeed1);
                serverPacketBuilder.WriteInt((int)CliEncKeySeed2);
                    
                // 2) 9-4. PC방 검사 결과 값 (4바이트)
                // 7: 등록된 PC방, 5: 등록되지 않은 PC방, 그 외: PC방이 아님?
                serverPacketBuilder.ByteOffset = 20;
                serverPacketBuilder.WriteInt(3);
            }
            else // 로그인 실패 시
            {
                serverPacketBuilder.PacketData = new byte[4];
                // 9-2. 오류 값 (4바이트)
                serverPacketBuilder.WriteInt(errorCode);
            }

            serverPacketBuilder.BuildResponse(encryptionKey);
            return serverPacketBuilder.PayloadBytes.ToArray();
        }

        // 3초마다 서버 목록을 갱신하는 백그라운드 루프
        private async Task UpdateServerListCacheLoop()
        {
            while (true)
            {
                try
                {
                    await Task.Delay(10000); // 10초 대기 (서버 부하 및 갱신 주기 고려)

                    // 패킷을 새로 조립하여 참조(포인터)만 갈아끼움
                    _cachedServerListPacket = GenerateServerListPacket();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LoginServer Cache Update Error] {ex.Message}");
                }
            }
        }

        private byte[] GenerateServerListPacket()
        {
            short serverCount = (short)_gameServers.Length;
            // 1. 헤더 크기(2바이트) + (서버 개수 * 63바이트)

            // 데이터 크기를 4의 배수로 보정함
            int adjustedSize = 2 + (serverCount * 63);
            adjustedSize = (adjustedSize + 3) & ~3;

            byte[] finalPacket = new byte[adjustedSize];

            // 2. 헤더 쓰기(서버 개수) - 빅 엔디안 (short)
            finalPacket[0] = (byte)(serverCount >> 8);
            finalPacket[1] = (byte)(serverCount & 0xFF);

            // 3. 서버 블록들 이어 붙이기
            for (int i = 0; i < _gameServers.Length; i++)
            {
                WriteServerToBuffer(_gameServers[i], finalPacket, i);
            }

            return finalPacket;
        }

        // 서버 한 개를 리스트에 추가하는 메서드
        private void WriteServerToBuffer(ServerDataBase server, byte[] buffer, int serverIndex)
        {
            if (server == null)
            {
                return;
            }

            // 서버개수 헤더(2바이트) + 서버 데이터(63바이트)
            int offset = 2 + serverIndex * 63;

            // 1. 서버 이름 (최대 16바이트, 남은 곳은 0x00)
            byte[] nameBytes = Encoding.GetEncoding("euc-kr").GetBytes(GameServer.GameServerName[server.Index]); // 한글 깨짐 방지
            Array.Copy(nameBytes, 0, buffer, offset, Math.Min(nameBytes.Length, 16));

            // 2. IP 주소 (클라이언트의 독특한 출력 로직에 맞춰 배열을 뒤집어 줍니다)
            byte[] ipBytes = server.IpAddress.GetAddressBytes();
            Array.Reverse(ipBytes);
            Array.Copy(ipBytes, 0, buffer, offset + 0x10, 4);

            // 3. 정수 값들 삽입 (C#은 기본이 Little Endian이므로 뒤집어주어야 Big Endian이 됩니다)
            int port = server.Enabled ? server.Port : -1;

            WriteBigEndianInt(buffer, offset + 0x14, port); // 포트
            buffer[offset + 0x18] = (byte)server.Id;        // 서버 ID
            buffer[offset + 0x19] = (byte)server.Index;     // 리스트 표시 순서
            buffer[offset + 0x1A] = (byte)server.Type;      // 서버 타입

            // 현재 서버의 현재 유저 수와 최대 유저 수는 가져오지 못함
            // 서버간 통신(S2S) 구현으로 서버간에 데이터를 주고 받아야 함
            float percent = server.CurrentUserCount / (float)server.MaxUserCount;
            // 100이 아닌 95를 곱하는 이유는 클라이언트 측에서 95%이상만 되면 full이라고 인식하기 때문
            int congestionPercent = (int)(percent * 95);
            WriteBigEndianInt(buffer, offset + 0x1B, congestionPercent);   // 혼잡도 (%)
            WriteBigEndianInt(buffer, offset + 0x1F, server.MaxUserCount); // 최대 유저 수

            // 0 대신 실제 현재 서버 시간(UNIX 타임스탬프)을 전송하도록 변경
            int currentUnixTime = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            WriteBigEndianInt(buffer, offset + 0x23, currentUnixTime);     // 체크 타임 (0으로 둬도 무방)

            // 0x27 ~ 0x3E 성주 정보 24바이트
            byte[] lordGuildBytes = Encoding.GetEncoding("euc-kr").GetBytes(server.LordGuild); // 한글 깨짐 방지
            Array.Copy(lordGuildBytes, 0, buffer, offset + 0x27, Math.Min(lordGuildBytes.Length, 12));
            for (int j = 0; j < 12; j++)
            {
                // 자기 자신과 XOR 연산을 수행
                buffer[offset + 0x27 + j] ^= (byte)(buffer[offset + j % 6] ^ (byte)congestionPercent);
            }

            byte[] lordNameBytes = Encoding.GetEncoding("euc-kr").GetBytes(server.LordName); // 한글 깨짐 방지
            Array.Copy(lordNameBytes, 0, buffer, offset + 0x33, Math.Min(lordNameBytes.Length, 12));
            for (int j = 0; j < 12; j++)
            {
                buffer[offset + 0x33 + j] ^= (byte)(buffer[offset + j % 6] ^ (byte)congestionPercent);
            }
            return;
        }

        private static int ExtractHexString(ref string rawRequest)
        {
            /*
             * " \r\n" 0
             * "GET /F3Auth.dll?%s%s\n\r\n\r" 1
             * "POST /GatewayServer.DLL?0xgw|0|0x00000000000053|shbs HTTP/1.0\r\n\r\n" 2
             * "POST /GatewayServer.DLL?0xF005|%s| HTTP/1.0\r\n\r\n" 3
             */
            int result = 0;

            string prefix = "GET /F3Auth.dll?";
            int startIndex = rawRequest.IndexOf(prefix);
            // 로그인 요청
            if (startIndex != -1)
            {
                result = 1;
                startIndex += prefix.Length; // '?' 바로 다음 위치 잡기
            }
            else
            {
                prefix = "POST /GatewayServer.DLL?0xgw|0|0x00000000000053|shbs HTTP/1.0";
                startIndex = rawRequest.IndexOf(prefix);

                // 게임 서버 목록 요청
                if (startIndex != -1)
                {
                    result = 2;
                }
                else
                {
                    prefix = "POST /GatewayServer.DLL?0xF005";
                    startIndex = rawRequest.IndexOf(prefix);

                    // 커뮤니티 서버 목록 요청
                    if (startIndex != -1)
                    {
                        result = 3;
                    }
                    else
                    {
                        startIndex = 0; // 일치하는 문자열이 없다면 원본 데이터 전송
                    }
                }
            }

            // 끝나는 위치 찾기 (\n, \r, 혹은 공백 중 가장 먼저 나오는 것)
            int endIndex = rawRequest.IndexOfAny(new char[] { '\n', '\r', ' ' }, startIndex);

            // 만약 끝나는 문자가 없다면 끝까지 다 가져옴
            if (endIndex == -1)
            {
                endIndex = rawRequest.Length;
            }

            rawRequest = rawRequest.Substring(startIndex, endIndex - startIndex);

            return result;
        }

        // 2. 추출된 Hex 문자열(2글자)을 1바이트로 변환하는 메서드
        private static byte[] HexStringToByteArray(string hex)
        {
            // 1바이트는 무조건 16진수 2글자로 표현되므로, 총 길이가 짝수여야 합니다.
            if (hex.Length % 2 != 0)
            {
                throw new ArgumentException("16진수 문자열의 길이는 짝수여야 합니다.");
            }

            // 변환될 바이트 배열의 길이는 텍스트 길이의 절반
#if NET5_0_OR_GREATER
                return Convert.FromHexString(hex);
#else
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < hex.Length; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            }
            return bytes;
#endif
        }

        // C# int를 Big Endian 바이트 배열로 버퍼에 쓰는 헬퍼 메서드
        private static void WriteBigEndianInt(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)((value >> 24) & 0xFF);
            buffer[offset + 1] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 3] = (byte)(value & 0xFF);
        }

        // 데이터 바이너리 코드를 문자열(Hex String)로 만들기
        public static string GetHexString(byte[] payloadBytes)
        {
            return BitConverter.ToString(payloadBytes).Replace("-", "");
        }
    }
}