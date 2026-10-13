# -*- coding: utf-8 -*-
"""本地化键对齐检查:代码里用到的 UI 键必须在 zh / en 两份 hjson 里都有。"""
import glob
import io
import os
import re
import sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "AE2ForTerraria")
ROOT = os.path.normpath(ROOT)

code_keys = set()
for path in glob.glob(os.path.join(ROOT, "**", "*.cs"), recursive=True):
    text = io.open(path, encoding="utf-8-sig").read()
    code_keys |= set(re.findall(r'ModText\.(?:Get|Format|Safe)\("([A-Za-z0-9_]+)"', text))


def load(path):
    text = io.open(path, encoding="utf-8-sig").read()
    return set(re.findall(r"^\t([A-Za-z0-9_]+):", text, re.M))


zh = load(os.path.join(ROOT, "Localization", "zh-Hans_Mods.AE2ForTerraria.hjson"))
en = load(os.path.join(ROOT, "Localization", "en-US_Mods.AE2ForTerraria.hjson"))

# HelpPageN 是拼出来的;Vanilla_ 之类的下划线结尾键是「前缀 + 运行时名字」,
# 都不是 hjson 里能直接找到的静态键。
dynamic = [k for k in code_keys if k.startswith("HelpPage") or k.endswith("_")]
static = [k for k in code_keys if k not in dynamic]

print("代码引用键: %d(静态 %d,动态 %d)" % (len(code_keys), len(static), len(dynamic)))
print("zh 键 %d / en 键 %d" % (len(zh), len(en)))

missing_zh = sorted(k for k in static if k not in zh)
missing_en = sorted(k for k in static if k not in en)

print("代码有、zh 缺: %s" % (missing_zh if missing_zh else "无"))
print("代码有、en 缺: %s" % (missing_en if missing_en else "无"))
print("zh 多出: %s" % (sorted(zh - en) if zh - en else "无"))
print("en 多出: %s" % (sorted(en - zh) if en - zh else "无"))

sys.exit(1 if (missing_zh or missing_en) else 0)
