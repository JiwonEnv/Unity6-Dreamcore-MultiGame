# Unity6 Dreamcore MultiGame

Unity 6 멀티플레이 드림코어 게임 · Tripo로 만든 에셋에 오일 파스텔·색연필 풀스크린 셰이더를 입히고, 꿈이 깨질수록 연필 스케치만 남는 연출 | 드림코어 저택 환경 아트 포트폴리오

> **Unity로 이전 중**
> Unreal Engine 5로 0단계(기술 검증)까지 진행한 뒤 Unity 6.3 LTS + URP로 옮기고 있습니다. Unity 쪽은 레벨 생성기(ProcLevelGen)와 게임플레이(인형·실 잡기·골동품·라운드·몬스터·서랍장·HUD·멀티플레이)까지 옮겼습니다. 같은 시드에서 UE5와 같은 맵이 나옵니다. 오일 파스텔 셰이더는 아직입니다.
> UE5 버전은 태그 [`ue5-final`](../../tree/ue5-final)에 그대로 남아 있습니다. 아래 스크린샷과 셰이더 설명은 UE5 버전 기준입니다.

![테스트 맵 전체](Docs/proposal_images/1_test_map_overview.png)

## 무엇을 실험하나

| 질문 | 확인 방법 |
|---|---|
| 셰이더 하나로 서로 다른 AI 생성 에셋을 **하나의 화풍**으로 묶을 수 있을까? | Tripo 원본 → 셰이더 적용 결과를 같은 각도로 비교 |
| 텍스처 이음새와 지저분한 디테일을 **종이 질감과 그레인**이 얼마나 가려줄까? | 이음새가 보이는 에셋을 골라 셰이더 파라미터별로 비교 |
| 게임 상태(꿈 안정도)를 **화풍 변화**로 보여줄 수 있을까? | 꿈이 깨질수록 색이 빠지고 연필 스케치만 남는 연출 |

## 오일 파스텔 포스트 프로세스

UE5 버전은 전체 화면 포스트 프로세스 머티리얼 하나에 다섯 단계를 넣었습니다. ([oil_pastel.hlsl](../../blob/ue5-final/Tools/ue/oil_pastel.hlsl)) Unity에서는 URP Full Screen Pass Renderer Feature로 다시 구현합니다.

1. **Kuwahara 필터**: 색을 붓 터치 크기의 덩어리로 뭉개 유화처럼 보이게 합니다.
2. **종이 결 위의 안료**: 절차적 종이 질감과 표면 방향을 따르는 해칭을 씌웁니다. 어두운 색은 결을 채우고, 밝은 색은 종이가 비쳐 보입니다.
3. **연필 외곽선**: 깊이와 노멀 차이로 선을 그리고, 약간 흔들리는 "라인 보일"을 넣었습니다.
4. **별 모양 반짝임**: 유리·황동처럼 밝고 매끈한 픽셀에만 4각 별을 그립니다. 필터에 뭉개지지 않게 따로 처리합니다.
5. **꿈 붕괴**: `Collapse` 값이 오를수록 채도가 빠지고 연필 스케치만 남습니다.

| 1인칭 (잡기) | 뒷방 |
|---|---|
| ![1인칭 잡기](Docs/proposal_images/2_grab_first_person.png) | ![뒷방](Docs/proposal_images/3_back_room.png) |

## 환경 아트 계획

**꿈속 골동품 저택**: 드림코어 + 빈티지 골동품 + 광대 동물 인형 (방 7개)

| 방 | 환경 포인트 |
|---|---|
| 현관 홀 | 시작 지점, 탈출용 오르골 침대 |
| 회전목마 홀 | 저택 중앙, 움직이는 회전목마 |
| 찻잔 응접실 | 작은 소품이 많은 넓은 방 |
| 유리 진열실 | 좁은 통로, 잘 깨지는 유리 소품 (반짝임 테스트) |
| 시계 서재 | 책장 사이 미로 |
| 성물 예배실 | 어둡고 촛불이 많은 방 (저조도 셰이더 테스트) |
| 다락 침실 | 2층, 계단 이동 |

- **구조물**: 벽·바닥·계단·선반은 직접 모델링합니다. (UE5에서는 모델링 모드를 썼고, Unity에서 쓸 도구는 다시 정합니다)
- **소품·캐릭터**: Tripo로 생성합니다. 운반 골동품은 3천 tri 이하로 리토폴로지하고, 랜드마크는 고폴리곤을 유지합니다. (UE5 계획은 Nanite) ([Tripo_Asset_List.md](Docs/Tripo_Asset_List.md))
- **스타일 통일**: 공통 프롬프트 문구와 같은 컨셉 이미지 세트로 생성하고, 남은 차이는 셰이더로 정리합니다.

## 게임: 꿈 수거반 (Dream Movers, 가제)

셰이더와 환경을 실제 플레이 속에서 검증하기 위한 2~4인 협동 물리 운반 게임입니다. 광대 인형 수거반이 잠든 사람의 꿈속 저택에서 깨지기 쉬운 골동품을 함께 들고 나와야 합니다.

- 리슨 서버(한 명이 방장이 되어 서버 겸 플레이어). 물리는 서버가 계산하고, 클라이언트는 잡기·놓기와 시선만 보냅니다. Unity에서는 공식 패키지 Netcode for GameObjects를 씁니다.
- 무거운 물건은 2명 이상이 잡아야 들립니다.
- 물건이 부딪히면 금이 가고 가치가 깎입니다. 충돌 소리는 몬스터가 듣습니다.

자세한 기획은 [GDD.md](Docs/GDD.md)에 있습니다.

## 진행 단계

| 단계 | 내용 | 상태 |
|---|---|---|
| 0. 기술 검증 | 2인 접속, 같이 들기 동기화, 셰이더 프로토타입 | 진행 중 |
| 1. 그레이박스 | 저택 7개 방 배치, 한 판 루프 완성 | |
| 2. 에셋 생성 | Tripo 에셋 생성·교체, 셰이더 적용 전후 비교 | |
| 3. 몬스터 | 촛대 집사, 종 달린 말 AI | |
| 4. 폴리싱 | 사운드, UI, 라이팅, 플레이테스트 | |

## 실행

- Unity 6.3 LTS (6000.3.9f1), URP, Netcode for GameObjects 2.9.1
- Unity Hub에서 이 폴더를 추가해 엽니다. 게임은 `Assets/_DreamMovers/Scenes/MainMenu.unity`(타이틀·메인 메뉴)에서 시작해 `ProcGen.unity`(라운드마다 새 시드로 맵 생성)로 넘어갑니다. `ProcGen`을 바로 플레이해도 같은 메뉴가 뜹니다.
- 플레이: 타이틀에서 아무 키 → 메인 메뉴에서 **방 만들기**(최대 인원 1~4, 혼자면 1명) 또는 **방 참가**(방장 주소 입력). 포트 7777. 방 만들기 화면에 내 주소가 나옵니다.
  - 아웃게임 화면: 타이틀, 메인 메뉴(닉네임), 방 만들기·참가, 설정(그래픽·오디오·조작 키 바꾸기·보이스챗·접근성·언어, `settings.json`에 저장), 크레딧, Esc 일시정지(시간은 안 멈춤, 참가자 목록, 방 나가기), 로딩 화면, 접속 실패·연결 끊김 알림. 방장은 버전이 다르거나 방이 가득 찬 참가를 거절합니다.
  - 화면 배치는 `Assets/_DreamMovers/Art/UI/OutGame`의 `UI_*.uxml`·`UI_OutGame.uss`에 있어 UI Builder에서 바꿀 수 있습니다. 코드는 요소 이름으로만 찾으니 이름(`name`)만 유지하면 됩니다.
  - 조작: WASD 이동, 마우스 시점, 왼쪽 클릭(누르고 있기) 실로 잡기, 휠 거리 조절, Shift 달리기(시끄러움), Ctrl 웅크리기(조용함), Space 점프, E 서랍장 숨기·나오기, Esc 일시정지 (키는 설정에서 바꿀 수 있음)
  - 한 PC에서 둘이 해 보기: 메뉴 **Dream Movers > Build Windows Player**로 `Builds/Windows/DreamMovers.exe`를 만들고 두 번 실행해 하나는 호스트, 하나는 `127.0.0.1`로 참가
- 씬 다시 만들기: 메뉴 **Dream Movers > Build All Scenes** (메인 메뉴 + 생성 맵, 게임 프리팹도 같이 다시 만듦. 따로: **Build Main Menu Scene**, **Build ProcGen Scene**, **Build Gameplay Prefabs**)
- 폴더 구조 만들기·정리: 메뉴 **Dream Movers > Create Folder Structure** (몇 번 실행해도 안전)
- 테스트: Test Runner의 PlayMode에서 `DMGameplayTests`(호스트로 한 라운드: 준비·내용물·서랍장·잡힘 판정·웅크리기·잡기·운반·안정도·붕괴값·재시작), `DMOutGameTests`(설정 저장·불러오기, 키 바꾸기 저장, 참가 허용 규칙, 메뉴 → 방 만들기 → 일시정지 → 나가기 → 메뉴, 없는 주소 참가 시 알림과 함께 메뉴로)와 `PLGSelfTest`
- 두 프로세스 네트워크 점검: 빌드 후 `DreamMovers.exe -batchmode -nographics -dmhost -dmnettest -plgseed=535117761`와 `DreamMovers.exe -batchmode -nographics -dmclient=127.0.0.1 -dmnettest`를 함께 실행. 둘이 같은 맵을 만들고, 혼자 못 드는 25kg 골동품을 둘이 같이 드는지 `[DMNetTest]` 줄로 확인
- 개발용 플래그: `-dmhost`, `-dmclient=IP` (메뉴를 건너뛰고 바로 방 만들기·참가), `-plgseed=N`, `-dmshownoise`(소음 표시), `-dmbutlertrace`, `-dmgrabtrace`, `-dmuishot=폴더`(아웃게임 화면을 모두 찍고 종료)
- 프로젝트 설정 검사 (배치모드):
  `Unity.exe -batchmode -projectPath . -executeMethod DreamMovers.Editor.ProjectCheck.Run -quit`
- UE5 버전 실행: `git worktree add ../DreamMovers-UE5 ue5-final`로 별도 폴더에 꺼낸 뒤, Unreal Engine 5.8에서 `DreamMovers.uproject`를 열고 모듈 빌드를 승인합니다.

## 폴더 구성

| 폴더 | 내용 |
|---|---|
| `Assets/ProcLevelGen` | 레벨 생성기 (게임과 분리된 모듈, 테스트와 규격 문서 포함). [README](Assets/ProcLevelGen/README.md) |
| `Assets/_DreamMovers` | 게임 콘텐츠 전부. 에셋 하나의 메시·텍스처·재질·프리팹은 한 폴더에 둡니다 (아래) |
| `Assets/_DreamMovers/Art` | `Characters` · `Monsters` · `Props/Valuables` · `Props/Decor` · `Environment/Modules` · `Environment/Landmarks` · `MaterialLibrary`(공용 재질·셰이더, 그레이박스) · `VFX` · `UI`(`OutGame`: 아웃게임 화면 배치·스타일·패널 설정) |
| `Assets/_DreamMovers/Audio` | `Music` · `SFX` |
| `Assets/_DreamMovers/Gameplay` | 네트워크 프리팹(인형, 골동품, 솜뭉치, 서랍장, 촛대 집사, 종 달린 말, 게임 상태)과 프리팹 목록. 모양은 코드가 만드는 임시 도형 |
| `Assets/_DreamMovers/Scenes` | 씬 (`MainMenu` 타이틀·메뉴, `ProcGen` 생성 맵) |
| `Assets/_DreamMovers/Lighting` | 볼륨 프로필, LUT |
| `Assets/_DreamMovers/Scripts` | `Runtime`(게임 코드, UE5 `Source/DreamMovers`를 옮긴 것) · `Editor`(씬·프리팹·플레이어 빌더, 폴더 생성, 검사) · `Tests`(플레이 모드 테스트) |
| `Assets/_DreamMovers/Settings` | URP 렌더 파이프라인 에셋·렌더러, `Input`(Input System 액션) |
| `Assets/ThirdParty` | 에셋 스토어 등 외부 에셋 (직접 고치지 않음) |
| `Assets/_Sandbox` | 개인 테스트용 (`Sandbox` 씬) |
| `Docs` | 기획서, Tripo 에셋 목록, UE5 테스트 가이드, UE5 스크린샷(`ue5_screenshots`) |
| `TestEvidence` | 테스트 로그와 스크린샷 (UE5 실행, Unity 게임플레이 이전 검증, 아웃게임 화면) |
