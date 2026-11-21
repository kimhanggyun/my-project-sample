# 관리자 페이지

> 2026-11-16 사용자 요청. 게임 서버가 함께 내놓는 웹 페이지 하나: **`<서버 주소>/unitygame1.html`**
> (지금: https://game-server-883012601666.asia-northeast3.run.app/unitygame1.html)

## 1. 들어가기

> **지금 (개발 중, 2026-11-16 사용자): 접속 ID만** — 서버 설정 `Game:AdminGoogle=off` (배포 기본, 저장소 변수 `ADMIN_GOOGLE`이 없으면 off). 구글 로그인 확인(②)은 **출시 전에 켠다** ([backlog.md](../design/backlog.md) F): 변수 `ADMIN_GOOGLE` = `on` + 아래 3장 2번(OAuth 원본).

| 단계 | 무엇 | 어디서 확인 |
|---|---|---|
| ① 접속 ID | 관리자 키. 페이지 첫 칸에 입력 | 서버 설정 `Game:AdminKey` (Cloud Run 환경 변수 `Game__AdminKey` ← GitHub 저장소 **비밀값** `GAME_ADMIN_KEY`) |
| ② 구글 로그인 (출시 전에 켬) | 마스터 구글 계정 (R-86) | 서버 설정 `Game:Masters` (저장소 변수 `GAME_MASTERS`), `Game:AdminGoogle`이 off가 아닐 때만 |

- **접속 ID는 저장소 · 페이지 어디에도 적지 않는다** (저장소가 공개). 서버만 안다. 바꾸려면 GitHub 비밀값을 바꾸고 배포를 다시 돌린다.
- 키 설정이 없으면 관리자 주소(`/v1/admin/*`)는 모두 404 = 꺼짐 (로컬 참고 서버 기본).
- 키가 틀리면 404, 로그인 안 하면 401, 관리자 · 서브 관리자가 아니면 403 (서브 관리자는 5장의 허용 항목만, 나머지 403). 키는 같은 시간 비교.
- 페이지는 이 탭(sessionStorage)에만 키 · 토큰을 둔다. [나가기]로 지운다.
- 구글 로그인은 기기와 같은 길 (deploy-cloudrun.md 5장): 구글 ID 토큰(Google Identity Services 웹 버튼) → Firebase `signInWithIdp` → 서버 `/v1/auth/firebase` → 게임 토큰.

## 2. 할 수 있는 것

| 탭 | 내용 | 서버 |
|---|---|---|
| 서버 상태 | 서버 시각 · 배포 판(K_REVISION) · 게임 · 계정 수 · 저장소 · 마스터 · 결제 검증 · Firebase 프로젝트 | `GET /v1/admin/status` |
| 계정 · 세이브 | 모든 계정 (닉네임 · 이메일 · 구글/게스트 · 가입한 게임 · **마지막 접속** · 삭제 요청 · 등급), 찾기(계정 id · 닉네임 · 이메일) + **등급 거르기** (전부 · 관리자 · 서브 관리자 · 일반) + **최근 접속 순** 정렬. 누르면 닉네임 · 등급 · 마지막 접속 · 세이브 요약(지갑 · 군주 레벨 · 깬 스텝 · 장수/부대 수) · 우편함 · 세이브 원문. 마지막 접속 = 앱이 `/v1/me`를 부른 때(켤 때), 10분 단위로만 기록 (쓰기 줄이기) | `GET /v1/admin/accounts`, `GET /v1/admin/games/{game}/accounts/{id}` |
| 우편 보내기 | 지정 계정(쉼표) 또는 그 게임의 모든 계정에 곡식 · 엽전 · 금괴 우편 (짧은 말 60자). 한 번에 1억까지. 받는 사람이 [받기] = `mail.claim` | `POST /v1/admin/games/{game}/mail` (우편 종류 `adminGift`) |
| 버전 · 점검 | 최소 · 최신 버전, 점검 켜기 · 문구 키 · 끝나는 시각 → [저장] (배포 없이 바로), [지우기] = 서버 설정으로 (R-118) | `GET` · `POST /v1/admin/notice` |
| 영토 · 연합 | 꽂힌 깃발 (주인 · 연합 · 장수 · 병력 · 보호 · 공성전 중), 연합 목록 | `GET /v1/admin/games/{game}/territory` |
| 버전 · 점검 안내 (R-118) | 최소 · 최신 앱 버전, 점검 켜기(문구 키 · 끝 시각). 저장하면 바로 `GET /v1/status`에 나온다. 지우기(Clear)면 서버 설정 값으로 | `GET` · `POST /v1/admin/notice` (몸 `AdminStatusNotice`: Override · Clear) |
| 모집 배너 (R-122) | 배너마다 켬 · 끔, 기간 덮어쓰기 (비우면 데이터 기간), 지금 열림. 저장하면 공개 `GET /v1/games/{game}/content`에 바로 나오고 서버가 꺼진 배너 모집을 거절. 기기는 앱을 다시 켤 때 받는다 | `GET` · `POST /v1/admin/games/{game}/banners` (몸 `AdminBannersRequest.Items`) |
| 상점 상품 (R-123) | 결제 상품 · 엽전 · 금괴 치장마다 종류 · 가격 · 데이터 기본(기본 켬/끔, 바꿈) · **표출** 체크. 저장하면 공개 `/content`에 바로 나오고 서버가 꺼진 상품 구매를 거절 (영구 상품 복원은 됨). 기본 끔 = 새로 등록한 상품 | `GET` · `POST /v1/admin/games/{game}/products` (몸 `AdminBannersRequest.Items`: Id · Off) |
| 결제 검증 (R-124, [상점 상품] 탭 아래) | [서버 설정 그대로] · [끔] · [가짜 허용] + 저장, 서버 설정 · 지금 쓰는 것 · 허용 여부 표시. 가짜 허용 중이면 페이지 맨 위 빨간 띠. 바꾸기는 관리자만 (서브 관리자는 보기). 서버 설정 `ALLOW_ADMIN_RECEIPT`=false(출시)면 듣지 않음 (422) | `GET` · `POST /v1/admin/games/{game}/receipts` (몸 `{ Override: null \| "off" \| "fake" }`) |
| 수입 가득 푸시 (R-119) | 수입이 다 찬 깃발 주인에게 알림 보내기 (보낸 기기 수). 예약(Cloud Scheduler)이 한 시간마다 부른다 | `POST /v1/admin/games/{game}/push/sweep` |

- 세이브를 직접 고치는 기능은 없다 (보기만). 자원은 우편으로만 준다 → 서버 규칙(`mail.claim`)을 그대로 거친다.

## 3. 처음 한 번 (사용자)

지금(개발 중)은 1 · 4번만. 2 · 3번은 출시 전에 구글 확인을 켤 때.

1. GitHub 저장소 → Settings → Secrets and variables → Actions → **Secrets**에 `GAME_ADMIN_KEY` = 접속 ID. (또는 `gh secret set GAME_ADMIN_KEY`)
2. GCP 콘솔 → API 및 서비스 → 사용자 인증 정보 → OAuth 2.0 클라이언트 **웹 클라이언트** (`googleWebClientId`와 같은 것) → **승인된 JavaScript 원본**에 서버 주소 `https://game-server-883012601666.asia-northeast3.run.app` 추가. (없으면 구글 버튼이 "origin not allowed")
3. `GAME_MASTERS`에 내 구글 이메일이 있는지 확인.
4. 배포를 다시 돌린다 (main에 머지하거나 Actions에서 deploy-cloudrun 수동 실행).

## 5. 계정 등급 (H8, 2026-11-17 — 권한 표는 사용자 확인 필요)

| 등급 | 누가 | 관리자 페이지 | 앱 (`/v1/me`) |
|---|---|---|---|
| **관리자** `admin` | 서버 설정 `Game:Masters` 계정 = **처음 관리자 (고정, 바꿀 수 없음)** + 관리자 페이지에서 관리자로 정한 계정 | **전부**: 상태 · 계정 · 세이브 · 우편 · 공지(버전 · 점검) · 영토 · 모집 배너 · 상점 상품 · 푸시 · **등급 지정** | `IsMaster` true → 개발 줄 · 개발 행동 `dev.*` (R-86) |
| **서브 관리자** `subAdmin` | 관리자 페이지에서 정한 계정 | **보기**(상태 · 계정 · 세이브 · 영토 · 결제 검증) + **우편 보내기** + **공지(버전 · 점검)** 만. 모집 배너 · 상점 상품 · 결제 검증 바꾸기 · 푸시 · 등급 지정은 403 | `IsMaster` false (개발 행동 없음), `Role` = subAdmin |
| 일반 | 나머지 | 들어갈 수 없음 (403) | 그대로 |

- **확정 (2026-11-20 사용자)**: 서브 관리자의 **우편 보내기(자원 선물 포함)는 허용** — 위 표 그대로.
- 정하는 곳: [계정 · 세이브] 탭 각 줄의 **등급** 칸 (관리자로 들어왔을 때만 고르기 + [저장], 처음 관리자는 "관리자 (처음 · 고정)"). `POST /v1/admin/accounts/{account}/role` 몸 `{ Role: "admin" | "subAdmin" | null }` — 처음 관리자면 422 `fixed`, 모르는 값 422 `badRole`, 없는 계정 422 `noAccount`.
- 저장: 계정 문서(Firestore `accounts/{id}`의 `Role`) · 폴더 저장소 `Roles`. 계정이 지워지면(R-102) 함께 지운다.
- 관리자 키는 그대로 필요하고, 구글 확인(`Game:AdminGoogle`)이 꺼져 있으면 키만으로 관리자 (지금 개발 중 설정).
- [서버 상태]의 "나" 줄에 내 등급이 나온다.

## 4. 코드

- 서버: `server/Game.Server/Admin.cs` (`AdminRoutes`), 계정 목록 `InMemoryStore.AdminAccounts`, 페이지 `server/Game.Server/wwwroot/unitygame1.html` (정적 파일, `Cache-Control: no-store`, `noindex`).
- 기기 우편함: `adminGift` 줄 "운영 선물: n 자원" + 짧은 말.
- 테스트: `tests/Game.Server.Tests/AdminTests.cs`.
- 출시 전에: 이 API 키(`firebaseApiKey`)를 안드로이드로만 제한하면 웹 로그인이 막힌다 → 웹 출처(서버 주소)도 허용 목록에 넣는다.
