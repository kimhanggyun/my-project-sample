#!/bin/sh
# 게임 테스트 서버 시작 (NAS·리눅스, deploy/nas/README.md).
# 포트·저장 폴더는 환경 변수로 바꿀 수 있다: PORT=5000 STORE=/volume1/... sh start.sh
DIR="$(cd "$(dirname "$0")" && pwd)"
PORT="${PORT:-5000}"
STORE="${STORE:-$DIR/store}"
PID="$DIR/server.pid"
mkdir -p "$STORE"

if [ -f "$PID" ] && kill -0 "$(cat "$PID")" 2>/dev/null; then
  echo "이미 실행 중입니다 (pid $(cat "$PID"), 포트 $PORT)."
  exit 0
fi

export ASPNETCORE_URLS="http://0.0.0.0:$PORT"
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1   # ICU 없는 NAS에서도
export DOTNET_gcServer=0                         # 메모리 적은 기기
export DOTNET_GCHeapHardLimit=0x8000000          # 힙 128MB 상한
export Game__StoreRoot="$STORE"
export Game__DataRoot="$DIR/data"

chmod +x "$DIR/app/Game.Server"
nohup "$DIR/app/Game.Server" > "$DIR/server.log" 2>&1 &
echo $! > "$PID"
echo "시작했습니다 (pid $(cat "$PID")). 확인 중..."
sleep 5
if ! kill -0 "$(cat "$PID")" 2>/dev/null; then
  echo "서버가 바로 멈췄습니다. 로그:"
  tail -n 30 "$DIR/server.log"
  rm -f "$PID"
  exit 1
fi
if command -v wget >/dev/null 2>&1; then
  if wget -qO- "http://127.0.0.1:$PORT/v1/time" >/dev/null 2>&1; then
    echo "정상: http://<NAS 주소>:$PORT 로 접속하면 됩니다."
  else
    echo "실행은 됐지만 아직 응답이 없습니다. 잠시 뒤 'sh status.sh'로 다시 보세요."
  fi
fi
