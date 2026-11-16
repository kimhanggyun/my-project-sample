# 서버 배포: Google Cloud Run + Firebase 로그인

> 상태: 코드·배포 설정 준비 (2026-11-11, 사용자 결정 "추천대로"). 계정·프로젝트 만들기는 사용자가 한다 (2장).
> 관련: [server.md](server.md) (참고 서버 API), `server/Game.Server/Dockerfile`, `.github/workflows/deploy-cloudrun.yml`

## 1. 구성

| 부분 | 쓰는 것 | 비고 |
|---|---|---|
| 게임 서버 | **Cloud Run** (서울 `asia-northeast3`) | 지금 서버 컨테이너 그대로. 요청 없으면 0대 (테스트 단계 비용 거의 없음) |
| 저장 | **Cloud Storage 버킷**을 `/data/store`에 붙여 지금 폴더 저장소(store.json) 그대로 | 테스트 단계용. 인스턴스 1대로 제한. 사용자가 늘면 Firestore 또는 Cloud SQL로 바꾼다 (`IGameStore` 구현 추가) |
| 로그인 | **Firebase Authentication** (구글, 나중에 애플) | 기기가 받은 Firebase ID 토큰을 서버가 확인 → 서버 계정 토큰 (3장) |
| 이미지 저장 | Artifact Registry 저장소 `game` | |
| 배포 | GitHub Actions `deploy-cloudrun`: **main에 합치면 서버 자동 배포** (2026-11-15) + 손으로도 실행 | 비밀 키 대신 Workload Identity |

- 결제 영수증: 배포 서버는 `Game__AllowFakeReceipts=false` (가짜 영수증 거절 = 검증기 `off`). 검증기는 `Game__ReceiptVerifier`로 고른다 (`fake` · `off` · `googlePlay`, R-114). 실제 Google Play 검증은 7장 (자리만 있음).
- **main에 합치면 서버 자동 배포** (2026-11-15 사용자 결정): `main`에 push(= PR 합치기)되고 바뀐 파일이 `server/**` · `src/**` · `data/**` · `config/**` · `GameCore.sln` · 이 워크플로 파일 중 하나면 `deploy-cloudrun`이 돈다. 먼저 `test` job(core-tests와 같은 `dotnet test GameCore.sln`)을 돌리고, **실패하면 배포하지 않는다** (`deploy`가 `needs: test`). 배포는 한 번에 하나씩 (`concurrency`, 인스턴스 1대 · 버킷 저장소). 문서 · 테스트 · Unity 화면만 바꾼 PR은 배포하지 않는다. 손 실행(Run workflow)은 그대로 — 변수(`GAME_MASTERS` 등)만 바꿨을 때 쓴다.
- **배포가 바뀌는 동안** (2026-11-16): 새 판이 켜져 트래픽을 넘겨받을 때까지 옛 판도 잠깐 요청을 받는다 → 두 서버가 같은 버킷 폴더를 쓴다. 새 판이 켜질 때 읽은 store.json으로 옛 판의 그 뒤 기록(우편 받기 · 세이브)을 덮어쓰면 기기에 staleSave가 났다. 그래서 폴더 저장소는 쓸 때마다 `store.gen`(판 표시)을 바꾸고, 저장소를 쓰기 전에(1초에 한 번까지) `store.gen`이 내 것과 다르면 store.json을 다시 읽는다 (`FolderStore.Sync`, 테스트 `FolderStoreTests`). 버킷 메타데이터 캐시 때문에 몇 초 늦게 보일 수는 있으니, 머지 직후 1~2분은 폰 시험을 피하는 것이 안전하다. 사용자가 늘면 DB(Firestore · Cloud SQL)로.
- 마스터 계정 (R-86): 저장소 변수 `GAME_MASTERS`(계정 id 또는 구글 로그인 이메일 — 확인된 이메일, 대소문자 무시, 섞어 적어도 됨, 여럿이면 쉼표. 게스트는 이메일이 없어 계정 id로) → `Game__Masters`. 바꾼 뒤 배포를 다시 돌린다.

## 2. 사용자가 할 일 (한 번)

콘솔에서 하는 일이다. 막히면 화면을 캡처해 보내면 된다. **비밀번호·키 값은 채팅에 붙이지 않는다.**

1. **Google Cloud 프로젝트**: https://console.cloud.google.com 에서 새 프로젝트 (구글 플레이 개발자 콘솔 계정과는 다른 곳. 같은 구글 계정으로 로그인하면 된다) (예: `ancientwar-dev`), **결제 계정 연결** (무료 사용량 안에서도 필요).
2. **Firebase**: https://console.firebase.google.com 에서 "프로젝트 추가" → 1번 프로젝트를 고른다 (같은 프로젝트를 쓰면 프로젝트 id도 같다).
   - Authentication → 시작하기 → 로그인 방법: **Google 사용 설정** (Apple은 막아 둔 상태이므로 나중에).
   - 프로젝트 설정 → 앱 추가 → **Android** → `google-services.json` 받기 (Unity SDK를 붙일 때 쓴다, 저장소에는 넣지 않는다).
   - **패키지 이름: `net.itgold.unitygame1`** (2026-11-11 결정). Unity(Player Settings → Package Name)·서버 기본 `PackageIds`에 넣어 두었다. Firebase 안드로이드 앱 등록 때 이 값을 쓴다. 스토어에 올린 뒤에는 바꿀 수 없다.
   - 디버그 서명 SHA-1 등록 (Unity 키스토어 기준, 방법은 SDK 붙일 때 안내).
3. **Cloud Shell**(콘솔 오른쪽 위 `>_`)에서 아래를 한 번 실행한다. `PROJECT`, `GH_REPO`만 바꾼다.

```bash
PROJECT=unitygame1-dev
REGION=asia-northeast3
GH_REPO=kimhanggyun/my-project-sample
BUCKET=$PROJECT-game-store

gcloud config set project $PROJECT
gcloud services enable run.googleapis.com artifactregistry.googleapis.com iam.googleapis.com iamcredentials.googleapis.com sts.googleapis.com storage.googleapis.com
gcloud artifacts repositories create game --repository-format=docker --location=$REGION
gcloud storage buckets create gs://$BUCKET --location=$REGION --uniform-bucket-level-access

# 서버 실행용 서비스 계정: 버킷(세이브 저장)에만 쓸 수 있다
gcloud iam service-accounts create game-runtime --display-name="Game server runtime"
RUNTIME=game-runtime@$PROJECT.iam.gserviceaccount.com
gcloud storage buckets add-iam-policy-binding gs://$BUCKET --member=serviceAccount:$RUNTIME --role=roles/storage.objectAdmin

# 배포용 서비스 계정 + GitHub Actions가 키 없이 쓰게 (Workload Identity)
gcloud iam service-accounts create deployer --display-name="GitHub deploy"
SA=deployer@$PROJECT.iam.gserviceaccount.com
for ROLE in roles/run.admin roles/artifactregistry.writer roles/iam.serviceAccountUser; do
  gcloud projects add-iam-policy-binding $PROJECT --member=serviceAccount:$SA --role=$ROLE --condition=None
done
gcloud iam workload-identity-pools create github --location=global
gcloud iam workload-identity-pools providers create-oidc github --location=global --workload-identity-pool=github \
  --issuer-uri=https://token.actions.githubusercontent.com \
  --attribute-mapping=google.subject=assertion.sub,attribute.repository=assertion.repository \
  --attribute-condition="assertion.repository=='$GH_REPO'"
POOL=$(gcloud iam workload-identity-pools describe github --location=global --format='value(name)')
gcloud iam service-accounts add-iam-policy-binding $SA --role=roles/iam.workloadIdentityUser \
  --member=principalSet://iam.googleapis.com/$POOL/attribute.repository/$GH_REPO

echo "GCP_PROJECT_ID=$PROJECT"
echo "GCP_REGION=$REGION"
echo "GCP_WIF_PROVIDER=$POOL/providers/github"
echo "GCP_SERVICE_ACCOUNT=$SA"
echo "GCP_RUNTIME_SA=$RUNTIME"
echo "GCS_BUCKET=$BUCKET"
echo "FIREBASE_PROJECT_ID=$PROJECT"
```

4. 마지막에 찍힌 7줄을 GitHub 저장소 → Settings → Secrets and variables → Actions → **Variables** 탭에 같은 이름으로 넣는다 (비밀값이 아니라서 Variables).
5. GitHub → Actions → `deploy-cloudrun` → **Run workflow**. 끝에 `서버 주소: https://game-server-….run.app`이 찍히면 성공.
6. 게임 설정 화면(개발 빌드) → 서버 연결 → [http]에 그 주소를 넣으면 폰이 클라우드 서버에 붙는다.

## 3. 로그인 흐름

```
기기: 구글 로그인 (Firebase Auth SDK) → Firebase ID 토큰
기기 → 서버: POST /v1/auth/firebase { IdToken }
서버: 서명(구글 공개 인증서)·iss·aud(프로젝트)·만료·sub 확인 → "제공자:사용자 id"로 계정 찾기(없으면 만들기) → { AccountId, Token }
기기: 이 Token으로 다른 API (게스트와 같음)
```

- 같은 구글 계정이면 기기를 바꿔도 같은 서버 계정이다.
- 서버 설정 `Game:Firebase:ProjectId`가 없으면 이 주소는 404 (참고 서버 로컬 실행은 지금처럼 게스트만).
- 게스트 → 구글 계정 연동(기존 진행 옮기기)은 다음 단계.
- 코드: `server/Game.Server/FirebaseAuth.cs` (확인기), `Stores.SignInExternal`, 테스트 `tests/Game.Server.Tests/FirebaseAuthTests.cs`.

## 4. 지금 상태 (2026-11-11)

- 첫 배포 완료: `https://game-server-883012601666.asia-northeast3.run.app` (Actions `deploy-cloudrun`으로 다시 배포). 개발 빌드의 기본 서버 연결이 이 주소다 (`config/services.development.json`, 설정 화면에서 바꾸면 그쪽이 앞선다).
- Firebase 프로젝트 `unitygame1-dev`, 안드로이드 앱 `net.itgold.unitygame1`, 디버그 SHA-1 등록, 구글 로그인 사용.

## 5. 기기 구글 로그인 (Firebase SDK 없이)

```
[구글로 로그인] → 안드로이드 Credential Manager (계정 고르기, 다음부터는 승인한 계정으로 자동)
  → 구글 ID 토큰 → Firebase Auth REST accounts:signInWithIdp → Firebase ID 토큰
  → 서버 POST /v1/auth/firebase → 계정 토큰 (기기에 보관, 게스트 토큰을 대신함)
```

- 플러그인: `unity/Assets/Plugins/Android/GoogleSignIn.androidlib` (Java `GoogleSignInBridge`, 의존 `androidx.credentials` 1.3.0 · `googleid` 1.1.1, Gradle 모듈로 함께 빌드).
- C#: `Online/GoogleFirebaseAuthService` (서비스 `auth.google` = `credentialManager`). 에디터·안드로이드가 아닌 곳에서는 가짜 로그인으로 대신한다 (서버는 게스트 그대로).
- 설정: `config/services.*.json`의 `firebaseApiKey`, `googleWebClientId`(google-services.json의 api_key, client_type 3). 앱에 들어가는 공개 식별자라 저장소에 둔다. 출시 전에 GCP 콘솔에서 이 API 키를 안드로이드 앱(패키지 + SHA-1)·Identity Toolkit API로 제한한다.
- 계정이 바뀌면(게스트 → 구글) 처음 맞추는 것으로 다시 동기화한다: 진행이 더 앞선 쪽(깬 스텝 수)이 남는다 (CloudSync). 이전 계정의 보내지 못한 기록은 버린다.
- `google-services.json` 파일 자체는 쓰지 않는다 (Firebase SDK를 쓰지 않으므로).
- 출시 서명(업로드 키·Play 앱 서명)의 SHA-1도 출시 전에 Firebase에 추가해야 한다.

## 6. 다음 단계

1. ~~폰에서 구글 로그인 확인~~ — **확인됨 (2026-11-11)**: 안드로이드 빌드 성공, 구글 로그인 → 클라우드 서버 계정, 앱 삭제·재설치 후 같은 구글 계정으로 닉네임·진행(스텝 클리어) 복구.
2. 애플 로그인 (같은 흐름, providerId apple.com).
3. 결제 영수증 실제 검증 (Google Play Developer API) — 뼈대 · 설정은 있음, 켜는 순서는 7장.
4. 사용자가 늘면 저장소를 Firestore로 (`IGameStore` 구현 추가) → 인스턴스 여러 대.

## 7. 결제 영수증 검증 (Google Play — R-114 · R-116)

서버는 `IReceiptVerifier`(src/Game.Flow/Purchases.cs) 하나로 영수증을 본다. 고르는 설정 `Game:ReceiptVerifier` (Cloud Run 환경 변수 `Game__ReceiptVerifier`, 배포 워크플로에서는 저장소 변수 `RECEIPT_VERIFIER`):

| 값 | 뜻 | 쓰는 곳 |
|---|---|---|
| `fake` (참고 서버 기본) | 개발용 영수증 `fake:…`만 받는다 | 로컬 서버 · 테스트 · 개발 빌드 |
| `off` | 모두 거절 | **지금 배포 서버** (`RECEIPT_VERIFIER`가 없으면 off) |
| `googlePlay` | Google Play Developer API로 확인 (R-116, `server/Game.Server/GooglePlay.cs`) | 출시 · 내부 테스트 트랙 |

**검사 (googlePlay)**: 기기가 보낸 영수증(구매 토큰, 또는 Unity IAP 영수증 JSON — 안의 `purchaseToken`)으로 `purchases.products.get(패키지, 상품 id, 토큰)`.
- 통과: `purchaseState` 0(구매됨) · 아직 소모 안 함. 거래 id = `orderId` (없으면 `gp:토큰`).
- 거절: 취소(rejected) · 결제 대기(pending) · 이미 소모(used) · 없는 토큰이나 다른 상품(rejected) · 스토어에 닿지 않음(unavailable, 다시 시도).
- 테스트 구매(라이선스 테스터, `purchaseType` 0)는 받는다. 출시 뒤 막으려면 `Game__GooglePlay__AllowTestPurchases=false`.
- **Play 상품 id = `data/Rules/products.json`의 id** (예: `gold_pack_small`, `remove_banner_permanent`, `cosmetic_armor_gold`). Play Console에 같은 id로 "인앱 상품"을 만든다.
- 반영한 거래는 서버 저장소에 남는다 (같은 거래는 서버를 다시 띄워도, 다른 계정에서도 다시 못 씀). 계정을 지워도 거래 기록은 남긴다.
- 반영한 뒤 서버가 스토어에 알린다: 영구 상품(광고 영구 제거 · 결제 치장)은 **acknowledge**, 나머지(꾸러미 · 30일 광고 제거)는 **consume** (다시 살 수 있게). 구글은 3일 안에 확인되지 않은 구매를 환불하므로, 실패하면 서버 기록에 `[결제] 스토어 확인 처리 실패`가 남는다.

**사용자가 할 일 (한 번, 키 값은 저장소 · 채팅 · 워크플로에 적지 않는다)**:

1. Play Console → 앱 → 수익 창출 → 인앱 상품에서 위 id로 상품을 만든다 (가격은 products.json `priceKRW`와 맞춘다).
2. Google Cloud 콘솔(서버와 같은 프로젝트) → IAM → 서비스 계정 만들기 (예: `play-receipts`). 키 → JSON 키 만들기 (내려받은 파일은 비밀).
3. Google Cloud 콘솔 → API 및 서비스 → **Google Play Android Developer API** 사용 설정.
4. Play Console → 사용자 및 권한 → 새 사용자 초대: 2의 서비스 계정 이메일, 앱 권한 "재무 데이터 보기" · "주문 및 구독 관리". (반영까지 최대 하루 걸릴 수 있다.)
5. Secret Manager → 비밀 만들기 이름 `google-play-service-account`, 값 = 2의 JSON 파일 내용. 서버 실행용 서비스 계정(`GCP_RUNTIME_SA`)에 이 비밀의 **Secret Manager 보안 비밀 접근자** 역할을 준다.
6. GitHub 저장소 → Settings → Secrets and variables → Actions → **Variables**: `RECEIPT_VERIFIER` = `googlePlay`, `GOOGLE_PLAY_PACKAGE` = 앱 패키지 이름 (예: `net.itgold.unitygame1`). 비밀 이름을 바꿨으면 `GOOGLE_PLAY_SA_SECRET`.
7. Actions → `deploy-cloudrun` → Run workflow (변수만 바꾼 것이라 손으로 한 번). 끝.
- 되돌리기: `RECEIPT_VERIFIER`를 `off`로 바꾸고 다시 배포.

**기기 쪽 (데스크탑)**: Unity IAP 구매 성공 → `ProcessPurchase`에서 `Pending`을 돌려주고, 영수증(`product.receipt`)을 `store.purchase` 행동으로 서버에 보낸다 → 서버가 받아들이면 `ConfirmPendingPurchase`. 서버가 `unavailable`이면 나중에 다시 보낸다 (구매는 남아 있음).
