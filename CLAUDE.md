# 프로젝트 안내 (AI·개발자 공통)

모바일 전략 전투 게임 (와룡전 전투 장면 참고, 자체 세계관). 설계(`design-v1`) 완료, 규칙 핵심 코드(순수 C#) 개발 중.

## 먼저 읽을 것
- `docs/README.md` — 문서 목록
- `docs/design/flow.md` — 전체 흐름
- `docs/design/structure.md` — 모듈 구조 (엔진 무관)
- `docs/dev-rules.md` — **개발 규칙 (반드시 지킬 것)**
- `docs/design/verification.md` — 규칙별 정답 예시
- `docs/impl/core.md` — 규칙 핵심 코드 구조

## 테스트
- `dotnet test GameCore.sln` (규칙 R은 `data/` 현재 값, 수치 N은 `tests/Game.Tests/Fixtures/balance.example.json`)
- `src/`는 Unity 로컬 패키지이기도 하다. `src/`에 파일·폴더를 추가하면 `python3 tools/unity_meta.py`로 `.meta`를 만든다 (빠지면 `UnityPackageTests` 실패).
- Unity 프로젝트: `unity/` (Unity 6.3 LTS `6000.3.28f1`, URP). Unity 안 테스트는 Test Runner의 EditMode (`GameCoreSmokeTests`).

## 핵심 규칙 요약
- 규칙 코드(전투·성장·경제 …)는 엔진·외부 서비스를 모른다. 의존은 화면 → 규칙 → 데이터 한 방향.
- 수치·문구는 데이터(JSON), 데이터 키는 영문 id.
- 외부 서비스(로그인·결제·광고·서버)는 인터페이스 뒤에, 설정으로 켜고 끈다. 꺼도 게임이 동작해야 한다.
- 새 기능은 기존 코드 수정이 아니라 구현 추가 + 등록으로.
- 무작위는 시드 난수 서비스(`SeededRandom`), 시간은 `ITimeProvider`에서만. 규칙 계산의 소수는 `Fix`(10진 고정소수점)만 쓴다 (float/double 금지).
- 게임 규칙을 바꾸면 설계 문서와 `verification.md`를 같은 변경에서 고친다.
- 테스트(단위·규칙 R·수치 N·기준 전투·데이터 검사)를 통과해야 완료.
