#!/bin/sh
# 게임 테스트 서버 멈추기.
DIR="$(cd "$(dirname "$0")" && pwd)"
PID="$DIR/server.pid"
if [ -f "$PID" ] && kill -0 "$(cat "$PID")" 2>/dev/null; then
  kill "$(cat "$PID")"
  echo "멈췄습니다."
else
  echo "실행 중이 아닙니다."
fi
rm -f "$PID"
