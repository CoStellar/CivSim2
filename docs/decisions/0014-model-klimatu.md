# ADR 0014: Model klimatu: gwiazda i orbita z prawdziwych wzorów, bilans energii i cykl węglowy

**Status:** przyjęta, 2026-11-05 (decyzje Patryka). Zastępuje wzór średniej temperatury z etapu 1b oraz ustawienia „jasność gwiazdy” i „siła pór roku”.

## Kontekst

W etapie 1b temperatura była dopasowanym wzorem (28 °C na równiku, −27 °C na biegunach), jasność gwiazdy suwakiem 80–120%, a siła pór roku
osobnym ustawieniem. Patryk chce, żeby planeta wynikała z fizyki: gwiazda, odległość i rok powiązane prawdziwymi wzorami, atmosfera, która
coś znaczy, i klimat, który w kolejnych etapach może się zmieniać (epoki lodowe, a kiedyś działania cywilizacji).

## Decyzja

**Gwiazda i orbita** (`StarOrbit`):
- Światło na planecie `S = L / d²` (L w jasnościach Słońca, d w AU). Masa gwiazdy z jasności (ciąg główny): `L = M⁴` do 2 M☉, `L = 1,4 · M^3,5` wyżej.
  Rok z trzeciego prawa Keplera: `P² = d³ / M`.
- Planeta leży zawsze w strefie zamieszkiwalnej (Kopparapu 2013): S od 0,356 do 1,107.
- Dowolne dwie z wielkości jasność, odległość, rok (oraz światło) wyznaczają resztę. W interfejsie gracz zmienia jedną, druga się dopasowuje,
  trzecia zostaje. Gdy planeta wypadłaby ze strefy, staje na najbliższej granicy, a zmieniana wartość zatrzymuje się tam, dokąd strefa pozwala.
- Na linii poleceń jedna wartość: odległość albo światło zachowuje gwiazdę, jasność albo rok zachowuje światło (tak `--year 84` daje Urana z gwiazdą
  ok. 1365 L☉ i 7,1 M☉ w odległości 37 AU).
- Jasność 0,04–7000 L☉, więc rok zawsze mieści się w 0,1–500 lat.

**Klimat: bilans energii w pasach szerokości** (`EnergyBalance`, Budyko–Sellers, North 1975):
- 90 pasów o równej powierzchni (jak rzędy mapy). `F(x)·(1 − albedo) = A + B·T − d/dx[D(x)·(1 − x²)·dT/dx]`, F z naszego `Insolation`.
- Ziemia: A = 203,3 W/m², B = 2,09 W/m²/K. Efekt cieplarniany przesuwa A: `A = 203,3 − B·(cieplarniany − 33)`.
- **Kalibracja do Ziemi** (zaakceptowana przez Patryka): same wartości z podręcznika (stałe albedo, D = 0,55) dają biegun −7 °C i brak lodu. Dlatego
  albedo rośnie lekko ku biegunom (0,32 + 0,16·P₂(x), niskie słońce), a ciepło płynie szybciej w tropikach (komórki Hadleya):
  D = 0,3 · (1 + 3·(1 − x²)²), średnio ok. 0,55. Lód (średnia poniżej −10 °C) ma albedo 0,62. Wynik: równik 27 °C, 45° 10 °C, 60° −4 °C, biegun −22 °C.
- Ciśnienie skaluje przenoszenie ciepła: `D · √(ciśnienie w barach)` (gęste atmosfery to głównie CO₂, które na bar przenosi mniej niż powietrze),
  i łagodzi pory roku: siła pór roku `= 100% · √(1 bar / ciśnienie)`.
- Liczby całkowite (milionowe części, iloczyny 128-bitowe), algorytm Thomasa, stała liczba kroków: 40 przebiegów z łagodną poprawką lodu.
  Zaczynamy bez lodu, więc planeta, która może utrzymać otwartą wodę, ją utrzymuje (ciepła gałąź równowagi).

**Atmosfera z cyklu węglanowo-krzemianowego** (`CarbonCycle`, decyzja Patryka z 2026-11-05 zamiast „docelowej temperatury na krańcach strefy”):
- Wulkany stale dodają CO₂ (ustawienie `volcanism`, % Ziemi). Wietrzenie skał zabiera CO₂: `(CO₂ / 280 ppm)^0,3`, razy dwa na każde 10 °C cieplej,
  tylko w pasach z ciekłą wodą (średnia powyżej 0 °C). Znormalizowane tak, że Ziemia z wulkanizmem 100% ma 280 ppm.
- Na starcie świata CO₂ jest w równowadze (wulkany = wietrzenie), szukanej bisekcją po logarytmie CO₂ z pełnym modelem klimatu w każdym kroku.
  Nachylenie osi liczy się samo: na Uranie wietrzeje cała planeta, bo nie ma zimnych biegunów (756 ppm zamiast 280).
- Cieplarniany z CO₂: 33 °C przy 280 ppm, +1,8 °C na podwojenie do 0,01 bar, wyżej +4,7 °C na podwojenie (gruba atmosfera). Ciśnienie = pozostałe
  gazy (`otherGases`, Ziemia 1 bar) + CO₂. CO₂ albo efekt cieplarniany można ustawić ręcznie; wtedy cykl nie działa.
- **CO₂ to stan świata, nie stała:** w 1c-B klimat zmienia się w trakcie gry (cykl odpowiada powoli), a w erze przemysłowej spalanie i wycinanie lasów
  dodadzą CO₂ przez mechanizm efektów. Cywilizacja może więc kiedyś zmienić klimat swojej planety.
- **Na każdej planecie jest życie** (decyzja Patryka z 2026-11-05). Co dalej z nią będzie, rozstrzyga symulacja.

**Presety:** Ziemia (Słońce, 1 AU, wulkanizm 100%), Mars (Słońce, 1,524 AU, S = 0,43, rok 1,88, wulkanizm 1000%: młoda, aktywna planeta, ok. 9 bar CO₂,
równik 9 °C, biegun 1 °C), Uran (rok 84 przy S = 1: gwiazda 1365 L☉, 7,1 M☉; taka gwiazda żyje ok. 50 mln lat, w grze naginamy).

## Rozważane alternatywy

- **Docelowa średnia temperatura zależna od światła** (8 °C na zimnym krańcu, 15 °C przy S = 1, 25 °C na gorącym): prosta, ale ignoruje nachylenie osi
  i nie daje stanu, na który cywilizacje mogą wpływać. Odrzucona przez Patryka.
- **Czysta fizyka bez kalibracji** (stałe albedo, D = 0,55): bieguny za ciepłe, brak czap lodowych.
- **Model 2D albo ogólnej cyrkulacji:** dokładniejszy, ale za drogi do liczenia w każdej turze w 1c-B i trudny do zrobienia deterministycznie.
- **D proporcjonalne do ciśnienia** (Williams i Kasting 1997, ten sam gaz): przy kilku barach planeta byłaby prawie jednakowo ciepła od równika po biegun.

## Konsekwencje

- Nowy format zapisu (10) i nowe złote hashe (inne mapy dla tych samych seedów: klimat Ziemi jest teraz trochę chłodniejszy w średnich szerokościach).
- Fizyka mówi, że planety na zimnym krańcu strefy gromadzą CO₂ tylko do granicy zamarzania: przy wulkanizmie 100% Mars ma równik ok. 0 °C. Żeby
  był zamieszkiwalny, preset ma silny wulkanizm.
- Model jest zonalny (bez kontynentów): wietrzenie liczymy tak, jakby ląd był wszędzie. Kiedyś można liczyć je z prawdziwych pól lądu.
- Przy biegunie stały krok pasów (sinus) daje ok. 8° szerokości na pas; to wystarcza, bo pola mapy też są równe powierzchniowo.
