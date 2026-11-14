# 최신 화면 캡처 — 디자인 구현 (시안 01~06)

> 규칙: [collab.md](../../collab.md) 7장. 이 폴더 하나만 쓰고, 새 캡처가 오면 **덮어쓴다**. 폭 540px JPG, LFS 밖.

- 캡처: 2026-11-14 13:09 (01~05, 원본 `captures/20261114_130953`), 13:23 (06, 원본 `captures/20261114_132302`) `Game ▸ Capture All Screens`, 에디터 Play 1080×1920 / 18:40 (06·07 군대 개편 뒤, 원본 `captures/20261114_184026`) / 20:02 (07, 원본 `captures/20261114_200232`·`_200041`)
- 코드 기준: 01~05 = `main` 1e5dc67 (#144), 06·07 = `desktop/army-v2` (군대 개편 PR 1), 06b = #145·#146
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
| 06 부대 관리 | [06_main_troop](06_main_troop.jpg) | [mock_list](../../design/mockups/06_troops/mock_list.jpg) | 군대 개편 뒤: **병종 카드 5개** (예전 부대 13개가 세이브를 읽을 때 병종별로 합쳐짐 — 보병 7,149 등). 계급 십부장(최대 3,000)이라 넘친 병종은 [충원] "가득". 탭은 배포 줄 |
| 07 부대 편성 | [07_main_formation](07_main_formation.jpg) | [mock_main](../../design/mockups/07_formation/mock_main.jpg) | 맞음: 고정 머리(상황 탭·[복사]·[군단 편성 ›]·계급 띠·보유 병력 미니 카드), 접는 군단 카드(진형 미리보기·병종 칸 [−][n][+][최대][빼기]·잠긴 칸), 아래 [자동 편성][되돌리기][저장]. 예전 세이브라 계급 초과로 빨간 테두리·저장 꺼짐 (정책대로) |
| 07b 군단 편성 | [07b_formation_board](07b_formation_board.jpg) | [mock_board](../../design/mockups/07_formation/mock_board.jpg) | 맞음: 장수 미니 카드(놓임 흐림·제n군) + 3×3 판. 자리 없던 예전 군단은 기본 자리(앞 가운데·가운데 좌우)에. 장수 줄은 끌기 때문에 격자. ★ 표시(첫 군단 기본)는 이 캡처 뒤 고침 |
| 07c 부품 탭 | [07c_parts](07c_parts.jpg) | (공통부품.md) | 개발 줄 [부품]: 버튼·탭·체크·글자·미니 카드·강조·접는 카드 … 상태별 |
| 06 승급 화면 | [06b_troop_promote](06b_troop_promote.jpg) | [mock_detail](../../design/mockups/06_troops/mock_detail.jpg) | 조건 체크 줄·꺼진 [승급]은 맞음. **승급할 인원 줄 없음**(규칙 변경 뒤), 위 자원 바·아래 탭 없음(따로 열리는 화면 — 규칙 작업 때 메인 틀 안으로) |

## 캡처로 못 본 것 (사람이 눌러 확인)

- 타이틀 준비 후 주홍 [시작], 화면 아무 곳 누름 시작
- 로그인 중 "로그인 중…"·실패 문구, 약관 체크 켜짐·본문 화면·다시 동의 줄
- 장수 카드 작은 ⓘ → 능력치 말풍선, [정렬 ▾] → 고르는 메뉴, 상태 띠(경상·중상·포로)와 할 일 점 (지금 데이터에 해당 장수 없음)
- 스토리 구출 카드 (갇힌 장수가 있을 때만)
- 부대 관리: 작은 ⓘ 병종 설명, [충원] 메뉴(징병·모병·치료)와 실제 충원, [훈련] 누름, 부상병 반투명 칸(지금 부상 0)

## 지난 버그 상태

- **B1 마스터 탭 지급·해금·장수 받기가 서버에서 거절됨** (PR #138 플레이 테스트): **해결** (PR #148·#149, 배포 완료). 서버 마스터를 계정 id 또는 구글 로그인 이메일로 지정 — 폰 구글 로그인·에디터 게스트 계정 모두 마스터로 확인됨.
