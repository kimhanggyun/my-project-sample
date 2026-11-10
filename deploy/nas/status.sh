#!/bin/sh
# 서버 상태와 NAS 환경 (문제가 있으면 이 출력을 그대로 보내 주세요).
DIR="$(cd "$(dirname "$0")" && pwd)"
PORT="${PORT:-5000}"
PID="$DIR/server.pid"
echo "== 기기"; uname -m; (ldd --version 2>&1 | head -n 1) || true; free -m 2>/dev/null || cat /proc/meminfo | head -n 3
echo "== 서버"
if [ -f "$PID" ] && kill -0 "$(cat "$PID")" 2>/dev/null; then echo "실행 중 (pid $(cat "$PID"))"; else echo "실행 중 아님"; fi
if command -v wget >/dev/null 2>&1; then
  printf "응답: "; wget -qO- "http://127.0.0.1:$PORT/v1/time" 2>&1 | head -c 200; echo
fi
echo "== 로그 (마지막 20줄)"; tail -n 20 "$DIR/server.log" 2>/dev/null || echo "(없음)"
