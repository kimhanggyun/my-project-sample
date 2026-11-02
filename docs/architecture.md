# 코드 구조 설계 (Unity)

> 상태: 설계 중 (구현 전)
> 관련 문서: [battle-design.md](battle-design.md)

## 원칙

1. **시스템 하나 = 클래스 하나.** 각 시스템은 자기 책임만 가진다 (사기, 보충, 진형 …).
2. **규칙과 화면 분리.** 전투 규칙(Model)은 Unity에 의존하지 않는 순수 C#. 화면(View)은 Model의 상태와 이벤트를 받아 그리기만 한다.
3. **데이터 주도.** 숫자·이름·종류는 JSON. 코드는 규칙만 가진다.
4. **확장은 "추가"로.** 새 명령·진형·승리 조건·전장 유형은 기존 코드를 고치지 않고 클래스나 데이터를 추가해 붙인다 (인터페이스 + 등록표).
5. **테스트 가능.** Model은 고정 시드 난수로 결정적으로 돌아가며, Unity 에디터 없이 단위 테스트한다.

---

## 1. 어셈블리(모듈) 구성

의존 방향은 위에서 아래로만 흐른다.

```
Game.UI ─────────┐
Game.Battle.View ┼─▶ Game.Battle.Model ─▶ Game.Data ─▶ (없음)
Game.Core ───────┘          ▲
Game.Tests ─────────────────┘
```

| 어셈블리 | 내용 | Unity 의존 |
|---|---|---|
| Game.Data | JSON과 1:1 데이터 클래스, 덮어쓰기 병합 | 없음 |
| Game.Battle.Model | 전투 규칙, 시스템, AI, 결과 계산 | **없음** |
| Game.Progression | 경험치·레벨·능력치 계산 | 없음 |
| Game.Core | 부트, 씬 전환, 세이브, 설정, 데이터 로딩 | 있음 |
| Game.Battle.View | 3D 표시, 카메라, 병사 풀링, 입력 | 있음 |
| Game.UI | 타이틀, 설정, HUD, 결과 화면 | 있음 |
| Game.Tests | Model·Progression 단위 테스트 | 테스트 프레임워크 |

```
Assets/
├─ Scripts/
│   ├─ Data/
│   ├─ Progression/
│   ├─ Battle/Model/
│   ├─ Battle/View/
│   ├─ Core/
│   └─ UI/
├─ Tests/
├─ Scenes/          Boot, Title, Story, Battle, Result
└─ StreamingAssets/Data/
```

---

## 2. Game.Data

| 클래스 | 역할 |
|---|---|
| `GeneralData` | 장수 기본 능력치(무력·통솔·지력), 성향, 성장률, 자동 전술 id |
| `TroopTypeData` | 병종: 속도, 상성 배율 |
| `FormationData` | 진형: 6개 슬롯 상대 위치, 공격·방어·속도 보정 |
| `TerrainData` | 지형 부품: 이동속도, 방어 보너스, 통과 가능 |
| `MapData` | 맵: 크기, 지형 배치, 출현 지점(P1, E1 …) |
| `BalanceData` | 공통 수치: 데미지 공식 상수, 퇴각 기준, 일기토 조건, 화면 병사 상한 |
| `LevelTableData` | 레벨별 필요 경험치, 레벨 상한 |
| `StepData` | 스텝별 전투 설정 (전투명, 세력, 맵, 군단, 이벤트, 보상, 덮어쓰기) |
| `DialogueData` | 대사 목록 |
| `BalanceResolver` | `BalanceData` + `StepData.overrides` → 그 스텝에 적용할 최종 수치 |

---

## 3. Game.Battle.Model

### 3.1 상태 객체

| 클래스 | 역할 |
|---|---|
| `BattleState` | 전투 전체 상태 (시간, 양측, 전장) |
| `Side` | 진영: 대장, 군단 목록 |
| `Corps` | 군단: 장수, 총병력, 예비 병력, 사기, 진형, 현재 명령 |
| `SubUnit` | 소부대: 슬롯(`SlotPosition`), 병종, 화면 병사 목록 |
| `Soldier` | 화면 병사 1명: 위치, 체력, 목표 |
| `BattleGeneral` | 전투 중 장수: 레벨 반영된 최종 능력치, 상태(정상·퇴각·부상·포로) |
| `Battlefield` | 지형 격자 조회 (이동 비용, 방어 보너스) |
| `SlotPosition` (enum) | Vanguard, Center, LeftWing, RightWing, LeftGuard, RightGuard |

### 3.2 진행

| 클래스 | 역할 |
|---|---|
| `BattleSimulation` | 고정 간격 틱(`Tick(dt)`)으로 시스템들을 순서대로 실행 |
| `BattleClock` | 일시정지, 배속(1~3배), 제한 시간 |
| `BattleContext` | 시스템들이 공유하는 상태·규칙·난수·이벤트 버스 |
| `BattleEventBus` | Model → View 알림 (교전 시작, 병사 사망, 군단 퇴각, 대사 …) |
| `BattleFactory` | `StepData`(또는 전략 화면) → `BattleSetup` → `BattleState` 생성 |

### 3.3 시스템 (각각 `IBattleSystem` 구현)

```csharp
public interface IBattleSystem { void Tick(BattleContext ctx, float dt); }
```

| 시스템 | 책임 |
|---|---|
| `CommandSystem` | 군단에 내려진 명령을 실행 상태로 반영 |
| `FormationSystem` | 진형에 따라 소부대 목표 위치 계산, 진형 변경 |
| `TargetingSystem` | 병사·소부대의 공격 대상 선정 |
| `MovementSystem` | 지형 비용을 반영한 이동 |
| `CombatSystem` | 교전 판정, 피해 적용 (`DamageCalculator` 사용) |
| `ReinforcementSystem` | 사망한 화면 병사를 예비 병력에서 보충 |
| `MoraleSystem` | 손실·기습·문관 피격·일기토 결과로 사기 증감 |
| `RetreatSystem` | 사기·예비 병력 기준과 장수 성향으로 자동 퇴각 판정 |
| `TacticSystem` | 책사 자동 전술(버프) 발동 |
| `AISystem` | 적 군단과 위임 상태의 아군 군단 지휘 |
| `EventTriggerSystem` | 스텝 JSON의 이벤트(대사, 증원) 조건 감시 |
| `VictorySystem` | 승리·패배 조건 판정 → 종료 |

전투 시작 전 단계:

| 클래스 | 책임 |
|---|---|
| `DuelResolver` | 일기토 발생 판정과 결과 (야전 시작 직전) |

### 3.4 확장 지점 (인터페이스 + 등록표)

JSON에 적힌 문자열 id로 구현 클래스를 찾는다. 새 종류는 클래스를 만들고 등록만 하면 된다.

| 인터페이스 | 구현 예 | JSON 키 |
|---|---|---|
| `ICorpsCommand` | Charge, Attack, HoldFormation, Wall, Defend, Retreat | 명령 id |
| `IDamageModifier` | 병종 상성, 측면·배후, 지형, 진형, 사기 | `balance.json`에서 on/off·배율 |
| `ICorpsAI` | Aggressive, Defensive, DefendWall, Delegate | `"ai"` |
| `IVictoryCondition` | DefeatCommander, CaptureCastle, Survive, TimeOver | `"victory"`, `"defeat"` |
| `ITacticSkill` | 사기 회복, 공격 버프 … | 장수의 자동 전술 id |
| `IBattleTypeRules` | FieldRules(야전), SiegeRules(성새전), NavalRules(수상전) | `"battleType"` |

`DamageCalculator`는 등록된 `IDamageModifier`들을 순서대로 곱해 최종 피해를 만든다. 새 보정 요소는 Modifier 하나를 추가하면 된다.

### 3.5 입구와 출구

| 클래스 | 역할 |
|---|---|
| `BattleSetup` | 전투 시작에 필요한 모든 값. 지금은 스텝 JSON에서, 나중에는 전략 화면에서 만든다 |
| `BattleResult` | 승패, 군단별 남은 병력, 장수 상태, 공적(격파 수·생존·승리 기여) |
| `BattleResultBuilder` | 종료 시점의 `BattleState` → `BattleResult` |

---

## 4. Game.Progression (스텝별 레벨링)

플레이어 장수는 스텝을 거치며 성장하고, 적은 스텝 JSON에서 레벨을 지정한다. **양쪽 모두 같은 계산기**를 써서 밸런스를 일관되게 맞춘다.

| 클래스 | 역할 |
|---|---|
| `GeneralProgress` | 세이브되는 장수 성장 상태: 레벨, 경험치 |
| `StatCalculator` | 기본 능력치 + 성장률 × 레벨 (+ 스텝 배율) → 최종 능력치 |
| `ExpCalculator` | `BattleResult`의 공적 + 스텝 보상 배율 → 장수별 획득 경험치 |
| `LevelUpService` | 경험치 반영, 레벨업 처리, 레벨 상한 적용, 레벨업 내역 반환 |
| `ProgressionReport` | 결과 화면에 보여줄 장수별 경험치·레벨업·능력치 변화 |

스텝별로 설정할 수 있는 값 (`StepData`):

| 값 | 의미 |
|---|---|
| `corps[].level` | 적 군단 장수 레벨 |
| `enemyStatMultiplier` | 적 능력치 배율 |
| `rewards.exp`, `rewards.expMultiplier` | 기본 경험치, 배율 |
| `rewards.levelCap` | 이 스텝까지 오를 수 있는 레벨 상한 |
| `recommendedLevel` | 권장 레벨 (표시용) |

흐름:

```
StepData ─▶ BattleFactory ─(StatCalculator)─▶ 전투 ─▶ BattleResult
                                                        │
                       SaveData ◀─ LevelUpService ◀─ ExpCalculator
```

---

## 5. Game.Core

| 클래스 | 역할 |
|---|---|
| `GameBootstrap` | 최초 실행: 서비스 생성, 데이터 로딩, 타이틀로 이동 |
| `GameServices` | 서비스 묶음 (간단한 의존성 주입 지점) |
| `IDataRepository` / `JsonDataRepository` | StreamingAssets JSON 로딩·캐시 |
| `ISaveService` / `JsonSaveService` | 세이브 슬롯 저장·불러오기 |
| `SaveData` | 현재 스텝, 장수별 `GeneralProgress`, 플레이 기록 |
| `SettingsService` | 사운드, 배속 기본값, 언어 |
| `SceneFlow` | 씬 전환 (타이틀 → 스토리 → 전투 → 결과 → 다음 스텝) |
| `StepFlow` | 스텝 진행 순서 관리. 나중에 전략 화면이 이 자리를 대신하거나 감싼다 |

---

## 6. Game.Battle.View / Game.UI

| 클래스 | 역할 |
|---|---|
| `BattleController` | Model과 View 연결: 틱 구동, 입력 → 명령, 이벤트 → 연출 |
| `CorpsView`, `SoldierView` | 3D 모델 표시, 애니메이션 |
| `SoldierPool` | 병사 오브젝트 재사용 (성능) |
| `BattleCameraController` | 드래그 이동, 핀치 확대, 연출 클로즈업 |
| `MinimapView` | 탭하면 펼치는 미니맵 |
| `BattleHUD` | 전투명, 군단 선택, 사기·예비 병력 게이지 |
| `CommandPanel`, `FormationPanel` | 명령 6개 + 위임, 진형 선택 |
| `SubUnitCardBar` | 소부대 6개 카드 |
| `SpeedControl` | 일시정지, 배속 |
| `DialoguePopup` | 초상화 + 대사 |
| `TitleScreen`, `SettingsScreen`, `StoryScreen`, `ResultScreen` | 각 화면 |

---

## 7. 확장 시나리오별 작업 범위

| 추가하고 싶은 것 | 해야 할 일 | 전투 코어 수정 |
|---|---|---|
| 새 스텝 | `Steps/stepNN.json` + 대사 파일 | 없음 |
| 새 장수 | `generals.json`에 항목 추가 | 없음 |
| 새 진형 | `formations.json`에 항목 추가 | 없음 |
| 새 명령 | `ICorpsCommand` 구현 + 등록 + 버튼 | 없음 |
| 새 승리 조건 | `IVictoryCondition` 구현 + 등록 | 없음 |
| 성새전·수상전 | `IBattleTypeRules` 구현 + 지형 부품 + 맵 | 없음 |
| 전략(국가 경영) 화면 | 새 모듈이 `BattleSetup` 생성, `BattleResult` 반영 | 없음 |

---

## 8. 테스트 계획

- `StatCalculator`, `ExpCalculator`, `LevelUpService`: 레벨·경험치 계산
- `DamageCalculator`: 상성·방향·지형 배율
- `MoraleSystem`, `RetreatSystem`: 기준값에서 퇴각 발생
- `BalanceResolver`: 스텝 덮어쓰기 병합
- `BattleSimulation`: 고정 시드로 같은 입력 → 같은 결과
