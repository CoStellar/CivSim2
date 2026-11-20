# CivSim 2: projekt gry

Spisane z odpowiedzi Patryka, 2026-11-17 (ADR 0028). To podstawa gry; zastępuje stare GDD (legacy). Kopia robocza jest też w notatkach
Projektu (`claude/projekt-gry.md`); przy zmianach aktualizujemy obie. **Najpierw gra, potem realizm.**

## 1. Wizja

- **Gracz jest całą cywilizacją**, jak umysł roju z Warhammera 40k. W pełni steruje ludem i prowadzi go przez wieki, widząc jego rozwój.
- **Lud gracza nie ma własnej AI.**
  - Sam wykonuje tylko codzienne prace. Gdy gracz zrobi pole uprawne, ludzie sami je uprawiają, a gracz wybiera, co rośnie.
  - Gracz ma pełną kontrolę i sprawczość.
  - Bez doradcy nikt mu nawet nie podpowiada.
- **Cywilizacja 6 w czasie rzeczywistym:** szybka (jak na grę tego typu) wędrówka przez historię: walka, rozbudowa, rozwój. Bez kultury
  i religii.
- **Po 15 minutach** gracz ma ciągłą ochotę rozwijać lud, widzieć kolejne możliwości i dochodzić do nich sam.
- **Po partii** gracz czuje się jak po grze planszowej: może przejrzeć przebieg i zrozumieć, która decyzja była zła.

## 2. Partia

- **Długość:** ok. 2 h do nowożytności, może być dłużej albo krócej. **Teraz demo do średniowiecza.**
- **Start:**
  - kolejność wyboru miejsca losowa;
  - przez 15 s mapa bez złóż i żyzności, potem 15 s na wybór;
  - strefy blokady wokół zajętych miejsc;
  - AI wybiera z losowym błędem X%;
  - trzy zdania historii plemienia.

  Każde miejsce ma ocenę według kryteriów: ludność, rolnictwo, wojsko, narzędzia, rozrost, przyszłość, ryzyko. Liczymy ją raz, po
  wygenerowaniu mapy; gracz widzi ją po partii.
- **Cel partii** ustala się przy jej tworzeniu, np. ostatnia żywa cywilizacja albo pierwsza z daną technologią.
- **Przegrana:** wymarcie ludu albo najgorszy wynik na koniec. Wynik liczy się z ludności, ziemi, technologii i siły wojska.
- **Czas płynie ciągle.** Pauza:
  - każdy gracz ma 3 pauzy na partię;
  - po 30 s pauzy każdy inny gracz może ją przerwać;
  - gracz sam z AI pauzuje bez limitu.

## 3. Pętla rozgrywki

- **Sekundy:**
  - gracz wybiera budynek, wskazuje heks, budowa rusza;
  - wysyła zwiadowców i patrzy, co odkryli;
  - wysyła część wojska na oddział wroga albo widoczną wioskę.

  Bieżących decyzji nie może być za dużo.
- **1–3 minuty:** gracz rozbudowuje np. rolnictwo i widzi skutek: rośnie poziom rolnictwa, liczba rolników i jedzenia. Widzi też skutki
  złe. Czy to się opłaciło, ocenia sam.
- **Era i partia:** gracz buduje własny profil cywilizacji. Przykład:
  - silne rolnictwo i handel z sąsiadem;
  - mocne wojska leśne i górskie, dobre wojska dystansowe;
  - brak floty;
  - mało cywilów, bo wielu ludzi poszło do wojska przed atakiem na sąsiada.
- **Rytm:** gracz pracuje stale; spokój to czekanie na zasoby. Działania można przerwać i wznowić (wstrzymana budowa kosztuje w sumie
  więcej). Zasoby przesuwa się tam, gdzie są potrzebne.

## 4. Decyzje gracza

- **Gracz decyduje o wszystkim:**
  - gdzie osada;
  - którą osadę awansować (miasto, stolica);
  - kogo zaatakować, jawnie czy z zaskoczenia;
  - co badać;
  - co i gdzie zbudować.
- **Samo się dzieje:** uprawa, zbieranie, przyrost. Gracz może przesunąć ludzi („więcej kamienia”).
- **Tempo:** mniej kliknięć niż RTS, więcej niż Cywilizacja. Dobry gracz pod koniec partii 60–70 na minutę, na początku mało.
- **Skutki widać w świecie** (choroba, przegrana walka). Wszystko się zapisuje; szczegóły w widoku rozszerzonym.

## 5. Ludzie, osady, myśliciele, doradcy

- **Na mapie:**
  - grupy ludzi na heksach (osadnicy, robotnicy);
  - wojsko jako suma jednostek na heksie;
  - osady.
- **Zawody** jak dziś (wytwórcy, rzemieślnicy, wojownicy, myśliciele…). Gracz widzi liczby i może wymusić zapotrzebowanie. Prosta wersja.
- **Ludność** rośnie z dostatku, podbojów, jedzenia i liczby oraz wielkości domów. Ich brak ją ogranicza.
- **Osady zakłada i awansuje gracz,** gdy ma zasoby. Lud nie ruszy się sam.
- **Przenosiny osady:**
  - obóz traci część zasobów;
  - wioska i wyżej budują się od nowa, a stara zostaje opuszczona;
  - miasto zakłada wioski, a nie nowe miasto.

  Jednostki chodzą grupami.
- **Myśliciele mają rzadkość i cechy, jak przedmioty w Fortnite:**
  - spełniają minimum doświadczenia do badań;
  - obniżają koszt w zasobach;
  - niektórzy obniżają wymagane doświadczenie, ale podnoszą koszt.

  Myśliciel ma następcę (np. Handalf Szary XXII), który z każdym pokoleniem działa słabiej.
- **Doradca jeden naraz:**
  - pojawia się losowo, z rzadkością i cechami;
  - gracz go zatrudnia za zasoby i płaci małe utrzymanie;
  - daje jedną radę naraz ze swojej dziedziny.
- **Imiona** myślicieli i doradców losuje system języków. To jedyne imiona w grze.

## 6. Gospodarka

- **Dobra podstawowe:** kamień, glina, jedzenie itp. Źródła: zbieractwo i polowanie, potem uprawa i wytwarzanie.
- **Magazyn** to budynek na mapie.
- **Praca w budynkach (jak w Anno i Settlers):**
  - budynek ma miejsca pracy (np. kamieniołom 5, pole 3);
  - ludzie sami obsadzają wolne miejsca;
  - gracz może wstrzymać budynek albo dać mu pierwszeństwo;
  - na pasku zasobów strzałka pokazuje przybywanie i ubywanie.
- **Braki widać kolorami** (brak składników, brak pracy). Ludzie stają się niezadowoleni, potem ich ubywa.

## 7. Technologie

- **Drzewo.** Do nowej technologii potrzeba:
  1. doświadczenia w poprzednich (z czasu używania i liczby wyszkolonych ludzi), a myśliciele muszą spełnić minimum;
  2. kosztu wyłącznie w zasobach;
  3. czasu na wynalazek.
- **Koszt** spada, gdy sąsiedzi znają technologię, gdy sprzyjają warunki i teren, i z dobrymi myślicielami.
- **Badania:** jedno naraz, później dwa.
- **Technologia daje:** ruch, siłę i style walki, wygląd miejsc itd.
- **Era** dodaje opcje, nie nowe rodzaje decyzji.

## 8. Wojna

- **Po co:** zysk albo obrona; narzędzie do celu gracza. Najwyżej 2–3 wojny naraz u jednej cywilizacji.
- **Forma:** jak w Cywilizacji, trochę RTS na żywo.
- **Jednostki:**
  - rodzaje według ery: wręcz, dystansowe, później konni i flota;
  - każdy rodzaj ma teren, w którym jest mocniejszy;
  - nowa era daje nowe rodzaje i ulepsza istniejące, a później już tylko ulepsza;
  - oddział to grupa na heksie;
  - rozkazy: idź, atakuj, broń pola, wycofaj się.
- **Armia** (zmiana 2026-11-20, decyzje 1, 9, 10, 11):
  - wojownik nie pracuje i kosztuje jedzenie; zostaje wojownikiem do końca (bez rozwiązywania oddziałów i bez rezerwistów);
  - limit oddziałów rośnie z poziomem osad, koszarami, zamkami i technologiami politycznymi; duża armia jest możliwa, ale droga;
  - doświadczenie ma rodzaj jednostki (np. wszyscy łucznicy), a na każdym poziomie gracz wybiera jedną z 3 premii;
  - z dala od swojej ziemi armia je z juków i z ziemi, a karawany mogą dowozić jej jedzenie.
- **Oblężenie** (2026-11-20, decyzje 3, 5, 7, 8): otoczenie odcina osadę i spowalnia jej pracę; bronią oddziały, straż z umocnień
  i 30% mieszkańców (zostaje co najmniej 40%); po zdobyciu zdobywca wybiera: zajmij, złup albo zburz.
- **Bitwy widoczne**, gracz stale wpływa i może wycofać jednostki.
- **Wojna to stan,** a każda walka należy do wojny. Wojna kończy się:
  - **pokojem z negocjacji:**
    - strony wybierają, co dają i czego chcą (zasoby, osada, okres pokoju);
    - gra proponuje łupy w proporcji do prowadzenia (zdobyte pola, pokonane jednostki);
    - AI ocenia ofertę po charakterze i przebiegu wojny;
  - **wyniszczeniem jednej ze stron.**
- **Atak bez wypowiedzenia:** wiedzą ofiara i ludy znające obie strony; przez jakiś czas trudniej o umowy.
- **Obrona:** mobilizacja na wybrane heksy, fortyfikacje (także w trakcie wojny, ale wtedy mogą zostać zaatakowane).

## 9. Inne ludy i dyplomacja

- **Liczba ludów** ustawiana przy tworzeniu partii.
- **Poznawanie:**
  - przez obserwację, zwiadowców i ataki;
  - zwiadowca jawny jest szybszy, ukryty wolniejszy;
  - obcy zwiadowca widzi tylko obserwowaną osadę, z czasem więcej;
  - ukrytych zwiadowców nie da się przypisać do ludu.
- **Relacje:** handel, umowy, sojusze. Wasalstwo później: tylko AI–AI albo gracz nad AI.
- **Charakter AI** gracz poznaje z jej działań, nigdy wprost.
- **Multiplayer od razu.**

## 10. Świat

- **Generacja świata zostaje,** z gęstszymi heksami: cały świat, heks to miejsce na jeden obiekt, osada to zbiór heksów (rośnie
  z poziomem i ludnością). Wielkość heksa nie musi odpowiadać kilometrom. Teren ma znaczenie.
- **Zdarzenia** (susza, powódź, epidemia, pożar…):
  - co ok. 8–10 min każda cywilizacja losuje ze wspólnej puli jedno, którego nikt inny teraz nie ma;
  - trwa 1–2 min;
  - radę daje tylko pasujący doradca.
- **Ery szybsze;** ziemia i zwierzyna wyczerpują się powoli.

## 11. Ekran

- **Mapa:** duże heksy, mocne przybliżenie.
- **Rozkład:**
  - lewy górny róg: zasoby;
  - środek u góry: era i lata (ozdoba), osobno prawdziwy czas partii;
  - prawy górny róg: doradca;
  - prawy dolny róg: rozwijana kronika;
  - lewa strona: panel boczny;
  - dół: pasek decyzji jak w WoW (klik w decyzję, potem w mapę).
- **Mgła wojny** jak dziś.
- **Bez ściany tekstu.** Widok rozszerzony na żądanie.
- **Podsumowanie** z tabelami i osią czasu, żeby sprawdzić, co zatrzymało rozwój.

## 12. Czego nie ma

- Wodzów i postaci (poza imionami myślicieli i doradców).
- Religii, kultury.
- Rozbudowanej symulacji w tle (`docs/wydajnosc.md`).
- Wiązania świata z latami.
- 100% realizmu.
