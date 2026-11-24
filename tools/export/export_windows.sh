#!/usr/bin/env bash
# Builds the Windows package of the game: build/CivSim2-windows.zip (the game, its .NET files, data/ and art/).
# Needs Godot 4.7.2 .NET with its export templates installed. Usage: tools/export/export_windows.sh [path to godot]
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
godot="${1:-godot}"
out="$root/build/windows"
rm -rf "$out" && mkdir -p "$out" "$root/build"
cd "$root/game"
"$godot" --headless --import --path . >/dev/null 2>&1 || true
"$godot" --headless --path . --export-release "Windows Desktop" "$out/CivSim2.exe" 2>&1 | tee "$root/build/export.log"
if grep -q "ERROR: Export" "$root/build/export.log" || [ ! -d "$out/data_CivSim.Game_windows_x86_64" ]; then
  echo "export failed: see build/export.log" >&2
  exit 1
fi
# The game reads its data, pictures and fonts from folders next to the exe (Main.FindDataDirectory, ArtLibrary.FindArtDirectory).
cp -r "$root/data" "$out/data"
# The fonts are read as plain files from next to the exe too (PixelTheme.LoadFile), not from the .pck.
mkdir -p "$out/fonts"
cp "$root"/game/fonts/*.ttf "$root"/game/fonts/*.otf "$root"/game/fonts/*OFL* "$root"/game/fonts/*LICENSE* "$out/fonts/" 2>/dev/null || true
mkdir -p "$out/art"
cp -r "$root/art/placeholder" "$out/art/placeholder"
[ -d "$root/art/packs" ] && cp -r "$root/art/packs" "$out/art/packs"
[ -d "$root/art/export" ] && cp -r "$root/art/export" "$out/art/export"
# Sounds are read as plain .ogg files from next to the exe too (AudioManager.FindAudioDirectory).
mkdir -p "$out/audio"
cp -r "$root/audio/music" "$root/audio/ambient" "$root/audio/sfx" "$root/audio/sounds.json" "$root/audio/CREDITS.md" "$out/audio/"
rm -rf "$out/data/schema/.git" 2>/dev/null || true
cd "$root/build"
rm -f CivSim2-windows.zip
(cd windows && zip -qr ../CivSim2-windows.zip .)
echo "built: $root/build/CivSim2-windows.zip"
