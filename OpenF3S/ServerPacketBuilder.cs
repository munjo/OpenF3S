using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Fortress3PaewangServerTest
{
    internal class ServerPacketBuilder
    {
        // 2번째 바이트의 상위 4비트
        public int PacketSizeGroup { get; set; }

        // 2번째 바이트의 하위 4비트(를 왼쪽 5칸 시프트) + 3번째 바이트의 상위 5비트
        public int CommandId { get; set; }

        // 3번째 바이트의 하위 3비트
        public int Flags { get; set; }

        // 4번째 바이트 전체
        public int SeqNum { get; set; }

        public int CustomPacketSize { get; set; }

        public int BitOffset
        {
            get => _bitOffset;
            set
            {
                _bitOffset = value;
                _byteOffset = (value + 7) / 8;
            }
        }

        public int ByteOffset
        {
            get => _byteOffset;
            set
            {
                _byteOffset = value;
                _bitOffset = value * 8;
            }
        }
        private int _bitOffset;
        private int _byteOffset;

        public byte[] PacketData { get; set; } = null;

        public ReadOnlySpan<byte> PayloadBytes => _payloadBytes;
        private byte[] _payloadBytes;

        public void WriteBits(int bitCount, int value)
        {
            int bitOffset = BitOffset;
            BitStream.WriteBits(PacketData, ref bitOffset, bitCount, value);
            BitOffset = bitOffset;
            return;
        }

        // 바이트 단위 동적 쓰기
        public void WriteByte(byte value)
        {
            EnsureByteAligned(); // 비트가 어긋나 있다면 다음 바이트로 정렬
            PacketData[ByteOffset] = value;
            ByteOffset++;
        }

        public void WriteShort(short value)
        {
            EnsureByteAligned(); // 비트가 어긋나 있다면 다음 바이트로 정렬
            Array.Copy(BitConverter.GetBytes(value), 0, PacketData, ByteOffset, 2);
            ByteOffset += 2;
        }

        public void WriteInt(int value)
        {
            EnsureByteAligned(); // 비트가 어긋나 있다면 다음 바이트로 정렬
            Array.Copy(BitConverter.GetBytes(value), 0, PacketData, ByteOffset, 4);
            ByteOffset += 4;
        }

        public void WriteBytes(int count, byte[] values)
        {
            EnsureByteAligned();
            Array.Copy(values, 0, PacketData, ByteOffset, Math.Min(values.Length, count));
            ByteOffset += count;
        }

        private void EnsureByteAligned()
        {
            if (BitOffset % 8 != 0)
            {
                BitOffset = ((BitOffset + 7) / 8) * 8;
            }
        }

        // [핵심] 패킷 완성 및 자동 사이즈 조절 로직
        public void BuildResponse(uint encryptionKey)
        {
            int totalSize = Program.GetExpectedPacketSize(PacketSizeGroup);
            int packetDataLength = PacketData?.Length ?? 0;

            // 가변 패킷(Group 0)인 경우 크기 자동 계산 및 주입
            if (PacketSizeGroup == 0)
            {
                // if (CustomPacketSize != 0)
                // { totalSize = (10 + CustomPacketSize + 3) & ~3; }
                // else
                // {
                //// 기본 헤더 4바이트 + 데이터량 표시용 헤더 2바이트 + 체크섬 헤더 4바이트
                totalSize = (10 + packetDataLength + 3) & ~3;
                // }

                if (totalSize < 16)
                {
                    totalSize = 16; // 16바이트 강제 룰
                }
            }

            _payloadBytes = new byte[totalSize];

            // 1. 헤더 조립 (8바이트)
            int currentBitOffset = 0;
            BitStream.WriteBits(_payloadBytes, ref currentBitOffset, 8, 0xa1);
            BitStream.WriteBits(_payloadBytes, ref currentBitOffset, 4, PacketSizeGroup);
            BitStream.WriteBits(_payloadBytes, ref currentBitOffset, 9, CommandId);
            BitStream.WriteBits(_payloadBytes, ref currentBitOffset, 3, Flags);
            BitStream.WriteBits(_payloadBytes, ref currentBitOffset, 8, SeqNum);

            int byteOffset = 4;
            if (PacketSizeGroup == 0)
            {
                // 포트리스3 가변 패킷은 항상 페이로드의 첫 2바이트가 '전체 크기'입니다.
                // 빌더가 스스로 크기를 계산하여 첫 2바이트에 덮어씌워 줍니다.
                byte[] sizeBytes = BitConverter.GetBytes((short)totalSize);
                Array.Copy(sizeBytes, 0, _payloadBytes, byteOffset, 2);
                byteOffset = 6;
            }

            // 2. 준비된 페이로드 데이터 복사
            if (PacketData != null)
            {
                Array.Copy(PacketData, 0, _payloadBytes, byteOffset, packetDataLength);
            }

            // 3. 남는 공간 패딩 (랜덤 값 주입)
            int payloadEndOffset = byteOffset + packetDataLength;
            int paddingLength = (totalSize - 4) - payloadEndOffset;
            if (0 < paddingLength)
            {
                byte[] randomPadding = new byte[paddingLength];
                using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
                {
                    rng.GetBytes(randomPadding);
                }
                Array.Copy(randomPadding, 0, _payloadBytes, payloadEndOffset, paddingLength);
            }

            // 4. 해시 생성 및 체크섬
            byte[] dataToHash = new byte[totalSize - 4];
            Array.Copy(_payloadBytes, 0, dataToHash, 0, totalSize - 4);
            uint checksum = Program.CratePacketChecksum(dataToHash);
            Array.Copy(BitConverter.GetBytes(checksum), 0, _payloadBytes, totalSize - 4, 4);

            // 5. 전체 암호화
            for (int i = 0; i < _payloadBytes.Length; i += 4)
            {
                uint plainChunk = BitConverter.ToUInt32(_payloadBytes, i);
                uint encryptedChunk = plainChunk ^ encryptionKey;
                Array.Copy(BitConverter.GetBytes(encryptedChunk), 0, _payloadBytes, i, 4);
            }
        }
    }
}
