# Rendert die Alex-Büste (FBX) von vorne und schräg, für Vorschau und Freigabe.
#   blender -b --factory-startup --python render_bust.py -- <fbx> <ausgabe-präfix>
import bpy, sys, math, mathutils
fbx, out = sys.argv[-2], sys.argv[-1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
sc = bpy.context.scene
mesh = [o for o in bpy.data.objects if o.type == "MESH"][0]
bb = [mesh.matrix_world @ mathutils.Vector(c) for c in mesh.bound_box]
ctr = sum(bb, mathutils.Vector()) / 8
h = max(v.z for v in bb) - min(v.z for v in bb)
for loc, en in (((1, -2, 2), 400), ((-2, -1, 1), 150), ((0, 2, 2), 200)):
    l = bpy.data.lights.new("l", "AREA"); l.energy = en; l.size = 2
    lo = bpy.data.objects.new("l", l); lo.location = ctr + mathutils.Vector(loc)
    lo.rotation_euler = (ctr - lo.location).to_track_quat('-Z', 'Y').to_euler(); sc.collection.objects.link(lo)
engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in engines else 'BLENDER_EEVEE'
sc.render.resolution_x, sc.render.resolution_y = 640, 800
sc.world = bpy.data.worlds.new("w"); sc.world.color = (0.05, 0.045, 0.04)
for name, yaw in (("front", 0), ("side", 35)):
    cam = bpy.data.cameras.new("c"); co = bpy.data.objects.new("c", cam); sc.collection.objects.link(co)
    r = h * 2.2; a = math.radians(yaw)
    co.location = ctr + mathutils.Vector((math.sin(a) * r, -math.cos(a) * r, h * 0.1))
    co.rotation_euler = (ctr + mathutils.Vector((0, 0, h * 0.12)) - co.location).to_track_quat('-Z', 'Y').to_euler()
    cam.lens = 70; sc.camera = co
    sc.frame_set(1); sc.render.filepath = f"{out}_{name}.png"; bpy.ops.render.render(write_still=True)
