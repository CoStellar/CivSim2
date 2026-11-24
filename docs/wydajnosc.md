# Zasady wydajności nowej symulacji

Zasada Patryka (2026-11-17): **jak najmniej mechanizmów w tle.** Nie liczymy tysiąca rzeczy naraz. Każdy system nowej symulacji
musi spełniać poniższe zasady; PR, który ich nie spełnia, opisuje dlaczego.

## Jak radzą sobie inne gry

| Gra | Co robi | Co bierzemy |
| --- | --- | --- |
| **Factorio** | Byt, który nie ma pracy, wypada z pętli w całości (nie „sprawdza i wraca”), aż coś z zewnątrz go obudzi; usypianie i budzenie kosztują O(1). Tysiące ramion nie sprawdzają co klatkę, czy mają się ruszyć. | **Usypianie:** budynek bez składników, bez ludzi albo z pełnym magazynem nie jest liczony, dopóki coś się nie zmieni. |
| **RimWorld** | Trzy rytmy: takt (1/60 s), takt rzadki (250 taktów, ok. 4 s), takt długi (2000 taktów, ok. 33 s). W wersji 1.6 rzeczy poza widokiem liczą się co 15 taktów ze zmianą razy 15. | **Rytmy:** ruch i walka co takt, gospodarka co sekundę, ludność i AI co kilka sekund. Bez zależności od kamery (multiplayer). |
| **Victoria 3** (Paradox) | Tura podzielona na zadania z własną częstotliwością (rocznie, miesięcznie, tygodniowo, dziennie). Ludność to grupy (Pops), a rozbijanie grup na mniejsze psuło wydajność, więc w 1.6 je łączą. | **Sumy zamiast ludzi:** osada trzyma liczby ludzi w zawodach, nie listę ludzi. Grupy łączymy, nie dzielimy bez potrzeby. |
| **Age of Empires** | Deterministyczny lockstep: przez sieć idą tylko komendy graczy, w turach ok. 200 ms; każdy komputer liczy to samo. | **Sieć:** tylko komendy, serwer z taktem 100 ms. Liczby całkowite (fixed-point), własny generator losowy. |
| **Supreme Commander 2, Planetary Annihilation** | Pole przepływu (flow field): jedna mapa kierunków do celu dla całej grupy zamiast ścieżki dla każdej jednostki. | **Ruch grupami:** jedna ścieżka na oddział; później pole przepływu, gdy wiele oddziałów idzie do jednego celu. |

Źródła: Factorio Friday Facts #204 i wypowiedzi twórców na forum; RimWorld Wiki „Ticks” i „Modder Primer 1.6”; Victoria 3 Dev Diary
#76 (Performance) i #105 (Migration); „1500 Archers on a 28.8” (GDC 2001); materiały o flow field w Supreme Commander 2.

## Zasady

1. **Śpi, co nie pracuje.** Budynek, oddział albo osada, w których nic się nie zmienia, nie są liczone. Budzi je zdarzenie
   (dostawa, nowy pracownik, rozkaz), a nie sprawdzanie co takt. Lista aktywnych z usypianiem i budzeniem w O(1).
2. **Termin zamiast sprawdzania.** Budowa, badanie, rekrutacja, koniec zdarzenia: przy starcie liczymy takt końca i wkładamy go do
   kolejki terminów. Nic nie sprawdza co takt „czy już”.
3. **Tempo zamiast sztuk.** Produkcja to tempo (ludzie × wydajność). Liczymy je od nowa tylko po zmianie (nowy budynek, ludzie,
   brak składnika), a magazyn dodaje tempo w rytmie gospodarki.
4. **Stałe rytmy, rozłożone w czasie.**
   - Takt (100 ms): ruch oddziałów, walka.
   - Gospodarka: co 10 taktów (1 s).
   - Ludność, zadowolenie, zdarzenia: co 50 taktów (5 s).
   - AI przeciwnika: co 20–50 taktów na cywilizację.

   Praca na rytmie rozkładamy na takty po numerze bytu (`id % okres`), żeby nie było skoków.
5. **Sumy, nie osoby.** Osada trzyma liczby ludzi w zawodach i zapasy; oddział to liczba żołnierzy danego rodzaju. Nie ma list ludzi.
6. **Ścieżka raz na grupę.** Oddział liczy trasę raz, przy rozkazie (A* po heksach), i trzyma ją. Liczy ponownie tylko wtedy,
   gdy droga się zablokuje.
7. **Widoczność tylko przy zmianie.** Mgłę wojny liczymy, gdy oddział zmieni heks albo powstanie budynek, tylko w promieniu widzenia.
8. **Płaskie tablice i zero alokacji** w pętli taktu (jak dotąd): pola mapy w strukturze tablic, byty jako liczby.
9. **Bez symulacji poza widokiem gracza, której gracz nie odczuje.** Jeśli coś nie zmienia decyzji gracza ani tego, co widzi,
   nie liczymy tego.
10. **Budżet i pomiar.**
    - Cel: średnio poniżej 2 ms na takt przy 8 cywilizacjach w późnej grze na mapie standardowej.
    - Pomiar zawsze przed i po na tym samym komputerze.
    - Ostateczny pomiar robi Patryk przy grze.

## Zmiany po testach 2026-11-24 (P1, A1)

- **Magazyn na progu nie budzi ludu co sekundę.** Towar jest „pełny” od 98% miejsca i zostaje pełny, dopóki nie spadnie poniżej
  90%. Jest „pusty” poniżej 1 i zostaje pusty, dopóki nie dojdzie do 3 sztuk (`stores` w `game.json`). Wcześniej zapas wahał się
  przy progu, a każde przełączenie liczyło gospodarkę całego ludu od nowa (5 753 przeliczeń na 9 600 kroków w minutach 100–120).
- **AI liczy wolne miejsca pracy raz na takt.** Każda z ok. 64 000 prób budowy na 20 minut przeglądała wszystkie budynki ludu
  (ponad 1000). Teraz wynik jest zapamiętany w obrębie taktu (`Civ.AiBuildScan`) i liczony od nowa, gdy przybędzie budynek.

Pomiar: bot „Builder”, ziarno 1, 8 ludów, mapa 98×60, 120 minut, ten sam komputer:

| Minuty | Takt przed (ms) | Takt po (ms) | Ludzie AI przed | Ludzie AI po |
| --- | --- | --- | --- | --- |
| 0–20 | 0,28 | 0,28 | 72 | 70 |
| 20–40 | 0,21 | 0,24 | 285 | 317 |
| 40–60 | 0,37 | 0,41 | 1 195 | 1 040 |
| 60–80 | 0,83 | 0,83 | 2 757 | 3 299 |
| 80–100 | 1,35 | 1,14 | 4 759 | 4 818 |
| 100–120 | **1,79** | **1,11** | 5 728 | 5 302 |
| cała partia | 0,80 | 0,67 | | |
| 99. centyl | 5,89 | 4,26 | | |
