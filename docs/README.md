# 문서

두 층으로 나뉜다.

| 층 | 폴더 | 내용 | 보관 |
|---|---|---|---|
| **설계 뼈대** | [design/](design/) | 무엇을 만드는가: 흐름, 규칙, 데이터, 모듈 구조. **엔진·언어와 무관** | 계속 보관 (`design-v1` 태그 예정) |
| **구현 선택** | [impl/](impl/) | 어떻게 만드는가: 엔진·버전·아트·SDK·클래스 구조 | 엔진별 폴더 |

처음 읽는 순서: **design/flow.md → design/planning-notes.md → design/content/ → design/structure.md → impl/unity/**

## 개발 규칙

| 문서 | 내용 |
|---|---|
| [dev-rules.md](dev-rules.md) | **개발 규칙**: 모듈·의존 방향, 캡슐화, 이식성, 외부 서비스 켜고 끄기, 확장성, 부작용 방지(테스트·변경 관리), Git, 완료 기준 |
| [collab.md](collab.md) | **두 Claude 협업 규칙**: 원격·데스크탑 역할, 브랜치, PR 인계 메시지, 에셋·캡처 (2026-11-13) |
| [worklog.md](worklog.md) | 작업일지: PR 하나에 한 줄 |

## 설계 뼈대 (design/)

| 문서 | 내용 |
|---|---|
| [flow.md](design/flow.md) | 전체 플로우, 단계별 담당 문서, 교체 단위 |
| [structure.md](design/structure.md) | 모듈 구조 (책임·입출력·확장 지점), 엔진 무관 |
| [verification.md](design/verification.md) | **검증 예시**: 규칙 예시(R) + 수치 예시(N), 기준 전투. 누가 구현해도 같은 답인지 확인 |
| [theme.md](design/theme.md) | 테마 교체 원칙 (옛날 전투 → 판타지 등) |
| [backlog.md](design/backlog.md) | **할 일 목록**: 설계 대비 미구현 전체, 진행 순서 A→F (2026-11-11) |
| [art-direction.md](design/art-direction.md) | 아트 방향: 수묵·한지 화면 + 단순 3D 전장 (2026-11-11 결정) |
| [planning-notes.md](design/planning-notes.md) | 논의·결정 기록, 미결 사항, 다음 단계 |
| [open-values.md](design/open-values.md) | **미결 수치 정리**: 지금 임시값, 측정 결과, 정하는 순서 |
| [review.md](design/review.md) | 전투 기획 검토 메모 |

### 컨텐츠 (design/content/)

| 문서 | 내용 | 상태 |
|---|---|---|
| [startup.md](design/content/startup.md) | 스플래시·로그인·약관 | 확정 |
| [main-screen.md](design/content/main-screen.md) | 메인 화면: 상단 바, 띠 배너, 하단 탭 | 확정 |
| [story.md](design/content/story.md) | 스토리 탭, 스텝, 대사, 출진 준비 | 확정 |
| [formation.md](design/content/formation.md) | 부대 편성, 초기 지급, 병력 관리 | 확정 |
| [battle.md](design/content/battle.md) | 전투 (실시간 군단 지휘) | 확정 (수치 미결) |
| [maps.md](design/content/maps.md) | 맵과 지형: 부품 목록, 높이, 맵 데이터, 유형 템플릿, 지형 변화, 제작 방식 | 뼈대 확정 (수치 미정) |
| [ai.md](design/content/ai.md) | 적 AI와 난이도: 계층, 작전 방침, 성향, 난이도, 스텝 스크립트, 위임 | 뼈대 확정 (수치 미정) |
| [skills.md](design/content/skills.md) | 스킬 체계: 10계열 약 35종, 반격 수단, 전조, 개발 순서 | 뼈대 확정 (수치 미정) |
| [formations.md](design/content/formations.md) | 진형 체계: 특성치, 10종, 상성, 병종·지형 궁합, 습득 단계 | 뼈대 확정 (수치 미정) |
| [troops.md](design/content/troops.md) | 병종 체계: 뼈대, 태그, 계열별 트리(T1~T3), 상성, 개발 순서 | 뼈대 확정 (수치 미정) |
| [generals.md](design/content/generals.md) | 장수: 획득(뽑기·등용·초기), 등급제, 데이터, 기본 스킬·스킬북, 확률 공개 | 기획 중 |
| [traits.md](design/content/traits.md) | 장수 성향 7종, 특성 약 40종(계열별), 특성 칸·획득·데이터 | 뼈대 확정 (수치 미정) |
| [secrets.md](design/content/secrets.md) | 숨은 보상(이스터 에그): 스토리·키우기 등에서 특별 조건으로 스킬·특성 획득 | 기획 중 |
| [captivity.md](design/content/captivity.md) | 포로·구출·등용 | 확정 |
| [economy.md](design/content/economy.md) | 군량·돈·결제 | 확정 (상품 일부 미결) |
| [ads.md](design/content/ads.md) | 광고, 광고 제거 상품 | 확정 |
| [hiring.md](design/content/hiring.md) | 장수·부대 등록과 고용, 전투 이력 (온라인) | 확정 (보상 미결) |
| [hiring-v2.md](design/content/hiring-v2.md) | 용병 개편: 닉네임 장수 용병, 빌리는 단위, 배신(첫 교전 직후 · 반간이면 낌새) | 확정 (2026-11-14), 구현 전 |
| [replay.md](design/content/replay.md) | 전투 녹화·재생, 공략 공유 | 기획 중 |
| [pvp.md](design/content/pvp.md) | 유저 대전 1:1 · N:N | 확장 예정 (구조만 대비) |
| [territory.md](design/content/territory.md) | 영토: 실제 지역(GPS) 땅따먹기, 지역 단계별 연합, 위치정보법·조작 방지 | 별도 컨텐츠, 기획 중 (서버 도입 후) |
| [territory-v2.md](design/content/territory-v2.md) | 영토 개편: 깃발 원형 땅(반지름 = 계급), 공성전(준비 1분 · 유저 대전/AI 수비), 전장 지원, 줌 단계, Natural Earth 지도 | 확정 (2026-11-14), 구현 전 |
| [general-growth.md](design/content/general-growth.md) | 장수 키우기 (스킬북·특성서·대결 보상 먼저, 세부 성장은 TO-BE) | 일부 먼저 열림 |
| [troop-growth.md](design/content/troop-growth.md) | 부대 키우기 | TO-BE |

## 구현 선택 (impl/)

| 문서 | 내용 |
|---|---|
| [impl/core.md](impl/core.md) | **규칙 핵심 코드 (순수 C#)**: 폴더, 의존 방향, 고정소수점·JSON·난수 결정, 테스트 실행 |
| [impl/server.md](impl/server.md) | **서버 기반**: 온라인 메시지·경계, 서버 전투 검증(R-40), 참고 서버 API |
| [impl/deploy-cloudrun.md](impl/deploy-cloudrun.md) | **서버 배포**: Google Cloud Run + Firebase 로그인, 사용자가 할 일 (2026-11-11) |
| [impl/unity/decisions.md](impl/unity/decisions.md) | Unity 버전·렌더링·3D 아트·SDK·백엔드 후보 (잠정, 설계 후 확정) |
| [impl/unity/art.md](impl/unity/art.md) | 임시 3D 아트 연결: 아트 목록, 무료 에셋 넣는 방법 |
| [impl/unity/checklist.md](impl/unity/checklist.md) | 에디터·폰 일괄 확인 목록 (설정 조합별 순서와 기대 결과) |
| [roadmap.md](roadmap.md) | 남은 일과 순서 (로컬 → 서버 → 수익·운영 → 출시), 진행 기록 |
| [impl/unity/architecture.md](impl/unity/architecture.md) | Unity·C# 클래스·폴더 구조 |
