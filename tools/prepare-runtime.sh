#!/usr/bin/env bash
# Baut den Linux-Host (Bun-Einzelbinary für macOS arm64 und Linux x64) und legt ihn mit
# v86.wasm und dem VM-Image unter Game/Assets/StreamingAssets/linux/ ab.
#
# Das VM-Image stammt aus der Webversion (alexle135.de, scripts/mission-image/build.sh).
#   IMAGE_DIR   Verzeichnis mit alpine-fs.json, alpine-rootfs-flat/, alpine-state.bin.zst,
#               seabios.bin, vgabios.bin und bildschirm/ (Default: runtime/assets/linux)
set -euo pipefail
cd "$(dirname "$0")/.."
ROOT=$PWD
IMAGE_DIR=${IMAGE_DIR:-$ROOT/runtime/assets/linux}
OUT=$ROOT/Game/Assets/StreamingAssets/linux

for f in alpine-fs.json alpine-state.bin.zst seabios.bin vgabios.bin bildschirm/alpine-fs.json bildschirm/alpine-state.bin.zst; do
  [ -e "$IMAGE_DIR/$f" ] || { echo "Fehlt: $IMAGE_DIR/$f" >&2; exit 1; }
done

(cd runtime && npm ci --no-audit --no-fund >/dev/null)
mkdir -p runtime/dist "$OUT/assets"
bun build runtime/linux-host.mjs --compile --target=bun-darwin-arm64 --outfile runtime/dist/linux-host-darwin-arm64
bun build runtime/linux-host.mjs --compile --target=bun-linux-x64 --outfile runtime/dist/linux-host-linux-x64
cp runtime/node_modules/v86/build/v86.wasm runtime/dist/

rsync -a --delete runtime/dist/ "$OUT/" --exclude assets
rsync -a --delete "$IMAGE_DIR/" "$OUT/assets/"
chmod +x "$OUT"/linux-host-*
echo "Laufzeit in $OUT:"
du -sh "$OUT" "$OUT"/assets
