# NAS에서 게임 테스트 서버 돌리기

> 대상: 집 NAS(ASUSTOR AS1004T: 32비트 ARM, 메모리 512MB)처럼 Docker가 어려운 리눅스 기기.
> 서버(`server/Game.Server`)를 그 기기용 실행 파일로 만들어 **Docker 없이** 바로 실행한다. 테스트·개발용 (집 안 와이파이에서만).
> 나중에 상용 클라우드로 옮길 때는 같은 서버 코드를 컨테이너로 묶는다 (2단계).

## 0. 묶음 받기

- GitHub ▸ 저장소 ▸ **Actions** ▸ **nas-server** ▸ 맨 위 실행 ▸ 아래 **Artifacts**의 `game-server-nas` (zip 안에 `game-server-nas.tar.gz`).
  - main에 서버·규칙·데이터가 바뀔 때마다 새로 만들어진다. 바로 만들려면 nas-server ▸ **Run workflow**.
- 안에 든 것: `app/`(서버 실행 파일), `data/`(게임 데이터), `start.sh`·`stop.sh`·`status.sh`.

## 1. NAS 준비 (처음 한 번)

1. ADM ▸ **설정** ▸ **서비스** ▸ **터미널** ▸ **SSH 서비스 사용** 체크 ▸ 적용.
2. ADM ▸ **파일 탐색기**로 `Public` 같은 공유 폴더에 `game-server-nas.tar.gz`를 올린다 (Windows 탐색기에서 `\\192.168.219.105`로 열어 복사해도 된다).
3. ADM에 방화벽을 켰다면 5000번 포트(TCP)를 허용한다.

## 2. 실행

PC의 PowerShell:

```
ssh 관리자계정@192.168.219.105
```

NAS 안 (프롬프트가 바뀐 뒤):

```
cd /volume1/Public
tar -xzf game-server-nas.tar.gz
cd game-server-nas
sh start.sh
```

- "정상"이 나오면 PC 브라우저에서 `http://192.168.219.105:5000/v1/time` → 시각(JSON)이 보이면 된다.
- 상태·문제 확인: `sh status.sh` (기기 정보·응답·로그를 보여 준다. 문제가 있으면 이 출력을 그대로 보낸다).
- 멈추기: `sh stop.sh`. 새 묶음으로 바꿀 때는 멈추고 → 새 tar를 같은 곳에 풀고 → 다시 `sh start.sh` (세이브는 `store/`에 남는다).
- 포트·저장 위치 바꾸기: `PORT=5001 STORE=/volume1/Public/game-store sh start.sh`.

## 3. 게임을 NAS 서버에 붙이기

`config/services.development.json`:

```json
"online": "http",
"onlineUrl": "http://192.168.219.105:5000",
```

- 에디터는 바로 반영. 폰은 **개발 빌드**로 다시 빌드한다 (개발 빌드만 `http://`를 허용, 출시 빌드는 HTTPS).
- 폰과 NAS가 같은 와이파이여야 한다.

## 주의

- 공유기에서 5000번(또는 SSH 22번)을 **바깥으로 열지 않는다**. 밖에서 테스트해야 하면 Tailscale 같은 사설망을 쓴다.
- NAS를 껐다 켜면 서버도 다시 `sh start.sh` (자동 시작은 필요해지면 추가).
- 메모리가 적어 동시 접속은 몇 명 수준 (테스트용).
