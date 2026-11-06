# Technologie i badania

**W jednym zdaniu:** ludzie każdej cywilizacji wytwarzają punkty nauki, które idą na jedną wybraną technologię; technologia jest tańsza, gdy sytuacja jej sprzyja (inspiracje) albo zna ją sąsiad (dyfuzja), a po zbadaniu odblokowuje receptury, zbieranie nowych dóbr i stałe efekty, i może przenieść cywilizację w nową erę (etap 3f).

**Faza tury:** 10. Nauka i wpływy (badanie, efekty, ery). Wybór technologii: faza decyzji (`ScholarAdvisor`, doradca uczony, komenda `SetResearchCommand`).

## Dane wejściowe i wyjściowe
- Czyta: `data/technologies.json` (graf, koszty, inspiracje, odblokowania, zasady dyfuzji), `data/eras.json` (`threshold`), `sciencePerThousandPerYear` warstw z `data/strata.json`, osady i ludność, warstwę `claim` mapy (strefy wpływów), rzeki, temperatury, zapasy.
- Zmienia: `CivilizationState.KnownTechnologies`, `KnownCount`, `Researching`, `ResearchProgress`, `LastScience`, `Era`; dodaje stałe efekty cywilizacji (`EffectBook`); wpisy w kronice („X learned Fire.”, „X entered the Neolithic.”).
- Publikuje w migawce: nic nowego (ery i technologie pokaże podgląd w jednej z kolejnych zmian; kronika już idzie do klienta).

## Przepływ

```mermaid
flowchart LR
  P["Ludzie × nauka warstwy<br/>(na 1000 osób na rok)"] --> R["Postęp badania"]
  F["Sytuacja cywilizacji:<br/>złoża, biomy, rzeki w strefie,<br/>najzimniejsza zima, ludzie, towary, sąsiedzi"] --> C["Koszt dziś = koszt × (100% − inspiracje − dyfuzja), min. 25%"]
  R --> L{"postęp ≥ koszt?"}
  C --> L
  L -->|tak| K["Technologia poznana"]
  K --> E["Efekty: gotowanie, ciepło, tradycja, przechowywanie"]
  K --> G["Zbieranie: krzemień, ryby, stada, zboże, glina, kamień"]
  K --> RC["Receptury"]
  K --> ER{"próg ery + 60% technologii ery?"}
  ER -->|tak| N["Nowa era → krótsza tura"]
  AI["ScholarAdvisor: najtańsza dostępna"] --> SR["SetResearchCommand"] --> R
```

## Zasady i wzory
- **Nauka** (GDD 6.1, uproszczone: uczeni i budynki przyjdą z erami): punkty na rok = Σ ludzie warstwy × `sciencePerThousandPerYear` / 1000 (wytwórcy 3, rzemieślnicy 30, wojownicy 2). Obóz 100 ludzi daje ok. 0,4 punktu na rok.
- **Badanie:** jedna technologia naraz. Punkty zbierają się zawsze; nadwyżka po zbadaniu przechodzi na następną.
- **Wymagania:** `requires` (wszystkie) i `requiresAny` (co najmniej jedna, np. tkactwo: hodowla albo rolnictwo).
- **Koszt dziś** = koszt bazowy × max(25%, 100% − Σ inspiracji − dyfuzja). Inspiracje (GDD 6.1): towar w magazynach cywilizacji, złoże, biom albo rzeka w strefie wpływów (woda tuż przy strefie też się liczy), zima obozu co najmniej tak zimna, liczba ludzi. **Dyfuzja:** −20% za każdego sąsiada, który zna technologię, najwyżej 2 sąsiadów; sąsiad = inna cywilizacja z osadą w promieniu 12 pól od naszej.
- **Odblokowania:** receptury (każda receptura musi mieć technologię, sprawdza to ładowanie danych); dodatkowe zbieranie według złoża albo biomu (łowienie ryb działa na wodzie przy strefie wpływów, bo strefy nie wchodzą na wodę); stałe efekty cywilizacji: `cooking` (+% plonów), `cold_tolerance`, `tradition` (+nastrój), `storage` (−% psucia), `carrying`.
- **Ery** (GDD 6.2): cywilizacja wchodzi w następną erę, gdy zna jej technologię progową (neolit: rolnictwo) i 60% technologii obecnej ery. Długość tury wyznacza nadal najbardziej zaawansowana żywa cywilizacja.
- **AI** (doradca uczony `ScholarAdvisor`, od etapu 4a na wspólnej ocenie, `docs/systems/ai.md`): bierze najtańszą dostępną technologię po dzisiejszych zniżkach, więc sytuacja kieruje rozwojem (nad morzem tanie rybołówstwo, przy zwierzynie łowiectwo, w zimnie ogień i garbarstwo).
- Odkrycia przypadkiem przez użytkowanie: w GDD 16 „Wytnij” (dubluje inspiracje).

## Pliki danych
- `data/technologies.json`: 14 technologii er 1–2 z GDD 6.3, `diffusionPercent`, `maxNeighbours`, `contactCells`, `minCostPercent`, `eraSharePercent`.
- `data/eras.json`: `threshold`.
- `data/strata.json`: `sciencePerThousandPerYear`.
- `data/effects.json`: `cooking`, `tradition`, `storage` (nowa statystyka `spoilage_percent`).

## Testy
- `ResearchTests`: graf się wczytuje i każda receptura ma technologię; wymagania (także „jedna z”) blokują badanie; inspiracje, dyfuzja i dolna granica kosztu; badanie uczy technologii i włącza efekty, nadwyżka przechodzi dalej; rolnictwo i 60% epoki kamienia otwierają neolit i skracają turę; AI wybiera najtańszą technologię, a komenda odrzuca niedostępną; rybołówstwo daje jedzenie z wybrzeża; ceramika spowalnia psucie; determinizm i zapis.
- `TradeProductionTests`: receptury tylko ze znanych technologii.

## Wydajność i strojenie
- Seed 7, mapa standard, 500 tur: tura ok. 21 ms (bez etapu 3f ok. 18,4 ms). Sytuację cywilizacji (`CivFacts`, przegląd stref wpływów) mierzymy tylko wtedy, gdy któraś cywilizacja ma już co najmniej 25% kosztu albo nie ma celu badań.
- Tempo (seed 7, 1500 tur): pierwsza cywilizacja zna rolnictwo i wchodzi w neolit w turze 227 (ok. 11 tys. lat), druga w turze 243; pod koniec średnio 10 z 14 technologii. Koszty: epoka kamienia 6 000–16 000, rolnictwo 27 000, reszta neolitu 12 000–20 000. Do strojenia razem z osiedlaniem się (wioski powinny przyspieszyć naukę).
