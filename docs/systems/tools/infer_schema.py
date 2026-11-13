#!/usr/bin/env python3
"""Writes a first JSON Schema for every file in data/ from the keys the file uses today (stage 6, T14).

Run once for a new data file, then edit the schema by hand (add optional keys the loader knows):
    python3 docs/systems/tools/infer_schema.py data data/schema [file.json ...]
Existing schemas are not overwritten unless --force is given.
Objects whose keys are names of other content (goods, strata, biomes, AI goals...) are maps: any key, values checked.
"""
import json, os, sys

# Properties whose value is a map {content key: value}; the loader checks the keys itself.
MAP_PROPERTIES = {"gather", "goalPercent", "goods", "inputs", "leaning", "outputs", "shifts", "stock",
                  "strataInfluence", "patterns", "newTribe@population.json", "biomes@units.json"}
ALWAYS = {"comment"}  # allowed in every object, never read by the loader


def is_map(name, file, parent_is_terrain):
    if name in MAP_PROPERTIES or f"{name}@{file}" in MAP_PROPERTIES and (name != "biomes" or parent_is_terrain):
        return True
    return False


def scalar_type(values):
    kinds = set()
    for v in values:
        if isinstance(v, bool):
            kinds.add("boolean")
        elif isinstance(v, (int, float)):
            kinds.add("number")
        elif isinstance(v, str):
            try:
                float(v)
                kinds.add("number")
            except ValueError:
                kinds.add("string")
        elif v is None:
            kinds.add("null")
    if kinds == {"boolean"}:
        return "boolean"
    if kinds == {"string"}:
        return "string"
    if "number" in kinds:
        return ["number", "string"]  # amounts may be written as "1.5" or 1.5
    return sorted(kinds) if len(kinds) > 1 else (kinds.pop() if kinds else "string")


def infer(values, file, name="", parent=""):
    objs = [v for v in values if isinstance(v, dict)]
    arrs = [v for v in values if isinstance(v, list)]
    scalars = [v for v in values if not isinstance(v, (dict, list))]
    if objs and not arrs and not scalars:
        if is_map(name, file, parent == "terrain"):
            inner = [x for o in objs for x in o.values()]
            return {"type": "object", "additionalProperties": infer(inner, file) if inner else {}}
        props = {}
        for o in objs:
            for k in o:
                props.setdefault(k, []).append(o[k])
        return {"type": "object",
                "properties": {k: ({"type": "string"} if k in ALWAYS else infer(v, file, k, name)) for k, v in props.items()},
                "additionalProperties": False}
    if arrs and not objs and not scalars:
        inner = [x for a in arrs for x in a]
        return {"type": "array", "items": infer(inner, file, name, parent) if inner else {}}
    if scalars and not objs and not arrs:
        return {"type": scalar_type(scalars)}
    return {}  # mixed kinds: anything


def main():
    args = [a for a in sys.argv[1:] if a != "--force"]
    force = "--force" in sys.argv
    data, out = args[0], args[1]
    files = args[2:] or sorted(f for f in os.listdir(data) if f.endswith(".json"))
    os.makedirs(out, exist_ok=True)
    for f in files:
        target = os.path.join(out, f.replace(".json", ".schema.json"))
        if os.path.exists(target) and not force:
            print(f"skip {target} (exists)")
            continue
        with open(os.path.join(data, f), encoding="utf-8") as h:
            doc = json.load(h)
        schema = {"$schema": "https://json-schema.org/draft/2020-12/schema", "title": f}
        schema.update(infer([doc], f))
        with open(target, "w", encoding="utf-8") as h:
            json.dump(schema, h, indent=2, ensure_ascii=False)
            h.write("\n")
        print(f"wrote {target}")


if __name__ == "__main__":
    main()
