# 서버 저장소를 진짜 DB로 (설계 — **Firestore로 결정**)

> 상태: **결정 (2026-11-16 사용자): Firestore** — 0장 제안 1~3 그대로 (Firestore Native 서울 · 출시 전에 옮김 · 폴더 저장소는 로컬 · 테스트 · 비상용으로 남김). 구현은 7장 순서대로 원격. 설계 제안 2026-11-16 원격 (#217). 지금 서버는 폴더 저장소(`FolderStore`: store.json 한 파일을 Cloud Storage 버킷에 붙여 씀, 인스턴스 1대)다.

## 0. 결정 (2026-11-16 사용자: 제안대로)

| # | 질문 | 제안 |
|---|---|---|
| 1 | DB 종류 | **Firestore (Native 모드, 서울 `asia-northeast3`)** — 2장 |
| 2 | 언제 옮기나 | 출시 전 (사용자가 아직 적을 때 한 번에). 옮기는 동안 점검 안내(R-118)로 10~30분 |
| 3 | 폴더 저장소는 | 남긴다: 로컬 PC 서버 · 테스트 · 비상용 (`Game:Store=folder`) |

## 1. 지금 저장소의 한계

- **한 파일 · 한 서버**: store.json 전체를 메모리에 들고, 바뀔 때마다 파일 전체를 다시 쓴다. 계정 · 우편 · 녹화가 늘수록 쓰기 한 번이 무거워진다 (지금은 작아서 괜찮음).
- **인스턴스 1대 고정** (`--max-instances 1`): 두 대가 같은 파일을 쓰면 서로 덮는다. 배포 중 옛 판 · 새 판이 겹치는 문제는 `store.gen`으로 줄였지만(#205) 근본 해결은 아니다.
- **영토 · 연합은 통째로 잠금** (`WithTerritory` · `WithAlliances`): 지도 전체를 한 번에 읽고 고친다. 사용자가 늘면 공성전 · 꽂기 · 수입 받기가 서로 기다린다.
- 백업 = 버킷 파일 사본뿐 (시점 복구 없음).

## 2. Firestore와 Cloud SQL 비교

| | **Firestore** (문서 DB, 서버리스) | **Cloud SQL** (PostgreSQL) |
|---|---|---|
| 켜 두는 비용 | **없음** (쓴 만큼: 읽기 · 쓰기 · 저장). 무료 한도 하루 읽기 5만 · 쓰기 2만 · 삭제 2만, 저장 1GiB | **인스턴스가 늘 켜짐**: 가장 작은 공유 코어도 월 1~2만 원대 + 저장소. 고가용성이면 두 배 |
| 사용자 적을 때 (테스트 · 초기) | 거의 0원 (무료 한도 안) | 고정비 |
| 서버 여러 대 | 그대로 됨 (연결 관리 없음) | 연결 수 관리 필요 (Cloud SQL 커넥터 · 풀) |
| 트랜잭션 | 문서 단위 · 여러 문서 트랜잭션 (최대 500개) — 세이브 Revision 비교 · 우편 받기에 충분 | 완전한 SQL 트랜잭션 |
| 질의 | 단순 (필드 같음 · 범위 · 정렬, 색인 필요) | 자유로운 SQL · 집계 · 순위 |
| 지도(깃발) 찾기 | 칸 키(cell)로 범위 질의 (지금 칸 체계 그대로) | SQL 범위 · PostGIS |
| 운영 | 거의 없음 (백업 · 시점 복구 설정만) | 버전 업그레이드 · 유지보수 창 · 디스크 |
| 우리 코드와 맞음 | 지금 데이터가 이미 "계정마다 JSON 문서" 모양 | 표로 다시 나눠야 함 (세이브는 JSON 열로) |
| 바꾸기 어려움 | 질의가 약해 순위표 · 통계는 따로 (BigQuery 내보내기) | 낮음 |

**제안: Firestore.** 테스트 · 초기에는 비용이 거의 없고, 서버를 여러 대 띄울 수 있고, 지금 데이터 모양(계정마다 세이브 JSON, 우편 목록, 깃발 하나하나)과 바로 맞는다. 순위표 · 운영 통계가 커지면 그때 BigQuery로 내보낸다.

> 가격은 2026-11 기준 Google Cloud 가격표로 다시 확인할 것 (지역 · 환율에 따라 다름). 이 문서의 숫자는 크기 감을 잡기 위한 것.

### 2.1 비용 감 (Firestore, 대략)

- 하루 활성 사용자 1,000명, 1인당 읽기 200 · 쓰기 50회로 잡으면 → 하루 읽기 20만 · 쓰기 5만 = 무료 한도를 조금 넘음 → **월 수천 원 안팎**.
- 1만 명이면 그 10배 → 월 수만 원 안팎. 세이브 문서 하나 수십 KB × 1만 = 수백 MB (저장 비용은 작음).
- 가장 큰 변수는 **영토 지도 읽기**: 지도를 열 때마다 깃발 전체를 읽으면 비싸진다 → 4장처럼 "보이는 칸만" 읽고, 기기에서 잠깐 기억(캐시)한다.

## 3. 무엇을 어떻게 나누나 (Firestore 모음 설계)

`games/{game}` 아래에 게임별 자료를, 맨 위에 공통(계정) 자료를 둔다.

| 자료 | 지금 (store.json) | Firestore 문서 | 쓰는 방식 |
|---|---|---|---|
| 토큰 → 계정 | `Tokens` | `tokens/{token}` {accountId} | 로그인마다 읽기 1 |
| 외부 로그인 | `External` | `logins/{provider:subject}` {accountId} | |
| 계정 (닉네임 · 이메일 · 가입 게임 · 삭제 요청) | 여러 사전 | `accounts/{accountId}` 문서 하나로 합침 | |
| 버전 · 점검 안내 (R-118) | `StatusOverride` | `config/status` | 짧게 기억 (30초) |
| **세이브** | `Saves` | `games/{g}/saves/{accountId}` {revision, json, savedAt} | **트랜잭션**: revision이 같을 때만 쓰기 (지금 규칙 그대로) |
| 우편 | `Mail` | `games/{g}/accounts/{a}/mail/{mailId}` | 받기 = 세이브 트랜잭션 안에서 우편 지우기 (지금은 따로) |
| 출석 (R-112) | `Attendance` | `games/{g}/accounts/{a}` 안 필드 | 트랜잭션 |
| 푸시 설정 · 토큰 (R-119) | `Push` | 같은 계정 문서 안 필드 | |
| 결제 기록 (R-116) | `Receipts` | `games/{g}/receipts/{transactionId}` | **만들기만(있으면 실패)** = 같은 거래 두 번 막기가 DB 수준에서 확실 |
| 고용 등록 | `Listings` | `games/{g}/listings/{id}` (kind · owner · createdAt 색인) | 목록 = kind로 질의 |
| 고용 기록 (24시간) | `HireLogs` | `games/{g}/accounts/{a}/hireLog/{auto}` + TTL 정책(25시간 뒤 자동 삭제) | |
| 고용된 전투 녹화 (R-120) | `HireReplays` | `games/{g}/hireReplays/{id}` {json, viewers[]} + TTL(예: 30일) | |
| **영토 깃발** | `Territory` (통째) | `games/{g}/flags/{flagKey}` 깃발마다 문서 + `cellLat`·`cellLon` 숫자 필드 | **4장** |
| 연합 | `Alliances` (통째) | `games/{g}/alliances/{id}` + 계정 문서에 `allianceId` | 가입 · 탈퇴 = 연합 문서 + 계정 문서 트랜잭션 |
| 공유 녹화 (R-81) | `SharedReplays` | `games/{g}/sharedReplays/{id}` (stepId · 이김 · 손실 색인), 큰 녹화 JSON은 Cloud Storage 파일 | |

- 문서 하나 최대 1MiB: 세이브 · 녹화 JSON은 지금 수십 KB라 괜찮다. 녹화가 커지면 Cloud Storage로.
- 계정 삭제(R-102): 지금 `PurgeDeleted`가 하는 일을 "계정 문서 · 하위 모음 지우기 + 깃발 · 등록 질의해서 지우기"로.

## 4. 영토가 가장 큰 변경

지금 규칙 코드(`TerritoryFlags` · `TerritoryLedger`)는 `Dictionary<string, TerritoryOwner>` 전체를 받아 계산한다. DB에서는:

1. **꽂기 · 공성전 · 회수 · 추가 파견**: 그 깃발과 **주변 칸의 깃발만** 읽는다 (반지름 최대 10km = 칸 범위 질의). 고치는 것은 그 깃발 문서 하나(공성전 이기면 깃발 + 이전 주인 우편) → 문서 트랜잭션.
2. **지도 보기**: 화면에 보이는 칸 범위만 질의. 수입(Pending)은 기기에 보낼 때 계산(지금과 같음).
3. **수입 가득 훑기(R-119)**: `CollectedAt ≤ 지금 − 24시간` 질의 (색인).
4. 규칙 코드는 그대로 두고, 저장소가 "필요한 깃발만 담은 작은 사전"을 만들어 넘긴 뒤 바뀐 문서만 쓴다 (`WithTerritory` → `WithFlags(keys 또는 칸 범위, use)`). → 규칙 테스트는 거의 그대로.

## 5. 코드 구조

- `IPlatformStore` · `IGameStore` 새 구현 `FirestoreStore` (`server/Game.Server/FirestoreStore.cs`, Google.Cloud.Firestore 패키지).
- 고르기: 설정 `Game:Store` = `folder`(지금 기본) · `memory`(테스트) · `firestore`. 프로젝트 · 데이터베이스 id는 `Game:Firestore:ProjectId` · `DatabaseId`. Cloud Run 서비스 계정에 **Cloud Datastore User** 역할 (키 파일 없음 — Cloud Run 기본 인증).
- 영토 · 연합처럼 통째 잠금을 쓰는 인터페이스는 4장처럼 나눈다 (폴더 저장소도 같은 인터페이스를 구현 → 테스트는 두 저장소로 같이 돈다).
- 같은 테스트 묶음을 Firestore **에뮬레이터**로도 돌린다 (CI에서 `gcloud emulators firestore start`).

## 6. 옮기는 방법 (한 번)

1. 점검 안내 켜기 (관리자 페이지, R-118) — 기기는 "점검 중".
2. 배포 워크플로를 잠깐 멈추고, 지금 서버의 store.json을 내려받는다 (버킷).
3. 옮기기 도구 `dotnet run --project server/Game.Server -- migrate --from store.json --to firestore` (새로 만듦): 계정 · 세이브 · 우편 · 영토 · 연합 · 등록 · 기록을 문서로 쓴다. 같은 것을 두 번 돌려도 같은 결과 (덮어쓰기).
4. 수를 맞춰 본다 (계정 수 · 세이브 revision · 깃발 수 · 우편 수 — 도구가 표로 출력).
5. 저장소 변수 `GAME_STORE=firestore`로 배포 → 폰으로 로그인 · 세이브 · 우편 · 영토 확인.
6. 점검 끄기. store.json은 버킷에 보관 (되돌리기: `GAME_STORE=folder`로 다시 배포).
7. 확인되면 `--max-instances`를 늘린다 (예: 3).

## 7. 순서 (결정 뒤)

| 단계 | 내용 | PR |
|---|---|---|
| 1 | 저장소 인터페이스 나누기 (영토 통째 잠금 → 깃발 단위 범위), 폴더 저장소도 맞춤 — **됨 (2026-11-17, 7.1)** | 규칙 테스트 그대로 |
| 2 | `FirestoreStore` + 에뮬레이터 테스트 + 설정 `Game:Store` | |
| 3 | 옮기기 도구 + 배포 변수 · 사용자 할 일(deploy-cloudrun.md) | |
| 4 | 실제 옮기기 (사용자와 시간 맞춰) | |

### 7.1 1단계 결과 (2026-11-17 원격)

- `IGameStore.WithTerritory(game, TerritoryScope, use)`: 요청마다 필요한 깃발 · 지역만 담은 작은 사전을 규칙에 넘기고, 규칙이 고친 · 더한 · 범위 안에서 지운 것만 다시 쓴다 (`server/Game.Server/TerritoryScope.cs`). 범위 밖은 규칙에 보이지 않는다.
- 범위 종류 → Firestore 2단계 질의:

| 범위 | 쓰는 곳 | Firestore 질의 (2단계) |
|---|---|---|
| `Keys(키…)` | 공성전 검사 · 준비 · 취소 · 끝, 수비 등록 · 보기, 회수, 옛 지역 차지 | 문서 id로 가져오기 |
| `Owner(계정)` | 수입 받기, 용병 등록(깃발에 나간 장수), 꽂기 · 추가 파견(장수가 다른 깃발에 있나) | `ownerAccountId ==` |
| `Near(점, 반지름)` | 꽂기: 꽂을 칸에서 **가장 큰 계급 반지름**(지금 10km) 안의 깃발 (같은 칸 · 남의 원 안) | `cellLat` 범위 질의 + 메모리에서 경도 · 거리 거르기 |
| `IncomeDue(시각)` | 수입 가득 푸시 훑기 (R-119) | `collectedAt <=` |
| `All()` | 영토 지도 보기, 관리자 | 전부 (→ 3단계 전후로 지도는 화면 범위 `Near`로 바꾼다: 기기 요청에 지도 가운데 · 반지름을 더해야 함) |

- **연합은 그대로 둔다**: 연합 목록 화면이 모든 연합을 보여 주고 수가 적다(한 게임 수십~수백). Firestore에서는 `alliances` 모음 전체 읽기 + 연합 문서 트랜잭션. 가입한 연합 찾기(`AllianceBook.Of`)도 그 안에서.
- 기기 안 가짜 서버(`LocalOnlineService`)는 장부가 메모리 사전 하나라 그대로.
- 테스트: `TerritoryScopeTests`(범위 · 되쓰기), `ServerTests.PlantUsesNearScope`(남의 원 안 → 공성전, 멀면 꽂힘, 내 깃발 장수 generalBusy), 기존 영토 · 공성전 · 푸시 테스트 그대로 통과.

### 7.2 2 · 3단계 결과 (2026-11-17 원격)

- **2단계** `server/Game.Server/FirestoreStore.cs` (`IStore` = 공통 + 게임별 + 관리자 계정 목록, 메모리 · 폴더 저장소도 같은 인터페이스). 설정 `Game:Store` = `memory` · `folder` · `firestore`, `Game:Firestore:ProjectId` · `DatabaseId`.
  - 문서마다 내용 = JSON 문자열 칸 `j` (store.json과 같은 모양) + 찾기용 칸 (`owner` · `kind` · `lat` · `coll` …). 모음: `tokens` · `logins` · `accounts` · `nicknames`(닉네임 중복 막기) · `config/status` · `games/{게임}/saves · players/{계정}(출석 · 푸시, mail · hireLog 하위) · listings · territory · alliances · sharedReplays · hireReplays · receipts`.
  - 읽고 고치는 일은 트랜잭션 하나. 그 안에서 부른 우편 보내기 · 지우기 · 연합 읽기도 **같은 트랜잭션**으로 묶인다 (쓰기는 모았다가 끝에) → 트랜잭션이 다시 돌아도 수입 우편이 두 번 가지 않고, 깃발 기록과 우편이 함께 반영되거나 함께 안 된다.
  - 시험: GitHub Actions `core-tests`의 `firestore-tests` job이 Firestore 에뮬레이터를 띄우고 **서버 테스트 전체**를 Firestore 저장소로 한 번 더 돌린다 + `FirestoreStoreTests`(계정 · 게임 자료 · 영토 트랜잭션 · 옮기기).
- **3단계** 옮기기 도구 `dotnet Game.Server.dll migrate --from <store.json 폴더> --project <GCP 프로젝트> [--dry]` (`Migrate.cs`) + 워크플로 `firestore-migrate` (손 실행, 버킷의 store.json을 받아 옮김, `dry_run`이면 수만). 배포 워크플로는 저장소 변수 `GAME_STORE`(기본 `folder`) · `MAX_INSTANCES`(firestore일 때만, 기본 1) · `FIRESTORE_DATABASE`.

## 8. 사용자가 할 일 (4단계: 실제로 옮기기, 30분 안팎)

**미리 (한 번, 아무 때나 — 게임에 영향 없음)**

1. Google Cloud 콘솔(서버 프로젝트 `GCP_PROJECT_ID`) → **Firestore** → 데이터베이스 만들기: **Native 모드**, 위치 **`asia-northeast3` (서울)**, 데이터베이스 ID `(default)`.
2. IAM → 서버 실행용 서비스 계정(`GCP_RUNTIME_SA`)에 역할 **Cloud Datastore User**.
3. IAM → 배포용 서비스 계정(`GCP_SERVICE_ACCOUNT`, GitHub Actions가 쓰는 것)에 역할 **Cloud Datastore User** + 버킷(`GCS_BUCKET`)의 **Storage Object Viewer** (옮기기 워크플로가 store.json을 읽고 Firestore에 쓴다).
4. (권장) Firestore → 재해 복구: **시점 복구(PITR)** 켜기, 매일 백업 일정.
5. GitHub → Actions → `firestore-migrate` → Run workflow, `dry_run` = **true** → 로그의 "원본" 수(계정 · 세이브 · 영토 …)가 맞는지 본다. (Firestore에 쓰지 않음)

**옮기는 날 (사용자가 적은 때)**

6. 관리자 페이지 → 버전 · 점검 → **점검 켜기** (끝 시각 30분 뒤). 폰에서 "점검 중"이 나오는지.
7. Actions → `firestore-migrate` → Run workflow, `dry_run` = **false** → 로그의 "Firestore에 쓴 문서" 수가 5번의 원본 수와 맞는지.
8. GitHub 저장소 → Settings → Variables: **`GAME_STORE` = `firestore`** → Actions → `deploy-cloudrun` → Run workflow.
9. 폰으로 확인: 로그인 · 닉네임 · 세이브(진행) · 우편 · 영토 깃발 · 연합 · 용병 목록이 그대로인지. 관리자 페이지 서버 상태의 저장소가 `firestore`인지.
10. 점검 끄기.
11. (며칠 문제없으면) 변수 **`MAX_INSTANCES` = `3`** → 배포: 서버 여러 대.

**되돌리기**: 변수 `GAME_STORE` = `folder` → 배포 (store.json은 지우지 않으므로 옮기기 직전 상태로 돌아간다 — 옮긴 뒤 Firestore에 생긴 진행은 빠진다).

### 8.1 서버 여러 대로 올리기 전 (`MAX_INSTANCES` = 3, 2026-11-20 원격 정리)

올리는 일은 변수 하나 + 배포뿐이다 (위 11번). 그 전 며칠 동안 아래를 본다.

**코드가 여러 대에서 안전한지 (원격 확인, 2026-11-20 main)**

- 서버 메모리에 게임 상태를 들고 있지 않다: 세이브 · 영토 · 연합 · 우편 · 출석 · 결제 기록 · 운영 켜고 끄기 · 등급은 모두 저장소(Firestore)에 있고, 서버마다 따로 가진 것은 캐시뿐 (구글 인증서 · 푸시 토큰 · 게임 데이터 정의). 그래서 어느 서버가 요청을 받아도 같은 결과.
- 바꾸는 일은 Firestore 트랜잭션 하나 안에서 읽고 쓴다 (`FirestoreStore.Atomic`). 두 서버가 같은 문서를 동시에 고치면 Firestore가 한쪽을 다시 시도시킨다 (SDK 기본 최대 5번). 세이브는 판 번호(Revision)로 한 번 더 막는다 — 같은 판에서 두 번 올리면 하나는 `staleSave`.
- 알고 있는 약한 곳 (사람이 적을 땐 문제 없음, 늘어나면 고칠 것):
  1. **연합**: 연합을 만들고 · 가입하고 · 나갈 때 연합 모음 **전체**를 한 트랜잭션에서 읽는다. 연합 활동이 동시에 몰리면 다시 시도가 늘고, 5번을 넘기면 그 요청은 실패(500)한다. → 로그에 `ABORTED` · `Too much contention`이 보이면 연합을 문서 하나씩 읽게 바꾼다 (원격 일).
  2. **영토 지도 열기** (`GET /territory`): 깃발 **전체**를 읽는다 (`TerritoryScope.All`). 깃발이 많아지면 읽기 비용이 가장 크게 늘어나는 곳. → 깃발이 수백 개를 넘으면 "보이는 칸만"(Near) 읽게 바꾼다 (4장, 원격 일).
  3. **결제 한 번만 쓰기**: "이 거래를 이미 썼나" 확인과 세이브 저장 · 거래 기록이 한 트랜잭션이 아니다. 같은 계정은 판 번호로 막히지만, **서로 다른 계정이 같은 영수증을 같은 순간에** 내면 둘 다 받을 수 있는 틈이 이론상 있다 (구글 영수증은 구매자 계정에 묶여 있어 실제로 일어나기 어렵다). → 결제를 실제로 켜기 전에 거래 기록을 세이브와 같은 트랜잭션에 넣는다 (원격 일).

**며칠 지켜볼 것 (사용자, 콘솔에서)**

| 볼 곳 | 어디서 | 괜찮은 상태 |
|---|---|---|
| 오류 | Cloud Run → `game-server` → **로그**, 심각도 "오류" | `ABORTED` · `Too much contention` · `DeadlineExceeded` 없음 |
| 응답 시간 | Cloud Run → 측정항목 → **요청 지연 시간** (p95) | 1초 아래 (첫 요청 콜드 스타트는 몇 초 괜찮음) |
| 동시 요청 | Cloud Run → 측정항목 → **컨테이너 인스턴스 수** · **요청 수** | 한 대가 바쁜 시간이 자주 있으면 여러 대가 도움이 된다 |
| 읽기 · 쓰기 수 | Firestore → **사용량** (일별 읽기 · 쓰기 · 삭제) | 무료 한도(하루 읽기 5만 · 쓰기 2만)에 비해 얼마나 쓰는지. 읽기가 튀면 대개 영토 지도 (위 2) |
| 비용 | 결제 → **예산 및 알림** → 예산 만들기 (예: 월 1만 원, 50% · 90% · 100% 알림 메일) | 한 번 만들어 두면 넘을 때 메일이 온다 |

**올린 뒤**

- 같은 화면을 하루 이틀 다시 본다. 인스턴스 수가 2~3으로 올라가는 때가 있는지, 오류가 늘지 않았는지.
- 문제가 보이면 변수 `MAX_INSTANCES` = `1` → 배포로 바로 되돌린다 (데이터는 그대로, Firestore라 한 대로 돌아가도 잃는 것 없음).
- `--min-instances 0`이라 손님이 없으면 0대 (비용 0), 첫 요청에 몇 초 걸릴 수 있다. 늘 빠르게 하려면 나중에 `--min-instances 1` (월 비용이 생김) — 출시 때 판단.

