"""Projiziert das Frontal-Porträt auf den Kopf der TRELLIS-Figur und brennt es in die Grundtextur.

Aufruf:
  blender -b --factory-startup --python tools/blender/alex_trellis_gesicht.py -- \
      <figur.glb> <portraet.png> <ausgabe.glb> [<vorschau-ordner>]

Kopfmaße (Scheitel, Kinn, Ohrenbreite) werden aus der Geometrie gemessen, die Lage im Porträt ist fest
auf die Vorlage art-source/alex/referenz/alex-portraet-front.png abgestimmt (PORTRAET_*).
"""
import bpy, bmesh, sys, math
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
GLB, PORTRAET, AUS = args[:3]
VORSCHAU = args[3] if len(args) > 3 else None

# Lage im Porträt (Pixel, Ursprung oben links): Haaransatz oben, Kinn, Ohren außen, Mittelachse
PORTRAET_OBEN, PORTRAET_KINN = 15, 950
PORTRAET_OHR_L, PORTRAET_OHR_R = 245, 975
PORTRAET_MITTE = 610

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=GLB)
obj = next(o for o in bpy.context.scene.objects if o.type == "MESH")
me = obj.data
W = obj.matrix_world
vs = [W @ v.co for v in me.vertices]
zmin, zmax = min(v.z for v in vs), max(v.z for v in vs)
H = zmax - zmin


def scheibe(f0, f1):
    return [v for v in vs if zmin + H * f0 <= v.z < zmin + H * f1]


# Kinn: oberhalb der Stelle, an der die Vorderkante vom Gesicht auf die Kapuze springt
kinn_f = 0.80
prev = None
for i in range(60):
    f = 0.95 - i * 0.0025
    sl = scheibe(f, f + 0.0025)
    if not sl:
        continue
    vorne = min(v.y for v in sl)
    if prev is not None and vorne - prev > 0.025:
        kinn_f = f + 0.0025
        break
    prev = vorne
kopf_oben, kinn = zmax, zmin + H * kinn_f
kopf_h = kopf_oben - kinn
ohr = max((scheibe(f, f + 0.01) for f in [kinn_f + kopf_h / H * t for t in (0.4, 0.45, 0.5, 0.55)]),
          key=lambda s: max(v.x for v in s) - min(v.x for v in s))
ohr_l, ohr_r = min(v.x for v in ohr), max(v.x for v in ohr)
mitte_x = (ohr_l + ohr_r) / 2
print(f"KOPF oben={kopf_oben:.3f} kinn={kinn:.3f} hoehe={kopf_h:.3f} ohren={ohr_r - ohr_l:.3f}")

img = bpy.data.images.load(PORTRAET)
PW, PH = img.size
sx = (PORTRAET_OHR_R - PORTRAET_OHR_L) / (ohr_r - ohr_l)
sz = (PORTRAET_KINN - PORTRAET_OBEN) / kopf_h

# Stirn glätten: TRELLIS legt dort eine runde Delle an, die das Porträt sichtbar verzerrt
bm = bmesh.new(); bm.from_mesh(me)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0004)   # TRELLIS-Netz hat lose Doppelpunkte
bm.normal_update()
stirn = [v for v in bm.verts
         if kinn + kopf_h * 0.62 < (W @ v.co).z < kinn + kopf_h * 0.86
         and abs((W @ v.co).x - mitte_x) < (ohr_r - ohr_l) * 0.30 and (W.to_3x3() @ v.normal).y < -0.3
         and v.is_manifold and not v.is_boundary]
for _ in range(20):
    bmesh.ops.smooth_vert(bm, verts=stirn, factor=0.5, use_axis_x=True, use_axis_y=True, use_axis_z=True)
bm.to_mesh(me); bm.free(); me.update()
print("STIRN geglaettet", len(stirn))

from mathutils.bvhtree import BVHTree
baum = BVHTree.FromPolygons([W @ v.co for v in me.vertices], [p.vertices[:] for p in me.polygons])
vs_ymin = min((W @ v.co).y for v in me.vertices)

# zweite UV-Ebene: frontale Parallelprojektion, dazu Maske als Farbattribut
uv_proj = me.uv_layers.new(name="Projektion")
maske = me.color_attributes.new("Maske", "FLOAT_COLOR", "POINT")
me.calc_normals_split() if hasattr(me, "calc_normals_split") else None


def glatt(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


for v in me.vertices:
    p = W @ v.co
    n = (W.to_3x3() @ v.normal).normalized()
    vorne = glatt((-n.y - 0.15) / 0.35)             # nur nach vorn gerichtete Flächen
    hoehe = glatt((p.z - (kinn - 0.006)) / 0.012)   # unter dem Kinn auslaufen
    px = PORTRAET_MITTE + (p.x - mitte_x) * sx
    rand = glatt((min(px - PORTRAET_OHR_L, PORTRAET_OHR_R - px)) / 60)
    # nur die vorderste Fläche: Kapuzeninnenseite und Rückseiten verdeckt
    treffer = baum.ray_cast(Vector((p.x, vs_ymin - 0.1, p.z)), Vector((0, 1, 0)))[0] if vorne > 0 else None
    sichtbar = 1.0 if treffer is not None and treffer.y > p.y - 0.004 else 0.0
    w = vorne * hoehe * rand * sichtbar
    maske.data[v.index].color = (w, w, w, 1)
for poly in me.polygons:
    for li in poly.loop_indices:
        p = W @ me.vertices[me.loops[li].vertex_index].co
        px = PORTRAET_MITTE + (p.x - mitte_x) * sx
        py = PORTRAET_OBEN + (kopf_oben - p.z) * sz
        uv_proj.data[li].uv = (px / PW, 1 - py / PH)

# Material: Original und Porträt mischen, als Emission einbrennen
mat = me.materials[0]
nt = mat.node_tree
orig = next(n for n in nt.nodes if n.type == "TEX_IMAGE")
bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
t_p = nt.nodes.new("ShaderNodeTexImage"); t_p.image = img; t_p.extension = "CLIP"
uvn = nt.nodes.new("ShaderNodeUVMap"); uvn.uv_map = "Projektion"
nt.links.new(uvn.outputs["UV"], t_p.inputs["Vector"])
uvo = nt.nodes.new("ShaderNodeUVMap"); uvo.uv_map = "UVMap"
nt.links.new(uvo.outputs["UV"], orig.inputs["Vector"])
attr = nt.nodes.new("ShaderNodeAttribute"); attr.attribute_name = "Maske"
mul = nt.nodes.new("ShaderNodeMath"); mul.operation = "MULTIPLY"
nt.links.new(attr.outputs["Fac"], mul.inputs[0]); nt.links.new(t_p.outputs["Alpha"], mul.inputs[1])
mix = nt.nodes.new("ShaderNodeMix"); mix.data_type = "RGBA"
nt.links.new(mul.outputs[0], mix.inputs["Factor"])
nt.links.new(orig.outputs["Color"], mix.inputs[6]); nt.links.new(t_p.outputs["Color"], mix.inputs[7])
emi = nt.nodes.new("ShaderNodeEmission")
nt.links.new(mix.outputs[2], emi.inputs["Color"])
out = next(n for n in nt.nodes if n.type == "OUTPUT_MATERIAL")
alt_link = out.inputs["Surface"].links[0].from_socket
nt.links.new(emi.outputs[0], out.inputs["Surface"])

ziel = bpy.data.images.new("Alex_Trellis_BaseColor", *orig.image.size, alpha=False)
t_z = nt.nodes.new("ShaderNodeTexImage"); t_z.image = ziel
nt.links.new(uvo.outputs["UV"], t_z.inputs["Vector"])
nt.nodes.active = t_z
sc = bpy.context.scene
sc.render.engine = "CYCLES"; sc.cycles.samples = 1; sc.cycles.device = "CPU"
sc.render.bake.margin = 8
for o in bpy.context.scene.objects:
    o.select_set(False)
bpy.context.view_layer.objects.active = obj; obj.select_set(True)
bpy.ops.object.bake(type="EMIT")

# eingebrannte Textur einsetzen, Hilfsknoten und -daten entfernen
nt.links.new(t_z.outputs["Color"], bsdf.inputs["Base Color"])
nt.links.new(alt_link, out.inputs["Surface"])
for n in (t_p, uvn, attr, mul, mix, emi, orig):
    nt.nodes.remove(n)
ziel.pack()
me.uv_layers.remove(me.uv_layers["Projektion"])
me.color_attributes.remove(me.color_attributes["Maske"])
bpy.ops.export_scene.gltf(filepath=AUS, export_format="GLB", use_selection=True)
print("EXPORT", AUS)

if VORSCHAU:
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x = sc.render.resolution_y = 800
    w = bpy.data.worlds.new("w"); w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.13, 0.14, 0.12, 1)
    w.node_tree.nodes["Background"].inputs[1].default_value = 2.0
    sc.world = w
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 4; sun.rotation_euler = (math.radians(50), 0, math.radians(25)); sc.collection.objects.link(sun)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.type = "ORTHO"
    mitte = Vector((mitte_x, 0, (kopf_oben + kinn) / 2))
    for name, ang, scale in [("gesicht", 0, kopf_h * 1.35), ("gesicht_dreiviertel", 35, kopf_h * 1.35),
                             ("ganz", 0, H * 1.1)]:
        a = math.radians(ang)
        ziel_p = mitte if name != "ganz" else Vector((mitte_x, 0, (zmax + zmin) / 2))
        cam.data.ortho_scale = scale
        cam.location = ziel_p + Vector((math.sin(a), -math.cos(a), 0)) * 2
        cam.rotation_euler = (ziel_p - cam.location).to_track_quat("-Z", "Y").to_euler()
        sc.render.filepath = f"{VORSCHAU}/{name}.png"
        bpy.ops.render.render(write_still=True)
