# ADR 0020: Fachy, nauka fachu, wymogi ludzi w technologiach i demografia z danych historycznych

**Status:** przyjęta, 2026-11-06 (decyzje Patryka do dokumentu „Etap: ludzie, zawody i przyczyny rozwoju — propozycja”;
plan `docs/prompts/06-etap-fachy.md`, etap 4F w `ROADMAP.md`).

## Kontekst

Po PR C ludności jest 1,6–2,3 razy więcej niż wcześniej. Przyrost (+3‰ na rok od startu) ogranicza w praktyce tylko jedzenie,
a technologie nie zmieniają śmiertelności. Nauka rośnie z liczbą ludzi, bez względu na to, kim są, a technologie wymagają tylko
innych technologii. Patryk: rozwój ma zależeć od ludzi, którzy umieją daną rzecz (łodzie od cieśli, broń od kowali, kowale od
wytopu metali), w łańcuchu przyczyn od zera; przyrost ma rosnąć dopiero z technologiami poprawiającymi życie.

## Decyzja

- **Dwa poziomy ludzi.** Warstwa (potrzeby, głos w państwie) i fach (co ludzie umieją). Nowe warstwy od startu: myśliciele
  (starszyzna, myśliciele, skrybowie, uczeni; nie kapłani, gra nie ma religii) i przywódcy. Fachy w `data/skills.json`: warstwa,
  technologia, która otwiera fach, lata nauki, surowce, poziom narzędzi.
- **Nauka fachu:** 2 pierwszych mistrzów w osadzie z największą liczbą myślicieli po odkryciu technologii; mistrz uczy do 3 uczniów;
  uczeń pracuje za pół przez lata nauki z danych; nauka tylko tam, gdzie fach jest potrzebny.
- **Przenoszenie fachu:** gdy osada ma co najmniej 3 mistrzów i o co najmniej 2 fachowców więcej niż miejsc pracy, najwyżej połowa
  nadwyżki przenosi się do najbliższej osady własnej sieci, w której fach jest potrzebny i brakuje mistrza albo rąk; raz na krok
  cywilizacji. Od obcych fach przychodzi tylko przez handel.
- **Utrata fachu:** po śmierci ostatniego fachowca technologia zostaje znana, ale nieużywana, dopóki fach nie wróci.
- **Wymóg ludzi w technologiach** (`requiresPeople`): liczba fachowców w całej cywilizacji, dla technologii „miejskich” (pismo,
  filozofia) w jednej osadzie. Nauka głównie od myślicieli: myśliciel 30, rzemieślnik 5, reszta 1 punkt na tysiąc na rok.
- **Demografia:** przyrost = urodzenia × (1 − śmiertelność dzieci) − zgony dorosłych, z efektami technologii, według danych
  historycznych (Volk i Atkinson 2013; Our World in Data): śmiertelność dzieci ok. 50% aż do renesansu, 25% w erze przemysłowej,
  4% w nowoczesnej; przyrost najedzonej osady w epoce kamienia +1‰ na rok. Tabela: GDD 5.2.

## Skutki

- Wolniejszy przyrost na start: mniej osad, krótsza tura.
- Kilkanaście liczb na osadę (fachowcy i uczniowie na fach), bez porównań par osad.
- Format zapisu i protokół podglądu rosną o fachy; nowe złote hashe.
- AI: uczony widzi braki ludzi; nowa decyzja, ilu ludzi kierować do nauki fachu (`docs/ai/`).
- Ery 3–10: fachy i wymogi wejdą z technologiami swoich etapów (6–12); graf w planie to szkic.
