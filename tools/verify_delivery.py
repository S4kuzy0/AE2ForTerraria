"""Read-only checks of installed package, enabled mods and all backed-up world/player files.
Writes evidence only beneath this repository's validation directory.
"""
from pathlib import Path
from zipfile import ZipFile
import argparse, hashlib, json, re, difflib, shutil, os
from datetime import datetime

root = Path(__file__).resolve().parents[1]
source = root / 'AE2ForTerraria'
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--tml-install', default=os.environ.get('TML_INSTALL'), required=not os.environ.get('TML_INSTALL'))
parser.add_argument('--save-root', type=Path, default=Path.home() / 'Documents/My Games/Terraria/tModLoader')
parser.add_argument('--save-backup', type=Path, required=True)
parser.add_argument('--enabled-before', type=Path, required=True)
parser.add_argument('--source-backup', type=Path, required=True)
args = parser.parse_args()
saved = args.save_root.resolve()
logs = Path(args.tml_install).resolve() / 'tModLoader-Logs'
out = root / 'validation'
out.mkdir(exist_ok=True)

def sha(data):
    return hashlib.sha256(data).hexdigest()

world_backup = args.save_backup.resolve()
with ZipFile(world_backup) as archive:
    names = {i.filename for i in archive.infolist() if not i.is_dir()}
    absent, changed = [], []
    for name in sorted(names):
        file = saved / name
        if not file.exists():
            absent.append(name)
        elif sha(file.read_bytes()) != sha(archive.read(name)):
            changed.append(name)
    current = {p.relative_to(saved).as_posix() for folder in ('Worlds', 'Players')
               for p in (saved / folder).rglob('*') if p.is_file()}

enabled = json.loads((saved / 'Mods/enabled.json').read_text(encoding='utf-8-sig'))
before_enabled = json.loads(args.enabled_before.read_text(encoding='utf-8-sig'))
package = saved / 'Mods/AE2ForTerraria.tmod'
version = re.search(r'^version\s*=\s*(.+)$', (source / 'build.txt').read_text(), re.M).group(1)
result = {
    'version': version, 'collectedAt': datetime.now().isoformat(),
    'installed': {'path': str(package), 'bytes': package.stat().st_size,
                  'modified': datetime.fromtimestamp(package.stat().st_mtime).isoformat(),
                  'sha256': sha(package.read_bytes())},
    'enabledMods': {'count': len(enabled), 'unchanged': enabled == before_enabled, 'names': enabled},
    'saveIntegrity': {'backup': str(world_backup), 'verifiedFiles': len(names), 'missing': absent,
                      'changed': changed, 'added': sorted(current - names)},
    'runs': {},
}
for kind in ('client', 'server'):
    text = (logs / (kind + '.log')).read_text(encoding='utf-8-sig')
    shutil.copy2(logs / (kind + '.log'), out / (kind + '-0.22.1.log'))
    count = re.findall(r'AE2 REGRESSION COMPLETE: (\d+) passed', text)
    # Match the actual content-loading stage in either locale, excluding tML itself.
    loaded = sorted(set(re.findall(
        r'\[tML\]: (?:自动加载中：|Adding Content: )([^\s]+) \(', text)) - {'ModLoader'})
    result['runs'][kind] = {
        'coreChecksPassed': int(count[-1]) if count else 0,
        'loadedMods': len(loaded),
        'loadedModNames': loaded,
        'loadedSourceVersion': bool(re.search(
            r'\[tML\]: (?:自动加载中：|Adding Content: )AE2ForTerraria \([^\r\n]+\) v'
            + re.escape(version) + r'\s*$', text, re.M)),
        'regressionFailed': 'AE2 REGRESSION FAILED' in text,
        'uiRendered': len(re.findall(r'AE2 UI RENDERED:', text)),
        'uiFailed': 'AE2 UI REGRESSION FAILED' in text,
        'loadComplete': 'Mod Load Completed' in text,
    }

(out / 'ui').mkdir(exist_ok=True)
for picture in (saved / 'AE2-Validation').glob('*.png'):
    shutil.copy2(picture, out / 'ui' / picture.name)
result['uiImages'] = sorted(p.name for p in (out / 'ui').glob('*.png'))

patch, changes = [], []
with ZipFile(args.source_backup) as archive:
    old = {p: archive.read(p) for p in archive.namelist()
           if p.endswith(('.cs', '.hjson', '.txt', '.csproj'))}
    new = {'AE2ForTerraria/' + p.relative_to(source).as_posix(): p.read_bytes()
           for p in source.rglob('*') if p.is_file() and p.suffix in ('.cs', '.hjson', '.txt', '.csproj')
           and not {'obj', 'bin'}.intersection(p.relative_to(source).parts)}
    for name in sorted(old.keys() | new.keys()):
        before, after = old.get(name, b''), new.get(name, b'')
        a, b = before.decode('utf-8-sig').replace('\r\n', '\n'), after.decode('utf-8-sig').replace('\r\n', '\n')
        if a == b:
            continue
        changes.append({'path': name, 'kind': 'added' if name not in old else 'removed' if name not in new else 'modified'})
        patch.extend(difflib.unified_diff(a.splitlines(True), b.splitlines(True), fromfile='before/' + name, tofile='after/' + name))
(out / 'source-changes.patch').write_text(''.join(patch), encoding='utf-8')
result['sourceChanges'] = changes
(out / 'delivery.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps({key: result[key] for key in ('version', 'installed', 'saveIntegrity', 'runs')}, ensure_ascii=True, indent=2))
if absent or changed or current - names or enabled != before_enabled:
    raise SystemExit('Integrity comparison needs review; no save files have been changed by this script.')
if any(run['regressionFailed'] or run['uiFailed'] or not run['loadComplete'] for run in result['runs'].values()):
    raise SystemExit('A validation run needs review.')
if any(not run['loadedSourceVersion'] or not run['loadedMods'] or run['coreChecksPassed'] != 47
       for run in result['runs'].values()):
    raise SystemExit('Version, loaded-mod or core-regression evidence is incomplete.')
if set(result['runs']['client']['loadedModNames']) != set(enabled):
    raise SystemExit('Client loaded mods differ from the enabled list.')
if result['runs']['client']['uiRendered'] != 24:
    raise SystemExit('UI rendering evidence is incomplete.')
