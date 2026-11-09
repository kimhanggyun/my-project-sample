# 서버 기반 (백엔드와 무관한 부분)

> 상태: 기반 구현 (2026-11-09). 실제 백엔드 선택(Unity Gaming Services / PlayFab / 자체 서버)은 미정 — [unity/decisions.md](unity/decisions.md).
> 관련: [../design/structure.md](../design/structure.md) 5장(경계), [../design/content/hiring.md](../design/content/hiring.md), [../design/content/secrets.md](../design/content/secrets.md) 4장(공정성), [../design/content/territory.md](../design/content/territory.md)

---

## 1. 원칙

- **세이브는 서버가 주인이다.** 기기는 서버 세이브를 받아 쓰고, 전투 결과는 서버가 검증·반영한 세이브가 정답이다.
- **전투는 서버가 다시 돌려 확인한다** (verification R-40). 전투가 결정적이라(R-29) 같은 입력이면 기기와 서버가 같은 지문을 낸다.
- **백엔드와 무관한 규칙은 순수 C#** 으로 `src/`에 둔다. 백엔드가 정해지면 그쪽(예: Cloud Code C# 모듈, Azure Functions)에서 같은 코드를 부른다.
- 기기 쪽은 `IOnlineService` 경계 뒤 (off / fake / 실제). **꺼도 게임은 기기 세이브로 동작**한다 (dev-rules 4장).

## 2. 코드 위치

| 위치 | 내용 |
|---|---|
| `src/Game.Online/OnlineContracts.cs` | 메시지: `SignInResponse`, `ServerTimeResponse`, `CloudSave`(Revision·세이브 JSON), `SavePutRequest/Response`, `BattleSubmission`, `BattleVerdict`. 경계 `IOnlineService`와 서버 없음 구현 `OfflineOnlineService`. JSON 설정 `OnlineJson` (Fix는 원본 정수) |
| `src/Game.Flow/PlayerActions.cs` | 전투 밖 행동(`PlayerAction`: 편성 저장, 스킬북 구매·습득, 징병·모병·치료, 부대 창설·해산)을 규칙대로 적용하는 `PlayerActionRunner`(종류별 처리기 등록) + 서버 쪽 `ActionVerifier` (R-41) |
| `src/Game.Flow/BattleVerifier.cs` | 서버 세이브 + 제출(출진 요청·시드·대결 선택·명령 기록)로 출진 준비 → 전투 재현 → 지문·틱 비교 → 맞으면 출진 확정 + 결과 반영 |
| `server/Game.Server` | **참고 서버** (ASP.NET Core, net8.0). 개발·테스트용. 공통 계정(`IPlatformStore`: 계정·토큰·게임 가입 기록) + 게임별 데이터(`IGameStore`: (게임 코드, 계정)별 세이브), 게임 등록표(`GameRegistry` + `IGameModule`). 저장소는 메모리 또는 폴더 |
| `tests/Game.Server.Tests` | 참고 서버 HTTP 테스트 (`dotnet test GameCore.sln`에 포함) |

의존: `Game.Online` → Foundation·Data·Battle.Model (명령 기록), `Game.Flow` → … + Online (`ArchitectureTests`).

## 3. 참고 서버 API (v1)

주소는 `Game.Online.OnlineRoutes`에 모아 둔다 (기기·서버 공통).

| 요청 | 하는 일 | 응답 |
|---|---|---|
| `POST /v1/auth/guest` | (공통) 게스트 계정 만들기 (개발용, 실제 로그인은 백엔드 인증으로 교체) | `SignInResponse` (토큰 → `Authorization: Bearer`) |
| `GET /v1/time` | (공통) 서버 시각 | `ServerTimeResponse` |
| `GET /v1/games` | (공통) 서버에 등록된 게임 코드 | `GameListResponse` |
| `GET /v1/games/resolve?app=…` | (공통) 앱 id(package명·번들 id) → 게임 코드. 없으면 404 | `ResolveAppResponse` |
| `GET /v1/me/games` | (공통) 내가 가입한(들어온 적 있는) 게임 | `GameListResponse` |
| `GET /v1/games/{game}/save` | 그 게임 세이브 받기. 처음이면 가입 기록 + 새 게임 (Revision 1). 없는 게임이면 404 | `CloudSave` |
| `PUT /v1/games/{game}/save` | **처음 가져오기 전용**: 서버 세이브가 새 게임(Revision 1)이고 `BaseRevision` 위일 때만. 아니면(또는 읽을 수 없는 세이브) 409 + 서버 사본 | `SavePutResponse` |
| `POST /v1/games/{game}/actions` | 행동 묶음 제출 (R-41). 서버 세이브에 차례로 적용, 옛 세이브 위면 409(`staleSave`), 하나라도 안 되면 422(`badAction` + 순번, 아무것도 반영 안 함), 맞으면 새 세이브 | `ActionVerdict` |
| `POST /v1/games/{game}/battles` | 전투 제출. 옛 세이브 위면 409(`staleSave`), 출진 불가면 422(`badSortie`), 지문이 다르면 422(`mismatch`), 맞으면 반영한 새 세이브 | `BattleVerdict` |

실행: `dotnet run --project server/Game.Server`. 설정:
- `Game:StoreRoot` = 저장 폴더 (없으면 메모리).
- `Game:Games:0:Code` / `DataRoot` / `Theme` / `PackageIds:0` … = 등록할 게임들. 없으면 이 저장소의 게임 하나(`ancientwar`, data 폴더는 위로 찾음, 앱 id `com.myproject.ancientwar` — 임시).

### 게임 추가하기
1. 그 게임의 서버 처리를 `IGameModule`로 구현한다 (같은 엔진의 게임이면 `StrategyGameModule`에 그 게임 데이터 폴더만 주면 된다).
2. 등록표에 더한다 (설정 `Game:Games:n:…` 또는 코드). 게임 코드·앱 id가 겹치면 시작할 때 거절한다.
3. 기기는 자기 게임 코드(const)로 `/v1/games/{game}/…`를 부른다. 앱 id로 찾을 때는 `resolve`.

## 4. 시간

- 부상 회복·구출 기한처럼 시간에 달린 규칙은 **서버 시각**으로 계산한다. 기기는 서버 시각을 받아 같은 시각으로 출진 준비를 해야 서버와 같은 출진이 나온다 (다르면 `badSortie`·`mismatch`).

## 4.5 여러 게임을 한 서버에서 (2026-11-09 결정 방향)

- 나중에 게임을 늘려도 **서버 한 곳**에서 관리한다. 구조는 **공통 계정 + 게임별 데이터**.
  - **공통(플랫폼) DB**: 유저, 로그인 수단(구글·애플·게스트), "유저 × 게임" 가입 기록, 결제 원장.
  - **게임별 DB(또는 스키마)**: 그 게임의 세이브·영토·고용·대전 기록. 한 게임의 버그·부하·구조 변경이 다른 게임에 번지지 않고, 백업·삭제도 게임별로.
- **게임 구분 키 = 직접 정한 게임 코드** (const, 예: `ancientwar`). 안드로이드 package명·iOS 번들 id는 플랫폼마다 다르고 바뀔 수 있어 키로 쓰지 않고, 게임 코드에 연결만 한다.
- 주소에 게임 코드를 넣는다: `/v1/games/{game}/save` … 저장소는 (게임, 계정)으로 나눈다 (구현됨, `MultiGameTests`).
- 이 구조는 백엔드 선택에도 영향: UGS는 게임(프로젝트)마다 계정이 따로라 공통 계정이 어렵고, PlayFab은 같은 namespace 안에서 공유 가능, 자체 서버는 자유. **추천: 자체 서버(이 참고 서버를 키움) + 관리형 DB(PostgreSQL)**.
- 한 사람이 여러 계정을 쓰는 것을 허용할지는 정책으로 따로 정한다.

## 5. 기기 쪽 연결 (2026-11-09)

| 코드 | 내용 |
|---|---|
| `Game.Online.IOnlineTransport` | HTTP 경계. Unity = `UnityWebTransport`(UnityWebRequest, 시간 초과 10초), 테스트 = HttpClient |
| `Game.Online.HttpOnlineService` | 게임 코드별 주소로 서버를 부른다. 게스트 토큰은 `OnlineLink`로 기기에 보관해 다음 실행에 다시 쓴다 |
| `Game.Flow.CloudSync` | 시작: 로그인 → 서버 시각(`ServerClock`) → 서버 세이브. **처음 연결**(보관한 Revision 0)이고 기기 쪽 클리어 스텝이 더 많으면 기기 세이브를 올리고, 아니면 서버 세이브를 받아 쓴다. 전투 뒤: 제출(`SubmissionOf`: 출진 준비 + 명령 기록 + 기기 결과) → 맞으면 서버 세이브를 받아 쓰고, 거절되면 서버 세이브로 되돌린다. 서버에 닿지 않으면 기기 결과 그대로 |
| `GameContext.Do(행동)` | 화면은 세이브를 직접 고치지 않고 행동을 만든다 → 기기에서 적용·저장 → 온라인이면 서버로 (`FlushActions`). 맞으면 서버 세이브, 거절되면 서버 세이브로 되돌림. 서버에 닿지 않으면 `OnlineLink.Pending`에 남겨 두고 다음 시작·다음 행동·전투 제출 전에 다시 보낸다. 전투 제출 전에는 남은 행동을 먼저 보낸다 (서버가 같은 편성으로 출진하도록) |
| `Game.Flow.LocalOnlineService` | **가짜 서버** (online = fake): 기기 안에서 같은 규칙(Revision·전투 검증). 세이브는 기기 저장소의 `fake-server`에 남는다 |
| `Game.Flow.ServerClock` | 기기 시각 + 서버 시각과의 차이. 시간 규칙(부상 회복·구출 기한)을 서버와 같게 |

**설정** (`config/services.development.json`): `online` = `off`(기본, 지금처럼 기기 세이브만) / `fake` / `http`, `onlineUrl`(http일 때 서버 주소), `onlineGame`(게임 코드, 기본 `ancientwar`).

**화면**: 타이틀에서 연결 상태를 보여 주고 동기화가 끝나면 [시작]. 전투 뒤 "서버에서 확인하는 중…" → 결과 화면 위에 확인 결과(확인됨 / 되돌림 / 오프라인).

### 직접 해 보기
1. **가짜 서버로** (서버 없이): `online`을 `fake`로 → Play. 타이틀에 "서버 세이브를 받았습니다", 전투 뒤 "서버에서 전투 결과를 확인했습니다".
2. **참고 서버로** (같은 PC):
   1. 터미널에서 `dotnet run --project server/Game.Server --urls http://localhost:5000` (세이브를 남기려면 `--Game:StoreRoot=server-data`).
   2. `online` = `http`, `onlineUrl` = `http://localhost:5000`.
   3. Unity ▸ Project Settings ▸ Player ▸ Other Settings ▸ **Allow downloads over HTTP** = *Allowed in development builds* (http:// 주소를 쓰려면 필요. 실제 서비스는 https).
3. **폰에서 참고 서버로**: `onlineUrl`을 PC의 같은 와이파이 주소(예: `http://192.168.0.10:5000`)로, 서버는 `--urls http://0.0.0.0:5000`로 실행하고 PC 방화벽에서 5000 포트를 허용.

### 알아둘 점
- 진행은 전투 제출(R-40)과 행동 제출(R-41)로만 바뀐다. `PUT /save`는 처음 연결 때 기기 진행을 가져오는 데만 쓴다 (서버 세이브가 새 게임일 때). 그 처음 가져오기도 실제 서비스에서는 막거나 상한을 둘지 정한다 (기기 세이브를 고쳐 올릴 수 있으므로) — 출시 전 과제.
- 새 행동(예: 장비, 상점)을 더할 때는 `ActionKinds`에 종류를, `PlayerActionRunner`에 처리기를 더하고 화면은 `Ctx.Do`를 부른다. 세이브를 직접 고치고 `Save()`만 부르면 서버에 반영되지 않아 다음 전투가 거절될 수 있다.
- 서버에 닿지 않을 때 한 전투는 기기에만 반영된다 (다음 시작 때 서버 세이브가 주인이므로 사라질 수 있다). 오프라인 전투를 나중에 올리는 방식은 미정.

## 6. 다음 단계

1. ~~기기 쪽 HTTP 구현 + 서비스 설정 `online` + 로그인 → 세이브 받기 → 전투 제출 흐름~~ (5장, 완료).
2. ~~전투 밖 행동을 서버가 같은 규칙으로 적용 (R-41), 세이브 통째 올리기는 처음 가져오기만~~ (완료).
3. 백엔드 선택 후 저장소·인증을 그쪽으로 (`IAccountStore` 대신 백엔드 저장소, 게스트 로그인 대신 백엔드 인증).
4. 고용(hiring.md)·숨은 보상 확정·영토(territory.md)·대전(pvp.md)을 같은 검증 위에 올린다.
