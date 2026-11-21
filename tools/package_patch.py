from pathlib import Path
import json
import zipfile

root = Path(__file__).resolve().parent.parent
version = json.loads((root / 'dist/manifest.json').read_text(encoding='utf-8'))['version']
files = [root / name for name in (
    'Prepare-Chinese.ps1', 'Start-Chinese.cmd', '汉化说明.txt', '译文对照.txt', '术语表.txt',
    'Solbrand汉化助手-美化版.exe', '美化版-使用方法.txt', '非商业学习说明.txt',
    'dist/manifest.json', 'dist/solbrand-zh-overlay.pck', 'patch/ZhPatch.gd',
    'tools/build.mjs', 'tools/localize-resources.mjs', 'tools/localize-scenes.mjs', 'tools/inspect-settings.mjs',
)]
files.extend(sorted((root / 'translations').rglob('*.json')))
output = root / 'dist' / f'Solbrand-zh-CN-{version}.zip'
with zipfile.ZipFile(output, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
    for item in files:
        archive.write(item, item.relative_to(root).as_posix())
with zipfile.ZipFile(output) as archive:
    if archive.testzip() is not None:
        raise RuntimeError('Package CRC verification failed')
    if any(n.startswith(('analysis/', 'runtime/')) for n in archive.namelist()):
        raise RuntimeError('Unexpected game or analysis files in package')
print(json.dumps({'path': str(output), 'bytes': output.stat().st_size, 'files': len(files)}))
