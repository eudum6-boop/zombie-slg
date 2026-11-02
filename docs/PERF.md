# 성능 측정 — 좀비 대량 처리 (M4)

좀비 시스템은 `IZombieSystem` 하나에 구현이 둘입니다. 같은 조건에서 바꿔 가며 측정합니다.

| 모드 | 클래스 | 시뮬레이션 | 그리기 |
|---|---|---|---|
| **GameObjects** (M0 기준선) | `ZombieManager` | 메인 스레드, 좀비마다 Transform 갱신 | 좀비마다 GameObject 3개(몸·팔·눈) |
| **Jobs** (M4) | `ZombieSystemJobs` | Burst `GridJob`(카운팅 정렬) → Burst `SteerJob`(병렬 조향·분리·벽 미끄러짐) | `Graphics.RenderMeshInstanced` 타입×부위별 인스턴싱, GameObject 0개 |

두 구현은 같은 규칙(목표 선택, 플로우 필드, 분리, 넉백, 공격)을 씁니다. 행동은 같고 처리 방식만 다릅니다.

## 측정 방법

1. Hierarchy에서 `BattleBootstrap` 오브젝트 선택 → Inspector **Zombie Mode** 를 고릅니다.
2. Play → 밤 맞이하기 → **T** 키를 4번 눌러 좀비를 800마리(상한)까지 채웁니다.
   게임은 60 FPS로 제한되어 있어 둘 다 60이 나옵니다. **U** 키로 제한을 풀면 FPS 차이가 보입니다.
3. 왼쪽 위 디버그 표시의 **FPS / 프레임 ms / 좀비 처리 ms** 를 10초 정도 지켜보고 평균을 적습니다.
4. 더 정확히: `Window ▸ Analysis ▸ Profiler` 에서 마커 `Zombies.Jobs.Simulate`, `Zombies.Jobs.Draw`, `Zombies.GameObjects.Tick` 를 확인합니다.
5. 모드를 바꿔 같은 과정을 반복합니다.

> 에디터는 실제 기기보다 느리고 Burst는 에디터에서 첫 실행 때 컴파일합니다(잠깐 끊김). 몇 초 지난 뒤 숫자를 보세요.

## 결과

측정 환경: (CPU / GPU / Unity 6000.0.84f1 / 에디터·빌드 여부)

| 좀비 수 | GameObjects: FPS · 좀비 ms | Jobs: FPS · 좀비 ms | 비고 |
|---|---|---|---|
| 800 | 60 (제한) · 1.1–1.2 ms | 60 (제한) · 0.5–0.6 ms | 에디터, 2026-11-02. 좀비 처리 메인 스레드 시간 약 **2배 감소** |
| 800 (U: 제한 해제) | **150–200 FPS** | **350–400 FPS** | 같은 조건, 프레임 전체 기준 약 **2배 이상** (≈5.7 ms → ≈2.7 ms/프레임) |

해석:
- `좀비 ms`는 메인 스레드에서 좀비 처리(시뮬레이션 + 그리기 제출)에 쓴 시간입니다. Jobs 쪽은 조향 계산이 워커 스레드로 빠져 메인 스레드 몫이 절반이 됐습니다.
- GameObjects 쪽은 이 숫자에 **안 잡히는 비용**이 더 있습니다: 좀비마다 Transform 3개의 갱신 동기화와 렌더러 2,400개의 컬링·드로우. Jobs 쪽은 타입×부위별 인스턴싱 드로우 몇 번으로 끝납니다. 실제 차이는 Profiler의 렌더링 항목과 U 키(프레임 제한 해제) FPS로 확인합니다.
