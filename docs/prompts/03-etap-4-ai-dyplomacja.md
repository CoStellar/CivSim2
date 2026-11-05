# Przekazanie pracy: start etapu 4 (AI i dyplomacja)

Stan na 2026-11-05. Etap 3 jest zamknięty (PR #22–#26 scalone do `development`). Etap 4 zaczyna nowa osoba lub sesja: ten plik mówi, co już jest, czego brakuje i od czego zacząć.
Na końcu jest gotowy prompt na nową sesję.

## Co już działa (stan `development`)

- **Świat:** planeta jako kula (ADR 0012), gwiazda i orbita z wzorów, bilans energii, cykl węglowy, epoki lodowe, poziom morza, lód morski (ADR 0013–0015).
- **Gospodarka i ludność (etap 3):** zbieranie, magazyny i psucie, 3 warstwy ludności, wzrost, głód, nastrój, strefy wpływów, migracje i odłamy,
  osady jako majątek (ruiny, przejmowanie), sieć handlowa jednej cywilizacji, receptury, rolnictwo i wioski, 14 technologii er 1–2, ery,
  pamięć mapy i wędrówki, zdarzenia (susza, pożar, powódź, epidemia).
- **Próba 2000 tur** (tabela w `ROADMAP.md`): bez załamań, 68–153 tys. ludzi, 9–12 z 12 cywilizacji żyje, tura 23–29 ms na mapie standard.
- Testy: 296 w ok. 15 s. Format zapisu 15. Opisy systemów: `docs/systems/`.

## Decyzje Patryka z 2026-11-05, które warto znać

- Zdarzenia zabierają ok. 20% ludności (89 zamiast 111 tys. przy seedzie 7, 1000 tur): **zostaje tak**.
- Handel w etapie 3 to tylko przesyłanie nadwyżek między osadami **jednej** cywilizacji (łańcuch ≤ 8 pól, 2% straty na pole, bez sprzedaży). Handel między
  cywilizacjami należy do etapu 4 (GDD 9.2).
- Wszystkie planety mają życie; symulacja rozstrzyga resztę.
- Testy i benchmarki tylko lokalnie (bez CI); zestaw testów ma trwać kilka sekund.

## Obecna AI (punkt wyjścia)

AI to dziś trzy proste systemy w fazie decyzji, każdy wydaje komendy przez `CommandQueue.Enqueue(PlayerId.Ai, civ.Id, cmd)`:

| Plik | Co robi | Ograniczenia |
| --- | --- | --- |
| `src/CivSim.Core/Ai/TribeAi.cs` | przenoszenie obozów, odłamy, osiedlanie (`SettleCommand`), wędrówki (`JourneyCommand`); ocenia pola po jedzeniu i wygodzie temperatury, tylko na znanych polach | rusza tylko z głodu albo zimna, więc puste kontynenty zostają niezasiedlone; ocena pól pomija premie z technologii |
| `src/CivSim.Core/Ai/EconomyAi.cs` | wybór receptur w osadach (`SetRecipeCommand`) | brak prognoz zasobów |
| `src/CivSim.Core/Research/ResearchSystem.cs` (`ResearchAi`) | bierze najtańszą dostępną technologię po zniżkach | brak wag celów (uczony z GDD 8.1) |

Brakuje całkiem: relacji między cywilizacjami (nawet „kto kogo zna”), umów, wojny, armii, charakteru, ustrojów i stabilności. Fazy `Combat` i `State` w
`TurnPhase` są puste. Strumienie losowe `ai` i `combat` już istnieją (`RandomStreamId`).

## Proponowany podział etapu 4 na PR-y (do akceptacji przez Patryka)

| PR | Zakres | GDD |
| --- | --- | --- |
| 4a | **Szkielet doradców i oceny działań:** wspólna ocena (iloczyn czynników z krzywych × waga celu × charakter − koszt), losowanie z 3 najlepszych na strumieniu `ai`, zapis 3 najważniejszych czynników („Dlaczego?”). Przenieść na nią `TribeAi`, `EconomyAi`, `ResearchAi` bez zmiany zachowania (test: ten sam hash albo te same metryki). | 8.1, 8.3, 8.7 |
| 4b | **Strateg, charakter, kierunek ekspansji:** 8 cech charakteru, wagi celów, mapa wartości wolnej ziemi z pamięci mapy; plemiona idą w nieznane, gdy jest powód, nie tylko z głodu | 8.2, 8.4, 7.6 |
| 4c | **Odkrywanie cywilizacji i pamięć relacji:** kontakt przez zwiad i zetknięcie stref, pogłoski, lista wydarzeń z wagą i wygasaniem, sympatia i zaufanie, wpis w kronice | 8.5 |
| 4d | **Dyplomacja i handel między cywilizacjami:** pokój, wojna, umowa handlowa, pakt, sojusz; propozycje oceniane przez dyplomatę drugiej strony; handel bez transportu fizycznego (jak w 3e), dyfuzja i choroby po kontaktach | 9.1, 9.2 |
| 4e | **Wojna:** cel wojny, armie na jednym polu, bitwa w rundach, uproszczone zaopatrzenie, wynik i zmęczenie wojną, pokój z warunkami, plemiona niezależne | 9.3, 9.4 |
| 4f | **Państwo:** 3–4 ustroje z tempem decyzji, stabilność, bunty, secesja, administracja; przywódca i sukcesja | 7.2, 8.4 |
| 4g | **Testy wsadowe:** 100 gier AI kontra AI w `CivSim.Headless` z metrykami (wojny, sojusze, upadki, głód bez reakcji) i raportem dla Patryka | 8.7 |

Kolejność ma znaczenie: 4a jest bazą dla reszty, 4c przed 4d i 4e.

## Pytania do Patryka (z rekomendacją)

1. **Epoka brązu przed etapem 4?** Dziś symulacja stoi przez ok. 45 tys. lat w neolicie, bo nie ma technologii er 3+. Rekomendacja: krótki PR z danymi
   technologii epoki brązu (miedź, brąz, pismo, koło) przed 4e, żeby wojny miały ciekawszy świat. Ale to wymaga zgody: ROADMAP przypisuje ery 3+ do etapów 6–12.
2. **Handel między cywilizacjami w 4d bez karawan?** GDD 9.2 mówi o fizycznych karawanach. Rekomendacja: w etapie 4 bez transportu (jak 3e), karawany
   razem z jednostkami na mapie.
3. **Ile umów na start?** Rekomendacja: pokój, wojna, umowa handlowa, pakt o nieagresji, sojusz obronny. Trybut, wasalstwo i federacje później.
4. **Armie w epoce kamienia:** czy plemiona mają walczyć (najazdy) już przed rolnictwem? Rekomendacja: tak, ale tylko proste najazdy wojowników
   (warstwa „wojownicy” już istnieje).

## Pułapki techniczne

- Determinizm: iteracja zawsze po id, `float`/`double` zakazane w logice (oceny AI w fixed-point, np. tysięczne części), losowanie tylko ze strumieni.
- AI liczona równolegle tylko czyta migawkę; jej komendy są sortowane przed rozstrzygnięciem (CLAUDE.md, zasada 4).
- AI widzi tylko to, co zna jej cywilizacja: korzystaj z `MapMemory.Knows` / `KnownCells`, nigdy z pełnej mapy.
- Zero alokacji w pętli tury: bufory wielokrotnego użytku (wzór: `PathFinder`, `TradeSystem`, `EventSystem`).
- `EffectBook` ma indeks po celu; nowe efekty (np. zmęczenie wojną) dodawaj przez dane w `data/effects.json`.
- Każdy nowy system: `docs/systems/<system>.md` z diagramem Mermaid; nowa decyzja techniczna: ADR w `docs/decisions/`.
- Środowisko: .NET 10 przez `dot.net/v1/dotnet-install.sh --jsonfile global.json --install-dir ~/.dotnet`, potem `export PATH=$HOME/.dotnet:$PATH`
  (także w poleceniach w tle). Klon bywa płytki: dla nowej gałęzi dodaj refspec. `gh` w sesjach Claude działa tylko przez REST (`gh api`).
- Benchmark porównawczy: `dotnet src/CivSim.Headless/bin/Release/net10.0/CivSim.Headless.dll --seed 7 --map-size standard --turns 1000 --report <plik>`,
  ten sam przebieg na gałęzi bazowej na tej samej maszynie (różnice między maszynami są większe niż 10%).

## Prompt na nową sesję

> Zaczynamy etap 4 CivSim 2 (repo `CoStellar/CivSim2`, gałąź `development`). Przeczytaj `CLAUDE.md`, `ROADMAP.md`, `docs/prompts/03-etap-4-ai-dyplomacja.md`
> i GDD rozdziały 7.2, 7.6, 8 i 9. Zainstaluj .NET 10, zbuduj i uruchom testy. Potem przedstaw Patrykowi plan sesji po polsku: podział etapu 4 na PR-y
> (propozycja w pliku przekazania) i pytania do decyzji z rekomendacjami. Kod zacznij dopiero po jego odpowiedzi, od PR 4a.
