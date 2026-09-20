"""Read selected application source in Electron ASAR for integration diagnosis."""
import json, struct, sys
from pathlib import Path

archive = Path(r'G:\ComfyUI\Comfy Desktop\resources\app.asar')
with archive.open('rb') as f:
    sizes = struct.unpack('<4I', f.read(16))
    header = json.loads(f.read(sizes[3]))
    base = 8 + sizes[1]
    def walk(tree, prefix=''):
        for name, entry in tree.get('files', {}).items():
            path = prefix + name
            if 'files' in entry:
                if name != 'node_modules':
                    yield from walk(entry, path + '/')
            else:
                yield path, entry
    entries = dict(walk(header))
    if len(sys.argv) == 1:
        for path in entries:
            if path.endswith(('.js', '.json', '.html')):
                print(path)
    else:
        name = sys.argv[1]
        entry = entries[name]
        f.seek(base + int(entry['offset']))
        content = f.read(entry['size']).decode('utf-8')
        for needle in sys.argv[2:]:
            start = 0
            count = 0
            while count < 35:
                pos = content.find(needle, start)
                if pos < 0:
                    break
                print(needle, ':', content[max(0, pos-180):pos+450])
                start = pos + len(needle)
                count += 1
