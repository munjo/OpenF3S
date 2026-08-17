using Fortress3PaewangServerTest.Manager;
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/*
 * 1. 서버 열기
 * 2. 클라이언트 측에서 데이터 받아오기
 * 3. 필요없는 데이터 제외하기("GET /F3Auth.dll?", "\r\n\r\n")
 * 4. 클라이언트 데이터 해독 키와 서버 암호화 키 분리
 * 5. 암호화 해제
 * 6. MD5 해시 계산 및 체크섬 검사
 * 7. 헤더 데이터 검사
 *      7-1. 매직 넘버(0xa1)(8비트) 확인
 *      7-2. 패킷 사이즈 그룹(4비트) 확인
 *      7-3. 커맨드ID(9비트) 확인
 *      7-4. 3비트 확인
 *      7-5. 8비트 확인
 * 8. 아이디/비밀번호 검사
 * 9. 클라이언트로 데이터 보내기
 *      9-1. 헤더 데이터 만들기(4바이트)
 *          9-1-1. 매직 넘버(0xa1)(8비트)
 *          9-1-2. 패킷 사이즈 그룹(4비트)
 *          9-1-3. 커맨드ID(9비트)
 *          9-1-4. 3비트
 *          9-1-5. 8비트
 *      9-2. 커맨드ID값이 19일때: 의문의 값(16바이트), 커맨드ID값이 19가 아닐때: 오류 값(4바이트)
 *      9-3. ?(일단은 0으로 (4바이트 채움)
 *      9-4. PC방 검사 결과 값
 *      9-5. 나머지 값을 패킷 그룹의 사이즈 - 4에 맞게 무작위 값으로 채움
 *      9-6. 데이터 끝에 MD5 해시 계산 및 체크섬 추가(4바이트)
 *      9-7. 데이터 암호화 키로 암호화
 *      9-8. 데이터 바이너리 코드를 문자열로 만들기
 * 10. 클라이언트로 데이터 보내기
 */

namespace Fortress3PaewangServerTest
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.CancelKeyPress += new ConsoleCancelEventHandler(CancelHandler);

            // 데이터베이스 초기화
            DatabaseManager.InitializeDatabase();

            // 랭킹 업데이트 백그라운드 루프 시작 (최초 1회 정렬 포함)
            RankingManager.StartRankingUpdateLoop();

            // ▼ 테스트용 계정들을 여기서 원하는 만큼 미리 뚫어둡니다. ▼
            AccountRepository.RegisterLoginAccount("test00", "0000");
            AccountRepository.RegisterLoginAccount("test01", "0000");
            AccountRepository.RegisterLoginAccount("test02", "0000");

            // 1. 로그인(게이트웨이) 서버 실행 (포트 5000)
            LoginServer loginServer = new LoginServer(5000);
            loginServer.GameServers[14].Enabled = true;
            // 클라이언트로 전송할 게임 서버의 아이피 주소
            loginServer.GameServers[0].IpAddress = GetLocalIpAddress();
            loginServer.GameServers[0].Port = 5001;
            loginServer.GameServers[0].MaxUserCount = 64;
            loginServer.GameServers[0].Enabled = true;

            Task.Run(() => loginServer.StartAsync());

            // 2. 1번 게임 서버 실행(포트 5001)
            GameServer gameServer1 = new GameServer(5001);
            Task.Run(() => gameServer1.StartAsync());

            Console.WriteLine("\n[System] 모든 서버가 가동되었습니다.\n");

            // [변경 핵심] 엔터키나 Ctrl+C의 강제 해제에 영향을 받지 않는 반영구 대기 방식입니다.
            // 스레드를 완전히 정지시켜 CPU 점유율을 먹지 않으면서도 프로그램을 유지합니다.
            Thread.Sleep(Timeout.Infinite);
        }

        protected static void CancelHandler(object sender, ConsoleCancelEventArgs e)
        {
            // 이 값을 true로 바꾸면 OS의 종료 신호를 무시합니다.
            e.Cancel = true;
        }

        public static int GetExpectedPacketSize(int packetSizeGroup)
        {
            switch (packetSizeGroup)
            {
                case 1:
                    return 256;

                case 2:
                    return 48;

                case 3:
                    return 16;

                case 4:
                    return 160;

                case 5:
                case 7:
                    return 64;

                case 6:
                    return 32;

                default:
                    return 0;
            }
        }

        public static uint CratePacketChecksum(byte[] dataToHash)
        {
            // MD5 해시 계산 (16바이트 결과물)
            byte[] md5Hash;
            using (MD5 md5 = MD5.Create())
            {
                md5Hash = md5.ComputeHash(dataToHash);
            }

            // 16바이트 해시를 4바이트(uint) 단위 4개로 쪼개기 (C++ 배열 local_10[0]~[3] 에 해당)
            // 윈도우 환경은 Little Endian을 사용하므로 BitConverter가 정확히 들어맞습니다.
            uint chunk0 = BitConverter.ToUInt32(md5Hash, 0);  // 인덱스 0~3
            uint chunk1 = BitConverter.ToUInt32(md5Hash, 4);  // 인덱스 4~7
            uint chunk2 = BitConverter.ToUInt32(md5Hash, 8);  // 인덱스 8~11
            uint chunk3 = BitConverter.ToUInt32(md5Hash, 12); // 인덱스 12~15

            // 쪼갠 4개의 값을 XOR 연산하여 4바이트로 압축 (Folding)
            uint checksum = chunk0 ^ chunk1 ^ chunk2 ^ chunk3;
            return checksum;
        }

        private static IPAddress GetLocalIpAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());

            // IPv4 주소 중 첫 번째 항목을 IPAddress로 반환
            return host.AddressList
                .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork);
        }
    }
}
