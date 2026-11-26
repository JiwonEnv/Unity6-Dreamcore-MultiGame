# Unity6 Dreamcore MultiGame

Unity 6 멀티플레이 드림코어 게임 · Tripo로 만든 에셋에 오일 파스텔·색연필 풀스크린 셰이더를 입히고, 꿈이 깨질수록 연필 스케치만 남는 연출 | 드림코어 저택 환경 아트 포트폴리오

> **Unity로 이전 중**
> Unreal Engine 5로 0단계(기술 검증)까지 진행한 뒤 Unity 6.3 LTS + URP로 옮기고 있습니다. Unity 쪽은 레벨 생성기(ProcLevelGen)까지 옮겼습니다. 같은 시드에서 UE5와 같은 맵이 나옵니다.
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

- 리슨 서버. 물리는 서버가 계산하고, 클라이언트는 잡는 목표 위치만 보냅니다.
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

- Unity 6.3 LTS (6000.3.9f1), URP
- Unity Hub에서 이 폴더를 추가해 엽니다. 시작 씬은 `Assets/_DreamMovers/Scenes/ProcGen.unity`(플레이할 때마다 새 시드로 맵 생성)입니다.
- 생성 맵 씬 다시 만들기: 메뉴 **Dream Movers > Build ProcGen Scene**
- 폴더 구조 만들기·정리: 메뉴 **Dream Movers > Create Folder Structure** (몇 번 실행해도 안전)
- 프로젝트 설정 검사 (배치모드):
  `Unity.exe -batchmode -projectPath . -executeMethod DreamMovers.Editor.ProjectCheck.Run -quit`
- UE5 버전 실행: `git worktree add ../DreamMovers-UE5 ue5-final`로 별도 폴더에 꺼낸 뒤, Unreal Engine 5.8에서 `DreamMovers.uproject`를 열고 모듈 빌드를 승인합니다.

## 폴더 구성

| 폴더 | 내용 |
|---|---|
| `Assets/ProcLevelGen` | 레벨 생성기 (게임과 분리된 모듈, 테스트와 규격 문서 포함). [README](Assets/ProcLevelGen/README.md) |
| `Assets/_DreamMovers` | 게임 콘텐츠 전부. 에셋 하나의 메시·텍스처·재질·프리팹은 한 폴더에 둡니다 (아래) |
| `Assets/_DreamMovers/Art` | `Characters` · `Monsters` · `Props/Valuables` · `Props/Decor` · `Environment/Modules` · `Environment/Landmarks` · `MaterialLibrary`(공용 재질·셰이더, 그레이박스) · `VFX` · `UI` |
| `Assets/_DreamMovers/Audio` | `Music` · `SFX` |
| `Assets/_DreamMovers/Gameplay` | 플레이어·매니저 같은 게임 로직 프리팹과 데이터 |
| `Assets/_DreamMovers/Scenes` | 씬 (`ProcGen` 생성 맵) |
| `Assets/_DreamMovers/Lighting` | 볼륨 프로필, LUT |
| `Assets/_DreamMovers/Scripts` | `Runtime`(게임 코드) · `Editor`(씬 빌더, 폴더 생성, 검사 스크립트) |
| `Assets/_DreamMovers/Settings` | URP 렌더 파이프라인 에셋·렌더러, `Input`(Input System 액션) |
| `Assets/ThirdParty` | 에셋 스토어 등 외부 에셋 (직접 고치지 않음) |
| `Assets/_Sandbox` | 개인 테스트용 (`Sandbox` 씬) |
| `Docs` | 기획서, Tripo 에셋 목록, UE5 테스트 가이드, UE5 스크린샷(`ue5_screenshots`) |
| `TestEvidence` | UE5 버전 테스트 로그와 스크린샷 |
