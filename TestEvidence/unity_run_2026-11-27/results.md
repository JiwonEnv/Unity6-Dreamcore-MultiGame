# Unity 게임플레이 이전 검증 (2026-11-27)

브랜치 `feature/gameplay-port`, Unity 6000.3.9f1, Netcode for GameObjects 2.9.1, 시드 535117761 (UE5 테스트 시드, 레이아웃 해시 e94bc661).

| 검사 | 명령 | 결과 |
|---|---|---|
| 에디트 모드 테스트 | `Unity.exe -batchmode -runTests -testPlatform EditMode` | 6 / 6 통과 |
| 플레이 모드 테스트 (`DMGameplayTests`, `PLGSelfTest`) | `Unity.exe -batchmode -runTests -testPlatform PlayMode` | 2 / 2 통과. 검사별 결과는 `playmode_DMTest.log` |
| 두 프로세스 네트워크 점검 | `DreamMovers.exe -batchmode -nographics -dmhost -dmnettest -plgseed=535117761` + `DreamMovers.exe -batchmode -nographics -dmclient=127.0.0.1 -dmnettest` | 2회 연속 양쪽 ALL PASSED, 예외 0. `net_host.log`, `net_client.log` |
| 화면 확인 | `DreamMovers.exe -dmhost -plgseed=535117761 -dmshot=...` (창 모드 1280x720) | `host_view.png`: HUD 한글, 가격표, 집사·골동품 임시 도형 |

## 확인한 것

- 호스트 혼자: 맵 준비 후 라운드 시작(준비 중 몬스터 없음), 골동품 15개·서랍장 6개·몬스터 2마리, 목표 30%, 서랍장 들어가기·나오기와 문 소리, 숨은 인형은 안 보이고 못 잡음, 벽 너머로 못 잡음, 웅크리면 조용·달리면 소리, 가벼운 물건 들기, 탈출 지점에 놓으면 금액 +·안정도 1,000당 20초 감소, 무거운 물건 혼자 못 듦, 서버의 잡기 검증(벽 너머, 물건 밖 지점), 붕괴값, 잡히면 솔로 라운드 실패, 새 맵으로 재시작
- 호스트 + 참가자: 참가자가 같은 시드로 같은 맵을 만듦(해시 일치), 골동품·서랍장·몬스터가 참가자에게 보임, 참가자 이동이 호스트에 동기화, 목표 40%(2인), 참가자 잡기를 서버가 받아들임, 25kg 액자를 혼자는 못 들고 둘이 잡으면 0.66 m 들림

## 확인하지 않은 것

- 사람이 직접 마우스·키보드로 하는 플레이, 두 대의 다른 PC 사이 접속 (같은 PC의 127.0.0.1만)
- UE5 `-dmprocexpect=deskhide` 책상 밑 추격 시나리오와 울타리 시나리오 (집사 로직은 옮겼지만 이 동적 시나리오 테스트는 아직 옮기지 않음)
- 오일 파스텔 셰이더 (이번 범위 밖. HUD는 `_DreamCollapse` 전역 값만 넘김)
