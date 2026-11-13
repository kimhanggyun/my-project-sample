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
| 배포 | GitHub Actions `deploy-cloudrun` (손으로 실행) | 비밀 키 대신 Workload Identity |

- 결제 영수증: 배포 서버는 `Game__AllowFakeReceipts=false` (가짜 영수증 거절). 실제 스토어 검증은 다음 단계.
- 마스터 계정 (R-86): 저장소 변수 `GAME_MASTERS`(계정 id, 여럿이면 쉼표) → `Game__Masters`. 바꾼 뒤 배포를 다시 돌린다.

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
3. 결제 영수증 실제 검증 (Google Play Developer API).
4. 사용자가 늘면 저장소를 Firestore로 (`IGameStore` 구현 추가) → 인스턴스 여러 대.
