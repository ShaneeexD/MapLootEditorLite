import json
import os
import gc
from pathlib import Path

base = Path(__file__).parent
locations_dir = base / "locations"

for map_dir in locations_dir.iterdir():
    if not map_dir.is_dir():
        continue

    counts = {}
    files = ["staticContainers.json", "looseLoot.json", "staticAmmo.json"]

    for filename in files:
        file_path = map_dir / filename
        if not file_path.exists():
            continue

        try:
            with open(file_path, "r", encoding="utf-8") as f:
                data = json.load(f)
        except Exception as e:
            print(f"Failed to parse {file_path}: {e}")
            continue

        sources = data.values() if isinstance(data, dict) else (data if isinstance(data, list) else [])
        for source in sources:
            if not isinstance(source, list):
                source = [source]
            for entry in source:
                if not isinstance(entry, dict):
                    continue

                template = entry.get("template", entry)
                if not isinstance(template, dict):
                    continue

                items = template.get("Items", [])
                if not isinstance(items, list):
                    continue

                for item in items:
                    if not isinstance(item, dict):
                        continue
                    tpl = item.get("_tpl")
                    if not tpl:
                        continue
                    upd = item.get("upd") or {}
                    stack = upd.get("StackObjectsCount", 1)
                    if isinstance(stack, int) and stack > 1:
                        if counts.get(tpl, 1) < stack:
                            counts[tpl] = stack

    out_path = map_dir / "itemStackCounts.json"
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(counts, f)

    print(f"{map_dir.name}: wrote {len(counts)} stack counts to {out_path}")
    gc.collect()
