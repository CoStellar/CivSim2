# Sterowanie i instrukcja gry (2026-11-23)

Prośba Patryka: sprawdzić układ sterowania, czy wszystko da się zrobić z klawiatury (włączyć każde okno, wybrać każdą rzecz),
czy sterowanie jest naturalne, i zrobić instrukcję gry dostępną z menu i w grze.

## Co sprawdziłem (stan `development` przed zmianą)

| Co | Było | Ocena |
| --- | --- | --- |
| Okna 1–6, samouczek (T), pomoc (H), patrole (P), trasy (G), kronika (L) | klawisze działały | dobrze |
| Kamera (WASD, strzałki, kółko, środkowy przycisk) | działało | dobrze |
| Czas (Spacja, + i −), zapis (F5) | działało | dobrze |
| Pasek budowy: sloty Z, X, C…, zakładki Tab | działało, ale Tab tylko do przodu | brakowało Shift + Tab |
| Przyciski **w panelach i oknach** (wybór badania, szkolenie, awans osady, wojna i pokój, zatrudnianie myślicieli) | **tylko myszą**: wszystkie przyciski miały wyłączone skupienie klawiatury (żeby Spacja i strzałki zostały dla mapy) | największa luka |
| Przejście do kolejnej osady, oddziału, alertu | tylko klikiem | brakowało |
| Przewijanie panelu bocznego i okna | tylko kółkiem | brakowało |
| Menu startowe | Godot pozwala chodzić strzałkami i Tabem, ale nie każda sekcja dawała skupienie na start | do poprawy |
| Esc | jedno Esc za dużo (gdy nic nie było otwarte) wyrzucało do menu **bez zapisu** | nienaturalne i groźne |
| Pomoc (H) | lista klawiszy wpisana na sztywno w kodzie, część klawiszy pominięta | do poprawy |
| Instrukcja gry | nie było | brakowało |

## Co dodałem

**Kursor klawiatury (Enter).** Żółta ramka na przycisku ekranu. Strzałki przesuwają ją do najbliższego przycisku w tym kierunku,
Enter naciska, Esc wyłącza. Pod ramką widać podpowiedź przycisku (to samo, co mysz pokazuje po najechaniu). Gdy otwarte jest okno,
kursor chodzi tylko po nim (np. po kartach drzewa technologii), a gdy nic nie jest otwarte, po panelu bocznym, pasku, alertach i górnym pasku.
Dzięki temu **każdy przycisk gry jest osiągalny z klawiatury**. W trybie kursora WASD dalej przesuwa mapę, a Spacja dalej pauzuje.

**Nowe klawisze:**

| Klawisz | Co robi |
| --- | --- |
| Q / E | poprzednia / następna nasza osada (zaznacza i pokazuje) |
| R / Shift + R | następny / poprzedni nasz oddział (zaznacza, prawy klik wysyła) |
| J | kolejny alert: idzie tam, czego dotyczy |
| Home | kamera wraca do zaznaczenia (albo do pierwszej osady) |
| Shift + Tab | poprzednia zakładka paska budowy |
| Page Up / Page Down | przewija otwarte okno, pomoc albo panel boczny |
| F1 | instrukcja gry (w grze i w menu) |
| Enter | kursor klawiatury |
| Esc dwa razy | wyjście do menu (jedno Esc tylko ostrzega, że gra nie zapisuje się przy wyjściu) |

**Menu startowe:** nowa sekcja **How to play** (instrukcja), F1 ją otwiera, a każda sekcja ma od razu skupienie klawiatury
(strzałki i Tab chodzą po przyciskach, Enter naciska).

**Instrukcja:** `data/sim/manual.md` (po angielsku, jak wszystkie teksty w grze; prosty format, można ją zmieniać bez kodu).
Rozdziały: cel gry, ekran, sterowanie (wszystkie klawisze), pierwsze minuty, osady i ludzie, badania i ery (jak przejść do kolejnej ery),
wojna i pokój, inne cywilizacje, wskazówki. W grze: przycisk **Manual** na górze albo F1; strzałki lewo/prawo zmieniają rozdział, góra/dół przewijają.
Pomoc pod H pokazuje teraz rozdział „Controls” z tej samej instrukcji, więc klawisze są opisane w jednym miejscu.

## Czego nadal nie da się zrobić bez myszy

Wskazywanie heksów na mapie: miejsce startu, miejsce budynku, cel marszu, rysowanie drogi. Tak jest w strategiach, w które
gra się myszą (Civilization, Age of Empires); klawiatura służy tam do skrótów. Kursor heksów na mapie (strzałki po heksach, Enter = klik)
da się dodać, ale to osobna praca i zmienia sposób grania, więc zostawiam to do decyzji.

## Jak sprawdzić

W grze: Enter, potem strzałki i Enter (np. w oknie badań: `2`, Enter, strzałki po kartach, Enter wybiera badanie).
F1 albo przycisk Manual: instrukcja. W menu: How to play.

Zrzuty ekranu (xvfb, katalog `game`): `--play --play-scenario 01_neolithic_start --play-window <Manual|Controls|Cursor|CursorResearch|Help> --play-screenshot x.png --play-seconds 6`
i dla menu `--menu-screenshot x.png --menu-section manual`.

## Do uzupełnienia po PR #162

PR #162 dodaje alert „Strangers”, przycisk „Lay down arms” i krok samouczka o obronie, a usuwa doświadczenie z badań.
Instrukcja jest napisana tak, że zgadza się przed i po #162 (nie opisuje doświadczenia badań); po jego merge'u dopiszę te trzy rzeczy.
