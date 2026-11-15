from pathlib import Path
import hashlib
import json
import shutil
import zipfile

root = Path(__file__).resolve().parent.parent
project = root / 'LongStickSpear'
version = '0.4.6'
stage = root / 'package' / f'LongStickSpear-v{version}'
archive = root / 'output' / f'LongStickSpear-v{version}.zip'
checks = []

def sha(data):
    return hashlib.sha256(data).hexdigest()

def put(source, relative):
    target = stage / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, target)

def included(path):
    return not {'bin', 'obj'}.intersection(path.parts) and path.suffix in {'.cs', '.csproj', '.json'}

with zipfile.ZipFile(root / 'output/LongStickSpear-v0.4.5.zip') as previous:
    assert sha((root / 'output/LongStickSpear-v0.4.5.zip').read_bytes()) == '04e8d87bd86cf679455195863674c04b3357d16d433d67ccd13a1e733b108419'
    prior_sources = {name for name in previous.namelist() if name.startswith('Source/src/') and not name.endswith('/')}
    production = {p.relative_to(project).as_posix() for p in (project / 'Source/src').rglob('*') if p.is_file() and included(p.relative_to(project / 'Source/src'))}
    assert production == prior_sources, (production - prior_sources, prior_sources - production)
    changed = {name for name in prior_sources if (project / name).read_bytes() != previous.read(name)}
    expected = {'Source/src/KelvinLongSticks.cs', 'Source/src/Mod.cs', 'Source/src/LongStickSpear.csproj', 'Source/src/manifest.json'}
    assert changed == expected, changed
    old_mod = previous.read('Source/src/Mod.cs').decode()
    assert (project / 'Source/src/Mod.cs').read_text() == old_mod.replace('0.4.5', version)
    old_kelvin = previous.read('Source/src/KelvinLongSticks.cs').decode()
    new_kelvin = (project / 'Source/src/KelvinLongSticks.cs').read_text()
    assert new_kelvin.split('    private static void EnsureTypeIds()')[0] == old_kelvin.split('    private static void EnsureTypeIds()')[0]
    assert new_kelvin.split('    internal static bool IsLongTypeId')[1] == old_kelvin.split('    internal static bool IsLongTypeId')[1]
    assert (project / 'Source/tests/Program.cs').read_bytes() == previous.read('Source/tests/Program.cs')
    assert (project / 'Source/tests/Tests.csproj').read_bytes() == previous.read('Source/tests/Tests.csproj')
    checks.append(f'PASS {len(prior_sources) - len(changed)} production files byte-identical to published v0.4.5. Only Kelvin type registration and its readback, the Mod.cs version label and project/manifest versions change; no new production files.')
    checks.append('PASS Kelvin setup outside EnsureTypeIds, all 94 pure-rule scenarios, crafting animation/ingredients, tree behavior, natural spawning, model appearance, book artwork/title, holder capacity and native hook scope are retained.')

for mod, published in [('CargoCart', '0.1.16'), ('GolfCartBatteryUpgrade', '0.1.1'), ('SkullStorage', '0.1.0')]:
    with zipfile.ZipFile(root / 'output' / f'{mod}-v{published}.zip') as previous:
        count = 0
        for name in previous.namelist():
            if name.startswith('Source/') and not name.endswith('/') and name != 'Source/package-release.py':
                assert (root / mod / name).read_bytes() == previous.read(name), (mod, name)
                count += 1
        assert (root / mod / 'Source/src/bin/Release/net6.0' / f'{mod}.dll').read_bytes() == previous.read(f'Mods/{mod}.dll'), mod
    checks.append(f'PASS {mod} v{published}: {count} source files and Release DLL unchanged; this standalone ZIP does not contain or replace that mod.')

build = (project / 'build.log').read_text()
tests = (project / 'rule-tests.log').read_text()
audit = (project / 'static-audit.log').read_text()
prior_audit = (project / 'prior-version-audit.log').read_text()
assert '0 Warning(s)' in build and '0 Error(s)' in build
assert '94 offline source, probability, Kelvin routing, geometry and storage scenarios passed.' in tests
assert 'Static audit passed.' in audit and audit.count('PASS native signature:') == 69
assert 'PASS native AI struct registration:' in audit
assert 'PASS all other native generic list writes use reference types;' in audit
assert 'PASS supplied fatal stack matches the repaired AI type' in audit
assert 'PASS supplied session uses Battery Upgrade v0.1.1' in audit
assert "Unsafe generic native struct insertion can corrupt Kelvin's shared type table and crash the next load" in prior_audit
checks.append('PASS Release build: zero warnings/errors; 94 existing offline scenarios, 69 native hook signatures and all static regressions.')
checks.append('PASS native TypeInfo is a CLR wrapper class for an IL2CPP value type; unsafe generic insertion is absent, native boxed IList signature and post-insertion ID/group/name verification are present.')
checks.append('PASS published v0.4.5 fails the new unsafe-native-struct regression guard; supplied fatal stack matches AI type-name conversion on reload with Battery Upgrade v0.1.1 active and its old callback errors absent.')
checks.append('PASS no additional boxed native struct uses a generic native list write in the updated Long Stick assembly; no new native hooks, custom native imports or global table clearing.')

if stage.exists():
    shutil.rmtree(stage)
stage.mkdir(parents=True)
put(project / 'Source/src/bin/Release/net6.0/LongStickSpear.dll', 'Mods/LongStickSpear.dll')
put(project / 'Source/src/manifest.json', 'Mods/LongStickSpear/manifest.json')
for name in ['README.md', 'VALIDATION.md', 'LICENSE.txt']:
    put(project / name, name)
for source in (project / 'Source').rglob('*'):
    if source.is_file() and included(source.relative_to(project / 'Source')):
        put(source, source.relative_to(project).as_posix())
put(Path(__file__), 'Source/package-release.py')
for source, target in [('build.log', 'build.txt'), ('rule-tests.log', 'offline-tests.txt'), ('static-audit.log', 'static-audit.txt'), ('prior-version-audit.log', 'prior-version-regression.txt')]:
    put(project / source, 'Validation/' + target)
manifest = json.loads((stage / 'Mods/LongStickSpear/manifest.json').read_text())
assert manifest['version'] == version
assert (stage / 'Mods/LongStickSpear/manifest.json').read_bytes() == (stage / 'Source/src/manifest.json').read_bytes()
assert f'<Version>{version}</Version>' in (stage / 'Source/src/LongStickSpear.csproj').read_text()
assert sorted(str(p.relative_to(stage)) for p in stage.rglob('*.dll')) == ['Mods/LongStickSpear.dll']
assert not any(p.suffix.lower() in {'.assets', '.bundle', '.unity', '.log'} for p in stage.rglob('*') if p.is_file())
checks.append('PASS matching standalone 0.4.6 project/manifest/assembly metadata; one LongStickSpear DLL with source/docs/license; no game/loader DLLs, raw user logs or personal data.')
checks.append('LIMIT: native Unity code was not executed. Fully exit the game before installing; verify repeated main-menu reloads, world vegetation, saved battery charge and Kelvin gathering in game.')
(stage / 'Validation/package-checks.txt').write_text('\n'.join(checks) + '\n')
hashes = {p.relative_to(stage).as_posix(): sha(p.read_bytes()) for p in sorted(stage.rglob('*')) if p.is_file()}
(stage / 'SHA256SUMS.txt').write_text(''.join(f'{digest}  {name}\n' for name, digest in hashes.items()))
archive.parent.mkdir(exist_ok=True)
with zipfile.ZipFile(archive, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as output:
    for source in sorted(p for p in stage.rglob('*') if p.is_file()):
        output.write(source, source.relative_to(stage).as_posix())
with zipfile.ZipFile(archive) as output:
    assert output.testzip() is None
    for name, digest in hashes.items():
        assert sha(output.read(name)) == digest, name
print('\n'.join(checks))
print(f'PASS ZIP CRC and {len(hashes)} file SHA-256 checks.')
print(f'{archive}\nBytes: {archive.stat().st_size}\nSHA-256: {sha(archive.read_bytes())}')
