# Skarbnik (T1)

[← spis treści](README.md) · kod: `src/CivSim.Core/Ai/TreasurerAdvisor.cs` · GDD 4.6, 8.1 · system: `docs/systems/economy-trade.md`

Skarbnik decyduje tylko o jednym: **którą recepturę robią rzemieślnicy każdej osady**. Podatki, polityki i handel z innymi
cywilizacjami z GDD 8.1 jeszcze nie istnieją.

## T1: Wybór receptury

**Komenda:** `SetRecipeCommand(osada, receptura)` (albo „brak receptury”: rzemieślnicy przestają pracować). Odrzucana, gdy osada jest
pusta albo żadna znana technologia nie odblokowuje receptury.

### Kiedy i dla kogo

- Własny krok cywilizacji, który zaczyna okres długości kroku ery (`WorldClock.PlansNow`, ADR 0018; na Ziemi każdy własny krok), i
  tylko gdy w grze są receptury.
- Dla każdej **sieci handlowej** cywilizacji AI (osady jednej cywilizacji połączone łańcuchem odstępów ≤ 8 pól), a w niej dla każdej
  osady, która ma **rzemieślników**. Obozy też.

### Dane wejściowe (wspólne dla całej sieci)

- **Popyt i podaż** sieci (`Market.Tally`): popyt = roczne potrzeby mieszkańców + roczne wejścia receptur, które sieć już robi;
  podaż = to, co leży w magazynach sieci. Wszystkie rodzaje jedzenia mają wspólny stosunek popytu do podaży.
- **Cena** towaru (`Market.Price`): `wartość towaru × popyt / podaż`, ograniczona do 25–400% wartości.
- **Waga towaru** (od PR B): jedzenie służy bezpieczeństwu (`goalOfFood`), reszta dobrobytowi (`goalOfGoods`). Waga = waga celu od
  stratega × charakter dla tego celu.
- **Premia handlowa** (od PR B): gdy cywilizacja ma umowę handlową, przez którą towary już płyną, wyroby cenne (narzędzia, odzież,
  ceramika, tkaniny) są warte więcej: `50% (tradeGoodsBonusPercent) × handlowość`, czyli +25% przy handlowości 500.

### Ocena receptury

Dla każdej receptury, którą cywilizacja zna (przez technologie), w kolejności numerów:

```
cena wejścia = cena przy popycie sieci BEZ własnej receptury tej osady, najwyżej wartość bazowa towaru
cena wyjścia = cena przy podaży powiększonej o rok własnej produkcji (pełny magazyn obniża cenę)
wartość = Σ cena wyjścia × ilość × waga towaru (× premia handlowa dla cennych)
        − Σ cena wejścia × ilość × waga towaru                       (dla jednego przebiegu receptury)
ocena = wartość
      × dostępność wejść: 1000‰, gdy zapas sieci ≥ wejścia jednego przebiegu (albo to obecna receptura), inaczej 0 (wyklucza)
```

Receptury z oceną ≤ 0 nie biorą udziału. Wybór: 3 najlepsze, ostrożność albo losowanie.

Dlaczego tak (PR B, koniec „skakania” receptur): dawniej własne wejścia osady podbijały cenę surowca, więc praca „traciła wartość”,
osada przestawała pracować, cena spadała i za krok wracała do tej samej receptury. Teraz osada nie płaci sama sobie za swój popyt,
a cena wyjścia uwzględnia, że przez rok zapełni magazyn.

### Koordynacja sieci (od PR B)

Osady jednej sieci wybierają po kolei. Po każdym wyborze skarbnik poprawia dane dla następnych (`Plan`): wejścia wybranej receptury
dochodzą do popytu (porzuconej znikają), a rok jej produkcji dochodzi do podaży. Druga osada widzi więc, że skóry są już „zajęte”, a
odzieży przybędzie, i chętniej robi coś innego. Tak powstają łańcuchy: jedna osada piecze chleb, druga tka.

### Histereza

```
zostań przy obecnej, gdy: obecna ma ocenę > 0  i  wybrana × 100 ≤ obecna × 125   (recipeSwitchBetterByPercent: 25)
```

Obecna receptura liczy się jako wykonalna nawet wtedy, gdy w tej turze zabrakło wejść (produkcja po prostu czeka). Gdy nic nie daje
wartości dodanej, a obecna też nie, skarbnik wysyła „brak receptury”.

Komenda jest wysyłana tylko wtedy, gdy wybrana receptura różni się od obecnej i histereza nie zatrzymała zmiany.

### Zapis „Dlaczego?”

Ocena, notatki `value added` (wartość dodana bez wag), `serves <cel>` (cel pierwszego wyjścia), `for trade` (premia handlowa w ‰,
gdy zadziałała) i czynnik `inputs available`. Przykłady z gry (seed 7):

- tura 27: `Lakhaem, SetRecipe 0 (narzędzia kamienne), ocena 274488: value added 313700, serves prosperity, inputs available 100%`;
- tura 55: `Rishboy, SetRecipe 0 (narzędzia kamienne), ocena 527438: value added 313700, serves prosperity, for trade 363`
  (handlowość 726, towary płyną do partnera, więc narzędzia są warte o 36% więcej).

### Jak to działa w praktyce

Potrzeby napędzają ceny: plemię bez odzieży ma drogą odzież, więc szyje odzież; gdy magazyny się zapełnią, cena spada i opłaca się
robić narzędzia. Głodna sieć (wysoka waga bezpieczeństwa, puste spiżarnie) piecze chleb, choć odzież daje więcej „na papierze”.
Handlująca cywilizacja chętniej robi wyroby na wymianę. Dzisiejsze receptury (`data/recipes.json`): narzędzia kamienne, odzież ze
skór, ceramika, tkanie wełny, tkanie lnu, pieczenie chleba.

### Uproszczenia (do Twojej oceny)

- Przełączanie odzież ↔ chleb zostało (ok. 800–900 zmian na grę): cena jedzenia wyraźnie skacze z plonami, więc raz wygrywa chleb,
  raz odzież. Histereza 25% tego nie zatrzymuje. To prawdziwa zmiana sytuacji, nie błąd liczenia, ale jest jej dużo
  ([uwagi](08-ocena-i-uwagi.md)).
- Liczy się wartość jednego przebiegu, nie ile przebiegów zrobią rzemieślnicy (tylko cena wyjścia uwzględnia rok produkcji).
- Premia handlowa nie patrzy, czego potrzebuje partner: każdy wyrób cenny jest „na wymianę”.
