# Riggt die Trellis-Figur (Alex, Ganzkörper): Skelett, Gesichts-Shape-Keys, Animationen, FBX-Export für Unity.
#   blender -b --factory-startup --python alex_trellis_rig.py -- <eingabe.glb> <ausgabe.fbx> <vorschau-ordner>
# Ablauf: Build (Aufbereiten, Skelett, Gewichte, Lider/Mundraum, Shape Keys, Actions, Export) -> frische
# Blender-Sitzung importiert die FBX erneut, listet Mesh/Keys/Knochen/Actions und rendert die Vorschauen.
# Deterministisch: kein Zufall, feste Parameter. Gesichtsmaße stehen in Einheiten der Eingabe (Höhe 1.0)
# und werden mit S in Meter umgerechnet (Figur 1,80 m). Die Augen und der Mund sind in die Textur gemalt;
# ihre Lage folgt aus der frontalen Parallelprojektion der Vorlage und wird an der Textur geprüft.
import bpy, bmesh, sys, os, math, subprocess
import numpy as np
from mathutils import Vector, Matrix, Quaternion
from mathutils.bvhtree import BVHTree

ARGS = sys.argv[sys.argv.index("--") + 1:]
if ARGS and ARGS[0] in ("build", "preview"):
    MODE, ARGS = ARGS[0], ARGS[1:]
else:
    MODE = "alles"
SRC, DST, DOCS = ARGS[0], ARGS[1], ARGS[2]

S = 1.8                # Skalierung Eingabe -> Meter
FPS = 30
RIG, MESH = "Alex_Trellis_Rig", "Alex_Trellis"
TEX = "Alex_Trellis_BaseColor"


def smooth(t):
    t = np.clip(t, 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def reset_import_fbx(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)


# ---------------------------------------------------------------- 1. Aufbereiten
def prep_mesh():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=SRC)
    ob = next(o for o in bpy.data.objects if o.type == 'MESH')
    for o in list(bpy.data.objects):
        if o is not ob: bpy.data.objects.remove(o)
    ob.parent = None; ob.matrix_world = Matrix.Identity(4)
    ob.name = ob.data.name = MESH
    me = ob.data
    bm = bmesh.new(); bm.from_mesh(me)
    n0 = len(bm.verts)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=0.0004)
    bm.to_mesh(me); bm.free()
    for p in me.polygons: p.use_smooth = True
    me.update()
    print(f"[Prep] remove_doubles: {n0} -> {len(me.vertices)} Vertices")
    co = np.empty(len(me.vertices) * 3, np.float32); me.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
    # Ursprung zwischen den Füßen (Mitte der beiden Fußschwerpunkte), Sohle auf z=0
    zmin = float(co[:, 2].min())
    feet = co[co[:, 2] < zmin + 0.04]
    ox = 0.5 * (feet[feet[:, 0] > 0, 0].mean() + feet[feet[:, 0] < 0, 0].mean())
    oy = float(0.5 * (feet[:, 1].min() + feet[:, 1].max()))
    o = np.array([ox, oy, zmin], np.float32)
    co = (co - o) * S
    me.vertices.foreach_set("co", co.ravel()); me.update()
    print(f"[Prep] Ursprung (Eingabe) = {np.round(o, 4)}, Höhe = {co[:, 2].max():.3f} m, x {co[:, 0].min():.3f}..{co[:, 0].max():.3f}")
    return ob, o


def T(p, o):
    """Punkt der Eingabe-Einheiten -> Meter (gleiche Abbildung wie das Mesh)."""
    return Vector(((p[0] - o[0]) * S, (p[1] - o[1]) * S, (p[2] - o[2]) * S))


# ---------------------------------------------------------------- 2. Skelett
def vco(me):
    co = np.empty(len(me.vertices) * 3, np.float32); me.vertices.foreach_get("co", co)
    return co.reshape(-1, 3)


def snap(co, est, radius, dz=0.02):
    """Schnittmittelpunkt: Median der Vertices in einer Scheibe um die Schätzung (xy-Radius)."""
    m = (np.abs(co[:, 2] - est[2]) < dz) & (np.hypot(co[:, 0] - est[0], co[:, 1] - est[1]) < radius)
    if m.sum() < 8: return Vector(est)
    return Vector((float(np.median(co[m, 0])), float(np.median(co[m, 1])), est[2]))


def joints(ob, o):
    """Gelenkpositionen (Meter): Schätzung aus der Figurengeometrie (Eingabe-Einheiten), an Querschnitten nachgezogen."""
    co = vco(ob.data)
    j = {}
    t = lambda x, y, z: T((x, y, z), o)
    # Körpermitte (y) aus dem Medianschnitt des Rumpfes
    def mid_y(z, hw=0.07):
        m = (np.abs(co[:, 2] - (z - o[2]) * S) < 0.03) & (np.abs(co[:, 0]) < hw * S)
        return float(np.median(co[m, 1])) if m.sum() else 0.0
    ycen = {k: mid_y(z) for k, z in (("hips", -0.06), ("spine", 0.02), ("chest", 0.15), ("neck", 0.275))}
    head = (co[:, 2] > (0.36 - o[2]) * S) & (co[:, 2] < (0.46 - o[2]) * S) & (np.abs(co[:, 0]) < 0.06 * S)
    yhead = float(np.median(co[head, 1]))
    j["Hips"] = Vector((0, ycen["hips"], (-0.06 - o[2]) * S))
    j["Spine"] = Vector((0, ycen["spine"], (0.02 - o[2]) * S))
    j["Chest"] = Vector((0, ycen["chest"], (0.15 - o[2]) * S))
    j["Neck"] = Vector((0, ycen["neck"], (0.275 - o[2]) * S))
    j["Head"] = Vector((0, 0.5 * (ycen["neck"] + yhead), (0.315 - o[2]) * S))
    j["HeadTip"] = Vector((0, yhead, (0.5 - o[2]) * S))
    for sd, sg in (("L", 1.0), ("R", -1.0)):
        # Beine: Schätzungen aus den Beinquerschnitten, nachgezogen
        r = 0.045 * S
        j["UpperLeg." + sd] = snap(co, t(sg * 0.060, 0.0, -0.07), r, 0.03)
        j["LowerLeg." + sd] = snap(co, t(sg * 0.075, 0.01, -0.26), r, 0.03)
        j["Foot." + sd] = snap(co, t(sg * 0.083, 0.03, -0.435), 0.035 * S, 0.02)
        shoe = co[(co[:, 2] < 0.12) & (co[:, 0] * sg > 0)]
        ytoe, yheel = float(shoe[:, 1].min()), float(shoe[:, 1].max())
        ln = yheel - ytoe
        j["Foot." + sd].y = yheel - 0.27 * ln
        j["Toes." + sd] = Vector((j["Foot." + sd].x, ytoe + 0.30 * ln, 0.05))
        j["ToesTip." + sd] = Vector((j["Foot." + sd].x, ytoe + 0.01, 0.035))
        # Arme (A-Haltung): Schulter, Ellbogen, Handgelenk, Handspitze
        j["Shoulder." + sd] = Vector((sg * 0.02 * S, ycen["chest"], (0.27 - o[2]) * S))
        j["UpperArm." + sd] = Vector((sg * 0.115 * S, ycen["chest"], (0.27 - o[2]) * S))
        j["LowerArm." + sd] = snap(co, t(sg * 0.158, 0.0, 0.12), 0.032 * S, 0.02)
        j["Hand." + sd] = snap(co, t(sg * 0.196, -0.01, -0.03), 0.028 * S, 0.012)
        j["HandTip." + sd] = Vector((j["Hand." + sd].x + sg * 0.012 * S, j["Hand." + sd].y - 0.006 * S, (-0.115 - o[2]) * S))
    return j


def build_armature(j):
    arm = bpy.data.armatures.new(RIG)
    rig = bpy.data.objects.new(RIG, arm)
    bpy.context.scene.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm.edit_bones
    def bone(name, head, tail, parent=None, connect=False):
        b = eb.new(name); b.head, b.tail = head, tail
        if parent: b.parent = eb[parent]; b.use_connect = connect
        return b
    bone("Hips", j["Hips"], j["Spine"])
    bone("Spine", j["Spine"], j["Chest"], "Hips", True)
    bone("Chest", j["Chest"], j["Neck"], "Spine", True)
    bone("Neck", j["Neck"], j["Head"], "Chest", True)
    bone("Head", j["Head"], j["HeadTip"], "Neck", True)
    for sd in ("L", "R"):
        bone("Shoulder." + sd, j["Shoulder." + sd], j["UpperArm." + sd], "Chest")
        bone("UpperArm." + sd, j["UpperArm." + sd], j["LowerArm." + sd], "Shoulder." + sd, True)
        bone("LowerArm." + sd, j["LowerArm." + sd], j["Hand." + sd], "UpperArm." + sd, True)
        bone("Hand." + sd, j["Hand." + sd], j["HandTip." + sd], "LowerArm." + sd, True)
        bone("UpperLeg." + sd, j["UpperLeg." + sd], j["LowerLeg." + sd], "Hips")
        bone("LowerLeg." + sd, j["LowerLeg." + sd], j["Foot." + sd], "UpperLeg." + sd, True)
        bone("Foot." + sd, j["Foot." + sd], j["Toes." + sd], "LowerLeg." + sd, True)
        bone("Toes." + sd, j["Toes." + sd], j["ToesTip." + sd], "Foot." + sd, True)
    bpy.ops.object.mode_set(mode='OBJECT')
    print("[Rig] Knochen:", len(arm.bones))
    return rig


def bind_auto(ob, rig):
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True); rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    me = ob.data
    vg_names = [g.name for g in ob.vertex_groups]
    print("[Gewichte] Gruppen:", len(vg_names), "/", len(rig.data.bones))
    # Unity: höchstens 4 Einflüsse je Vertex, normalisiert; Vertices ohne Gewicht -> nächster Knochen
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=4)
    bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)
    segs = [(b.name, rig.matrix_world @ b.head_local, rig.matrix_world @ b.tail_local) for b in rig.data.bones]
    miss = 0
    for v in me.vertices:
        if not any(g.weight > 1e-6 for g in v.groups):
            miss += 1
            best = min(segs, key=lambda s: closest_dist(v.co, s[1], s[2]))
            ob.vertex_groups[best[0]].add([v.index], 1.0, 'REPLACE')
    print(f"[Gewichte] Vertices ohne Gewicht (Fallback nächster Knochen): {miss}")


def closest_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
    return (p - (a + ab * t)).length


# ---------------------------------------------------------------- 3. Gesicht
def face_samples(ob, o):
    """Texturfarben dicht über die Frontdreiecke des Kopfes abtasten (Netz gröber als die Textur)."""
    me = ob.data
    img = next(i for i in bpy.data.images if i.name.startswith(TEX))
    w, h = img.size
    px = np.empty(w * h * 4, np.float32); img.pixels.foreach_get(px)
    px = px.reshape(h, w, 4)[:, :, :3]
    co = vco(me)
    uv = np.empty(len(me.loops) * 2, np.float32); me.uv_layers[0].data.foreach_get("uv", uv); uv = uv.reshape(-1, 2)
    me.calc_loop_triangles()
    nt = len(me.loop_triangles)
    tv = np.empty(nt * 3, np.int32); me.loop_triangles.foreach_get("vertices", tv); tv = tv.reshape(-1, 3)
    tl = np.empty(nt * 3, np.int32); me.loop_triangles.foreach_get("loops", tl); tl = tl.reshape(-1, 3)
    zc = (0.33 - o[2]) * S
    sel = (co[tv][:, :, 1].max(1) < (-0.03 - o[1]) * S) & (co[tv][:, :, 2].min(1) > zc)
    tv, tl = tv[sel], tl[sel]
    n = 10
    bw = np.array([(i / n, j / n, 1 - (i + j) / n) for i in range(n + 1) for j in range(n + 1 - i)], np.float32)
    P = np.einsum("sk,tkd->tsd", bw, co[tv]).reshape(-1, 3)
    U = np.einsum("sk,tkd->tsd", bw, uv[tl]).reshape(-1, 2)
    xs = np.clip((U[:, 0] * w).astype(int), 0, w - 1); ys = np.clip((U[:, 1] * h).astype(int), 0, h - 1)
    return P, px[ys, xs], U


def locate_face(P, C, o):
    """Augen: Iris-Blau nahe der projizierten Position der Vorlage; Mund: dunkle Lippenfuge als Parabel."""
    inv = lambda ix, iy: ((ix - 610) * 0.126 / 730, 0.5 - (iy - 15) * 0.170 / 935)   # Bild -> Eingabe x,z
    ex = {}
    for sd, ix in (("L", 765), ("R", 510)):
        px_, pz_ = inv(ix, 490)
        c0 = T((px_, 0, pz_), o)
        m = (C[:, 2] > C[:, 0] + 0.10) & (C[:, 2] > C[:, 1]) & (np.hypot(P[:, 0] - c0.x, P[:, 2] - c0.z) < 0.012 * S)
        c = np.median(P[m], axis=0) if m.sum() > 20 else np.array([c0.x, 0, c0.z])
        ex[sd] = (float(c[0]), float(c[2]))
        print(f"[Auge {sd}] Iris-Mitte x={c[0]:.4f} z={c[2]:.4f} (Projektion x={c0.x:.4f} z={c0.z:.4f}), {int(m.sum())} Punkte")
    mx, mz = inv(640, 755)
    c0 = T((mx, 0, mz), o)
    L = C @ np.array([0.3, 0.55, 0.15], np.float32)
    m = (L < 0.15) & (np.abs(P[:, 2] - c0.z) < 0.006 * S) & (np.abs(P[:, 0] - c0.x) < 0.026 * S)
    x, z = P[m, 0], P[m, 2]
    keep = np.ones(len(x), bool)
    for _ in range(6):
        coef = np.polyfit(x[keep], z[keep], 2)
        res = np.abs(np.polyval(coef, x) - z)
        keep = res < max(0.0012, np.percentile(res[keep], 70) * 1.5)
    xmin, xmax = float(x[keep].min()), float(x[keep].max())
    print(f"[Mund] {int(m.sum())} dunkle Punkte, {int(keep.sum())} Inlier, x={xmin:.4f}..{xmax:.4f}, z(Mitte)={np.polyval(coef, 0.5 * (xmin + xmax)):.4f}")
    return ex, dict(coef=coef, xmin=xmin, xmax=xmax, xm=0.5 * (xmin + xmax), hw=0.5 * (xmax - xmin))


class Surface:
    """Raycast frontal (+Y) auf die Hautoberfläche für eine gegebene Vertexposition."""
    def __init__(self, co, tris):
        self.bvh = BVHTree.FromPolygons([tuple(v) for v in co], [tuple(t) for t in tris])
    def y(self, x, z):
        hit = self.bvh.ray_cast(Vector((x, -3.0, z)), Vector((0, 1, 0)))
        return hit[0].y if hit[0] is not None else None
    def hit(self, x, z):
        h = self.bvh.ray_cast(Vector((x, -3.0, z)), Vector((0, 1, 0)))
        return h


def add_patch(bm, rows_xyz, mat_idx, uvs=None, flip=False):
    """Quad-Gitter aus rows_xyz[r][c]; liefert Vertex-Liste (zeilenweise)."""
    nr, nc = len(rows_xyz), len(rows_xyz[0])
    vs = [[bm.verts.new(p) for p in row] for row in rows_xyz]
    uvl = bm.loops.layers.uv.active
    for r in range(nr - 1):
        for c in range(nc - 1):
            quad = (vs[r][c], vs[r + 1][c], vs[r + 1][c + 1], vs[r][c + 1])
            f = bm.faces.new(quad[::-1] if flip else quad)
            f.material_index = mat_idx; f.smooth = True
            if uvs is not None:
                grid = isinstance(uvs[0][0], (tuple, list)) if isinstance(uvs[0], (tuple, list)) else False
                idx = ((r, c), (r + 1, c), (r + 1, c + 1), (r, c + 1))
                for lp, (rr, cc) in zip(f.loops, idx[::-1] if flip else idx):
                    lp[uvl].uv = uvs[rr][cc] if grid else uvs
    return vs


def build_face(ob, o):
    """Lider und Mundraum als Zusatzgeometrie plus Shape Keys; Patches voll an Head gewichtet."""
    me = ob.data
    P, C, Uall = face_samples(ob, o)
    eyes, mouth = locate_face(P, C, o)
    co0 = vco(me)
    me.calc_loop_triangles()
    tris = np.empty(len(me.loop_triangles) * 3, np.int32); me.loop_triangles.foreach_get("vertices", tris); tris = tris.reshape(-1, 3)
    skin = Surface(co0, tris)
    def front_y(surf, x, z, d=0.0012 * S):
        """Vorderster Hautpunkt in der Umgebung: der Patch taucht auch zwischen den Stützstellen nicht ein."""
        ys_ = [surf.y(x + i * d, z + k * d) for i in (-1, 0, 1) for k in (-1, 0, 1)]
        ys_ = [v for v in ys_ if v is not None]
        return min(ys_) if ys_ else None
    s = S   # Längen der Eingabe -> m
    OFF = 0.0006 * s            # Patch liegt knapp vor der Haut
    coef, xm, hw = mouth["coef"], mouth["xm"], mouth["hw"]
    zl = lambda xx: float(np.polyval(coef, np.clip(xx, mouth["xmin"], mouth["xmax"])))

    # ---- Haut-UV oberhalb des Auges: Lid-Patch bekommt Hautfarbe
    def uv_at(x, z):
        h = skin.hit(x, z)
        if h[0] is None: return (0.5, 0.5)
        f = me.loop_triangles[h[2]]
        a, b, c = [Vector(co0[i]) for i in f.vertices]
        p = h[0]
        v0, v1, v2 = b - a, c - a, p - a
        d00, d01, d11, d20, d21 = v0.dot(v0), v0.dot(v1), v1.dot(v1), v2.dot(v0), v2.dot(v1)
        den = d00 * d11 - d01 * d01
        wb = (d11 * d20 - d01 * d21) / den; wc = (d00 * d21 - d01 * d20) / den; wa = 1 - wb - wc
        U = [Vector(me.uv_layers[0].data[l].uv) for l in f.loops]
        r = U[0] * wa + U[1] * wb + U[2] * wc
        return (r.x, r.y)

    bm = bmesh.new(); bm.from_mesh(me)
    bm.verts.ensure_lookup_table()
    n_skin = len(bm.verts)
    mat_mouth = bpy.data.materials.new("Alex_Mundraum"); mat_mouth.use_nodes = True
    bsdf = mat_mouth.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (0.05, 0.015, 0.015, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.9
    bsdf.inputs["Specular IOR Level"].default_value = 0.0
    me.materials.append(mat_mouth)

    keys = {}          # Name -> {Vertexindex: neue Position}
    pending = []       # (Key, Vertexgitter, Zielpositionen); Indizes erst nach index_update gültig
    # ---- Lider: Mandelform der gemalten Augen; UVs auf die Haut direkt über der Wimpernkante, unten dunkler Wimpernstreifen
    HW = 0.0123 * s
    nc = 13
    skin_rows, sf = 5, 0.14                       # Hautzeilen; Wimpernstreifen = unterste 14 % der Lidhöhe
    fr = [r / (skin_rows - 1) * (1 - sf) for r in range(skin_rows)] + [1 - sf * 0.8, 1.0]
    lum = C @ np.array([0.3, 0.55, 0.15], np.float32)
    for sd, sg in (("L", 1.0), ("R", -1.0)):
        xc, zc = eyes[sd]
        def lines(t):                    # t in [-1,1] von innen nach außen (nasal -> temporal)
            tt = float(np.clip(t, -1, 1)); k = 1 - tt * tt
            base = zc - 0.0023 * s + 0.0006 * s * tt
            return base + 0.0083 * s * k + 0.0002 * s, base - 0.0020 * s * k - 0.0003 * s
        # Wimpernfarbe: dunkelste Texturpunkte der gemalten oberen Wimpernlinie
        win = np.where((np.abs(P[:, 0] - xc) < 0.011 * s) & (P[:, 2] > zc + 0.002 * s) & (P[:, 2] < zc + 0.0085 * s))[0]
        dark = win[np.argsort(lum[win])[:max(5, len(win) // 20)]]
        kd = dark[np.argsort(lum[dark])[len(dark) // 2]]
        uv_lash = (float(Uall[kd, 0]), float(Uall[kd, 1]))
        print(f"[Lid {sd}] Wimpernfarbe {np.round(C[kd], 2)}")
        # Haut direkt über dem Auge (unter der Braue): Abtastpunkt mit der mittleren Farbe des Streifens
        # zwischen Wimper und Braue (Blenden zwischen entfernten UV-Punkten streut über fremde Texturinseln)
        def strip_uv(z0, z1):
            m = np.where((np.abs(P[:, 0] - xc) < 0.007 * s) & (P[:, 2] > zc + z0 * s) & (P[:, 2] < zc + z1 * s) & (lum > 0.5))[0]
            kk = m[np.argmin(np.linalg.norm(C[m] - np.median(C[m], axis=0), axis=1))]
            print(f"[Lid {sd}] Haut {np.round(C[kk], 2)}")
            return np.array([Uall[kk, 0], Uall[kk, 1]])
        uvA = uvB = strip_uv(0.0045, 0.0092)
        closed, folded, uvg = [], [], []
        for r, f in enumerate(fr):
            rc, rf, ru = [], [], []
            lash = r >= skin_rows
            for c in range(nc):
                t = c / (nc - 1) * 2 - 1                    # -1 nasal ... +1 temporal
                x = xc + t * HW * sg
                zu, zlw = lines(t)
                zc_ = zu + (zlw - zu) * f
                y = front_y(skin, x, zc_)
                yu = front_y(skin, x, zu)
                rc.append((x, (y if y is not None else yu) - OFF, zc_))
                rf.append((x, (yu if yu is not None else y) - OFF, zu))
                if lash: ru.append(uv_lash)
                else:
                    uvv = uvA + (uvB - uvA) * (f / (1 - sf)); ru.append((float(uvv[0]), float(uvv[1])))
            closed.append(rc); folded.append(rf); uvg.append(ru)
        vs = add_patch(bm, folded, 0, uvg, flip=sg < 0)   # Windung spiegeln, damit die Normalen nach vorn zeigen
        pending.append(("Blinzeln_" + sd, vs, closed))
    # ---- Mundraum
    ncm, nrm = 21, 4
    xs = np.linspace(xm - 0.62 * hw, xm + 0.62 * hw, ncm)     # Öffnung = 62 % der Lippenbreite
    H = 0.0056 * s          # 1,0 cm Öffnung in der Mitte
    D = 0.0040 * s          # Kieferabsenkung
    def mouth_w(P_):        # Gewicht der Haut-Verformung für Mund_Auf
        x, y, z = P_[:, 0], P_[:, 1], P_[:, 2]
        sv = z - np.array([zl(v) for v in x])
        ysurf = skin.y(xm, zl(xm))
        gate = 1.0 - smooth((y - (ysurf + 0.035 * s)) / (0.04 * s))
        wx = 1.0 - smooth((np.abs(x - xm) - 1.0 * hw) / (0.9 * hw))
        wv = smooth(-sv / (0.0025 * s)) * (1.0 - smooth((-sv - 0.030 * s) / (0.030 * s)))
        return wx * wv * gate, sv, gate
    w_open, _, _ = mouth_w(co0)
    dopen = np.zeros_like(co0); dopen[:, 2] = -D * w_open; dopen[:, 1] = 0.3 * D * w_open
    surf_open = Surface(co0 + dopen, tris)
    uvm = (0.5, 0.5)
    fold_m, open_m = [], []
    for r in range(nrm):
        f = r / (nrm - 1)
        rf, ro = [], []
        for x in xs:
            u = (x - xm) / (hw * 0.62)
            hx = H * max(0.0, 1 - u * u) ** 0.9      # spitz auslaufend zu den Winkeln
            z0 = zl(x)
            y0 = front_y(skin, x, z0)
            zo = z0 - f * hx
            yo = front_y(surf_open, x, zo)
            rf.append((x, y0 - OFF, z0))
            ro.append((x, (yo if yo is not None else y0) - OFF, zo))
        fold_m.append(rf); open_m.append(ro)
    vm = add_patch(bm, fold_m, 1, uvm)
    pending.append(("Mund_Auf", vm, open_m))
    bm.verts.index_update()
    for name, vs_, tgt in pending:
        keys[name] = {v.index: Vector(tgt[r][c]) for r, row in enumerate(vs_) for c, v in enumerate(row)}
    patch_idx = list(range(n_skin, len(bm.verts)))
    bm.to_mesh(me); bm.free()
    me.update()
    coB = vco(me)

    # ---- Shape Keys
    ob.shape_key_add(name="Basis", from_mix=False)
    def add_key(name, co_new):
        kb = ob.shape_key_add(name=name, from_mix=False)
        kb.slider_min, kb.slider_max = 0.0, 1.0
        kb.data.foreach_set("co", co_new.astype(np.float32).ravel())
        print(f"[Key] {name}: max. Verschiebung {np.linalg.norm(co_new - coB, axis=1).max() * 100:.2f} cm")
    for name in ("Blinzeln_L", "Blinzeln_R"):
        c = coB.copy()
        for i, p in keys[name].items(): c[i] = p
        add_key(name, c)
    c = coB + dopen_pad(dopen, len(coB))
    for i, p in keys["Mund_Auf"].items(): c[i] = p
    add_key("Mund_Auf", c)
    # Mund_Schmal: Mundwinkel nach innen (auch die gefalteten Mundraum-Ecken)
    x, y, z = coB[:, 0], coB[:, 1], coB[:, 2]
    sv = z - np.array([zl(v) for v in x])
    ysurf = skin.y(xm, zl(xm))
    gate = 1.0 - smooth((y - (ysurf + 0.035 * s)) / (0.04 * s))
    dx = x - xm
    wc = smooth((np.abs(dx) - 0.35 * hw) / (0.6 * hw)) * (1.0 - smooth((np.abs(dx) - 1.1 * hw) / (0.6 * hw)))
    wz = 1.0 - smooth((np.abs(sv) - 0.006 * s) / (0.012 * s))
    wt = wc * wz * gate
    c = coB.copy(); c[:, 0] -= np.sign(dx) * 0.0035 * s * wt; c[:, 1] -= 0.0008 * s * wt
    add_key("Mund_Schmal", c)

    # ---- Patches: Gruppe Head voll gewichtet
    vg = ob.vertex_groups["Head"]
    vg.add(patch_idx, 1.0, 'REPLACE')
    print(f"[Face] Patch-Vertices: {len(patch_idx)} (alle Head=1.0), Mund Mitte x={xm:.4f} Halbbreite={hw:.4f}")
    return patch_idx


def dopen_pad(d, n):
    out = np.zeros((n, 3), np.float32); out[:len(d)] = d
    return out


# ---------------------------------------------------------------- 4. Animationen
def pose_setup(rig):
    arm = rig.data
    rest = {b.name: b.matrix_local.to_3x3() for b in arm.bones}
    return rest


def rotq(axis, deg):
    return Quaternion(axis, math.radians(deg))


X, Y, Z = (1, 0, 0), (0, 1, 0), (0, 0, 1)


def pose_state(rig, rest, rel, loc=None):
    """rel[bone] = Qrel (Weltachsen der Ruhepose, in der Elternpose); loc[bone] = Weltverschiebung (nur Wurzel)."""
    for pb in rig.pose.bones:
        q = rel.get(pb.name, Quaternion())
        m = rest[pb.name]
        pb.rotation_mode = 'QUATERNION'
        pb.rotation_quaternion = (m.inverted() @ q.to_matrix() @ m).to_quaternion()
        if pb.name == "Hips":
            v = (loc or {}).get("Hips", Vector())
            pb.location = m.inverted() @ v


def base_rel():
    """Ruhepose aus der A-Haltung: Arme dicht an den Körper, Ellbogen leicht gebeugt."""
    r = {}
    for sd, sg in (("L", 1.0), ("R", -1.0)):
        r["UpperArm." + sd] = rotq(Y, 8.0 * sg)
        r["LowerArm." + sd] = rotq(X, -9.0)
        r["Hand." + sd] = rotq(X, -4.0)
    return r


def mul(*qs):
    out = Quaternion()
    for q in qs: out = out @ q
    return out


def key_action(rig, rest, name, frames, fn, loop=False):
    """fn(f_norm 0..1, frame) -> (rel-Dict, loc-Dict); Zusatz auf die Basis-Ruhepose."""
    act = bpy.data.actions.new(name); act.use_fake_user = True
    ad = rig.animation_data_create(); ad.action = act
    base = base_rel()
    f0 = 1
    for f in list(range(f0, f0 + frames, 2)) + [f0 + frames]:
        u = (f - f0) / frames
        extra, loc = fn(u)
        rel = dict(base)
        for k, q in extra.items(): rel[k] = q @ rel.get(k, Quaternion())
        pose_state(rig, rest, rel, loc)
        for pb in rig.pose.bones:
            pb.keyframe_insert("rotation_quaternion", frame=f)
            if pb.name == "Hips": pb.keyframe_insert("location", frame=f)
    act.frame_start = f0; act.frame_end = f0 + frames
    act.use_frame_range = True
    ad.action = None
    return act


def pulse(u, a, b, ease=0.15):
    """0 vor a, 1 zwischen a+ease und b-ease, 0 nach b (glatt)."""
    return float(smooth((u - a) / ease) * (1 - smooth((u - (b - ease)) / ease)))


def anim_idle(u):
    w = 2 * math.pi * u
    br = math.sin(2 * w)                       # zwei Atemzüge in 4 s
    sh = math.sin(w)                           # ein Gewichtswechsel
    rel = {
        "Chest": mul(rotq(X, 1.1 * br)), "Spine": mul(rotq(X, 0.5 * br), rotq(Y, -0.9 * sh)),
        "Hips": rotq(Y, 1.4 * sh), "Neck": rotq(X, -0.4 * br), "Head": rotq(Z, 1.5 * math.sin(w + 1.0)),
        "Shoulder.L": rotq(Y, -0.6 * br), "Shoulder.R": rotq(Y, 0.6 * br),
        "UpperArm.L": rotq(X, 0.8 * math.sin(w + 0.4)), "UpperArm.R": rotq(X, -0.8 * math.sin(w + 0.4)),
        "UpperLeg.L": rotq(Y, -1.4 * sh), "UpperLeg.R": rotq(Y, -1.4 * sh),
    }
    return rel, {"Hips": Vector((0.004 * sh, 0, 0.0025 * br))}


def anim_explain(u):
    g = pulse(u, 0.0, 1.0, 0.22)                      # Ein-/Ausblenden der Geste
    beat = 0.5 + 0.5 * math.sin(2 * math.pi * (u * 3.0 - 0.25))   # drei Betonungen
    wob = g * (0.55 + 0.45 * beat)
    rel = {"Spine": rotq(X, -2 * g), "Chest": rotq(Z, 3 * g * math.sin(2 * math.pi * u * 1.5)),
           "Neck": rotq(X, 3 * g * beat), "Head": mul(rotq(X, 2 * g * beat), rotq(Z, 4 * g * math.sin(2 * math.pi * u * 1.5)))}
    for sd, sg in (("L", 1.0), ("R", -1.0)):
        out = 1.0 if sd == "L" else 0.7
        rel["Shoulder." + sd] = rotq(Y, -3.0 * sg * g)
        rel["UpperArm." + sd] = mul(rotq(Y, -7 * sg * wob), rotq(X, -24 * g - 8 * wob))
        rel["LowerArm." + sd] = mul(rotq(Z, sg * -(14 + 10 * beat) * g), rotq(X, -58 * g - 10 * wob))
        rel["Hand." + sd] = mul(rotq(X, -14 * g * beat), rotq(Z, sg * -6 * g))
    return rel, {}


def anim_listening(u):
    w = 2 * math.pi * u
    nod = math.sin(math.pi * min(1.0, max(0.0, (u - 0.25) / 0.5)))
    br = math.sin(w)
    rel = {"Head": mul(rotq(Y, 7.0), rotq(X, 4.5 * nod), rotq(Z, -3.0)), "Neck": mul(rotq(Y, 4.0), rotq(X, 2.0 * nod), rotq(Z, -1.5)),
           "Chest": rotq(X, 0.8 * br), "Spine": rotq(X, 0.3 * br)}
    return rel, {}


def anim_zeigen(u):
    g = pulse(u, 0.0, 1.0, 0.36)                        # Ein-/Ausblenden
    pt = g * (1.0 + 0.04 * math.sin(2 * math.pi * u * 2.0))
    rel = {"Chest": rotq(Z, -9 * g), "Neck": rotq(Z, -10 * g), "Head": rotq(Z, -22 * g),
           "Shoulder.R": rotq(Y, 8 * pt),
           "UpperArm.R": mul(rotq(Y, 76 * pt), rotq(Z, 8 * pt)),
           "LowerArm.R": rotq(X, 4 * g), "Hand.R": rotq(Z, 0)}
    rel["UpperArm.L"] = rotq(Y, 0)
    return rel, {}


def make_actions(rig):
    rest = pose_setup(rig)
    acts = [key_action(rig, rest, "Alex_Idle", 120, anim_idle),
            key_action(rig, rest, "Alex_Explain", 90, anim_explain),
            key_action(rig, rest, "Alex_Listening", 60, anim_listening),
            key_action(rig, rest, "Alex_Zeigen", 75, anim_zeigen)]
    for a in acts:
        print(f"[Action] {a.name}: Frames {a.frame_range[0]:.0f}-{a.frame_range[1]:.0f}")
    # Ruhepose (Basis) im Rig stehen lassen
    pose_state(rig, rest, base_rel())
    return acts


# ---------------------------------------------------------------- Build
def build():
    ob, o = prep_mesh()
    sc = bpy.context.scene
    sc.render.fps = FPS
    j = joints(ob, o)
    rig = build_armature(j)
    bind_auto(ob, rig)
    # Textur neben die FBX legen und im Material neu verknüpfen
    out_dir = os.path.dirname(os.path.abspath(DST)); os.makedirs(out_dir, exist_ok=True)
    img = next(i for i in bpy.data.images if i.name.startswith("Alex_Trellis"))
    png = os.path.join(out_dir, TEX + ".png")
    build_face(ob, o)
    img.name = TEX + "_alt"
    img.filepath_raw = png; img.file_format = 'PNG'; img.save()
    new = bpy.data.images.load(png); new.name = TEX
    for m in ob.data.materials:
        if m and m.node_tree:
            for nd in m.node_tree.nodes:
                if nd.type == 'TEX_IMAGE' and nd.image == img: nd.image = new
    bpy.data.images.remove(img)
    mat0 = ob.data.materials[0]; mat0.name = "Alex_Trellis_Mat"
    print("[Export] Materialien:", [m.name for m in ob.data.materials])
    acts = make_actions(rig)
    # Jede Action als eigene NLA-Spur: der Exporter schreibt dann einen Take je Strip mit exakt dem Strip-Namen
    ad = rig.animation_data
    for a in acts:
        tr = ad.nla_tracks.new(); tr.name = a.name
        st = tr.strips.new(a.name, int(a.frame_start), a)
        if a.slots: st.action_slot = a.slots[0]
        tr.mute = False
    ad.action = None
    for kb in ob.data.shape_keys.key_blocks: kb.value = 0.0
    bpy.ops.object.select_all(action='DESELECT')
    bpy.ops.export_scene.fbx(
        filepath=os.path.abspath(DST), use_selection=False,
        object_types={'ARMATURE', 'MESH'}, use_mesh_modifiers=True, mesh_smooth_type='FACE',
        axis_forward='-Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
        bake_space_transform=True, add_leaf_bones=False, use_armature_deform_only=False,
        bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=True,
        bake_anim_use_all_bones=True, bake_anim_force_startend_keying=True, bake_anim_simplify_factor=1.0,
        bake_anim_step=1.0, path_mode='STRIP', embed_textures=False)
    print("[Export]", DST)
    # Arbeitsdatei für Debug (nicht versioniert)
    if os.environ.get("ALEX_RIG_BLEND"):
        bpy.ops.wm.save_as_mainfile(filepath=os.environ["ALEX_RIG_BLEND"])


# ---------------------------------------------------------------- Vorschau / Verifikation
def use_action(rig, act):
    rig.animation_data.action = act
    if act.slots: rig.animation_data.action_slot = act.slots[0]


def shot(sc, cam, name, loc, rot, scale, res=(540, 900)):
    cam.location, cam.rotation_euler = loc, rot
    cam.data.ortho_scale = scale
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.filepath = os.path.join(DOCS, name); bpy.ops.render.render(write_still=True)


def preview():
    reset_import_fbx(DST)
    sc = bpy.context.scene
    ob = bpy.data.objects[MESH]
    rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    keys = [k.name for k in ob.data.shape_keys.key_blocks]
    print(f"[Verify] Mesh {ob.name}: {len(ob.data.vertices)} Vertices, {len(ob.data.polygons)} Polygone")
    print("[Verify] Shape Keys:", keys)
    print("[Verify] Armature:", rig.name, "- Knochen:", len(rig.data.bones), [b.name for b in rig.data.bones])
    print("[Verify] Materialien:", [m.name for m in ob.data.materials], "Texturen:", [(i.name, tuple(i.size)) for i in bpy.data.images])
    acts = {a.name: a for a in bpy.data.actions}
    print("[Verify] Actions:", [(n, tuple(a.frame_range)) for n, a in sorted(acts.items())])
    gruppen = {g.name for g in ob.vertex_groups}
    nw = sum(1 for v in ob.data.vertices if not any(g.weight > 0 for g in v.groups))
    print(f"[Verify] Vertex-Gruppen: {len(gruppen)}, Vertices ohne Gewicht: {nw}")
    bb = [ob.matrix_world @ Vector(c) for c in ob.bound_box]
    print(f"[Verify] Höhe: {max(v.z for v in bb) - min(v.z for v in bb):.3f} (Szeneneinheit nach Import)")
    short = {n.split("|")[-1]: a for n, a in acts.items() if not n.startswith("Key|")}

    w = bpy.data.worlds.new("w"); w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.82, 0.82, 0.82, 1.0)
    w.node_tree.nodes["Background"].inputs[1].default_value = 1.0
    sc.world = w
    sc.render.engine = 'BLENDER_EEVEE'
    sun = bpy.data.objects.new("s", bpy.data.lights.new("s", "SUN")); sun.data.energy = 3.5
    sun.rotation_euler = (math.radians(55), 0, math.radians(-25)); sc.collection.objects.link(sun)
    cam = bpy.data.objects.new("c", bpy.data.cameras.new("c")); cam.data.type = 'ORTHO'
    cam.data.clip_start, cam.data.clip_end = 0.1, 50
    sc.collection.objects.link(cam); sc.camera = cam
    os.makedirs(DOCS, exist_ok=True)
    if rig.animation_data is None: rig.animation_data_create()
    ob.data.shape_keys.animation_data_clear()
    sc.frame_set(1)
    idle, expl, zei = short["Alex_Idle"], short["Alex_Explain"], short["Alex_Zeigen"]
    for n, a, f in (("rig_idle.png", idle, 61), ("rig_zeigen.png", zei, 38), ("rig_explain.png", expl, 36)):
        use_action(rig, a); sc.frame_set(f)
        shot(sc, cam, n, Vector((0, -6, 0.9)), (math.pi / 2, 0, 0), 2.05, (900, 900))
    if os.environ.get("ALEX_SEITE"):
        for n, a, f in (("seite_zeigen.png", zei, 38), ("seite_explain.png", expl, 36)):
            use_action(rig, a); sc.frame_set(f)
            shot(sc, cam, n, Vector((6, 0, 0.9)), (math.pi / 2, 0, math.pi / 2), 2.05, (900, 900))
    use_action(rig, idle); sc.frame_set(1)
    head = Vector((0.0, -6, 1.58))
    for n, vals in (("gesicht_neutral.png", {}), ("gesicht_blinzeln.png", {"Blinzeln_L": 1, "Blinzeln_R": 1}),
                    ("gesicht_mund_auf.png", {"Mund_Auf": 1})):
        for kb in ob.data.shape_keys.key_blocks:
            if kb.name != "Basis": kb.value = float(vals.get(kb.name, 0.0))
        shot(sc, cam, n, head, (math.pi / 2, 0, 0), 0.20, (900, 900))
    if os.environ.get("ALEX_EXTRA"):
        for n, vals in (("gesicht_halb.png", {"Blinzeln_L": 0.5, "Blinzeln_R": 0.5}), ("gesicht_schmal.png", {"Mund_Schmal": 1}),
                        ("gesicht_blinzeln_l.png", {"Blinzeln_L": 1})):
            for kb in ob.data.shape_keys.key_blocks:
                if kb.name != "Basis": kb.value = float(vals.get(kb.name, 0.0))
            shot(sc, cam, n, head, (math.pi / 2, 0, 0), 0.20, (900, 900))
        for kb in ob.data.shape_keys.key_blocks: kb.value = 0.0
        shot(sc, cam, "gesicht_seite.png", Vector((6, 0, 1.58)), (math.pi / 2, 0, math.pi / 2), 0.30, (900, 900))


if MODE in ("build", "alles"): build()
if MODE == "preview": preview()
if MODE == "alles":
    subprocess.run([bpy.app.binary_path, "-b", "--factory-startup", "--python", os.path.abspath(__file__), "--", "preview", SRC, DST, DOCS], check=True)
    ov = os.path.join(DOCS, "rig_uebersicht.png")
    subprocess.run(["magick"] + [os.path.join(DOCS, n) for n in ("rig_idle.png", "rig_explain.png", "rig_zeigen.png")] + ["+append", ov], check=True)
    print("[Vorschau]", ov)
