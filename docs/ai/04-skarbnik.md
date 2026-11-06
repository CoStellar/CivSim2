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

- **Ceny wewnętrzne** sieci (`Market.Prices`): `wartość towaru × popyt / podaż`, ograniczone do 25–400% wartości. Popyt = roczne
  potrzeby mieszkańców + wejścia receptur, które sieć już robi. Podaż = to, co leży w magazynach sieci. Wszystkie rodzaje jedzenia
  mają wspólny stosunek popytu do podaży.
- **Zapas** sieci: suma magazynów wszystkich osad sieci.

### Ocena receptury

Dla każdej receptury, którą cywilizacja zna (przez technologie), w kolejności numerów:

```
wartość dodana = Σ cena wyjścia × ilość − Σ cena wejścia × ilość   (dla jednego przebiegu receptury)
ocena = wartość dodana
      × dostępność wejść: 1000‰, gdy zapas sieci ≥ wejścia jednego przebiegu (albo to obecna receptura), inaczej 0 (wyklucza)
      × waga dobrobytu × (500 + handlowość)
```

Receptury z oceną ≤ 0 nie biorą udziału. Wybór: 3 najlepsze, ostrożność albo losowanie.

### Histereza

```
zostań przy obecnej, gdy: obecna ma ocenę > 0  i  wybrana × 100 ≤ obecna × 125   (recipeSwitchBetterByPercent: 25)
```

Obecna receptura liczy się jako wykonalna nawet wtedy, gdy w tej turze zabrakło wejść (produkcja po prostu czeka). Gdy nic nie daje
wartości dodanej, a obecna też nie, skarbnik wysyła „brak receptury”.

Komenda jest wysyłana tylko wtedy, gdy wybrana receptura różni się od obecnej i histereza nie zatrzymała zmiany.

### Zapis „Dlaczego?”

Ocena, notatka `value added`, czynnik `inputs available`, waga i charakter. Przykład z gry (seed 7, tura 27):
`Civilization 4, Treasurer, SetRecipe 0 (wyrób kamiennych narzędzi), ocena 274487: character 87% (0), value added 313700, inputs available 100% (1)`.

### Jak to działa w praktyce

Potrzeby napędzają ceny: plemię bez odzieży ma drogą odzież, więc szyje odzież; gdy magazyny się zapełnią, cena spada i opłaca się
robić narzędzia. Dzisiejsze receptury (`data/recipes.json`): narzędzia kamienne, odzież ze skór, ceramika, tkanie wełny, tkanie lnu,
pieczenie chleba.

### Uproszczenia (do Twojej oceny)

- Wszystkie osady sieci widzą te same ceny i ten sam zapas, a wybór jednej nie zmienia danych dla następnej. Kilka osad może więc
  wybrać tę samą recepturę, choć wejść wystarczy dla jednej.
- Waga dobrobytu i handlowość mnożą **wszystkie** receptury jednakowo, więc **nie zmieniają wyboru** (patrz
  [uwagi](08-ocena-i-uwagi.md#1-wagi-i-cechy-które-nic-nie-zmieniają)).
- Liczy się tylko wartość dodana jednego przebiegu, nie ile przebiegów zrobią rzemieślnicy ani jak długo trwa receptura.
