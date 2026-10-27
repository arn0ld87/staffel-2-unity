# Gesichtsanimation für die Alex-Büste: Shape Keys (Blinzeln, Mund, Neutral) + FBX-Export für Unity.
#   blender -b --factory-startup --python alex_gesicht.py -- <eingabe.fbx> <ausgabe.fbx> <docs-ordner>
# Ablauf: Build (Shape Keys, Mundraum, Export) -> frische Blender-Sitzung importiert den Export
# erneut, listet Keys/Actions und rendert die Vorschauen (docs-ordner/vorschau_*.png).
# Deterministisch: kein Zufall, feste Parameter. Texte stehen in Metern (Büste = 1 m hoch).
import bpy, sys, os, math, subprocess, mathutils
import numpy as np
from mathutils import Vector

ARGS = sys.argv[sys.argv.index("--") + 1:]
if ARGS and ARGS[0] in ("build", "preview"):
    MODE, ARGS = ARGS[0], ARGS[1:]
else:
    MODE = "alles"
SRC, DST, DOCS = ARGS[0], ARGS[1], ARGS[2]


def smooth(t):
    t = np.clip(t, 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def reset_import(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)


# ---------------------------------------------------------------- Build
def dense_samples(me, img, tri_mask_fn):
    """Texturfarben dicht über die Dreiecke abtasten (das Netz ist gröber als die Textur).
    Liefert Weltpositionen P und Farben C."""
    w, h = img.size
    px = np.empty(w * h * 4, np.float32); img.pixels.foreach_get(px)
    px = px.reshape(h, w, 4)[:, :, :3]
    nv = len(me.vertices)
    co = np.empty(nv * 3, np.float32); me.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
    uv = np.empty(len(me.loops) * 2, np.float32); me.uv_layers[0].data.foreach_get("uv", uv); uv = uv.reshape(-1, 2)
    me.calc_loop_triangles()
    nt = len(me.loop_triangles)
    tv = np.empty(nt * 3, np.int32); me.loop_triangles.foreach_get("vertices", tv); tv = tv.reshape(-1, 3)
    tl = np.empty(nt * 3, np.int32); me.loop_triangles.foreach_get("loops", tl); tl = tl.reshape(-1, 3)
    sel = tri_mask_fn(co[tv])
    tv, tl = tv[sel], tl[sel]
    n = 8
    bw = np.array([(i / n, j / n, 1 - (i + j) / n) for i in range(n + 1) for j in range(n + 1 - i)], np.float32)
    P = np.einsum("sk,tkd->tsd", bw, co[tv]).reshape(-1, 3)
    U = np.einsum("sk,tkd->tsd", bw, uv[tl]).reshape(-1, 2)
    xs = np.clip((U[:, 0] * w).astype(int), 0, w - 1); ys = np.clip((U[:, 1] * h).astype(int), 0, h - 1)
    return co, P, px[ys, xs]


def find_eyes(P, C):
    """Iris-Blau (b deutlich über r und g) im oberen Gesicht; zwei Cluster über die größte x-Lücke."""
    r, g, b = C[:, 0], C[:, 1], C[:, 2]
    m = (b > r + 0.10) & (b > g) & (P[:, 2] > 0.6)
    Q = P[m]
    # Cluster über x-Lücken > 3 cm; Ausreißer (Haar, Ohr) fallen weg, die zwei größten bleiben die Augen
    order = np.argsort(Q[:, 0]); Q = Q[order]
    cuts = np.where(np.diff(Q[:, 0]) > 0.03)[0] + 1
    groups = sorted(np.split(Q, cuts), key=len, reverse=True)[:2]
    groups.sort(key=lambda gq: gq[:, 0].mean())
    split = 0.5 * (groups[0][:, 0].max() + groups[1][:, 0].min())
    eyes = []
    for E in groups:
        lo, hi = E.min(0), E.max(0)
        eyes.append(dict(n=int(len(E)), c=(lo + hi) / 2, lo=lo, hi=hi))
    print(f"[Augen] {m.sum()} blaue Abtastpunkte, Trennung bei x={split:.3f}")
    for name, e in zip(("rechts(Figur)/x<", "links(Figur)/x>"), eyes):
        print(f"[Augen] {name}: n={e['n']} Mitte={np.round(e['c'], 3)} x={e['lo'][0]:.3f}..{e['hi'][0]:.3f} z={e['lo'][2]:.3f}..{e['hi'][2]:.3f}")
    return eyes


def find_mouth(P, C, eyes):
    """Dunkle Mundlinie als Parabel z(x) anpassen; Suchfenster relativ zu den Augen."""
    L = C @ np.array([0.3, 0.55, 0.15], np.float32)
    ez = np.mean([e["c"][2] for e in eyes])
    x0, x1 = eyes[0]["c"][0] - 0.03, eyes[1]["c"][0] + 0.01
    z0, z1 = ez - 0.185, ez - 0.135
    m = (L < 0.15) & (P[:, 2] > z0) & (P[:, 2] < z1) & (P[:, 0] > x0) & (P[:, 0] < x1)
    x, z = P[m, 0], P[m, 2]
    keep = np.ones(len(x), bool)
    for _ in range(6):
        coef = np.polyfit(x[keep], z[keep], 2)
        res = np.abs(np.polyval(coef, x) - z)
        keep = res < max(0.004, np.percentile(res[keep], 70) * 1.5)
    xmin, xmax = x[keep].min(), x[keep].max()
    print(f"[Mund] {m.sum()} dunkle Punkte, {keep.sum()} Inlier, x={xmin:.3f}..{xmax:.3f}, Parabel={np.round(coef, 3)}")
    return dict(coef=coef, xm=(xmin + xmax) / 2, hw=(xmax - xmin) / 2, xmin=xmin, xmax=xmax)


def build():
    reset_import(SRC)
    bust = bpy.data.objects["Alex_Bust"]; rig = bpy.data.objects["Alex_Rig"]
    me = bust.data
    img = bpy.data.images["Alex_BaseColor"]
    co, P, C = dense_samples(me, img, lambda t: (t[:, :, 1].max(1) < -0.1) & (t[:, :, 2].min(1) > 0.3) & (t[:, :, 2].max(1) < 0.9))
    eyes = find_eyes(P, C)
    mouth = find_mouth(P, C, eyes)
    x, y, z = co[:, 0], co[:, 1], co[:, 2]
    delta = {}

    # --- Blinzeln: Augenbereich vertikal auf die untere Lidlinie stauchen (Ellipsoid-Maske, glatter Abfall)
    def blink(e):
        a = 0.5 * (e["hi"][0] - e["lo"][0]) * 1.15 + 0.004
        b = 0.5 * (e["hi"][2] - e["lo"][2]) * 1.35 + 0.004
        ay = max(0.03, 0.5 * (e["hi"][1] - e["lo"][1]) + 0.02)
        c = e["c"]
        d = np.sqrt(((x - c[0]) / a) ** 2 + ((z - c[2]) / b) ** 2 + ((y - c[1]) / ay) ** 2)
        w = 1.0 - smooth((d - 1.0) / 0.9)          # 1 im Auge, 0 bei d=1.9
        zt = c[2] - 0.45 * b                       # untere Lidlinie
        dv = np.zeros_like(co)
        dv[:, 2] = w * 0.93 * (zt - z)
        print(f"[Blinzeln] Halbachsen a={a:.3f} b={b:.3f} ay={ay:.3f}, Lidlinie z={zt:.3f}, {int((w > 0).sum())} Vertices")
        return dv
    # Figur-links = +X (Bild rechts), Figur-rechts = -X
    delta["Blinzeln_L"] = blink(eyes[1])
    delta["Blinzeln_R"] = blink(eyes[0])

    # --- Mundmasken
    coef, xm, hw = mouth["coef"], mouth["xm"], mouth["hw"]
    def zl(xx): return np.polyval(coef, np.clip(xx, mouth["xmin"], mouth["xmax"]))
    s = z - zl(x)                                   # Höhe über der Mundlinie
    dx = x - xm
    near = (np.abs(dx) < 0.03) & (np.abs(s) < 0.02) & (y < -0.1)
    ym = float(np.min(y[near])) if near.any() else -0.25
    ysurf = float(np.mean(y[near & (y < ym + 0.02)]))
    gate = 1.0 - smooth((y - (ym + 0.07)) / 0.06)   # Hinterkopf/Nacken ausnehmen
    wx = 1.0 - smooth((np.abs(dx) - 0.5 * hw) / (1.1 * hw))
    print(f"[Mund] Mitte x={xm:.3f}, Halbbreite={hw:.3f}, Linie z(xm)={float(zl(xm)):.3f}, Oberfläche y={ysurf:.3f}")

    D = 0.008                                        # Unterlippen-/Kieferabsenkung (m)
    sdown = smooth(-s / 0.006)
    wj = 1.0 - smooth((-s - 0.06) / 0.09)
    w_auf = wx * sdown * wj * gate
    dv = np.zeros_like(co); dv[:, 2] = -D * w_auf; dv[:, 1] = 0.3 * D * w_auf
    delta["Mund_Auf"] = dv

    wzl = 1.0 - smooth((np.abs(s) - 0.025) / 0.03)
    wc = smooth((np.abs(dx) - 0.3 * hw) / (0.7 * hw)) * wzl * (1.0 - smooth((np.abs(dx) - 1.1 * hw) / (0.7 * hw))) * gate
    dv = np.zeros_like(co); dv[:, 0] = -np.sign(dx) * 0.014 * wc; dv[:, 1] = -0.004 * wc
    delta["Mund_Schmal"] = dv

    wxn = 1.0 - smooth((np.abs(dx) - hw) / (0.8 * hw))
    wzn = np.where(s > 0, 1.0 - smooth((s - 0.03) / 0.05), 1.0 - smooth((-s - 0.05) / 0.06))
    mm = wxn * wzn * gate
    dv = np.zeros_like(co)
    dv[:, 2] = -0.75 * (zl(x) - float(zl(xm))) * mm                    # Lächelkurve abflachen
    dv[:, 0] = -np.sign(dx) * 0.006 * smooth(np.abs(dx) / hw) * mm       # Mundwinkel nach innen
    wzc = smooth(s / 0.02) * (1.0 - smooth((s - 0.05) / 0.04))
    wcheek = smooth((np.abs(dx) - 0.5 * hw) / (0.5 * hw)) * (1.0 - smooth((np.abs(dx) - 1.4 * hw) / (0.6 * hw))) * wzc * gate
    dv[:, 2] -= 0.004 * wcheek                                           # Wangen entspannen
    dv[:, 1] += 0.002 * wcheek
    delta["Neutral"] = dv

    bust.shape_key_add(name="Basis", from_mix=False)
    for name, dv in delta.items():
        kb = bust.shape_key_add(name=name, from_mix=False)
        kb.slider_min, kb.slider_max = 0.0, 1.0
        kb.data.foreach_set("co", (co + dv).ravel())
        print(f"[Key] {name}: max. Verschiebung {np.linalg.norm(dv, axis=1).max() * 1000:.1f} mm")

    # --- Mundraum: dunkle Linse entlang der Mundlinie. Im Ruhezustand kollabiert und 12 mm hinter der
    # Oberfläche (unsichtbar), bei Mund_Auf öffnet sie sich unter der Linie und liegt 3 mm vor der gedehnten Haut.
    nrm = np.empty(len(me.vertices) * 3, np.float32); me.vertices.foreach_get("normal", nrm); nrm = nrm.reshape(-1, 3)
    n0 = nrm[near].mean(0); f_out = Vector((n0[0], n0[1], 0.0)).normalized()
    cols = 25
    xs = np.linspace(mouth["xmin"] + 0.012, mouth["xmax"] - 0.012, cols)
    front = y < ym + 0.08
    ysf = np.empty(cols)
    for k, xv in enumerate(xs):
        zv = float(zl(xv))
        sel = front & (np.abs(x - xv) < 0.007) & (np.abs(z - zv) < 0.007)
        yy = y[sel]; ysf[k] = yy[yy <= yy.min() + 0.006].mean() if sel.any() else ysurf
    ysf = np.convolve(np.pad(ysf, 2, mode="edge"), np.ones(5) / 5, mode="valid")   # Netzrauschen glätten
    hmax = D + 0.003
    hx = hmax * np.clip(1.0 - ((xs - xm) / hw) ** 2, 0.0, 1.0) ** 0.8
    ztop = zl(xs) + 0.0015
    rows = (0.0, 0.5, 1.0)
    def lens(open_):
        pts = []
        for r in rows:
            for k in range(cols):
                zz = ztop[k] - (r * (hx[k] + 0.0015) if open_ else 0.0)
                off = -0.003 if open_ else 0.012
                pts.append((xs[k] + f_out.x * off, ysf[k] + f_out.y * off, zz))
        return np.array(pts, np.float32)
    faces = [(r * cols + c, (r + 1) * cols + c, (r + 1) * cols + c + 1, r * cols + c + 1)
             for r in range(len(rows) - 1) for c in range(cols - 1)]
    mr = bpy.data.meshes.new("Alex_Mundraum"); mr.from_pydata(lens(False).tolist(), [], faces); mr.update()
    for f in mr.polygons: f.use_smooth = True
    mo = bpy.data.objects.new("Alex_Mundraum", mr)
    bpy.context.scene.collection.objects.link(mo)
    mo.parent = rig
    mat = bpy.data.materials.new("Alex_Mundraum_Mat"); mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (0.02, 0.003, 0.003, 1.0)
    bsdf.inputs["Roughness"].default_value = 1.0
    bsdf.inputs["Specular IOR Level"].default_value = 0.0
    mr.materials.append(mat)
    vg = mo.vertex_groups.new(name="Head"); vg.add(list(range(len(mr.vertices))), 1.0, 'REPLACE')
    md = mo.modifiers.new("Armature", 'ARMATURE'); md.object = rig
    mo.shape_key_add(name="Basis", from_mix=False)
    kb = mo.shape_key_add(name="Mund_Auf", from_mix=False)
    kb.data.foreach_set("co", lens(True).ravel())

    # --- Textur neben die FBX legen und im Material neu verknüpfen
    out_dir = os.path.dirname(os.path.abspath(DST)); os.makedirs(out_dir, exist_ok=True)
    png = os.path.join(out_dir, "Alex_BaseColor.png")
    img.filepath_raw = png; img.file_format = 'PNG'; img.save()
    new = bpy.data.images.load(png); new.name = "Alex_BaseColor_ext"
    for m in bust.data.materials:
        for nd in m.node_tree.nodes:
            if nd.type == 'TEX_IMAGE' and nd.image == img: nd.image = new
    bpy.data.images.remove(img); new.name = "Alex_BaseColor"

    # --- Actions auf kurze Namen: Take-Name im Export wird "Alex_Rig|Alex_Idle" wie in der Eingabe
    for a in bpy.data.actions:
        a.name = a.name.split("|")[-1]; a.use_fake_user = True
    print("[Actions]", sorted((a.name, tuple(a.frame_range)) for a in bpy.data.actions))
    # Der Exporter bäckt die aktuellen Key-Werte in jeden Take: vor dem Export alle auf 0 (Ruhewert)
    for o in (bust, mo):
        for kb in o.data.shape_keys.key_blocks: kb.value = 0.0
    bpy.ops.export_scene.fbx(
        filepath=os.path.abspath(DST), use_selection=False,
        object_types={'ARMATURE', 'MESH'}, use_mesh_modifiers=True, mesh_smooth_type='FACE',
        axis_forward='-Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
        bake_space_transform=True, add_leaf_bones=False, use_armature_deform_only=False,
        bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
        bake_anim_use_all_bones=True, bake_anim_force_startend_keying=True, bake_anim_simplify_factor=1.0,
        path_mode='STRIP', embed_textures=False)
    print("[Export]", DST)


# ---------------------------------------------------------------- Vorschau / Verifikation
def preview():
    reset_import(DST)
    sc = bpy.context.scene
    bust = bpy.data.objects["Alex_Bust"]
    print("[Verify] Mesh-Objekte:", [(o.name, len(o.data.vertices)) for o in bpy.data.objects if o.type == 'MESH'])
    print("[Verify] Shape Keys Alex_Bust:", [k.name for k in bust.data.shape_keys.key_blocks])
    mr = bpy.data.objects.get("Alex_Mundraum")
    if mr and mr.data.shape_keys: print("[Verify] Shape Keys Alex_Mundraum:", [k.name for k in mr.data.shape_keys.key_blocks])
    print("[Verify] Actions:", sorted((a.name, tuple(a.frame_range)) for a in bpy.data.actions))
    print("[Verify] Armatur-Bones:", [b.name for o in bpy.data.objects if o.type == 'ARMATURE' for b in o.data.bones])
    print("[Verify] Texturen:", [(i.name, tuple(i.size)) for i in bpy.data.images])
    for o in (bust, mr):
        if o: print("[Verify]", o.name, "Eltern:", o.parent.name if o.parent else None, "Modifier:", [m.type for m in o.modifiers])
    rig = bpy.data.objects["Alex_Rig"]
    idle = next((a for a in bpy.data.actions if a.name.endswith("Alex_Idle")), None)
    if idle:
        rig.animation_data_create().action = idle
    bb = [bust.matrix_world @ Vector(c) for c in bust.bound_box]
    ctr = sum(bb, Vector()) / 8
    h = max(v.z for v in bb) - min(v.z for v in bb)
    for loc, en in (((1, -2, 2), 400), ((-2, -1, 1), 150), ((0, 2, 2), 200)):
        l = bpy.data.lights.new("l", "AREA"); l.energy = en; l.size = 2
        lo = bpy.data.objects.new("l", l); lo.location = ctr + Vector(loc)
        lo.rotation_euler = (ctr - lo.location).to_track_quat('-Z', 'Y').to_euler(); sc.collection.objects.link(lo)
    engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
    sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in engines else 'BLENDER_EEVEE'
    sc.render.resolution_x, sc.render.resolution_y = 640, 800
    sc.world = bpy.data.worlds.new("w"); sc.world.color = (0.05, 0.045, 0.04)
    cam = bpy.data.cameras.new("c"); co = bpy.data.objects.new("c", cam); sc.collection.objects.link(co)
    co.location = ctr + Vector((0, -h * 2.2, h * 0.1))
    co.rotation_euler = (ctr + Vector((0, 0, h * 0.12)) - co.location).to_track_quat('-Z', 'Y').to_euler()
    cam.lens = 70; sc.camera = co
    sets = (("neutral", {"Neutral": 1}), ("laechelnd", {}), ("blinzeln", {"Blinzeln_L": 1, "Blinzeln_R": 1}),
            ("sprechen", {"Mund_Auf": 1}))
    extra = (("blinzeln_halb", {"Blinzeln_L": 0.5, "Blinzeln_R": 0.5}), ("schmal", {"Mund_Schmal": 1}))
    os.makedirs(DOCS, exist_ok=True)
    keyed = [o for o in (bust, mr) if o and o.data.shape_keys]
    for o in keyed: o.data.shape_keys.animation_data_clear()   # gebackene Konstantkurven würden die Werte überschreiben
    for name, vals in sets + (extra if os.environ.get("ALEX_EXTRA") else ()):
        for o in keyed:
            for kb in o.data.shape_keys.key_blocks:
                if kb.name != "Basis": kb.value = float(vals.get(kb.name, 0.0))
        sc.frame_set(1)
        sc.render.filepath = os.path.join(DOCS, f"vorschau_{name}.png"); bpy.ops.render.render(write_still=True)
    if os.environ.get("ALEX_CLOSE"):   # Nahaufnahmen zur Kontrolle (Debug)
        tgt = Vector((0.05, -0.24, 0.62)); co.location = tgt + Vector((0, -0.75, 0.0))
        co.rotation_euler = (tgt - co.location).to_track_quat('-Z', 'Y').to_euler(); cam.lens = 70
        for name, vals in sets + extra:
            for o in keyed:
                for kb in o.data.shape_keys.key_blocks:
                    if kb.name != "Basis": kb.value = float(vals.get(kb.name, 0.0))
            sc.frame_set(1); sc.render.filepath = os.path.join(os.environ["ALEX_CLOSE"], f"nah_{name}.png"); bpy.ops.render.render(write_still=True)
    # 2x2-Kontaktbogen
    imgs = [bpy.data.images.load(os.path.join(DOCS, f"vorschau_{n}.png")) for n, _ in sets]
    arrs = []
    for i in imgs:
        a = np.empty(i.size[0] * i.size[1] * 4, np.float32); i.pixels.foreach_get(a); arrs.append(a.reshape(i.size[1], i.size[0], 4))
    sheet = np.concatenate([np.concatenate(arrs[:2], axis=1), np.concatenate(arrs[2:], axis=1)][::-1], axis=0)
    out = bpy.data.images.new("sheet", sheet.shape[1], sheet.shape[0], alpha=True)
    out.pixels.foreach_set(sheet.ravel()); out.filepath_raw = os.path.join(DOCS, "vorschau_uebersicht.png"); out.file_format = 'PNG'; out.save()


if MODE in ("build", "alles"): build()
if MODE == "preview": preview()
if MODE == "alles":
    subprocess.run([bpy.app.binary_path, "-b", "--factory-startup", "--python", os.path.abspath(__file__), "--", "preview", SRC, DST, DOCS], check=True)
