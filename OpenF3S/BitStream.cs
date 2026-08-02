using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fortress3PaewangServerTest
{
    internal class BitStream
    {
        public static uint ReadBits(byte[] sourceBuffer, int bitOffset, int bitsToRead)
        {
            uint result = 0;

            while (bitsToRead > 0)
            {
                // 1. 현재 읽어야 할 바이트 인덱스와 바이트 내 비트 시작 위치 계산
                int byteIndex = bitOffset / 8;
                int bitRemainder = bitOffset % 8;

                // 2. 현재 바이트에서 읽을 수 있는 남은 비트 수 계산
                int bitsAvailable = 8 - bitRemainder;

                // 3. 이번 루프에서 실제로 읽어낼 비트 수 결정
                int bitsToReadNow = (bitsToRead < bitsAvailable) ? bitsToRead : bitsAvailable;

                // [안전장치] 버퍼 길이를 초과해서 읽으려 하면 예외 처리
                if (sourceBuffer.Length <= byteIndex)
                {
                    throw new IndexOutOfRangeException();
                }

                // 4. 버퍼에서 1바이트를 가져옴
                byte currentByte = sourceBuffer[byteIndex];

                // 5. 시프트 연산(>>)과 마스킹(&)을 통해 원하는 구간의 비트만 정확히 추출 (MSB-first)
                // 참고: C#에서 byte에 시프트 연산을 하면 자동으로 int로 변환되므로 명시적으로 uint 캐스팅
                uint extractedBits = (uint)((currentByte >> (8 - bitRemainder - bitsToReadNow)) & ((1 << bitsToReadNow) - 1));

                // 6. 결과값(누산기)을 자리 이동시킨 뒤, 방금 떼어낸 비트를 OR 연산(|)으로 끼워 넣음
                result = (result << bitsToReadNow) | extractedBits;

                // 7. 읽은 만큼 오프셋을 전진시키고, 남은 목표량을 줄임
                bitsToRead -= bitsToReadNow;
                bitOffset += bitsToReadNow;
            }

            return result;
        }

        // 안전하고 정확하게 비트를 잘라 넣는 완벽한 형태의 WriteBits
        public static void WriteBits(byte[] buffer, ref int bitOffset, int bitCount, int value)
        {
            while (bitCount > 0)
            {
                // 1. 현재 어느 바이트(Index)의 몇 번째 비트(Remainder)에 써야 하는지 계산
                int byteIndex = bitOffset / 8;
                int bitRemainder = bitOffset % 8;
                int bitsAvailable = 8 - bitRemainder;

                // 2. 이번 루프에서 실제로 쓸 수 있는 비트 수 계산
                int bitsToWriteNow = (bitCount < bitsAvailable) ? bitCount : bitsAvailable;
                bitCount -= bitsToWriteNow;

                // 3. 덮어쓸 위치의 비트만 정확히 0으로 비우는 마스크(Mask) 생성
                // 예: 4비트를 써야 한다면 1111을 만들고 원하는 위치로 이동시킨 뒤 반전(~)시킴
                int writeMask = ((1 << bitsToWriteNow) - 1) << (8 - bitRemainder - bitsToWriteNow);
                buffer[byteIndex] &= (byte)~writeMask;

                // 4. 새 값(value)에서 필요한 만큼의 비트만 떼어냄
                int extractedBits = (value >> bitCount) & ((1 << bitsToWriteNow) - 1);

                // 5. 알맞은 위치로 시프트(<<)하여 기존 버퍼에 OR(|) 연산으로 끼워 넣음
                buffer[byteIndex] |= (byte)(extractedBits << (8 - bitRemainder - bitsToWriteNow));

                // 6. 오프셋 증가
                bitOffset += bitsToWriteNow;
            }
        }
    }
}
