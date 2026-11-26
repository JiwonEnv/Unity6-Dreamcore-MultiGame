# ProcLevelGen

시드 기반 방 모듈 조립기 (Unity 6.3, C#). UE5 플러그인(태그 `ue5-final`의 `Plugins/ProcLevelGen`)을 그대로 옮겼습니다.

미리 정한 방 모듈을 문 소켓으로 이어 붙이고 겹침을 검사합니다. 결과로 그레이박스 바닥과 벽(문 구멍 포함), 책상 소품을 짓고 내비메시를 만듭니다.

**UE5와 같은 결과**: 난수(`FRandomStream`)와 해시까지 비트 단위로 옮겨서, 같은 시드는 UE5와 같은 레이아웃을 만듭니다. 시드 1~200과 셀프테스트 시드 535117761의 레이아웃 해시가 UE5 로그와 모두 같습니다 (`Tests/Editor/UE5SeedHashes.txt`).

## 사용

1. 씬에 빈 GameObject를 만들고 `PLGGenerator`를 붙입니다. `NavMeshSurface`가 같이 붙습니다.
2. 인스펙터의 **Preview** / **Preview New Seed**로 에디터에서 바로 확인합니다. 미리보기는 씬에 저장되지 않고, 씬 뷰에 모듈 이름과 테마가 표시됩니다.
3. 플레이하면 서버(오프라인 포함)가 새 시드를 고르고 맵을 짓습니다. 시드를 고정하려면 `New Seed Each Run`을 끄세요.
4. 멀티플레이: 서버는 `SeedChosen` 이벤트로 받은 시드를 보내고, 클라이언트는 `IsAuthority = false`로 두었다가 `ApplyServerSeed(시드)`로 짓습니다. 네트워크 연결은 아직 없습니다.
5. 생성이 끝나면 `Generated` (코드) / `OnGenerated` (인스펙터) 이벤트가 (생성기, 성공 여부)로 호출됩니다. 실패했으면 아무것도 지어지지 않았으니 라운드를 시작하지 마세요. 서버는 `IsNavigationReady()`가 참이 된 뒤 AI를 움직이면 됩니다. 콘텐츠는 `GetSpawnSlots(태그)`로 모듈의 스폰 포인트를 받아 배치합니다. 무작위 선택에는 시드에서 만든 `MakeContentStream()`을 씁니다.

시작 모듈은 항상 생성기 위치(원점)에 놓이므로, 플레이어 시작 지점은 시작 모듈 안에 고정해 두면 됩니다.

## 파일

| 파일 | 내용 |
|---|---|
| `Runtime/PLGTypes.cs` | 모듈·소켓·테마·배치 결과 타입 |
| `Runtime/PLGLayout.cs` | 조립 알고리즘 (씬에 의존하지 않는 순수 함수), 규격 검사, 해시, 샘플 키트 |
| `Runtime/PLGRandomStream.cs` | UE5 `FRandomStream`과 해시 함수 이식 |
| `Runtime/PLGSpace.cs` | 평면 좌표(cm, Z 위) ↔ Unity 좌표(m, Y 위) 변환 |
| `Runtime/PLGGenerator.cs` | 씬 컴포넌트: 시드, 그레이박스, 내비메시, 스폰 슬롯, 셀프테스트 |
| `Editor/PLGGeneratorEditor.cs` | Preview 버튼 |
| `Tests/Editor` | 시드 1~200 검사와 UE5 해시 비교 (에디트모드) |
| `Tests/Runtime` | 셀프테스트: 내비메시, 플레이어 바닥, 모든 모듈 경로, 스크린샷 (플레이모드) |
| `Docs/ModuleSpec.md` | 모듈 규격: 좌표, 피벗, 그리드, 벽, 소켓, 생성 순서 |

## 테스트

```
Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode -assemblyNames ProcLevelGen.Tests.Editor -testResults Logs/editmode.xml
Unity.exe -batchmode -projectPath . -runTests -testPlatform PlayMode -assemblyNames ProcLevelGen.Tests.Runtime -testResults Logs/playmode.xml
```

플레이모드 셀프테스트는 `Logs/PLGSelfTest/`에 플레이어 시점과 내비메시를 겹친 위쪽 스크린샷을 남깁니다.

## UE5와 다른 점

- 그레이박스는 인스턴스드 메시 대신 상자마다 GameObject(큐브 + BoxCollider)를 씁니다. 상자 수는 같습니다 (시드 535117761에서 113개).
- 내비메시는 `NavMeshSurface`(AI Navigation 패키지)가 생성기 아래 콜라이더로 비동기로 짓습니다.
- 셀프테스트에 `Player` 태그 오브젝트가 없으면 캡슐 프로브가 시작 모듈에 대신 섭니다. 위쪽 스크린샷의 초록색은 UE의 `show Navigation` 대신 그린 내비메시입니다.
- 로그는 `Debug.Log`입니다. 모듈별 위치는 `Log Modules`를 켜면 나옵니다.
