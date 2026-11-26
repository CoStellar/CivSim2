# CivSim 2: game server and relay

This branch holds only what the CivSim 2 game server and the relay need. It is written by a workflow for each game version
(`Server for v26.11.1`, tag `server-v26.11.1`); nobody commits here by hand. The game itself lives on the main branches.

- **Relay** (`ghcr.io/costellar/civsim2-relay`): players and game servers connect *out* to it; it gives each game a room code and passes
  bytes between the game and its players. Nobody opens a port at home, and players never see the host's address. It never reads the game.
- **Game server** (`ghcr.io/costellar/civsim2-server`): a CivSim game without a window. It can take players on its own port, open a room on
  a relay, or both.

Both images are small (.NET chiseled, no shell), run as a user without root rights and need no writable file system except the server's
`/data` (saved games).

## Run your own relay

```sh
mkdir civsim && cd civsim
curl -O https://raw.githubusercontent.com/CoStellar/CivSim2/server/docker-compose.yml
mkdir config && curl -o config/passwords.txt https://raw.githubusercontent.com/CoStellar/CivSim2/server/passwords.example.txt
nano config/passwords.txt        # your passwords to open games, one per line, each with its last day: spring-otter 2026-12-31
docker compose up -d             # the relay listens on TCP port 7700
docker compose logs -f relay
```

Open TCP port 7700 in the firewall of the server. Give the password to the people who will host games. Joining needs no password from
this file, only the room code the host gives (and the game's own password, if the host set one).

- **Passwords** are read again within a minute when the file changes; an old password still works 7 days after its last day
  (`graceDays`). Without `passwordsFile` anyone may open games on your relay.
- **TLS**: with a domain pointing at the server and a Let's Encrypt certificate, uncomment the two certificate lines and the
  `/etc/letsencrypt` volume in `docker-compose.yml`. Players then type the domain (TLS, checked certificate); without a domain they type
  the IP address (no TLS). A renewed certificate is picked up without a restart.
- **Limits** (all optional, in the environment as `CIVSIM_RELAY_<NAME>` or in a JSON file, see `relay.example.json`): `maxRooms` 32,
  `maxPlayersPerRoom` 16, `maxConnections` 400, `maxPerAddress` 24, `roomKBps` 2048, `idleMinutes` 5, `reserveMinutes` 10. Wrong passwords
  (5) and wrong codes (20) from one address within 10 minutes are refused for a while. The log holds no IP addresses.

A game of 8 players passes about 65 KB/s through the relay (about 170 MB an hour each way); the relay itself takes about 11 MB of memory.

## Run a game server

```sh
docker compose --profile game up -d     # a game next to the relay; its room code is in the log
docker compose logs game | grep code
```

The server's settings are its command line (`command:` in `docker-compose.yml`): `--name`, `--seats` (places for players), `--civs`,
`--size 98x60`, `--seed`, `--speed 1|2`, `--password` (to join), `--load <save>`, `--relay host[:port]` and `--relay-password`
(or `CIVSIM_RELAY_PASSWORD`). Next to the relay the game reaches it as `relay` (a name without a dot: no TLS inside the Docker network);
set `CIVSIM_PUBLIC_ADDRESS` in `.env` to the address players type (your domain or IP), shown with the room code. The first player in is the host and starts the game from the lobby. Saved games are in `./saves`.

## Build from source

```sh
docker build -f Dockerfile.relay -t civsim2-relay .
docker build -f Dockerfile.server -t civsim2-server .
dotnet test tests/CivSim.Tests -c Release
```
