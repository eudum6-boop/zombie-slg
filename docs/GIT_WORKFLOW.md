# Git / GitHub 작업 가이드

1인 개발 + 포트폴리오 기준. 원칙은 세 가지입니다.

1. **`main`은 항상 플레이 가능한 상태**로 둔다.
2. **작업 하나 = 브랜치 하나 = PR 하나.** 혼자 개발해도 PR을 남기면 그대로 개발 기록이 됩니다.
3. **커밋은 작게, 자주.** 특히 Claude에게 큰 작업을 맡기기 전에는 꼭 커밋해 둡니다.

---

## 1. 준비 (최초 1회)

| 설치할 것 | 용도 |
|---|---|
| [Git for Windows](https://git-scm.com/download/win) | Git 본체. Git LFS가 같이 설치됩니다 |
| [GitHub Desktop](https://desktop.github.com/) (선택) | 변경 내용 보기·커밋을 화면에서. 명령어가 익숙하지 않으면 추천 |
| GitHub 계정 | 저장소 호스팅 |

PowerShell에서 한 번만 실행합니다.

```powershell
git config --global user.name  "내 이름"
git config --global user.email "eudum6@gmail.com"
git config --global init.defaultBranch main
git config --global core.autocrlf true

# 이미지·모델·사운드는 LFS로 (첫 커밋 전에 꼭!)
git lfs install

# Unity 씬/프리팹 병합 도구 (.gitattributes의 merge=unityyamlmerge가 이걸 사용)
git config --global merge.unityyamlmerge.name "Unity SmartMerge"
git config --global merge.unityyamlmerge.driver "'C:/Program Files/Unity/Hub/Editor/6000.0.84f1/Editor/Data/Tools/UnityYAMLMerge.exe' merge -p %O %B %A %A"
git config --global merge.unityyamlmerge.recursive binary
```

> Unity 버전을 올리면 위 경로의 `6000.0.84f1` 부분도 바꿔 주세요.

**Unity 설정 확인** — `Edit ▸ Project Settings ▸ Editor`
- Version Control → Mode: **Visible Meta Files**
- Asset Serialization → Mode: **Force Text**

(Unity 6 기본값이라 보통 이미 되어 있습니다.)

---

## 2. 저장소 만들기 (최초 1회)

1. GitHub → **New repository**
   - 이름 예: `zombie-slg`
   - **Private** 로 시작 (공개 전 체크리스트는 §9)
   - README / .gitignore / license **추가하지 않기** (프로젝트에 이미 있음)
2. PowerShell:

```powershell
cd "C:\Users\USER\My project (1)"

# CI·PR 템플릿을 제자리로 옮기기 (docs/github → .github, 최초 1회)
Move-Item docs\github .github

git init
git add .
git status          # ← Library/, Temp/, Logs/ 가 목록에 없어야 정상
git commit -m "chore: 첫 커밋 (M0 전투 프로토타입 + M1 기지 루프)"
git remote add origin https://github.com/<내아이디>/zombie-slg.git
git push -u origin main
```

GitHub Desktop을 쓴다면: **File ▸ Add local repository** → 폴더 선택 → "create a repository" → **Publish repository**.

---

## 3. 매일 작업 흐름

```
main ──●──────────────●─────────── (항상 플레이 가능)
        \            /  squash merge
         ●──●──●──●─●   feat/m2-csv-pipeline
```

```powershell
# 1) 최신 main에서 작업 브랜치 만들기
git switch main
git pull
git switch -c feat/m2-csv-pipeline

# 2) 작업하면서 수시로 커밋
git add -A
git commit -m "feat(data): 건물 테이블 CSV 임포터 추가"

# 3) 올리고 PR 만들기
git push -u origin HEAD
#   → GitHub에 뜨는 "Compare & pull request" 클릭
#   → PR 템플릿 체크리스트 확인, CI(Domain tests) 초록불 확인
#   → "Squash and merge"

# 4) 정리
git switch main
git pull
git branch -d feat/m2-csv-pipeline
```

### 브랜치 이름

| 접두사 | 언제 | 예 |
|---|---|---|
| `feat/` | 새 기능 | `feat/m2-hero-gacha` |
| `fix/` | 버그 수정 | `fix/zombie-stuck-at-gate` |
| `refactor/` | 동작 그대로, 구조만 | `refactor/ui-mvp` |
| `chore/` | 설정·도구·패키지 | `chore/add-unitask` |
| `docs/` | 문서 | `docs/devlog-flowfield` |

### 커밋 메시지 (Conventional Commits)

```
<종류>(<범위>): <무엇을 했는지, 한 줄>
```

- 종류: `feat` `fix` `refactor` `test` `docs` `chore` `perf`
- 범위 예: `battle` `base` `domain` `save` `ui` `data` `ci`
- 예시
  - `feat(base): 의무실 건물 추가`
  - `fix(battle): 동문에서 좀비가 끼는 문제 수정`
  - `perf(battle): 좀비 이동을 Jobs+Burst로 이전 (200마리 4.1ms → 0.6ms)`
  - `test(domain): 오프라인 정산 분할 테스트`

성능 개선 커밋에는 **전후 수치**를 적어 두세요. 나중에 개발기와 포트폴리오에 그대로 씁니다.

---

## 4. Claude와 같이 작업할 때

Claude는 프로젝트 폴더의 파일을 직접 고칩니다. Git이 있으면 이 변경을 안전하게 검토하고 되돌릴 수 있습니다.

1. **요청 전에 커밋** (또는 새 브랜치) — 깨끗한 상태에서 시작
2. Claude 작업이 끝나면 **변경 내용 확인**
   - GitHub Desktop의 Changes 탭, 또는 `git status` / `git diff`
   - 예상하지 않은 파일이 바뀌었으면 물어보기
3. Unity에서 **Ctrl+R → 컴파일 확인 → Play 확인**
4. 괜찮으면 커밋. 커밋 메시지는 Claude에게 "커밋 메시지 써줘"라고 해도 됩니다.

되돌리기:

```powershell
git restore .            # 커밋 안 한 변경 전부 버리기 (새 파일은 남음)
git clean -fd -n         # 새로 생긴 파일 목록 미리 보기 (-n 빼면 실제 삭제)
git reset --hard HEAD~1  # 마지막 커밋 취소 (push 전에만!)
```

---

## 5. 마일스톤 관리

기획서 로드맵(M0–M5)을 GitHub 기능에 그대로 대응합니다.

| 로드맵 | GitHub |
|---|---|
| M0 전투 ~ M5 출시 | **Milestones** 6개 |
| 각 작업 | **Issues** (템플릿: 작업 / 버그) — 마일스톤 지정 |
| 진행 현황 | **Projects** 보드 (Todo / In progress / Done) |
| 마일스톤 완료 | **태그 + Release** (빌드 APK·플레이 영상 첨부) |

PR 설명에 `Closes #12` 를 쓰면 머지할 때 이슈가 자동으로 닫힙니다.

마일스톤이 끝나면 태그를 붙입니다.

```powershell
git switch main
git pull
git tag -a v0.2.0-m1 -m "M1 기지 경영 루프"
git push origin --tags
```

추천 라벨: `task` `bug` `battle` `base` `data` `perf` `ui` `infra` `devlog`(개발기로 쓸 거리)

---

## 6. CI (GitHub Actions)

`.github/workflows/` 에 두 개가 들어 있습니다.

| 워크플로 | 언제 | 하는 일 | 필요한 것 |
|---|---|---|---|
| **Domain tests** | push(main) · 모든 PR | 게임 규칙(Domain/Application) 테스트를 .NET으로 실행 | 없음 — 바로 동작 |
| **Unity tests** | 수동 실행 | Unity 에디터로 EditMode 테스트 전체 | Unity 라이선스 시크릿 (§8) |

로컬에서도 같은 테스트를 돌릴 수 있습니다 (.NET 8 SDK 필요).

```powershell
dotnet test tools/DomainTests
```

Unity 안에서는 `Window ▸ General ▸ Test Runner ▸ EditMode ▸ Run All`.

---

## 7. LFS — 큰 파일

`.gitattributes` 에 이미 png/jpg/psd/fbx/wav/mp3/ogg/ttf 등이 LFS로 지정되어 있습니다.

- 에셋 팩을 넣기 **전에** `git lfs install` 이 되어 있는지 확인
- 새 확장자를 쓰게 되면: `git lfs track "*.확장자"` → `.gitattributes` 도 커밋
- GitHub 무료 LFS 용량은 한정적입니다(저장소/대역폭). 큰 에셋 팩은 필요한 파일만 프로젝트에 남기세요.
- 확인: `git lfs ls-files`

---

## 8. Unity CI 켜기 (선택)

Unity Personal 라이선스로 GameCI를 쓰는 방법입니다.

1. Unity Hub → **Preferences ▸ Licenses ▸ Add** → Personal 라이선스 추가 (이미 있어도 한 번 더 Add)
2. 라이선스 파일 위치: `C:\ProgramData\Unity\Unity_lic.ulf` (숨김 폴더)
3. GitHub 저장소 → **Settings ▸ Secrets and variables ▸ Actions ▸ New repository secret**
   - `UNITY_LICENSE` : `.ulf` 파일 내용 전체
   - `UNITY_EMAIL` : Unity 계정 이메일
   - `UNITY_PASSWORD` : Unity 계정 비밀번호
4. **Actions ▸ Unity tests ▸ Run workflow** 로 실행
5. 잘 되면 `unity-tests.yml` 의 `on:` 에 `pull_request:` 를 추가해서 PR마다 돌릴 수 있습니다.

> `.ulf` 파일 자체는 절대 커밋하지 마세요 (`.gitignore` 에 이미 제외되어 있음).

---

## 9. 공개(Public) 전 체크리스트

포트폴리오로 공개하기 전에 확인합니다.

- [ ] **유료 에셋 스토어 에셋이 저장소에 없는가** — 공개 저장소에 올리면 라이선스 위반입니다. 공개용은 에셋을 빼고, "에셋 팩 별도 구매 필요"를 README에 적습니다.
- [ ] 키스토어(`.keystore`), API 키, 광고/IAP 키, `.ulf` 가 히스토리에 없는가
- [ ] README에 플레이 영상(1–2분), 아키텍처 다이어그램, 실행 방법이 있는가
- [ ] Release에 플레이 가능한 APK 또는 스토어 링크가 있는가

과거 커밋에 비밀값이 들어갔다면 파일만 지워서는 안 되고, 키를 **폐기·재발급**해야 합니다.

---

## 10. 자주 생기는 문제

| 증상 | 해결 |
|---|---|
| `Library/` 가 커밋됨 | `git rm -r --cached Library` → 커밋 (`.gitignore` 에 있는지 확인) |
| push가 거절됨 (rejected) | `git pull --rebase` 후 다시 `git push` |
| 씬 파일 충돌 | 1인 개발이면 거의 없음. 생기면 `git mergetool` (UnityYAMLMerge) 또는 한쪽 선택: `git checkout --ours/--theirs 파일` |
| 이미지가 포인터 텍스트로 보임 | LFS 미설치 상태로 clone → `git lfs install` 후 `git lfs pull` |
| `.meta` 만 바뀌었다고 뜸 | 정상인 경우가 많음. 단, `.meta` 를 지우거나 빼먹으면 참조가 깨지니 항상 원본 파일과 같이 커밋 |
| 줄바꿈 경고 (LF/CRLF) | 무시해도 됨 (`core.autocrlf true` + `.gitattributes`) |
