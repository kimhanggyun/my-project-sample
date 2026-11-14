# 최신 화면 캡처 — 디자인 구현 (시안 01~06)

> 규칙: [collab.md](../../collab.md) 7장. 이 폴더 하나만 쓰고, 새 캡처가 오면 **덮어쓴다**. 폭 540px JPG, LFS 밖.

- 캡처: 2026-11-14 13:09 (01~05, 원본 `captures/20261114_130953`), 13:23 (06, 원본 `captures/20261114_132302`) `Game ▸ Capture All Screens`, 에디터 Play 1080×1920
- 코드 기준: 01~05 = `main` 1e5dc67 (#144), 06 = `desktop/troops-06`
- 비교 기준: [design/mockups/](../../design/mockups/README.md)의 각 화면 `mock_*.jpg`
- 지난 전투 확인 캡처(pt*)·기록은 PR #141 README(이 폴더의 이전 판)에.

## 시안과 비교

| 화면 | 캡처 | 시안 | 결과 |
|---|---|---|---|
| 01 타이틀 | [01_title](01_title.jpg) | [mock_connecting](../../design/mockups/01_title/mock_connecting.jpg) | 맞음. 그림 자리(그림 없으면 비움) + 붓 획 제목, 낙관·"눌러 시작" 없음. 캡처는 서버 연결 중이라 [시작]이 꺼진 회색 (준비되면 주홍) |
| 02 로그인 | [02_signin](02_signin.jpg) | [mock_default](../../design/mockups/02_signin/mock_default.jpg) | 맞음. 구글 흰 버튼(색 "G" 글자 — 공식 로고 그림은 아직), Apple 검은 버튼 꺼짐(흐림), 게스트 보조 버튼, 설명은 게스트 아래 |
| 03 약관 | [03_terms](03_terms.jpg) | [mock_default](../../design/mockups/03_terms/mock_default.jpg) | 맞음. 체크 상자 줄, 맨 위 "전체 동의", "보기 ›" → 본문 화면, 진행 안내 + 꺼진 [동의하고 시작] |
| 04 메인 틀 + 스토리 | [04_main_story](04_main_story.jpg) | [mock_story](../../design/mockups/04_main_story/mock_story.jpg) | 맞음. 자원 칩·가방/톱니 아이콘 버튼, 띠 배너 자리(광고 꺼짐 → 높이 0), 다음 도전 카드, 깬 스텝 ✓ 줄, 잠긴 스텝 자물쇠 줄 |
| 05 장수 탭 | [05_main_general](05_main_general.jpg) | [mock_default](../../design/mockups/05_generals/mock_default.jpg) | 맞음. 2열 카드(등급 테두리·꼬리표·Lv·능력치·작은 ⓘ), 정렬 ▾·모집·주홍 ⓘ. 초상 그림이 없어 원 안에 이름 첫 글자 |
| 06 부대 관리 | [06_main_troop](06_main_troop.jpg) | [mock_list](../../design/mockups/06_troops/mock_list.jpg) | 맞음 (지금 규칙 범위). 병종 그림, 병력(+반투명 부상)·훈련도·결속 게이지, [훈련]·[충원] 2단 버튼, 작은 ⓘ, "승급 n가지 ›". 승급 부대 나무 카드는 규칙 변경 뒤 |
| 06 승급 화면 | [06b_troop_promote](06b_troop_promote.jpg) | [mock_detail](../../design/mockups/06_troops/mock_detail.jpg) | 조건 체크 줄·꺼진 [승급]은 맞음. **승급할 인원 줄 없음**(규칙 변경 뒤), 위 자원 바·아래 탭 없음(따로 열리는 화면 — 규칙 작업 때 메인 틀 안으로) |

## 캡처로 못 본 것 (사람이 눌러 확인)

- 타이틀 준비 후 주홍 [시작], 화면 아무 곳 누름 시작
- 로그인 중 "로그인 중…"·실패 문구, 약관 체크 켜짐·본문 화면·다시 동의 줄
- 장수 카드 작은 ⓘ → 능력치 말풍선, [정렬 ▾] → 고르는 메뉴, 상태 띠(경상·중상·포로)와 할 일 점 (지금 데이터에 해당 장수 없음)
- 스토리 구출 카드 (갇힌 장수가 있을 때만)
- 부대 관리: 작은 ⓘ 병종 설명, [충원] 메뉴(징병·모병·치료)와 실제 충원, [훈련] 누름, 부상병 반투명 칸(지금 부상 0)

## 지난 버그 상태

- **B1 마스터 탭 지급·해금·장수 받기가 서버에서 거절됨** (PR #138 플레이 테스트): **해결** (PR #148·#149, 배포 완료). 서버 마스터를 계정 id 또는 구글 로그인 이메일로 지정 — 폰 구글 로그인·에디터 게스트 계정 모두 마스터로 확인됨.
