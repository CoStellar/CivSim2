# Przekazanie pracy: etap 1c (gwiazda, orbita, atmosfera, epoki lodowe)

Stan na 2026-11-05. Wklej ostatni rozdział („Prompt na nową sesję”) jako pierwszą wiadomość w nowej sesji.

## Co jest zrobione

- Etapy 0–2, 3a–3d: scalone do `development` (osady jako majątek: PR #16).
- Etap 1b: mapa jako kula (ADR 0012), planety z presetami Ziemia/Mars/Uran, klimat ze słońca, kontynenty z zarodków, długie zimy (PR #17, scalony).
- PR #18 (gałąź `feat/seasonal-clock`): „dwa zegary” (ADR 0013): pory roku na żywo przy roku ≥ 8 lat (Uran: tura 10,5 roku), wolne decyzje raz na turę ery,
  AI ocenia wygodę temperatury (efekt `cold_tolerance`), ustawienie `seasonStrength`. Jeśli jeszcze nie scalony, nowa praca startuje z tej gałęzi.
- Środowisko: .NET 10 instaluje się skryptem `dot.net/v1/dotnet-install.sh --jsonfile global.json --install-dir ~/.dotnet`. Testy i benchmarki tylko lokalnie
  (bez CI), testy mają być krótkie.

## Decyzje Patryka z 2026-11-05 (wszystkie zaakceptowane)

1. **Gwiazda, odległość, rok na prawdziwych wzorach.**
   - Światło na planecie: `S = L / d²` (L w jasnościach Słońca, d w AU; Ziemia: S = 1).
   - Masa gwiazdy z jasności (ciąg główny): `L ≈ M⁴` dla 0,43–2 M☉, `L ≈ 1,4 · M^3,5` dla 2–55 M☉.
   - Rok z trzeciego prawa Keplera: `P² = d³ / M` (lata, AU, masy Słońca).
   - Strefa zamieszkiwalna (Kopparapu 2013, gwiazda jak Słońce): S od 0,356 (maksymalny efekt cieplarniany) do 1,107 (niekontrolowany efekt cieplarniany);
     `d_wewn = √(L / 1,107)`, `d_zewn = √(L / 0,356)`.
   - Z trzech wielkości (jasność, odległość, rok) dowolne dwie wyznaczają trzecią. Gracz zmienia jedną, druga się dopasowuje; gdy wyjdzie poza strefę,
     druga wartość jest ustawiana na granicy strefy.
   - Presety: Ziemia (Słońce, 1 AU, rok 1), Mars (Słońce, 1,52 AU, S ≈ 0,43, rok 1,88), Uran (rok 84 przy S ≈ 1 wymaga gwiazdy ok. 1200–1300 L☉,
     ok. 7 M☉; uczciwie: taka gwiazda żyje ok. 50 mln lat, w grze naginamy). Nachylenie osi i mimośród jak dziś.
   - „Jasność gwiazdy 80–120%” znika (zastępują ją te wielkości).
2. **Atmosfera zamiast siły pór roku.** Ustawienia: efekt cieplarniany (°C ocieplenia; Ziemia 33) i ciśnienie (bar; Ziemia 1). Domyślnie dobierane do ilości
   światła (cykl węglanowo-krzemianowy: planety na zimnym krańcu strefy mają grubą atmosferę CO₂, np. Mars ok. 2–3 bar), z możliwością ręcznej zmiany.
   Ciśnienie zwiększa przenoszenie ciepła (mniejsza różnica równik–biegun) i łagodzi pory roku; `seasonStrength` z PR #18 ma z niego wynikać.
3. **Nowy model klimatu:** jednowymiarowy bilans energii w pasach szerokości (Budyko–Sellers, North 1975):
   `Q·s(x)·(1 − albedo(T)) = A + B·T − D·d/dx[(1 − x²)·dT/dx]`, x = sin(szerokości). Ziemia: A = 203,3 W/m², B = 2,09 W/m²/K (T w °C), D ≈ 0,55,
   Q = S₀/4, albedo 0,32 bez lodu i 0,62 z lodem (T < −10 °C). Efekt cieplarniany przesuwa A (`A = A_Ziemi − B·(GH − 33)`), ciśnienie skaluje D.
   `s(x)` brać z naszego `Insolation`. Liczyć na liczbach stałoprzecinkowych ze stałą liczbą iteracji (determinizm). Kalibracja do Ziemi: równik ok. 28 °C,
   biegun ok. −20…−27 °C. Zastępuje `ClimateModel.MeanTemperature`.
4. **Wahania klimatu:** cykle Milankovicia (nachylenie Ziemi 22,1–24,5° co 41 tys. lat, mimośród 0,005–0,058 co ok. 100 i 405 tys. lat, precesja co 23 tys. lat)
   oraz ustawienie zmienności gwiazdy (siła i okres). Epoki lodowe: −4…−6 °C średnio, morze niżej o ok. 120 m (pomosty lądowe). Zmiana poziomu morza
   w trakcie gry to osobny, późniejszy PR.
5. **Pory roku na żywo** zostają dla planet z bardzo długim rokiem (PR #18).
6. Na później (ROADMAP): „klocki pomocnicze” na biegunach; wędrówki trwające kilka tur do znanych dalekich miejsc (etap 3g).

## Plan etapu 1c (3 PR-y)

- **1c-A:** gwiazda, orbita, strefa zamieszkiwalna z dopasowaniem; atmosfera (cieplarniany, ciśnienie, wartości domyślne); model bilansu energii;
  presety; parametry w trybie bez okna, w podglądzie i w zapisie; mapy PNG trzech presetów; testy; ADR „model klimatu”; GDD 3.
- **1c-B:** cykle Milankovicia i zmienność gwiazdy: klimat zmienia się w trakcie gry (temperatura, lód, biomy, dzika żywność), mapy przed i po epoce lodowej.
- **1c-C:** zmiana poziomu morza w trakcie gry (pomosty lądowe, zalewanie wybrzeży), z obsługą osad na zalanych polach.

## Prompt na nową sesję

> Kontynuujemy CivSim 2 (repo `CoStellar/CivSim2`). Przeczytaj `CLAUDE.md`, `ROADMAP.md`, `docs/prompts/02-etap-1c-gwiazda-atmosfera.md`,
> GDD rozdział 3 i ADR 0012–0013. Sprawdź, czy PR #18 jest scalony (jeśli nie, pracuj od gałęzi `feat/seasonal-clock`). Zainstaluj .NET 10,
> zbuduj i uruchom testy. Potem zacznij etap 1c-A według planu z pliku przekazania: najpierw krótki plan sesji po polsku, potem kod.
