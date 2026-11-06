# Graf technologii, fachów i ludzi

Plik tworzy skrypt `docs/systems/tools/tech_graph.py` z `data/technologies.json`, `data/skills.json` i `data/strata.json`. Nie edytuj go ręcznie; po zmianie danych uruchom skrypt jeszcze raz.

Jak czytać (etap 4F, ADR 0020):

- **prostokąt (niebieski):** technologia; kropkowana strzałka: technologia, której wymaga (`albo`: jedna z kilku);
- **gruba strzałka:** technologia otwiera fach (pierwszych 2 mistrzów, potem uczniowie);
- **owal (pomarańczowy):** fach; strzałka z liczbą: tylu fachowców trzeba, żeby badać technologię (uczeń za pół);
- **sześciokąt (zielony):** warstwa ludzi (np. myśliciele), też z liczbą;
- `w osadzie`: wszyscy w jednej osadzie (pismo i filozofia w kolejnych erach).

Test `TechnologyGraphTests` sprawdza, że do każdej technologii i każdego fachu da się dojść od zera, a wczytywanie danych odrzuca fach z technologii, która nie jest wcześniej w drzewie (pętla albo ukryty krok).

![Graf technologii](img/graf_technologii.png)

```mermaid
flowchart TD
  subgraph E1["Epoka kamienia"]
    t_fire["Ogień"]
    t_flint_knapping["Obróbka krzemienia"]
    k_knapper(["krzemieniarz"])
    t_cooperative_hunting["Łowiectwo zespołowe"]
    k_hunter(["myśliwy"])
    t_fishing["Rybołówstwo"]
    k_fisher(["rybak"])
    t_tanning["Garbarstwo"]
    k_tanner(["garbarz"])
    t_traditions["Tradycje"]
  end
  subgraph E2["Neolit"]
    t_agriculture["Rolnictwo"]
    k_farmer(["rolnik"])
    t_animal_husbandry["Udomowienie zwierząt"]
    k_herder(["pasterz"])
    t_pottery["Garncarstwo"]
    k_potter(["garncarz"])
    t_weaving["Tkactwo"]
    k_weaver(["tkacz"])
    t_stonemasonry["Kamieniarstwo"]
    k_mason(["kamieniarz"])
    t_irrigation["Nawadnianie"]
    t_bread_baking["Wypiek chleba"]
    k_baker(["piekarz"])
    t_barter["Wymiana barterowa"]
  end
  s_thinkers{{"myśliciele"}}
  t_flint_knapping ==> k_knapper
  t_flint_knapping -.-> t_cooperative_hunting
  t_cooperative_hunting ==> k_hunter
  k_knapper -->|5| t_cooperative_hunting
  t_flint_knapping -.-> t_fishing
  t_fishing ==> k_fisher
  k_knapper -->|5| t_fishing
  t_cooperative_hunting -.-> t_tanning
  t_tanning ==> k_tanner
  k_hunter -->|10| t_tanning
  t_fire -.-> t_traditions
  s_thinkers -->|5| t_traditions
  t_fire -.-> t_agriculture
  t_flint_knapping -.-> t_agriculture
  t_agriculture ==> k_farmer
  s_thinkers -->|10| t_agriculture
  t_cooperative_hunting -.-> t_animal_husbandry
  t_animal_husbandry ==> k_herder
  k_hunter -->|10| t_animal_husbandry
  t_agriculture -.-> t_pottery
  t_pottery ==> k_potter
  k_farmer -->|20| t_pottery
  t_animal_husbandry -.->|albo| t_weaving
  t_agriculture -.->|albo| t_weaving
  t_weaving ==> k_weaver
  k_herder -->|10 albo| t_weaving
  k_farmer -->|20 albo| t_weaving
  t_flint_knapping -.-> t_stonemasonry
  t_stonemasonry ==> k_mason
  k_knapper -->|10| t_stonemasonry
  t_agriculture -.-> t_irrigation
  k_farmer -->|30| t_irrigation
  t_agriculture -.-> t_bread_baking
  t_fire -.-> t_bread_baking
  t_bread_baking ==> k_baker
  k_farmer -->|20| t_bread_baking
  t_pottery -.-> t_barter
  k_potter -->|10| t_barter
  classDef tech fill:#e8f0fe,stroke:#2a6fdb,color:#111
  classDef skill fill:#fff1e0,stroke:#e8833a,color:#111
  classDef stratum fill:#e6f5ec,stroke:#3aa676,color:#111
  class t_fire,t_flint_knapping,t_cooperative_hunting,t_fishing,t_tanning,t_traditions,t_agriculture,t_animal_husbandry,t_pottery,t_weaving,t_stonemasonry,t_irrigation,t_bread_baking,t_barter tech
  class k_hunter,k_fisher,k_farmer,k_herder,k_knapper,k_tanner,k_potter,k_weaver,k_mason,k_baker skill
  class s_thinkers stratum
```
