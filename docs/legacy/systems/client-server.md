# Klient i serwer

**W jednym zdaniu:** gra zawsze działa jako klient i serwer; serwer liczy symulację i sprawdza komendy, klient tylko wysyła komendy i dostaje swój widok.

**Faza tury:** poza turą. Serwer zbiera komendy między turami, uruchamia turę, gdy wszyscy gracze skończyli, i rozsyła widoki po fazie 11.

## Dane wejściowe i wyjściowe
- Czyta: wiadomości od klientów (`HelloMessage` z rolą gracz/obserwator, `SubmitCommandMessage`, `EndTurnMessage`, `SetSpeedMessage`).
- Zmienia: kolejkę komend, przypisanie cywilizacji do graczy, stan gry (tylko przez turę).
- Publikuje: `WelcomeMessage`, `ContentMessage` (nazwy i kolory z `data/`, raz przy powitaniu), `GameViewMessage` (tura, prędkość, czas tury, nowe wpisy kroniki),
  `MapChunkMessage` (fragment 32×32 mapy: przy powitaniu wszystkie znane, potem tylko zmienione), `CommandRejectedMessage`, `ConnectionRejectedMessage`.

## Przepływ

```mermaid
sequenceDiagram
  participant K as Klient (Godot)
  participant T as Połączenie (w pamięci, później sieć)
  participant S as GameServer
  participant R as TurnRunner (rdzeń)
  K->>T: Hello(wersja protokołu, nazwa)
  T->>S: Hello
  S-->>K: Welcome(gracz, cywilizacja) + widok tury 0
  K->>S: SubmitCommand(cywilizacja, komenda)
  S->>S: czy gracz rządzi tą cywilizacją? czy komenda jest znana?
  S->>R: komenda do kolejki
  K->>S: EndTurn(tura)
  S->>R: wszyscy gotowi → RunTurn()
  R-->>S: migawka
  S-->>K: odrzucone komendy + nowy widok (przefiltrowany)
```

## Zasady i wzory
- **Projekty:** `CivSim.Protocol` nie zależy od `CivSim.Core`. Klient (`game/`) widzi tylko Protocol, więc fizycznie nie może
  sięgnąć do stanu rdzenia. Serwer tłumaczy wiadomości na komendy rdzenia (`CommandMapper`) i stan na widok (`ViewBuilder`).
  To odstępstwo od tabeli w GDD 2 (tam Protocol zależy od Core): dzięki temu granica klient–serwer jest pilnowana przez kompilator.
- **Ramka:** 4 bajty wersji protokołu, potem treść w MessagePack. Inna wersja = odrzucenie połączenia.
  Numery w `[Union]` i `[Key]` są częścią formatu: można dopisywać, nie wolno zmieniać.
- **Połączenie w pamięci** (`InMemoryConnection`) też serializuje każdą wiadomość, więc gra jednoosobowa używa dokładnie
  tego samego formatu co przyszła gra sieciowa.
- **Serwer jest autorytatywny:** sprawdza, czy gracz przywitał się, czy rządzi wskazaną cywilizacją i czy komenda jest znana.
  Zasady gry sprawdza rdzeń w fazie 4; odrzucenia wracają do gracza z jego numerem komendy.
- **Przydział cywilizacji:** nowy gracz dostaje pierwszą żywą cywilizację prowadzoną przez AI.
  Kto rządzi cywilizacją, to dane sesji, a nie stan gry: nie wchodzi do hasha ani do zapisu (powtórka zapisuje przejęcie jako zdarzenie,
  `docs/systems/replay.md`).
- **Powtórka** (T11): serwer na życzenie (`GameServer.StartRecording`) zapisuje każdy rozkaz gracza, zmianę nazwy obserwatora i
  przejęcie cywilizacji z numerem taktu; host podglądu zapisuje plik przy zamknięciu (`LocalGameOptions.ReplayPath`, długi bieg zawsze).
- **Rozłączenie:** cywilizację przejmuje AI (GDD 2.1).
- **Koniec tury:** gdy wszyscy przywitani gracze wysłali `EndTurn` dla bieżącej tury. Limit czasu tury dojdzie razem z grą sieciową;
  `AdvanceTurn()` pozwala uruchomić turę bez graczy (obserwator, Headless).
- **Widok:** zawiera światy, nazwy cywilizacji i fragmenty mapy (`MapViewBuilder`). Pola, których klient nie zna, są wyzerowane;
  fragment bez żadnego znanego pola w ogóle nie jest wysyłany. Mgła wojny i pamięć mapy (etap 3) zastąpią dzisiejszy promień 4 od startu.
- **Obserwator** (protokół v2): nie rządzi cywilizacją (`WelcomeMessage.NoCiv = -1`), widzi całą mapę i jako jedyny może zmieniać
  prędkość symulacji. Gracz, który wyśle `SetSpeed`, dostaje odmowę.
- **Kronika:** serwer pamięta ostatnie 500 wpisów; nowy klient dostaje je raz, przy powitaniu.
- **Osady** (protokół v3): widok zawiera czas gry w miesiącach i listę osad. Gracz widzi swoje osady z zawartością magazynu,
  a cudze tylko na polach, które zna, i bez magazynu. Obserwator widzi wszystko. `ContentMessage` niesie też towary i nazwy poziomów osad.
- **Mapa** (protokół v4): fragmenty niosą dziką żywność i siłę wpływu. Gracz może wysłać komendy `MoveSettlementDto` i `SplitSettlementDto`; serwer sprawdza je tak samo jak komendy AI.
- **`LocalServerHost`:** uruchamia serwer w osobnym wątku i daje klientowi połączenie w pamięci (ADR 0010). Tury idą
  w tempie wybranej prędkości, tylko gdy podłączony jest obserwator; przy maks. widok wychodzi najwyżej co 250 ms, przy ×1/×2/×4 najwyżej co 100 ms (ADR 0025: takt co 100 ms przy ×1, więc przy ×4 widok co czwarty takt).
- **Klient, który nie nadąża** (poprawka z 2026-11-07, po 37 GB pamięci w kilkugodzinnej grze Patryka): połączenie mówi, ile wysłanych
  wiadomości druga strona jeszcze nie odebrała (`IConnection.Backlog`). Gdy klient ma ich więcej niż 256 (`GameServer.MaxBacklog`),
  serwer nie wysyła mu nowych widoków, tylko zbiera dla niego zmienione fragmenty mapy i najnowsze 500 wpisów kroniki. Gdy klient
  odczyta zaległości, dostaje raz najnowszy widok z tym, co ominął. Wcześniej przy maks. prędkości serwer wysyłał ok. 3 MB/s
  (widok, ~35 fragmentów mapy i warstwy co 250 ms), a okno czytało wolniej (albo wcale, gdy było zminimalizowane), więc kolejka
  w pamięci rosła bez końca.
- **Kronika w oknie** ma stały limit 2000 wpisów (lista w sesji i tekst w panelu); panel rósł wcześniej bez końca.

**Zapis gry** (`SaveGameSerializer`, po stronie serwera):
- Plik: `CS2S` + wersja formatu (4 bajty) + MessagePack skompresowany LZ4.
- Zawiera ustawienia, numer tury, czas gry, stany wszystkich generatorów losowych, światy, cywilizacje (z erą), osady z magazynami i aktywne efekty (format 5).
- Zapis idzie najpierw do pliku `.tmp`, potem podmienia właściwy plik, więc awaria nie zostawi uszkodzonego zapisu.
- Obcy plik albo nowsza wersja formatu = czytelny błąd zamiast awarii.

## Pliki danych
Brak.

## Testy
- `ProtocolSerializerTests`: każda wiadomość w obie strony, wersja w ramce, zła wersja, śmieci, stały format bajtów, połączenie w pamięci.
- `LocalServerHostTests`: obserwator i gracz przez wątek serwera (zawartość, mapa, znane pola, prędkość).
- `GameServerTests`: powitanie, zła wersja, koniec tury, odrzucanie cudzych i nieznanych komend, odrzucenia z numerem komendy,
  rozłączenie, oraz **komenda przez protokół daje ten sam hash co komenda podana bezpośrednio** (21 tur).
- `ObserverLayerTests.AViewerThatFallsBehindGetsNoPileOfViewsButCatchesUp`: klient, który przez 120 tur nic nie czyta, ma w kolejce
  najwyżej limit plus jedną rundę wiadomości, a po odczycie dostaje najnowszy widok z ominiętą kroniką i fragmentami mapy.
- `SaveGameTests`: zapis i odczyt dają ten sam hash; wczytana gra po 70 kolejnych turach jest identyczna z oryginałem; zapis do pliku; obce i nowsze pliki.

## Wydajność
Fragment mapy 32×32 to kilkanaście tablic po 1024 liczby; cała mapa standardowa (40 fragmentów) idzie raz, przy powitaniu.
Potem, do etapu 3, tury nie zmieniają mapy, więc widoki są małe. Pomiar razem z benchmarkiem w `CivSim.Headless`.
