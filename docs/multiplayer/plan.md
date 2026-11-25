# Multiplayer przez sieć: plan

**Stan:** plan w trakcie uzgadniania z Patrykiem. **Planowanie zamknięte 2026-11-25** po 4 rundach pytań; decyzja architektoniczna: ADR 0031. Prace: CI1 → W1 → MP1 → … (rozdz. 13).
Kodu jeszcze nie zmieniamy; Patryk chce co najmniej dwóch kolejnych rund pytań przed budową.
Podstawa: kod `development` z 2026-11-25 (`645f2b0`), ADR 0008, 0010, 0028, `docs/projekt-gry.md` (rozdz. 2 i 9), `docs/known-issues.md`.
Pomysły na później (m.in. kooperacja jednym ludem): `docs/multiplayer/pomysly-na-przyszlosc.md`.

![Pośrednik i serwer na VPS](2_posrednik.png)

## 1. Decyzje Patryka (runda 1, 2026-11-25)

| # | Pytanie | Decyzja |
| --- | --- | --- |
| P1 | Ilu ludzi? | **2–8 ludzi.** Liczbę ludów (ludzie + AI) ustala się przy zakładaniu serwera. Najpierw dobry plan konfiguracji serwera (rozdz. 6). |
| P2 | Jak się łączyć? | **Bez VPN i bez Steam/EOS.** Wpisanie IP i portu serwera albo IP i portu **pośrednika** (relay). Pośrednik na VPS Patryka w Dockerze (rozdz. 4); konfiguracja pośrednika na sam koniec. **Gałąź serwerowa** z samym kodem serwera, aktualizowana z każdą wersją gry, z CI (rozdz. 8). |
| P3 | Rywalizacja czy kooperacja? | **Każdy swój lud.** Kooperacja jednym ludem kiedyś jako dodatkowy tryb (zapisane w `pomysly-na-przyszlosc.md`). |
| P4 | Prędkość? | **×1 albo ×2.** Gospodarz widzi informację, że ×2 jest trudniejsze. Zmiana prędkości w trakcie wymaga **głosowania i zgody większości**. Pauzy: 3 na gracza, po 30 s inni mogą przerwać. |
| P5 | Rozłączony gracz? | Jego lud od razu przejmuje AI; po powrocie gracz go odzyskuje. |
| P6 | Gracz, którego lud upadł? | **Widzi całą mapę.** Gra to „1 na wszystkich”; sojusze pomagają, ale odpadnięcie to sprawa gracza. |
| P7 | Kiedy? | **Osobny wątek, równolegle** z poprawianiem gry. Najpierw stabilna pętla rozgrywki (ok. 45 min do średniowiecza), potem nowe treści (zarządca itd.). |
| — | Docelowo | **Hostowanie gry ma być możliwe na VPS** (serwer bez okna, rozdz. 5). |

## 1a. Decyzje Patryka (runda 2, 2026-11-25)

| # | Pytanie | Decyzja |
| --- | --- | --- |
| R2-1 | Pośrednik: cały ruch czy tylko zapoznanie? | **Cały ruch** (rekomendacja). |
| R2-2 | Domena VPS? | VPS ma własną domenę, ale związaną z inną usługą (dalej: R3-2). |
| R2-3 | Kto zakłada gry na pośredniku? | **Tylko ktoś, kto zna hasło**; hasło zmienia się co jakiś czas, Patryk daje je zainteresowanym. Kto go nie ma: gra u siebie albo stawia **własnego pośrednika**; tę możliwość trzeba przygotować (dalej: R3-3, R3-4). |
| R2-4 | Kto zarządza partią? | **Gospodarz gry** (ten, kto ją założył). Żadnych haseł administratora do VPS w konfiguracji. |
| R2-5 | Nowy gracz w trwającej partii? | **Może tylko obserwować.** Może wybrać lud prowadzony przez AI i poprosić o przejęcie; gracze **głosują**, czy pozwalają. |
| R2-6 | Wolne miejsca przy starcie? | **Prowadzi je AI.** Później gracze mogą dołączyć i zająć miejsca dla graczy prowadzone przez AI, do ustalonego limitu ludzi. |
| R2-7 | Zaawansowane ustawienia świata? | **Wszystkie, w dwóch poziomach**, podgląd mapy później (PR W1). |
| R2-8 | Wersja gry? | **Tag wersji z datą** (np. `v0.1_25.11.26`); schemat do wyboru w R3-1 (rozdz. 16). |
| R2-9 | CI na `development`? | **Tak**, ale testy mają być krótkie: rozdzielić na lokalne i CI (rozdz. 17). |
| R2-10 | Czat? | **Do wszystkich i do sojuszników.** |
| R2-11 | Głosowanie o prędkość? | **Ponad połowa grających, 20 s, brak głosu = „nie”.** |
| R2-12 | Ludy razem? | **Do 8 teraz**, 12 po pomiarze późnej gry. |

## 1b. Decyzje Patryka (runda 3, 2026-11-25)

| # | Pytanie | Decyzja |
| --- | --- | --- |
| R3-1 | Wersje | **`v26.11.1`** (rok.miesiąc.numer). |
| R3-2 | Subdomena dla TLS | **Spróbujemy** dodać subdomenę wskazującą na VPS. |
| R3-3 | Hasło do zakładania gier | **Plik z kilkoma hasłami z datą ważności**, czytany bez restartu; stare hasło działa jeszcze ok. 7 dni. |
| R3-4 | Pośrednik dla innych | **Publiczny obraz Docker + instrukcja**; w grze pola „adres i hasło pośrednika”, domyślnie pośrednik Patryka. |
| R3-5 | VPS prowadzi partie? | **Na start tylko przekazuje ruch**; partie na VPS w drugim kroku, najwyżej 2 naraz. |
| R3-6 | Gospodarz wychodzi z partii na VPS | **Gospodarzem zostaje gracz, który najdłużej jest w partii.** Zapisaną partię trzeba móc odzyskać: gospodarz widzi listę partii zapisanych na VPS; rozsądny limit miejsca (rozdz. 20). |
| R3-7 | Obserwator | **Widzi całą mapę i nie może już przejąć ludu**; kto chce grać, czeka na głosowanie bez podglądu mapy. |
| R3-8 | Głosowanie o nowego gracza | Głosują tylko gracze, **którzy grają już przez chwilę**; nowo dołączeni przez jakiś czas nie mają prawa głosu. |
| R3-9 | Wyrzucanie | **Gospodarz albo głosowanie z przewagą ponad 50%** (remis = gracz zostaje). Powrót wyrzuconego: głosowanie reszty graczy (on nie głosuje); po zgodzie odzyskuje swój lud. Lud wyrzuconego jest **zablokowany** dla innych dołączających (mogą przejąć tylko inne ludy AI). |
| R3-10 | Testy | **Szybkie lokalnie, pełne w CI** (PR CI1). |
| R3-11 | Paczka gry z CI | **Dopiero gdy skończymy większość multiplayera.** |
| R3-12 | Tożsamość | **Pseudonim** wpisywany przez gracza i **zapamiętany na przyszłe partie** (w ustawieniach gry na jego komputerze). |

## 2. Pomiary (2026-11-25, kod `development`)

Osobny program w chmurze: serwer gry (`PlayHost`) i 1–8 klientów w pamięci, prędkość ×1; liczone bajty dokładnie tak, jak poszłyby przez sieć.

**Ruch na gracza**

| Partia | Start (jednorazowo) | Potem | Po kompresji (Brotli) |
| --- | --- | --- | --- |
| Nowa gra, 1. minuta | 99 KB | 3,6 KB/s | 2,1 KB/s |
| Scenariusz `02_bronze_age_empire` | 1,1 MB | 4,9 KB/s | 2,6 KB/s |
| Scenariusz `04_middle_ages` | 1,1 MB | 5,2 KB/s | 2,8 KB/s |

**Procesor i pamięć serwera** (cały proces, 40 s gry ×1, razem z liczeniem pomiaru)

| Partia | Klienci | Procesor | Pamięć |
| --- | --- | --- | --- |
| Nowa gra, 8 ludów | 1 | 3,4 s na 40 s (ok. 9% jednego rdzenia) | 109 MB |
| Nowa gra, 8 ludów | 8 | 3,5 s na 40 s (ok. 9%) | 214 MB |
| `04_middle_ages` | 1 | 3,5 s na 40 s | 125 MB |
| `04_middle_ages` | 4 | 3,5 s na 40 s | 201 MB |

Wnioski: widok dla kolejnego gracza prawie nie kosztuje procesora; pamięć rośnie o ok. 15–25 MB na gracza (bufory widoku).
Jedna partia zmieści się na najmniejszym VPS (1 rdzeń, 1–2 GB). Pośrednik przy 8 graczach przenosi ok. 40 KB/s w każdą stronę,
czyli ok. 150 MB w każdą stronę na godzinę partii. Do sprawdzenia w MP1: późna gra (90+ minut) z 8 ludami i najgorszy takt.

## 3. Architektura (bez zmian po rundzie 1)

- **Serwer autorytatywny:** tylko serwer liczy grę, sprawdza każdy rozkaz i wysyła każdemu graczowi widok przez jego mgłę wojny
  (ADR 0008; tak działa już gra solo). Nie ma rozjazdów stanu i nie da się podejrzeć mapy innych.
- **Gdzie działa serwer:**
  - **A. U gospodarza** (gra + serwer w jednym oknie, jak dziś solo);
  - **B. Na VPS** jako program bez okna w Dockerze (docelowo). Gra trwa, gdy gospodarz wyjdzie, a gospodarz nie ma przewagi
    (w wariancie A proces gospodarza zna cały stan gry).
- **Połączenie:** TCP z czystego .NET (`System.Net.Sockets`), ramka = 4 bajty długości + dzisiejsza ramka (wersja protokołu + MessagePack).
  Nowa klasa `TcpConnection` za istniejącym `IConnection`. Duże wiadomości (kawałki mapy) kompresowane Brotli.
- **Trzy sposoby dołączenia** (gracz wybiera w menu):
  1. **IP i port serwera** (LAN, serwer na VPS, otwarty port u gospodarza);
  2. **IP i port pośrednika + kod pokoju** (internet bez otwierania portów; rozdz. 4);
  3. (później) lista serwerów na pośredniku.

## 4. Pośrednik (relay) na VPS

### Tylko „zapoznanie” czy przekazywanie całego ruchu?

Patryk zaproponował pośrednika tylko do nawiązania połączenia. Tak działa tzw. *hole punching*: pośrednik podaje graczom swoje adresy,
a oni próbują połączyć się wprost. Ograniczenia:

- działa dobrze tylko dla UDP; dla TCP jest zawodne (zależy od routera);
- nie działa przy „symetrycznym” NAT i CGNAT (częste u operatorów komórkowych i części dostawców): wtedy i tak potrzebne jest przekazywanie;
- każdy gracz widzi IP gospodarza (a gospodarz IP wszystkich), więc łatwiej o atak na domowe łącze.

Dlatego **rekomendacja: pośrednik przekazuje cały ruch.** Kosztuje to mało (pomiar wyżej), działa zawsze i ukrywa adresy graczy.
Bezpośrednie połączenie po „zapoznaniu” można dodać później jako oszczędność, z przekazywaniem jako zapasem (tak robi np. Steam).

### Jak działa

1. Serwer gry (u gospodarza albo na VPS) łączy się do pośrednika i zakłada **pokój**; dostaje **kod** (np. `K7M-4QD`).
2. Gospodarz podaje kod znajomym. Gracz wpisuje adres pośrednika i kod.
3. Pośrednik łączy gracza z serwerem pokoju i od tej chwili tylko przekazuje bajty w obie strony. **Nie czyta gry** (nie dekoduje wiadomości),
   więc nie musi być aktualizowany z każdą wersją gry, a błąd w protokole gry go nie dotyczy.
4. Pokój znika, gdy serwer się rozłączy.

### Wdrożenie na VPS Patryka

- Osobny mały program .NET (`CivSim.Relay`), obraz Docker z gałęzi serwerowej, jeden port TCP (np. 7700).
- Plik konfiguracji albo zmienne środowiskowe: port, najwięcej pokoi, najwięcej połączeń z jednego IP, limit KB/s na pokój,
  czas bezczynności do zamknięcia, **hasło do zakładania gier** (R2-3), zmieniane co jakiś czas (R3-3). Dołączanie do istniejącej gry: kod pokoju (+ hasło partii, jeśli gospodarz je ustawił).
- Docker: osobna sieć od drugiej aplikacji, użytkownik bez uprawnień roota, system plików tylko do odczytu, limit pamięci (np. 128 MB) i procesora.
- Szyfrowanie TLS (rozdz. 9), jeśli VPS ma domenę.
- Instrukcja krok po kroku po polsku w `docs/multiplayer/vps.md` (powstanie w MP6).

## 5. Serwer na VPS (docelowo)

- Program bez okna: `CivSim.Server` + nowy punkt startu (np. `CivSim.DedicatedServer` albo `CivSim.Headless --serve`), bez Godota.
- Obraz Docker z gałęzi serwerowej; uruchomienie np. `docker compose up -d`.
- Konfiguracja w `server.json` (rozdz. 6) albo w zmiennych środowiskowych; zapisy w katalogu podpiętym z zewnątrz (volume).
- **Gospodarz** (R2-4): partią zarządza ten, kto ją założył (start, wyrzucenie gracza, zapis, koniec); żadnych haseł administratora w konfiguracji VPS. Kto zostaje gospodarzem, gdy gospodarz wyjdzie z partii na VPS: pytanie R3-6.
- Konsola serwera: lista graczy, zapis, wyrzucenie, zatrzymanie; dziennik do pliku (bez adresów IP, rozdz. 9).
- Może sam połączyć się z pośrednikiem (kod pokoju), albo przyjmować graczy wprost na swój port.
- Jeden proces = jedna partia; kilka partii = kilka kontenerów.

## 6. Konfiguracja serwera (plan ustawień)

Te same ustawienia w menu „Załóż grę” (wariant A) i w `server.json` (wariant B). Kto może zmienić: **G** gospodarz/administrator w lobby,
**P** tylko w pliku przed startem serwera.

| Grupa | Ustawienie | Domyślnie | Zakres | Kto |
| --- | --- | --- | --- | --- |
| Serwer | Nazwa partii | „CivSim game” | tekst do 32 znaków | G |
| | Hasło do partii | brak | tekst | G |
| | Port | 7777 | 1024–65535 | P / G przed startem |
| | Pośrednik: adres, port, hasło | puste (bez pośrednika) | — | P / G |
| | Autozapis | co 5 min | 0 (wył.) – 30 min | G |
| Gracze | Miejsca dla ludzi | 2 | 2–8 | G |
| | Ludy razem (ludzie + AI) | 4 | liczba ludzi – 8 (więcej po pomiarach) | G |
| | Puste miejsca przy starcie | prowadzi AI (R2-6) | — | — |
| | Dołączanie w trakcie | obserwator; przejęcie ludu AI z miejsca dla gracza po głosowaniu (R2-5, R2-6) | wł. / wył. | G |
| Gra | Cel partii | pierwszy w średniowieczu | jak w grze solo | G |
| | Prędkość startowa | ×1 | ×1, ×2 (×2 z ostrzeżeniem „trudniej”) | G; w grze tylko głosowanie |
| | Pauzy na gracza | 3 | 0–10 | G |
| | Czas na wybór startu | 30 s | 15–60 s | G |
| | Rudy | sprawiedliwe | jak w grze solo | G |
| Świat | Seed, rozmiar, ląd, morze | jak w grze solo | jak w grze solo | G |
| | **Zaawansowane ustawienia świata** (rozdz. 7) | zwinięte, „jak preset” | — | G |

W lobby gracze widzą wszystkie ustawienia (tylko do odczytu), a przy zmianie ustawienia wszystkim kasuje się „Gotowy”.

## 7. Zaawansowane ustawienia świata

**Gdzie są dziś:** w nowym menu (od `88a9be4`, „Save and load the game; a new start menu”) zostały tylko: seed, rozmiar mapy, ląd
(kontynenty / jeden ląd / wyspy), morze, liczba ludów, cel, rudy, wybór startu, samouczek. Zaawansowane ustawienia ze starego menu
**zniknęły z menu**, ale **generator świata nadal je obsługuje** (`WorldGenOptions` w `CivSim.Core/WorldGen`): nowa gra przekazuje mu
dziś tylko ląd i morze. Stare pola przeżyły jedynie jako parametry wiersza poleceń starego podglądu (`ViewerSettings`, legacy).

| Ustawienie ze starego menu | Generator dziś | Uwagi |
| --- | --- | --- |
| Wilgotność (suchy / normalny / wilgotny) | tak | |
| Wilgotność w głębi lądu (sucha / normalna / wilgotna) | tak | |
| Rzeźba (płaska / normalna / górzysta) | tak | |
| Złoża (skąpe / normalne / bogate) | tak | dziś nowa gra ma też własne „Rudy: sprawiedliwe / realistyczne” |
| Preset planety (Ziemia, Mars, Uran…) | tak | presety są w `data/planets.json` (plik legacy): trzeba je przenieść do `data/sim/` |
| Nachylenie osi, mimośród, jasność gwiazdy, odległość, rok, światło, CO₂, ciśnienie, wulkanizm, zmienność gwiazdy | tak (klimat przy generacji) | nowa gra ma własne pory roku; te liczby zmienią mapę (biomy, temperatura), ale nie długość zimy w grze |

**Zrobione w W1 (2026-11-25):** w „New game” przełącznik „Advanced world settings” (zapamiętywany), pod nim planeta (preset),
deszcz, wilgotność w głębi lądu, rzeźba, złoża; przycisk „Planet and star numbers (show)” odsłania 11 liczb planety i gwiazdy
(puste = jak preset). „Check the planet” pokazuje, czy z liczb da się zrobić planetę i co gra poprawiła; zły zestaw nie uruchamia gry.
Ustawienia trafiają do generatora (`PlayWorld` w `CivSim.Server`, `SimSettings` w `CivSim.Sim`) i do zapisu gry. Wyłączony przełącznik
albo same domyślne wartości = świat dokładnie taki jak dotąd. Morze zostaje z głównego ustawienia „Sea” (nie z presetu planety).
Presety planet są jeszcze w `data/planets.json` (wczytywane przez wspólny loader); przeniosą się do `data/sim/` przy wydzielaniu legacy (ADR 0029, krok 3).

Pierwotny plan: w „New game” zwijana sekcja **„Advanced world settings”** (domyślnie
zamknięta) z polami jak w starym menu, w dwóch poziomach: najpierw wilgotność, głąb lądu, rzeźba, złoża, preset planety;
niżej „Planet and star” z liczbami fizyki. `PlayOptions` i zapis gry dostają te pola; ta sama sekcja trafia do „Załóż grę”
i do `server.json`. Podgląd mapy (jak w starym menu) do decyzji w rundzie 2.

## 8. Gałąź serwerowa i CI

**Cel:** na GitHubie gałąź `server` z samym tym, czego potrzebuje serwer i pośrednik, aktualizowana razem z każdą wersją gry, z CI.

- **Zawartość:** `src/CivSim.Core`, `src/CivSim.Sim`, `src/CivSim.Protocol`, `src/CivSim.Server`, nowy punkt startu serwera,
  `src/CivSim.Relay`, `data/sim/`, testy serwera, `Dockerfile`, `docker-compose.yml`, `server.example.json`, krótki `README.md`.
  Bez `game/`, `art/`, `audio/`, legacy i dokumentów gry.
- **Aktualizacja:** gdy Patryk wydaje wersję gry (tag wersji, np. `v26.11.1`, rozdz. 16), workflow kopiuje te pliki na gałąź `server`
  jednym commitem „Server for v26.11.1” i taguje go `server-v26.11.1`. Ręcznie nikt tam nie pisze.
- **CI na gałęzi `server`:** build, testy serwera, test „dwóch klientów przez sieć”, budowa obrazów Docker serwera i pośrednika,
  publikacja w GitHub Container Registry (`ghcr.io/costellar/civsim2-server:26.11.1`). VPS pobiera gotowy obraz zamiast budować.
- **Koszt:** repo jest **publiczne**, a dla publicznych repozytoriów GitHub Actions na zwykłych maszynach i publiczne obrazy w GHCR są
  darmowe. Ograniczenie minut z 2026-11-04 dotyczy repozytoriów prywatnych; Patryk zgodził się na CI (R2-9).
- **Zgodność wersji:** serwer i klient sprawdzają przy połączeniu wersję protokołu, wersję gry i skrót danych `data/sim`; gracz z inną
  wersją dostaje czytelny komunikat („Serwer ma wersję v26.11.2, Ty v26.11.1”).
- **Dokumentacja:** opis gałęzi w `docs/multiplayer/serwer.md` i zasada w `CLAUDE.md` („nie commituj ręcznie na `server`”) w PR, który ją tworzy.

## 9. Bezpieczeństwo (research)

### Czego uczą inne gry

- Komputer gracza atakuje się przez dane, które jego gra **odczytuje z sieci**. Przykłady: w grach na silniku Source atakujący wysyłał
  zaproszenie albo prowadził własny serwer i przez błąd w odczycie danych uruchamiał swój kod u gracza (CVE-2021-30481, zaproszenia Steam;
  wcześniej exploit z animacją zabójstwa w TF2/CS). Check Point znalazł podobne błędy w bibliotece sieciowej Valve (2020).
  Wszystkie dotyczyły kodu w C++, gdzie błąd w odczycie daje dostęp do pamięci.
- Biblioteka MessagePack dla C#, której używamy, miała własne podatności: zalewanie kolizjami haszy (CVE-2024-48924, naprawione w 3.0.214),
  obejście blokady typów w trybie *Typeless* (CVE-2026-48517) i ogromne bufory przy dekompresji LZ4 (CVE-2026-48510), obie naprawione w 3.1.7.
  **Mamy 3.1.12** i tryb `UntrustedData`; nie używamy *Typeless* ani LZ4.

### Jak chronimy komputery graczy

| Zasada | Co to daje | Stan |
| --- | --- | --- |
| Kod gry w C# (.NET), nie C++ | błąd w odczycie daje wyjątek, a nie przejęcie pamięci | jest |
| MessagePack w trybie `UntrustedData`, aktualna wersja, bez *Typeless* i LZ4 | znane ataki na bibliotekę nie działają | jest; pilnować przy aktualizacjach (Dependabot w CI gałęzi `server`) |
| Limit rozmiaru ramki (np. 4 MB) **przed** przydzieleniem pamięci, limit po dekompresji | wiadomość nie zapcha pamięci | MP1 |
| Z sieci przychodzą tylko dane gry: nigdy kod, skrypty, mody, pliki ani ścieżki | serwer nie może niczego zainstalować ani nadpisać u gracza | zasada w CLAUDE.md (MP1) |
| Żadnych zasobów Godota z sieci (`bytes_to_var` z obiektami, `.tres`, `.tscn`) | zasoby Godota mogą zawierać skrypty | zasada w CLAUDE.md |
| Czat i nazwy jako zwykły tekst (bez BBCode i linków) | nikt nie wstawi linku ani obrazu do okna gry | MP4 |
| Klient sprawdza identyfikatory z widoku (zły numer nie wyłącza gry) | złośliwy serwer nie zawiesi klienta | MP1–MP4 (testy z losowymi danymi) |
| Gracze łączą się tylko „na zewnątrz” do pośrednika albo serwera | żadnych otwartych portów u graczy; adres IP gospodarza ukryty przez pośrednika | MP6 |
| Gra pobierana tylko z GitHub Releases, z sumą kontrolną | nikt nie podsunie podrobionej wersji | przy wydaniu |

### Jak chronimy serwer i pośrednika

- **Serwer sprawdza każdy rozkaz** (już jest: `SimGame.Apply`) i wysyła tylko to, co lud widzi (mgła na serwerze).
- **Błąd jednego klienta nie zatrzymuje gry.** Dziś wyjątek przy obsłudze rozkazu kończy wątek gry dla wszystkich (`PlayHost.Run`
  łapie wyjątek i staje). W multiplayerze: wyjątek przy wiadomości klienta = odrzucenie tej wiadomości (i ew. rozłączenie klienta), gra idzie dalej.
- Limity: rozkazów na sekundę na gracza (np. 30), wiadomości czatu, połączeń z jednego IP, czasu na przywitanie (np. 5 s), rozmiaru ramki.
- Hasło partii i hasło administratora; wyrzucenie gracza; porównanie haseł odporne na pomiar czasu.
- Pośrednik nie dekoduje gry, ma limity pokoi i transferu, kody pokoi z kryptograficznego generatora (poza symulacją, więc wolno),
  osobna sieć Docker, bez roota, tylko do odczytu, zaktualizowany obraz bazowy z CI.
- **Szyfrowanie TLS** (`SslStream` w .NET) do pośrednika i serwera na VPS: chroni hasła i czat przed podsłuchem w kawiarnianym Wi-Fi.
  Wymaga domeny wskazującej na VPS i certyfikatu Let's Encrypt (darmowy). Bez domeny: połączenie bez TLS albo certyfikat
  własny „przypięty” w grze.
- **Prywatność:** brak kont; dziennik bez adresów IP albo z krótkim terminem (w UE adres IP to dane osobowe).

### Oszustwa

- Podglądanie mapy innych: niemożliwe (klient nie dostaje pełnego stanu).
- Przyspieszanie gry, edycja zasobów: niemożliwe (liczy tylko serwer).
- Gospodarz w wariancie A ma pełny stan w swoim procesie i mógłby go podejrzeć: uczciwe partie na serwerze na VPS (wariant B).

## 10. Optymalizacja serwera (research + plan)

Co już mamy (i co robią gry RTS z serwerem autorytatywnym): **widok przez mgłę** (gracz dostaje tylko swoje okolice, tzw. *interest management*),
**różnice zamiast całości** (`PlayHost.Diff` wysyła tylko zmienione budynki, osady i jednostki), **stały rytm** (takt 100 ms,
widok co 100 ms, pełny widok co 250 ms) i **pomijanie spóźnionego klienta** (`Backlog`).

Do zrobienia:

| Pomysł | Po co | Kiedy |
| --- | --- | --- |
| Kompresja Brotli dużych wiadomości (kawałki mapy) | start partii 1,1 MB → ok. 125 KB | MP1 |
| Pomiar w serwerze: czas taktu (średni, 99. percentyl, najgorszy), czas widoku na gracza, bajty na gracza; zapis do dziennika co minutę | wiemy, kiedy VPS jest za mały | MP1 |
| Kompilacja z góry (ReadyToRun) obrazu serwera | mniej skoków przy pierwszych taktach (znane z pomiarów `--sim-report`) | MP6 |
| Mały obraz .NET (`runtime:10.0-noble-chiseled`), limit pamięci GC w kontenerze | serwer zajmuje ok. 150–250 MB i nie zabiera pamięci drugiej aplikacji | MP6 |
| Rzadszy widok dla graczy z wolnym łączem (np. co 200 ms) | płynna gra przy słabym internecie | MP4, jeśli potrzeba |
| Widoki dla graczy liczone równolegle | dopiero gdy pomiar pokaże, że to wąskie gardło (dziś nie: 8 graczy ≈ 1 gracz) | później |

## 11. Prędkość, pauzy, głosowanie

- Przy zakładaniu: ×1 albo ×2; przy ×2 napis „Gra na ×2 jest trudniejsza: mniej czasu na decyzje”.
- W grze: każdy gracz może zaproponować zmianę prędkości; okienko głosowania
  u wszystkich ludzi, którzy jeszcze grają, 20 s; zmiana, gdy **ponad połowa** głosujących zagłosuje „tak”; nie głosujący = „nie”.
  Ten sam gracz może zaproponować zmianę raz na 2 minuty. Widzowie (upadłe ludy) nie głosują.
- Pauzy: 3 na gracza; po 30 s każdy inny gracz może pauzę przerwać (już w kodzie, `PlayHost.SetSpeed`). Spowolnienie przy bitwie
  działa tylko w grze solo (już tak jest).

## 12. Co w kodzie zakłada dziś jednego gracza

| # | Gdzie | Co jest dziś | Co trzeba |
| --- | --- | --- | --- |
| 1 | `PlayHost.Receive` / `Pump` | Rozłączony gracz znika, ale jego lud zostaje „ludzki” i nikt go nie prowadzi (znany problem 19). | AI przejmuje lud; powrót oddaje go graczowi. |
| 2 | `PlayHost.Run` | Wyjątek przy rozkazie jednego gracza zatrzymuje grę dla wszystkich. | Odrzucić wiadomość, ew. rozłączyć klienta. |
| 3 | `PlayHost.Advance` | Kronika tylko dla ludu 0. | Każdy gracz dostaje swoją. |
| 4 | `PlaySaves`, `Replay` | Zapis zna tylko ludy ludzi w chwili zapisu (znany problem 11). | Dołączenie, wyjście, przejęcie przez AI jako komenda z taktem. |
| 5 | `PlayHost.Create` | Wybór startu czeka bez limitu. | W multiplayerze limit z ustawień (30 s). |
| 6 | `Receive` (`HelloMessage`) | „Następny wolny lud”, bez imienia gracza, bez hasła. | Miejsca z lobby, imię, kolor, hasło, klucz powrotu. |
| 7 | `SetSpeed` | Prędkość ustawia ostatni pytający, także Max. | ×1/×2, głosowanie (rozdz. 11). |
| 8 | `Receive` (debug) | Klawisze debug ma gospodarz. | Wyłączone w multiplayerze. |
| 9 | `HelloMessage` | Tylko wersja protokołu. | Też wersja gry i skrót danych. |
| 10 | Klient (`PlayGame.cs`) | Zawsze sam uruchamia serwer. | Tryb „dołącz”: samo połączenie. |
| 11 | `Save` | Nazwa i era od ludu 0, zapisać może każdy. | Zapis robi gospodarz/administrator; w zapisie gracze. |
| 12 | `SimGame.Create`, menu | Zaawansowane ustawienia świata nieprzekazywane. | PR W1 (rozdz. 7). |

## 13. Plan prac (PR-y)

| PR | Co | Jak sprawdzić |
| --- | --- | --- |
| **CI1. Podział testów i CI** (przed MP1) | Znacznik `Slow`, szybki zestaw lokalnie, pełny w CI na `development`, wersja gry `v26.11.1` w grze (rozdz. 16–17). | CI zielone na PR; szybki zestaw < 20 s. |
| **W1. Zaawansowane ustawienia świata** (niezależny) | Zwijana sekcja „Advanced world settings” w „New game”, pola w `PlayOptions` i zapisie, presety planet w `data/sim/`. | Zrzuty menu; te same mapy dla tych samych ustawień (test). |
| **MP1. Połączenie i serwer bez okna** | `TcpConnection`, nasłuch, limity ramek, Brotli, pomiary w serwerze; punkt startu serwera bez okna; bot `--join`. | Test: dwóch klientów przez sieć lokalną, ten sam hasz co w pamięci; pomiar późnej gry z 8 ludami. |
| **MP2. Serwer dla kilku ludzi** | Punkty 1–9 z rozdz. 12, limity rozkazów, hasło. | Testy: rozłącz/wróć, wyjątek klienta nie zatrzymuje gry, wczytanie partii z dołączeniem daje ten sam hasz. |
| **MP3. Konfiguracja i lobby** | Ustawienia z rozdz. 6, lobby (miejsca, gotowość, czat, ustawienia tylko do odczytu), menu „Multiplayer: Załóż / Dołącz (IP i port)”. | Zrzuty z dwóch okien; **pierwsza partia przez LAN albo otwarty port.** |
| **MP4. Gra z ludźmi** | Lista graczy z pingiem, czat (wszyscy, sojusznicy), głosowanie o prędkość i o przyjęcie nowego gracza, obserwatorzy, pauzy widoczne dla wszystkich, odpowiedzi na propozycje innego gracza, widz po upadku (cała mapa). | Scenariusz z dwoma botami; zrzuty. |
| **MP5. Zapis i wczytanie** | Zapis u gospodarza/administratora, wczytanie do lobby, powrót na swoje miejsca. | Zapis po 10 min z 2 graczami, wczytanie, ten sam hasz. |
| **MP6. Pośrednik, Docker, gałąź `server`** | `CivSim.Relay`, kody pokoi, TLS, obrazy Docker, workflow gałęzi `server` z CI i GHCR, instrukcja VPS po polsku. | Partia Patryka ze znajomym przez pośrednika na VPS; pomiar pingu i ruchu. |

Po rundach pytań: ADR 0031 („Multiplayer: serwer autorytatywny, TCP, pośrednik, serwer na VPS”) i wiersze w `ROADMAP.md`.

## 14. Ryzyka

| Ryzyko | Co robimy |
| --- | --- |
| Późna gra z 8 ludami może być cięższa niż zmierzone 9% rdzenia. | Pomiar w MP1 przed obietnicą „najmniejszy VPS wystarczy”. |
| Gospodarz w wariancie A wychodzi = koniec partii. | Autozapis; wariant B na VPS. |
| Pośrednik jest jednym punktem awarii i kosztem transferu VPS. | Limity transferu; połączenie wprost jako zapas; sprawdzić limit transferu u dostawcy VPS. |
| Długie wczytywanie dużej partii (zapis = lista rozkazów). | Pomiar w MP5; w razie potrzeby migawka stanu co N minut. |
| Różne wersje gry u graczy. | Wersja gry + skrót danych przy dołączeniu; gałąź `server` z tagami wersji. |
| Druga aplikacja na VPS. | Osobna sieć Docker, limity pamięci i procesora, bez roota. |

## 16. Wersje gry

Patryk chce daty w wersji (R2-8). Dwie sensowne drogi:

| Schemat | Przykład (dziś) | Plusy | Minusy |
| --- | --- | --- | --- |
| **A. Rok.miesiąc.numer** (kalendarzowy, jak nowy schemat Minecrafta „26.1”) | `v26.11.1` (pierwsze wydanie w listopadzie 2026), potem `v26.11.2`, `v26.12.1` | data widać od razu; liczby rosną same; pasuje do .NET, Dockera i GitHuba bez wyjątków | nie mówi, czy gra jest jeszcze „wczesna” (to mówi napis „Demo” w grze) |
| B. Numer + data | `v0.1.0` z datą w nazwie wydania („v0.1.0, 25.11.2026”) | klasyczny „0.x” = gra przed premierą | data tylko w opisie; trzeba decydować, kiedy 0.2, kiedy 1.0 |
| (propozycja Patryka) | `v0.1_25.11.26` | jedno i drugie | podkreślenie i kropki w dacie nie są poprawną wersją dla .NET i części narzędzi; data dzień.miesiąc.rok nie sortuje się |

**Rekomendacja: A** (`v26.11.1`). W grze w menu: „CivSim 2 · v26.11.1 · Demo”. Ta sama wersja trafia do pliku gry, do tagu Git,
do gałęzi `server` i do obrazu Docker (`ghcr.io/costellar/civsim2-server:26.11.1`). Serwer i klient muszą mieć tę samą wersję (rozdz. 8).

## 17. Testy: krótkie lokalnie, pełne w CI

Pomiar 2026-11-25 (chmura, 4 rdzenie, Release, po zbudowaniu):

| Zestaw | Testów | Czas | Testy ≥ 1 s | Najdłuższe |
| --- | --- | --- | --- | --- |
| `CivSim.Sim.Tests` | 186 | 27 s | 8 (34 s łącznie) | `TheAiKeepsThePeaceItMade` 10,9 s, scenariusze 4,9–6,5 s |
| `CivSim.Tests` | 232 | 13 s | 5 (16 s) | `ViewsDoNotChangeTheGame` 8,2 s |
| `CivSim.Legacy.Tests` | 417 | 30 s | 20 (67 s) | `LeadersStayOutOfTheChronicle…` 17 s |
| Razem | 835 | ok. 70 s + 28 s budowania | 33 | |

**Zrobione w CI1 (2026-11-25):** 37 testów ≥ 0,5 s oznaczonych `Slow` (24 w `CivSim.Sim.Tests`, 13 w `CivSim.Tests`). Szybki zestaw lokalnie:
`CivSim.Sim.Tests` 162 testy w 12 s, `CivSim.Tests` 216 testów w 7 s (razem ok. 19 s zamiast ok. 40 s dla nowej gry i 70 s dla całości).
CI: `.github/workflows/tests.yml` (Linux i Windows: szybkie, wolne, legacy). Stary `ci.yml` (wyłączony, z benchmarkiem gry legacy) usunięty.

Pierwotna propozycja:

- Testy dłuższe niż 1 s dostają znacznik `[Trait("Category", "Slow")]` (33 testy). **Lokalnie** (przed każdym commitem) idą tylko szybkie
  testy nowej gry: `dotnet test --filter "Category!=Slow"` w `CivSim.Sim.Tests` i `CivSim.Tests` (szacunkowo ok. 15 s zamiast 70 s).
- **CI na `development`** przy każdym PR: wszystkie testy, także wolne i legacy (ok. 2 min na GitHubie, za darmo w publicznym repo).
- **CI na gałęzi `server`:** testy serwera i protokołu + test „dwóch klientów przez sieć” + budowa obrazów Docker.
- Test, który pilnuje czasu: zestaw szybki dłuższy niż 30 s = ostrzeżenie w CI, żeby testy nie puchły po cichu.
- `CLAUDE.md`: zasada „lokalnie szybkie, pełne w CI; nowy test > 1 s oznacz jako Slow”.

## 18. Runda 3: pytania (zamknięta, odpowiedzi w rozdz. 1b)

| # | Pytanie | Rekomendacja |
| --- | --- | --- |
| R3-1 | Schemat wersji (rozdz. 16)? | **Rok.miesiąc.numer: `v26.11.1`.** |
| R3-2 | Czy możesz dodać subdomenę (np. `civsim.twojadomena.pl`) wskazującą na VPS? Nie rusza drugiej usługi, a daje darmowy certyfikat TLS. | **Tak, subdomena.** Bez niej: certyfikat własny wbudowany w grę (działa, ale przy zmianie trzeba nowej wersji gry). |
| R3-3 | Jak zmieniać hasło do zakładania gier? | **Plik na VPS z kilkoma hasłami, każde z datą ważności**; pośrednik czyta go co minutę (bez restartu). Stare hasło może działać jeszcze np. 7 dni, żeby nikt nie został odcięty w połowie tygodnia. Gracz wpisuje hasło raz, gra je pamięta. |
| R3-4 | Jak przygotować pośrednika dla innych? | **Ten sam obraz Docker publicznie w GHCR + instrukcja po polsku i angielsku** (`docker compose up -d`, port, hasło). W grze pole „Pośrednik: adres i port” + „Hasło”, z Twoim pośrednikiem wpisanym domyślnie. |
| R3-5 | Czy VPS ma też **prowadzić** partie (gracz z hasłem klika „Załóż grę na serwerze”), czy tylko przekazywać ruch, a serwer gry zawsze jest u gospodarza? | **Na start tylko przekazywanie** (VPS prawie nic nie liczy). Partie na VPS w drugim kroku, z limitem np. 2 naraz (ok. 10% rdzenia i 250 MB na partię). |
| R3-6 | Partia na VPS: gospodarz wychodzi. Co dalej? | **Gospodarzem zostaje gracz, który najdłużej jest w partii**; gdy wyjdą wszyscy ludzie, partia się zapisuje i kończy po 5 min. |
| R3-7 | Obserwator, który chce przejąć lud: co widzi, zanim go przyjmą? Gdyby widział całą mapę, po przejęciu znałby wszystkie sekrety. | **Obserwator widzi całą mapę, ale wtedy nie może już przejąć ludu.** Kto chce dołączyć, wybiera „Chcę grać”: widzi tylko listę ludów do przejęcia (bez mapy), czeka na głosowanie. Upadli gracze zawsze widzą całą mapę (P6). |
| R3-8 | Głosowanie o przyjęcie nowego gracza: kto i jak? | **Ludzie grający, ponad połowa, 30 s**; gospodarz bez weta. Przejęty lud zostaje z tym, co miał (AI go prowadziła). |
| R3-9 | Wyrzucanie gracza: tylko gospodarz czy głosowanie? | **Gospodarz**; wyrzucony lud przejmuje AI, a gracz nie wróci do tej partii. |
| R3-10 | Podział testów (rozdz. 17): lokalnie szybkie, pełne w CI? | **Tak**, osobny mały PR „CI1” jeszcze przed MP1. |
| R3-11 | Czy CI ma przy tagu wersji budować też paczkę gry dla Windows (Godot bez okna, eksport) i wrzucać ją do GitHub Releases? | **Tak, później** (po MP3): gracze pobierają grę z jednego miejsca, z sumą kontrolną; dziś paczki robi Twój komputer. |
| R3-12 | Tożsamość gracza: tylko pseudonim (bez kont), a klucz powrotu na swój lud zapisany na komputerze gracza? | **Tak.** Konta dopiero, gdyby gra trafiła do obcych ludzi (np. Steam). |

## 19. Dołączanie, obserwatorzy, głosowania, wyrzucanie (zasady po rundach 2–3)

- **Dołączenie w trakcie partii:** nowy gracz wchodzi jako **kandydat** (bez mapy, widzi tylko listę ludów do przejęcia) albo jako **obserwator**
  (cała mapa, bez możliwości przejęcia ludu w tej partii).
- **Kandydat** wybiera lud prowadzony przez AI z puli miejsc dla graczy (limit ludzi z konfiguracji); rusza głosowanie.
  Nie można wybrać ludu wyrzuconego gracza.
- **Kto głosuje:** gracze prowadzący lud, którzy grają w tej partii co najmniej **3 minuty** (R4-1), także ten, którego głosowanie dotyczy
  (np. wyrzucany), jeśli gra wystarczająco długo (R4-2). Obserwatorzy, kandydaci i upadli gracze nie głosują.
- **Wynik:** „tak” musi dać ponad połowa uprawnionych; remis i brak głosu = „nie” (R4-2, decyzja Patryka). Głosowanie trwa 30 s (o gracza) albo 20 s (o prędkość).
- **Wyrzucenie:** decyzja gospodarza albo głosowanie (ponad 50% uprawnionych, wyrzucany też głosuje, jeśli gra ≥ 3 min; remis = zostaje). Lud wyrzuconego
  prowadzi AI i jest zablokowany dla innych.
- **Powrót wyrzuconego:** jako kandydat na swój dawny lud; głosują pozostali uprawnieni; po zgodzie odzyskuje lud.
- **Powrót po rozłączeniu** (nie wyrzucony): bez głosowania, na swój lud (klucz powrotu na komputerze gracza).
- **Gospodarz wychodzi:** u gospodarza (wariant A) partia się zapisuje i kończy; na VPS (wariant B) gospodarzem zostaje gracz najdłużej w partii.
- Każde dołączenie, wyjście, przejęcie przez AI, przyjęcie i wyrzucenie to komenda z taktem w zapisie (determinizm, znany problem 11).

## 20. Zapisy partii na VPS (gdy VPS zacznie prowadzić partie)

- Gdy wyjdą wszyscy ludzie, partia na VPS zapisuje się i kończy po 5 minutach.
- **Lista zapisów** w grze („Partie na serwerze”): gracz z hasłem do zakładania gier widzi zapisy partii, w których grał (rozpoznaje go klucz
  z jego komputera); może taką partię wczytać i zostaje jej gospodarzem; reszta wraca na swoje ludy jak po rozłączeniu.
- **Limity** (R4-3): na partię ostatni autozapis + do 3 zapisów ręcznych; na cały serwer np. 200 MB; zapis nieużywany
  przez 30 dni jest usuwany; przy braku miejsca znika najstarszy nieużywany. Zapis to lista rozkazów, więc jest mały (do zmierzenia w MP5;
  szacuję poniżej 1 MB na godzinę partii).
- Zapisy w katalogu podpiętym do kontenera (volume), więc przeżywają aktualizację obrazu.

## 21. Runda 4 (zamknięta)

| # | Pytanie | Rekomendacja |
| --- | --- | --- |
| R4-1 | Ile trzeba grać, żeby mieć prawo głosu? | **Decyzja: 3 minuty.** |
| R4-2 | Większość liczona z uprawnionych (brak głosu = „nie”)? | **Decyzja: tak.** Gracz, którego głosowanie dotyczy, też głosuje, jeśli gra ≥ 3 min. |
| R4-3 | Limity zapisów na VPS (rozdz. 20)? | **Decyzja: autozapis + 3 ręczne na partię, 200 MB na serwer, usuwanie po 30 dniach.** |
| R4-4 | Dwóch graczy z tym samym pseudonimem w jednej partii? | **Decyzja: drugi dostaje losowy, zabawny dopisek** z listy ok. 50 wariantów (rozdz. 22), np. „Ania the Second”. |
| R4-5 | Czy planowanie jest zamknięte? | **Decyzja: tak.** ADR 0031; prace w tym wątku, jeden PR naraz: CI1 → W1 → MP1 → … |

## 22. Dopiski do powtórzonych pseudonimów (R4-4)

Gdy w partii jest już „Ania”, kolejna „Ania” dostaje losowy dopisek, którego jeszcze nikt w tej partii nie ma, np. „Ania the Late”.
Lista trafi do `data/sim/game.json` (teksty w grze po angielsku); Patryk może ją zmieniać.

the Second, the Other One, Doppelganger, the Late, the Lesser, the Echo, the Understudy, the Spare, the Sequel, the Remake, the Evil Twin, the Good Twin, the Copy, the Imposter, the Reflection, from Another Timeline, the Bootleg, Junior, the Rerun, Number Two, the Backup, the Shadow, Who Came Later, the Lookalike, the Knockoff, the Encore, the Mirror, the Unexpected, the Uninvited, the Also, Mk II, the Reprint, the Afterthought, the Other Other One, Who Was Here First (Allegedly), the Clone, the Duplicate, the Recurring, the Reissue, Again, the Unoriginal, the Forgery, the Stand-In, the Body Double, the Alter Ego, the Remix, the Second Draft, the Twice-Told, the Returning, the Also-Ran.

## Źródła (research 2026-11-25)

- Source engine RCE przez zaproszenia Steam (CVE-2021-30481): https://secret.club/2021/04/20/source-engine-rce-invite.html
- Exploit przez animację zabójstwa (Source): https://www.pcgamer.com/hackers-hijacked-pcs-using-source-engine-kill-animation-exploit
- Check Point: podatności w bibliotece sieciowej Steam (2020): https://research.checkpoint.com/2020/game-on-finding-vulnerabilities-in-valves-steam-sockets/
- Bezpieczeństwo gier sieciowych (Black Hat EU 2013): https://media.blackhat.com/eu-13/briefings/Ferrante/bh-eu-13-multiplayer-online-games-ferrante-wp.pdf
- Granica klient–serwer (Roblox): https://create.roblox.com/docs/scripting/security/client-server-boundary.md
- MessagePack CVE-2026-48517: https://advisories.gitlab.com/nuget/messagepack/CVE-2026-48517/
- MessagePack CVE-2026-48510: https://advisories.gitlab.com/nuget/messagepack/CVE-2026-48510/
- MessagePack CVE-2024-48924: https://osv.dev/vulnerability/CVE-2024-48924
- Pośrednik Steam Datagram Relay (jak działa przekazywanie z ukrywaniem IP): https://partner.steamgames.com/doc/features/multiplayer/steamdatagramrelay
- Pośrednik dla gier (otwarte źródła, przykład): https://github.com/RoccoDevs/RoccoRelayServer
- RTS: przepustowość przy 1000 jednostek: https://www.construct.net/en/blogs/ashleys-blog-2/rts-devlog-units-bandwidth-1603
- Architektura MMORTS: https://www.gamedeveloper.com/programming/-i-massively-multiplayer-game-development-2-i-architecture-and-techniques-for-an-mmorts
