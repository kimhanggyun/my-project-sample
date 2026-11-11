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

## 2. 사용자가 할 일 (한 번)

콘솔에서 하는 일이다. 막히면 화면을 캡처해 보내면 된다. **비밀번호·키 값은 채팅에 붙이지 않는다.**

1. **Google Cloud 프로젝트**: https://console.cloud.google.com 에서 새 프로젝트 (예: `ancientwar-dev`), **결제 계정 연결** (무료 사용량 안에서도 필요).
2. **Firebase**: https://console.firebase.google.com 에서 "프로젝트 추가" → 1번 프로젝트를 고른다 (같은 프로젝트를 쓰면 프로젝트 id도 같다).
   - Authentication → 시작하기 → 로그인 방법: **Google 사용 설정** (Apple은 막아 둔 상태이므로 나중에).
   - 프로젝트 설정 → 앱 추가 → **Android** → `google-services.json` 받기 (Unity SDK를 붙일 때 쓴다, 저장소에는 넣지 않는다).
   - **패키지 이름을 먼저 정한다**: 지금 Unity 프로젝트는 템플릿 기본값(`com.UnityTechnologies.com.unity.template.urpblank`)이다. 스토어에 올린 뒤에는 바꿀 수 없으므로 `com.<회사·개인 이름>.<게임 이름>` 형태로 정해 Unity(Player Settings → Other Settings → Package Name)와 Firebase에 같은 값을 넣는다. 서버 설정의 `PackageIds`도 같은 값으로 (지금 기본 `com.myproject.ancientwar`).
   - 디버그 서명 SHA-1 등록 (Unity 키스토어 기준, 방법은 SDK 붙일 때 안내).
3. **Cloud Shell**(콘솔 오른쪽 위 `>_`)에서 아래를 한 번 실행한다. `PROJECT`, `GH_REPO`만 바꾼다.

```bash
PROJECT=ancientwar-dev            # 1번 프로젝트 id
REGION=asia-northeast3
GH_REPO=kimhanggyun/my-project-sample
BUCKET=$PROJECT-game-store

gcloud config set project $PROJECT
gcloud services enable run.googleapis.com artifactregistry.googleapis.com iamcredentials.googleapis.com storage.googleapis.com
gcloud artifacts repositories create game --repository-format=docker --location=$REGION
gcloud storage buckets create gs://$BUCKET --location=$REGION --uniform-bucket-level-access

# 배포용 서비스 계정 + GitHub Actions가 키 없이 쓰게 (Workload Identity)
gcloud iam service-accounts create deployer --display-name="GitHub deploy"
SA=deployer@$PROJECT.iam.gserviceaccount.com
for ROLE in roles/run.admin roles/artifactregistry.writer roles/iam.serviceAccountUser roles/storage.admin; do
  gcloud projects add-iam-policy-binding $PROJECT --member=serviceAccount:$SA --role=$ROLE
done
gcloud iam workload-identity-pools create github --location=global
gcloud iam workload-identity-pools providers create-oidc github --location=global --workload-identity-pool=github \
  --issuer-uri=https://token.actions.githubusercontent.com \
  --attribute-mapping=google.subject=assertion.sub,attribute.repository=assertion.repository \
  --attribute-condition="assertion.repository=='$GH_REPO'"
POOL=$(gcloud iam workload-identity-pools describe github --location=global --format='value(name)')
gcloud iam service-accounts add-iam-policy-binding $SA --role=roles/iam.workloadIdentityUser \
  --member=principalSet://iam.googleapis.com/$POOL/attribute.repository/$GH_REPO

# 서버(Cloud Run 기본 서비스 계정)가 버킷에 쓸 수 있게
NUM=$(gcloud projects describe $PROJECT --format='value(projectNumber)')
gcloud storage buckets add-iam-policy-binding gs://$BUCKET --member=serviceAccount:$NUM-compute@developer.gserviceaccount.com --role=roles/storage.objectAdmin

echo "GCP_PROJECT_ID=$PROJECT"
echo "GCP_REGION=$REGION"
echo "GCP_WIF_PROVIDER=$POOL/providers/github"
echo "GCP_SERVICE_ACCOUNT=$SA"
echo "GCS_BUCKET=$BUCKET"
echo "FIREBASE_PROJECT_ID=$PROJECT"
```

4. 마지막에 찍힌 6줄을 GitHub 저장소 → Settings → Secrets and variables → Actions → **Variables** 탭에 같은 이름으로 넣는다 (비밀값이 아니라서 Variables).
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

## 4. 다음 단계

1. Unity: Firebase Auth SDK + 구글 로그인을 `auth.google` 구현으로 붙인다 (`google-services.json` 필요).
2. 기기 `HttpOnlineService`에 Firebase 로그인 호출 추가, 게스트 진행을 구글 계정으로 옮기기.
3. 결제 영수증 실제 검증 (Google Play Developer API).
4. 사용자가 늘면 저장소를 Firestore로 (`IGameStore` 구현 추가) → 인스턴스 여러 대.
