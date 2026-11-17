# Co robią lepiej podobne gry (przegląd z 2026-11-16)

Cel: znaleźć sprawdzone rozwiązania na trzy problemy, które widać w pomiarach CivSim 2 (`2026-11-16_sesja-1.md`):
gracz nie czuje sprawczości, ekran jest zalany komunikatami bez znaczenia, a postęp ludu jest mało widoczny.
Każdy punkt kończy się wnioskiem „dla nas”, a propozycje z numerami są w `propozycje.md`.

## 1. „Ciekawa decyzja” (Sid Meier, GDC 2012)

Meier opisuje grę jako serię ciekawych decyzji. Decyzja jest ciekawa, gdy:
- ma **koszt** (wybierając A, tracę B);
- jest **sytuacyjna** (dobra odpowiedź zależy od tego, co się teraz dzieje na mapie);
- pozwala wyrazić **styl gracza** (ostrożny albo agresywny);
- jej skutek **trwa** długo;
- gracz ma **dość informacji**, żeby ją podjąć (lepiej za dużo niż za mało).

**Dla nas:** w dzisiejszym prototypie najczęstsza decyzja gracza to wybór technologii. Pomiar pokazuje, że kolejność technologii
prawie nie zmienia wyniku po 15 minutach (P-03), a gracz nie widzi, co dana technologia mu da „tu i teraz”. Brakuje kosztu i sytuacji:
każda technologia jest po prostu „następna”. Trzy propozycje uczonego (PR 6) powinny pokazywać przy każdej opcji jedno zdanie
„co zyskasz na swojej ziemi” (np. „+30% jedzenia: 4 pola nad rzeką”), czyli informację, której gracz potrzebuje.

Źródło: [GDC 2012: Sid Meier on how to see games as sets of interesting decisions](https://gamedeveloper.com/design/gdc-2012-sid-meier-on-how-to-see-games-as-sets-of-interesting-decisions).

## 2. Opowieść z symulacji (RimWorld, Tynan Sylvester)

- **Apofenia:** gracz dopowiada sobie więcej historii, niż mechanika zawiera. Wystarczy dać mu kilka nazwanych rzeczy z przyczyną.
- **Mało nazwanych bohaterów:** kolonia ok. 12 osób, każda z imieniem i historią, zamiast 200 anonimowych. Przywiązanie rośnie z liczbą
  wspólnych zdarzeń.
- **Przyczyna przy każdym zdarzeniu:** załamanie postaci zawsze ma podaną myśl, która je wywołała.
- **Usuwanie tego, co już nie ma znaczenia:** zapomniane myśli i wyleczone choroby znikają z ekranu, blizny zostają jako zaczep historii.
- **Narrator (storyteller)** steruje rytmem: zagrożenie, oddech, znowu zagrożenie; siła zdarzeń rośnie z bogactwem kolonii.

**Dla nas:** w 15 minutach kronika CivSim 2 ma ok. 2000 wpisów (ponad 2 na sekundę), z czego 68% to śmierć i zmiana przywódców,
a tylko ok. 3% to rzeczy, które gracz pamięta (technologia, kontakt, wojna, nowa wioska). To odwrotność zasady Sylvestra. Propozycje
P-01 (wyciszyć przywódców do brązu) i P-05 (kronika gracza tylko o jego ludzie i sąsiadach, z limitem). Rytm „zagrożenie i oddech”
pasuje do kart decyzji (PR 7): karta co 30 s to dobra górna granica, ale warto dodać dolną (coś co najmniej co 2 minuty).

Źródła: [How RimWorld Generates Great Stories (Rock Paper Shotgun)](https://www.rockpapershotgun.com/2016/08/12/how-rimworld-generates-great-stories),
[AI Storytellers (RimWorld Wiki)](https://rimworldwiki.com/wiki/AI_Storytellers).

## 3. Zdarzenia z wyborem (Old World, Soren Johnson)

- Ponad 3000 zdarzeń, każde z **wyzwalaczem** (np. spotkanie nowego ludu) i **warunkami** (np. bezdzietny władca).
- Podmioty zdarzenia (postać, miasto, technologia) są brane z bieżącego stanu gry, więc zdarzenie zawsze dotyczy czegoś, co gracz zna.
- Skutki zmieniają te podmioty (nowa postać, rozkazy na obronę miasta), a zdarzenia łączą się luźno w wątki.
- Nacisk na **przejrzystość**: gracz widzi, co da każda opcja, zanim wybierze.

**Dla nas:** karty z `cards.json` (PR 7) powinny brać podmiot z kroniki gracza (ta wioska, ten sąsiad, ta susza), a każda opcja ma pokazany
skutek liczbą. W neolicie najlepsze wyzwalacze to dokładnie te momenty, które dziś giną w szumie: osadnicy głodują w drodze, susza
przy osadzie, pierwszy kontakt, obcy obóz na naszej ziemi.

Źródło: [Old World Designer Notes #9: Events](https://www.designer-notes.com/?p=1733).

## 4. Piaskownica do oglądania (WorldBox)

Gracze chwalą oglądanie, jak cywilizacje rosną od chat do królestw, walczą i wymierają, oraz moce, którymi „pcha się” świat. Narzekają, że
**po kilku godzinach robi się nudno**, bo pętla się powtarza, późna gra jest płytka, a duże mapy spadają z FPS.

**Dla nas:** CivSim 2 w trybie obserwatora ma tę samą słabość: po 15 minutach świat stoi (ludność 5 cywilizacji prawie stała od
11. minuty, seed 7). WorldBox ratuje się mocami gracza; my mamy dźwignie, więc muszą dawać widoczny skutek, inaczej obserwator i gracz
widzą to samo. Lekcja z wydajności: FPS na dużych mapach to jedna z głównych skarg graczy WorldBoxa, więc znany problem 1 (FPS niezmierzony)
warto zamknąć przed demem.

Źródło: [WorldBox: opinie graczy ze Steam (VaporLens)](https://vaporlens.app/app/1206560/world_box_god_simulator.md).

## 5. Sterowanie pośrednie (Crusader Kings, Victoria, Songs of Syx)

Gry Paradoxu i Songs of Syx mają symulację jako pana, a gracz rządzi przez kilka dźwigni (prawa, budowy, decyzje). Co z nich działa:
- **pasek z kilkoma liczbami i strzałkami** (CK: prestiż, pobożność, złoto; Songs of Syx: jedzenie, szczęście): gracz w 1 sekundę widzi,
  czy idzie lepiej, czy gorzej;
- **powiadomienia z priorytetem** zamiast pełnej kroniki: ważne wyskakują, reszta czeka w dzienniku;
- **cel na krótką metę**, który gracz sam wybiera (ambicje w CK, cele w Victoria 3).

Typowa skarga na Victorię 3 (premiera 2022): gracz nie widzi związku między swoją decyzją a wynikiem, bo symulacja rynku wszystko wygładza.
To ta sama pułapka, którą mierzy pomiar sprawczości w `2026-11-16_sesja-1.md`.

**Dla nas:** ekran sternika z GDD 1.2 (pięć liczb na pasku, kronika z przyczyną, karta) idzie w dobrą stronę. Brakuje „krótkiego celu”:
propozycja P-06 (cel na 3 minuty od doradcy, np. „osiądź przy rzece przed suszą”).

Źródła: [Crusader Kings III review (NME)](https://www.nme.com/reviews/crusader-kings-iii-review-a-superb-sequel-that-welcomes-new-players-to-the-joys-of-grand-strategy-2744117),
[Victoria 3 (VaporLens, opinie graczy)](https://vaporlens.app/app/529340/victoria_3.md), [Songs of Syx (VaporLens)](https://vaporlens.app/app/1162750/songs_of_syx).
