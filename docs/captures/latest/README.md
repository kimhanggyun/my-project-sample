# 최신 화면 캡처 — 디자인 구현 1차 (시안 01~05)

> 규칙: [collab.md](../../collab.md) 7장. 이 폴더 하나만 쓰고, 새 캡처가 오면 **덮어쓴다**. 폭 540px JPG, LFS 밖.

- 캡처: 2026-11-14 13:09 `Game ▸ Capture All Screens` (원본 `captures/20261114_130953`), 에디터 Play 1080×1920
- 코드 기준: `main` 598cc39 (#143) + `desktop/design-impl-1` (공통 부품 + 시안 01~05 화면)
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

## 캡처로 못 본 것 (사람이 눌러 확인)

- 타이틀 준비 후 주홍 [시작], 화면 아무 곳 누름 시작
- 로그인 중 "로그인 중…"·실패 문구, 약관 체크 켜짐·본문 화면·다시 동의 줄
- 장수 카드 작은 ⓘ → 능력치 말풍선, [정렬 ▾] → 고르는 메뉴, 상태 띠(경상·중상·포로)와 할 일 점 (지금 데이터에 해당 장수 없음)
- 스토리 구출 카드 (갇힌 장수가 있을 때만)
