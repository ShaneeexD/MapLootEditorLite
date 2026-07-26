import json
import sys
from pathlib import Path

LANG_DIR = Path(__file__).parent / "lang"
EN_FILE = LANG_DIR / "en.json"


def main(target="zh-CN.json"):
    target_file = LANG_DIR / target
    if not EN_FILE.is_file() or not target_file.is_file():
        print("Missing lang files.")
        return 1

    with EN_FILE.open("r", encoding="utf-8-sig") as f:
        en = json.load(f)
    with target_file.open("r", encoding="utf-8-sig") as f:
        loc = json.load(f)

    missing = [k for k in en if k not in loc]
    untranslated = [k for k in en if k in loc and loc[k] == en[k] and k != "_name"]
    to_translate = sorted(set(missing + untranslated))

    out_file = LANG_DIR / target.replace(".json", "_missing.json")
    data = {"_name": loc.get("_name", target.replace(".json", ""))}
    for k in to_translate:
        data[k] = en[k]

    with out_file.open("w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)

    print(f"Wrote {len(data) - 1} entries to {out_file}")
    return 0


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "zh-CN.json")
