# ADR 0021: Wojna z celem, wyprawy, zaopatrzenie i uzbrojenie z technologii

**Status:** przyjęta, 2026-11-07 (decyzje Patryka do dokumentu „Etap 4e: wojna — propozycja”; plan `docs/prompts/07-etap-wojna.md`,
etap 4e w `ROADMAP.md`, zasady w GDD 9.3).

## Kontekst

Po etapie 4d wojna jest tylko stanem między cywilizacjami. Jedynym ruchem jest wypędzenie obcego obozu: obóz przenosi się na obcy
obóz i wygrywa z szansą równą udziałowi siły. Wojna nie ma celu, wyniku ani warunków pokoju. Kończy się, gdy trwa długo. Nie ma
armii, zaopatrzenia, terenu ani morale. Patryk chce wojny z przyczynami i skutkami jak w etapie 4F: uzbrojenie z technologii,
dowódca, który liczy szanse, zaopatrzenie z łańcuchem dostaw i podbój.

## Decyzja

- **Cel wojny jest obowiązkowy:** ziemia (osada wroga), łupy, odwet. Cel ustala warunki pokoju i karę w oczach świadków
  (nowe wspomnienie „napaść bez powodu”).
- **Wynik wojny (−100…+100)** z wygranych walk, zdobytych osad i łupów. **Zmęczenie** (0…100) każdej strony rośnie z latami wojny
  i stratami w ludziach. Obniża nastrój przez efekt `war_weariness` w `data/effects.json`.
- **Pokój z warunkami:** oddanie spornej osady, jednorazowy okup w dobrach (część zapasów przegranego) albo biały pokój.
  Warunki wynikają z celu i wyniku. Trybut i wasalstwo są modułami.
- **Uzbrojenie z technologii** (`data/war.json`, `armaments`): poziom = technologia, dobro i jego zużycie na wojownika, procent siły.
  Wojownik bierze najlepszy poziom, na jaki stać jego osadę; bez uzbrojenia walczy za 50%. Kolejne ery dopisują poziomy w danych.
- **Armia (wyprawa)** to byt na mapie z wodzem, ludźmi, uzbrojeniem, jedzeniem i morale. Limit wodza i liczba ludzi na polu są
  w danych er. Dziś armia mieści się na jednym polu; większa w przyszłych erach zajmie kilka sąsiednich pól (front), bez zmiany kodu bitwy.
  Co najmniej 1/3 wojowników osady zostaje w domu.
- **Bitwa w rundach** na strumieniu losowym `combat`. Siła = ludzie × uzbrojenie × teren × morale × najedzenie. Straty co rundę,
  spadek morale, ucieczka zamiast śmierci przy niskim morale.
- **Zaopatrzenie:** tragarze (jednostki jak zwiadowcy) z własnych osad blisko trasy marszu donoszą jedzenie. Armia bez jedzenia traci
  morale i walczy słabiej, a dowódca kieruje ją na najbliższe wioski wroga po łupy.
- **Dowódca (nowy doradca AI)** liczy spodziewany wynik bitwy tym samym modelem, ale bez losowania i tylko z tego, co widzi.
  Nie rusza i nie atakuje poniżej progu szansy z `data/ai.json`.
- **Podbój:** wioska przechodzi do zwycięzcy z mieszkańcami (fachowcy też). Pokonani z obozu uciekają.
- **Co erę** moduł wojny trzeba zaktualizować (sekcja „Co erę” w `docs/systems/war.md`, przypomnienie w `CLAUDE.md` i `ROADMAP.md`).

## Skutki

- Nowe pliki: `data/war.json`, `docs/systems/war.md`. Nowy stan: pola wojny w traktacie, armie, tragarze. Nowy format zapisu.
- Dzisiejsze wypędzenie obozu w czasie wojny zastępuje bitwa wyprawy. W czasie pokoju zostaje jak jest (spór o obozowisko).
- Oblężenia i mury czekają na budynki, plemiona niezależne na secesję w 4f.
- Podział na 5 PR-ów: `docs/prompts/07-etap-wojna.md`.
