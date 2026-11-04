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
├─ Game.Battle.Model/      피해 계산, 사기 보정, 책략 점수, 지형 전투 보정, 화면 병사 비율
├─ Game.Campaign/          보유 장수·부대, 편성 검사, 병력 한도, 손실 분배, 초기 지급, 세이브(버전·변환기)
├─ Game.Economy/           지갑, 군량 계산, 치료 비용
└─ Game.Online/            부대 고용 계산
tests/Game.Tests/          단위·규칙(R)·수치(N)·데이터 검사·의존 방향 테스트
data/                      규칙 데이터(Rules/)와 테마 데이터(Themes/<테마>/) — Unity에서는 StreamingAssets로 복사
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

## 5. Step 1a에서 아직 하지 않은 것 (Step 2 이후)

- 전투 시뮬레이션 자체 (이동·교전·시야·스킬 효과·AI), 일기토·논쟁 엔진 → R-16, R-20~R-23, R-26, 기준 전투
- 스킬·특성·진형 배치 데이터의 전체 정의 (지금은 id만 사용)
- 실제 외부 서비스 구현 (Unity·서버 쪽 어셈블리에서 등록)
