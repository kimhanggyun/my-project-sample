# 두 Claude 협업 규칙

> 상태: 확정 (2026-11-13). 이 저장소는 **원격 Claude**(클라우드)와 **데스크탑 Claude**(사용자 PC) 둘이 함께 작업한다.
> 관련: [dev-rules.md](dev-rules.md) 7장(Git 규칙), [impl/unity/art.md](impl/unity/art.md)(에셋 넣는 방법·받은 에셋 목록), [worklog.md](worklog.md)(작업일지)

## 1. 왜 둘인가

| | 원격 Claude | 데스크탑 Claude |
|---|---|---|
| 어디서 | 클라우드 (저장소 사본) | 사용자 PC (`D:\my_unity_proj\my-project-sample`) |
| 할 수 있는 것 | 코드·문서·데이터, `dotnet test`, PR | 그 외에 Unity 에디터·폰 빌드, 무료 에셋 사이트 다운로드, Git LFS 올리기 |
| 할 수 없는 것 | 사용자 PC 접근, 외부 에셋 사이트 다운로드, LFS 올리기(막힘) | — |

## 2. 역할 나누기

| 담당 | 일 |
|---|---|
| **데스크탑** | 무료 에셋 찾기·받기·라이선스 확인, `.meta` 생성(`tools/unity_meta.py`) |
| | Unity 실행, EditMode 테스트, 캡처와 캡처 리뷰, Android 빌드 확인 |
| | 바이너리 파일(이미지·모델·소리, LFS) 추가 |
| | **에디터가 저장하는 파일**: 씬(`*.unity`), 프리팹, `ProjectSettings/`, `Assets/Settings/`, 재질(`*.mat`) |
| **원격** | 규칙 코드(`src/`), 화면 코드(`unity/Assets/Game/Scripts/`), 데이터 JSON, 설계·구현 문서, 서버, `dotnet test` |

- 같은 파일을 양쪽에서 동시에 고치지 않는다. 특히 씬·ProjectSettings는 병합이 어렵다.
- 상대 담당 파일을 꼭 고쳐야 하면, 고치지 말고 PR 인계 메시지에 "요청"으로 적는다.

## 3. Git

| 규칙 | 내용 |
|---|---|
| 브랜치 이름 | 데스크탑 `desktop/<주제>`, 원격 `claude/<주제>` (또는 기존 `feature/`·`docs/` 등) |
| 브랜치 주인 | 한 브랜치는 한쪽만 커밋한다. 상대 작업이 필요하면 `main`에 합쳐진 뒤 받아 간다 |
| main | 직접 push 금지. 브랜치 → PR → 테스트 통과 → 합치기 ([dev-rules.md](dev-rules.md) 7장) |
| 시작 전 | `git fetch` 후 최신 `main`에서 브랜치를 만든다. 열린 PR 목록을 보고 겹치는 파일이 있는지 확인 |
| 올리기 | 데스크탑은 push·PR 만들기 전에 사용자 확인을 받는다 |

## 4. 인계: PR 설명이 메시지다

- 작업을 넘길 때 **PR 설명란**에 인계 메시지를 쓴다 (양식: `.github/pull_request_template.md`, PR을 만들면 자동으로 채워짐).
- 상대는 "PR #N 읽고 이어서 해줘"만 듣고 시작할 수 있어야 한다. 필요한 경로·이유·남은 일을 PR 안에 다 적는다.
- 사용자에게도 같은 내용을 채팅으로 주면, 사용자가 상대에게 붙여 넣을 수 있다 (보조 수단).
- 합친 뒤 [worklog.md](worklog.md)에 한 줄을 추가한다 (5장).

## 5. 작업일지 ([worklog.md](worklog.md))

- PR 하나에 한 줄. **맨 아래에 추가만** 한다 (고치거나 지우지 않는다).
- 형식: `- 날짜 [원격|데스크탑] 브랜치 — 한 일 (PR #N)`
- 두 브랜치가 동시에 한 줄씩 추가해도 충돌하지 않도록 `.gitattributes`에서 `merge=union`으로 둔다 (두 줄 모두 남는다).
- 자세한 내용은 PR에 있다. 일지는 "최근에 누가 뭘 했나"를 훑어보는 용도.

## 6. 에셋

| 규칙 | 내용 |
|---|---|
| 라이선스 | CC0 우선. CC BY(출처 표시)는 허용하고 `CREDITS.md`에 적는다. **Unity Asset Store 에셋은 저장소에 넣지 않는다** (재배포 금지) |
| 넣는 곳 | [impl/unity/art.md](impl/unity/art.md)의 경로와 이름 규칙을 따른다 |
| 기록 | 받은 에셋은 [art.md](impl/unity/art.md) "받은 에셋 목록" 표에 적는다: 경로, 출처 URL, 라이선스, 크기, **생김새 설명** |
| 원격이 볼 수 없음 | 이미지·모델은 LFS라 원격에서는 내용이 아닌 포인터만 보일 수 있다. 원격은 목록의 설명과 경로를 보고 코드에서 연결한다 |
| 폰 성능 | 병사 모델 삼각형 500개 이하, `Resources`에는 쓰는 것만 |

## 7. 캡처·리뷰

- `captures/`는 Git에서 제외되어 있다. 원격은 캡처를 볼 수 없다.
- 데스크탑이 캡처를 보고 **글로 쓴 리뷰**(화면별 문제·제안)를 PR이나 문서에 남긴다.

## 8. 테스트

| 누가 | 무엇 |
|---|---|
| 원격 | `dotnet test GameCore.sln` |
| 데스크탑 | 위와 같음 + Unity EditMode(`GameCoreSmokeTests`) + 필요하면 캡처 확인 |

- PR 설명에 무엇을 돌렸고 결과가 어땠는지 적는다. 못 돌린 것도 적는다.
- 데스크탑 PC는 NuGet 소스가 비어 있으면 `dotnet restore --source https://api.nuget.org/v3/index.json`을 쓴다.
