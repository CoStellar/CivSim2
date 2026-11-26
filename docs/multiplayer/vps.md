# Pośrednik i serwer gry na VPS: instrukcja krok po kroku

Dla Patryka (i każdego, kto chce postawić własnego pośrednika). Wersja po angielsku: `server/README.md` (na gałęzi `server` to jej `README.md`).

## Co to jest i po co

- **Pośrednik** (relay) to mały program na VPS. Gospodarz gry i gracze łączą się **do niego** (nikt nie otwiera portu w domu),
  a on tylko przekazuje bajty między grą a graczami. Nie czyta gry, nie zna jej zasad, więc nie trzeba go aktualizować z każdą wersją gry.
  Gracze nie widzą adresu IP gospodarza.
- Gospodarz zakłada grę w menu „Host a game” → „Players join: Through a relay”, wpisuje adres pośrednika i **hasło do zakładania gier**.
  Gra dostaje **kod pokoju** (np. `K7M-4QD`), widoczny w lobby i w liście graczy w trakcie gry.
- Gracz: „Join a game” → „Through a relay, with a room code”, adres pośrednika i kod. Hasło do zakładania gier nie jest mu potrzebne.
- **Serwer gry na VPS** (opcjonalnie): ta sama gra bez okna w Dockerze. Gra trwa, gdy gospodarz wyjdzie, i nikt nie ma pełnego stanu
  gry u siebie w komputerze (uczciwsze partie).

## Czego trzeba

- VPS z Linuksem i Dockerem (`docker compose version` działa). Pośrednik zajmuje ok. 11 MB pamięci i prawie nic procesora;
  serwer gry ok. 100–250 MB i do jednego rdzenia przy 8 cywilizacjach.
- Otwarty port TCP **7700** w zaporze VPS (u dostawcy i w `ufw`, jeśli jest).
- (Dla szyfrowania) subdomena wskazująca na VPS, np. `civsim.twojadomena.pl` (decyzja R3-2), i certyfikat Let's Encrypt.
- Ruch: partia 8 graczy to ok. 65 KB/s przez pośrednika, ok. 170 MB na godzinę w każdą stronę (pomiar z rozdz. 2 planu).

## Krok 1. Pliki na VPS

```sh
mkdir -p ~/civsim/config && cd ~/civsim
curl -O https://raw.githubusercontent.com/CoStellar/CivSim2/server/docker-compose.yml
curl -o config/passwords.txt https://raw.githubusercontent.com/CoStellar/CivSim2/server/passwords.example.txt
```

## Krok 2. Hasła do zakładania gier

Otwórz `config/passwords.txt` (`nano config/passwords.txt`). Jedno hasło w wierszu, z ostatnim dniem ważności:

```
# kto zakłada gry na moim pośredniku
zimowa-wydra 2026-12-31
styczniowy-bobr 2027-01-31
```

- Pośrednik czyta plik sam co minutę, gdy się zmieni: **nie trzeba restartu** (R3-3).
- Hasło działa jeszcze **7 dni po ostatnim dniu**, żeby nikt nie został odcięty w połowie tygodnia grania. Dopisz nowe hasło przed
  końcem starego i daj je znajomym; stare wygaśnie samo.
- Hasło bez daty działa zawsze. Wiersz zaczynający się od `#` to notatka.
- 5 złych haseł z jednego adresu w 10 minut = ten adres czeka 10 minut (nie da się zgadywać).

## Krok 3. Start

```sh
docker compose pull
docker compose up -d
docker compose logs relay
```

W dzienniku powinno być: `CivSim relay on port 7700 ... games open with a password (2 work today).`

Sprawdzenie z własnego komputera (z katalogu gry, jeśli masz .NET): `dotnet run --project src/CivSim.Headless -c Release -- --serve --relay ADRES_VPS --relay-password zimowa-wydra`
wypisze kod pokoju; drugi terminal: `--join ADRES_VPS --code KOD` pokaże ping przez pośrednika.

## Krok 4. Szyfrowanie (subdomena i TLS)

Bez domeny gracze wpisują **adres IP** pośrednika i połączenie nie jest szyfrowane (hasła i czat da się podsłuchać np. w publicznym Wi-Fi).
Z subdomeną gracze wpisują **nazwę** (`civsim.twojadomena.pl`), a gra sama łączy się przez TLS i sprawdza certyfikat.

1. U dostawcy domeny dodaj rekord `A` dla `civsim` z adresem IP VPS. Druga usługa na tej domenie się nie zmienia.
2. Certyfikat (jednorazowo; port 80 musi być na chwilę wolny):
   ```sh
   sudo apt install certbot
   sudo certbot certonly --standalone -d civsim.twojadomena.pl
   ```
   Jeśli na VPS działa już serwer WWW na porcie 80, użyj jego wtyczki (`--nginx` albo `--webroot`). Certbot sam odnawia certyfikat.
3. W `docker-compose.yml` odkomentuj (usuń `#`) dwa wiersze `CIVSIM_RELAY_CERTIFICATEFILE` / `CIVSIM_RELAY_KEYFILE` (wpisz swoją subdomenę)
   i wiersz `- /etc/letsencrypt:/certs:ro`.
4. `docker compose up -d`. W dzienniku: `TLS on`. Odnowiony certyfikat pośrednik weźmie sam, bez restartu.
5. Gracze wpisują w grze `civsim.twojadomena.pl` jako adres pośrednika. Adres IP dalej działa (bez szyfrowania).

## Krok 5 (opcjonalnie). Serwer gry na VPS

```sh
echo "CIVSIM_RELAY_PASSWORD=zimowa-wydra" > .env
mkdir saves && sudo chown 1654 saves     # użytkownik bez uprawnień roota w obrazie
docker compose --profile game up -d
docker compose logs game | grep code     # kod pokoju dla graczy
```

Ustawienia partii są w `docker-compose.yml` w wierszu `command:` (`--name`, `--seats`, `--civs`, `--size 98x60`, `--speed 1|2`, `--password`,
`--load <zapis>`). Pierwszy gracz w lobby jest gospodarzem i startuje grę. Zapisy są w `~/civsim/saves`. Jeden kontener = jedna partia.

## Aktualizacja

Gdy wyjdzie nowa wersja gry (np. `v26.12.1`), workflow sam zbuduje nowe obrazy. Na VPS:

```sh
cd ~/civsim && docker compose pull && docker compose up -d
```

Pośrednik nie zależy od wersji gry, więc aktualizacja jest potrzebna tylko dla poprawek pośrednika. Serwer gry musi mieć **tę samą wersję**
co gra graczy (gra pokaże czytelny komunikat, jeśli nie ma). Konkretną wersję wybierzesz w `.env`: `CIVSIM_VERSION=26.11.1`.

## Bezpieczeństwo w skrócie

- Pośrednik i serwer działają w osobnej sieci Dockera (nie widzą drugiej aplikacji na VPS), bez roota, z systemem plików tylko do odczytu,
  z limitem pamięci (128 MB / 768 MB) i procesora.
- Limity pośrednika: 32 gry, 16 graczy na grę, 24 połączenia z jednego adresu, 2 MB/s na grę, cisza dłuższa niż 5 minut zamyka połączenie,
  20 złych kodów z jednego adresu w 10 minut = przerwa. Zmiana: zmienne `CIVSIM_RELAY_MAXROOMS` itd. (lista w `server/README.md`).
- Dziennik nie zawiera adresów IP (w UE to dane osobowe).
- Kody pokoi są losowane kryptograficznie (25 znaków bez łatwych do pomylenia, 6 znaków: ok. 244 mln kombinacji).

## Gdy coś nie działa

| Objaw w grze | Co sprawdzić |
| --- | --- |
| „Could not reach the relay …: no answer” | port 7700 w zaporze VPS i u dostawcy; `docker compose ps` |
| „… its certificate is not valid for this address” | czy subdomena wskazuje na VPS i czy w compose jest certyfikat tej subdomeny; na razie wpisz adres IP |
| „Wrong password to open games on this relay.” | hasło i data w `config/passwords.txt` |
| „No game with the code …” | kod od gospodarza; gra gospodarza musi być włączona |
| „The game did not take you in time.” | gra gospodarza się zawiesiła albo ma pełne lobby |
