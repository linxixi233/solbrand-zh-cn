"""Read-only comparison of two original game PCK archives."""
from pathlib import Path
import sys, json
from pck import Pack

old, new = (Pack(p) for p in sys.argv[1:3])
output = Path(sys.argv[3])
output.mkdir(parents=True, exist_ok=True)
changed = [n for n in new.entries if n in old.entries and new.entries[n]['md5'] != old.entries[n]['md5']]
added = sorted(new.entries.keys() - old.entries.keys())
removed = sorted(old.entries.keys() - new.entries.keys())
report = {'oldSize':old.path.stat().st_size,'newSize':new.path.stat().st_size,
          'oldFiles':len(old.entries),'newFiles':len(new.entries),'changed':changed,'added':added,'removed':removed}
(output/'pack-diff.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
story = '.godot/imported/Main.ink-e0f455a283e8899f6dee802ddd078da4.res'
for label, pack in [('old',old),('new',new)]:
    for name in ['project.binary',story]:
        (output/(label+'-'+('story.res' if name == story else name))).write_bytes(pack.read(name))
print(json.dumps({**{k:v for k,v in report.items() if k not in ['changed','added','removed']},
                  'changedCount':len(changed),'addedCount':len(added),'removedCount':len(removed),
                  'project':new.entries['project.binary']['md5'],'story':new.entries[story]['md5'],
                  'changedNonImported':[n for n in changed if not n.startswith('.godot/')][:40]},ensure_ascii=False))
