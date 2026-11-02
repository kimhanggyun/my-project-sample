# 코드 구조 설계 (Unity)

> 상태: 설계 중 (구현 전)
> 관련 문서: [battle-design.md](battle-design.md)

## 원칙

0. **수치는 실수(float).** 능력치 1~100, 소수점 허용. 화면 표시는 정수. **능력치 항목은 데이터로 정의**하고 코드는 이름으로 조회한다.

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
| Game.Campaign | 스텝 진행, 장수 상태, 포로·구출·등용 | 없음 |
| Game.Economy | 군량 등 자원, 앱 결제 (Google Play / App Store) | 결제 구현부만 있음 |
| Game.Auth | 로그인, 게스트, 계정 연동 | 플랫폼 로그인 구현부만 있음 |
| Game.Ads | 보상형 광고, 띠 배너, 광고 동의 | 광고 SDK 구현부만 있음 |
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
├─ Scenes/          Boot(스플래시·로그인), Title, Main(하단 탭), Battle, Result
└─ StreamingAssets/Data/
```

---

## 2. Game.Data

| 클래스 | 역할 |
|---|---|
| `StatDefinition` | 능력치 정의: id, 표시 이름, 최소·최대, 기본값 (`stats.json`) |
| `StatBlock` | 이름-값 목록으로 된 능력치 묶음. 장수·소부대·병종 공통 |
| `GeneralData` | 장수 능력치(`StatBlock`: 통솔·무력·지략 …), 성향, 성장률, 스킬 목록, 특성 태그, 숙련 진형 |
| `SkillData` | 스킬: 요구 능력치, 비용, 재사용 대기, 대상, 효과 목록 |
| `InfluenceRuleData` | 장수 능력치 → 소부대 능력치 영향 계수 |
| `TroopTypeData` | 병종: 단계, 상위 병종, 승급 조건, 태그, 기본 능력치(`StatBlock`) |
| `TroopMatchupData` | 태그 간 상성 배율 |
| `FormationData` | 진형: 6개 슬롯 상대 위치, 특성치(정면 폭·돌파력·측면 방어·이동 속도·응집력·전환 시간) |
| `FormationMatchupData` | 진형 상성표와 배율 |
| `TraitData` | 장수 특성 태그별 보정치 |
| `TerrainData` | 지형 부품: 이동속도, 방어 보너스, 통과 가능 |
| `MapData` | 맵: 크기, 지형 배치, 출현 지점(P1, E1 …), 맵별 군단 수(선택) |
| `BalanceData` | 공통 수치: 데미지 공식 상수, 퇴각 기준, 일기토 조건, 화면 병사 상한 |
| `LevelTableData` | 레벨별 필요 경험치, 레벨 상한 |
| `StepData` | 스텝별 전투 설정 (전투명, 세력, 맵, 군단, 이벤트, 보상, 덮어쓰기) |
| `DialogueData` | 대사 목록 |
| `BalanceResolver` | `BalanceData` + `StepData.overrides` → 그 스텝에 적용할 최종 수치 |
| `CorpsLimitResolver` | 군단 수 결정: 스텝 값 → 맵 값 → 기본값(3), 출현 지점 수로 상한 |

---

## 3. Game.Battle.Model

### 3.1 상태 객체

| 클래스 | 역할 |
|---|---|
| `BattleState` | 전투 전체 상태 (시간, 양측, 전장) |
| `Side` | 진영: 대장, 군단 목록 |
| `Corps` | 군단: 장수, 총병력, 예비 병력, 사기, 진형, 군단 명령, 명령 예약(`CorpsOrderQueue`) |
| `CorpsOrderQueue` | 순서대로 실행할 명령 목록 (예: 이동 → 스킬) |
| `SubUnit` | 소부대: 슬롯(`SlotPosition`), 병종, 능력치(`SubUnitStats`), 현재 행동, 화면 병사 목록 |
| `SubUnitStats` | 소부대 최종 능력치. `StatBlock` 기반이라 항목이 늘어나도 클래스 수정 없음 |
| `StatModifier` | 보정 1개: 출처, 대상 능력치, 더하기/곱하기 값 |
| `CommandAuthority` | 장수 능력치 → 규율·용맹 계산, 지휘 범위, 반응 지연, 독자 행동 확률 |
| `StatModifierPipeline` | 기본 능력치에 `StatModifier` 목록(장수 영향·진형·명령·특성·스킬)을 순서대로 적용 |
| `Soldier` | 화면 병사 1명: 위치, 목표, 전열/후열 위치. **체력 없음** (전투는 병력 숫자로 계산) |
| `TroopRepresentation` | 병력 ↔ 화면 병사 비율(N), 손실 누적에 따른 병사 쓰러짐 처리 |
| `BattleGeneral` | 전투 중 장수: 레벨 반영된 최종 능력치, 상태(정상·퇴각·부상·포로) |
| `Battlefield` | 지형 격자 조회 (이동 비용, 방어 보너스, 시야 보정) |
| `VisibilityGrid` | 진영별 격자 칸의 상태: 미탐색 / 탐색됨 / 보임 |
| `SideKnowledge` | 진영이 알고 있는 적 목록과 마지막 목격 위치 (아군 전체 공유, AI 판단에 사용) |
| `SlotPosition` (enum) | Vanguard, Center, LeftWing, RightWing, LeftGuard, RightGuard |

### 3.2 진행

| 클래스 | 역할 |
|---|---|
| `BattleSimulation` | 고정 간격 틱(`Tick(dt)`)으로 시스템들을 순서대로 실행 |
| `BattleClock` | 일시정지, 배속(1~3배), 제한 시간 |
| `BattleContext` | 시스템들이 공유하는 상태·규칙·난수·이벤트 버스 |
| `BattleEventBus` | Model → View 알림 (교전 시작, 병사 사망, 군단 퇴각, 대사 …) |
| `BattleFactory` | `StepData`(적군·맵) + 플레이어 편성 → `BattleSetup` → `BattleState` 생성 |

### 3.3 시스템 (각각 `IBattleSystem` 구현)

```csharp
public interface IBattleSystem { void Tick(BattleContext ctx, float dt); }
```

| 시스템 | 책임 |
|---|---|
| `CommandSystem` | 군단의 명령 예약(`CorpsOrderQueue`)을 순서대로 실행 |
| `FormationSystem` | 장수 위치·방향 기준으로 소부대 진형 자리 계산, 진형 변경·회전, 좁은 길에서 종대 전환 |
| `SubUnitAISystem` | 소부대마다 행동 후보 점수를 매겨 가장 높은 행동 선택 (능력치 기반 자율 판단) |
| `TargetingSystem` | 선택된 행동에 따라 병사 단위 공격 대상 선정 |
| `CorpsNavigator` | 장수 기준 군단 경로 탐색 (지형 비용, 다른 군단은 움직이는 장애물) |
| `MovementSystem` | 소부대는 진형 자리를, 병사는 소부대를 따라 이동. 병사 간 밀어내기 회피 |
| `CombatSystem` | 교전 판정, 병력 숫자에 피해 적용 (`DamageCalculator` 사용) |
| `ReinforcementSystem` | 사망한 화면 병사를 예비 병력에서 보충 |
| `VisionSystem` | 소부대별 시야 계산 → 진영별 보임 격자(아군 전체 공유), 마지막 목격 위치 갱신. 일정 주기로 실행 |
| `RangedAttackSystem` | 사거리(능력치 + 보정) 안의 보이는 적에게 원거리 공격, 곡사, 오사 옵션 |
| `DetectionSystem` | 은신 부대 탐지 판정 (탐지 능력치·거리) |
| `MoraleSystem` | 사기 증감 (손실, 기습, 문관 피격, 일기토, 주변 퇴각·포로). 사기 보정은 `MoraleModifier`(가산)로 피해 계산에 반영 |
| `SupplySystem` | 군단 군량 소비(병력 × 병종 하루 소비량 × 경과 일수, 1분 = 7일, 게임 시간 기준), 바닥 시 사기 하락 → 퇴각, 종료 시 포획 가능 군량 집계 (궤멸·포로 군단 전량, 퇴각 군단은 생존 병력 비율만큼 제외) |
| `RetreatSystem` | 퇴각 판정(사기·병력 기준 + 장수 성향, 후퇴 명령), 퇴각 이동, 전장 이탈 처리 |
| `CaptureSystem` | 장수 포위 판정(방향 비율·주변 아군 병력), 포로 확률 누적, 무력 기반 탈출, 포로 시 군단 궤멸 |
| `SkillSystem` | 스킬 사용 조건 확인, 비용·대기시간 관리, 효과 실행 (위임 시 자동 사용) |
| `InfluenceSystem` | 장수 능력치를 영향표·지휘 범위에 따라 소부대 보정으로 변환 |
| `AISystem` | 적 군단과 위임 상태의 아군 군단에 **군단 명령**을 내림 |
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
| `ICorpsOrder` | MoveOrder(지점 이동), AttackOrder(대상 적 군단), SkillOrder(스킬 사용), ApproachThenSkill(접근 후 사용) | 입력에서 생성 |
| `ISkillTargeting` | Self, AllyCorps, EnemyCorps, Point, Area, Direction | `skills.json`의 `target.type` |
| `IDamageModifier` | 병종 상성, 측면·배후, 지형, 진형 상성, 진형 돌파력, 용맹, 사기 | `balance.json`에서 on/off·보정값(%p) |
| `ICorpsAI` | Aggressive, Defensive, DefendWall, Delegate | `"ai"` |
| `IVisionModifier` | 병종, 장수, 지형(언덕·숲), 진형·명령, 날씨 | 보정 목록으로 시야·탐지에 적용 |
| `ISubUnitBehavior` | HoldFormation(진형 위치 유지), EngageNearest(반경 내 최근접 처리), EngageFavorable(유리한 상대), FillGap(빈 곳 메우기), SupportAlly(아군 지원), Flank(측면·배후), Retreat | 행동 목록·가중치는 `balance.json` |
| `IVictoryCondition` | DefeatCommander, CaptureCastle, Survive, TimeOver, SupplyExhausted(군량 소진), HoldZone(구역 점령), DestroyStructure(수용소 파괴) | `"victory"`, `"defeat"` |
| `ISkillEffect` | Damage, Buff, Debuff, Morale, Disrupt(진형 혼란), Move, Stealth(매복), SurpriseAttack(기습) … | `skills.json`의 `effects[].type` |
| `ISkillCondition` | StatAtLeast(지략 ≥ 70), LevelAtLeast, TroopTag … | `skills.json`의 `requires` |
| `IBattleTypeRules` | FieldRules(야전), SiegeRules(성새전), NavalRules(수상전) | `"battleType"` |

`DamageCalculator`는 등록된 `IDamageModifier`들의 보정값(%p)을 **더한 뒤** 상·하한을 적용하고, 기본 피해에 한 번만 곱한다 (가산 방식). 새 보정 요소는 Modifier 하나를 추가하면 된다. 결과와 함께 보정 내역(어떤 요소가 몇 %p)을 반환해 조정 패널과 시뮬레이터에서 볼 수 있게 한다.

### 3.5 소부대 자율 판단 구조

```csharp
public interface ISubUnitBehavior {
    string Id { get; }
    float Score(SubUnit unit, BattleContext ctx);   // 지금 이 행동이 얼마나 적절한가
    void Execute(SubUnit unit, BattleContext ctx, float dt);
}
```

- `SubUnitAISystem`이 매 판단 주기마다 각 행동의 `Score`를 계산하고 가장 높은 행동을 실행한다.
- 점수에는 소부대 능력치가 반영된다. 예: `FillGap`은 판단력이 낮으면 점수가 거의 0이라 기본 부대는 고르지 않는다.
- 군단 명령은 `SubUnitStats`에 보정을 거는 방식으로만 영향을 준다. 그래서 명령과 행동이 서로 독립적이다.
- 새 행동은 `ISubUnitBehavior` 구현 클래스를 추가하고 등록하면 된다.

### 3.6 입구와 출구

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
| `TroopProgress` | 세이브되는 소부대 성장 상태: 병종, 숙련도 |
| `PromotionService` | 승급 조건 확인 → 상위 병종으로 승급 |
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

## 4.5 Game.Campaign (전투 밖 진행 상태)

전투 결과를 받아 스토리 진행·장수 상태를 관리한다. 전투 모듈은 결과만 넘기고, 이후 처리는 여기서 한다.

| 클래스 | 역할 |
|---|---|
| `CampaignState` | 스텝 해금 상태, 장수 상태 목록, 열려 있는 구출 전투 |
| `GeneralStatus` | 장수 상태: 출전 가능 / 포로(수용소 id) / 등용됨 |
| `PrisonCampData` | 수용소: id, 이름, 소속 세력, 구출 전투 데이터(맵·수비 병력) |
| `CaptivityService` | 아군 장수 포로 처리 → 수용소 배정, 구출 기한(기본 30일) 설정, 구출 전투 생성, 구출 성공 또는 기한 경과(자동 석방) 시 복귀 |
| `PrisonerDecisionService` | 적 장수 포로 처리: 등용(성공률 계산, 실패 시 감금 유지·재시도 대기시간) / 해방(보상 지급) |
| `ITimeProvider` | 현재 시각 조회 한 곳 (기기 시계 → 나중에 서버 시간으로 교체) |
| `CaptivityTimerService` | 구출 기한, 등용 재시도 대기시간 계산 |
| `IRecruitRule` | 등용 가능 여부(`recruitable`)와 성공률 계산 규칙 (성향·충성 등). 규칙 추가로 확장 |
| `RosterService` | 출전 가능한 장수 목록 (포로 제외, 등용 장수 포함) |
| `StartingKitService` | 새 게임 시 `startingKit.json`대로 장수 3명·군단·진형·스킬·군량·기본 편성 지급 |
| `FormationPresetStore` | 상황별 편성(야전·공성·공격·수비) 저장·조회 |
| `UnlockService` | 배운 진형·스킬 목록 (초기 지급 + 스토리 보상 + 나중에 키우기) |

## 4.6 Game.Economy (자원·결제)

| 클래스 | 역할 |
|---|---|
| `Wallet` | 보유 자원 (군량 등). 자원 종류는 데이터로 정의해 추가 가능 |
| `SupplyService` | 출진 군량 자동 계산(기본 3분 분량)·증감, 차감, 전투 후 남은 군량 반환, 적 군량 포획분 지급, 패키지 보유 시 시간 회복 |
| `IStoreService` | 앱 결제 추상화. 구현: `UnityIapStoreService` (Google Play + App Store). 테스트용 `FakeStoreService` |
| `IReceiptValidator` | 결제 영수증 검증. 처음에는 기기 검증, 나중에 서버 검증으로 교체 |

- 결제 상품 목록은 데이터(`products.json`)로 둔다.
- 결제 연동과 서버 검증은 추후 단계. 지금은 인터페이스 자리만 둔다.

## 4.7 Game.Auth (로그인)

| 클래스 | 역할 |
|---|---|
| `IAuthService` | 로그인 추상화: 자동 로그인, 게스트 로그인, 계정 연동, 로그아웃 |
| `LocalGuestAuthService` | 서버 없는 동안 쓰는 기기 게스트 계정 (Step 1) |
| `UgsAuthService` | 서버 도입 후: Unity Gaming Services 인증 (게스트 + 연동) |
| `IPlatformSignIn` | 로그인 수단. 출시 구현: `GooglePlayGamesSignIn`, `GameCenterSignIn`, `AppleSignIn`. 나중에 `KakaoSignIn` 등을 추가 |
| `SignInProviderRegistry` | 플랫폼별로 보여줄 로그인 버튼 목록 (데이터). 새 로그인 수단은 구현 추가 + 목록 등록 |
| `AccountLinkService` | 게스트 계정에 구글/Apple 계정 연동 |

## 4.8 Game.Ads (광고)

| 클래스 | 역할 |
|---|---|
| `IAdService` | 광고 추상화: 초기화, 동의 상태 반영 |
| `IRewardedAdService` | 보상형 광고 보기 → 끝까지 본 경우만 보상 콜백. 구현: LevelPlay / AdMob, 테스트용 `FakeRewardedAd` |
| `IBannerAdService` | 띠 배너 표시·숨김, 배너 높이 알려주기 (화면 배치 조정용) |
| `AdRewardService` | 보상 지급(군량), 하루 최대 횟수, 재시청 대기시간 관리 (`ITimeProvider` 사용) |
| `AdPlacementData` | 광고 위치·보상량·횟수 제한 데이터 (`ads.json`) |
| `AdConsentService` | 아이폰 추적 허용 요청(ATT), 지역별 광고 동의 |

- 서버 도입 후에는 광고 보상 **서버 검증**(광고망이 서버로 보상 완료를 알려주는 방식)으로 바꿔 부정 보상을 막는다.
- 광고 제거 구매 시 배너를 끈다. 배너 표시 규칙·광고 제거 상품은 [ads.md](ads.md) 참고.

| 추가 클래스 | 역할 |
|---|---|
| `BannerVisibilityController` | 현재 화면·광고 제거 상태로 배너 표시 결정 (탭 화면에서만 노출) |
| `AdRemovalEntitlement` | 광고 제거 권한: 영구 / 만료 시각 (30일 이용권, 중복 시 기간 연장) |
| `PurchaseHandler` | 구매 완료 → `products.json` 효과 적용, 구매 복원 |

## 5. Game.Core

| 클래스 | 역할 |
|---|---|
| `GameBootstrap` | 최초 실행: 서비스 생성, 데이터 로딩 후 `StartupFlow` 실행 |
| `StartupFlow` | 앱 시작 순서: 스플래시 → 버전 확인(`IVersionCheck`, 서버 전까지 건너뜀) → 로그인 → 약관 동의 → 타이틀 → 메인 |
| `ConsentService` | 첫 실행 약관 동의 기록 |
| `GameServices` | 서비스 묶음 (간단한 의존성 주입 지점) |
| `IDataRepository` / `JsonDataRepository` | StreamingAssets JSON 로딩·캐시 |
| `ISaveService` / `JsonSaveService` | 세이브 슬롯 저장·불러오기 |
| `SaveData` | 현재 스텝, 장수별 `GeneralProgress`, `CampaignState`(장수 상태·포로·구출 전투), 플레이 기록 |
| `SettingsService` | 사운드, 배속 기본값, 언어 |
| `SceneFlow` | 씬 전환 (타이틀 → 메인 → 전투 → 결과 → 메인) |
| `StepFlow` | 스텝 진행 순서 관리. 나중에 전략 화면이 이 자리를 대신하거나 감싼다 |

---

## 6. Game.Battle.View / Game.UI

| 클래스 | 역할 |
|---|---|
| `BattleController` | Model과 View 연결: 틱 구동, 입력 → 명령, 이벤트 → 연출 |
| `CorpsView`, `SoldierView` | 3D 모델 표시, 애니메이션 |
| `SoldierPool` | 병사 오브젝트 재사용 (성능) |
| `QualityBudget` | 기기 품질 단계별 화면 병사 예산, 첫 몇 초 프레임 측정으로 자동 조정 |
| `SoldierLodRenderer` | 거리별 단순 모델, GPU 인스턴싱으로 대량 병사 표시 |
| `DebugTuningPanel` | (개발 빌드 전용) 전투 중 보정값 실시간 변경 |
| `BattleCameraController` | 드래그 이동, 핀치 확대, 연출 클로즈업 |
| `BattleInputController` | 터치 입력을 받아 터치·드래그·핀치로 구분 (`GestureRecognizer`) |
| `CorpsPicker` | 터치 지점에서 군단 찾기. 부대원 위치를 감싼 영역 + 여유 반경으로 판정, 겹치면 우선순위 적용. 병사마다 충돌체를 두지 않고 Model의 위치 데이터로 계산해 성능 확보 |
| `SelectionService` | 선택된 아군·적 군단(과 강조할 소부대)을 한 곳에서 관리. 변경 이벤트를 HUD·카드·3D 표시가 함께 구독 |
| `SelectionHighlightView` | 선택된 군단 부대원 발밑 링 표시 |
| `SoldierStatusVisuals` | 사기·버프·디버프·은신 상태를 병사 몸 색·테두리(셰이더 값)와 자세·동작으로 표시 |
| `OrderInputController` | 선택 상태에서 터치를 명령으로 변환: 적 → 공격, 빈 땅 → 이동 |
| `SkillTargetingController` | 스킬 지정 모드: 사거리·범위 미리보기, 대상 검증, 지점 스킬의 "이동만 / 도착 후 사용" 선택 |
| `OrderMarkerView` | 예약된 명령의 경로선과 목적지 표식 |
| `MinimapView` | 탭하면 펼치는 미니맵 (안개 반영) |
| `FogOfWarView` | 보임 격자를 텍스처로 그려 3단계(미탐색·탐색됨·보임) 표시 |
| `LastKnownMarkerView` | 안 보이게 된 적의 마지막 목격 위치 흐린 표시 |
| `BattleHUD` | 전투명, 군단 선택, 사기·예비 병력 게이지 |
| `CommandPanel`, `FormationPanel` | 명령 6개 + 위임, 진형 선택 |
| `SubUnitCardBar` | 소부대 6개 카드 |
| `SpeedControl` | 일시정지, 배속 |
| `DialoguePopup` | 초상화 + 대사 |
| `PrisonerScreen` | 결과 화면 다음, 잡은 적 장수별 등용·해방 선택 |
| `TitleScreen`, `SettingsScreen`, `ResultScreen` | 각 화면 (설정은 상단 톱니바퀴에서 여는 팝업) |
| `MainScreen` | 상단 바(군량·충전·설정) + 띠 배너 자리 + 탭 화면 영역 + 하단 탭 바. 시작 시 탭 목록의 첫 번째 탭을 연다 |
| `MainTabRegistry` | `mainTabs.json`을 읽어 탭 목록·순서·상태(사용/TO-BE/숨김)·해금 조건 관리 |
| `IMainTab` | 탭 화면 모듈 인터페이스. 구현: `StoryTab`·`FormationTab`(이번 범위), `GeneralGrowthTab`·`TroopGrowthTab`(TO-BE), `ComingSoonTab` |
| `FormationTab` | 보유 장수 → 군단 슬롯 배치, 소부대 병종, 진형 선택, 상황별 편성 저장 |
| `SortiePrepScreen` | 출진 준비: 전장 유형에 맞는 편성 자동 선택·변경, 군량 조정 |
| `StoryTab` | 스텝 목록, 해금 상태, 스텝 선택 → 대사 → 전투 |
| `BadgeService` | 탭 알림 표시(빨간 점) |

---

## 6.5 개발용 도구 (서버 없이 확인)

레벨링·세이브·시간 규칙은 서버 없이 기기 안에서 계산·저장한다. 서버는 나중에 `ISaveService`·`ITimeProvider` 구현만 바꿔 붙인다.

| 도구 | 역할 |
|---|---|
| `JsonSaveService` | 세이브를 기기에 JSON 파일로 저장 (웹 시안은 브라우저 저장소). 사람이 읽을 수 있는 형식 |
| `DevMenu` (개발 빌드 전용) | 경험치 지급, 레벨 직접 설정, 스텝 해금, 군량 지급, 세이브 초기화 |
| 세이브 내보내기·불러오기 | 세이브를 텍스트로 복사·붙여넣기. "스텝 5 진행 상태" 같은 테스트용 세이브를 저장해 두고 재사용 |
| `DebugTimeProvider` | 시간 빨리 감기 (구출 기한 30일, 등용 대기 3시간 등을 바로 확인) |
| 성장 곡선 시뮬레이션 | 스텝 1부터 N까지 자동으로 진행시켜 스텝별 아군 레벨과 권장 레벨을 표로 비교 |

## 7. 확장 시나리오별 작업 범위

| 추가하고 싶은 것 | 해야 할 일 | 전투 코어 수정 |
|---|---|---|
| 새 스텝 | `Steps/stepNN.json` + 대사 파일 | 없음 |
| 새 장수 | `generals.json`에 항목 추가 | 없음 |
| 새 진형 | `formations.json`에 항목 추가 | 없음 |
| 새 메인 탭 | `IMainTab` 구현 + `mainTabs.json` 등록 | 없음 |
| 새 능력치 | `stats.json` 추가 + 사용하는 규칙 추가 | 없음 |
| 새 스킬 | `skills.json` 추가 (새 효과 종류면 `ISkillEffect` 구현) | 없음 |
| 새 병종·특화 | `troopTypes.json` 추가 (태그로 상성 자동 적용) | 없음 |
| 새 명령 | `ICorpsCommand` 구현 + 등록 + 버튼 | 없음 |
| 새 소부대 행동 | `ISubUnitBehavior` 구현 + 등록 | 없음 |
| 새 승리 조건 | `IVictoryCondition` 구현 + 등록 | 없음 |
| 성새전·수상전 | `IBattleTypeRules` 구현 + 지형 부품 + 맵 | 없음 |
| 전략(국가 경영) 화면 | 새 모듈이 `BattleSetup` 생성, `BattleResult` 반영 | 없음 |

---

## 8. 테스트 계획

### 밸런스 시뮬레이터 (`Game.Tools.BalanceSim`)
- Model만 사용해 화면 없이 전투를 빠르게 실행하는 도구.
- 입력: 스텝 JSON 또는 조건 조합. 출력: 승률, 평균 손실, 평균 전투 시간, 보정 요소별 기여도.
- 수치 변경 시 자동 실행해 이전 결과와 비교한다.

### 단위 테스트

- `StatCalculator`, `ExpCalculator`, `LevelUpService`: 레벨·경험치 계산
- `DamageCalculator`: 보정 가산, 상·하한, 보정 내역
- `TroopRepresentation`: 비율 계산, 손실 누적 시 병사 쓰러짐
- `MoraleSystem`, `RetreatSystem`: 기준값에서 퇴각 발생, 성향별 퇴각 시점
- `CaptureSystem`: 포위 비율 경계값, 무력에 따른 탈출 확률
- `FormationSystem`: 좁은 길 종대 전환과 복귀
- `BalanceResolver`: 스텝 덮어쓰기 병합
- `BattleSimulation`: 고정 시드로 같은 입력 → 같은 결과
- **진형 상성 회귀 테스트**: 같은 능력치·병력으로 진형 36개 조합을 시뮬레이션해 상성표대로 승패가 나오는지 확인
- `CommandAuthority`: 규율·용맹 값에 따른 반응 지연·독자 행동 확률
- `CorpsPicker`: 겹친 군단, 여유 반경 경계, 같은 자리 재터치 순환
- `VisionSystem`: 지형·병종 보정, 진영 공유, 은신 탐지
- `RangedAttackSystem`: 사거리 보정 합산, 안 보이는 적 제외
- `CorpsOrderQueue`: 이동 → 스킬 순서 실행, 사거리 밖 자동 접근, 대상 소실 시 처리
