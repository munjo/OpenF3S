using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Fortress3PaewangServerTest
{
    internal class ClientPacketParser
    {
        /*
         * 클라이언트에서 받아온 패킷을 풀어주는 클래스
         * 커맨드 값
         * 18: 로그인 요청(오프셋 4~15: 아이디, 오프셋 16~27: 비밀번호)
         * 84: 게임 아이디 리스트 요청(오프셋 4~15: 아이디)
         */

        private byte[] _payloadBytes;
        // 2번째 바이트의 상위 4비트
        private int _packetSizeGroup;
        // 2번째 바이트의 하위 4비트(를 왼쪽 5칸 시프트) + 3번째 바이트의 상위 5비트
        private int _commandId;
        // 3번째 바이트의 하위 3비트
        private int _flags;
        // 4번째 바이트 전체
        private int _seqNum;

        public ReadOnlySpan<byte> PayloadBytes { get => _payloadBytes; }

        public int PacketSizeGroup { get => _packetSizeGroup; }

        public int CommandId { get => _commandId; }

        public int Flags { get => _flags; }

        public int SeqNum { get => _seqNum; }

        public static int GetPacketSize(byte[] packetBytes, int readOffset, uint decryptionKey)
        {
            byte[] bytes = new byte[8];

            // 페이로드를 해독키(decryptionKey)를 이용해 4바이트 단위로 해독하기
            for (int i = 0; i < 8; i += 4)
            {
                // 4바이트씩 읽어서 해독키와 XOR 연산
                uint encryptedChunk = BitConverter.ToUInt32(packetBytes, readOffset + i);
                uint decryptedChunk = encryptedChunk ^ decryptionKey;

                // 해독된 결과를 다시 바이트 배열로 덮어쓰기
                byte[] decryptedBytes = BitConverter.GetBytes(decryptedChunk);
                Array.Copy(decryptedBytes, 0, bytes, i, 4);
            }

            // 헤더 데이터 검사 (비트 마스킹)
            int magicNumber = (int)BitStream.ReadBits(bytes, 0, 8);
            // 2번째 바이트의 상위 4비트
            int packetSizeGroup = (int)BitStream.ReadBits(bytes, 8, 4);

            if (magicNumber != 0xa1) { return 0; }

            if (packetSizeGroup == 0)
            {
                return BitConverter.ToUInt16(bytes, 4);
            }
            else
            {
                return Program.GetExpectedPacketSize(packetSizeGroup);
            }
        }

        public bool ParseAuthRequest(byte[] packetBytes, uint decryptionKey)
        {
            this._payloadBytes = new byte[packetBytes.Length];

            // 페이로드를 해독키(decryptionKey)를 이용해 4바이트 단위로 해독하기
            for (int i = 0; i < packetBytes.Length; i += 4)
            {
                // 4바이트씩 읽어서 해독키와 XOR 연산
                uint encryptedChunk = BitConverter.ToUInt32(packetBytes, i);
                uint decryptedChunk = encryptedChunk ^ decryptionKey;

                // 해독된 결과를 다시 바이트 배열로 덮어쓰기
                byte[] decryptedBytes = BitConverter.GetBytes(decryptedChunk);
                Array.Copy(decryptedBytes, 0, this._payloadBytes, i, 4);
            }

            //Console.WriteLine("[해독된 페이로드 데이터]");
            //Console.WriteLine(BitConverter.ToString(this._payloadBytes).Replace("-", " "));

            bool isVerifyPacketChecksum = VerifyPacketChecksum(this._payloadBytes);
            if (!isVerifyPacketChecksum)
            {
                Console.WriteLine("오류: Checksum이 일치하지 않습니다.");
                return false;
            }

            // 헤더 데이터 검사 (비트 마스킹)
            int magicNumber = (int)BitStream.ReadBits(this._payloadBytes, 0, 8);
            // 2번째 바이트의 상위 4비트
            _packetSizeGroup = (int)BitStream.ReadBits(this._payloadBytes, 8, 4);
            // 2번째 바이트의 하위 4비트(를 왼쪽 5칸 시프트) + 3번째 바이트의 상위 5비트
            _commandId = (int)BitStream.ReadBits(this._payloadBytes, 12, 9);
            // 3번째 바이트의 하위 3비트
            _flags = (int)BitStream.ReadBits(this._payloadBytes, 21, 3);
            // 4번째 바이트 전체
            _seqNum = (int)BitStream.ReadBits(this._payloadBytes, 24, 8);

            Console.WriteLine($"[헤더 분석] 매직: 0x{magicNumber:X2}, 패킷 사이즈 그룹: {_packetSizeGroup}, 커맨드 ID: {_commandId}, 플래그: {_flags}, 시퀀스: {_seqNum}");

            if (magicNumber != 0xa1)
            {
                Console.WriteLine("오류: 잘못된 매직 넘버입니다.");
                return false;
            }

            int packetSize;
            if(_packetSizeGroup == 0)
            {
                packetSize = BitConverter.ToUInt16(_payloadBytes, 4);
            }
            else
            {
                packetSize = Program.GetExpectedPacketSize(_packetSizeGroup);
            }

            if (packetSize != this._payloadBytes.Length)
            {
                Console.WriteLine("오류: 받은 크기가 사이즈 그룹의 크기와 다릅니다.");
                return false;
            }

            return true;
        }

        private static bool VerifyPacketChecksum(byte[] decryptedPayload)
        {
            if (decryptedPayload == null || decryptedPayload.Length % 4 != 0)
            {
                Console.WriteLine("패킷 길이가 부정확 합니다.");
                return false;
            }

            // 1. 체크섬 계산을 위해 패킷의 맨 뒤 4바이트를 제외하고 모두 복사
            int newLength = decryptedPayload.Length - 4;
            byte[] dataToHash = new byte[newLength];
            Array.Copy(decryptedPayload, 0, dataToHash, 0, newLength);

            // 2. 패킷 맨 끝에 들어있는 4바이트 원본 체크섬 추출 (Little Endian 기준)
            uint expectedChecksum = BitConverter.ToUInt32(decryptedPayload, newLength);

            // 3. MD5 해시 계산 및 체크섬 만들기
            uint calculatedChecksum = Program.CratePacketChecksum(dataToHash);

            // 결과 출력 및 비교
            Console.WriteLine($"[서버 계산 체크섬] : {calculatedChecksum:X8}");
            Console.WriteLine($"[클라 원본 체크섬] : {expectedChecksum:X8}");

            return calculatedChecksum == expectedChecksum;
        }
    }
}
