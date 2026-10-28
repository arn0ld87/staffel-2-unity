#!/usr/bin/env bash
# Startet den Linux-Build auf einem Linux-Host in Docker (Xvfb, Software-Rendering) im Demo-Modus.
#   tools/linux-test/teste-linux-build.sh <build-ordner> <ergebnis-ordner> [episoden]
# Der Build ist nur lesend eingebunden; die VM des Spiels hat ohnehin kein Netz.
set -euo pipefail
BUILD=$(realpath "$1"); OUT=$(realpath -m "$2"); EP=${3:-0,1,2,3}
mkdir -p "$OUT"; chmod 777 "$OUT"
docker build -q -t staffel2-linux-test "$(dirname "$0")" >/dev/null
BIN=$(cd "$BUILD" && ls *.x86_64 | head -1)
docker run --rm --name staffel2-linux-test \
  -v "$BUILD":/spiel:ro -v "$OUT":/ergebnis \
  staffel2-linux-test bash -c "
    xvfb-run -a -s '-screen 0 1920x1080x24' /spiel/'$BIN' \
      -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 \
      -demo -episoden $EP -schnell \
      -protokoll /ergebnis/demo-protokoll.txt -spielstand /tmp/spielstand \
      -aufnahme /ergebnis/bilder -logFile /ergebnis/player.log; echo EXIT=\$? > /ergebnis/exit.txt"
cat "$OUT/exit.txt"; tail -5 "$OUT/demo-protokoll.txt"
