# 아웃게임 화면 검증 (2026-11-27)

브랜치 `feature/gameplay-port`, Unity 6000.3.9f1, Netcode for GameObjects 2.9.1.

| 검사 | 명령 | 결과 |
|---|---|---|
| 에디트 모드 테스트 | `Unity.exe -batchmode -runTests -testPlatform EditMode` | 6 / 6 통과 (`editmode_results.xml`) |
| 플레이 모드 테스트 | `Unity.exe -batchmode -runTests -testPlatform PlayMode` | 6 / 6 통과 (`playmode_results.xml`, 검사 줄은 `playmode_DMTest.log`) |
| 두 프로세스 네트워크 점검 | `DreamMovers.exe -batchmode -nographics -dmhost -dmnettest -plgseed=535117761` + `DreamMovers.exe -batchmode -nographics -dmclient=127.0.0.1 -dmnettest` | 2회 연속 양쪽 ALL PASSED, 예외 0 (`net_host_*.log`, `net_client_*.log`). 이제 메인 메뉴 씬에서 시작해 게임 씬으로 넘어가고, 방장의 참가 허용 검사를 거쳐 들어감 |
| 화면 캡처 | `DreamMovers.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -plgseed=535117761 -dmuishot=screens` | `screens/` 14장: 타이틀, 메뉴, 방 만들기·참가, 설정 6탭, 크레딧, 팝업, 로딩, 일시정지 |
| 프로젝트 설정 검사 | `Unity.exe -batchmode -executeMethod DreamMovers.Editor.ProjectCheck.Run` | PASS |

## 확인한 것

- `DMOutGameTests`
  - 설정: 저장한 값이 다시 읽힘, 깨진 파일은 기본값으로, 범위 밖 값은 잘림, 닉네임은 12자로 잘림, 기본값 되돌리기는 닉네임 유지
  - 키 바꾸기: 바꾼 키가 저장·복원되고 비우면 기본 키로
  - 참가 허용: 버전 다름, 방 가득 참(혼자 방 포함), 판 진행 중 참가 막기 옵션
  - 흐름: 메뉴 씬 → 설정 닫으면 저장 → 방 만들기 → 게임 씬에서 호스트 → 라운드 시작 후 로딩 화면 사라짐 → 참가자 목록에 내 닉네임 → 일시정지 열고 닫기 → 방 나가기 → 메뉴(네트워크 정리됨) → 없는 주소 참가 → 약 12초 후 "접속하지 못했습니다" 알림과 함께 메뉴
- 기존 `DMGameplayTests`(호스트 한 라운드)와 두 프로세스 같이 들기 점검이 그대로 통과
- 캡처에서 한글(맑은 고딕)이 모든 화면에 나오고, 일시정지 중에는 HUD가 점수·시간만 그림

## 확인하지 않은 것

- 사람이 직접 마우스·키보드로 메뉴를 누르는 플레이 (테스트는 같은 함수를 코드로 부름). 키 바꾸기를 실제 키 입력으로 해 보는 것
- 다른 PC끼리의 접속, 방이 가득 찼을 때 실제 세 번째·다섯 번째 참가자가 거절되는 장면 (규칙은 단위 테스트로만)
- 해상도·창 모드 변경이 실제 창에 적용되는 모습 (배치 모드에서는 일부러 적용하지 않음)
- 음악·효과음·보이스챗 볼륨, 마이크, 흔들림, 자막, 언어는 저장만 되고 아직 적용할 대상이 없음
