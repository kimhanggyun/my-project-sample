# 검증 예시

> 상태: 설계 뼈대 · 누가 구현해도 **같은 규칙이면 같은 답**이 나오는지 확인하는 기준
> 관련: [structure.md](structure.md) 7장(검증 도구), [content/battle.md](content/battle.md) 외 각 컨텐츠 문서

---

## 0. 읽는 법

| 표시 | 종류 | 무엇을 확인하나 | 바뀌는 때 |
|---|---|---|---|
| **R-번호** | 규칙 예시 | **계산 방식·규칙** | 규칙을 바꿀 때만. 바꾸면 **설계 버전을 올린다** (예: `design-v1.1`) |
| **N-번호** | 수치 예시 | **예시 수치로 나온 결과** | 밸런스 조정 때마다. "예시 수치, 확정 아님" |

- 코드 테스트에서 **규칙 예시(R)는 현재 `balance.json` 값을 읽어 계산**한다 → 수치를 바꿔도 테스트가 깨지지 않고, 계산 방식이 틀렸을 때만 실패한다.
- **수치 예시(N)는 아래 "예시 수치"로 고정**해서 돌린다 → 수치를 확정하면 정답을 갱신한다.
- 첫 구현이 나오면 **기준 전투(정답 파일)**를 추가한다 (9장).

**예시 수치 (balance.json 확정 전)**

| 값 | 예시 |
|---|---|
| 보정 합계 상한·하한 | +100 / −60 (%p) |
| 진형 상성 (핵심 6종 / 상황 진형) | ±25 / ±15 (%p) |
| 사기 기준값 | 50 |
| 사기 공격·방어 가산 계수 | 능력치 × 0.25 (사기 100일 때 최대) |
| 사기 최저 감산 | −30 (%p, 사기 0) |
| 병력 한도 | 통솔 1 → 5,000, 통솔 100 → 20,000, 비례 |
| 병사 하루 군량 | 1 (특수 병종 2·3) |
| 전투 시간 환산 | 1분 = 7일 |
| 출진 군량 자동 계산 | 3분 분량 |
| 전투 손실 중 부상 비율 | 40% |
| 치료 비용 | 징병(또는 모병) 비용 × 0.5 |
| 고용 가격 상한 | 추천 가격 × 2 |
| 경상 능력치 | × 0.9 |

---

## 1. 피해 계산 ([battle.md](content/battle.md) 2.4.2)

**R-1 가산 → 상한 → 한 번 적용**
- 모든 보정(병종 상성, 진형 상성, 방향, 지형, 사기, 장수, 특성, 스킬, 날씨)을 **%p로 더한다**.
- 합계를 하한·상한으로 자른다.
- `최종 피해 = 기본 피해 × (100% + 잘린 합계)` — 곱하기는 **한 번만**.
- 꺼진 스위치의 보정은 0으로 본다.

**R-2 보정 내역**
- 계산 결과와 함께 "어떤 보정이 몇 %p였는지" 목록을 돌려준다. 목록의 합 = 잘리기 전 합계.

**N-1**
| 입력 | 값 |
|---|---|
| 기본 피해 | 100 |
| 병종 유리 / 측면 / 상대가 숲 | +20 / +15 / −10 |
| 합계 → 결과 | +25 → **125** |

**N-2 상한에 걸리는 경우**
| 입력 | 값 |
|---|---|
| 기본 피해 | 100 |
| 보정 | +40, +35, +30, +20 (합계 +125) |
| 잘린 합계 → 결과 | +100 → **200** (225가 아님) |

**N-3 하한**
| 보정 합계 −80 | 잘린 합계 −60 → 기본 100이면 **40** |
|---|---|

---

## 2. 사기 보정 ([battle.md](content/battle.md) 2.5)

**R-3**
- 사기 ≥ 기준(50): 공격 가산 = (사기 − 50) ÷ 50 × 계수 × **무력**, 방어 가산 = (사기 − 50) ÷ 50 × 계수 × **지략**.
- 사기 < 기준: 공격·방어 모두 감산 = (50 − 사기) ÷ 50 × 최저 감산.
- 사기 보정은 R-1의 가산 항목으로 들어간다.

**N-4**
| 사기 | 무력 | 지략 | 공격 가산 | 방어 가산 |
|---|---|---|---|---|
| 100 | 80 | 40 | **+20** | **+10** |
| 75 | 80 | 40 | +10 | +5 |
| 50 | 80 | 40 | 0 | 0 |
| 25 | (무관) | (무관) | −15 | −15 |
| 0 | (무관) | (무관) | −30 | −30 |

---

## 3. 병력 한도와 편성 ([battle.md](content/battle.md) 2.4.4, [formation.md](content/formation.md) 2장)

**R-4** `한도 = 최저 + (통솔 − 1) ÷ 99 × (최고 − 최저)`

**R-5** 군단 병력 = 소부대 칸에 넣은 부대 병력의 합 ≤ 장수 한도. 한 부대는 한 군단에만 들어간다.

**N-5**
| 통솔 | 한도 |
|---|---|
| 1 | 5,000 |
| 50 | **12,424** (약 12,400) |
| 100 | 20,000 |

**N-6** 통솔 50 장수(한도 12,424)에 3,000 + 3,000 + 3,000 + 2,000 + 1,500 = 12,500 → **넣을 수 없음** (76 초과).

---

## 4. 군량 ([battle.md](content/battle.md) 2.15, [economy.md](content/economy.md))

**R-6** 전투 중 소비 = Σ(부대 병력 × 병종 하루 소비량) × 7 × 경과 분.

**R-7** 출진 자동 계산 = 출전 병력(고용 포함) 기준 R-6 × 3분. 플레이어가 증감할 수 있다.

**R-8 퇴각한 적의 군량**: 가져감 = 남은 군량 × (퇴각 시 병력 ÷ 출전 병력), 몰수 = 남은 군량 − 가져감. 궤멸·포로 군단은 전량 몰수.

**N-7**
| 병력 | 하루 소비 | 분 | 소비 |
|---|---|---|---|
| 10,000 (보병) | 1 | 3 | **210,000** |
| 8,000 보병 + 2,000 철기(3) | | 1 | (8,000×1 + 2,000×3) × 7 = **98,000** |

**N-8** 출전 9,000 → 퇴각 시 3,000, 남은 군량 90,000 → 가져감 **30,000**, 몰수 **60,000**.

---

## 5. 손실·치료 ([battle.md](content/battle.md) 2.4.4)

**R-9** 손실 = 사망 + 부상. 부상 = 손실 × 부상 비율, 사망 = 나머지. 사망은 회복 불가.

**R-10** 치료 비용 = 그 수만큼 징병(군량) 또는 모병(돈)하는 비용 × 0.5. 플레이어가 자원을 고른다.

**N-9** 손실 1,000 → 부상 **400**, 사망 **600**. 징병 비용이 1명당 군량 10이면, 400명 치료 = **2,000 군량**.

---

## 6. 스킬 ([battle.md](content/battle.md) 2.8, [skills.md](content/skills.md))

**R-11** 시작 책략 점수 = 지략. 사용 가능 = (점수 ≥ 비용) **그리고** (남은 횟수 ≥ 1) **그리고** 발동 조건 충족.

**R-12** 사용하면 점수와 횟수가 줄어든다. **실패·취소해도** 돌려주지 않는다.

**R-13** 점수 회복(`skillPointRegen` 켜짐일 때)은 사기 ≥ 기준일 때만, 시작 점수(지략)를 넘지 않는다.

**N-10** 지략 80 장수: 화계(40, 1회) → 점수 40 남음, 화계 남은 횟수 0 → 화계 **다시 못 씀**. 간파(15, 2회) 두 번 → 점수 10 남음.

---

## 7. 상성표 규칙 ([formations.md](content/formations.md) 4장, [troops.md](content/troops.md) 4장)

**R-14 대칭**: A가 B에 유리(○)이면 B는 A에 불리(×)다. 보통(-)은 서로 보통.

**R-15 핵심 6진형**: 각 진형은 핵심 6종 안에서 유리 2, 불리 2, 보통 1.

**R-16 같은 능력치 진형 대결**: 같은 병력·능력치·병종으로 진형만 다르게 자동 대전하면, 상성표의 ○ 쪽이 이긴다 (100개 조합 회귀 테스트).

**R-17 지형 상성 순서**: 숲에서 같은 조건이면 보병 > 창병 > 궁병 > 기병 순으로 전투력이 높다.

---

## 8. 그 밖의 규칙

| 번호 | 규칙 | 관련 문서 |
|---|---|---|
| R-18 | 덮어쓰기 순서: 공통 기본값 → 스텝 → 규칙 세트(대전 등). 나중 것이 이긴다 | [structure.md](structure.md) |
| R-19 | 상위 스위치가 꺼지면 하위 스위치는 꺼진 것으로 본다 (예: `capture` 꺼짐 → `duelCapture` 무시) | [captivity.md](content/captivity.md) 3장 |
| R-20 | 사거리 안의 적은 반드시 공격한다 (은신 유지 중 복병 예외) | [battle.md](content/battle.md) 4.3.1 |
| R-21 | 일기토: 퇴각 vs 활쏘기 → 체력 > 50이면 −50, 50 이하면 0 (쓰러짐) | [battle.md](content/battle.md) 2.7.3 |
| R-22 | 일기토: 공격 vs 공격 → 무력이 높은 쪽이 차이만큼 상대 체력을 깎음 (무작위 폭 적용 전 기준) | [battle.md](content/battle.md) 2.7.3 |
| R-23 | 일기토 패배(체력 0) → 중상(출전 불가). 경상은 능력치 × 0.9 | [battle.md](content/battle.md) 2.5.3 |
| R-24 | 부대 고용 가능 인원 = 최소(등록 인원, 내 군량으로 먹일 수 있는 인원, 장수 한도의 남은 자리) | [hiring.md](content/hiring.md) 2.2 |
| R-25 | 부대 고용 비용 = 1명당 가격 × 인원. 등록자 보상은 고용 인원에 비례 | [hiring.md](content/hiring.md) |
| R-26 | 같은 등록분을 여러 유저가 동시에 고용해도 등록 인원은 줄지 않는다 | [hiring.md](content/hiring.md) |
| R-27 | 고용 가격 ≤ 추천 가격 × 상한 배수 | [hiring.md](content/hiring.md) 3.1 |
| R-28 | 화면 병사 비율 N = 전장 전체 병력 ÷ 화면 병사 예산. 한 전투 안에서 모든 군단이 같은 N | [battle.md](content/battle.md) 2.4 |
| R-29 | 같은 입력(시드·편성·명령 기록·데이터 버전)이면 같은 결과 | [structure.md](structure.md) |
| R-30 | **상점 상품 데이터 검사**: 편의 기능도 치장(kind cosmetic, R-111)도 아닌 상품은 "플레이로 얻는 경로"가 비어 있으면 안 된다 (치장은 결제 전용 허용, 2026-11-15) | [economy.md](content/economy.md) 0장 |
| R-31 | **스킬북 습득**: 돈으로 산다(가격 = 스킬 단계별). 배우려면 책 1권 + 요구 능력치 + 습득 최소 조건(스킬마다, 없으면 통과; `any`면 하나만)을 모두 채워야 하고, 배우면 책이 1권 줄어든다. 못 배우면 책은 그대로 보관 | [generals.md](content/generals.md) 4장 |
| R-40 | **서버 전투 검증**: 서버는 자기 세이브(제출이 가리키는 Revision과 같아야 함)에서 같은 출진 요청·시드·대결 선택으로 출진 준비를 하고, 같은 명령 기록으로 전투를 다시 돌린다. 상태 지문·틱 수가 기기 값과 같을 때만 출진 확정 + 결과 반영하고 Revision을 올린다. 다르면 세이브는 그대로 | [../impl/server.md](../impl/server.md) |
| R-41 | **서버 행동 적용**: 전투 밖 행동(편성 저장·상황별 편성 지우기, 스킬북 구매·습득, 징병·모병·치료, 부대 창설·해산)은 기기와 서버가 같은 규칙으로 적용한다. 서버는 자기 세이브(제출의 Revision과 같아야 함)에 행동을 차례로 적용하고, 하나라도 안 되면 아무것도 반영하지 않는다(세이브 그대로, `badAction` + 순번). 세이브 통째 올리기는 서버 세이브가 새 게임(Revision 1)일 때만(처음 가져오기) | [../impl/server.md](../impl/server.md) 5장 |
| R-42 | **기록 시각·오프라인 기록**: 행동·전투 기록은 기기가 한 시각(서버 시각 기준)을 달고 오고, 서버는 그 시각으로 다시 계산한다(전투는 그 시각으로 출진 준비·결과 반영). 허용: 앞 기록(서버 세이브 시각) 이후 · 지금 이전 · 72시간 이내 (시계 차이 2분은 봐줌), 아니면 `badTime`. 서버 세이브 시각 = 마지막 기록 시각. 서버에 닿지 않은 기록은 기기에 한 순서로 남겨 두고 다음 연결 때 차례로 올린다. 하나라도 거절되면 남은 기록은 버리고 서버 세이브로 되돌린다. 그사이 서버 세이브가 바뀌었으면(다른 기기) 기록을 버리고 서버 세이브 | [../impl/server.md](../impl/server.md) 5장 |
| R-43 | **고용 출진·서버 재현**: 등록은 등록자의 서버 세이브에서 스냅샷을 만든다 (장수는 출전 가능 상태로, 부대는 부대원 전부). 고용은 등록 id·판(Version)·부대 인원으로 하고, 판이 바뀌었거나 · 내 등록이거나 · 이미 가진 장수(같은 정의)이거나 · 등록 인원을 넘으면 거절. 비용(장수 1회 가격, 부대 1명당 × 인원)은 출진 확정 때 군량과 함께 낸다 (모자라면 출진 불가). 고용분의 손실·부상·포로·경험은 어디에도 반영하지 않는다. 서버는 같은 스냅샷·편성으로 재현해 맞을 때만 반영하고, 등록자에게 비용 × 보상 비율(내림)을 우편으로 보낸다. 우편은 행동 `mail.claim`으로 받는다 (서버는 우편이 있고 자원·양이 같을 때만) | [hiring.md](content/hiring.md) |
| R-44 | **고용 제한·이력**: 한 계정의 지난 24시간 고용 수 + 이번 전투의 고용 수 ≤ 하루 상한(hire.dailyMax), 같은 등록은 마지막 고용 시각 + 대기시간(hire.cooldownMinutes)이 지나야 다시 고용 (시각 = 전투 기록 시각, R-42). 넘으면 `hireLimit`. 이력은 서버가 재현해 확정한 전투로만 쌓는다: 고용이 든 군단(장수 = 지휘한 군단, 부대 = 들어간 군단)의 상태로 포로 → 포로, 궤멸 → 궤멸, 퇴각 → 퇴각, 그 밖은 아군 승리면 승 아니면 패. 잃은 병력은 장수 = 군단 전체, 부대 = 그 부대. 최근 전투는 hire.recentMax개. 다시 등록해도 이력은 이어진다 | [hiring.md](content/hiring.md) 3·4장 |
| R-45 | **좌표 → 지역·공격 가능 지역**: 좌표는 백만분의 1도 정수. 지역은 큰 단계(시·도)부터 그 상위 지역 안에서 경계 다각형이 점을 품는(경계선 위 포함) 첫 지역을 찾아 가장 작은 단계까지 내려간다. 공격할 수 있는 지역 = 찾은 단계마다 지금 지역 + 그 이웃(같은 단계, 서로). 차지에 필요한 군단 수 = 지역 값, 없으면 단계 기본값 | [territory.md](content/territory.md) 2·3장 |
| R-46 | **영토 전투·차지**: 지역의 수비군(단계 기본값 + 지역 덮어쓰기, 지역 지형)으로 스텝 "territory.<지역>"을 만든다. 출진 조건: 혼자 차지할 수 있는 단계(필요 군단 1, 시즌 행사 단계 아님) · 공격자 위치에서 공격할 수 있는 지역(R-45). 결과 반영은 스토리와 같되 스텝 클리어·첫 보상·탐색 기억·구출은 없다. 서버가 재현해 맞고 아군이 이기면 그 지역 주인 = 공격자 (이전 주인은 바뀐다) | [territory.md](content/territory.md) 5장 |
| R-47 | **영토 수입**: 차지한 지역마다 단계의 시간당 자원(income)이 쌓인다. 쌓인 양 = 시간당 양 × 쌓인 초 ÷ 3600 (내림), 쌓인 초 = 지금 − 마지막으로 받은(처음엔 차지한) 시각, 최대 incomeCapHours. 받기는 서버가 우편으로 보내고(받은 시각 = 지금) 우편은 행동 mail.claim으로 받는다. 남에게 빼앗기면 그때까지 쌓인 것을 이전 주인에게 우편으로 정산하고, 새 주인은 차지한 시각부터. 이미 내 지역을 다시 이기면 그대로 | [territory.md](content/territory.md) 6장 |
| R-48 | **수비 편성**: 지역 주인만 등록하고, 등록하면 판(Version)이 1 오른다. 스냅샷 = 주인 서버 세이브의 기본 편성(검사 통과), 장수는 출전 가능 상태로·부대는 부상병 없이. 수비 편성이 있으면 그 지역 전투의 적 = 그 편성(군단 D1…, AI 수비 태세, 군량 10분 분량), 없으면 AI 수비군. 서버는 제출의 판이 지금 판과 다르면 거절. 결과의 부상은 내 군단의 장수에게만, 수비 장수는 포로로 오지 않는다(스냅샷). 주인이 바뀌면 수비 편성은 사라진다 | [territory.md](content/territory.md) 5장 |
| R-49 | **연합**: 한 계정은 한 연합에만. 만들기: 이름은 앞뒤 공백을 뺀 길이가 범위(nameMin~nameMax) 안, 다른 연합과 같으면 안 됨(대소문자 무시), 만든 사람이 연합장. 가입: 인원 상한(memberMax)까지 (지금은 신청 없이 바로). 탈퇴: 연합장이 나가면 남은 사람 중 먼저 들어온 사람이 연합장, 모두 나가면 연합이 없어진다. 영토 목록에는 주인의 연합이 붙는다 | [territory.md](content/territory.md) 6장 |
| R-50 | **연합 관리**: 가입 방식은 바로(기본) / 신청. 신청 방식이면 가입 = 신청을 남김, 연합장이 수락(인원 상한 안, 그새 다른 연합에 들어갔으면 안 됨)·거절. 연합장만 가입 방식을 바꾸고 수락·거절·내보내기(자기 자신은 못 함). 어느 연합에 들어가거나 만들면 다른 연합에 남긴 신청은 지운다 | [territory.md](content/territory.md) 6장 |
| R-51 | **장수 모집**: 재화를 내고 1회·10회 (모자라면 아무것도 바뀌지 않음). 한 번마다: 천장 횟수에 닿았으면 천장 등급, 10회의 마지막에 아직 보장 등급 이상이 없으면 보장 등급 이상 중에서(확률 비율), 아니면 등급 확률(누적 비교)로 등급을 고르고, 그 등급의 목록 장수 중 고르게 하나. 천장 등급 이상이 나오면 천장 수를 0으로, 아니면 +1. 이미 있는 장수는 조각 +1, 없으면 레벨 1로 들어온다. 같은 세이브·같은 시드면 같은 결과. 공개 확률 = 등급 확률 ÷ 그 등급 장수 수 | [generals.md](content/generals.md) 5장 |
| R-52 | **부대 키우기**: 훈련 = 돈(병력+부상병 1,000명당 goldPer1000, 올림)을 내고 훈련도 +step, 상한 max. 승급 = 데이터의 from → to(to의 부모가 from)만, 훈련도 ≥ 조건·조건 스텝 클리어·돈이 모두 될 때. **인원 N을 골라 갈라 낸다** (2026-11-14): 1 ≤ N ≤ 병력 (0 = 전부), 돈 = ⌈N/1000⌉ × 금액. N명이 기본 부대 아래 승급 부대(그 병종, 위 부대 id)로 옮기고 기본 부대는 남는다 (전부면 병력 0). 승급 부대가 없으면 새로 (부대 수 상한에 듦, 훈련도·결속 = 기본 부대 값), 있으면 합친다 (훈련도·결속 = 인원 가중 평균 내림, 병력+부상병 ≤ unitMax). 부상병은 옮기지 않는다. 예: 보병 4,000·훈련도 0.5에서 800명 → 보병 3,200 + 방패병 800(0.5), 돈 400; 다시 0.8에서 3,000명 → 방패병 4,000, 훈련도 0.725. 전투 보정 = 공격 부대 훈련도 × attackPp − 방어 부대 훈련도 × defensePp (%p, 훈련도 0이면 보정 없음) | [troop-growth.md](content/troop-growth.md) |
| R-53 | **숨은 보상**: 전투가 끝나면 데이터의 숨은 보상마다 (스텝 지정이면 그 스텝만, victory면 이겼을 때만) 조건을 **모두** 만족하면 받는 장수(지휘 장수 / 일기토로 쓰러뜨린 장수 / 그 스킬을 쓴 장수)에게 스킬·특성. 요구 능력치가 모자라거나 이미 가졌으면 스킬북·특성서로 보관. once면 계정당 한 번. 도감은 찾은 것만 이름·보상, 못 찾은 것은 "???"와 힌트. 예: step04 손실 5% 이하로 이김 → 지휘 장수에게 재정비, step05 궁병 없이 이김 → 특성 야습 (기본 편성은 궁병이 있어 안 나옴) | [content/secrets.md](content/secrets.md) |
| R-54 | **전투 녹화**: 녹화 = 전투 설정(스텝 규칙을 뺀 것: 시드·군단·대결·탐색·연출·환경) + 명령 기록 + 결과 지문. 재생은 스텝 규칙(데이터·맵·보정·스위치)을 **지금 데이터**로 다시 채워 같은 틱에 같은 명령을 넣는다 → 같은 결과 지문. 지문이 다르면 "규칙이 바뀌어 다시 볼 수 없음", 스텝이 없거나 형식이 다르면 "읽을 수 없음". 내 녹화는 새것이 앞, 한도(maxSaved)를 넘으면 가장 오래된 것부터 지움 | [content/replay.md](content/replay.md) |
| R-55 | **전투 이어하기**: 전투 중 남긴 기록(설정 + 그때까지의 명령 + 멈춘 틱 + 출진 정보)으로 같은 틱까지 다시 돌린 뒤 이어서 끝까지 하면, 꺼지지 않은 전투와 결과 지문·명령 기록·결과 반영·서버 제출이 모두 같다. 스텝이 없거나 형식이 다르거나 기록대로 명령이 들어가지 않으면 이어 하지 않는다. 끝난 전투는 기록을 남기지 않는다. 고용 출진도 이어 하며, 이어서 끝낸 전투를 올리면 서버가 같은 시장으로 재현해 받아들인다 | [content/battle.md](content/battle.md) 4.5 |
| R-56 | **규율·용맹**: 규율 = 통솔 × 0.8 + 지략 × 0.2, 용맹 = 무력 × 0.8 + 통솔 × 0.2. 진형 전환 시간 = 3초 × (1 − (규율 − 50) × 0.01) (1~6초, 틱 올림): 통솔 80·지략 50 → **23틱**, 50·50 → 30틱, 20·20 → 39틱. 전환 중에는 이전 진형을 쓰고 받는 피해 +10%p, 원래 진형을 고르면 전환을 그만둔다. 손실 사기 하락 × (1 − (용맹 − 50) × 0.005) (0.6~1.4배): 용맹 74 → 0.88배. 스위치 discipline·valor를 끄면 바로 바뀌고 배율 1 | [content/battle.md](content/battle.md) 2.6 |
| R-57 | **한계 돌파**: 장수의 조각(모집에서 겹친 장수)으로 단계를 올린다. 단계마다 조각 1·1·2·2·3개 (끝 단계 5), 레벨 상한 = 기본 50 + 단계 × 5. 조각이 모자라거나 끝 단계면 안 됨. 조각 10개로 5단계 → 상한 75, 조각 1개 남음. 상한에 닿아 경험치가 0이던 장수도 돌파하면 다시 오른다. 행동 general.limitBreak (서버도 같은 규칙), 예전 세이브는 0단계 | [content/generals.md](content/generals.md) 5장 |
| R-58 | **병종 태그 효과**: 손실 사기 하락 × 태그마다 (1 − 그 태그 병력 비율 × (1 − 배율)), 포위 포로 압력 × 배율(그 태그 부대가 살아 있으면). 지금 정예(elite) 사기 0.8, 근위(bodyguard) 포로 압력 0.5. 전원이 그 태그면 배율 그대로: 사기 배율 0.5면 첫 손실의 사기 하락이 정확히 절반, 포로 압력 배율 0.5면 처음 쌓인 압력이 정확히 절반. 받는 피해 방향 보정: 창벽(wall) 방원·횡진에서 정면 −25%p, 측면·배후 +10%p (다른 진형이면 정면 보정 없음) | [content/troops.md](content/troops.md) 2장 |
| R-59 | **연습 대결**: 장수·대결·상대·시드·내 선택 차례로 다시 돌려 같은 결과. 결과(승/패/물러남/무)를 전투 대결과 같은 전적에 더하고 같은 성장 보상 (일기토 99승에서 1승 → 무력 +1), 결과별 경험치(승 30 …). 선택이 모자라거나·남거나·그 라운드에 고를 수 없거나(기운 0에서 필살), 없는 상대·장수면 반영하지 않음. 하루 상한 dailyLimit (0 = 없음), 날이 바뀌면 다시 0 | [content/general-growth.md](content/general-growth.md) 2.4 |
| R-60 | **진군·후퇴 (명령 = 행위)**: 진군·전군 진군 = 군단마다 지금 가로 위치 그대로 우리 진영의 앞 방향으로 곧게 (전장 끝까지, 공격 이동). 후퇴 = 뒤로 곧게 fallBackCells(8)칸 강행 이동, 군단은 전장에 남고 계속 명령 가능 (다시 진군하면 앞으로). 퇴각(이탈)은 따로: 전장을 떠남 | [content/battle.md](content/battle.md) 4.3.2 |
| R-61 | **부상병 자연 회복·보충**: 부대에 부상병이 새로 생기면 회복 시각 = 지금 + woundedRecoverMinutes(30분, 0이면 자연 회복 없음). 시각이 지나면 그 부대의 부상병 전부가 병력으로 (새 부상이 생기면 시각을 다시 잡는다). 치료로 부상병이 0이 되면 시각도 지운다. 시간 경과 처리는 화면을 열 때와 출진 직전(기기), 전투 검증 직전(서버, 출진 시각 기준)에 한다. 편성 한도까지 보충 = 기본 편성의 군단마다 장수 한도 − (병력 + 부상병)을 **가장 적은 부대부터 고르게**(같은 수면 선봉·중군·좌익·우익·좌후·우후 순) 부대 상한까지 나눠 징병 (부대마다 징병 행동, 2026-11-12 바꿈: 칸 id 순서로 채우면 선봉이 비었다) | [content/battle.md](content/battle.md) 2.4.4 |
| R-62 | **처음 안내**: 안내 = id·자리(place)·문장 키들 (`Rules/guides.json`, id 겹침·빈 문장은 읽을 때 거절). 화면이 자리 id로 물으면 아직 안 본 첫 안내. 다 읽고 닫으면 행동 `guide.seen`(GuideId): 없는 안내이거나 이미 봤으면 거절. `guide.reset`은 본 기록을 모두 지운다 (지울 것이 없으면 거절). 본 기록은 세이브(SeenGuides)에 남는다 | [content/story.md](content/story.md) 7장 |
| R-63 | **곧은 진군의 역할** (2026-11-11 고침): 진군·전군 진군에서 선봉·측면·역할 없음 = 자기 줄 그대로 앞으로 곧게(공격 이동, 군단마다 이동 명령 하나). 후위 = 앞으로 곧게 지원 이동(support): 가장 가까운 적이 rearStandoffCells(8) 안이면 제자리 (쫓지 않음) | [content/battle.md](content/battle.md) 4.3.2 |
| R-65 | **AI 진군은 자기 줄로** (적·위임): 가장 가까운 보이는 적이 aiEngageCells(12) 안이면 그 적 공격(지금 대상이면 유지). 아니면 자기 줄 진군(Lane): 목표 = 지금 위치에서 앞 방향으로 전장 끝까지 (공격 이동). 줄 끝(1칸 안)에 닿았는데도 멀면 그 적 공격, 안 보이면 예전 진군(마지막 목격 위치·적 시작 위치) | [content/battle.md](content/battle.md) 4.3.2 |
| R-66 | **포로 처리 행동**: prisoner.recruit(GeneralId, Seed) = 등용 시도: 감금 중이고 등용 가능 장수이고 재시도 대기가 끝났을 때만 (아니면 거절). 성공 확률 = 기본 + 성향 + 세뇌 보너스, 시드 난수 한 번. 성공 → 아군 장수(기본 스킬·특성), 실패 → 마지막 시도 시각 = 행동 시각(재시도 대기), 재시도 스위치가 꺼졌으면 해방. prisoner.release(GeneralId) = 해방 + 보상 (없는 포로면 거절). 시각은 행동 시각 | [content/captivity.md](content/captivity.md) 1.2 |
| R-67 | **장수 특성 효과**: 특성 = traits.json의 효과 묶음. 군단 장수가 가진 특성의 효과 중 조건(지형·시간대·날씨·병종 태그·퇴각 중인 대상·대기 중)이 맞는 것을 더한다. attackPp(공격 쪽)·defensePp(방어 쪽, 빼기) = 피해 보정 %p로 다른 보정과 함께 상한(R-1). moraleLossMul = 손실 사기 하락에 곱. skillCostPct = 그 스킬(비면 전부) 비용 × (100 + 합)/100, 올림, 최소 0. contestDamagePct = 대결에서 주는 피해 %에 더함. 스위치 traits가 꺼지면 전투 효과 없음(대결 제외) | [content/traits.md](content/traits.md) 3장 |
| R-68 | **장수 합류 보상**: 스텝 보상 `joinGenerals`의 장수는 그 스텝 **첫 클리어**에 합류한다. 새 장수(뽑기·등용·합류 모두 같음)는 레벨 1, 기본 스킬·기본 특성을 가진다. 이미 있는 장수면 대신 조각 1개. 뽑기 목록의 모든 장수는 초기 지급·스텝 합류·적으로 나와 등용 중 하나로도 얻을 수 있어야 한다 | [content/generals.md](content/generals.md) 0장, [content/story.md](content/story.md) |
| R-69 | **예약할 때와 꺼낼 때의 검사**: 스킬 예약은 넣을 때 아는 스킬·요구 능력치·대상·남은 횟수만 보고, 점수·사용 조건(지형·바람·교전 중 아님 등)은 예약을 꺼낼 때 본다. 꺼낸 예약을 받을 수 없으면 건너뛰고 이벤트 queuedSkipped를 남긴다. 자리를 먼저 고르는 자신 스킬(target.moveFirst, 복병)은 지점을 고르면 그 자리로 이동 + 도착 후 스킬 예약 | [content/battle.md](content/battle.md) 4.2, 4.3.6 |
| R-70 | **예약 고치기·막힘**: 명령 editQueue — "remove"는 Index번 예약 취소, "move"는 Index번 예약(지점 이동, 지점·범위 스킬)의 지점만 바꾼다. 지금 명령·다른 예약은 그대로, 명령 기록에 남는다. 지점 이동 중 길을 다시 찾았는데 더 갈 칸이 없으면(막힘) 이벤트 pathBlocked를 남기고 그 이동을 끝내 다음 예약으로 | [content/battle.md](content/battle.md) 4.3.5, 4.3.6 |
| R-71 | **전투 중 일기토**: 스킬 challenge(적 군단, 사거리 10)를 쓰면 받는 장수가 성향별 확률(accept.<성향>, 없으면 acceptChance, 거부 성향이면 0)로 응한다. 거절 → 그 군단 사기 + refuseMorale(−10), 이벤트 duelRefused. 응함 → 두 군단은 서로 공격·접전하지 않고 제자리, 명령을 받지 않으며 AI도 생각하지 않는다 (이벤트 duelStarted). 플레이어 쪽 선택은 명령 duelChoices(행동 id 쉼표)로, decideSec(30초) 안에 오지 않으면 양쪽 AI. 대결 계산은 이 대결만의 시드(전투 시드·틱·두 군단 번호)로 하므로 화면이 보여 준 결과와 전투 결과가 같다. 결과의 전투 영향은 전투 직전 일기토와 같다 (2.7.5) | [content/battle.md](content/battle.md) 2.7.5, [content/skills.md](content/skills.md) |
| R-72 | **진형 특성치**: formations.json stats (모두 가산). 칠 때: 정면이면 breakthroughPp, 측면·배후면 envelopPp, 원거리 소부대면 rangedPp를 더한다. 맞을 때: 정면 frontDefensePp, 측면 flankDefensePp, 배후 rearDefensePp를 뺀다 (모두 피해 %p, 다른 보정과 함께 상한 R-1). 이동 거리 × (100 + moveSpeedPct)/100 (최소 10%), 범위 피해(화살비·화계) × (100 + areaDamageTakenPct)/100, 손실 사기 × (100 + moraleLossPct)/100. 바꾸는 중이면 지금 진형 기준. 스위치 formationStats | [content/formations.md](content/formations.md) 2장 |
| R-73 | **전열과 후열**: 근접 소부대가 칠 때 쓰는 병력 = min(병력, 앞줄 병력). 앞줄 한도 = frontLine.troopsPerUnit(4000) × (100 + 진형 frontWidthPct)/100. 첫 틱 앞줄 = min(병력, 한도). 손실을 반영할 때마다 앞줄에서 잃은 수를 빼고, 1초에 한도의 refillPctPerSecond(25)%만큼 뒷줄이 채운다 (최대 min(병력, 한도)). 원거리 소부대는 접전 상대가 아니면 전체 병력으로 쏜다. 스위치 frontLine | [content/battle.md](content/battle.md) 2.4.1 |
| R-74 | **소부대 자율 판단**: ① 닿는 적 소부대 중 점수 = −거리 + 판단력(judgment)/100 × (병종 상성 %p 합/10 + (1 − 병력/처음 병력) × 3)이 가장 큰 상대를 친다 (같으면 먼저 본 것). ② 선봉 칸 부대가 없거나 쓰러지면 중군의 근접 부대가 선봉 자리에 선다. ③ 지난 틱에 치던 적의 군단이 퇴각 중이면 진형 자리에서 그쪽으로 추격(pursuit)/25칸까지 나간다. 스위치 unitAutonomy | [content/battle.md](content/battle.md) 2.2.2 |
| R-75 | **전체 난이도·사령관 협공**: 세이브의 난이도 설정(쉬움·보통·어려움, 행동 settings.difficulty)이 스텝 적 군단 난이도를 ai.json difficultyOrder에서 settingShift(−1·0·+1)칸 옮긴다 (끝을 넘지 않음). 어려움(coordinate)인 AI 공격형 군단은 ① 아직 붙지 않았을 때 같은 편 공격형 군단 중 가장 먼 군단보다 적에 coordinateGapCells(4)칸 넘게 가까우면 제자리에서 기다리고, ② 붙으면 aiEngageCells × 1.5 안에서 같은 편이 이미 치는 적을 함께 친다 | [content/ai.md](content/ai.md) 4장·7장 |
| R-76 | **2차 스킬 효과**: confuse = 대상 군단 살아 있는 소부대 중 ratio(올림)만큼을 시드 난수로 골라 혼란(결속 저항 R-적용). fortify = durationSec 동안 제자리(퇴각 제외), 받는 피해 −defensePp, 손실 사기 × (100 + moraleLossPct)/100. quickFormation = 지속 안의 다음 진형 변경 한 번은 바로. focusFire = 지속 동안 원거리 소부대가 닿으면 대상 군단부터 쏘고 그 군단에 +pp. clearConfusion에 allyRadius가 있으면 둘레 아군 군단도 (재정비 6칸). 화전 일제사격·척후 파견은 기존 효과(점화+범위 피해, 간파) 조합 | [content/skills.md](content/skills.md) 6장 |
| R-77 | **수비·휴식 기습·지휘 범위**: ① 수비 — 명령이 대기(보통)이고 이번 틱에 움직이지 않은 군단은 받는 피해 −holdDefensePp(10)%p (스위치 holdDefense). ② 휴식 중 측면·배후로 맞으면 소부대마다 restSurpriseConfusionChance(0.5)로 restSurpriseConfusionSec(3)초 혼란 (결속 저항, 스위치 restSurprise). ③ 지휘 범위 — 장수 무력·통솔이 소부대 공격·방어에 주는 영향 × 계수: 군단 중심에서 baseCells(2.5) × 통솔/50칸 안이면 1, 넘으면 fadeCells(3)에 걸쳐 minFactor(0.5)까지 (스위치 commandRange) | [content/battle.md](content/battle.md) 2.6, 4.3.4 |
| R-78 | **승리 조건·맵 오브젝트**: 패배 조건 supplyOut = 전장의 그 팀 군단(가져온 군량이 있던) 군량이 모두 0. holdOut = 스텝 holdOut.team이 holdOut.seconds까지 전장에 군단을 남기면 다른 팀 패배. zone = 점령 구역(objects type zone)의 목표 팀만 반경 안에 군단 중심이 있는 틱이 seconds만큼 이어지면 다른 팀 패배 (다른 팀이 들어오거나 비우면 0부터). 맵 오브젝트: 반경 안에 군단 중심이 있는 팀이 하나뿐이면 그 팀이 차지(다툼·빈 곳은 주인 그대로). 보급소(depot) = 주인 팀의 반경 안 군단 군량을 1초에 value씩 (가져온 양까지) 되돌림. 망루(watchtower) = 주인 팀이 그 자리에서 value칸을 본다 (숨은 부대는 못 봄) | [content/battle.md](content/battle.md) 6장 |
| R-79 | **적이 거는 일기토·AI 2차 스킬**: 일기토 신청을 받는 쪽이 플레이어가 직접 지휘하는 군단(위임 아님)이면 바로 정하지 않고 신청을 남긴다 (이벤트 duelOffered, 그동안 두 군단은 계속 싸운다). 명령 duelAnswer(Mode accept = 응함, 그 밖 = 거절)로 답하고, offerSec(10초) 안에 답이 없으면 성향 확률(R-71)로 정한다. 응하면 R-71과 같이 대결 (플레이어 = B, 선택 기록 duelChoices도 B 군단으로). 신청이 걸린 군단은 다른 신청을 받거나 걸 수 없다. AI 판단: 일기토 = 난이도 challenges(어려움)만, 내 군단이 접전 중이고 대결 능력치가 상대보다 challengeEdge(10) 이상 높은 사거리 안 적 장수에게 (점수 7). 혼란 = 사거리 안 접전 중인 적 (8). 철벽 = 맞는 중이고 사기가 시작 − rallyMoraleDrop 이하, 철벽 아님 (9). 집중 사격 = 사거리 안 가장 가까운 적, 집중 중 아님 (6). 진형 급변 = 가장 가까운 적 진형에 밀리고 이길 진형을 앎 (5). 일기토 신청 조건 무력 55 | [content/battle.md](content/battle.md) 2.7.5, [content/ai.md](content/ai.md) 3장·7장 |
| R-80 | **계략 4종**: 함정(trap) = 지점(사거리 8, 반경 1.5, 접전 중 불가)에 setupSec(3) 뒤 작동하는 숨은 함정. 놓은 팀만 본다. 상대 소부대가 반경 안에 들어오면 그 군단의 반경 × 1.5 안 소부대가 병력 lossPct(8)% 손실·confuseSec(4) 혼란(결속 저항), 함정은 사라진다 (trapSprung). 상대 팀 간파 범위에 들면 사라진다 (trapFound). 낙석(rockfall) = 시전 군단 칸 높이 > 대상 지점 칸 높이일 때만(casterAbove), 전조 2초 뒤 반경 1.5 안 상대 소부대마다 병력 12% 손실·3초 혼란 (rockfallHit). 위장 깃발(decoy) = 지점(사거리 20)에 40초 가짜 깃발. 상대 AI 군단은 aiEngageCells 안에 진짜 적이 없고, 보이는 깃발이 가장 가까운 진짜 적보다 가까우면 그 깃발로 공격 이동한다. 상대 팀 간파에 들거나 상대 군단 중심이 1.5칸 안에 오면 들통 (decoyExposed). 거짓 퇴각(feignedRetreat) = 접전 중(근접 접촉 또는 피격)에만, 적 반대쪽 fallCells(4)칸 강행 이동 + 5초 동안 AI 판단 없음. 끝날 때 아직 나를 공격 대상으로 둔 상대 군단마다 소부대 50%(올림, 시드 난수)가 4초 혼란 (feignSprung). 하나라도 걸리면 나는 8초 공격 +15%p. AI: 함정 = 아직 붙지 않았고 다가오는 적과 나 사이 (5), 낙석 = 아래의 사거리 안 적 (8), 위장 깃발 = 공격형이 아니고 적이 안 보일 때 옆 4칸·앞 2칸 (3), 거짓 퇴각 = 접전 중·사기 여유·나를 치는 적이 있을 때 (6) | [content/skills.md](content/skills.md) 6장 |
| R-81 | **녹화 공유 (공략)**: 올리기 = 녹화 JSON을 지금 규칙으로 다시 돌려 결과 지문이 같을 때만 받는다 (읽을 수 없으면 broken, 다르면 versionChanged → HTTP 422). 같은 계정·같은 지문은 하나만 (바꿔 넣음). 게임마다 sharedStoreMax(500)개, 넘으면 오래된 것부터. 목록 = 그 스텝의 것을 이긴 것 → 손실률(손실/시작 병력) 낮은 것 → 짧은 것 → 최근 것 순으로 sharedListMax(20)개. 받은 녹화는 재생 전에 다시 확인 (R-54) | [content/replay.md](content/replay.md) 7장, [../impl/server.md](../impl/server.md) |
| R-82 | **3·4차 스킬 9종** (새 병종·맵 불필요): 강행군 = 15초 이동 +60% (상태 movePct, 다른 이동 보정 뒤 곱, 최소 10%), 끝나면 사기 −8. 은밀 행군 = 숨는 지형·밤·안개(stealthCover)에서만, 바로 숨고 40초 동안 움직여도 숨음. 가림막을 벗어나거나 접전·교전·피격하면 풀림 (간파는 R-22대로). 선두 돌파 = 대상 적을 공격 명령, 12초 공격 +25%p, 장수 포위 압력 증가 × (100 + 100)/100. 이간 = 성공 확률 0.5 + (내 지략 − 상대 지략) × 0.02, [0.1, 0.95]. 성공하면 20초 동안 그 군단은 지휘 범위 배율 = minFactor, 같은 편 협공 대기에서 빠지고(기다려 주지 않음), 그 군단의 눈은 팀 시야에 보태지지 않음 (discorded / discordFailed). 유언비어 = 상대 팀 모든 군단 사기 −6. 반간 = 60초 동안 나를 노린 상대의 교란(disrupt) 군단 대상 스킬 한 번과 유언비어 한 번을 막고, 건 장수 책략 점수 −15 (counterIntrigued). 군량 습격 = 사거리 8 적 군단의 지금 군량 25% 없애고 그 50%를 내 군량으로 (가져온 양까지). 군량 보급 = 아군 군단(나 포함) 군량 + 가져온 양의 20% (가져온 양까지). 응급 치료 = 칼이 맞닿아 있지 않을 때(notInMelee), 살아 있는 소부대마다 잃은 병력의 30% 되돌림. 군량 가감은 누적 소비를 고쳐서 (SupplySystem과 같은 식). AI: 강행군(멀 때 4)·은밀 행군(가림막 안·적 못 봄 3)·선두 돌파(접전·사기 시작 이상 7)·이간(나보다 지략 낮은 적 6)·유언비어(적 둘 이상 보임 5)·반간(적이 보이면 3)·군량 습격(사거리 안 적 6)·군량 보급(군량 40% 밑 아군 7)·응급 치료(병력 75% 밑 6) | [content/skills.md](content/skills.md) 6장 |
| R-83 | **공병·투석기**: 병종 투석기(catapult, T1, 태그 siege·ranged·area, 사거리 18, 숲·산 불가, 기병 +30%p·light +20%p 상성), 공병(engineer, T2, 보병에서 승급). 목책(palisade) = 공병(casterHasTag engineer)·접전 아님, 지점(사거리 3, 반경 1.5)에 setupSec(5) 뒤 선다. 막는 소부대가 같은 편 목책 반경 안이면 받는 피해 −15%p, 치는 쪽이 mounted면 −30%p 더 (겹쳐도 한 번). 목책 칸에 불이 붙으면 무너진다. 다리 파괴(destroyBridge) = 공병·대상 칸이 bridge(targetTerrain), 사거리 2, 작업(전조) 5초 뒤 반경 1(+0.5) 안의 bridge 칸을 river로 (모든 군단 길 다시 찾기). 집중 포격(bombard) = siege 태그 군단, 범위(사거리 18, 반경 2), 전조 2초, 피해 = siege 병력 × 0.6 (화살비와 같은 규칙, AI도 같은 판단) | [content/troops.md](content/troops.md) 6.1, [content/skills.md](content/skills.md) 6장 |
| R-84 | **둑·수공**: 맵 오브젝트 dam (반경 = 차지 범위, floodX·floodY·floodRadius = 잠기는 원). 차지 규칙은 R-78과 같다. 수공(floodAttack) = 내 군단 중심이 내 팀이 차지한 안 터진 둑 반경 안(atOwnDam), 전조 3초. 터지면 둑은 Broken. 잠기는 원 안의 칸 중 높이 0이고 모두 막는 지형이 아닌 칸 → flood(기병·공성 불가, 비용 4) floodSec(20) 동안 → mud(비용 2, 기병·중장 3, 공성 4) mudSec(30) 동안 → 원래 지형. 터지는 순간 그 칸들 위의 소부대는 팀과 상관없이 병력 15% 손실·4초 혼란(결속 저항), 그 칸의 불은 꺼진다 (damBroken, flooded). 전조 동안 둑을 떠나면 아무 일 없음(소모는 그대로, R-12). 전투 보정: flood 보병·창 −15, 원거리 −20, light −5 / mud 기병 −15, 중장 −10 (%p). AI: 둑에 서 있고 잠길 곳의 적 소부대가 아군보다 많으면 (9) | [content/maps.md](content/maps.md), [content/skills.md](content/skills.md) 6장 |
| R-85 | **수상전**: 지형 water(물, 강 river도 물) = 수군(naval 태그)만 지나간다. 수군은 물이 아닌 칸에 못 간다 (다른 막힘 규칙보다 먼저). 병종 전투선 warship(T1, naval·melee), 누선 towerShip(T2, 사거리 10)·충각선 ramShip(T2, ram·heavy)·쾌속선 fastBoat(T2, light) — 전투선에서 승급. 상성 ram → naval +15%p. 전장 유형 naval (스텝 battleType → setup.BattleType, 녹화에도 남김): 진형 상성·진형 특성치 %p × combat.json battleTypes.naval.formationPct(50)/100 (배 위 난전). 수상전 출진은 아군 부대가 모두 수군이어야 한다 (아니면 계획 오류). 퇴각: 가장자리로 갈 길이 아예 없으면(길 찾기 결과 없음) 그 자리에서 퇴각 완료. 훈련 전투 (steps.json drills: 스텝 → 태그): 그 태그 병종의 부대가 있으면 열리고 반복할 수 있다 (naval_drill ← naval, 호수 맵 lake_naval). 화선 fireShip = 수군만, 범위(사거리 10, 반경 2), 전조 2초, 내 소부대마다 병력 15%를 잃고(보낸 배) 반경 안 적 소부대가 (보낸 병력 × 2)를 나눠 받으며 4초 혼란, 맞은 군단 사기 −8. 충각 돌격 ramming = ram 태그, 적 군단(사거리 8, 배끼리 맞붙은 거리)을 공격 명령, 6초 공격 +40%p, 대상 소부대 30%(올림) 3초 혼란. AI: 화선(사거리 안 적 병력 ≥ 내 절반, 7)·충각(사거리 안 가장 가까운 적, 7) | [content/battle.md](content/battle.md) 10장, [content/troops.md](content/troops.md) 3.7 |
| R-86 | **마스터 계정·개발 행동**: 행동 `dev.grant`(PayWith = supply·gold·gem, Count 1 ~ 10억 → 그만큼 더함), `dev.unlockAll`(모든 스텝을 깬 것으로, 보상 없음, 바뀔 것이 없으면 거절), `dev.giveGeneral`(GeneralId, 가지지도 잡혀 있지도 않은 장수를 합류 — "*" = 그런 모든 장수, 없으면 거절). 서버는 `dev.`로 시작하는 행동을 마스터 계정(설정 `Game:Masters`, 쉼표 구분)의 것만 받고 아니면 묶음 전체 거절(422). `GET /v1/me`는 IsMaster를 준다. 기기: 개발 줄 탭은 개발 빌드이거나 마스터일 때만 | [content/main-screen.md](content/main-screen.md) 2.1.1, [../impl/server.md](../impl/server.md) |
| R-87 | **샘플전투·맵 목록**: 맵 목록 = 스텝 순서(본편 → 훈련 → 구출)로 스텝이 쓰는 맵을 모은 것 (맵마다 한 줄, 쓰는 스텝 목록). 샘플전투 = 세이브 사본(모든 스텝 깸, 군량 10억)으로 그 맵을 쓰는 스텝에 출진. 편성은 저장된 편성 그대로, 다른 출진 규칙(수상전은 수군만 등)은 같다. 결과·보상·군량·부상·진행·이어하기는 남지 않는다 (원본 세이브는 그대로). 공성전 맵은 아직 없음 (공성 시스템 필요) | [content/main-screen.md](content/main-screen.md) 2.1.1 |
| R-88 | **접전 중 빼기**: 플레이어가 직접 지휘하는(위임 아님) 군단의 보통 지점 이동(MoveTo, 방식 normal)은 접전·교전으로 멈추는 규칙에서, 목표 방향과 가장 가까운(보이는) 적 방향의 내적이 0 이하(적에게서 멀어지거나 옆)이면 멈추지 않는다. 보이는 적이 없어도 멈추지 않는다. 적 쪽 이동·공격 이동·후위 이동·AI·위임 군단은 그대로 멈춘다 (강행·퇴각은 원래 멈추지 않음) | [content/battle.md](content/battle.md) 이동 방식 표 |
| R-89 | **마주침 일기토**: 대결 정의 trigger.when = encounter(일기토)이면 출진 때 판정하지 않고, 스텝이 허용하면(duelAllowed·스위치) setup.EncounterContests에 넣는다. 전투 중 틱마다: 짝(pairs = commanders → 양쪽 총대장 군단끼리, any → 장수 군단 아무끼리)인 두 편 군단이 둘 다 전장·장수 있음·대결/신청 중 아님·아직 마주침 대결 안 함이고, 거리 ≤ encounterCells(8)이며 어느 한쪽 팀이 상대를 보면 그 쌍을 한 번만 판정 (전투 직전 대결과 같은 규칙: 거부 성향 없음, 능력치 차이 ≤ maxStatDifference(3), chance(0.8), 전투 난수). 일어나면 (duelEncounter, 군단 = 받는 쪽, 값 = 건 쪽) 플레이어 쪽이 받는 쪽: 직접 지휘 중이면 신청(duelOffered)으로 offerSec(10) 동안 답을 기다리고 답이 없으면 응함, 거절하면 받은 군단 사기 + refuseMorale(−5). 위임·AI끼리면 바로 시작하고 고르는 시간 없이 다음 틱에 AI로 끝낸다. 플레이어가 응하면 decideSec(30) 안에 고른다 (R-71과 같음). 한 전투에 maxPerBattle(1)번까지. 논쟁(when = sortie)은 그대로 전투 직전 | [content/battle.md](content/battle.md) 2.7 |
| R-90 | **장수 탈것 (보기만)**: 장수 정의 mount(horse·palanquin, stats.json mounts.kinds 밖이면 데이터 오류)가 있으면 그것, 없으면 지금 능력치로 지력 ≥ 무력 + palanquinIntellectOverMight(10)이면 가마, 아니면 말. 전투 규칙에는 영향 없음 (전장 3D 장수 인형·일기토 화면 인형만). 논쟁 화면은 걸어서 | [content/battle.md](content/battle.md) 2.7, [../impl/unity/art.md](../impl/unity/art.md) 2.2 |
| R-91 | **일기토는 신청과 응답으로** (R-89를 바꿈): 마주침(R-89 조건)이 일어나면 "기회"만 생긴다 (duelEncounter, 군단 = 거는 쪽). 거는 쪽 = 대결 능력치가 높은 쪽, 같으면 trigger.boldOrder에서 앞선 성향, 같으면 번호가 앞선 군단. 거는 쪽이 직접 지휘하는 플레이어 군단이면 기회(duelChance)를 offerSec(10) 동안 기다려 명령 duelChance(challenge/pass)로 답, 답이 없으면 넘어감(duelChancePassed). AI·위임은 신청한다. 신청(마주침·스킬 공통): 받는 쪽이 직접 지휘하는 플레이어 군단이면 duelOffered로 묻고 답이 없으면, 그 밖은 바로, 받는 장수 성향의 응할 확률(contests.json duel.accept, 거부 성향이면 0, 스킬 매개변수가 있으면 그것)로 정한다. 거절: 받은 군단 사기 + refuse.morale(−10), refuse.sec(10)초 동안 공격 refuse.attackPp(−10)%p (상태 duelRefused), 같은 편 다른 전장 군단 사기 + refuse.allyMorale(−3). 응하면 R-71대로 (고를 사람이 없는 쪽끼리면 다음 틱에 AI로 끝냄). 스킬 challenge는 forAll(모든 장수가 가짐, 스킬북 판매 없음), 점수 0, 한 전투 3번, cooldownSec(60) | [content/battle.md](content/battle.md) 2.7, [content/skills.md](content/skills.md) |
| R-92 | **진형 전환 중 자리 옮기기** (R-56을 더함): 전환 시간(규율로 정한 틱 수, ReformTotalTicks) 동안 소부대 자리 = 옛 진형 자리 + (새 진형 자리 − 옛 자리) × 진행(지난 틱 / 전체 틱). 전환이 끝나면 새 자리. 전환 중인 군단은 받는 피해 + reformingDamageTakenPp(10)%p, 주는 피해 + reformingAttackPp(−10)%p (스위치 discipline). 진형 특성치는 끝날 때까지 옛 진형 | [content/battle.md](content/battle.md) 진형 표 |
| R-93 | **퇴각 선택**: 퇴각 조건 = 병력 ≤ retreat.troopRatio(0.3) × 출진 병력이면 까닭 troops, 아니면 사기 ≤ 성향 기준이면 morale. 직접 지휘하는(위임 아님) 플레이어 군단이고 스위치 retreatChoice면 퇴각하지 않고 묻는다 (retreatAsked, 값 = 까닭), askSec(8)초 기한, 명령 retreatAnswer(retreat/hold). 기한까지 답이 없으면 그 까닭으로 퇴각. 조건이 풀리면(사기 회복 등) 묻기도 끝. hold = 버티기(standFast): 그 뒤 병력 ≤ standFastTroopRatio(0.1) × 출진 또는 사기 ≤ standFastMorale(0)이면 까닭 rout으로 퇴각. 위임·AI는 바로 퇴각. retreatStarted 값 = 까닭 (morale·troops·order(퇴각 명령)·rout(버티다 무너짐·패주 효과)·other) | [content/battle.md](content/battle.md) 2.5.1 |
| R-94 | **군대 개편 (병종 묶음·계급)**: 병사는 병종마다 묶음 하나(보유 부대 id = 병종 id). 계급 = 계정 레벨(`campaign.json lord`): Lv1 십부장 = 출진 군단 1·병종 칸 1·병종 최대 3,000, Lv3 백부장 1·2·5,000, Lv6 천부장 2·2·8,000, Lv10 교위 2·3·12,000, Lv15 장군 3·3·16,000, Lv21 대장군 3·4·20,000. 새 병사(징병·모병) 훈련도·결속 = min(90, 50 + 2 × (레벨 − 1)), 기존과 인원 가중 평균 내림 (보병 2,000·훈련도 80 + Lv1 500명 → 74). 치료·자연 회복은 값을 안 바꿈. 편성 = 장수 + 병종 칸(병종, 인원): 군단 수·칸 수 ≤ 계급, 한 군단에 같은 병종 두 칸 금지, 병종마다 모든 군단 인원 합 ≤ 그 묶음 병력, 군단 합 ≤ 통솔 한도. 출진 때 계급을 넘는 군단·칸은 뒤에서부터 빼고, 묶음 병력보다 많이 적힌 병종은 비율대로 줄인다. 진형 칸 6개 자동 배치: 근접만/원거리만이면 6칸, 둘 다면 근접 = 선봉·좌익·우익·중군, 원거리 = 좌비·우비 (한쪽 가지 수가 칸보다 많으면 다른 쪽 끝 칸을 빌림), 무리 안에서 큰 병종부터 1칸씩 + 남는 칸은 칸당 인원이 많은 병종, 칸 인원 = 병종 인원 ÷ 칸 수(나머지 앞 칸부터). 보병 3,000 하나 → 6칸 × 500. 전투 결과는 병종 묶음으로 모은다 (결속은 묶음마다 한 번). 예전 세이브는 읽을 때 병종별로 합친다 (병력·부상병 더함, 훈련도·결속 가중 평균, 칸 편성 → 병종 칸). 마스터 `dev.lordLevel`(Count = ±) 1~30 | [content/army-v2.md](content/army-v2.md) |
| R-95 | **군단 편성 (3×3 포진판)**: 편성마다 자리(줄 0 앞 · 1 가운데 · 2 뒤, 칸 0 왼 · 1 가운데 · 2 오른)와 총대장(★, 하나). 자리가 있으면 진군 역할 = 줄(앞 선봉 · 가운데 측면 · 뒤 후위), 전투 시작 위치 = 팀 시작 위치 + 진영 앞 방향 × spawnSpacingCells × (1 − 줄) + 오른쪽 × spawnSpacingCells × (칸 − 1). 총대장 군단이 대장(없으면 첫 군단). 자리를 겹치거나 3×3 밖, 총대장 둘 이상이면 저장 안 됨. 다른 군단 자리로 옮기면 맞바꿈. 자동 편성 = 계급 군단 수까지 통솔 한도 큰 장수, 군단마다 남은 병력이 많은 병종부터 칸 수·통솔 한도까지, 빈 자리는 앞 가운데 → 가운데 왼·오른 → … 순, 총대장이 없으면 첫 군단. 편성 복사 = 검사 통과한 사본을 다른 상황 편성으로 | [mockups/07_formation](mockups/07_formation/변경점.md) |
| R-96 | **영토 깃발 꽂기** (territory-v2.md 2.1 · 2.2): 좌표는 칸(cellLatE6 × cellLonE6, 백만분의 1도) 가운데로 맞춘다 (음수는 아래로 내림). 거리² = (Δ위도 × 1도당 m)² + (Δ경도 × 1도당 m)² (정수, 평면 근사). 원 반지름 = 계정 계급 flagRadiusM (모든 깃발 같음, 경계 포함). 꽂기 검사 순서: 위치 → 꽂는 칸이 내 위치에서 반지름 안 → 장수 있음 · 출전 가능(CanDeploy) · 다른 내 깃발에 없음 · 기본 편성에 그 장수 군단이 있음 → 같은 칸에 내 · 우리 연합 깃발이면 occupied → 다른 주인(우리 연합 아님) 원 안이면 꽂지 않고 가장 가까운 깃발과 공성전(siege, 같은 거리면 키 순서), 그 깃발이 보호 중이면 protected. 내 · 우리 연합 원과는 겹쳐도 된다. 꽂으면 키 "flag:위도E6,경도E6", 장수, 반지름, 수비 = 그 장수 군단 하나의 스냅샷(R-48 방식, 판 1) | [territory-v2.md](content/territory-v2.md) |
| R-97 | **깃발 공성전 · 수입** (territory-v2.md 2.3): 스텝 id = "territory." + 깃발 키, 틀 = flags.json battle, 적 = 깃발 주인의 수비 스냅샷. 출진 검사: 깃발 키 · 위치가 있고 깃발이 내 위치에서 내 계급 반지름 안 (서버는 더해서: 깃발이 있고, 내 것이 아니고, 보호 중이 아님). 끝나면 지든 이기든 그 깃발은 지금 + protectMinutes까지 보호. 공격자가 이기면 이전 주인에게 쌓인 수입 우편 → 그 자리에 공격자 깃발 (장수 = 제출 편성 첫 군단 장수, 없으면 기본 편성 첫 군단, 반지름 = 공격자 계급, 수비 = 그 장수 군단 스냅샷). 수입 = 깃발마다 시간당 incomePerHour × 쌓인 초 ÷ 3600 (내림), 최대 incomeCapHours, 받기는 지역 수입(R-47)과 같이 우편 | [territory-v2.md](content/territory-v2.md) |
| R-98 | **닉네임 장수 · 용병 장수** (hiring-v2.md 1·2장): 닉네임 장수 = 장수 틀 avatar (모든 유저 같은 틀, 화면 이름 = 닉네임). 새 게임은 처음 지급에 있고, 예전 세이브는 행동 avatar.create (없을 때만 합류, 있으면 false). 용병 장수 등록은 avatar만 (다른 장수 = notAvatar, 없으면 notOwned 먼저). 남의 avatar를 고용하면 스냅샷 장수 id를 avatar_hired로 바꿔 내 avatar와 겹치지 않는다 (같은 전투에 고용 avatar는 하나). dev.giveGeneral "*"는 avatar_hired를 주지 않는다 | [hiring-v2.md](content/hiring-v2.md) |
| R-99 | **용병 배신** (hiring-v2.md 4장): 수비 스냅샷에 주인 계정(OwnerAccountId)을 둔다 (깃발 꽂기 · 공성전 뒤 · 수비 등록). 출진 준비에서 고용 장수의 등록자가 이 전투 수비 주인과 같으면 그 군단 BetrayToTeam = 적. 전투: 그 군단의 지금 편 아무 군단이 교전(Engaged)한 틱 끝에 한 번 — 편을 적으로 바꾸고(상태의 편만, 설정은 그대로) AI(공격 성향)가 지휘, 명령 예약 비움, 사건 betrayed(값 = 원래 편). 결과 반영: 배신한 장수의 등록자 몫 고용비를 모두 돌려받고(보고 Refunded · Betrayed), 서버는 그 등록자 보상을 보내지 않는다. 첫 교전이 없으면 배신도 없다 | [hiring-v2.md](content/hiring-v2.md) |
| R-100 | **무료 모집** (12 시안): 모집마다 하루 freeDaily번 (서버 시각 unix 일, 날이 바뀌면 다시). 재화 없이 1회 모집과 같게 뽑는다 (천장도 센다). 오늘 남은 무료가 없으면 아무것도 바뀌지 않는다. 행동 gacha.free (BannerId · Seed · 시각). 광고는 화면이 (끝까지 본 경우에만, 광고가 꺼져 있거나 광고 제거면 광고 없이) | [generals.md](content/generals.md) 5장 |
| R-101 | **최근 녹화 자동 보관** (14 시안): 끝난 전투는 저장하지 않아도 최근 목록에 recentKeep개 (replay.json, 새것이 앞, 넘으면 가장 오래된 것이 밀려남, 같은 결과는 한 번). [저장]하면 id 그대로 저장 목록(maxSaved)으로 옮긴다. 분류: 영토 전투 = territory, 고용을 데리고 나감 = merc, 나머지 story. 기기에만 | [replay.md](content/replay.md) |
| R-102 | **계정 삭제** (15 시안, 스토어 정책): 설정에서 요청하면 서버가 요청 시각을 남기고(다시 눌러도 처음 시각) 7일(AccountDeletion.GraceSeconds) 뒤 영구 삭제 — 토큰 · 외부 로그인 · 이메일 · 닉네임 · 가입 · 게임별 세이브 · 우편 · 고용 기록 · 고용 등록 · 깃발 · 공유 녹화. 그 안에 다시 들어오면(/me) 취소 (DeletionCancelled). 요청한 기기는 로그아웃 | [startup.md](content/startup.md) |
| R-103 | **새 명령** (19 시안, battle.md 4.3.4.1): 돌격(이동 +chargeMovePct, 첫 접전에 상대 사기 −chargeShockMorale 한 번 → 보통 공격) · 사격만(원거리 필요, 사거리 안이면 멈춰 쏨) · 유인(맞붙으면 정한 지점으로 강행) · 은밀(이동 stealthMovePct, 적 시야 stealthSightPct %) · 지키기(지점 둘레 guardRadiusCells 안 적만, 아니면 지점) · 엄호(아군 둘레 escortRadiusCells 안 적, 아니면 escortFollowCells 안으로). 여러 군단 일제 공격 = 같은 적 협공 대기, 집결 = 한 지점 둘레 2칸 간격, 경로 = 첫 점 + 예약 이동 + 끝 적이면 예약 공격. 명령 검사: 사격만은 원거리, 유인 · 지키기는 전장 안 지점, 엄호는 다른 아군. 재현 같음 | [battle.md](content/battle.md) 4.3.4.1 |
| R-104 | **패배 팁** (20 시안): 진 전투에서 Rules/defeatTips.json order 차례로 첫 맞는 이유 — supply(아군 군단 군량이 0) · commander(아군 총대장 군단이 물러남) · troops(처음 병력 < 적 처음 병력 × troopsRatio) · losses(내 손실 ≥ heavyLossPct %, 적 손실 < enemyLightLossPct %) · time(시간 제한), 없으면 other. 이유마다 바로가기 목록(shortcuts). 이기면 팁 없음. 보고에 장수마다 경험치 전 · 후(ExpGains, 성장 게이지) | [story.md](content/story.md) 5장 |
| R-105 | **구글 연동 · 로그아웃** (15 설정): 게스트 계정에 구글(Firebase ID 토큰)을 붙이면(POST /v1/me/link/firebase) 계정 번호 · 진행 그대로, 다음 구글 로그인도 그 계정. 그 구글이 이미 다른 계정 것이면 409 taken (섞지 않음) — 화면은 "그 계정으로 바꿀까요?"를 묻고, 바꾸면 이 기기의 게스트 진행을 지우고 구글로 다시 시작해 그 계정의 진행을 받는다. 이 계정에 이미 구글이 있으면 alreadyLinked. /me의 Linked = 외부 로그인이 붙었는지. 로그아웃 = 남은 기록을 올린 뒤(못 올리면 묻고) 이 기기의 계정 토큰 · 세이브 · 로그인 수단을 지움 → 다음 게스트는 새 계정, 같은 구글은 그 계정의 서버 세이브. 게스트 로그아웃은 "다시 찾을 수 없음" 경고 + 구글 연동 추천. 계정 삭제 요청 뒤에도 같은 로그아웃 | [startup.md](content/startup.md) 2장 |
| R-106 | **위임 켜면 바로 AI 지휘** (R-103 위임 스위치를 고침): 위임을 켜는 순간 진행 중인 플레이어 명령도 AI에게 넘기고(플레이어 명령 표시 끔) 예약을 비운다. 그 뒤 AI 판단 주기에 R-65대로 가까운 적을 공격하거나 줄 진군. 위임 중 새로 내린 플레이어 명령은 예전처럼 끝날 때까지 우선. 끄면 지금 명령 그대로 플레이어에게 (예전: 켜도 진행 중 이동을 두어, 적에게서 멀어지던 이동이 접전으로 멈춘 채 끝나지 않아 등을 보인 채 싸우지 않음) | [content/ai.md](content/ai.md) 9장 |
| R-107 | **근접 공격** (공격 › 근접 공격): 상대를 누르지 않고, 고른 군단마다 가장 가까운 보이는 전투 중 적 군단을 보통 공격 (같은 거리면 앞 순서). 보이는 적이 없으면 받지 않음. 상대를 누르는 공격은 "지명 공격"(여러 군단이면 일제 공격) | [content/battle.md](content/battle.md) 4.3.4.1 |
| R-108 | **치장 1단계** (cosmetics.md): 데이터 Rules/cosmetics.json items (kind armor = 갑옷 한 벌 · horse = 말 색, lines, look = 그림 값, obtain.shop = 가격 하나 gold · gem · priceKRW, playPaths). 계열 = 부모를 따라 올라간 1단계 병종 (공병 태그면 engineer, 투석기 · 배는 없음), 갑옷은 사람 계열 · 말은 말 탄 계열(기병)만. 사기(cosmetic.buy): 안 가진 것만, 엽전 · 금괴 가격만큼 빠짐(모자라면 거절), 결제 가격만이면 거절. 입히기(cosmetic.equip, TroopType = 계열 · Situation = 종류 · CosmeticId, 없으면 벗기): 가진 것 · 입을 수 있는 것만, 이미 그 모습이면 거절, 하나 사면 입을 수 있는 모든 계열에. 출진 때 내 소부대 설정에 입은 갑옷 · 말 id(Armor · Horse, 빌린 부대는 없음)를 싣고 녹화에도 남지만 **규칙 계산 · 상태 해시는 읽지 않는다** (입은 전투와 안 입은 전투의 결과 · 해시가 같다). 데이터 검사: 종류 · 계열 · 가격 하나 · 플레이로 얻는(엽전 가격 또는 playPaths) 공용 갑옷 하나 이상 | [content/cosmetics.md](content/cosmetics.md) |
| R-109 | **깃발 장수 여럿 · 회수** (R-96을 넓힘, 2026-11-15 사용자): 꽂기 요청 GeneralIds(1명 이상, 겹친 것 빼고 차례대로, 없으면 GeneralId 하나). 장수마다 R-96 검사(있음 · 출전 가능 · 다른 내 깃발에 없음 · 기본 편성 군단), 하나라도 안 되면 꽂지 않음. 깃발 GeneralIds = 그 장수들(GeneralId = 첫째), 수비 = 그 장수들 군단 스냅샷. 회수(RecallRequest: 깃발 키 · 장수들, 없으면 모두): 없는 깃발 noFlag, 남의 깃발 notMine, 깃발에 없는 장수 notOnFlag. 남는 장수가 있으면 수비를 남은 장수로 다시(판 + 1), 모두 회수하면 쌓인 수입을 우편으로 보내고 깃발을 지운다. 회수한 장수는 다시 다른 깃발에 나갈 수 있다. 공성전에서 이긴 공격자 깃발은 장수 1명 | [territory-v2.md](content/territory-v2.md) |
| R-110 | **용병 등록 서버 검사** (hiring-v2.md 1장, R-98을 넓힘): ① 등록하는 계정의 영토 깃발(flag: 키, 그 계정 것)에 닉네임 장수가 나가 있으면 장수·부대 모두 거절 (까닭 `onFlag`). 깃발에서 회수하면 다시 된다. ② 부대 등록 = 닉네임 장수가 **기본 편성**(CompositionsFor(null))에서 이끄는 군단의 병종 칸(Picks, 고용 칸 제외)에 있는 병종 묶음만 (아니면 `notAvatarCorps`). 등록 인원 = min(묶음 병력, 그 칸 인원 합). 예전 칸 편성(Slots)만 있으면 그 부대 전부. 서버(StrategyGameModule.RegisterHire)와 가짜 서버(LocalOnlineService)가 같은 규칙(HireRules.Register + TerritoryFlags.GeneralsOnFlags) | [content/hiring-v2.md](content/hiring-v2.md) 1·3장 |
| R-111 | **치장 2단계 — 얻기 · 남의 겉모습** (cosmetics.md 5·7장, R-108을 넓힘): ① 스텝 첫 클리어 보상 `rewards.cosmetics`, 숨은 보상 `reward.cosmetic` → `CosmeticService.GrantOrRefund`: 없으면 얻고(보고 Cosmetics / FoundSecret.Cosmetic), 이미 가졌으면 엽전 + `duplicateGold`(임시 200, 보고 CosmeticRefundGold). 모르는 id는 아무것도 안 함. ② 상품 `kind: cosmetic` + `cosmeticId`: 영수증 결제(R-64)로 같은 GrantOrRefund. 치장 상품은 편의 기능처럼 플레이 경로가 없어도 R-30 통과 (`ProductDef.PlayPathExempt`), cosmeticId 없으면 데이터 오류. ③ 데이터 검사 `ValidateRewards`: 보상 · 상품의 치장 id가 있고, 치장 playPaths(story:스텝 · secret:id)가 실제 보상과 맞고, priceKRW 치장에 치장 상품이 있다. ④ 용병 부대 등록에 주인의 그 계열 갑옷 · 말 (`HireListing.Armor` · `Horse`) → 고용 출진의 그 부대 `UnitSetup.Armor` · `Horse`. 영토 수비 스냅샷 `DefenseUnit.Armor` · `Horse` → 공성전 적 부대. 겉모습만이라 해시 · 결과에 영향 없음 (R-108) | [content/cosmetics.md](content/cosmetics.md) 5·7장 |
| R-112 | **출석 · 교환 · 금괴 스킬 해금** (economy.md 1장 · 1.0장 · 3.1장, 값은 `economy.json` 임시): ① 출석 `DailyEconomy.CheckIn`(서버 `POST /attendance`, 가짜 서버 같은 코드): 하루 = 서버 시각 + `dayStartUtcOffsetHours`의 날, 하루 한 번 (같은 날이면 already), `attendance.cycle` 차례대로 우편(`MailKinds.Attendance`), 끝나면 처음, `missedResets`면 하루 빠질 때 처음. 기록은 서버 저장소 (세이브 밖). 우편 받기 `mail.claim`은 금괴도 받는다. ② 교환 행동 `economy.exchange`: 받는 양 = ⌊⌊낸 양 × get ÷ give⌋ × (100 − lossPct) ÷ 100⌋, 0이면 거절. 비율마다 하루 낸 양 ≤ `dailyLimit`(0 = 없음), 날은 행동 시각의 날 (시각 0이면 거절), 날이 바뀌면 다시 센다. 금괴로 바꾸는 비율은 없다 (데이터 검사). ③ 행동 `skill.unlockLearn`: 습득 조건이 있는 스킬만, 단계 금괴(`gemByTier`)를 내고 계정에 한 번 기록 → 스킬북 습득(R-31)에서 습득 최소 조건을 보지 않는다 (책 · 요구 능력치는 그대로) | [content/economy.md](content/economy.md) 1장 · 1.0장 · 3.1장 |
| R-113 | **공성전 준비 1분 · 수비자 알림** (territory-v2.md 2.3, R-97을 넓힘): ① `TerritoryFlags.StartSiege` (서버 `POST /territory/siege`, 가짜 서버 같은 코드): `CheckSiege`를 지나면 깃발 기록에 공격자 · 서버 시각을 남기고 수비자에게 우편 `siegeAlert`(RegionId = 깃발, FromAccountId = 공격자). 준비 · 공격 시간(준비 + window) 안에 다른 공격자면 `busy`, 같은 공격자면 기록 그대로 (알림 한 번). ② 공성전 제출 `CheckSiegeReady`: `siegePrepSeconds` > 0이면 (데이터 0 = 기다림 없음, 2026-11-16 사용자: 서버 기록 · 수비자 알림만 — 60초 대기는 유저 대전과 함께 to-be.md) 내가 준비했고, 지금 ≥ 시작 + 준비, 지금 ≤ 시작 + 준비 + window여야 한다 (아니면 BadSortie). ③ `CancelSiege`: 내 준비이고 준비 시간 안이면 기록 · 수비자 알림을 지움 (준비가 끝나면 취소 없음). ④ `AfterSiege`는 이기든 지든 기록 · 알림을 지운다 | [content/territory-v2.md](content/territory-v2.md) 2.3 |
| R-114 | **영수증 검증기 뼈대** (economy.md 4장, R-64를 넓힘): 결제 행동의 영수증은 서버의 `IReceiptVerifier.Verify(상품, 영수증)`가 판정 (`ReceiptVerdict`: Valid · Reason · TransactionId). `ReceiptCheck`는 빈 영수증 → empty, 검증 실패 → 검증기 까닭, 통과해도 **같은 거래 id**(없으면 영수증 문자열)를 이미 썼으면 used로 거절. 설정 `Game:ReceiptVerifier`: fake(기본, "fake:"만) · off(모두 거절) · googlePlay(자리, 지금은 notConfigured로 거절) · 모르는 값은 서버 시작 오류. ReceiptVerifier가 없고 `AllowFakeReceipts=false`면 off | [content/economy.md](content/economy.md) 4장, [../impl/deploy-cloudrun.md](../impl/deploy-cloudrun.md) 7장 |
| R-115 | **깃발 추가 파견** (territory-v2.md 2장, R-109를 넓힘, 2026-11-16 사용자): 꽂기 요청에 `FlagKey`가 있으면 새로 꽂지 않고 그 깃발에 장수를 더 보낸다 (`TerritoryFlags.Reinforce`, 서버 · 가짜 서버 같은 꽂기 길). 위치는 보지 않는다. 깃발이 없으면 `noFlag`, 남의 깃발이면 `notMine`, 공격받는 중(공성전 기록)이면 `underSiege`, 장수 검사는 꽂기와 같다 (없음 · 출전 불가 · 다른 깃발에 있음 → `generalBusy` · 기본 편성 군단 없음). 되면 장수 = 원래 + 새 장수, 수비 = 모두의 군단 스냅샷 (판 + 1), 꽂은 때 · 수입 기록은 그대로. 추가 파견 뒤 원래 장수 회수(R-109) = 장수 바꾸기 | [content/territory-v2.md](content/territory-v2.md) 2장 |
| R-116 | **Google Play 영수증 실제 검증 · 결제 기록** (economy.md 4장, R-114를 넓힘): ① `GooglePlayReceiptVerifier` (설정 googlePlay + 패키지 + 서비스 계정 키, 없으면 notConfigured): 영수증에서 구매 토큰(그대로 · `purchaseToken` · Unity IAP Payload)을 꺼내 `purchases.products.get` → purchaseState 0 · 소모 안 함만 통과 (1 → rejected, 2 → pending, 소모함 → used, 400/404/410 → rejected, 그 밖 · 예외 → unavailable), 테스트 구매(purchaseType 0)는 AllowTestPurchases일 때만. 거래 id = orderId, 없으면 "gp:"+토큰. ② 서버 `ReceiptCheck`는 쓴 거래를 **저장소**에서 확인하고(같은 묶음 안 두 번도 거절) 통과한 것은 Pending에만 모은다 → 세이브 저장이 성공한 뒤에만 `RecordReceipts`(계정 · 상품 · 영수증 · 시각) → 행동이 거절되면 영수증이 타지 않고, 서버를 다시 띄워도 used. ③ 기록 뒤 `IReceiptSettler.Settle`: 영구 상품(`ProductDef.Permanent` = 광고 영구 제거 · 치장)은 acknowledge, 나머지는 consume. 실패해도 지급은 그대로 (서버 기록) | [content/economy.md](content/economy.md) 4장, [../impl/deploy-cloudrun.md](../impl/deploy-cloudrun.md) 7장 |
| R-117 | **결제 복원** (economy.md 4장): 행동 `store.restore`(ProductId, Receipt). 규칙: 상점에 있는 **영구 상품**(`ProductDef.Permanent`: adFreeDays 0 · kind cosmetic)만, 광고 영구 제거는 AdFreeForever = true, 치장은 없으면 얻기(중복 엽전 없음), 이미 있으면 바꾸지 않고 성공. 소모성 · 기간제 · 영수증 없음은 거절. 서버 검사: 스토어에 다시 확인(R-114 · R-116) → 거래가 이 계정에 반영된 것이면 그대로 통과, 다른 계정이면 `otherAccount` 거절, 처음 보는 거래면 기록(Pending → 세이브 저장 뒤 RecordReceipts, 영구라 acknowledge) | [content/economy.md](content/economy.md) 4장 |
| R-118 | **버전 확인 · 점검 안내** (server.md): `GET /v1/status`(인증 없음) = 설정 + 관리자 값 (버전은 관리자 칸이 있으면 그것, 점검은 관리자 값이 있으면 통째). 기기 판정 `AppStatus.Evaluate`: 앱 < MinVersion → UpdateRequired, 아니고 앱 < LatestVersion → UpdateAvailable, 점검 On이고 (끝 시각 0 또는 지금 < 끝) → Maintenance (문구 키 없으면 `ui.status.maintenance`). 버전은 점으로 나눈 숫자 비교 (모자란 자리 0, `-` 뒤 꼬리 무시), 읽을 수 없으면 검사 안 함. 응답이 없으면 모두 false (그대로 시작) | [../impl/server.md](../impl/server.md) |
| R-119 | **푸시 알림 서버 쪽** (territory-v2.md 2.4): 종류 attacked · allyHelp · incomeFull. ① 공성전 준비 시작(StartSiege가 수비자 알림 우편을 새로 보낼 때, R-113) → 깃발 주인에게 attacked, 주인 연합원(주인 · 공격자 빼고)에게 allyHelp. ② `PushRules.IncomeFullDue`: 깃발이고, 지금 − 받은 시각 ≥ incomeCapHours, 아직 알리지 않음(알린 시각 ≤ 받은 시각) → 주인별 한 통(깃발 수), 알린 시각을 적음. ③ 받는 사람 설정(`PushSettings`, 처음 모두 켬)에서 끈 종류는 보내지 않음. 기기 토큰은 계정당 5개(오래된 것부터 지움), 무효 토큰은 보낼 때 지움. 글은 테마 문자열 push.{종류}.title · body. 보내기 `IPushSender` off(기본) · fake · fcm | [content/territory-v2.md](content/territory-v2.md) 2.4, [../impl/deploy-cloudrun.md](../impl/deploy-cloudrun.md) 8장 |
| R-120 | **내 용병이 고용된 전투 녹화** (replay.md 5.1): 서버가 고용이 든 전투를 검증할 때 고용 결과가 있으면 녹화(`ReplayService.Capture` — 출진 설정 · 명령 · 지문)를 떠서 저장, 볼 수 있는 계정 = 고용된 등록의 주인들 + 고용한 사람. 그 등록들의 `HireRecent.ReplayId`에 녹화 id. `GET /hire/replays/{id}`는 볼 수 있는 계정만 (아니면 404), 받은 녹화는 기기 녹화 검사(`ReplayService.Check`)를 통과. 게임마다 500개, 오래된 것부터 지움, 계정 삭제 때 그 계정을 보는 사람에서 빼고 아무도 없으면 지움. `HireBook.HiredBattles` = 내 등록들의 최근 전투를 늦은 것부터 | [content/replay.md](content/replay.md) 5.1 |
| R-121 | **기간 배너** (generals.md 5장): 배너 `startsAt` · `endsAt`(unix 초, 선택). 열림 = (시작 없음 또는 지금 ≥ 시작) 그리고 (끝 없음 또는 지금 < 끝). 기간 배너(둘 중 하나라도 있음)는 모집 · 무료 모집 행동의 시각이 열린 때여야 하고 시각 0이면 거절(`closed`), 상시 배너는 시각을 보지 않는다. 열린 배너 목록 = 상시(데이터 순) 다음 기간 배너(끝나는 순). 남은 날 = ⌈(끝 − 지금) ÷ 86400⌉. 데이터 검사: 음수 · 끝 ≤ 시작이면 오류 | [content/generals.md](content/generals.md) 5장 |
| R-122 | **운영 켜고 끄기 — 모집 배너** (generals.md 5장): `ContentToggles`(종류 · id · Off · 기간 덮어쓰기 null = 데이터 그대로). 배너 열림 = 꺼지지 않았고 (덮어쓴 기간 또는 데이터 기간 안). 끈 배너는 상시라도 모집 · 무료 모집 `closed`(시각 없어도), 기간 덮어쓰기가 있으면 그 기간으로 R-121 판정 · 남은 날. 행동 처리기는 `PlayerActionRunner.Toggles`(서버 = 저장소 값, 기기 = 받아 둔 값, 없으면 데이터 그대로). 관리자 저장은 배너 종류를 통째로 바꾸고 모르는 id · 데이터 그대로인 줄은 적지 않는다 | [content/generals.md](content/generals.md) 5장 |
| R-123 | **운영 켜고 끄기 — 상점 상품 표출** (economy.md 0.2): 종류 `product`, id = 결제 상품 id 또는 엽전 · 금괴 치장 id. 보임 = 관리자 값이 있으면 Off가 아님, 없으면 데이터 기본값 (`defaultOff` = 기본 끔, 새로 등록하는 상품). 결제 상품: 가격이 있고 보이면 상점 목록 · 구매(`store.purchase`), 아니면 목록에서 빠지고 구매 거절. 이미 산 영구 상품 복원(`store.restore`)은 꺼도 된다. 엽전 · 금괴 치장: 보이면 상점 · 꾸미기에서 판매, 끄면 구매(`cosmetic.buy`) 거절이고 안 가진 판매 전용 치장(플레이 경로 없음)은 꾸미기에서도 숨김. 행동 처리기는 R-122와 같은 `Toggles`. 관리자 저장은 상품 종류를 통째로 바꾸고 데이터 기본값과 같은 줄 · 모르는 id는 적지 않는다 | [content/economy.md](content/economy.md) 0.2 |
| R-124 | **결제 검증 덮어쓰기 (관리자, 개발 중)**: 저장소에 게임별 덮어쓰기 null(서버 설정 그대로) · off · fake. 서버 설정 `Game:AllowAdminReceiptOverride`가 true일 때만 쓰인다 — false(출시)면 무시하고 관리자 API는 422 `notAllowed`. 구매 · 복원 검사는 요청마다 덮어쓰기 → 없으면 서버 설정 검증기로. fake = "fake:" 영수증 통과, off = 모두 거절, googlePlay는 덮어쓰기 값이 아님(422 `badValue`). 바꾸기는 관리자 등급만(서브 관리자 403). 스토어 확인 처리(acknowledge · consume)는 서버 설정이 googlePlay이고 덮어쓰기가 없을 때만 | [../impl/deploy-cloudrun.md](../impl/deploy-cloudrun.md) 7.1 |
| R-125 | **샘플전투2 — 병사 단위 전투 (시험 중)**: 병사마다 위치 · 체력, `engageRange` 안 가장 가까운 적에게 몰려가 가장자리 사이 `attackReach` 안이면 친다 (피해 1, 쿨다운 `attackSeconds` + 시드 난수 흔들림). 겹친 이웃(편 상관없이)과 겹친 만큼 절반씩 밀려나 섞이지 않는다. 전진 군단은 적 장수 쪽으로, 맞붙은 비율만큼 느려진다. 장수 체력 0(사로잡음) · 전멸이면 패배, 시간 끝은 남은 병사 많은 쪽. id 순서 계산 · 한꺼번에 반영, 같은 시드 = 같은 결과. 기대: 쐐기는 얇은 일자를 두툼보다 빨리 뚫어 적장에 닿고, V자는 일자보다 두툼을 더 많이 잡는다 | [content/battle-crowd.md](content/battle-crowd.md) |
| R-126 | **샘플전투2 조작 · 돌격 · 부딪침**: 편마다 부대 1 또는 3 (좌 · 중 · 우, 중군 = 총대장, 총대장 사로잡으면 승패, 다른 장수를 잃은 부대는 제자리). 부대 명령 제자리 · 이동(도착하면 제자리) · 공격(대상 부대 병사 가운데로). 적 전진 AI = 1초마다 가장 가까운 우리 부대 공격. 공격 명령으로 대열 가장자리 사이 `chargeRange` 안 → 맞붙기 전까지 돌격(빨라짐), 10% 맞붙으면 끝. 빠르게 달려와 처음 친 한 방 = 피해 +`impactDamage` · 상대 `impactPush` 밀침 | [content/battle-crowd.md](content/battle-crowd.md) 3.5 |
| R-127 | **샘플전투2 병종 · 상성**: 부대마다 병종 (troopTypes.json). 피해 = hitDamage × (1 + (공격 − 방어) × statPpPerPoint% + 상성% troopCounters.json), 1 이상. 빠르기 = speed ÷ baseSpeedStat 배. 창은 (attackRange − 1) × polearmReach 더 닿음. 궁병은 맞붙지 않았으면 사거리(attackRange × rangeScale) 안 가장 가까운 적에게 shootSeconds마다 화살 (arrowSpeed, 닿는 틱에 살아 있으면 arrowHitChance로 피해 × arrowDamageScale), 공격 명령이면 사거리 80%에서 멈춤. 기병은 반지름 mountedRadius · 부딪침 mountedImpact배, 창병에게 달려들면 받아치기 (기병이 braceDamage배 피해 · 튕김). 기대: 기병은 궁병을 창병보다 많이 잡고 창병에게 더 많이 잃는다, 궁병은 맞붙기 전에 체력 10% 넘게 깎는다 | [content/battle-crowd.md](content/battle-crowd.md) 3.6 |
| R-128 | **샘플전투2 사기 · 퇴각**: 부대 사기 moraleStart. 쓰러질 때마다 moraleDeathTotal ÷ 처음 병사 수, 옆 · 뒤에서 맞는 동안 초당 flankDrain · rearDrain (옆 · 뒤 = 부대 가운데 기준 방향 · 부대 앞 내적 frontDot · rearDot), 최근 encircleSeconds 안에 앞 아닌 두 방향 이상이면 포위 초당 encircleDrain, 장수 잃음 generalLostMorale, 같은 편 무너짐 allyRoutedMorale, calmSeconds 맞지 않으면 초당 moraleRegen. 옆 · 뒤 피해 +flankPp · +rearPp. waverAt 이하 흔들림 (걸음 × waverSpeed, 칠 간격 × waverAttack), routAt 이하 무너짐 (명령 못 받음 · 자기 쪽 가장자리로 fleeSpeed 도망 · 치지 않음 · 가장자리에서 빠짐). 한 편이 모두 무너지면 routEndSeconds 뒤 끝 (routed). 기대: V자로 감싼 두툼은 일자로 미는 것보다 빨리 무너진다, 무너진 부대는 자기 쪽으로 간다 | [content/battle-crowd.md](content/battle-crowd.md) 3.7 |
| R-129 | **샘플전투2 명령 예약 (경유 지점)**: 이동 명령에 append면 지금 이동 중일 때 경유 지점으로 붙인다 (부대마다 최대 6, 이동 중이 아니면 바로 그 지점으로 이동). 목표에 닿으면 다음 경유 지점으로, 없으면 제자리. 정지 · 공격 · 새 이동(append 아님)은 경유 지점을 비운다 | [content/battle-crowd.md](content/battle-crowd.md) 3.8 |
| R-130 | **샘플전투2 스킬 (기본 다섯)**: skills.json의 warCry · rally · holdTheLine · forcedMarch · arrowRain을 부대가 쓴다 (횟수 usesPerBattle, 화살비는 궁병 부대만, 스킬 사이 skillGapSeconds, 장수 잃음 · 무너짐이면 못 씀). 함성 = 공격 +pp%p · 쓸 때 skillContactRange 안 적 부대 사기 −amount, 고무 = allyRadius 안 같은 편 사기 +amount, 철벽 = 맞는 피해 −defensePp%p · 사기 깎임 × (1 + moraleLossPct%), 강행군 = 걸음 +movePct% · 끝에 사기 endMorale, 화살비 = 사거리 안 지점 · telegraphSec 뒤 radius 안 모든 병사에게 arrowRainVolleys번 (arrowRainChance). 적 AI: 사기 40 이하 고무 · 맞붙으면 함성 · 맞붙어 사기 60 이하 철벽 | [content/battle-crowd.md](content/battle-crowd.md) 3.9 |
| R-131 | **샘플전투2 3×3 배치 (테스트 탭)**: 편마다 부대 0~9를 줄(앞 · 중 · 뒤) · 칸(좌 · 중 · 우)에. 칸 → x = 전장 가운데 + (칸 − 1) × gridColPitch (보는 쪽 기준), 줄 → 편 앞 자리에서 자기 쪽으로 줄 × gridRowPitch. 병사 수 1 ~ gridMaxCount (기본 gridDefaultCount). 총대장은 표시한 첫 부대, 없으면 줄 · 칸 순 첫 부대 | [content/battle-crowd.md](content/battle-crowd.md) 3.10 |
| R-132 | **샘플전투2 조작 명령**: 위임 = 적 AI와 같은 규칙 (10틱마다 대상 없으면 가장 가까운 적 부대 공격 · AI 스킬, 궁병 화살비 포함), 다른 이동 명령이면 꺼짐. 진군 = x 그대로 적 쪽 끝으로 (돌격 거리 안이면 돌격, 궁병은 가장 가까운 적이 사거리 80% 안이면 멈춤), 끝에 닿으면 대기. 근접 공격 = 가장 가까운 적 부대 공격 + 궁병도 쏘지 않고 붙음. 후퇴 = 자기 쪽 끝 retreatMargin 안쪽까지 전진 걸음 × retreatSpeed, 대열은 그대로, 후퇴 중엔 SideOf 앞뒤를 뒤집음 (앞에서 맞아도 뒤), 병사는 닿은 적만 침, 도착하면 대기. 경로 = 점들 차례로 (MaxPathPoints 16까지), 끝 대상이 있으면 도착 뒤 공격 | [content/battle-crowd.md](content/battle-crowd.md) 3.11 |
| R-133 | **샘플전투2 지형 1단계**: 맵 1칸 = 전장 칸 2.5 (24 × 40 → 60 × 100, R-135부터 mapFieldWidth ÷ 맵 폭), 맵 위 줄 = 적 쪽. 배치는 spawns (teamA 우리 · teamB 적)를 가운데 줄로. 병사 · 부대 걸음 × 1 ÷ 선 칸 이동 비용 (terrainRules). 못 가는 칸에는 병사가 들어가지 않음 (한 축 미끄러짐 · 제자리), 부대는 곧은 길이 막히면 맵 칸 가장 싼 길 (8방향, 대각선 ×1.4, 모서리 끼기 금지, 같으면 칸 번호 작은 것). 이동 목표가 못 가는 칸이면 가장 가까운 칸. 피해 %p += 치는 병사가 선 지형의 terrainCombat (태그 합) | [content/battle-crowd.md](content/battle-crowd.md) 3.12 |
| R-134 | **샘플전투2 막는 소품**: crowd.json obstacles의 지형 칸마다 perCell개 (소수 부분 = 해시 확률로 하나 더), 자리 · 크기(× 0.75 ~ 1.25)는 칸 번호 해시 (결정적). 병사 다음 위치가 소품 반지름 + 병사 반지름 안이면 밖으로 (2회, 정면이면 옆으로 비킴), 그래도 안이면 제자리. 평지 맵엔 없음. 제자리 부대 병사: 자리(소품 안이면 테두리 밖)에서 0.2칸 안이면 멈춤, 멈춘 곳에서 0.8칸 넘게 밀리기 전엔 그대로, 15틱 동안 0.02칸 넘게 가까워지지 못하면 그 자리에서 멈춤 | [content/battle-crowd.md](content/battle-crowd.md) 3.13 |
| R-135 | **샘플전투2 맵 48 × 80**: 맵 1칸 = mapFieldWidth ÷ 맵 폭 (24칸 → 2.5, 48칸 → 1.25, 전장 60 × 100). 맵 높이 층 heights(0 ~ 9)가 있으면 높이 = 값 ÷ 3 단, 없으면 지형 규칙 height. 막는 소품 수 = perCell × 칸 넓이 ÷ 6.25 (2.5 × 2.5 넓이 기준). 길 찾기 = 다익스트라 + 이진 힙 (같은 거리면 칸 번호 작은 것). 샘플전투2 맵 7개 모두: 데이터 검사 통과, 양쪽 시작 자리가 보병 · 기병 · 궁병에게 열려 있고 서로 길이 있음 | [content/battle-crowd.md](content/battle-crowd.md) 3.14 |
| R-136 | **샘플전투2 행군 대열**: 자동 행군이 켜져 있고 움직이는 명령이며 (돌아가는 길이 있거나 가운데가 marchTerrains 위) 적이 돌격 거리 + marchReformRange 밖이면 행군. 병사 k(살아 있는 병사 id 순) → 줄 k ÷ marchWidth, 칸 k % marchWidth, 자취 위 (줄 + 1) × 간격 × 1.3 뒤 + 자취 직각으로 칸 벌림 (못 가는 땅이면 자취 위). 끄기: 멈춤 · 적 가까움이면 바로, 아니면 marchOffTicks 동안 조건 아님 → 진형 자리 다시 나눔. 자리까지 막힌 병사는 자취의 가까운 점에서 머리 쪽 6점 안, 곧게 갈 수 있는 가장 앞 점으로. 장수의 나무 · 바위 몸집 = 병사 반지름. 협곡 끝까지 150명 이동 → 도착 뒤 95% 이상이 부대 가운데 15칸 안 | [content/battle-crowd.md](content/battle-crowd.md) 3.15 |
| R-137 | **샘플전투2 지름길 지도 · 행군 이동**: 자리까지 곧은 길이 막힌 병사 → 부대 가운데 칸까지의 다익스트라 거리 지도(부대 칸이 바뀌거나 flowRefreshTicks마다)에서 거리가 줄어드는 이웃 칸(같으면 칸 번호 작은 것), 두 칸 앞이 곧게 보이면 두 칸 앞. 행군은 OrderMove(march = true)일 때 그 이동만 (AutoMarch 기본 끔), 시작할 때 자취 새로 · 줄 순서 = 목표에 가까운 병사부터. 협곡 끝까지 150명: 진형 · 행군 모두 도착 뒤 90% 이상 모임. 행군으로 올라갔다 되돌아오면 예전 꼭대기보다 1칸 넘게 올라가는 병사 없음 | [content/battle-crowd.md](content/battle-crowd.md) 3.15 |
| R-138 | **샘플전투2 장수 성향 · 병종 적성**: 장수 자리 front = 대열 앞끝 + generalFront, back = 뒤끝 − generalBack, rear = 뒤끝 − generalRear. 체력 = generalHp × 무력 ÷ generalHpMight (반올림). 무력 ≥ generalFightMight면 직접 싸움 (+ (무력 − 50) × generalMightPp %p). 장수 받는 피해 = 피해 × clamp(100 − (무력 − 30) × generalGuardPerMight ÷ 100, generalGuardMinPct, 100)% (1 이상). 체력 × 100 ≤ 최대 × generalEscapeHpPct면 물러남 (자기 쪽 끝 0.5칸 안이면 빠져나감 = FledTick), 총대장이 빠져나가면 끝 까닭 generalFled. 적성 %p (aptitudePp[등급], 등급 = 병종 → * → B) 치는 쪽 + · 맞는 쪽 − (근접 · 화살) | [content/battle-crowd.md](content/battle-crowd.md) 3.16 |
| R-139 | **샘플전투2 장수 호위 · 공수 교대**: 선봉 장수 체력 × 100 ≤ 최대 × generalPullBackHpPct면 자리 = max(대열 뒤끝 − generalBack, 앞끝 − 간격 × generalPullBackRows). 장수 이번 틱 피해 × generalGuardSharePct ÷ 100을 generalGuardRange 안 자기 병사가 generalGuardMin명 이상이면 가까운 순(같으면 id) 최대 3명에게 나눔 (나머지는 앞 병사부터 1씩). 도망 = 체력 ≤ generalEscapeHpPct% 또는 (흔들림 · 무너짐이고 ≤ generalEscapeWaverHpPct%). 뒤집힌 맵: 행 ↔ 아래에서 줄 = 같은 수, 우리 배치 = 맵 teamB 자리 | [content/battle-crowd.md](content/battle-crowd.md) 3.17 |
| R-140 | **샘플전투2 공성 1단계**: gate 칸 4방향 덩어리 = 성문 (체력 gateHp), 지키는 편 = 배치 자리가 성문 가운데에 더 가까운 편. 체력 > 0이면 성문 칸은 모두에게 막힘 (부대 가운데도 들어가지 않음). 성을 치는 편 근접 병사가 성문 칸 가운데까지 거리 ≤ 반지름 + 칸 × 3/4 + attackReach면 쿨다운마다 − gateHitDamage (장수 × 2), 0이면 열림 (칸 열고 길 지도 새로). 성을 치는 편 길 찾기는 닫힌 성문 칸을 비용 × gatePlanCost로 지나감. 공성 시험 맵: 부서지기 전 성 안 공격 병사 0, 부서진 뒤 들어감 | [content/battle-crowd.md](content/battle-crowd.md) 3.18 |
| R-141 | **샘플전투2 진형 11개**: single 1줄 · echelonL/R 2줄 × (y + x × ±0.5) · split 4줄 덩이 둘 ± (덩이 폭 ÷ 2 + 덩이 폭 × 0.3) · circle 정수 격자에서 반지름 r(점 수 ≥ 병사 수인 가장 작은 r) 안 점을 거리 큰 순 (같으면 x, y) · loose 간격 × 1.8 3줄 · column 폭 4. 모든 진형: 자리 수 = 병사 수, 무게중심 (0, 0), 자리끼리 간격 × 0.67 이상. 화살 맞을 확률 × formationArrowHitPct[맞는 진형] %, 포위 사기 × formationEncircleDrainPct %, 부대 걸음 × formationSpeedPct %, 피해 %p += formationCounterPp["공격>맞는"] | [content/battle-crowd.md](content/battle-crowd.md) 3.19 |
| R-142 | **샘플전투2 성벽 위 · 장수 자리 · 부상**: 칸 a ↔ b 바로 오감 = 같은 지형 또는 서로의 connects(비면 모두)가 허락 (wallTop.connects = [stairs] → 땅 ↔ 성벽 위 불가, 계단 ↔ 성벽 위 가능; 걸음 · Clear · 다익스트라 · NextCell 모두). 땅에 선 병사의 성벽 위 자리 → 가장 가까운 이어진 칸 (같으면 칸 번호 작은 것). 장수 자리 = 부상 ? 대열 뒤 generalRear : 물러남 ? max(대열 뒤 − generalBack, 앞줄 − 간격 × generalPullBackRows) : 만남(적 부대 가운데가 chargeRange + marchReformRange 안) ? 성향 자리 : 대열 뒤 − generalBack. 원진 = 모두 (0, 0), 종대 = 성향 자리도 대열 뒤 − generalBack. 행군 장수 = 대열 가운데. 물러남 · 부상 장수는 반지름 + 기병 반지름 + 손 닿는 거리 안 적만 치고 자리로 걸음. 부상 = 흔들림 · 무너짐 아님 + 체력 ≤ generalEscapeHpPct%, 도망 = 흔들림 · 무너짐 + 체력 ≤ generalEscapeWaverHpPct%. SetFormation은 그 부대 병사 멈춤을 풂, 멈춘 병사는 목표가 UnsettleDistance 넘게 옮겨지면 풀림 | [content/battle-crowd.md](content/battle-crowd.md) 3.20 |
| R-143 | **샘플전투2 높이 효과**: 높이 = 맵 heights ÷ 3, d = clamp(치는 자리 높이 − 맞는 자리 높이, ±heightMaxLevels). 근접 피해 %p += d × heightMeleePpPerLevel. 궁병은 기본 사거리 × (1 + heightMaxLevels × heightRangePctPerLevel%) 안에서 가장 가까운 적을 찾고, 그 적까지 거리가 기본 × (1 + max(0, d) × heightRangePctPerLevel%) 안이면 쏨. 화살 맞을 확률 × max(0, 100 + d × heightArrowHitPctPerLevel)%. 걸음 (병사 · 부대 가운데): 걸음 방향 한 칸(CellSize) 앞 높이 − 지금 높이 = s, s > 0이면 × (100 − s × uphillSlowPctPerLevel)%, s < 0이면 × (100 + |s| × downhillFastPctPerLevel)%, slopeMinPct ~ slopeMaxPct. 공격 쪽(SiegeDefender ≠ 편) 병사: 자리가 connects 지형(성벽 위)이고 그 병사가 그 지형 위가 아니면 → 가장 가까운 connects 없는 갈 수 있는 칸 (같으면 칸 번호 작은 것), 걸음은 connects 없는 칸 → connects 칸 불가, 길 찾기(openGates)는 connects 칸을 막힘으로 | [content/battle-crowd.md](content/battle-crowd.md) 3.21 |
| R-144 | **샘플전투2 장수 자리 · 회복**: 진형 자리 = Slots(n + 1)에서 장수 설 곳(맹장 (0, 앞줄) · 지장 · 종대 (0, (앞줄 + 뒷줄) ÷ 2) · 원진 (0, 0))에 가장 가까운 자리를 거리 ≤ 간격 × 0.75면 비움 (같으면 번호 작은 것; 책사는 원진 · 종대일 때만). 장수 자리: 맹장 만나기 전 = 빈자리 (없으면 (0, 앞줄 − 간격 ÷ 2)), 만난 뒤 = (0, 앞줄 + generalFront); 지장 = 빈자리 (없으면 대열 가운데); 책사 = (0, 뒷줄 − generalRear); 원진 모두 = 빈자리 또는 (0, 0), 종대 = 빈자리 또는 가운데. 도망 = 무너짐 또는 체력 ≤ generalEscapeHpPct%, 부상 = 체력 ≤ generalWoundedHpPct%. 맹장 물러남 = 체력 ≤ generalPullBackHpPct%; 물러난 동안 마지막으로 맞은 틱에서 generalRegenCalmSeconds 지나면 틱마다 최대 체력 × generalRegenPctPerSec% × 틱초 누적 회복, ≥ generalReturnHpPct%면 선두로. 지장(front가 아닌 무력 ≥ generalFightMight 장수): 쿨다운 0일 때 generalRoamRange 안 가장 가까운 적 중 자리에서 generalRoamRange 안인 적만. 스킬 간격 틱 = round(skillGapTicks × clamp(100 − (지력 − 50) × skillGapIntellectPct, skillGapMinPct, skillGapMaxPct) ÷ 100) | [content/battle-crowd.md](content/battle-crowd.md) 3.22 |
| R-145 | **샘플전투2 공성 명령**: 공성 중 = 성문 ≥ 1 이고 BreachTick < 0. 명령 목록 = 지키는 쪽 (궁병 [wall] · 기병 [brace] · 그 밖 [brace, wall]) / 치는 쪽 (궁병 [shootWall] · 기병 [waitBreach] · 그 밖 [bash, ladder, ram]). 성문 한 방 = gateHitDamage × (장수 2) × 충차배수 × (100 − 막기%) ÷ 100, 1 미만은 Carry에 누적. 막기% = min(gateBraceMaxPct, Σ(성문 + 안쪽 × 1.5칸 둘레 gateBraceRange 안 지키는 병사 gateBraceWeight[병종], 없으면 1) × gateBracePctPerWeight). 충차배수 = 충차 명령이고 성문 gateBraceRange 안 그 부대 병사 ≥ ramCrew면 gateRamMult. 성문이 처음 0 → BreachTick, 모든 공성 명령 지움 (성벽 궁병은 자리 유지), 치는 쪽 부대 OrderAttack(가장 가까운 적). 성벽 자리: 부대 가운데까지 거리 지도에서 닿는 connects 칸을 가장 가까운 적 부대 무게중심 거리 순(같으면 칸 번호), 살아 있는 병사 id 순으로 칸마다 wallPerCell명. 사다리: wall 칸 중 4방향 이웃(발)이 치는 쪽 거리 지도에서 닿고 맞은편이 wallTop, 모든 성문에서 ladderMinGateCells × 칸 넘게 떨어진 것 중 발이 부대 가운데에 가장 가까운 것. 오르기 시작 = 발에서 ladderReach 안 · FreeTick 지남, ladderClimbSeconds 뒤 꼭대기; 꼭대기 ladderGuardRange 안 적 n명이면 min(ladderPushMaxPct, n × ladderPushPct)% 확률로 떨어짐 (최대 체력 × ladderFallDamagePct% 피해). 돌: 성벽 위 지키는 쪽 비궁병, 쿨다운 0 · 맞붙은 적 없음, stoneRange 안 가장 가까운 적이 1단 이상 낮으면, 0.5초 뒤 stoneHitChance로 stoneDamage, 쿨다운 stoneSeconds. 장수 (R-144 고침): 물러남 = 성향과 관계없이 체력 × 100 ≤ 최대 × generalPullBackHpPct (책사 자리는 대열 뒤 그대로), 회복 · 복귀는 R-144대로; 장수 퇴로 = 무너짐 또는 체력 ≤ max(1, 최대 × generalEscapeHpPct ÷ 100), 무너지지 않았으면 그 부대 OrderRetreat; 부상 상태 없음. 공성 명령이 있는 동안 장수 자리는 성향 자리 대신 만나기 전 자리. 근접 상대: 성문이 있는 맵에서 Clear(병사 → 적)가 아니면 맞붙지 않음. 화살 · 돌 맞은 쪽: 쏜 자리가 connects 칸이면 앞(0). 충차 지붕: 공성 중, 충차 명령 부대 · RamManned인 닫힌 성문 gateBraceRange 안 병사는 맞을 확률 × (100 − ramCoverPct) ÷ 100 | [content/battle-crowd.md](content/battle-crowd.md) 3.23 |
| R-146 | **샘플전투2 부대 시작 행동**: CrowdUnitSpec.Behavior (hold 기본 · advance · delegate). 전투를 만든 뒤 delegate → Delegated = true, advance → OrderAdvance. 적 부대는 EnemyAdvance이거나 Delegated면 AI가 지휘 (예전: EnemyAdvance만). 시험 탭은 EnemyAdvance = false, 시작 배속 1 · 2 · 4 | [content/battle-crowd.md](content/battle-crowd.md) 3.24 |
| R-147 | **샘플전투2 부대 퇴각 · 장수 피해**: 장수 살아 있고 퇴로 아님 · 부대 지휘 중이고 살아 있는 병사 × 100 ≤ 처음 × armyRetreatTroopPct → OrderRetreat + 장수 퇴로. 그 밖에 무너짐 또는 장수 체력 ≤ max(1, 최대 × generalEscapeHpPct ÷ 100) → 장수 퇴로만. 장수 받는 피해 % = max(generalGuardMinPct, 100 − (무력 − 30) × generalGuardPerMight ÷ 100). 지장(front · rear 아닌) 자리 = 비워 둔 자리가 있으면 그 자리 (대열 깊이 > 간격 × 2.5이고 가장 가까운 자리가 간격 × 0.75 안), 없으면 (0, 뒷줄 − generalBack) | [content/battle-crowd.md](content/battle-crowd.md) 3.25 |
| R-148 | **샘플전투2 사기 약화 · 이탈 · 퇴각**: 약화 %p = 사기 ≥ moraleLowAt → 0, waverAt ≤ 사기 < moraleLowAt → moraleLowPct × (moraleLowAt − 사기) ÷ (moraleLowAt − waverAt), 사기 < waverAt → moraleLowPct + (moraleWaverPct − moraleLowPct) × (waverAt − max(0, 사기)) ÷ waverAt. 근접 · 화살 피해 %p += −약화(친 부대) + 약화(맞는 부대). 이탈: Tick % round(desertSeconds ÷ 틱) == 0일 때 흔들리는(사기 ≤ waverAt) 퇴각 아닌 부대마다 n = max(1, round(남은 병사 × (desertMinPct + (desertMaxPct − desertMinPct) × clamp((waverAt − 사기) ÷ waverAt, 0, 1)) ÷ 100))명, 후보 = 살아 있고 장수 아니고 이탈 아니고 마지막 공격이 attackTicks × 2 넘게 전, (자리 − 부대 가운데)·앞 방향이 작은 순 (같으면 id). 퇴각 = 남아 싸우는 병사(이탈 빼고) × 100 ≤ 처음 × armyRetreatTroopPct (사기 routAt 없음). 물러난 장수 자리 = (0, 뒷줄 − generalBack), 책사는 (0, 뒷줄 − generalRear) | [content/battle-crowd.md](content/battle-crowd.md) 3.26 |
| R-64 | **결제 상품 받기**: 행동 `store.purchase`(ProductId, Receipt). 상점 상품(가격 있음)이 아니거나 영수증이 비면 거절. 반영 = grants 자원 더하기 + 광고 제거(adFreeDays 0 = 영구, 아니면 끝나는 시각 = max(지금 끝, 행동 시각) + 일수 × 86400). 서버는 영수증을 먼저 검사: 결제가 아니면 통과, 가짜 영수증(`fake:`)은 서버 설정이 허락할 때만, 같은 영수증은 한 번만 | [content/economy.md](content/economy.md) 4장 |
| R-39 | **부대 창설·해산**: 창설은 기본 병종(1단계)만, 보유 부대 수 상한(`campaign.json` `units.maxCount`)까지, 병력 0·훈련도 0·결속 0으로 생긴다 (징병·모병으로 채움). 해산은 기본·상황별 어느 편성에도 없는 부대만, 병력·부상병은 사라진다 | [formation.md](content/formation.md) 4.1 |
| R-36 | **명령 예약**: 예약 명령은 지금 명령이 대기면 바로, 아니면 뒤에 (상한 넘으면 거절). 지금 명령이 끝나면(도착·대상 사라짐·지점 스킬 사용) 차례로 꺼내고, 받을 수 없는 예약은 건너뛴다. 예약이 아닌 새 명령은 예약을 비운다 | [battle.md](content/battle.md) 4.3.5·4.3.6 |
| R-37 | **협공 대기**: 같은 팀에서 협공 대기 중이거나 예약에 협공 대기가 있는 군단이 모두 협공 대기에 들어서면 함께 공격 (정한 적 또는 진군). 혼자 `coordinateWaitSec` 기다리면 한 번 묻고, `coordinateResponseSec`가 더 지나면 단독 공격. 다시 협공 대기를 내리면 시간을 다시 센다 | [battle.md](content/battle.md) 4.3.4 |
| R-38 | **휴식**: 휴식 중 싸우지 않으면 사기·책략 점수 회복 × 배율. 교전·피격하면 보통 대기로 바뀐다 | [battle.md](content/battle.md) 4.3.4 |
| R-35 | **출진 편성 선택**: 출진할 때 스텝 전장 유형(야전·성새전·수상전 …)의 상황별 편성이 있으면 그것, 없거나 비어 있으면 기본 편성. 상황별 편성은 기본 편성을 복사해 만들고, 지우면 다시 기본 편성을 쓴다. 기본 편성은 지울 수 없고 빈 편성은 저장되지 않는다 | [formation.md](content/formation.md) 4장 |
| R-34 | **역할 진군**: 플레이어 진군 명령에서 선봉(역할 없음 포함)은 가장 가까운 적으로, 측면은 기준선(아군 중심→적) 자기 쪽 옆으로 벌린 자리로 갔다가 가까워지면 돌아들고, 후위는 적과 `rearStandoffCells` 거리를 두고 멈춘다. AI의 진군은 역할과 무관 | [battle.md](content/battle.md) 4.3.2 |
| R-33 | **병력 보충**: 징병(군량)·모병(돈)은 부대 최대 병력까지만 (병력 + 부상병 ≤ `troopCap.unitMax`). 남은 자리보다 많이 요청하면 남은 자리만큼, 비용이 모자라면 아무것도 바뀌지 않는다 | [formation.md](content/formation.md) 4.1 |
| R-32 | **전투 기록**: 전투가 끝나면 아군 군단에 일어난 전투 이벤트마다 그 장수의 카운터 `battle.<이벤트 종류>`가 1 오른다 (적 군단 이벤트는 세지 않는다). 숨은 적 발견(`hiddenFound`: 간파를 쓴 군단, 또는 복병 준비를 들킨 경우 가장 가까운 적 군단), 복병 기습 당함(`ambushed`: 닿은 적 군단마다) | [generals.md](content/generals.md) 4장 |

**N-11** 고용: 등갑병 8,000명 등록, 내 군량으로 3분 동안 4,000명까지 먹일 수 있음, 장수 한도에 5,000명 남음 → 최대 **4,000명** 고용.

**N-12** 1명당 가격 2, 4,000명 고용 → 비용 **8,000**.

**N-13** 복병 스킬북(초급) 300돈, 지략 62 장수, 돈 1,000: 사면 돈 **700**. 복병 기습을 당한 적도, 숨은 적을 찾은 적도 없으면 **못 배움**(책 보관) → 복병 기습을 1번 당한 뒤 **배움**, 책 0권.

---

## 8.1 테스트 위치

- R·N 예시는 `tests/Game.Tests/VerificationRuleTests.cs`(R), `VerificationNumericTests.cs`(N)에 같은 번호로 있다.
- 전투 시뮬레이션 규칙(R-16, R-17 전장 확인, R-20, R-6 전투 중 소비, R-29 리플레이)은 `tests/Game.Tests/Battle/BattleSimulationTests.cs`.
- 시야·안개·복병·스킬(R-11~R-13 전투 안, N-10 간파 부분, R-29 스킬 리플레이)은 `tests/Game.Tests/Battle/VisionSkillTests.cs`.
- 대결(일기토·논쟁, R-21~R-23)과 군단 AI는 `tests/Game.Tests/Battle/AiContestTests.cs`.
- 포로(포위·일기토·스위치)·바람·날씨·시간대·화계는 `tests/Game.Tests/Battle/EnvironmentCaptureTests.cs`.
- 캠페인 흐름(출진 R-7, 결과 반영 R-8·R-9, 포로·등용·부상·성장·결속·스텝 해금)은 `tests/Game.Tests/Campaign/CampaignFlowTests.cs`.
- 결속·스텝 연출·전투 이벤트·말풍선은 `tests/Game.Tests/Battle/EventSpeechTests.cs`, 장수 레벨·숨은 보상은 `tests/Game.Tests/Campaign/LevelSecretTests.cs`.
- 서버 전투 검증(R-40)은 `tests/Game.Server.Tests/ServerTests.cs` (참고 서버 HTTP).
- 서버 행동 적용(R-41)은 `tests/Game.Tests/Campaign/PlayerActionTests.cs`(가짜 서버), HTTP는 `ServerTests.Actions`.
- 기록 시각·오프라인 기록(R-42)은 `tests/Game.Tests/Campaign/JournalTests.cs`, HTTP는 `ServerTests.BattleTimeWindow`.
- 부대 창설·해산(R-39)은 `tests/Game.Tests/Campaign/UnitRosterTests.cs`.
- 명령 예약·협공 대기·휴식(R-36~R-38)은 `tests/Game.Tests/Battle/OrderQueueTests.cs`, 화면 입력(예약 모드)은 `BattleControlTests`.
- 상황별 편성·세이브 v4(R-35)는 `tests/Game.Tests/Campaign/FormationPresetTests.cs`.
- 역할 진군(R-34)은 `tests/Game.Tests/Battle/RoleAdvanceTests.cs`.
- 편성 편집(R-5 저장 검사)·병력 보충(R-33)·치료(R-10)는 `tests/Game.Tests/Campaign/FormationEditorTests.cs`.
- 스킬북 습득·전투 기록(R-31, R-32, N-13)은 `tests/Game.Tests/Campaign/SkillBookTests.cs`, 이벤트 발생 자체는 `Battle/VisionSkillTests.cs`.
- 장수 모집(R-51)은 `tests/Game.Tests/Campaign/GachaTests.cs`, 부대 키우기(R-52)는 `TroopGrowthTests.cs`, 숨은 보상(R-53)은 `LevelSecretTests.cs`, 전투 녹화(R-54)·최근 자동 보관(R-101)은 `ReplayTests.cs`, 전투 이어하기(R-55)는 `BattleResumeTests.cs`, 규율·용맹(R-56)은 `tests/Game.Tests/Battle/DisciplineValorTests.cs`, 한계 돌파(R-57)·무료 모집(R-100)은 `GachaTests.cs`, 병종 태그 효과(R-58)는 `tests/Game.Tests/Battle/TagEffectTests.cs`, 연습 대결(R-59)은 `PracticeTests.cs`, 진군·후퇴(R-60)·곧은 진군의 역할(R-63)은 `BattleControlTests.cs`, AI 진군(R-65)은 기준 전투·`StepBalanceTests`, 부상병 자연 회복·보충(R-61)은 `WoundedRecoveryTests.cs`·`CampaignPlaythroughTests.cs`(새 게임 위임 출진 + 회복·보충으로 모든 스텝 클리어), 처음 안내(R-62)는 `GuideTests.cs`, 포로 처리(R-66)는 `PrisonerActionTests.cs`, 장수 특성(R-67)은 `tests/Game.Tests/Battle/TraitTests.cs`, 장수 합류(R-68)는 `GeneralJoinTests.cs`, 예약 검사·복병 지점(R-69)·예약 고치기(R-70)는 `BattleControlTests.cs`, 전투 중 일기토(R-71)는 `tests/Game.Tests/Battle/InBattleDuelTests.cs`, 진형 특성치(R-72)는 `FormationStatTests.cs`, 전열·후열(R-73)은 `FrontLineTests.cs`, 소부대 자율 판단(R-74)은 `UnitAutonomyTests.cs`, 난이도·협공(R-75)은 `DifficultyTests.cs`, 2차 스킬(R-76)은 `SkillTier2Tests.cs`, 수비·휴식 기습·지휘 범위(R-77)는 `HoldRestCommandTests.cs`, 승리 조건·맵 오브젝트(R-78)는 `ObjectiveTests.cs`, 적이 거는 일기토·AI 2차 스킬(R-79)은 `AiChallengeTests.cs`, 계략 4종(R-80)은 `StratagemTests.cs`, 3·4차 스킬(R-82)은 `Tier3SkillTests.cs`, 공병·투석기(R-83)는 `EngineeringTests.cs`, 둑·수공(R-84)은 `FloodTests.cs`, 수상전(R-85)은 `NavalTests.cs`, 녹화 공유(R-81)는 `SharedReplayTests.cs`·서버 `ServerTests.ShareReplay`, 마스터·개발 행동(R-86)은 `DevActionTests.cs`·서버 `ServerTests.MasterOnlyDevActions`, 샘플전투·맵 목록(R-87)은 `SampleBattleTests.cs`, 접전 중 빼기(R-88)는 `tests/Game.Tests/Battle/DisengageMoveTests.cs`, 마주침 일기토(R-89)는 `EncounterDuelTests.cs`, 장수 탈것(R-90)은 `tests/Game.Tests/Campaign/MountTests.cs`, 일기토 신청·응답(R-91)은 `EncounterDuelTests.cs`, 진형 전환 이동·퇴각 선택(R-92·R-93)은 `ReformRetreatTests.cs`, 고르게 보충(R-61)은 `WoundedRecoveryTests.cs`, 결제(R-64)는 `PurchaseTests.cs`·서버 `ServerTests.Purchase`, 새 명령(R-103)·위임 넘김(R-106)·근접 공격(R-107)은 `tests/Game.Tests/Battle/NewCommandTests.cs`, 치장(R-108)은 `tests/Game.Tests/Campaign/CosmeticTests.cs`, 깃발 장수 여럿 · 회수(R-109)는 `TerritoryFlagTests.cs`, 패배 팁(R-104)은 `DefeatAdviceTests.cs`, 용병 등록 서버 검사(R-110)는 `HiringTests.RegisterLimits`·`TerritoryFlagTests.AvatarOnFlagBlocksHire`·`HireDraftTests.FakeMarket`·서버 `ServerTests.HireFlow`, 치장 2단계(R-111)는 `CosmeticRewardTests.cs`, 출석 · 교환 · 금괴 해금(R-112)은 `DailyEconomyTests.cs`·서버 `ServerTests.AttendanceFlow`, 공성전 준비(R-113)는 `TerritoryFlagTests.SiegePrep`·서버 `ServerTests.SiegePrepFlow`, 추가 파견(R-115)은 `TerritoryFlagTests.Reinforce`, 영수증 검증기(R-114)는 `PurchaseTests.ReceiptVerifiers`·서버 `ReceiptVerifierTests`, 구글 영수증 · 결제 기록(R-116)은 서버 `GooglePlayTests`, 결제 복원(R-117)은 `PurchaseTests.RestoreAction`·`RestoreCheck`·서버 `ServerTests.RestorePurchase`, 버전 · 점검(R-118)은 `AppStatusTests.cs`·서버 `AdminTests.StatusNotice`, 푸시(R-119)는 `tests/Game.Tests/Territory/PushRulesTests.cs`·서버 `PushTests`, 고용된 전투 녹화(R-120)는 `HiringTests.HiredBattles`·서버 `ServerTests.HireFlow`, 기간 배너(R-121)는 `GachaTests.PeriodBanner`, 운영 켜고 끄기(R-122)는 `GachaTests.Toggles`·서버 `AdminTests.BannerToggles`, 상점 상품 표출(R-123)은 `PurchaseTests.ShopToggles`·서버 `AdminTests.ProductToggles`, 결제 검증 덮어쓰기(R-124)는 서버 `AdminTests.ReceiptOverride*`·`FirestoreStoreTests.ReceiptOverride*`, 샘플전투2 병사 단위 전투(R-125) · 조작 · 돌격 · 부딪침(R-126) · 병종 · 상성(R-127) · 사기 · 퇴각(R-128) · 명령 예약(R-129) · 스킬(R-130) · 3×3 배치(R-131) · 조작 명령(R-132) · 지형(R-133) · 막는 소품(R-134) · 맵(R-135) · 행군(R-136 · R-137) · 장수(R-138 · R-139) · 공성(R-140) · 진형(R-141) · 성벽 위 · 장수 부상(R-142) · 높이(R-143) · 장수 자리 · 회복(R-144) · 공성 명령(R-145) · 시작 행동(R-146) · 부대 퇴각(R-147) · 사기 약화 · 이탈(R-148)은 `tests/Game.Tests/Battle/CrowdBattleTests.cs`.
- 좌표 → 지역(R-45)은 `tests/Game.Tests/TerritoryTests.cs`, 영토 전투·차지(R-46)는 `Campaign/TerritoryBattleTests.cs`, 수입(R-47)은 `Campaign/TerritoryIncomeTests.cs`, 수비 편성(R-48)은 `Campaign/TerritoryDefenseTests.cs`, 연합(R-49, R-50)은 `Campaign/AllianceTests.cs`, HTTP는 `ServerTests.TerritoryCapture`.
- 고용(R-24~R-27 등록·가격·동시 고용, R-43)은 `tests/Game.Tests/Campaign/HiringTests.cs`, 제한·이력(R-44)은 `HireBookTests.cs`, HTTP는 `ServerTests.HireFlow`.

## 9. 기준 전투

- 고정 시드·편성·명령 기록으로 돌린 결과(승패, 군단별 남은 병력·손실·군량·사기, 소부대별 병력, 경과 시간, 상태 지문)를 **정답 파일**로 저장한다: `tests/Game.Tests/Golden/*.json`, 테스트 `GoldenBattleTests`.
- 다시 구현했거나 코드를 고쳤을 때 같은 결과가 나오면 규칙 면에서 같은 게임이다.
- 밸런스를 **의도적으로** 바꿨으면 정답 파일을 새로 저장하고(`UPDATE_GOLDEN=1 dotnet test`), 바뀐 내용을 PR에서 확인한다. 의도하지 않은 변화면 버그다.

| 기준 전투 | 확인하는 것 | 상태 |
|---|---|---|
| `plain_mirror` 평원 1:1 같은 편성 | 대칭이면 무승부 근처 (남은 병력 차 1% 이하) | 있음 |
| `formation_fishScale_vs_craneWing` 진형 상성 1쌍 | 상성대로 승패 | 있음 |
| `supply_shortage_retreat` 군량 부족 | 군량 바닥 → 사기 하락 → 먼저 퇴각 | 있음 |
| `forest_infantry_vs_cavalry` 숲 | 지형 전투 보정으로 보병 승 (평원에서는 기병 승) | 있음 |
| `mountain_2v2_commands` 2:2 혼성 + 플레이어 명령 | 명령 기록 재생, 산 우회, 대장 패배 조건 | 있음 |
| `forest_ambush_no_reveal` / `forest_ambush_with_reveal` 숲 복병 vs 간파 미사용 / 사용 | 간파 안 하면 기습(혼란·사기↓)으로 더 빨리 무너지고 복병 쪽 손실이 적다 | 있음 |
| `duel_before_battle` 전투 직전 일기토 (AI 선택) | 대결 결과(퇴각·쓰러짐)가 사기·혼란·퇴각·부상으로 전투에 반영 | 있음 |
| `ai_skills_normal` 스킬을 쓰는 AI끼리 (보통) | AI 스킬 판단·상성 대응이 결정적으로 재현 | 있음 |
| `tailwind_fire_attack` 순풍 화계 | 숲 안 적의 바람 위쪽에 불 → 바람 방향으로 번져 적만 피해, 숲 → 불탄 땅 | 있음 |

---

## 10. 변경 기록

| 버전 | 날짜 | 바뀐 규칙 |
|---|---|---|
| v1 | 2026-11-03 | 최초 (`design-v1` 태그) |
| v1.1 | 2026-11-09 | R-31 스킬북 습득 최소 조건, R-32 전투 기록 카운터, N-13 추가 |
| v1.2 | 2026-11-09 | R-33 병력 보충 상한 추가 |
| v1.3 | 2026-11-09 | R-34 역할 진군 추가 |
| v1.4 | 2026-11-09 | R-35 상황별 편성 선택 추가 |
| v1.5 | 2026-11-09 | R-36 명령 예약, R-37 협공 대기, R-38 휴식 추가 |
| v1.6 | 2026-11-09 | R-39 부대 창설·해산 추가 |
| v1.7 | 2026-11-09 | R-40 서버 전투 검증 추가 |
| v1.8 | 2026-11-09 | R-41 서버 행동 적용 추가 (세이브 통째 올리기는 처음 가져오기만) |
| v1.9 | 2026-11-09 | R-42 기록 시각·오프라인 기록 나중에 올리기 추가 |
| v1.10 | 2026-11-09 | R-43 고용 출진·서버 재현·등록자 우편 보상 추가 |
| v1.11 | 2026-11-09 | R-44 고용 제한(하루 고용 수·대기시간)·이력 추가 |
| v1.12 | 2026-11-09 | R-45 좌표 → 지역·공격 가능 지역 추가 |
| v1.13 | 2026-11-09 | R-46 영토 전투·차지 추가 |
| v1.14 | 2026-11-09 | R-47 영토 수입 추가 |
| v1.15 | 2026-11-09 | R-48 수비 편성 추가 |
| v1.16 | 2026-11-09 | R-49 연합 만들기·가입·탈퇴 추가 |
| v1.17 | 2026-11-09 | R-50 연합 관리(가입 신청·수락·내보내기) 추가 |
| v1.18 | 2026-11-09 | R-51 장수 모집 추가 |
| v1.19 | 2026-11-09 | R-52 부대 키우기(훈련·승급·훈련도 보정) 추가 |
| v1.20 | 2026-11-09 | R-53 숨은 보상(조건·받는 장수·보관·한 번만·도감) 추가 |
| v1.21 | 2026-11-09 | R-54 전투 녹화(저장·재생·버전 확인) 추가 |
| v1.22 | 2026-11-09 | R-55 전투 이어하기 추가 |
| v1.23 | 2026-11-09 | R-56 규율·용맹 1차 추가 (기준 전투 3개 갱신: 진형 전환 시간) |
| v1.24 | 2026-11-09 | R-57 한계 돌파 추가 |
| v1.25 | 2026-11-09 | R-58 병종 태그 효과 추가 |
| v1.26 | 2026-11-10 | R-59 연습 대결 추가 |
| v1.27 | 2026-11-10 | R-60 진군·후퇴를 행위 그대로 (직진·뒤로 곧게), 퇴각(이탈) 분리 |
| v1.28 | 2026-11-10 | R-61 부상병 자연 회복·편성 한도까지 보충 추가 |
| v1.29 | 2026-11-10 | R-62 처음 안내 추가 |
| v1.30 | 2026-11-10 | R-63 곧은 진군에 역할 반영 (측면 벌리기, 후위 지원 이동) |
| v1.31 | 2026-11-10 | R-64 결제 상품 받기 (임시 테스트 가격, 서버 영수증 검사) |
| v1.32 | 2026-11-11 | R-63 측면도 곧게 (벌리기 뺌), R-65 AI 진군은 자기 줄로. 기준 전투 2개 갱신, 스텝 적 병력·군량 보상 조정 |
| v1.33 | 2026-11-11 | R-66 포로 처리 행동 (등용 시도·해방) |
| v1.34 | 2026-11-11 | R-67 장수 특성 효과 (스텝 적 편성·특성 수치 조정) |
| v1.35 | 2026-11-11 | R-68 장수 합류 보상 (step03·05·07·09·11·12), 새 장수는 기본 스킬·특성과 함께 |
| v1.36 | 2026-11-11 | R-69 예약 스킬의 조건은 꺼낼 때, 복병 지점 지정 (이동 후 매복) |
| v1.37 | 2026-11-11 | R-70 예약 고치기(취소·옮기기), 길 막힘 알림 |
| v1.38 | 2026-11-12 | R-71 전투 중 일기토 (스킬 challenge) |
| v1.39 | 2026-11-12 | R-72 진형 특성치, R-61 보충을 가장 적은 부대부터 고르게 (스텝 03·05·07·10·11 적 편성 조정) |
| v1.40 | 2026-11-12 | R-73 전열과 후열 (스텝 02·05·06·10·11 적 편성 조정) |
| v1.41 | 2026-11-12 | R-74 소부대 자율 판단 (스텝 03·05·07·09·11 적 편성 조정) |
| v1.42 | 2026-11-12 | R-75 전체 난이도 설정·어려움(사령관 협공) |
| v1.43 | 2026-11-12 | R-76 2차 스킬 6종 (혼란·철벽·진형 급변·집중 사격·화전 일제사격·척후 파견), 재정비 범위 |
| v1.44 | 2026-11-12 | R-77 수비 방어·휴식 기습·지휘 범위 (step05 조정) |
| v1.45 | 2026-11-12 | R-78 승리 조건(버티기·구역 점령·군량 소진)·맵 오브젝트(보급소·망루) |
| v1.86 | 2026-11-17 | R-122 운영 켜고 끄기 (모집 배너, 관리자 · 공개 /content) |
| v1.87 | 2026-11-17 | R-123 상점 상품 표출 (관리자 [상점 상품], 데이터 기본 끔, 꺼진 상품 구매 거절) |
| v1.88 | 2026-11-20 | R-124 결제 검증 덮어쓰기 (관리자 [상점 상품] → 결제 검증, ALLOW_ADMIN_RECEIPT로 출시 서버 잠금) |
| v1.89 | 2026-11-21 | R-125 샘플전투2 병사 단위 전투 (시험 중, 기존 전투와 따로) |
| v1.90 | 2026-11-21 | R-126 샘플전투2 부대 셋 · 이동 · 공격 명령 · 돌격 · 부딪침 |
| v1.91 | 2026-11-21 | R-127 샘플전투2 병종 · 상성 (창 · 궁 · 기병, 화살, 받아치기), 병사 체력 10 |
| v1.92 | 2026-11-21 | R-128 샘플전투2 사기 · 옆 · 뒤 · 포위 · 흔들림 · 무너짐(도망) |
| v1.93 | 2026-11-21 | R-129 샘플전투2 명령 예약 (경유 지점) · 조작 화면 (이름표 · 미니맵 · 원형 메뉴) |
| v1.94 | 2026-11-21 | R-130 샘플전투2 스킬 다섯 (함성 · 고무 · 철벽 · 강행군 · 화살비) |
| v1.95 | 2026-11-21 | R-131 샘플전투2 3×3 배치 · 테스트 탭 |
| v1.96 | 2026-11-21 | R-132 샘플전투2 조작 명령 (위임 · 진군 · 근접 공격 · 후퇴 · 경로) · 조작 화면 아래 3단 |
| v1.97 | 2026-11-22 | R-133 샘플전투2 지형 1단계 (맵 60 × 100, 이동 비용 · 못 가는 땅 · 길 찾기 · 지형 전투 보정) |
| v1.98 | 2026-11-22 | R-134 샘플전투2 나무 · 바위가 병사를 막음, 테스트 탭 기본 맵 평원 |
| v1.99 | 2026-11-22 | R-135 샘플전투2 맵 7개 (48 × 80, 높이 층, tools/crowd_maps.js) |
| v2.00 | 2026-11-22 | R-136 샘플전투2 자동 행군 대열 · 자취 따라가기 · 장수 바위 틈, 큰 강 왼쪽 넓힘 |
| v2.01 | 2026-11-22 | R-137 막힌 병사 지름길 지도, 행군은 [행군 이동]으로만 (자동 행군 기본 끔) |
| v2.02 | 2026-11-22 | R-138 샘플전투2 장수 성향 (맹장 · 장수 · 책사: 자리 · 버티기 · 도망) · 병종 적성 %p |
| v2.03 | 2026-11-22 | R-139 장수 호위 · 선봉 물러남 · 도망 기준, 공수 교대(맵 뒤집기), 부대 하나면 [전체] 숨김 |
| v2.04 | 2026-11-22 | R-140 샘플전투2 공성 1단계 (성벽 · 성벽 위 · 계단 · 성문 부수기), 공성 시험 맵 |
| v2.05 | 2026-11-22 | R-141 샘플전투2 진형 11개 (1줄 · 사선 · 분진 · 원진 · 산개 · 종대 추가) · 진형 특성 · 상성 표 자리, 아이콘 |
| v2.06 | 2026-11-22 | R-142 성벽 위는 계단으로만, 장수 자리는 적과 만난 뒤 성향대로 (원진 = 가운데), 장수 부상 (부대가 버티면 맵 끝까지 안 감), 진형 바로 바꾸기 |
| v2.07 | 2026-11-23 | R-143 샘플전투2 높이 효과 (내려치기 · 높은 곳 궁병 사거리 · 명중, 비탈 걸음) |
| v2.08 | 2026-11-23 | R-144 샘플전투2 장수 선두 · 중위 · 후위 (원진 · 종대 가운데, 장수 빈자리), 도망 5%, 맹장 물러나 회복, 지장 한 대씩, 책사 스킬 간격 |
| v2.09 | 2026-11-23 | R-145 샘플전투2 공성 명령 (성벽 배치 · 문 막기 · 성문 부수기 · 충차 · 사다리 · 성벽 쏘기 · 돌입 대기), 돌, 성문 500 · 막기 40%, 부서지면 야전 명령. 장수 20%에 물러나 회복 · 5%면 부대 후퇴 (부상 상태 없앰) |
| v2.10 | 2026-11-23 | R-146 시험 탭 시작 배속 · 적 부대별 시작 행동 (위임 · 진군 · 대기) |
| v2.11 | 2026-11-24 | R-147 부대 퇴각 = 병력 5% (장수 체력 5%는 장수만), 이름표 장수 체력 막대 · 맨 위 아장 체력, 장수 피해 감소 낮춤, 얇은 대열 지장은 뒤, 물러남 30% |
| v2.12 | 2026-11-24 | R-148 사기로 무너지지 않음 — 약화 · 이탈자, 퇴각은 병력 5%, 물러난 장수는 대열 뒤 |
| v1.85 | 2026-11-17 | R-121 기간 배너 (startsAt · endsAt, 기간 밖 모집 거절, n일 남음) |
| v1.84 | 2026-11-16 | R-120 내 용병이 고용된 전투 녹화 (서버가 떠 둠, 등록자 · 고용한 사람만) |
| v1.83 | 2026-11-16 | R-119 푸시 알림 서버 쪽 (설정 · 토큰, 공성전 · 연합 · 수입 가득, IPushSender) |
| v1.82 | 2026-11-16 | R-118 버전 확인 · 점검 안내 (/v1/status, 관리자 값) |
| v1.81 | 2026-11-16 | R-117 결제 복원 (영구 상품만, 같은 계정이면 지급 없이 Ok, 다른 계정 거절) |
| v1.80 | 2026-11-16 | R-116 Google Play 영수증 실제 검증 · 결제 기록(저장소) · 반영 뒤 확인 · 소모 |
| v1.79 | 2026-11-16 | R-115 깃발 추가 파견 (바꾸기 = 추가 파견 + 회수) |
| v1.78 | 2026-11-15 | R-114 영수증 검증기 뼈대 (fake · off · googlePlay 자리) |
| v1.77 | 2026-11-15 | R-113 공성전 준비 1분 · 수비자 알림 · 준비 중 취소 |
| v1.76 | 2026-11-15 | R-112 출석 · 교환 · 금괴 스킬 해금 (구조 + 임시값, 선호 구매는 미결) |
| v1.75 | 2026-11-15 | R-111 치장 2단계 (스텝 · 숨은 보상 · 결제 치장 상품, 중복이면 엽전, 용병 · 수비 스냅샷에 주인 겉모습) |
| v1.74 | 2026-11-15 | R-110 용병 등록 서버 검사 (깃발에 나간 닉네임 장수 거절, 부대는 닉네임 장수 군단의 병종 칸만) |
| v1.73 | 2026-11-15 | R-109 깃발 장수 여럿 · 회수 (모두 회수하면 깃발 거두기) |
| v1.72 | 2026-11-15 | R-108 치장 1단계 (갑옷 한 벌 · 말 색, 엽전으로 사기 · 입히기, 겉모습만) |
| v1.71 | 2026-11-15 | R-106 위임 켜면 바로 AI 지휘, R-107 근접 공격 |
| v1.70 | 2026-11-15 | R-105 구글 연동 · 진짜 로그아웃 (게스트 경고) |
| v1.69 | 2026-11-15 | R-104 패배 팁 (defeatTips.json), 보고 ExpGains |
| v1.68 | 2026-11-15 | R-103 새 명령 (돌격 · 사격만 · 유인 · 은밀 · 지키기 · 엄호, 일제 공격 · 집결 · 경로) |
| v1.67 | 2026-11-15 | R-102 계정 삭제 (7일 유예, 다시 들어오면 취소, 지나면 모두 지움) |
| v1.66 | 2026-11-15 | R-101 최근 녹화 자동 보관 (recentKeep, 저장하면 옮김, 분류) |
| v1.65 | 2026-11-15 | R-100 무료 모집 (하루 freeDaily번, 천장 셈) |
| v1.64 | 2026-11-15 | R-99 용병 배신(첫 교전 직후 적 편 · AI, 고용비 돌려받음, 등록자 보상 없음) |
| v1.63 | 2026-11-15 | R-98 닉네임 장수(avatar) · 용병 장수 등록은 닉네임 장수만 · 고용 시 avatar_hired |
| v1.62 | 2026-11-15 | R-96 영토 깃발 꽂기(칸 · 거리 · 계급 반지름 · 장수 · 겹침 · 남의 원 안 = 공성전), R-97 깃발 공성전(보호 시간 · 깃발 교체) · 깃발 수입 |
| v1.61 | 2026-11-14 | R-95 군단 편성 3×3 포진판(자리 → 역할·시작 위치), 총대장 ★, 자동 편성, 편성 복사 |
| v1.60 | 2026-11-14 | R-94 군대 개편: 병종 묶음, 계급(계정 레벨)으로 군단·칸·최대 병력, 새 병사 평균, 진형 칸 자동 배치. R-5·R-33·R-39·R-61은 R-94 기준으로 (편성 = 병종 칸, 최대 병력 = 계급, 창설 = 없는 병종의 묶음, 보충 = 편성 인원까지) |
| v1.59 | 2026-11-14 | R-52 승급을 인원 갈라 내기로 (부분·전체 승급, 기본 부대는 남음, 비용은 인원 비례) |
| v1.58 | 2026-11-14 | R-92 진형 전환 중 자리 옮기기·전환 중 공격 −10%p, R-93 퇴각 선택(퇴각/버티기, 까닭). 기준 전투 3개 다시 저장 (forest_ambush 2개: 퇴각 묻기 8초, mountain_2v2_commands: 진형 전환 이동) |
| v1.57 | 2026-11-13 | R-91 일기토는 신청·응답으로 (마주침 기회, 모든 장수 신청 가능, 성향대로 응함/거절, 거절 벌칙). 재조정 step04 ×0.96·step06 ×0.846·step10 ×0.752·step12 ×0.93, 숨은 보상 step04 손실 5% → 8.5% |
| v1.56 | 2026-11-13 | R-90 장수 탈것 (말·가마, 보기만) |
| v1.55 | 2026-11-13 | R-88 접전 중 빼기 (플레이어 보통 이동), R-89 일기토를 전투 중 대장끼리 마주칠 때로 (step04 ×1.05, step12 ×0.9, 숨은 보상 step06 손실 10% → 17%) |
| v1.54 | 2026-11-13 | R-87 샘플전투(세이브 사본, 결과 없음)·맵 목록 |
| v1.53 | 2026-11-13 | R-86 마스터 계정·개발 행동 (자원 지급·전체 해금·장수 주기) |
| v1.52 | 2026-11-13 | R-85 수상전 (물·수군·난전·화선·충각·수군 훈련) |
| v1.51 | 2026-11-13 | R-84 둑·수공 (물 → 진흙 → 원래), step07 서쪽 둑 |
| v1.50 | 2026-11-13 | R-83 공병·투석기 병종, 목책·다리 파괴·집중 포격 |
| v1.49 | 2026-11-12 | R-82 3·4차 스킬 9종 (강행군·은밀 행군·선두 돌파·이간·유언비어·반간·군량 습격·군량 보급·응급 치료) |
| v1.48 | 2026-11-12 | R-81 녹화 공유 (공략): 서버 검증·고르기 |
| v1.47 | 2026-11-12 | R-80 계략 4종 (함정·낙석·위장 깃발·거짓 퇴각) |
| v1.46 | 2026-11-12 | R-79 적이 거는 일기토(플레이어가 응함/거절)·AI 2차 스킬 판단, 일기토 신청 무력 60 → 55 |
