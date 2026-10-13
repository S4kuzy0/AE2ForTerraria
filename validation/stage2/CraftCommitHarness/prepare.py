import hashlib
import pathlib
import re
import sys

path = pathlib.Path(sys.argv[1]).resolve()
source = path.read_text(encoding='utf-8-sig')
methods = []
for name in ['ProduceOne', 'ActiveJobIsCurrent', 'JobHasUsePermission', 'SetPermissionDenied', 'SameStorageSources']:
    match = re.search(r'(?m)^\s*(?:private|public) (?:static )?[^\n]+\b' + name + r'\(', source)
    if not match:
        if name == 'SameStorageSources':
            continue
        raise RuntimeError(name + ' not found')
    start = match.start()
    first_brace = source.index('{', start)
    arrow = source.find('=>', start, first_brace)
    if arrow >= 0:
        end = source.index(';', arrow) + 1
    else:
        depth = 1
        end = first_brace + 1
        # These source methods have balanced braces in comments/strings as well.
        while depth:
            if source[end] == '{': depth += 1
            elif source[end] == '}': depth -= 1
            end += 1
    methods.append(source[start:end])
header = pathlib.Path(__file__).with_name('Header.txt').read_text(encoding='utf-8')
pathlib.Path(__file__).with_name('SourceAssembler.cs').write_text(header + '\n'.join(methods) + '\n}\n', encoding='utf-8')
print('SOURCE', str(path))
print('SHA256', hashlib.sha256(path.read_bytes()).hexdigest())
print('Methods copied unchanged:', ', '.join(['ProduceOne', 'ActiveJobIsCurrent', 'JobHasUsePermission', 'SetPermissionDenied', 'SameStorageSources (when present)']))
