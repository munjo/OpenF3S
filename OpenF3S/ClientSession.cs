using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Fortress3PaewangServerTest
{
    // 로비에서 본 방의 핵심 정보를 기억하기 위한 내부 클래스
    internal class ClientSession
    {
        // 1. 네트워크 소켓 정보
        public TcpClient Client { get; private set; }
        public NetworkStream Stream { get; private set; }
        public string ClientIp { get; private set; }

        // 2. 메모리 할당 없는 고속 슬라이딩 윈도우 버퍼
        public byte[] ReceiveBuffer { get; private set; } = new byte[16384]; // 16KB 고정 버퍼
        public int BufferDataLength { get; set; } = 0; // 버퍼에 쌓인 유효한 데이터의 총 길이

        // 3. 유저 계정 정보 (기존 LoginServerAccount 역할)
        public string AccountId { get; set; }
        public List<GameAccount> GameAccounts { get; set; } = new List<GameAccount>();

        public GameAccount SelectedCharacter { get; set; } // 유저가 선택한 특정 게임 ID

        public bool IsFemale { get; set; }
        public string MyGuildName { get; set; }
        public int MyGuildNameColor { get; set; }

        public int CurrentRoomIndex { get; set; } = -1; // -1이면 방에 들어가지 않은 로비/대기실 상태

        // [신규] 클라이언트가 로비에서 마지막으로 전달받은 방 목록을 캐싱(기억)하는 딕셔너리
        // Key: 방 번호(Index), Value: 방의 핵심 정보
        public ConcurrentDictionary<int, uint> CachedLobbyRooms { get; } = new ConcurrentDictionary<int, uint>();

        // 4. 독립적인 암호화 상태 정보 (절대 공유되면 안 됨!)
        public uint DecryptionKey { get; private set; }
        public uint EncryptionKey { get; private set; }
        public int SendSequenceNum { get; private set; }
        public int RecvSequenceNum { get; private set; }

        private uint _decKeySeed1;
        private uint _decKeySeed2;
        private uint _encKeySeed1;
        private uint _encKeySeed2;

        // [핵심 1] 동시 다발적인 전송을 막기 위한 자물쇠 객체
        private readonly object _sendLock = new object();

        // 생성자: 클라이언트가 접속할 때 소켓 정보를 받아서 초기화
        public ClientSession(TcpClient client, string clientIp)
        {
            Client = client;
            ClientIp = clientIp;
            Stream = client.GetStream();
        }

        // 수신 버퍼에서 처리가 끝난 패킷을 밀어내고, 남은 데이터를 앞으로 당기는 메서드 (슬라이딩)
        public void SlideBuffer(int processedBytes)
        {
            if (processedBytes <= 0) return;

            int remainingBytes = BufferDataLength - processedBytes;
            if (remainingBytes > 0)
            {
                // 남은 데이터를 버퍼의 맨 앞으로 복사 (C# 에서는 메모리 겹침 현상을 Array.Copy가 안전하게 처리해 줌)
                Array.Copy(ReceiveBuffer, processedBytes, ReceiveBuffer, 0, remainingBytes);
            }
            BufferDataLength = remainingBytes;
        }

        // 다른 곳에서 호출할 수 있는 안전한 일방향 전송 메서드
        public void SendPacket(ServerPacketBuilder packetBuilder)
        {
            // 이 lock 블록 안에는 동시에 오직 1개의 스레드만 들어올 수 있습니다.
            // A유저와 B유저가 동시에 나에게 채팅을 보내도, 무조건 줄을 서서 1개씩 처리됩니다.
            lock (_sendLock)
            {
                // 1. 현재 세션의 시퀀스 넘버를 안전하게 주입
                packetBuilder.SeqNum = this.SendSequenceNum;

                // 2. 현재 세션의 암호화 키로 패킷 자동 크기 조정, 체크섬, 암호화 처리
                packetBuilder.BuildResponse(this.EncryptionKey);
                byte[] responseBytes = packetBuilder.PayloadBytes.ToArray();

                // 3. 다음 패킷을 위해 암호화 키 및 시퀀스 넘버 1칸 전진!
                this.UpdateEncryptionKey();

                try
                {
                    // 4. 소켓으로 발사
                    if (Stream != null && Stream.CanWrite)
                    {
                        Console.WriteLine($"[{ClientIp} 송신] 커맨드: {packetBuilder.CommandId}, {responseBytes.Length} 바이트");
                        Stream.Write(responseBytes, 0, responseBytes.Length);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{ClientIp} 송신 오류] {ex.Message}");
                    this.Close();
                }
            }
        }

        // --- 암호화 키 관리 메서드 ---
        public void InitializeCryptionKey(uint decSeed1, uint decSeed2, uint encSeed1, uint encSeed2)
        {
            _decKeySeed1 = decSeed1;
            _decKeySeed2 = decSeed2;
            DecryptionKey = _decKeySeed1 ^ _decKeySeed2;
            RecvSequenceNum = 0;

            _encKeySeed1 = encSeed1;
            _encKeySeed2 = encSeed2;
            EncryptionKey = _encKeySeed1 ^ _encKeySeed2;
            SendSequenceNum = 0;
        }

        public void UpdateDecryptionKey()
        {
            uint bucket = (~DecryptionKey + _decKeySeed1) * _decKeySeed2;
            DecryptionKey = (bucket >> 16) ^ bucket;
            RecvSequenceNum = (RecvSequenceNum + 1) % 255;
        }

        public void UpdateEncryptionKey()
        {
            uint bucket = (~EncryptionKey + _encKeySeed1) * _encKeySeed2;
            EncryptionKey = (bucket >> 16) ^ bucket;
            SendSequenceNum = (SendSequenceNum + 1) % 255;
        }

        // --- 유틸리티 메서드 ---
        public void Close()
        {
            try
            {
                if (Client != null && Client.Connected)
                {
                    Client.Close();
                }
            }
            catch { }
        }
    }
}
