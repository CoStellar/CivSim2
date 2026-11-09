# ADR 0025: Czas w grze na żywo: takt, pulsy cywilizacji i licznik lat

**Status:** przyjęta, 2026-11-09 (decyzja Patryka). Wdrożona w etapie 6 PR 1 (`feat/live-clock`): takt, pulsy, licznik lat, epizody
i liczniki wojny w taktach. Zastępuje ADR 0018 i 0022 (kroki w latach, krótki zegar epizodów) i turę decyzyjną ADR 0013. Tempa dalej
są „na rok” (ADR 0011) i przeliczane przez miesiące pulsu; strojenie pod minuty to etap 6 PR 2.

## Kontekst

Dziś gra liczy się w krokach świata (ADR 0018): każda cywilizacja ma krok co tyle lat, ile podaje jej era (50, 25, 20…),
a zdarzenia z przebiegiem (kampanie wojenne) mają osobny zegar miesięczny i zatrzymują świat (ADR 0022). W grze na żywo
(ADR 0024) to nie działa:
- marsz wojska nie może trwać „300 lat” w neolicie ani zatrzymywać reszty świata (znany problem 4: wojny spowalniają czas gry);
- gracz potrzebuje stałego rytmu w sekundach, niezależnie od ery;
- jedna tura liczy dziś całą gospodarkę i AI naraz, a w grze na żywo to musi być rozłożone w czasie.

Jak robią to inne gry (z wiedzy ogólnej): RTS-y (Age of Empires, StarCraft, Rise of Nations) mają stały takt symulacji,
a prędkość gry to liczba taktów na sekundę, bez kalendarza. Gry Paradoxu mają takt = 1 dzień, prędkość = dni na sekundę,
a podatki, ludność i AI liczą raz na miesiąc, rozłożone między kraje. Civilization ma tury, a lata na turę maleją z erami
i są tylko podpisem.

## Decyzja

Trzy warstwy czasu:

1. **Takt** (np. co 0,1 s przy prędkości ×1; liczba do strojenia). Liczy wszystko, co dzieje się „w akcji”: ruch oddziałów,
   budowę, walkę, transport, wykonanie zadań na obszarze. Rozkazy gracza i AI wchodzą w najbliższym takcie. Takt jest taki sam
   w każdej erze; szybsze jednostki daje technologia. Osobny zegar epizodów znika: kampania to oddziały w ruchu.
2. **Puls cywilizacji** (np. co ok. 3 s; do strojenia). Liczy rozwój: gospodarkę, ludność, naukę, doświadczenie w technologiach,
   decyzje AI i karty decyzji. Różne cywilizacje mają puls w różnych taktach (rozłożenie obciążenia). Ile dzieje się w jednym
   pulsie (przyrost, produkcja, nauka), ustala **era tej cywilizacji** w danych, więc każda cywilizacja rozwija się we własnym
   tempie, a z erą zmienia się charakter gry. Tempa w danych definiujemy na puls (albo na „rok etykiety” przeliczany przez puls;
   do ustalenia przy wdrożeniu), nie na prawdziwy rok historii.
3. **Licznik lat:** tylko liczba dla gracza, „historia ludu”. Idzie za najbardziej rozwiniętą cywilizacją i płynnie zwalnia, gdy
   ktoś wchodzi w nową erę (późne ery: mniej lat na tyle samo wydarzeń). Nic w symulacji nie liczy się od lat; kronika i wykresy
   tylko podpisują nimi zdarzenia.

**Wydajność:** takt jest tani (ruch i walka oddziałów); ciężka praca idzie w pulsach rozłożonych między takty i liczonych
równolegle na wielu rdzeniach (wyniki łączone w kolejności id, determinizm bez zmian). Jednostka to oddział (grupa ludzi), nie
człowiek. Klient rysuje tylko zmiany i płynnie dorysowuje ruch między taktami. Silnik zostaje (Godot 4 + .NET, ADR 0001):
wąskim gardłem jest pętla symulacji, nie grafika. Cel do sprawdzenia: mapa standard, 6–8 cywilizacji, 300 oddziałów,
takt poniżej 30 ms w najgorszym przypadku.

**Determinizm:** bez zmian (ADR 0003). Takt to licznik całkowity; prędkość gry zmienia tylko liczbę taktów na sekundę czasu
rzeczywistego, nie wynik.

## Rozważane alternatywy

- **Zostawić kroki w latach i przyspieszyć je:** marsz i walka dalej trwałyby lata; wojny dalej zatrzymywałyby świat.
- **Jeden zegar dla wszystkiego co takt:** gospodarka i AI co 0,1 s byłyby za drogie i bez sensu (przyrost ludności to proces wolny).
- **Wspólne ery dla wszystkich (Millennia):** odrzucone; Patryk chce, żeby tempo zależało od gracza i terenu.
- **Nowy silnik (Unity DOTS, Bevy):** niepotrzebny; rdzeń jest niezależny od silnika (ADR 0002), a koszt przepisania to miesiące.

## Konsekwencje

- Przebudowa pętli w `CivSim.Core` (`WorldClock`, `Episodes`, fazy tury), zapisu gry i migawek; nowe złote hashe.
- Strojenie wszystkich temp (przyrost, nauka, zbieranie) od nowa, pod puls. Proporcje i przyczyny zostają (np. higiena obniża
  śmiertelność dzieci), bezwzględne lata nie.
- `docs/systems/episodes.md` i ADR 0022 przejdą do historii; `war.md` opisze kampanie jako oddziały w ruchu.
- Przywódcy z imieniem dopiero w późniejszych erach.
