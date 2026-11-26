# Plan przebudowy (ADR 0028)

Cel: demo do średniowiecza, w którym Patryk gra na żywo. Każdy etap kończy się czymś, co da się zagrać albo obejrzeć w kliencie Godot.
Co zostaje, a co jest legacy: `docs/legacy/README.md`. Etapy A–G są zrobione (2026-11-20); dalsze prace: sprint do demo w `ROADMAP.md`.

**Sposób pracy:**
- Nowa symulacja powstaje obok starej, w nowym projekcie `src/CivSim.Sim` (bez zależności od Godota).
- Korzysta z fundamentu w `CivSim.Core`: mapa, generacja świata, liczby, losowanie, hasz, nazwy.
- Stary kod znika, gdy nowa część go zastąpi.
- Każdy system spełnia `docs/wydajnosc.md`.

## Etapy

| Etap | Zakres | Do zagrania na końcu |
| --- | --- | --- |
| **A. Mapa** | gęstość heksów (7 albo 19 drobnych na dawny), heks jako miejsce na obiekt, pomiar pamięci, czasu generacji i klatek | przelot nad światem w nowej skali |
| **B. Osada i gospodarka** | wybór miejsca z oceną, osada z heksów, budynki z miejscami pracy, magazyn, domy, przyrost, pasek zasobów i akcji, wstrzymanie i pierwszeństwo budynku | 15 min budowania jednej osady |
| **C. Ekspansja** | osadnicy, awans i przenosiny osad, zwiadowcy jawni i ukryci, mgła wojny, kronika, oś czasu | kilka osad, odkrywanie mapy |
| **D. Rozwój** | drzewo do średniowiecza (doświadczenie, koszt w zasobach, czas), myśliciele, doradca, ery | rozwój przez ery |
| **E. Wojna** | jednostki według ery, rezerwiści, rozkazy oddziałów, walka na żywo, fortyfikacje, wojna jako stan, negocjacje, reputacja | wojna z drugą cywilizacją |
| **F. Gracz AI** | AI z tymi samymi komendami co gracz | partia przeciw kilku AI |
| **G. Świat i cel** | kontakt, handel, umowy, sojusze, zdarzenia z puli, cele partii, wynik, podsumowanie | pełna partia z celem |
| **H. Multiplayer** | kilku graczy, pauzy, dyplomacja między ludźmi | partia ze znajomym |
