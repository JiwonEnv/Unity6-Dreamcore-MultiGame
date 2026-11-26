# 프로젝트 규칙

폴더, 이름, 커밋, 문서를 어떻게 다루는지 정리한 문서입니다. 규칙을 바꾸면 이 문서를 먼저 고치고, 이미 있는 에셋도 한 번에 같이 고칩니다.

> 2026-11-26 확정 (프로젝트 관리 카드 관리 1~7).

## 1. 폴더 구조

게임 콘텐츠는 모두 `Assets/_DreamMovers` 한 곳에 둡니다. 종류별(Models/Textures/Materials)이 아니라 **내용별**로 나누고, **에셋 하나의 메시·텍스처·재질·프리팹은 한 폴더**에 둡니다.

```
Assets/
  _DreamMovers/
    Art/
      Characters/              PC_Bear/ ...   (에셋마다 폴더)
        Animations/            여러 캐릭터가 같이 쓰는 애니메이션
      Monsters/                MON_Butler/ ...
      Props/Valuables/         VAL_Teacup/ ...  (운반 골동품)
      Props/Decor/             DEC_ ...         (장식 소품)
      Environment/Modules/     방 모듈
      Environment/Landmarks/   LM_ ...
      MaterialLibrary/         여러 에셋이 같이 쓰는 재질·셰이더
        Graybox/
        Shaders/
      VFX/
      UI/
    Audio/Music/  Audio/SFX/
    Gameplay/                  플레이어·매니저 같은 게임 로직 프리팹, 데이터(ScriptableObject)
    Scenes/                    씬
    Lighting/Volumes/          볼륨 프로필, LUT
    Scripts/Runtime/           게임 코드 (네임스페이스 DreamMovers)
    Scripts/Editor/            에디터 도구 (네임스페이스 DreamMovers.Editor)
    Settings/                  URP 에셋·렌더러, Input/
  ProcLevelGen/                맵 생성기. 게임과 분리된 재사용 모듈 (네임스페이스 ProcLevelGen)
  ThirdParty/                  에셋 스토어 등 외부 에셋. 직접 고치지 않음
  _Sandbox/                    개인 테스트. 게임에서 참조하지 않음
```

- 왜 프로젝트 폴더 하나: 에셋 스토어 에셋은 `Assets/` 바로 아래에 자기 폴더를 만들기 때문에, 섞이지 않게 분리합니다.
- 왜 에셋별 폴더: Tripo 에셋은 모델과 텍스처가 한 묶음입니다. 교체·삭제할 때 폴더 하나만 다룹니다.
- 빈 폴더에는 `.keep` 파일이 있습니다. git이 빈 폴더를 지우지 않게 하려는 것입니다.
- 메뉴 **Dream Movers > Create Folder Structure**로 이 구조를 다시 만들 수 있습니다.

## 2. 이름

### 공통

- 폴더와 파일은 **PascalCase**, 공백 없이, 영문·숫자·`_`만 씁니다. 예: `TeacupParlor`
- 변형은 두 자리 숫자: `SM_DEC_Vase_01`, `SM_DEC_Vase_02`

### 에셋 ID (무엇인지)

Tripo 에셋 목록과 같은 ID를 씁니다. 에셋 폴더 이름도 이 ID입니다.

| 접두어 | 뜻 | 예 |
|---|---|---|
| `PC_` | 플레이어 캐릭터 | `PC_Bear` |
| `MON_` | 몬스터 | `MON_Butler` |
| `VAL_` | 운반 골동품 | `VAL_Teacup` |
| `LM_` | 랜드마크 | `LM_Carousel` |
| `DEC_` | 장식 소품 | `DEC_Vase` |

### 파일 종류 (어떤 파일인지)

파일 이름은 **종류 접두어 + 에셋 ID**입니다. **프리팹만 접두어 없이 에셋 ID 그대로** 씁니다. 게임에서 가장 많이 다루는 파일이라 짧게 둡니다.

| 접두어 | 종류 | 예 |
|---|---|---|
| `SM_` | 메시 (뼈대 없음) | `SM_VAL_Teacup.fbx` |
| `SK_` | 메시 (뼈대 있음, 캐릭터·몬스터) | `SK_PC_Bear.fbx` |
| `M_` | 재질 | `M_VAL_Teacup.mat` |
| `T_` | 텍스처 | `T_VAL_Teacup_BC.png` |
| `A_` | 애니메이션 클립 | `A_PC_Walk.anim` |
| `AC_` | 애니메이터 컨트롤러 | `AC_PC.controller` |
| `SH_` | 셰이더·셰이더 그래프 | `SH_OilPastel.shadergraph` |
| `VFX_` | 이펙트 | `VFX_DreamCrack` |
| `SFX_` / `MUS_` | 효과음 / 음악 | `SFX_GlassBreak.wav` |
| (없음) | 프리팹 | `VAL_Teacup.prefab` |

텍스처 끝에는 채널 접미어를 붙입니다: `_BC` 색(베이스 컬러) · `_N` 노멀 · `_MS` 금속도·매끄러움(URP 마스크) · `_AO` 앰비언트 오클루전 · `_E` 발광

예: 찻잔 하나

```
Art/Props/Valuables/VAL_Teacup/
  SM_VAL_Teacup.fbx
  M_VAL_Teacup.mat
  T_VAL_Teacup_BC.png
  T_VAL_Teacup_N.png
  VAL_Teacup.prefab
```

### 코드

- 네임스페이스: 게임 `DreamMovers`, 에디터 도구 `DreamMovers.Editor`, 맵 생성기 `ProcLevelGen`
- 파일 하나에 public 타입 하나, 파일 이름 = 클래스 이름 (PascalCase)

## 3. 커밋과 브랜치

### 커밋 메시지

`종류: 무엇을 했는지` 한 줄, 영어 소문자로 시작. **한 커밋에는 한 목적**만 담습니다.

| 종류 | 언제 |
|---|---|
| `feat` | 새 기능 |
| `fix` | 버그 수정 |
| `art` | 모델·텍스처·재질·프리팹·셰이더·라이팅 등 아트 작업 |
| `refactor` | 동작은 같고 구조만 바꿈 (코드 구조, 폴더 정리) |
| `docs` | 문서 |
| `chore` | 프로젝트 설정, Git, 패키지 |

폴더 정리와 모델 수정처럼 종류가 다른 작업은 커밋을 나눕니다.

예: `feat: add the first person player controller`, `art: add the teacup valuable`

### 브랜치

- **기능 단위 작업은 브랜치 + PR**: 플레이어 컨트롤러, 물건 잡기, 몬스터 AI처럼 여러 커밋이 드는 작업. 브랜치 이름은 `feature/짧은-이름` (예: `feature/player-controller`), 아트는 `art/짧은-이름`.
- **작은 수정은 main에 바로**: 오탈자, 문서, 설정 한 줄 같은 것.
- PR 설명에는 무엇을, 왜 했는지와 확인한 방법(테스트, 스크린샷)을 적습니다. 합친 브랜치는 지웁니다.
- 왜: 포트폴리오에서 작업 단위(PR)와 그 이유가 보이게 하려고.

### Git 설정

- 큰 바이너리(이미지, 모델, 사운드, 영상)는 Git LFS로 관리합니다. 대상은 `.gitattributes`에 있습니다.
- 씬·프리팹·에셋은 텍스트(YAML)로 저장하고 Unity의 스마트 머지(UnityYAMLMerge)로 병합합니다. 이 연결은 컴퓨터마다 한 번 해야 합니다 (경로는 설치한 Unity 버전에 맞게):

```
git config merge.unityyamlmerge.name "Unity SmartMerge (UnityYAMLMerge)"
git config merge.unityyamlmerge.driver "'C:/Program Files/Unity/Hub/Editor/6000.3.9f1/Editor/Data/Tools/UnityYAMLMerge.exe' merge -p %O %B %A %A"
git config merge.unityyamlmerge.recursive binary
```

## 4. 문서 관리

Notion 작업 노트가 중심이고, 저장소에는 확정된 내용만 둡니다.

| 무엇 | 어디에 | 규칙 |
|---|---|---|
| 기획 (논의 중) | Notion 전체 기획서 | 🙋 확정 · 🤖 AI 제안 · ❓ 미정으로 표시 |
| 기획 (확정본) | `Docs/GDD.md` | Notion 기획서의 **🙋 항목만** 옮김. 🤖·❓는 확정 전까지 Notion에만 |
| 모든 문서 목록 | Notion 문서 모음 | 문서가 생기면 행을 하나 추가. 저장소 문서는 내용을 복사하지 않고 GitHub 링크로 연결 (한쪽만 고쳐져 어긋나지 않게) |
| 할 일 | Notion 작업 계획 | 시작하면 진행 중, 끝나면 완료. 계획에 없던 일은 행 추가 |
| 하루 기록 | Notion 일지 | 작업한 날마다 한 행. 무엇을, 어떻게, 왜 |
| 진행 스냅샷 | Notion 진행 스냅샷 | **화면이 눈에 띄게 바뀔 때마다** 캡처 한 장 + 한 줄: PR을 합칠 때, 단계가 끝날 때, 아트·셰이더가 바뀔 때. 포트폴리오·블로그 글감으로 씀 |
| 규칙 | 이 문서 | 바꾸면 이 문서부터 고침 |
