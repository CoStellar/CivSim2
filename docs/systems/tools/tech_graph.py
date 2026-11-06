"""Draws the tree of technologies, skills and people from the game data (stage 4F, ADR 0020).

Usage: python3 docs/systems/tools/tech_graph.py [DATA_DIR]
Writes docs/systems/graf-technologii.md (Mermaid, shown by GitHub) and, when the Mermaid CLI (mmdc) is installed,
docs/systems/img/graf_technologii.png. Run it again after changing data/technologies.json, skills.json or strata.json.
"""
import json
import os
import shutil
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..', '..'))
DATA = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, 'data')
OUT_MD = os.path.join(ROOT, 'docs', 'systems', 'graf-technologii.md')
OUT_PNG = os.path.join(ROOT, 'docs', 'systems', 'img', 'graf_technologii.png')

# Polish labels for Patryk; anything missing falls back to the English name from the data.
POLISH = {
    'fire': 'Ogień', 'flint_knapping': 'Obróbka krzemienia', 'cooperative_hunting': 'Łowiectwo zespołowe',
    'fishing': 'Rybołówstwo', 'tanning': 'Garbarstwo', 'traditions': 'Tradycje', 'agriculture': 'Rolnictwo',
    'animal_husbandry': 'Udomowienie zwierząt', 'pottery': 'Garncarstwo', 'weaving': 'Tkactwo',
    'stonemasonry': 'Kamieniarstwo', 'irrigation': 'Nawadnianie', 'bread_baking': 'Wypiek chleba', 'barter': 'Wymiana barterowa',
    'hunter': 'myśliwy', 'fisher': 'rybak', 'farmer': 'rolnik', 'herder': 'pasterz', 'knapper': 'krzemieniarz',
    'tanner': 'garbarz', 'potter': 'garncarz', 'weaver': 'tkacz', 'mason': 'kamieniarz', 'baker': 'piekarz',
    'producers': 'wytwórcy', 'artisans': 'rzemieślnicy', 'warriors': 'wojownicy', 'thinkers': 'myśliciele', 'leaders': 'przywódcy',
}
ERAS = {1: 'Epoka kamienia', 2: 'Neolit', 3: 'Era brązu', 4: 'Era żelaza'}


def load(name):
    with open(os.path.join(DATA, name), encoding='utf-8') as f:
        return json.load(f)


def label(item):
    return POLISH.get(item['key'], item['name'])


def people_edges(tech, field, alternative):
    lines = []
    for p in tech.get(field, []):
        source = 'k_' + p['skill'] if 'skill' in p else 's_' + p['stratum']
        text = str(p['count']) + (' w osadzie' if p.get('inOneSettlement') else '') + (' albo' if alternative else '')
        lines.append(f'  {source} -->|{text}| t_{tech["key"]}')
    return lines


def build():
    techs = load('technologies.json')['technologies']
    skills = load('skills.json')['skills']
    strata = {s['key']: s for s in load('strata.json')['strata']}
    used_strata = sorted({p['stratum'] for t in techs for f in ('requiresPeople', 'requiresPeopleAny')
                          for p in t.get(f, []) if 'stratum' in p})
    skill_of = {k['technology']: k for k in skills}

    lines = ['flowchart TD']
    for era in sorted({t['era'] for t in techs}):
        lines.append(f'  subgraph E{era}["{ERAS.get(era, "Era " + str(era))}"]')
        for t in (t for t in techs if t['era'] == era):
            lines.append(f'    t_{t["key"]}["{label(t)}"]')
            if t['key'] in skill_of:
                k = skill_of[t['key']]
                lines.append(f'    k_{k["key"]}(["{label(k)}"])')
        lines.append('  end')
    for key in used_strata:
        lines.append(f'  s_{key}{{{{"{label(strata[key])}"}}}}')

    for t in techs:
        for r in t.get('requires', []):
            lines.append(f'  t_{r} -.-> t_{t["key"]}')
        for r in t.get('requiresAny', []):
            lines.append(f'  t_{r} -.->|albo| t_{t["key"]}')
        if t['key'] in skill_of:
            lines.append(f'  t_{t["key"]} ==> k_{skill_of[t["key"]]["key"]}')
        lines += people_edges(t, 'requiresPeople', False)
        lines += people_edges(t, 'requiresPeopleAny', True)

    lines.append('  classDef tech fill:#e8f0fe,stroke:#2a6fdb,color:#111')
    lines.append('  classDef skill fill:#fff1e0,stroke:#e8833a,color:#111')
    lines.append('  classDef stratum fill:#e6f5ec,stroke:#3aa676,color:#111')
    lines.append('  class ' + ','.join('t_' + t['key'] for t in techs) + ' tech')
    lines.append('  class ' + ','.join('k_' + k['key'] for k in skills) + ' skill')
    if used_strata:
        lines.append('  class ' + ','.join('s_' + s for s in used_strata) + ' stratum')
    return '\n'.join(lines) + '\n'


def main():
    graph = build()
    with open(OUT_MD, 'w', encoding='utf-8') as f:
        f.write('# Graf technologii, fachów i ludzi\n\n')
        f.write('Plik tworzy skrypt `docs/systems/tools/tech_graph.py` z `data/technologies.json`, `data/skills.json` i '
                '`data/strata.json`. Nie edytuj go ręcznie; po zmianie danych uruchom skrypt jeszcze raz.\n\n')
        f.write('Jak czytać (etap 4F, ADR 0020):\n\n')
        f.write('- **prostokąt (niebieski):** technologia; kropkowana strzałka: technologia, której wymaga (`albo`: jedna z kilku);\n')
        f.write('- **gruba strzałka:** technologia otwiera fach (pierwszych 2 mistrzów, potem uczniowie);\n')
        f.write('- **owal (pomarańczowy):** fach; strzałka z liczbą: tylu fachowców trzeba, żeby badać technologię (uczeń za pół);\n')
        f.write('- **sześciokąt (zielony):** warstwa ludzi (np. myśliciele), też z liczbą;\n')
        f.write('- `w osadzie`: wszyscy w jednej osadzie (pismo i filozofia w kolejnych erach).\n\n')
        f.write('Test `TechnologyGraphTests` sprawdza, że do każdej technologii i każdego fachu da się dojść od zera, '
                'a wczytywanie danych odrzuca fach z technologii, która nie jest wcześniej w drzewie (pętla albo ukryty krok).\n\n')
        f.write('![Graf technologii](img/graf_technologii.png)\n\n')
        f.write('```mermaid\n' + graph + '```\n')
    print(OUT_MD)

    mmdc = shutil.which('mmdc')
    if mmdc:
        os.makedirs(os.path.dirname(OUT_PNG), exist_ok=True)
        with tempfile.TemporaryDirectory() as tmp:
            src = os.path.join(tmp, 'graph.mmd')
            with open(src, 'w', encoding='utf-8') as f:
                f.write(graph)
            subprocess.run([mmdc, '-i', src, '-o', OUT_PNG, '-s', '2', '-b', 'white'], check=True)
        print(OUT_PNG)
    else:
        print('mmdc not found: PNG skipped')


if __name__ == '__main__':
    main()
