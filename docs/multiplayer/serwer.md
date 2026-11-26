# Gałąź `server` i obrazy Docker

Plan: rozdział 8 `docs/multiplayer/plan.md`; instrukcja dla VPS: `docs/multiplayer/vps.md`.

## Co jest na gałęzi `server`

Tylko to, czego potrzebuje serwer gry i pośrednik (ustala to `server/assemble.sh`):

- `src/CivSim.Core`, `CivSim.Data`, `CivSim.Sim`, `CivSim.Protocol`, `CivSim.Server`, `CivSim.Headless` (serwer bez okna: `--serve`),
  `CivSim.Relay` (pośrednik);
- `data/` (dane gry, presety planet, schematy; ok. 0,5 MB);
- `tests/CivSim.Tests` (bez testu plików dźwięku) i `CivSim.Server.slnx`;
- `Dockerfile.server`, `Dockerfile.relay`, `docker-compose.yml`, `relay.example.json`, `passwords.example.txt`, `README.md` (po angielsku,
  z `server/README.md`).

Bez `game/`, `art/`, `audio/`, testów symulacji i legacy, dokumentów gry.

## Jak powstaje

Workflow `.github/workflows/server.yml` uruchamia się, gdy Patryk wydaje wersję gry (tag `v26.11.1`, ADR 0031), albo ręcznie
(Actions → Server → Run workflow, z nazwą istniejącego tagu):

1. sprawdza, że tag zgadza się z `ProtocolInfo.GameVersion`;
2. składa drzewo `server/assemble.sh` i buduje je **samo** (bez reszty repo), uruchamia wszystkie testy `CivSim.Tests` (protokół, serwer,
   pośrednik, gra dwóch klientów przez sieć i przez pośrednika);
3. zapisuje je na gałęzi `server` jako jeden commit „Server for v26.11.1” z tagiem `server-v26.11.1`;
4. buduje z niej oba obrazy i publikuje w GitHub Container Registry:
   `ghcr.io/costellar/civsim2-server:26.11.1` i `ghcr.io/costellar/civsim2-relay:26.11.1` (oraz `:latest`).

Nikt nie commituje na `server` ręcznie (zasada w `CLAUDE.md`). Workflow nie wrzuca tam własnych plików workflow (GitHub na to nie pozwala
zwykłemu tokenowi), więc testy gałęzi `server` robi ten sam workflow przed zapisem.

## Raz, po pierwszym wydaniu

Nowe obrazy w GHCR są domyślnie **prywatne**. Żeby VPS i inni mogli je pobrać bez logowania (R3-4): GitHub → profil organizacji/konta →
Packages → `civsim2-relay` → Package settings → Change visibility → Public; to samo dla `civsim2-server`. Robi się to raz.

## Sprawdzenie lokalnie

```sh
server/assemble.sh /tmp/server
cd /tmp/server && dotnet build CivSim.Server.slnx -c Release && dotnet test tests/CivSim.Tests -c Release
docker build -f Dockerfile.relay -t civsim2-relay . && docker build -f Dockerfile.server -t civsim2-server .
```

Pomiar 2026-11-26: drzewo buduje się samo, 280 z 280 testów przechodzi; obraz pośrednika 145 MB na dysku (45 MB do pobrania),
11 MB pamięci w pracy; obraz serwera 153 MB (47 MB do pobrania), ok. 70 MB pamięci w lobby małej mapy.
