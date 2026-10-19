# 데이터 테이블 (CSV)

게임의 모든 수치는 `Assets/_Project/Data/Tables/` 의 CSV 4개에 있습니다. 코드를 고치지 않고 표만 바꿔서 밸런스를 조정합니다.

| 파일 | 내용 |
|---|---|
| `zombies.csv` | 좀비 3종 능력치 (체력·속도·공격력·공격 간격·크기) |
| `buildings.csv` | 건물 7종 이름·설명·최대 레벨·비용·건설 시간·생산량 (레벨당 증가율 포함) |
| `hordes.csv` | 대규모 습격 시각과 마리 수 |
| `balance.csv` | 나머지 단일 수치 전부 — 경제, 밤 난이도, 보상, 웨이브, 영웅, 스킬, 포탑, 본부 |

## 고치는 방법

1. 메뉴 **ZombieSLG ▸ 데이터 테이블 폴더 열기**
2. 엑셀·구글 시트·VS Code 등으로 CSV 수정
   - 엑셀에서 저장할 때는 **"CSV UTF-8 (쉼표로 분리)"** 형식으로 저장하세요. 그냥 "CSV"로 저장하면 한글이 깨집니다.
   - `#` 으로 시작하는 줄은 주석입니다.
   - `balance.csv` 의 `key` 는 코드가 읽는 이름이라 바꾸면 안 됩니다. `value` 만 고치세요.
3. Unity로 돌아오면 **자동으로 가져옵니다** → Console에 `데이터 테이블 가져오기 완료`
4. Play

## 흐름

```
CSV (Data/Tables)  ──저장──▶  에디터 임포터  ──검증 통과──▶  Resources/GameData.asset  ──▶  GameData (런타임)
                                    │
                                    └─ 오류면 Console에 "파일:줄 [열]" 로 표시하고 이전 데이터를 그대로 사용
```

- 런타임에는 CSV를 읽지 않습니다. 임포터가 만든 ScriptableObject(`GameData.asset`)의 행 데이터로 `GameData` 를 만듭니다.
- 검증 항목: 숫자 형식, 최솟값·최댓값, 빠진 행(좀비·건물 종류), 알 수 없는 id, 중복, 빠진 `balance.csv` 키, 습격 시각 순서·밤 길이 초과.
- 같은 검증이 테스트(`dotnet test tools/DomainTests`)와 CI에서도 돌아서, 잘못된 표는 PR 단계에서 막힙니다.
- M3에서 Remote Config 값을 `BalanceTable.ApplyOverrides` 로 덮어써서 출시 후 밸런스를 조정합니다.

## 새 수치를 추가할 때 (코드 작업)

1. `balance.csv` 에 행 추가
2. 도메인 규칙이면 `GameDataBuilder` 에서 읽고, 화면/전투 쪽이면 `GameDataBuilder.PresentationKeys` 와 `BattleConfig.From` 에 추가
3. 테스트 `EveryBalanceKeyIsUsed` 가 "표에는 있는데 코드가 안 읽는 키"를 잡아 줍니다
