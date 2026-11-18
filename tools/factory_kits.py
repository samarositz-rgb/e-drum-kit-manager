# Builds E-Drum Kit Manager kits from EZdrummer 3's factory kits (one per drum-set number in the core rooms).
import json, re, sqlite3, sys, uuid

BASE = 'C:/Program Files (x86)/Common Files/Toontrack/EZDrummer/'
ROOMS = {'MainRoom': 'Main Room', 'BrightRoom': 'Bright Room', 'TightRoom': 'Tight Room'}
# slot index -> (accepted types by id prefix, Main Room position)
SLOTS = [('KD', 'Kick'), ('SD', 'Snare'), ('HA', 'Hats'), ('TO', 'Racktom1'), ('TO', 'Racktom2'), ('TO', 'Racktom3'),
         ('TO', 'Floortom1'), ('TO', 'Floortom2'), ('RI', 'Ride4'), ('CR', 'Crash1'), ('CR', 'Crash2'), ('CR', 'Crash5'),
         ('XC', 'Crash6')]
EXTRA = ('CR', 'SP', 'CH')
TOMTYPE = {'Racktom1': 'R', 'Racktom2': 'R', 'Racktom3': 'R', 'Floortom1': 'F', 'Floortom2': 'F'}

kits = []
for room, roomName in ROOMS.items():
    lib = 'EZX3_EZdrummer3_' + room
    c = sqlite3.connect('file:' + BASE + lib + '/soundDB?immutable=1', uri=True)
    drums = [dict(pos=p, id=i, name=n) for p, i, n in c.execute(
        'select p.Name, d.Name, e.Name from SOUNDENTITY e join SDXPOSITIONS p on p.ID=e.SDXPositionID '
        'join SDXDRUMS d on d.ID=e.SDXDrumID order by d.Name')]
    for d in drums:
        d['code'], d['set'] = d['id'][:2], d['id'][2:4]
        d['tom'] = 'F' if d['pos'].startswith('Floor') else 'R' if d['pos'].startswith('Rack') else ''
    # Factory kits: each kick whose number ends the set (KD01, KD02...) and has toms of the same number.
    for kick in [d for d in drums if d['code'] == 'KD' and d['set'][0] == '0']:
        n = kick['set']
        cymSets = sorted({d['set'] for d in drums if d['code'] == 'HA'})
        cym = n if n in cymSets else cymSets[0]
        used, slots = set(), []
        for code, pos in SLOTS:
            codes = EXTRA if code == 'XC' else (code,)
            cands = [d for d in drums if d['code'] in codes and d['id'] not in used
                     and (code != 'TO' or d['tom'] == TOMTYPE[pos])]
            want = cym if code in ('HA', 'RI', 'CR', 'XC') else n
            if code in ('CR', 'XC'):  # cymbals: keep EZdrummer's position first, so crash 1 stays crash 1
                cands.sort(key=lambda d: (d['pos'] != pos, d['set'] != want, d['id']))
            else:
                cands.sort(key=lambda d: (d['set'] != want, d['pos'] != pos, d['set'][0] != '0', d['id']))
            if not cands:  # e.g. no second floor tom: reuse the closest drum of the same kind
                cands = sorted([d for d in drums if d['code'] in codes and (code != 'TO' or d['tom'] == TOMTYPE[pos])],
                               key=lambda d: (d['set'] != want, d['id']))
            d = cands[0]
            used.add(d['id'])
            slots.append({'Lib': lib, 'Drum': d['id']})
        name = re.sub(r'^[\d.x"]+ ', '', kick['name'])
        name = re.sub(r' - .*$', '', name)
        kits.append({'Id': uuid.uuid4().hex, 'Name': f'{name} ({roomName})', 'Source': 'EZdrummer factory kit',
                     'Td12KitNumber': 0, 'Slots': slots, 'RoomAmount': 100, 'OneShot': '', 'OneShotPad': 'Aux 2',
                     'OneShotLevel': 100, 'OverheadsAmount': 100})
        print(kits[-1]['Name'], [s['Drum'] for s in slots])

with open(sys.argv[1], 'w', encoding='utf-8') as f:
    json.dump(kits, f, indent=2)
