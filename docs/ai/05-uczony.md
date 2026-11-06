# Uczony (U1)

[← spis treści](README.md) · kod: `src/CivSim.Core/Research/ResearchSystem.cs:112` (`ScholarAdvisor`) · GDD 6.1, 8.1 · system: `docs/systems/research.md`

## U1: Wybór technologii do badania

**Komenda:** `SetResearchCommand(technologia)`. Odrzucana, gdy technologia jest już znana albo jej wymagania nie są spełnione.

### Kiedy

Dla każdej żywej cywilizacji AI, która **nic nie bada** (na starcie i zaraz po odkryciu technologii, bo system badań zeruje wybór).
Punkty zebrane ponad koszt przechodzą na następną technologię, więc przerwy w badaniu nie ma.

### Kandydaci

Każda technologia **dostępna**: jeszcze nieznana, wszystkie wymagane (`requires`) znane, a jeśli ma listę „jedna z” (`requiresAny`),
to co najmniej jedna z niej znana.

### Ocena

```
koszt dziś = koszt bazowy × (100% − inspiracje − dyfuzja), ale nie mniej niż 25% kosztu bazowego
najtańsza = najniższy koszt dziś wśród dostępnych
ocena = 1 000 000 000 × (najtańsza / koszt)²  × waga nauki × (500 + ciekawość)
```

- **Inspiracje** (`data/technologies.json`): sytuacje, które obniżają koszt o podany procent, np. ogień −30% przy zimie poniżej 0 °C,
  rybołówstwo −25% przy wybrzeżu w strefie, rolnictwo −30% przy złożu dzikiego zboża i −15% przy rzece.
- **Dyfuzja:** każdy sąsiad (cywilizacja z osadą w promieniu 12 pól od naszej), który zna technologię, obniża koszt o 20%, najwyżej
  2 sąsiadów.
- Kwadrat ilorazu liczony jest w liczbach całkowitych: `1000 × najtańsza / koszt × najtańsza / koszt` (‰).

Wybór: 3 najlepsze, ostrożność albo losowanie. Najtańsza ma czynnik 1000; dwa razy droższa 250, więc w losowaniu ma 4 razy mniejszą
szansę. Trzy razy droższa: 111.

### Zapis „Dlaczego?”

Czynnik `research cost` (z kosztem dziś w setnych punktu), waga nauki i charakter. Przykład (seed 7, tura 1):
`Civilization 1, Scholar, SetResearch 0 (ogień), ocena 1052000000: character 105% (0), research cost 100% (420000)`.
Koszt 4200 zamiast 6000, bo zadziałała inspiracja „zima poniżej 0 °C” (−30%).

### Co uczony bierze pod uwagę, a czego nie

| Bierze pod uwagę | Nie bierze pod uwagę |
| --- | --- |
| koszt dziś (inspiracje, sąsiedzi) | co technologia daje (receptury, zbieranie, efekty) |
| | czego cywilizacji brakuje (głód, zimno, brak narzędzi) |
| | drogi do ważnych technologii (np. rolnictwa) |
| | wagi stratega (nauka zawsze 1000) |

Uczony to dziś **„bierz najtańsze”**. Sytuacja wpływa na wybór tylko pośrednio, przez inspiracje: plemię nad morzem ma tańsze
rybołówstwo, plemię wśród zwierzyny tańsze łowiectwo. Ciekawość świata mnoży wszystkie technologie jednakowo, więc nie zmienia
wyboru. Patrz [uwagi](08-ocena-i-uwagi.md#3-uczony-wybiera-najtańsze-a-nie-najpotrzebniejsze).
