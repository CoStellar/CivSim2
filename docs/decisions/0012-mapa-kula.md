# ADR 0012: Mapa jako kula: walec heksów z przejściem przez bieguny

**Status:** przyjęta, 2026-11-05 (decyzja Patryka). Zastępuje [ADR 0004](0004-mapa-hex-torus.md) w części o zawijaniu.

## Kontekst

ADR 0004 zawijał mapę w obu kierunkach (torus). Idąc ciągle na północ, wracało się od południa. To nie jest kula: na kuli idący na
północ przechodzi przez biegun i schodzi po drugiej stronie planety. Patryk chce symulacji globu.

Siatki złożonej wyłącznie z sześciokątów nie da się położyć na kuli bez zniekształceń. Zawsze potrzeba 12 pięciokątów (twierdzenie
Eulera). Rozważaliśmy trzy drogi.

## Decyzja

**Walec heksów z przejściem przez bieguny.**

- Wschód–zachód zawija się jak dotąd.
- Rząd 0 dotyka bieguna północnego, ostatni rząd południowego. Krok poza górny rząd w kolumnie *c* prowadzi do górnego rzędu
  w kolumnie *c + szerokość/2*, czyli na drugą stronę planety. Kierunek północ–południe odwraca się: kto szedł na północ, dalej idzie
  „na północ” w sensie mapy, ale schodzi w dół, po drugiej stronie bieguna. Tak samo na biegunie południowym.
- **Rozmiar:** połowa szerokości musi być nieparzysta (szerokość = 4k + 2), bo tylko wtedy kolumny przesunięte i nieprzesunięte
  schodzą się nad biegunem bez dziury i bez nakładania. Rozmiary map: testowa 66×40, standard 258×160, duża 386×240, ogromna 514×320.
- **Rzędy to pasy o równej powierzchni.** Sinus szerokości geograficznej spada równo z rzędu na rząd, więc każde pole ma tę samą
  powierzchnię na prawdziwej kuli. Okolice biegunów zajmują mniej rzędów niż na dawnej mapie: polarne lądy nie są już przesadnie duże.
  Kształty są wierne w okolicach 48° szerokości; przy biegunach pola są w rzeczywistości szersze i niższe niż na ekranie.
- Szum generatora jest liczony w punktach na kuli (3D), więc kontynenty przechodzą przez bieguny i szew bez śladu.
- Wszystkie odległości i sąsiedztwa idą przez jeden moduł `HexGlobe` (dawniej `HexTorus`).

## Rozważane alternatywy

- **Geodezyjna kula** (heksy i 12 pięciokątów, jak piłka): najwierniejsza, ale wymaga globusa 3D albo pociętej siatki w podglądzie,
  osobnych kafli pięciokątnych i przebudowy geometrii, fragmentów, zapisu i grafiki 48×42.
- **Kurczące się rzędy** (mniej heksów przy biegunach): wierne powierzchnie, ale na styku rzędów pola mają 5 lub 7 sąsiadów.
- **Zostawić torus:** prosty, ale nie zachowuje się jak glob.

## Konsekwencje

- Przy samym biegunie połączenie jest przybliżone. Pole górnego rzędu ma po drugiej stronie bieguna dwóch sąsiadów, a na kuli
  wszystkie pola polarne stykają się w jednym punkcie. Odległości przy biegunie mogą się różnić od kuli o jedno pole. Matematycznie
  powierzchnia jest butelką Kleina, nie sferą, ale w grze widać tylko tyle, że przez biegun przechodzi się na drugą stronę planety.
- Rzeki nie płyną przez narożniki polarnych rzędów (bieguny i tak pokrywa lód).
- Krawędź nad biegunem południowym zapisujemy w wolnym miejscu krawędzi północnych, żeby nadal były dokładnie 3 krawędzie na pole.
- Podgląd przewija się tylko na boki; kamera zatrzymuje się na biegunach.
- Stare zapisy gry (format 8) się nie wczytają (format 9).
