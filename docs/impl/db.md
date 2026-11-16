# 서버 저장소를 진짜 DB로 (설계 — 결정 대기)

> 상태: **설계 제안 (2026-11-16 원격)**. 사용자 결정을 받은 뒤 구현한다. 지금 서버는 폴더 저장소(`FolderStore`: store.json 한 파일을 Cloud Storage 버킷에 붙여 씀, 인스턴스 1대)다.

## 0. 결정해 주실 것 (요약)

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
| 1 | 저장소 인터페이스 나누기 (영토 · 연합 통째 잠금 → 깃발 단위), 폴더 저장소도 맞춤 | 규칙 테스트 그대로 |
| 2 | `FirestoreStore` + 에뮬레이터 테스트 + 설정 `Game:Store` | |
| 3 | 옮기기 도구 + 배포 변수 · 사용자 할 일(deploy-cloudrun.md) | |
| 4 | 실제 옮기기 (사용자와 시간 맞춰) | |

## 8. 사용자가 할 일 (결정 뒤, 구현 PR에서 다시 자세히)

1. Google Cloud 콘솔 → Firestore → 데이터베이스 만들기: **Native 모드**, 위치 `asia-northeast3` (서울), 이름 `(default)`.
2. 서버 실행용 서비스 계정에 **Cloud Datastore User** 역할.
3. (권장) Firestore 백업 일정 · 시점 복구(PITR) 켜기.
