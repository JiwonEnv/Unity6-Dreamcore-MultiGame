# 이전 테스트 증거 (2026-11-03 ~ 11-04)

`Saved/Logs`는 엔진이 백업 개수를 넘으면 지우고, `Saved/Screenshots`는 같은 이름으로 덮어씁니다. 그래서 남아 있던 파일을 2026-11-04에 여기로 복사했습니다. 원본 위치는 각 행에 적었습니다.

- 시각은 로그에 찍힌 UTC입니다 (한국 시간 = UTC + 9).
- 엔진: UE 5.8.4, CL 58902052, 런처 빌드. 출처는 `C:/Program Files/Epic Games/UE_5.8/Engine/Build/Build.version`이고, 실행 당시 값이 아니라 복사 시점의 값입니다.
- 실행 명령은 각 로그의 `Command Line:` 줄에서 옮겼습니다. 앞부분 `UnrealEditor.exe "E:\Project\Unreal_Oil-Shader\DreamMovers.uproject"`는 생략했습니다.
- **대상 코드**:
  - 로그에는 코드 버전이 기록되지 않습니다.
  - "실행 당시 HEAD"는 커밋 시각을 실행 시각과 비교해서 정했습니다.
  - 모든 실행은 미커밋 변경이 있는 작업 트리에서 했습니다. "이후 커밋"은 그 변경이 들어간 커밋이고, 작업 세션 기록으로 확인했습니다. 로그만으로는 확인할 수 없습니다.

## 실행별 정리

| 시각 (UTC) | 실행 당시 HEAD | 이후 커밋 | 명령 | 시드 | 결과 (로그 원문) | 파일 |
|---|---|---|---|---|---|---|
| 11-03 13:20 | `fc528a3` | `904a901` (자가 검사 코드). 레이아웃 코드는 `a2801d6`과 같음 | `UnrealEditor-Cmd.exe ... -ExecutePythonScript=E:/Project/Unreal_Oil-Shader/Tools/ue/test_proc_level.py -unattended -nop4 -nosplash -stdout -FullStdOutLogOutput` | 1~200 | `[PLGTest] PASS 200/200 seeds` | `logs/editor_200seed_2026-11-03.log` |
| 11-03 13:20 직전 | `fc528a3` | 위와 같음 | `... -ExecutePythonScript=.../build_proc_level.py ...` | 1 (미리보기) | `seed 1: 9 modules` | `logs/editor_buildmap_2026-11-03.log` |
| 11-03 13:38 | `fc528a3` | `904a901`, `55350f1` | `/Game/Maps/Lvl_ProcGen?listen -game -windowed -ResX=1280 -ResY=720 -plgselftest -nosplash -unattended` | 1778635777 | `seed 1778635777: 8 modules`, `ALL PASSED` | `logs/DreamMovers-backup-2026.11.03-13.38.59.log` |
| 11-03 13:39 | 위와 같음 | 위와 같음 | 위와 같음 | 2093480321 | `9 modules`, `ALL PASSED` | `logs/DreamMovers-backup-2026.11.03-13.39.37.log` |
| 11-03 13:39~13:40 | 위와 같음 | 위와 같음 | 위와 같음 | 1571208065 | `12 modules`, `ALL PASSED` | `logs/DreamMovers-backup-2026.11.03-13.40.04.log` |
| 11-03 13:40 | 위와 같음 | 위와 같음 | 서버와 클라이언트 실행 (시드 1074167809) | 1074167809 | **원본 없음.** 11-04 실행이 `PLG_Server.log`와 `PLG_Client.log`를 덮어씀 | — |
| 11-03 13:41 | 위와 같음 | 위와 같음 | `/Game/Maps/Lvl_Proto?listen -game ... -dmselftest -nosplash -unattended` | — | `[SelfTest] DONE: 11 passed, 0 failed` | `logs/DreamMovers-backup-2026.11.03-13.42.00.log` |
| 11-03 16:53 | 확인 불가 | 확인 불가 | `Command Line:` 줄이 비어 있음. 이 세션의 자동 실행이 아님 (사용자가 에디터를 연 것으로 보이나 확인 불가) | — | — | `logs/DreamMovers-backup-2026.11.03-16.53.31.log` |
| 11-04 05:50 | `d7110de` | `d291c2f`, `44136d9`, `47161c1` | `... -ExecutePythonScript=.../build_proc_level.py ...` | 1 (미리보기) | `seed 1: 9 modules` | `logs/DreamMovers-backup-2026.11.04-05.50.29.log`, `logs/editor_buildmap_2026-11-04.log` |
| 11-04 05:50 | 위와 같음 | 위와 같음 | `... -ExecutePythonScript=.../test_proc_level.py ...` | 1~200 | `[PLGTest] PASS 200/200 seeds` | `logs/DreamMovers-backup-2026.11.04-05.50.49.log`, `logs/editor_200seed_2026-11-04.log` |
| 11-04 05:51 | 위와 같음 | 위와 같음 | `/Game/Maps/Lvl_ProcGen?listen -game ... -plgselftest ...` | 535117761 | `[DMProcTest] PASS 15/15`, `target 8040 of 26800`, `ALL PASSED` | `logs/DreamMovers-backup-2026.11.04-05.51.25.log`, `screenshots/top_1.png`, `screenshots/player_1.png` |
| 11-04 05:51 | 위와 같음 | 위와 같음 | 위와 같음 | 2037773057 | 위와 같음 | `logs/DreamMovers-backup-2026.11.04-05.51.50.log`, `screenshots/top_2.png`, `screenshots/player_2.png` |
| 11-04 05:52 | 위와 같음 | 위와 같음 | 위와 같음 | 1873600385 | 위와 같음 (모듈 7개) | `logs/DreamMovers-backup-2026.11.04-05.52.14.log`, `screenshots/top_3.png`, `screenshots/player_3.png` |
| 11-04 05:52 | 위와 같음 | 위와 같음 | 서버: `... -plgselftest -ABSLOG=...PLG_Server.log` · 클라이언트: `127.0.0.1 -game -windowed -ResX=960 -ResY=540 -nosplash -unattended -ABSLOG=...PLG_Client.log` | 1040415745 | 서버와 클라이언트 모두 `seed 1040415745: 8 modules`. 서버는 `target 10720 of 26800`, `ALL PASSED` | `logs/PLG_Server.log`, `logs/PLG_Client.log`, `screenshots/PLGSelfTest_Top.png`, `screenshots/PLGSelfTest_Player.png` |
| 11-04 05:53 | 위와 같음 | 위와 같음 | `/Game/Maps/Lvl_Proto?listen -game ... -dmselftest ...` | — | `[SelfTest] DONE: 11 passed, 0 failed` | `logs/DreamMovers.log`, `screenshots/SelfTest_*.png` |

빌드 로그는 `build/`에 있습니다. 확장자 앞에 날짜가 없는 파일은 11-03 빌드이고, `2026-11-04_` 접두어가 붙은 파일은 11-04 빌드입니다.

## 이 증거의 한계

다음은 2026-11-04 리뷰에서 확인된 한계입니다.

- **내비 경로 검사**: `-plgselftest`는 내비 투영의 반환값을 확인하지 않았습니다. 투영이 실패하면 목표점이 원점(시작 방)이 되어 통과로 처리될 수 있습니다.
- **판정 합산**: `[DMProcTest]` 결과는 `ALL PASSED`에 합산되지 않았고, 종료 코드는 항상 0이었습니다. 위 결과는 사람이 로그 줄을 읽고 판정한 것입니다.
- **골동품 검사**: Z와 속도만 봤습니다. 기대 개수, 바닥 접촉, 겹침은 확인하지 않았습니다.
- **서버와 클라이언트 비교**: 시드와 모듈 수만 비교했습니다. 레이아웃 위치는 비교하지 않았습니다.
- **2인 검증**: 2인 공동 운반과 Network Emulation 테스트는 한 번도 하지 않았습니다.
