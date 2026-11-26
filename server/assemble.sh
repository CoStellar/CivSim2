#!/usr/bin/env bash
# Copies what the game server and the relay need into a folder: the content of the `server` branch (docs/multiplayer/serwer.md).
# Used by .github/workflows/server.yml; run it locally to check the branch builds:
#   server/assemble.sh /tmp/server && cd /tmp/server && dotnet test tests/CivSim.Tests -c Release --filter "Category!=Slow"
# Needs: bash, run from the repository root.
set -euo pipefail
tree="${1:?usage: server/assemble.sh <folder>}"
rm -rf "$tree"
mkdir -p "$tree/src" "$tree/tests"
for p in CivSim.Core CivSim.Data CivSim.Sim CivSim.Protocol CivSim.Server CivSim.Headless CivSim.Relay; do
  cp -r "src/$p" "$tree/src/"
done
cp -r tests/CivSim.Tests "$tree/tests/"
rm "$tree/tests/CivSim.Tests/Data/AudioFilesTests.cs" # the server has no sound
cp -r data "$tree/" # data/sim, the planet presets and the schemas the shared loader checks (half a megabyte)
cp global.json Directory.Build.props .editorconfig "$tree/"
cp server/Dockerfile.server server/Dockerfile.relay server/docker-compose.yml server/relay.example.json server/passwords.example.txt "$tree/"
cp server/README.md "$tree/README.md"
sed -i 's#-f server/Dockerfile#-f Dockerfile#' "$tree/Dockerfile.server" "$tree/Dockerfile.relay"
find "$tree" -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
cat > "$tree/CivSim.Server.slnx" <<'SLN'
<Solution>
  <Folder Name="/src/">
    <Project Path="src/CivSim.Core/CivSim.Core.csproj" />
    <Project Path="src/CivSim.Data/CivSim.Data.csproj" />
    <Project Path="src/CivSim.Sim/CivSim.Sim.csproj" />
    <Project Path="src/CivSim.Protocol/CivSim.Protocol.csproj" />
    <Project Path="src/CivSim.Server/CivSim.Server.csproj" />
    <Project Path="src/CivSim.Headless/CivSim.Headless.csproj" />
    <Project Path="src/CivSim.Relay/CivSim.Relay.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/CivSim.Tests/CivSim.Tests.csproj" />
  </Folder>
</Solution>
SLN
echo "Server tree in $tree"
