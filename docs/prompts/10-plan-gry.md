# Plan gry do epoki brązu: przegląd z poziomu gracza i instrukcje dla agentów

Stan na 2026-11-13. Źródło: dokument „Przegląd planu CivSim 2 z poziomu gracza — propozycja” w Claude Docs
(https://claude.ai/artifact/Rxw5LyAhga7e7EGGonuTwL), decyzje Patryka z 2026-11-13 („zapisz to wszystko”). Zasady: ADR 0024, 0025, **0026**.
Ten plik uzupełnia `09-etap-gra-na-zywo.md` (T1–T14 i PR-y 1–3 zostają tam) i jest źródłem kolejności etapu 6 od PR 4a.

## Co pokazał pomiar (15 minut partii, 9000 taktów, mapa standard, 8 cywilizacji, seed 7)

| Minuta | Co widzi gracz |
| --- | --- |
| 0–4 | jeden obóz, 53 ludzi; obozy dzielą się (8 → 34); zero technologii, zero wiosek |
| 4–8 | pierwsze technologie (1–3 na cywilizację); 34 → 116 obozów; pierwsze wioski w 8. minucie |
| 9 | era 1; pierwsza (i jedyna z 8 wypowiedzianych) wojna z bitwą |
| 10–15 | 150 → 224 osad (ok. 25 na cywilizację); 20–50 tys. ludzi u czterech cywilizacji; 10 secesji: 8 → 18 cywilizacji |

Trzy z ośmiu cywilizacji przez całe 15 minut mają 21–78 ludzi. Decyzje AI: 2936 (3 na minutę na cywilizację), z tego 91 takich, które
gracz chciałby dostać jako kartę (wojna, pokój, umowa, ustrój). Wniosek: 4 puste minuty, potem wykładniczy wybuch; gracz potrzebuje
odwrotnej krzywej (coś do zrobienia od 1. minuty, 3–6 osad w 15. minucie).

## Zasady z 2026-11-13 (ADR 0026)

1. **Prototyp sterowania przed kolejnymi mechanikami.** Każdy PR etapu 6 od PR P ma punkt „jak sprawdzić ręcznie w prototypie”.
2. **Tempo i skala przed startem, technologiami i kartami.** Cele: pierwsza technologia ≤ 1 min, wioska ≤ 3 min, era 1 ≤ 5 min,
   3–6 osad w 15. minucie, żadna cywilizacja < 100 ludzi po 5 min. Raport `--milestones` na 10 seedach w każdym PR zmieniającym dane.
3. **Partia do brązu na mapie małej** (ok. 128×80) z 4–6 cywilizacjami; standard dla dłuższych partii.
4. **Podgląd technologii:** 5% × liczba cywilizacji, które ją znają, **tylko z kontaktu i spoza własnej secesji, limit 50%** (GDD 6.4).
5. **Secesja to karta decyzji** (ustąp, zapłać, puść), najwyżej raz na 5 minut; limit cywilizacji liczy tylko żywe.
6. **Karty decyzji tylko ze zdarzeń** z `cards.json` (wojna, pokój, umowa, secesja, głód, klęska), najwyżej jedna na 30 s, licznik 20 s
   czasu gry; mikro zostaje u AI.
7. **Start bez „z pamięci”:** losowa kolejność, 15 s z mapą bez złóż i żyzności, strefy blokady, AI z błędem, trzy zdania historii plemienia.
8. **Wycięte:** „zaliczenie technologii” (doświadczenie rośnie samo), pięć poziomów delegowania (zostają dwa przełączniki: osady same
   zlecają zadania, dowódca sam prowadzi wojnę), zwalniający licznik lat (pasek: era + minuta partii; lata tylko w kronice).
9. **Jeden ustrój (rada starszych) do epoki brązu;** wódz i ustroje jako karty od brązu.
10. **Każda technologia odblokowuje jedną widoczną rzecz** (zadanie, oddział, budowla, grafika osady); mnożniki są dodatkiem. Tkactwo
    i pieczenie chleba wycięte do brązu (tabela niżej).
11. **Zamrożone do brązu** (flaga w danych, kod zostaje): klimat dynamiczny (CO2, lody, cykl gwiazdy, morze; zostaje pora roku), ustroje
    i stabilność (jedna liczba „nastrój”), wpływ polityczny warstw (zostają fachy i myśliciele), pakty i sojusze (zostają kontakt, wojna,
    pokój, barter), sieci handlowe z cenami (wspólny magazyn osad w promieniu N pól), dryf dalekich osad, pożar i powódź (zostają susza
    i epidemia jako karty), odbudowa ruin (ruiny zostają), wiele światów.
12. **Rozwój widoczny:** osada zmienia wygląd z technologią (obóz → ognisko → wioska z polami → palisada → miasto), pasek postępu każdej
    cywilizacji i warstwa „era”, kamienie milowe w kronice z minutą partii (PR P); jedna liczba na osadę ze strzałką, „dlaczego” przy
    zdarzeniach z dziennika decyzji, ekran końca partii (etap 8).
13. Mapa zagrożenia (4c) i palisada (4d) po kartach (PR 7). Etap 7 zaczyna się od „wioska rośnie w miasto, większa wciela mniejszą”.
14. Cel partii „wszystkie technologie brązu” zostaje, z jawnym paskiem postępu wszystkich cywilizacji.

## Technologie do brązu: co widać po odkryciu

| Technologia | Co widać po odkryciu |
| --- | --- |
| ogień | ognisko w obozie (grafika), obóz może stać w tundrze |
| obróbka krzemienia | zadanie „krzemień” na złożu, wojownicy z grotami |
| łowy zespołowe | oddział łowców wychodzi na zwierzynę poza strefę |
| rybołówstwo | zadanie „łowisko”, łodzie na wodzie (grafika) |
| garbarstwo | odzież ze skór: licznik „głód/zimno” osady spada |
| tradycje | kronika ludu; pierwsza karta decyzji |
| rolnictwo | zadanie „pole”, pola na mapie, obóz → wioska |
| hodowla | stada w strefie, mięso bez łowów |
| garncarstwo | spichlerz: zapas rośnie, mniej psucia |
| kamieniarstwo | zadanie „kamieniołom”, palisada z kamienia, grafika wioski |
| nawadnianie | pola nad rzeką zielone w suszy |
| barter | karawana między osadami na mapie, pierwsza umowa |
| *tkactwo, pieczenie chleba* | *wycięte do brązu (mnożniki; chleb jako efekt rolnictwa)* |

Brąz (etap 7, ok. 8): miedź, cyna, wytop, odlew, pismo, koło, mur, łódź żaglowa; każda z jedną widoczną rzeczą. Trzy gałęzie: żywność,
rzemiosło, społeczeństwo. Kierunek rozwoju (GDD 6.4) = gałąź tańsza, bo lud jej używa.

## Etap 6 od PR 4a: kolejność

| # | Gałąź | Co | Brama / jak sprawdzić |
| --- | --- | --- | --- |
| 4a | `feat/area-tasks` | zadania w `tasks.json`, komendy, grupa robocza jako oddział; zbieranie ze strefy zostaje pod flagą do 4b; pomiar taktu z 200 grupami | takt < 30 ms przy 200 grupach |
| **P** | `feat/play-civ` | **prototyp sterowania**: „prowadź cywilizację” w menu, rola `Player`, widok przez mgłę, rozkazy z protokołu klikane na mapie, panel „co AI zrobiła za mnie”, pauza; grafiki osady wg technologii, pasek postępu, kamienie milowe w kronice | Patryk gra 15 min; lista „co nudzi, czego brakuje” |
| **T** | `feat/pace` | **tempo i skala**: `--milestones`, cele z zasady 2, przyrost gaśnie z gęstością osad, limit osadników z administracji, gwarancja startu, mapa mała, zamrożenia z zasady 11 | tabela minut dla 10 seedów; 3–6 osad w 15. minucie; zero martwych startów |
| 4b | `feat/area-tasks-ai` | gubernator zleca zadania; pola i łowiska jako zadania; koniec zbierania ze strefy poza żywnością | statystyki AI z 3 seedów bez spadku ludności > 10% |
| 5 | `feat/start-pick` | start z zasady 7 | Patryk zaczyna partię w prototypie |
| 6 | `feat/tech-mastery` | doświadczenie, kierunek, koszt od poprzedniej, podgląd z zasady 4; technologie wg tabeli wyżej | raport minut: rozrzut tempa między kierunkami |
| 7 | `feat/decision-cards` | karty z zasady 6, secesja jako karta | karty w prototypie; Patryk ocenia częstość |
| 4c | `feat/threat-map` | T6, napaść na grupę roboczą, ucieczka | dziennik dowódcy pokazuje wpływ mapy |
| 4d | `feat/build-on-site` | budowa w miejscu, palisada +50% obrony | odsetek wygranych obron w raporcie |
| 8 | `feat/live-perf` | T7, T8, T10, T13; test 300 oddziałów | takt < 30 ms, FPS u Patryka |

## Instrukcje dla agentów

### Zanim zaczniesz PR

1. `git fetch`, `development`, `dotnet test` (ok. 570 testów, 30 s). Test powtórki w podglądzie gra danymi hosta (`ContentLoader.Load()`),
   nie `TestContent` (puls co takt rozjeżdża hasz w takcie 600).
2. Przeczytaj `docs/known-issues.md`, `09-etap-gra-na-zywo.md` (T1–T14, „czego unikać”) i ten plik.
3. Nowa gałąź zawsze od `development`, nigdy od innej gałęzi feature (#73 trafił do `feat/battle-time`; trzeba było go przenosić jako #74).
4. PR przez `gh api repos/CoStellar/CivSim2/pulls -f base=development ...`. Po scaleniu sąsiedniego PR sprawdź `mergeable`; konflikty są
   zwykle w `ROADMAP.md` i w `docs/prompts/`.

### Wzór każdej mechaniki (zadania, karty, budowle)

1. **Dane:** `data/<x>.json` + `data/schema/<x>.schema.json` (`python3 docs/systems/tools/infer_schema.py data data/schema <x>.json`,
   potem klucze opcjonalne). Loader waliduje odwołania i zakresy.
2. **Stan:** płaskie tablice z id liczbowym i pulą (wzór `src/CivSim.Core/Units/`), zapis MessagePack z nowym kluczem i wartością
   domyślną, `SaveGameSerializer.FormatVersion + 1`, nowa część w `GameStateHash`.
3. **Komenda:** `ICommand` w Core, DTO w `CivSim.Protocol/Messages/Commands.cs`, sprawdzenie w `GameServer`. AI i gracz wydają tę samą.
4. **Takt lub puls:** ruch i walka w takcie (bez alokacji), produkcja i decyzje w pulsie; tempa w danych na rok, przeliczane przez
   miesiące pulsu ery (`TimeRules`). Nie ruszaj `time.json` do strojenia tempa.
5. **AI:** doradca w `src/CivSim.Core/Ai/`, parametry w `ai.json`, sytuacja w `DeadValueTests.MakeSituations` dopisana na końcu, wpis
   w `docs/ai/` (decyzja, `07-parametry`, `08-ocena`, `09-statystyki` z 3 seedów, historia w `README`).
6. **Podgląd:** `ObserverViewBuilder` → nowy `Key(n)` w wiadomości, `ProtocolInfo.Version + 1`, test zgodności, rysowanie w
   `game/scripts/Map/MapObjects.cs`, grafika zastępcza w `art/placeholder/generate.py`.
7. **Determinizm:** nowe złote hasze z komentarzem; losowanie ze strumieni; iteracja po id.
8. **Opis PR po polsku:** co, dlaczego, efekt z 3 seedów (7, 23, 42) przed i po, benchmark, **jak sprawdzić ręcznie w prototypie**,
   raport `--milestones`, testy. Wzrost taktu lub pamięci > 10% wymaga wyjaśnienia.

### PR P: prototyp sterowania (`feat/play-civ`)

- Serwer: `HelloMessage` z rolą `Player` już przydziela `PlayerId`; dopisz `ChooseCivilizationMessage` (serwer ustawia `Controller`)
  i `PlayerViewBuilder` (pola z pamięci mapy cywilizacji, własne osady i oddziały, obce tylko widoczne). Gdy gracz nie wyda rozkazu,
  doradcy działają jak dla AI; wyłączone są tylko decyzje, które gracz podjął w ostatnich N pulsach (N w `ai.json`).
- Klient: w menu „prowadź cywilizację nr”; kliknięcie oddziału + pola = `MoveUnitDto`; przyciski: załóż obóz, wróć, atakuj, zadanie
  (po 4a). Panel „AI zrobiła za Ciebie” z ostatnich 10 wierszy dziennika „dlaczego” tej cywilizacji. Pauza.
- Rozwój widoczny: grafika osady wg technologii (5 placeholderów), pasek postępu technologii do celu, kamienie milowe w kronice z minutą.
- To narzędzie testowe, nie interfejs z GDD 12.4. Zasada 10 (klient tylko przez `Protocol`) bez wyjątków.
- Testy: rozkaz gracza wykonuje się i AI nie cofa go przez N pulsów; gracz nie dostaje pól spoza pamięci mapy.

### PR T: tempo i skala (`feat/pace`)

- `CivSim.Headless --milestones out.csv`: wiersz na cywilizację i seed z minutą (takt / 600) pierwszej technologii, wioski, ery 1,
  5 osad, kontaktu, wojny, celu; `--batch` dodaje te kolumny; `docs/systems/tools/milestones_chart.py` rysuje rozrzut dla 10 seedów.
- Dźwignie w kolejności: `population.json` (mnożnik przyrostu maleje, gdy osad w promieniu 6 pól jest więcej niż N), `ai.json` (próg
  podziału obozu wyżej, osadnicy tylko przy wolnej zdolności administracyjnej), `eras.json` (miesięcy na puls neolitu),
  `research.json` i `technologies.json` (koszt pierwszych technologii).
- Gwarancja startu: ocena pola startu jak u doradcy osiedlenia (PR C), minimum żywności w promieniu 3 pól; `WorldGenerator` odrzuca start
  poniżej progu; nowy złoty hasz świata. Obóz, który 2 minuty nie rośnie, dostaje od AI wędrówkę.
- Mapa „mała” w `StartSettings` (szerokość 4k+2, wysokość parzysta) jako domyślna dla partii do brązu.
- Zamrożenia z zasady 11: flaga `enabled` w `planets.json` (klimat dynamiczny), `governments.json` (jeden ustrój), `strata.json`
  (wpływ polityczny), `diplomacy.json` (pakty, sojusze), `trade.json` (sieci z cenami → wspólny magazyn), `events.json` (pożar, powódź),
  `settlements.json` (dryf, odbudowa). System z flagą `false` nie liczy się w pulsie; testy tych systemów ładują dane z flagą `true`.

### PR 5, 6, 7: start, technologie, karty

- **Start:** `StartPhase` w `GameState` (150 taktów na cywilizację), `PickStartCommand(col,row)`, strefa blokady z promieniem ze strumienia
  `world`; AI przez doradcę osiedlenia z szumem z `ai.json`; mapa dla wybierającego bez złóż i żyzności (filtr w `PlayerViewBuilder`);
  historia plemienia: trzy zdania z `names.json` i biomu startu w kronice.
- **Technologie:** `TechnologyMastery[civ, tech]` 0–1000 w płaskiej tablicy, rośnie w pulsie od ludzi przy pracy w fachu technologii
  (`skills.json`), spada 1% na puls bez użycia; efekt = `effect × (1 + mastery/1000 × maxBonus)`. Koszt = `cost × (1 − min(0,5; 0,05 ×
  znających z kontaktu i spoza własnej secesji)) × (1 − 0,2 × mastery poprzedniej/1000)`. Kierunek rozwoju = waga w wyborze badania
  uczonego z `mastery` technologii pokrewnych (`related` w `technologies.json`). Każda technologia ma pole `unlocks.visible` (zadanie,
  oddział, budowla albo grafika osady); loader odrzuca technologię bez niego.
- **Karty:** `cards.json` (zdarzenie, warunek, opcje, domyślna opcja AI, licznik 200 taktów), kolejka na cywilizację z limitem 1 na 300
  taktów, `AnswerCardCommand`. Zdarzenia: wypowiedzenie wojny, propozycja umowy, secesja, głód osady, susza, epidemia. Secesja czeka na
  odpowiedź; bez odpowiedzi decyduje AI jak dziś.

### Czego nie robić w etapie 6

- Interfejsu gracza z GDD 12.4 przed etapem 8; prototyp to narzędzie.
- Nowych er, ustrojów ani postaci z imieniem.
- Zmian tempa w `time.json` zamiast w danych ery i ludności.
- Oddziałów per człowiek; grupa robocza to oddział.
- Sieci; transport w pamięci zostaje jedynym.
