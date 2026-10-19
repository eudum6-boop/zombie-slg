# 좀비 생존 SLG (가제)

> 무너진 한국 도시에서 낮에는 피난처를 키우고, 밤에는 몰려오는 감염체를 직접 막아내는 생존 전략 게임.

Unity 6 · URP · 모바일 세로 화면 · 1인 개발

<!-- 플레이 영상 / 스크린샷: docs/media/ 에 넣고 여기에 링크 -->

![Domain tests](https://github.com/eudum6-boop/zombie-slg/actions/workflows/domain-tests.yml/badge.svg)

---

## 지금 되는 것

| 단계 | 내용 | 상태 |
|---|---|---|
| **M0 전투 프로토타입** | 3분 밤 습격 · 좀비 3종 · 플로우 필드 길찾기 · 영웅 1명 + 화염 폭격 · 기관총 포탑 · 타격감(히트 스톱, 화면 흔들림, 데미지 숫자, 파편) | ✅ |
| **M1 기지 경영 루프** | 건물 7종 · 생산/창고 상한/식량 소비/사기 · 생존자 배치 · 건설 대기열 · 오프라인 정산 · 암호화 로컬 세이브 · 낮↔밤 루프 | ✅ |
| M2 영웅·병력·가챠 | CSV → ScriptableObject 데이터 파이프라인, 영웅 8명, 가챠·천장 | ⏳ |
| M3 서버 연동 | UGS 인증 · Cloud Save · Cloud Code 서버 시간 타이머 | ⏳ |
| M4 최적화·폴리싱 | Jobs/Burst + GPU 인스턴싱, Addressables, 튜토리얼 | ⏳ |
| M5 출시 | IAP·광고 테스트 연동, 구글 플레이 출시 | ⏳ |

## 실행

1. Unity **6000.0.84f1** 로 프로젝트 열기
2. 메뉴 **ZombieSLG ▸ 0. 전체 설정** (3D URP 렌더러 + 씬 생성, 최초 1회)
3. Game 뷰를 1080×1920 등 세로 해상도로 두고 **Play**

| 조작 | |
|---|---|
| 낮: 건물 탭 | 건설 / 업그레이드 / 인력 배치 |
| 밤: 땅 탭·드래그 | 영웅 이동 (사격은 자동) |
| Q / 버튼 → 땅 탭 | 화염 폭격 |
| T | 좀비 200마리 스트레스 테스트 (FPS 표시) |

에디터 메뉴: `ZombieSLG ▸ 진행 데이터 초기화` · `세이브 폴더 열기`

## 아키텍처

게임 규칙은 Unity에 의존하지 않는 순수 C#에 두고, 의존성은 모두 안쪽(Domain)을 향합니다. 어셈블리 정의(asmdef)로 컴파일 단계에서 강제합니다.

```mermaid
flowchart LR
    P["Presentation<br/>ZombieSLG.Battle<br/>View · HUD · GameFlow"] --> A["Application<br/>BaseService · EventBus"]
    P --> I["Infrastructure<br/>LocalSaveRepository · SystemClock"]
    I --> A
    A --> D["Domain (순수 C#)<br/>FlowField · WaveDirector · BaseState<br/>BuildingCatalog · SaveMigrator"]
    I --> D
    P --> D
```

| 어셈블리 | UnityEngine 참조 | 내용 |
|---|---|---|
| `ZombieSLG.Domain` | ❌ | 길찾기, 웨이브, 전투 공식, 기지 경제, 세이브 모델·마이그레이션 |
| `ZombieSLG.Application` | ❌ | 유스케이스(BaseService), 이벤트 버스, 시계·저장소 인터페이스 |
| `ZombieSLG.Infrastructure` | ✅ | AES 암호화 JSON 세이브(원자적 쓰기 + 백업), 시스템 시계 |
| `ZombieSLG.Battle` | ✅ | 맵·좀비·영웅·포탑·HUD·낮밤 상태 머신 |
| `ZombieSLG.Tests.EditMode` | — | Domain/Application 단위 테스트 |

### 기술 포인트

- **플로우 필드** — 목표(본부)가 하나라서 Dijkstra 한 번으로 모든 좀비가 같은 방향장을 공유합니다. 대각선 모서리 통과 금지, 도달 불가 셀 처리.
- **대량 유닛** — M0 기준선은 GameObject + 풀링 + 공간 해시(이웃 탐색 O(n)). M4에서 Jobs/Burst + GPU 인스턴싱으로 바꾸고 전후 프로파일을 비교합니다.
- **결정적 웨이브** — 시드 기반 `WaveDirector`. 같은 밤은 항상 같은 스폰 → 재현·테스트·서버 검증이 가능합니다.
- **오프라인 정산** — 건설 완료 시점에서 시간 구간을 나눠 정산합니다. "한 번에 2시간 점프"와 "1초씩 7200번"의 결과가 같은지 테스트로 보장합니다.
- **세이브 버전 관리** — `GameSave.Version` + `SaveMigrator`. 새 건물이 추가돼도 기존 세이브가 깨지지 않습니다.

## 테스트

```powershell
dotnet test tools/DomainTests      # Unity 없이 게임 규칙 테스트 (CI와 동일)
```

Unity: `Window ▸ General ▸ Test Runner ▸ EditMode ▸ Run All`

## 폴더

```
Assets/_Project/
  Scripts/
    Domain/          순수 C# 게임 규칙
    Application/     유스케이스, 이벤트 버스
    Infrastructure/  세이브, 시계
    Battle/          프레젠테이션 (전투 + 기지)
    Editor/          설정 메뉴
  Tests/EditMode/    단위 테스트
tools/DomainTests/   .NET 테스트 프로젝트 (CI용)
docs/                작업 가이드, 개발기
```

## 작업 방식

브랜치·커밋 규칙, CI, 마일스톤 관리: [docs/GIT_WORKFLOW.md](docs/GIT_WORKFLOW.md)
