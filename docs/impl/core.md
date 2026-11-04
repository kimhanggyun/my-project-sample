# 규칙 핵심 코드 (순수 C#)

> 엔진과 무관한 규칙 모듈. Unity 클라이언트와 서버가 같은 코드를 쓴다.
> 구조 설계: [../design/structure.md](../design/structure.md) · 개발 규칙: [../dev-rules.md](../dev-rules.md) · 검증: [../design/verification.md](../design/verification.md)

## 1. 폴더

```
GameCore.sln
src/                       규칙 모듈 (netstandard2.1, C# 9 — Unity 호환)
├─ Game.Foundation/        고정소수점(Fix), 시드 난수, 시간(ITimeProvider), 세이브 저장소 경계
├─ Game.Data/              데이터 읽기, 덮어쓰기 병합(BalanceResolver), 규칙 스위치(RuleFlags), 정의, 데이터 검사
├─ Game.Services/          외부 서비스 경계(로그인·결제·광고)와 끔·가짜 구현, 서비스 설정·등록표
├─ Game.Battle.Model/      전투 시뮬레이션(5장), 피해 계산, 사기 보정, 책략 점수, 지형 전투 보정, 화면 병사 비율
├─ Game.Campaign/          보유 장수·부대, 편성 검사, 병력 한도, 손실 분배, 초기 지급, 세이브(버전·변환기)
├─ Game.Economy/           지갑, 군량 계산, 치료 비용
└─ Game.Online/            부대 고용 계산
tests/Game.Tests/          단위·규칙(R)·수치(N)·데이터 검사·의존 방향 테스트, 기준 전투(Golden/)
data/                      규칙 데이터(Rules/), 맵(Maps/), 테마 데이터(Themes/<테마>/) — Unity에서는 StreamingAssets로 복사
config/                    외부 서비스 설정 (services.<환경>.json) — 규칙 스위치와 별개
.github/workflows/         push·PR마다 빌드·테스트
```

## 2. 의존 방향

```
Game.Battle.Model ─┐
Game.Campaign ─────┼─▶ Game.Data ─▶ Game.Foundation
Game.Economy ──────┤
Game.Online ───────┘
Game.Services ───────────────────▶ Game.Foundation
```

- `ArchitectureTests`가 허용되지 않은 참조(다른 규칙 모듈, Unity)를 잡는다.

## 3. 핵심 결정

| 항목 | 결정 | 이유 |
|---|---|---|
| 숫자 | **`Fix`: 10진 고정소수점, 소수 4자리** (값 × 10,000, long) | 기기 간 같은 결과(서버 재현·대전). 10진이라 0.4·0.25·72.5가 정확 |
| 반올림 | 곱셈 내림, 나눗셈 0 쪽 버림. 결과별 규칙은 각 계산기에 명시 (예: 병력 한도 내림, 치료 비용 올림) | 결정적 |
| JSON | Newtonsoft.Json 13 (Unity는 `com.unity.nuget.newtonsoft-json` 패키지) | Unity·서버 공통. 소수는 decimal로 읽음 |
| 난수 | SplitMix64 (`SeededRandom`) | 단순·결정적, 상태 저장 가능 |

## 4. 테스트 실행

```bash
dotnet test GameCore.sln
```

- 규칙 테스트(R)는 `data/`의 현재 값으로 계산한다 → 밸런스를 바꿔도 깨지지 않는다.
- 수치 테스트(N)는 `tests/Game.Tests/Fixtures/balance.example.json`(verification.md의 예시 수치)으로 고정한다.

## 5. 전투 시뮬레이션 (Step 2a)

### 5.1 경계

```
BattleSetup (맵·정의·balance·combat·규칙 스위치·시드·군단 목록·패배 조건·시간 제한)
   + 명령 기록 (BattleCommand: 틱·군단·종류·좌표/대상/진형/방식)
        │
        ▼
BattleSimulation  ── Step() 한 틱씩 / Issue(명령) / RunToEnd() / Run(setup, 기록)
        │
        ▼
BattleResult (승자·끝난 이유·틱·군단별 남은 병력·군량·사기·소부대별 병력·받아들인 명령·상태 지문)
```

- 전투는 경제·포로·고용을 모른다. 손실의 부상·사망 나누기(R-9), 군량 몰수(R-8)는 바깥 모듈이 결과로 처리한다.
- 장수 능력치는 `GeneralSnapshot`으로 받는다 (부상·고용 반영은 캠페인 쪽).
- 화면(Unity)은 `Context`를 읽기만 하고, 입력은 `Issue`로만 넣는다. 받을 수 없는 명령(AI 군단, 퇴각 중, 등록 안 된 종류 …)은 거절되고 기록되지 않는다.
- 같은 setup + 같은 명령 기록 → 같은 `StateHash` (R-29). 리플레이·서버 검증은 `Run(setup, result.Commands)`.

### 5.2 틱 순서 (고정 0.1초, `combat.json`)

1. 명령 적용 → 2. AI 판단 (`aiThinkEveryTicks`마다) → 3. 스킬 (사거리까지 다가가기, 전조 끝난 효과) → 4. 도발 강제
→ 5. 이동 (A* 길 찾기, 소부대는 진형 칸 위치) → 6. 시야 (`vision.updateEveryTicks`마다) → 7. 은신·복병
→ 8. 전투 → 9. 군량 소비 → 10. 사기 → 11. 상태 시간·책략 점수 회복 → 12. 궤멸·퇴각 → 13. 승패.

| 시스템 | 내용 |
|---|---|
| 이동 | 목적지(명령) → 8방향 A* (병종별 진입 불가·이동 비용, 군단은 가장 느린 값). 못 가는 목표면 가장 가까운 칸까지. 속도 = 기준 속도 × 가장 느린 병종 속도/50 ÷ 칸 비용 |
| 전투 | ① 사거리 안의 적은 반드시 공격 (R-20). ② 근접 소부대 하나라도 적 군단에 닿으면 **접전**: 그 군단의 근접 소부대 전부가 접전 상대와 싸운다. 피해는 동시에 계산 후 한꺼번에 적용 |
| 피해 | 병력 × 초당 기본 피해 × 틱 × (100 + 보정 합)/100 (R-1·R-2). 보정 출처: 능력치, 병종 상성, 진형 상성, 측면·배후, 지형 전투, 높이, 사기, 무작위 폭 (시드 난수) |
| 군량 | R-6과 같은 식을 틱마다 누적 (교전 없으면 `SupplyCalculator.BattleConsumption`과 같은 값, 테스트로 확인) |
| 사기 | 손실 %당 감소, 측면·배후 피격 감소, 군량 바닥 감소, 싸우지 않으면 시작값까지 회복 |
| 퇴각 | 사기 ≤ 성향 기준(`temperaments.json`) 또는 병력 ≤ 시작 × 비율 → 퇴각 방향 끝으로 이동 → 이탈 |
| 승패 | 등록된 패배 조건 (`annihilation`, `commander`) 중 하나라도 만족한 팀 패배. 시간 제한이면 남은 병력 비율 |
| 시야 | 팀마다 하나 (아군 시야를 합침, 플레이어·AI 공통). 소부대 시야 = 기준 + 시야 능력치·장수 지략·높이 보정 − 숨는 지형 안 감소. 숨는 지형(`conceals`: 숲·수풀) 안의 적은 가까이 가야 보임. 칸 3단계(미탐색·탐색함·보임), 적 마지막 목격 위치. 보이지 않는 적은 공격·지정 불가. 스위치 `fogOfWar`, 탐색 기억 `fogMemory`(`BattleSetup.InitialExplored` → `BattleResult.Explored`) |
| 스킬 | `useSkill` 명령. 받는 순간 책략 점수·횟수를 냄 (R-11, R-12). 사거리 밖이면 다가간 뒤 사용, 대상을 잃으면 취소. 전조(`telegraphSec`) 뒤 효과. 점수는 사기 ≥ 기준일 때 회복 (R-13) |
| 은신·복병 | 숨는 지형에서 준비(움직이거나 들키면 실패) → 은신(안 보이고 공격 안 함, R-20 예외) → 적이 사거리에 들면 기습: 기습 보정, 닿은 적 사기↓·일부 소부대 혼란(`ambushConfusion`). 간파 범위에 들거나 범위 피해를 맞거나 움직이면 드러남 |
| 상태 | 군단 상태(버프·도발·복병 준비 …, 시간 제한)와 소부대 혼란(공격 안 함). 공격 보정이 있는 상태는 피해 보정 출처로 더해짐 |

| 대결 | 전투 직전 일기토·논쟁 (`BattleSetup.Contests`). 한 엔진, 행동·결과표·전투 영향은 `contests.json` (논쟁은 일기토 결과표를 그대로 씀). 선택 기록이 없거나 모자라면 AI가 고름. 결과 → 사기·책략 점수·혼란 뒤 퇴각·부상(`BattleResult.GeneralInjuries`, R-23). 발생 판정은 `ContestTrigger` (바깥에서) |
| 군단 AI | 성향(공격형·방어형·매복형)이 이동 명령을, 장수 판단(`AiPlanner`)이 스킬·진형을 고른다. 스킬은 점수 매기기 규칙(`AiSkillRules`)으로, 플레이어와 같은 명령 처리기·제한(점수·횟수·시야). 난이도(`ai.json`: 쉬움·보통)는 판단 빈도·스킬 사용 확률·상성 대응 확률·정찰 여부만 바꾼다 |

### 5.3 확장 (등록표, `BattleExtensions`)

| 등록표 | 기본 등록 | 새로 붙이는 예 |
|---|---|---|
| `CommandRegistry` | advance, moveTo(normal·forced·attackMove), attack, hold, retreat, changeFormation | 스킬 사용, 경유, 협공 대기 |
| `AiRegistry` | aggressive, defensive, ambusher | 유격형·지원형·신중형, 사령관 AI 작전 방침 |
| `AiSkills` | morale, clearConfusion, attackBuff, areaDamage, taunt, reveal (스킬 첫 효과 종류로 찾음) | 화계(순풍·숲), 연계 판단 |
| `VictoryRegistry` | annihilation, commander | 거점 점령, 생존 시간 |
| `DamageSources` | 위 보정 출처 7종 + 상태 공격 보정 | 날씨, 진형 특성치 |
| `SkillRegistry` | 효과: reveal, ambush, areaDamage, morale, clearConfusion, attackBuff, contactMoraleDamage, taunt · 조건: casterOnConcealingTerrain, notEngaged, casterHasTag | 새 스킬은 `skills.json`에 효과 조합만 추가. 새 효과 종류만 코드 |

**스킬 (2b, `data/Rules/skills.json`)**: 간파, 복병, 화살비, 고무, 재정비, 돌격 함성, 도발. skills.md 1차의 **화계는 바람·불 번짐·지형 변화와 함께 2d**에서 넣는다.

### 5.4 2a에서 단순화한 것 (이후 단계)

- 동시에 싸우는 수는 소부대 위치가 아니라 **접전 여부**로 정한다. 진형 특성치(정면 폭·돌파력·측면 방어 …, [formations.md](../design/content/formations.md) 2장)는 수치가 정해지면 보정 출처로 추가한다.
- 진형 변경은 즉시 (전환 시간·전환 중 응집력 저하는 특성치와 함께).
- 측면·배후는 두 군단 중심의 방향으로 판정한다. 진군(`advance`)의 역할별 배치(선봉·측면·후위)는 아직 없다.
- 사령관 AI(작전 방침·역할 배분·협공), 어려움·지장 난이도, 위임 판단 수준 차등은 AI 2차 이후 (ai.md 10장).
- 전투 중 일기토 신청(`challenge`, 3차 스킬)은 아직 없다. 대결은 전투 직전에만.
- 포로·바람·날씨·화계·지형 변화(2d)는 아직 없다.
- 시야는 소부대 원형 범위로 계산한다 (지형이 시선을 가리는 계산은 없음, 숲은 "가까이 가야 보임"으로만).
- 명령 예약(경유), 협공 대기, "도착 후 사용" 선택은 아직 없다 (사거리 밖 스킬은 자동으로 다가가 사용).

## 6. 아직 하지 않은 것

- 위 5.4 항목, 일기토·논쟁 엔진 → R-21~R-23, R-26
- 스킬·특성 데이터의 전체 정의 (지금은 id만 사용)
- 실제 외부 서비스 구현 (Unity·서버 쪽 어셈블리에서 등록)
