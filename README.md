# OpenF3S (Fortress 3 Paewangjeon Server Emulator)

OpenF3S is an open-source, unofficial server emulator for the classic online game, Fortress 3 Paewangjeon (1.020). It is written in C# and designed to recreate the core server-side functionality for educational and research purposes.

Note: OpenF3S is a server emulator only. It does NOT contain any game client files, assets, or copyrighted material.

## Disclaimer (면책 조항)

OpenF3S is an open-source server emulator created strictly for educational, network protocol research, and archival purposes.

This project is a non-profit, fan-made initiative. It is not affiliated with, endorsed by, or sponsored by CCR or any of their partners.

All game assets, character names, trademarks, and copyrights belong to their respective owners.

This repository does not contain any client executable files, images, sounds, or proprietary code from the original game. You must legally obtain your own copy of the game client to use this software.

The use of this software to operate commercial or illegal private servers is strictly prohibited. The authors assume no responsibility for any misuse of this project.

------------------------------------------

OpenF3S는 오직 교육, 네트워크 프로토콜 연구 및 학술적 보존 목적으로만 제작된 오픈소스 서버 에뮬레이터입니다.

이 프로젝트는 완전한 비영리 팬 프로젝트이며, 원저작권자인 CCR 및 관련 회사와 어떠한 관계도 없고, 공식적인 승인이나 후원을 받지 않았습니다.

게임 내 모든 캐릭터 명칭(탱크 이름), 상표, 에셋 및 지식재산권은 원작자 및 해당 권리자에게 귀속됩니다.

본 저장소에는 원작 게임의 클라이언트 실행 파일, 이미지, 사운드 등 어떠한 게임 에셋이나 독점적인 코드도 포함되어 있지 않습니다. 이 소프트웨어를 테스트하려면 본인이 합법적으로 소유한 게임 클라이언트를 사용해야 합니다.

본 소스 코드를 활용하여 금전적 이익을 취하거나 불법적인 사설 서버(프리서버)를 운영하는 행위는 엄격히 금지되며, 프로젝트 기여자는 사용자의 오용으로 인해 발생하는 어떠한 법적 문제에도 책임을 지지 않습니다.

## Getting Started (시작하기)

1. Clone the repository.

2. Build the solution using Visual Studio 2022 (C# .NET).

3. Install Required NuGet Packages:
  * This project uses Newtonsoft.Json for database management.
  * Go to Tools > NuGet Package Manager > Manage NuGet Packages for Solution, search for Newtonsoft.Json, and install it.

4. Build the solution and run the compiled executable. 

5. Configure your client to connect to the server. (Note: Instructions or tools for connecting the client to a custom server are not provided, in compliance with South Korean Game Industry laws regarding private servers.)

---------------------------------------

1. 저장소를 클론합니다.

2. Visual Studio 2022에서 솔루션을 엽니다.

3. 필수 패키지를 설치합니다:
  * 본 프로젝트는 데이터베이스 관리를 위해 Newtonsoft.Json 패키지를 사용합니다.
  * 도구 > NuGet 패키지 관리자 > 솔루션용 NuGet 패키지 관리로 이동하여 Newtonsoft.Json을 검색하고 설치해 주세요.

4. 솔루션을 빌드하고 컴파일된 실행 파일을 실행하여 서버를 구동합니다.

5. 클라이언트가 해당 서버에 연결되도록 구성하십시오. (참고: 클라이언트를 서버에 연결하는 방법 및 도구는 한국 게임산업진흥에 관한 법률상 사설 서버 운영 문제로 인해 제공하거나 안내해 드릴 수 없습니다.)

## License

This project is licensed under the GNU AGPL v3.0 License. See the LICENSE file for details.
If you modify this server code and run it over a network, you must share the modified source code under the same license.