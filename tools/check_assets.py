from pathlib import Path
import re,json
root=Path(__file__).resolve().parents[1]/'AE2ForTerraria'
classes={};skipped=[]
for f in root.rglob('*.cs'):
    text=f.read_text(encoding='utf-8-sig')
    pattern=r'(?m)^[ \t]*((?:\[[^\]\r\n]+\]\s*)*)((?:(?:public|private|internal|protected|sealed|abstract|partial|static)\s+)*)class\s+(\w+)\s*:\s*(\w+)'
    for m in re.finditer(pattern,text):
        disabled=bool(re.search(r'Autoload\s*\(\s*false\s*\)',m[1]))
        classes[m[3]]=(m[4],f,'abstract' in m[2],text,disabled)
def kind(name,visited=None):
    if name in ('ModItem','ModTile'): return name
    if name not in classes: return None
    visited=set() if visited is None else visited
    if name in visited: return None
    visited.add(name)
    return kind(classes[name][0],visited)
checked=[];missing=[]
for name,(base,f,abstract,text,disabled) in classes.items():
    if abstract or not kind(name): continue
    if disabled: skipped.append(name);continue
    if re.search(r'override\s+string\s+Texture',text):
        checked.append({'class':name,'override':True});continue
    image=f.with_name(name+'.png')
    checked.append({'class':name,'path':str(image.relative_to(root)),'exists':image.exists()})
    if not image.exists(): missing.append(str(image))
report={'checked':len(checked),'missing':missing,'autoloadDisabled':skipped,'assets':checked}
out=root.parent/'validation';out.mkdir(exist_ok=True)
(out/'asset-check.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'checked':len(checked),'missing':missing,'autoloadDisabled':skipped},ensure_ascii=False))
raise SystemExit(bool(missing))
