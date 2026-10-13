from pathlib import Path
import re,json
root=Path(__file__).resolve().parents[1]/'AE2ForTerraria'
classes={}
for f in root.rglob('*.cs'):
    text=f.read_text(encoding='utf-8-sig')
    for m in re.finditer(r'(public\s+(?:sealed\s+|abstract\s+)?)class\s+(\w+)\s*:\s*(\w+)',text):
        classes[m[2]]=(m[3],f,'abstract' in m[1],text)
def kind(name,visited=None):
    if name in ('ModItem','ModTile'): return name
    if name not in classes: return None
    return kind(classes[name][0])
checked=[];missing=[]
for name,(base,f,abstract,text) in classes.items():
    if abstract or not kind(name): continue
    if re.search(r'override\s+string\s+Texture',text):
        checked.append({'class':name,'override':True});continue
    image=f.with_name(name+'.png')
    checked.append({'class':name,'path':str(image.relative_to(root)),'exists':image.exists()})
    if not image.exists(): missing.append(str(image))
report={'checked':len(checked),'missing':missing,'assets':checked}
out=root.parent/'validation';out.mkdir(exist_ok=True)
(out/'asset-check.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'checked':len(checked),'missing':missing},ensure_ascii=False))
raise SystemExit(bool(missing))
