"""Builds Assets/Localization/strings.<lang>.json from keys.txt + <lang>.txt (one line per key, same order).

Checks: equal line counts, every key is a real UI string (found in XAML {l:L '...'} or C# Loc.T("...")),
and each translation keeps the same {0}/{1} placeholders. Run: python build.py
"""
import glob, html, json, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.normpath(os.path.join(HERE, '..', '..', '..'))  # src/XNotch
DYNAMIC = {"Focus", "Break", "Good", "Moderate", "Sensitive", "Unhealthy", "Very unhealthy", "Hazardous"}


def ui_strings():
    found = set(DYNAMIC)
    for f in glob.glob(os.path.join(SRC, 'Views', '**', '*.xaml'), recursive=True):
        for m in re.finditer(r"\{l:L '((?:[^'\\]|\\.)*)'\}", open(f, encoding='utf-8').read()):
            found.add(html.unescape(m.group(1).replace("\\'", "'").replace("\\\\", "\\")))
    for f in glob.glob(os.path.join(SRC, '**', '*.cs'), recursive=True):
        if os.sep + 'obj' + os.sep in f or os.sep + 'bin' + os.sep in f:
            continue
        for m in re.finditer(r'Loc\.T\("((?:[^"\\]|\\.)*)"', open(f, encoding='utf-8').read()):
            found.add(m.group(1))
    return found


def placeholders(s):
    return sorted(re.findall(r'\{\d+\}', s))


def main():
    keys = open(os.path.join(HERE, 'keys.txt'), encoding='utf-8').read().splitlines()
    ui = ui_strings()
    missing = [k for k in keys if k not in ui]
    if missing:
        sys.exit(f"keys not used in the UI: {missing}")
    for path in sorted(glob.glob(os.path.join(HERE, '*.txt'))):
        lang = os.path.splitext(os.path.basename(path))[0]
        if lang == 'keys':
            continue
        lines = open(path, encoding='utf-8').read().splitlines()
        if len(lines) != len(keys):
            sys.exit(f"{lang}: {len(lines)} lines, expected {len(keys)}")
        table = {}
        for k, v in zip(keys, lines):
            if placeholders(k) != placeholders(v):
                sys.exit(f"{lang}: placeholder mismatch in {k!r} -> {v!r}")
            table[k] = v
        out = os.path.join(HERE, '..', f'strings.{lang}.json')
        json.dump(table, open(out, 'w', encoding='utf-8'), ensure_ascii=False, indent=1, sort_keys=True)
        print(f"{lang}: {len(table)} strings")


if __name__ == '__main__':
    main()
