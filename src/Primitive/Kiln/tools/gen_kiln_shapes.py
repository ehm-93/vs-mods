# Kiln shapes v3 generator + validator (bottle kiln with chamfered corners).
# Generates base.json, small.json, large.json into assets/primitivekiln/shapes/block/kiln.
# Validates: strict JSON, <=16 voxels per element axis, uv = [0,0,W,H] per face rule,
# all six faces present, Tier/DoorSeal naming, no overlapping AABBs.
import json, sys, io, re, os

OUT = os.path.join(os.path.dirname(__file__), "..", "assets", "primitivekiln", "shapes", "block", "kiln")

BRICK = "#brick"
BRICK2 = "#brick2"

def el(name, frm, to, tex=BRICK, face_tex=None):
    dx = to[0]-frm[0]; dy = to[1]-frm[1]; dz = to[2]-frm[2]
    assert dx > 0 and dy > 0 and dz > 0, (name, frm, to)
    uv = {
        "north": [0,0,dx,dy], "south": [0,0,dx,dy],
        "east":  [0,0,dz,dy], "west":  [0,0,dz,dy],
        "up":    [0,0,dx,dz], "down":  [0,0,dx,dz],
    }
    faces = {}
    for f in ["north","east","south","west","up","down"]:
        t = tex
        if face_tex and f in face_tex:
            t = face_tex[f]
        faces[f] = {"texture": t, "uv": uv[f]}
    return {"name": name, "from": list(frm), "to": list(to), "faces": faces}

def chamfered_ring(prefix, lo, hi, y0, y1, tex=BRICK, north_override=None, max_len=16):
    """Octagonal-reading wall ring, thickness 2. Straight N/S walls span x lo+4..hi-4,
    W/E walls span z lo+5..hi-5. Each corner bridged by two stepped 2x2-plan pieces:
    C1 inset 1 from the N/S outer face, C2 inset 3 (flush against W/E wall end).
    All contacts are face contacts; no volume overlaps, no axis-ray pinholes."""
    els = []
    def split_x(name, xa, xb, za, zb):
        pieces = []
        total = xb - xa
        nchunks = 1
        while total / nchunks > max_len:
            nchunks += 1
        step = total / nchunks
        for i in range(nchunks):
            x0 = int(xa + step*i); x1 = int(xa + step*(i+1))
            suffix = "" if nchunks == 1 else chr(ord("A")+i)
            pieces.append(el(prefix+name+suffix, (x0,y0,za),(x1,y1,zb), tex))
        return pieces
    def split_z(name, xa, xb, za, zb):
        pieces = []
        total = zb - za
        nchunks = 1
        while total / nchunks > max_len:
            nchunks += 1
        step = total / nchunks
        for i in range(nchunks):
            z0 = int(za + step*i); z1 = int(za + step*(i+1))
            suffix = "" if nchunks == 1 else chr(ord("A")+i)
            pieces.append(el(prefix+name+suffix, (xa,y0,z0),(xb,y1,z1), tex))
        return pieces

    if north_override is None:
        els += split_x("WallNorth", lo+4, hi-4, lo, lo+2)
    else:
        els += north_override
    els += split_x("WallSouth", lo+4, hi-4, hi-2, hi)
    els += split_z("WallWest", lo, lo+2, lo+5, hi-5)
    els += split_z("WallEast", hi-2, hi, lo+5, hi-5)
    els.append(el(prefix+"CornerNW1", (lo+2,y0,lo+1),(lo+4,y1,lo+3), tex))
    els.append(el(prefix+"CornerNW2", (lo+1,y0,lo+3),(lo+3,y1,lo+5), tex))
    els.append(el(prefix+"CornerNE1", (hi-4,y0,lo+1),(hi-2,y1,lo+3), tex))
    els.append(el(prefix+"CornerNE2", (hi-3,y0,lo+3),(hi-1,y1,lo+5), tex))
    els.append(el(prefix+"CornerSW1", (lo+2,y0,hi-3),(lo+4,y1,hi-1), tex))
    els.append(el(prefix+"CornerSW2", (lo+1,y0,hi-5),(lo+3,y1,hi-3), tex))
    els.append(el(prefix+"CornerSE1", (hi-4,y0,hi-3),(hi-2,y1,hi-1), tex))
    els.append(el(prefix+"CornerSE2", (hi-3,y0,hi-5),(hi-1,y1,hi-3), tex))
    return els

def pinwheel(prefix, lo, hi, ilo, ihi, y0, y1, tex=BRICK, face_tex=None):
    """Square ring (outer lo..hi, inner hole ilo..ihi) tiled with 4 non-overlapping slabs."""
    return [
        el(prefix+"N", (lo, y0, lo ),(ihi, y1, ilo), tex, face_tex),
        el(prefix+"E", (ihi,y0, lo ),(hi,  y1, ihi), tex, face_tex),
        el(prefix+"S", (ilo,y0, ihi),(hi,  y1, hi ), tex, face_tex),
        el(prefix+"W", (lo, y0, ilo),(ilo, y1, hi ), tex, face_tex),
    ]

def shape(elements):
    return {
        "textureWidth": 16,
        "textureHeight": 16,
        "textures": {
            "brick": "game:block/clay/brick/four/running/fire1",
            "brick2": "game:block/clay/brick/four/running/fire3"
        },
        "elements": elements
    }

# ---------------------------------------------------------------- base.json
base = shape([ el("Floor", (0,0,0),(16,4,16)) ])

# --------------------------------------------------------------- small.json
S = []
# Tier1: combustion chamber y4..18, ring -2..18, stoke arch north x4..12 y4..14 (open)
t1_north = [
    el("Tier1-WallNorthLeft",  ( 2, 4,-2),( 4,18, 0)),
    el("Tier1-WallNorthRight", (12, 4,-2),(14,18, 0)),
    el("Tier1-ArchLintel",     ( 4,14,-2),(12,18, 0)),
]
S += chamfered_ring("Tier1-", -2, 18, 4, 18, north_override=t1_north)
# stoke arch inset lining: 1-voxel-deep reveal at z 0..1 framing the opening
S.append(el("Tier1-ArchJambWest",  ( 4, 4, 0),( 5,14, 1)))
S.append(el("Tier1-ArchJambEast",  (11, 4, 0),(12,14, 1)))
S.append(el("Tier1-ArchLiningTop", ( 5,13, 0),(11,14, 1)))
# firing floor y14..16 (ware sits at y16), central 4x4 flue gap x6..10 z6..10
S.append(el("Tier1-FloorNorth", ( 2,14, 0),(14,16, 6)))
S.append(el("Tier1-FloorSouth", ( 2,14,10),(14,16,16)))
S.append(el("Tier1-FloorWest",  ( 0,14, 3),( 2,16,13)))
S.append(el("Tier1-FloorEast",  (14,14, 3),(16,16,13)))
S.append(el("Tier1-FloorFlueW", ( 2,14, 6),( 6,16,10)))
S.append(el("Tier1-FloorFlueE", (10,14, 6),(14,16,10)))
# Tier2: ware chamber y18..34, door opening north x3..13 (full tier height)
t2_north = [
    el("Tier2-DoorJambWest", ( 2,18,-2),( 3,34, 0)),
    el("Tier2-DoorJambEast", (13,18,-2),(14,34, 0)),
]
S += chamfered_ring("Tier2-", -2, 18, 18, 34, north_override=t2_north)
# Tier3: shoulder taper y34..44, 3 stepped rings, 6x6 flue (5..11) open through all
S += pinwheel("Tier3-Shoulder1", -1, 17, 5, 11, 34, 38)
S += pinwheel("Tier3-Shoulder2",  1, 15, 5, 11, 38, 41)
S += pinwheel("Tier3-Shoulder3",  3, 13, 5, 11, 41, 44)
# Tier4: chimney neck, tube 4..12 (inner 6..10) y44..54, flared crown 3..13 y54..58
S += pinwheel("Tier4-Neck",  4, 12, 6, 10, 44, 54, tex=BRICK2)
S += pinwheel("Tier4-Crown", 3, 13, 6, 10, 54, 58, tex=BRICK2)
# DoorSeal: fills door opening, recessed 2 voxels behind outer wall face (z=-2)
S.append(el("DoorSeal", (3,18,0),(13,34,2)))
small = shape(S)

# --------------------------------------------------------------- large.json
L = []
# Tier1: combustion chamber y4..18, ring -2..34, stoke arch north x10..22 y4..14 (open)
l1_north = [
    el("Tier1-WallNorthLeft",  ( 2, 4,-2),(10,18, 0)),
    el("Tier1-WallNorthRight", (22, 4,-2),(30,18, 0)),
    el("Tier1-ArchLintel",     (10,14,-2),(22,18, 0)),
]
L += chamfered_ring("Tier1-", -2, 34, 4, 18, north_override=l1_north)
L.append(el("Tier1-ArchJambWest",  (10, 4, 0),(11,14, 1)))
L.append(el("Tier1-ArchJambEast",  (21, 4, 0),(22,14, 1)))
L.append(el("Tier1-ArchLiningTop", (11,13, 0),(21,14, 1)))
# firing floor y14..16, interior 0..32, central 6x6 flue x13..19 z13..19
L.append(el("Tier1-FloorNorthA", ( 2,14, 0),(16,16,13)))
L.append(el("Tier1-FloorNorthB", (16,14, 0),(30,16,13)))
L.append(el("Tier1-FloorSouthA", ( 2,14,19),(16,16,32)))
L.append(el("Tier1-FloorSouthB", (16,14,19),(30,16,32)))
L.append(el("Tier1-FloorWestA",  ( 0,14, 3),( 2,16,16)))
L.append(el("Tier1-FloorWestB",  ( 0,14,16),( 2,16,29)))
L.append(el("Tier1-FloorEastA",  (30,14, 3),(32,16,16)))
L.append(el("Tier1-FloorEastB",  (30,14,16),(32,16,29)))
L.append(el("Tier1-FloorFlueW",  ( 2,14,13),(13,16,19)))
L.append(el("Tier1-FloorFlueE",  (19,14,13),(30,16,19)))
# Tier2: ware chamber lower y18..34, door opening north x9..23 (continues to y38)
l2_north = [
    el("Tier2-DoorJambWest", ( 2,18,-2),( 9,34, 0)),
    el("Tier2-DoorJambEast", (23,18,-2),(30,34, 0)),
]
L += chamfered_ring("Tier2-", -2, 34, 18, 34, north_override=l2_north)
# Tier3: central pillar (brick2) + shelf y32..34 (top faces brick2), door column
# cutout x9..23 z0..10 kept open
L.append(el("Tier3-Pillar", (14,16,14),(18,32,18), tex=BRICK2))
st = {"up": BRICK2}
L.append(el("Tier3-ShelfW1", ( 0,32, 3),( 9,34,16), face_tex=st))
L.append(el("Tier3-ShelfW2", ( 0,32,16),( 9,34,29), face_tex=st))
L.append(el("Tier3-ShelfNW", ( 2,32, 0),( 9,34, 3), face_tex=st))
L.append(el("Tier3-ShelfNE", (23,32, 0),(30,34, 3), face_tex=st))
L.append(el("Tier3-ShelfE1", (23,32, 3),(32,34,16), face_tex=st))
L.append(el("Tier3-ShelfE2", (23,32,16),(32,34,29), face_tex=st))
L.append(el("Tier3-ShelfC1", ( 9,32,10),(23,34,16), face_tex=st))
L.append(el("Tier3-ShelfC2", ( 9,32,16),(23,34,29), face_tex=st))
L.append(el("Tier3-ShelfS1", ( 2,32,29),(16,34,32), face_tex=st))
L.append(el("Tier3-ShelfS2", (16,32,29),(30,34,32), face_tex=st))
# Tier4: upper ware ring y34..44 (door gap continues to y38, lintel above) + shoulder
l4_north = [
    el("Tier4-DoorJambWest", ( 2,34,-2),( 9,44, 0)),
    el("Tier4-DoorJambEast", (23,34,-2),(30,44, 0)),
    el("Tier4-DoorLintel",   ( 9,38,-2),(23,44, 0)),
]
L += chamfered_ring("Tier4-", -2, 34, 34, 44, north_override=l4_north)
# shoulder step 1: ring 0..32, inner 12..20 (8x8 flue), y44..48 - 6 pieces (<=16 each)
L.append(el("Tier4-Shoulder1NA", ( 0,44, 0),(16,48,12)))
L.append(el("Tier4-Shoulder1NB", (16,44, 0),(32,48,12)))
L.append(el("Tier4-Shoulder1SA", ( 0,44,20),(16,48,32)))
L.append(el("Tier4-Shoulder1SB", (16,44,20),(32,48,32)))
L.append(el("Tier4-Shoulder1W",  ( 0,44,12),(12,48,20)))
L.append(el("Tier4-Shoulder1E",  (20,44,12),(32,48,20)))
# shoulder step 2: ring 4..28, inner 12..20, y48..52
L += pinwheel("Tier4-Shoulder2", 4, 28, 12, 20, 48, 52)
# Tier5: chimney neck, tube 10..22 (inner 12..20) y52..66, flared crown 9..23 y66..70
L += pinwheel("Tier5-Neck",  10, 22, 12, 20, 52, 66, tex=BRICK2)
L += pinwheel("Tier5-Crown",  9, 23, 12, 20, 66, 70, tex=BRICK2)
# DoorSeal: x9..23 y18..38 z0..2 (recessed 2), split because y span 20 > 16
L.append(el("DoorSeal", (9,18,0),(23,34,2)))
L.append(el("DoorSeal", (9,34,0),(23,38,2)))
large = shape(L)

# ---------------------------------------------------------------- emitter
def fmt_arr(a):
    return "[ " + ", ".join(str(int(v)) if float(v).is_integer() else str(v) for v in a) + " ]"

def emit(shp):
    out = io.StringIO()
    w = out.write
    w("{\n")
    w('\t"textureWidth": 16,\n')
    w('\t"textureHeight": 16,\n')
    w('\t"textures": {\n')
    w('\t\t"brick": "%s",\n' % shp["textures"]["brick"])
    w('\t\t"brick2": "%s"\n' % shp["textures"]["brick2"])
    w("\t},\n")
    w('\t"elements": [\n')
    for i, e in enumerate(shp["elements"]):
        w("\t\t{\n")
        w('\t\t\t"name": "%s",\n' % e["name"])
        w('\t\t\t"from": %s,\n' % fmt_arr(e["from"]))
        w('\t\t\t"to": %s,\n' % fmt_arr(e["to"]))
        w('\t\t\t"faces": {\n')
        fkeys = ["north","east","south","west","up","down"]
        for j, f in enumerate(fkeys):
            face = e["faces"][f]
            comma = "," if j < len(fkeys)-1 else ""
            w('\t\t\t\t"%s": { "texture": "%s", "uv": %s }%s\n' % (f, face["texture"], fmt_arr(face["uv"]), comma))
        w("\t\t\t}\n")
        w("\t\t}%s\n" % ("," if i < len(shp["elements"])-1 else ""))
    w("\t]\n")
    w("}\n")
    return out.getvalue()

# ---------------------------------------------------------------- validator
def validate(text, allowed_name_re):
    errs = []
    try:
        data = json.loads(text)
    except Exception as ex:
        return ["JSON parse error: %s" % ex]
    if data.get("textureWidth") != 16 or data.get("textureHeight") != 16:
        errs.append("textureWidth/Height not 16")
    tx = data.get("textures", {})
    if tx.get("brick") != "game:block/clay/brick/four/running/fire1":
        errs.append("textures.brick wrong")
    if tx.get("brick2") != "game:block/clay/brick/four/running/fire3":
        errs.append("textures.brick2 wrong")
    boxes = []
    for e in data.get("elements", []):
        name = e.get("name","?")
        if not re.match(allowed_name_re, name):
            errs.append("bad name: %s" % name)
        f, t = e["from"], e["to"]
        dx, dy, dz = t[0]-f[0], t[1]-f[1], t[2]-f[2]
        for d, ax in ((dx,"x"),(dy,"y"),(dz,"z")):
            if d <= 0:
                errs.append("%s: non-positive %s size" % (name, ax))
            if d > 16:
                errs.append("%s: %s size %s > 16" % (name, ax, d))
        faces = e.get("faces", {})
        need = {"north","east","south","west","up","down"}
        if set(faces.keys()) != need:
            errs.append("%s: faces missing/extra: %s" % (name, sorted(faces.keys())))
        expect = {"north":[0,0,dx,dy],"south":[0,0,dx,dy],
                  "east":[0,0,dz,dy],"west":[0,0,dz,dy],
                  "up":[0,0,dx,dz],"down":[0,0,dx,dz]}
        for fn, fd in faces.items():
            if fd.get("uv") != expect.get(fn):
                errs.append("%s: face %s uv %s != expected %s" % (name, fn, fd.get("uv"), expect.get(fn)))
            if fd.get("texture") not in ("#brick","#brick2"):
                errs.append("%s: face %s bad texture %s" % (name, fn, fd.get("texture")))
        boxes.append((name, f, t))
    for i in range(len(boxes)):
        for j in range(i+1, len(boxes)):
            n1, f1, t1 = boxes[i]; n2, f2, t2 = boxes[j]
            if all(f1[k] < t2[k] and f2[k] < t1[k] for k in range(3)):
                errs.append("OVERLAP: %s %s-%s <-> %s %s-%s" % (n1, f1, t1, n2, f2, t2))
    return errs

files = {
    "base.json":  (base,  r"^Floor$"),
    "small.json": (small, r"^(Tier[1-4]-\w+|DoorSeal)$"),
    "large.json": (large, r"^(Tier[1-5]-\w+|DoorSeal)$"),
}

all_ok = True
for fname, (shp, name_re) in files.items():
    text = emit(shp)
    errs = validate(text, name_re)
    if errs:
        all_ok = False
        print("FAIL %s:" % fname)
        for e in errs:
            print("  -", e)
    else:
        with open(os.path.join(OUT, fname), "w", newline="\n") as fh:
            fh.write(text)
        data = json.loads(text)
        counts = {}
        for e in data["elements"]:
            key = e["name"].split("-")[0] if "-" in e["name"] else e["name"]
            counts[key] = counts.get(key, 0) + 1
        print("PASS %s: %d elements, by tier: %s" % (fname, len(data["elements"]), counts))

sys.exit(0 if all_ok else 1)
