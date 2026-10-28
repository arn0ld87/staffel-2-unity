#!/usr/bin/env python3
"""
Interaktive Live-3D-Modellierung von Alex (Ganzkörper, 1,80 m) im sichtbaren Blender-GUI.
Baut auf Basis von Alex_Bueste_KopfOriginal.fbx und alexander-ganz-zeigt.webp
Schritt für Schritt vor den Augen des Zuschauers:
  1. Referenz-Silhouette & Studio-Setup
  2. Kopf-/Schulter-Büste + Mundraum + 6 Gesichts-Shape-Keys auf 1,80 m Lebensgröße (S=0.60, Z=1.20..1.80 m)
  3. 3D-Körper-Textur (Alex_Body_BaseColor.png, 1024x1024, Vorder- & Rückseite) + Torso/Hoodie
  4. 3D-Kängurutasche & Hoodie-Saumdetails
  5. Linker Arm & Hand in der Hosentasche
  6. Rechter angewinkelter Zeigearm + voll ausmodellierte zeigende 3D-Hand
  7. Becken & beide Jeans-Beine (exakte A-Pose-Silhouette aus der Vorlage)
  8. Weiße 3D-Sneaker (links & rechts, exakt auf Z=0.00 m)
  9. Skelett-Rig (9 Bones), Skinning & 3 Animationen (Alex_Idle, Alex_Explain, Alex_Listening)
 10. Export nach Game/Assets/Art/Alex/Alex_Bueste.fbx & Alex_Ganzkoerper.blend
     + Endlos-Live-Animation im offenen Blender-Viewport.
"""

from __future__ import annotations

import bmesh
import bpy
import math
import os
from mathutils import Euler, Matrix, Vector
import numpy as np

PROJEKT_WURZEL = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
ART_ORDNER = os.path.join(PROJEKT_WURZEL, "Game/Assets/Art/Alex")
# Quelldateien liegen außerhalb von Assets/, damit Unity sie nicht importiert (.blend bräuchte ein installiertes Blender).
QUELL_ORDNER = os.path.join(PROJEKT_WURZEL, "art-source/alex")
ORIGINAL_FBX = os.path.join(QUELL_ORDNER, "Alex_Bueste_KopfOriginal.fbx")
ZIEL_FBX = os.path.join(ART_ORDNER, "Alex_Bueste.fbx")
ZIEL_BLEND = os.path.join(QUELL_ORDNER, "Alex_Ganzkoerper.blend")
KOERPER_TEXTUR_PFAD = os.path.join(ART_ORDNER, "Alex_Body_BaseColor.png")
KOPF_TEXTUR_PFAD = os.path.join(ART_ORDNER, "Alex_BaseColor.png")
# Zeichnung als Proportionsvorlage; nicht im Repo. Pfad per Umgebungsvariable ALEX_VORLAGE.
WEBP_VORLAGE = os.environ.get("ALEX_VORLAGE", os.path.join(QUELL_ORDNER, "alexander-ganz-zeigt.webp"))
DOCS_RENDER = os.path.join(PROJEKT_WURZEL, "docs/alex/ganzkoerper_uebersicht.png")

# Exakte Kalibrierung zwischen Alex_Bueste_KopfOriginal.fbx (ly = 0..1) und alexander-ganz-zeigt.webp:
# Bei S = 0.60, Z_OFF = 1.200 m, X_OFF = +0.018 m hat der Kopf bei Z = 1.65 m exakt 0.242 m Breite
# (Vorlage: 0.250 m) und reicht vom Brustkorb (Z = 1.20 m) bis zum Scheitel (Z = 1.80 m).
BUESTE_SKALIERUNG = 0.60
BUESTE_Z_OFFSET = 1.200
BUESTE_X_OFFSET = 0.018


def log(msg: str) -> None:
    print(f"[Alex-Live-3D] {msg}", flush=True)


def aktualisiere_banner(titel: str, untertitel: str = "") -> None:
    txt_obj = bpy.data.objects.get("Live_Status_Text")
    if txt_obj is None:
        curve = bpy.data.curves.new(name="Live_Status_Curve", type="FONT")
        curve.align_x = "CENTER"
        curve.align_y = "BOTTOM"
        curve.size = 0.072
        curve.extrude = 0.003
        txt_obj = bpy.data.objects.new("Live_Status_Text", curve)
        bpy.context.scene.collection.objects.link(txt_obj)
        txt_obj.location = (0.35, -0.05, 1.92)
        txt_obj.rotation_euler = (math.pi / 2.0, 0.0, 0.0)

        mat = bpy.data.materials.get("Live_Banner_Mat")
        if mat is None:
            mat = bpy.data.materials.new("Live_Banner_Mat")
            mat.use_nodes = True
            bsdf = mat.node_tree.nodes.get("Principled BSDF")
            if bsdf:
                bsdf.inputs["Base Color"].default_value = (0.20, 0.86, 1.0, 1.0)
                if "Emission Color" in bsdf.inputs:
                    bsdf.inputs["Emission Color"].default_value = (0.20, 0.86, 1.0, 1.0)
                if "Emission Strength" in bsdf.inputs:
                    bsdf.inputs["Emission Strength"].default_value = 1.8
        txt_obj.data.materials.append(mat)

    volltext = titel if not untertitel else f"{titel}\n{untertitel}"
    txt_obj.data.body = volltext


def richte_viewport_ein(
    ziel: tuple[float, float, float] = (0.32, 0.0, 0.95),
    distanz: float = 2.85,
    yaw_grad: float = 12.0,
    pitch_grad: float = 80.0,
    zeige_knochen: bool = False,
) -> None:
    bpy.ops.object.select_all(action="DESELECT")
    wm = bpy.context.window_manager
    if not wm:
        return
    rot = Euler((math.radians(pitch_grad), 0.0, math.radians(yaw_grad)), "XYZ").to_matrix().to_quaternion()
    for win in wm.windows:
        for area in win.screen.areas:
            if area.type == "VIEW_3D":
                for space in area.spaces:
                    if space.type == "VIEW_3D":
                        space.shading.type = "MATERIAL"
                        space.shading.use_scene_lights = True
                        space.shading.use_scene_world = False
                        space.overlay.show_floor = True
                        space.overlay.show_axis_x = True
                        space.overlay.show_axis_y = True
                        space.overlay.show_cursor = False
                        space.overlay.show_extras = False
                        space.overlay.show_relationship_lines = False
                        space.overlay.show_outline_selected = False
                        space.overlay.show_bones = zeige_knochen
                        r3d = space.region_3d
                        if r3d:
                            r3d.view_perspective = "PERSP"
                            r3d.view_location = Vector(ziel)
                            r3d.view_distance = distanz
                            r3d.view_rotation = rot
                area.tag_redraw()


def erzeuge_koerper_textur() -> bpy.types.Image:
    log("Generiere Körper-Textur Alex_Body_BaseColor.png ...")
    src_img = bpy.data.images.load(WEBP_VORLAGE, check_existing=True)
    sw, sh = src_img.size
    src = np.array(src_img.pixels[:], dtype=np.float32).reshape((sh, sw, 4))

    H, W = 1024, 1024
    half_w = W // 2
    x_mid_front = int(0.48 * half_w)

    x_idx = np.clip((np.arange(half_w, dtype=np.float32) / half_w * sw).astype(np.int32), 0, sw - 1)
    front = np.zeros((H, half_w, 4), dtype=np.float32)
    for y in range(H):
        v = y / float(H)
        if v < 0.25:
            v_l = 0.022 + v * (0.228 / 0.25)
            v_r = 0.052 + v * (0.198 / 0.25)
        else:
            v_l = v
            v_r = v
        yl = int(np.clip(v_l * sh, 0, sh - 1))
        yr = int(np.clip(v_r * sh, 0, sh - 1))
        front[y, :x_mid_front] = src[yl, x_idx[:x_mid_front]]
        front[y, x_mid_front:] = src[yr, x_idx[x_mid_front:]]

    hoodie_rgb = np.array([0.1804, 0.1765, 0.1686], dtype=np.float32)
    jeans_rgb = np.array([0.205, 0.305, 0.435], dtype=np.float32)
    shoe_rgb = np.array([0.88, 0.88, 0.87], dtype=np.float32)

    for y in range(H):
        v = y / float(H)
        row = front[y]
        opaque = np.where(row[:, 3] > 0.65)[0]
        if len(opaque) >= 4:
            left_edge = opaque[min(2, len(opaque) - 1)]
            right_edge = opaque[max(0, len(opaque) - 3)]
            left_col = row[left_edge, :3].copy()
            right_col = row[right_edge, :3].copy()
            for x in range(0, left_edge):
                dist = (left_edge - x) / max(1.0, float(left_edge))
                row[x, :3] = left_col * (1.0 - 0.12 * dist)
                row[x, 3] = 1.0
            for x in range(right_edge + 1, half_w):
                dist = (x - right_edge) / max(1.0, float(half_w - right_edge))
                row[x, :3] = right_col * (1.0 - 0.12 * dist)
                row[x, 3] = 1.0
            # Lücken zwischen Beinen mit der jeweils nächsten Bein-Randfarbe füllen
            inner_trans = np.where(row[:, 3] <= 0.65)[0]
            if len(inner_trans) > 0:
                for x in inner_trans:
                    nearest_op = opaque[np.argmin(np.abs(opaque - x))]
                    row[x, :3] = row[nearest_op, :3] * 0.90
                    row[x, 3] = 1.0
        else:
            fallback = hoodie_rgb if v > 0.48 else (jeans_rgb if v > 0.08 else shoe_rgb)
            row[:, :3] = fallback
            row[:, 3] = 1.0

    # Saubere weiße Sneaker-Zone (v < 0.066, entspricht Z < 0.119 m):
    # Behält die gezeichneten weiß-grauen Sneaker-Details aus dem WebP, ersetzt aber
    # herunterragende blaue Jeans-Pixel oder dicke schwarze Außenkonturen durch Sneaker-Weiß.
    xx_front = np.linspace(-1.0, 1.0, half_w, dtype=np.float32)
    shade_front = 1.0 - 0.10 * (xx_front ** 2)
    for y in range(int(0.066 * H)):
        v = y / float(H)
        row = front[y]
        r, g, b = row[:, 0], row[:, 1], row[:, 2]
        lum = 0.299 * r + 0.587 * g + 0.114 * b
        is_jeans_or_dark = (b - r > 0.05) | (lum < 0.58)
        base_val = 0.77 if v < 0.016 else (0.68 if abs(v - 0.017) < 0.003 else 0.91)
        for c in range(3):
            clean_shoe = np.clip(base_val * shade_front, 0.65, 0.95)
            row[:, c] = np.where(is_jeans_or_dark, clean_shoe, row[:, c])
        row[:, 3] = 1.0

    # Rechte Hälfte (u = 0.5 .. 1.0): Comic-Rückseite
    back = np.ones((H, half_w, 4), dtype=np.float32)
    xx = np.linspace(-1.0, 1.0, half_w, dtype=np.float32)[None, :]
    vv = np.linspace(0.0, 1.0, H, dtype=np.float32)[:, None]

    shade = 1.0 - 0.15 * (xx ** 2)
    folds = 0.016 * np.sin(xx * 9.0 + vv * 14.0)

    hoodie_mask = (vv >= 0.485)
    jeans_mask = (vv >= 0.072) & (vv < 0.485)
    shoe_mask = (vv < 0.072)

    for c in range(3):
        col_map = (
            hoodie_mask * (hoodie_rgb[c] * shade + folds)
            + jeans_mask * (jeans_rgb[c] * shade + 0.5 * folds)
            + shoe_mask * (shoe_rgb[c] * shade)
        )
        back[:, :, c] = np.clip(col_map, 0.04, 0.96)

    seam_mask = (vv >= 0.36) & (vv < 0.485) & (np.abs(xx) < 0.022)
    pocket_l = (vv >= 0.39) & (vv <= 0.46) & (np.abs(np.abs(xx + 0.28) - 0.13) < 0.014)
    pocket_r = (vv >= 0.39) & (vv <= 0.46) & (np.abs(np.abs(xx - 0.28) - 0.13) < 0.014)
    for c in range(3):
        back[:, :, c] = np.where(seam_mask | pocket_l | pocket_r, back[:, :, c] * 0.70, back[:, :, c])

    full = np.concatenate([front, back], axis=1)

    img = bpy.data.images.get("Alex_Body_BaseColor")
    if img is not None:
        bpy.data.images.remove(img)
    img = bpy.data.images.new("Alex_Body_BaseColor", width=W, height=H, alpha=True)
    img.pixels.foreach_set(full.ravel())
    img.filepath_raw = KOERPER_TEXTUR_PFAD
    img.file_format = "PNG"
    img.save()
    img.pack()
    return img


def erzeuge_koerper_material(img: bpy.types.Image) -> bpy.types.Material:
    mat = bpy.data.materials.get("Alex_Body_Mat")
    if mat is None:
        mat = bpy.data.materials.new("Alex_Body_Mat")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()

    out = nt.nodes.new("ShaderNodeOutputMaterial")
    out.location = (300, 0)
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.location = (0, 0)
    bsdf.inputs["Roughness"].default_value = 0.92
    if "Specular IOR Level" in bsdf.inputs:
        bsdf.inputs["Specular IOR Level"].default_value = 0.08

    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.location = (-320, 0)
    tex.image = img

    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


def weise_projektions_uvs_zu(obj: bpy.types.Object) -> None:
    """Projiziert Welt-(X, Z) exakt auf die WebP-Vorderseite (u=0..0.5) bzw. Rückseite (u=0.5..1.0)."""
    mesh = obj.data
    if not mesh.uv_layers:
        mesh.uv_layers.new(name="UVMap")
    uv_layer = mesh.uv_layers.active.data
    mw = obj.matrix_world

    for poly in mesh.polygons:
        n_world = (mw.to_3x3() @ poly.normal).normalized()
        ist_rueckseite = (n_world.y > 0.12)
        for loop_idx in poly.loop_indices:
            v_idx = mesh.loops[loop_idx].vertex_index
            wco = mw @ mesh.vertices[v_idx].co
            px_norm = max(0.015, min(0.985, (152.0 + wco.x * 355.555) / 319.0))
            v = max(0.008, min(0.992, wco.z / 1.80))
            u = (0.5 + px_norm * 0.5) if ist_rueckseite else (px_norm * 0.5)
            uv_layer[loop_idx].uv = (u, v)


def setze_vertex_gruppe(obj: bpy.types.Object, bone_name: str, gewicht: float = 1.0) -> None:
    vg = obj.vertex_groups.get(bone_name) or obj.vertex_groups.new(name=bone_name)
    alle_ids = list(range(len(obj.data.vertices)))
    if alle_ids:
        vg.add(alle_ids, gewicht, "REPLACE")


def baue_loft_mesh(
    name: str,
    profile: list[tuple[float, float, float, float, float]],
    segmente: int = 40,
    deckel_unten: bool = False,
    deckel_oben: bool = False,
    falten_amp: float = 0.0,
) -> bpy.types.Object:
    bm = bmesh.new()
    ringe: list[list[bmesh.types.BMVert]] = []

    for cx, cy, z, rx, ry in profile:
        ring: list[bmesh.types.BMVert] = []
        for s in range(segmente):
            phi = (2.0 * math.pi * s) / segmente
            mod = 1.0 + (falten_amp * math.sin(6.0 * phi + z * 16.0) if falten_amp > 0.0 else 0.0)
            x = cx + rx * mod * math.cos(phi)
            y = cy + ry * mod * math.sin(phi)
            ring.append(bm.verts.new((x, y, z)))
        ringe.append(ring)

    for r_idx in range(len(ringe) - 1):
        r0, r1 = ringe[r_idx], ringe[r_idx + 1]
        for s in range(segmente):
            sn = (s + 1) % segmente
            bm.faces.new((r0[s], r0[sn], r1[sn], r1[s]))

    if deckel_unten and ringe:
        c0 = profile[0]
        v_center = bm.verts.new((c0[0], c0[1], c0[2]))
        for s in range(segmente):
            sn = (s + 1) % segmente
            bm.faces.new((ringe[0][sn], ringe[0][s], v_center))

    if deckel_oben and ringe:
        c1 = profile[-1]
        v_center = bm.verts.new((c1[0], c1[1], c1[2]))
        for s in range(segmente):
            sn = (s + 1) % segmente
            bm.faces.new((ringe[-1][s], ringe[-1][sn], v_center))

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    for poly in mesh.polygons:
        poly.use_smooth = True

    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def baue_pfad_roehre(
    name: str,
    punkte_radien: list[tuple[Vector, float, float]],
    segmente: int = 24,
    kappe_start: bool = True,
    kappe_ende: bool = True,
) -> bpy.types.Object:
    bm = bmesh.new()
    ringe: list[list[bmesh.types.BMVert]] = []
    n_pts = len(punkte_radien)

    for i, (pos, rx, ry) in enumerate(punkte_radien):
        if i == 0:
            tangent = (punkte_radien[1][0] - pos).normalized()
        elif i == n_pts - 1:
            tangent = (pos - punkte_radien[i - 1][0]).normalized()
        else:
            tangent = (punkte_radien[i + 1][0] - punkte_radien[i - 1][0]).normalized()

        ref = Vector((0.0, 1.0, 0.0)) if abs(tangent.y) < 0.85 else Vector((1.0, 0.0, 0.0))
        binorm = tangent.cross(ref).normalized()
        norm = binorm.cross(tangent).normalized()

        ring: list[bmesh.types.BMVert] = []
        for s in range(segmente):
            phi = (2.0 * math.pi * s) / segmente
            lokal = binorm * (rx * math.cos(phi)) + norm * (ry * math.sin(phi))
            ring.append(bm.verts.new(pos + lokal))
        ringe.append(ring)

    for i in range(n_pts - 1):
        r0, r1 = ringe[i], ringe[i + 1]
        for s in range(segmente):
            sn = (s + 1) % segmente
            bm.faces.new((r0[s], r0[sn], r1[sn], r1[s]))

    if kappe_start and ringe:
        v_c = bm.verts.new(punkte_radien[0][0])
        for s in range(segmente):
            sn = (s + 1) % segmente
            bm.faces.new((ringe[0][sn], ringe[0][s], v_c))

    if kappe_ende and ringe:
        v_c = bm.verts.new(punkte_radien[-1][0])
        for s in range(segmente):
            sn = (s + 1) % segmente
            bm.faces.new((ringe[-1][s], ringe[-1][sn], v_c))

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    for poly in mesh.polygons:
        poly.use_smooth = True

    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


class AlexLiveBuilder:
    def __init__(self) -> None:
        self.schritt = 0
        self.koerper_teile: list[bpy.types.Object] = []
        self.koerper_img: bpy.types.Image | None = None
        self.koerper_mat: bpy.types.Material | None = None
        self.bust_obj: bpy.types.Object | None = None
        self.mund_obj: bpy.types.Object | None = None
        self.rig_obj: bpy.types.Object | None = None
        self.body_obj: bpy.types.Object | None = None
        self.endlos_zeit = 0.0

    def schritt_1_szene_und_referenz(self) -> None:
        log("Schritt 1/10: Bereinige Szene & stelle Studio-Licht + Referenztafel auf ...")
        for obj in list(bpy.data.objects):
            bpy.data.objects.remove(obj, do_unlink=True)

        scene = bpy.context.scene
        scene.unit_settings.system = "METRIC"
        scene.unit_settings.scale_length = 1.0

        def erstelle_licht(name: str, typ: str, energie: float, farbe: tuple[float, float, float], ort: tuple[float, float, float], rot_grad: tuple[float, float, float]) -> None:
            ldata = bpy.data.lights.new(name=name, type=typ)
            ldata.energy = energie
            ldata.color = farbe
            lobj = bpy.data.objects.new(name, ldata)
            scene.collection.objects.link(lobj)
            lobj.location = ort
            lobj.rotation_euler = tuple(math.radians(g) for g in rot_grad)

        erstelle_licht("Studio_KeyLight", "AREA", 180.0, (1.0, 0.97, 0.92), (1.4, -2.2, 2.1), (55.0, 0.0, 28.0))
        erstelle_licht("Studio_FillLight", "AREA", 95.0, (0.85, 0.93, 1.0), (-1.6, -1.8, 1.5), (65.0, 0.0, -35.0))
        erstelle_licht("Studio_RimLight", "SPOT", 160.0, (0.55, 0.82, 1.0), (0.0, 2.0, 2.3), (-55.0, 0.0, 180.0))

        bpy.ops.mesh.primitive_cylinder_add(vertices=48, radius=0.48, depth=0.02, location=(0.0, 0.0, -0.01))
        podest = bpy.context.active_object
        podest.name = "Studio_Podest"
        pmat = bpy.data.materials.new("Studio_Podest_Mat")
        pmat.use_nodes = True
        pbsdf = pmat.node_tree.nodes.get("Principled BSDF")
        if pbsdf:
            pbsdf.inputs["Base Color"].default_value = (0.09, 0.12, 0.16, 1.0)
            pbsdf.inputs["Roughness"].default_value = 0.35
        podest.data.materials.append(pmat)

        # Referenz-Ebene rechts neben dem Ursprung (X = +0.78 m) mit exakter 1.80 m Höhe (size=1.0 -> Kantenlänge 1.0 m)
        ref_w = 1.80 * (319.0 / 640.0)
        bpy.ops.mesh.primitive_plane_add(size=1.0, location=(0.78, 0.08, 0.90), rotation=(math.pi / 2.0, 0.0, 0.0))
        ref_plane = bpy.context.active_object
        ref_plane.name = "Referenz_Vorlage"
        ref_plane.scale = (ref_w, 1.80, 1.0)

        ref_mat = bpy.data.materials.new("Referenz_Vorlage_Mat")
        ref_mat.use_nodes = True
        if hasattr(ref_mat, "blend_method"):
            ref_mat.blend_method = "CLIP"
        rnt = ref_mat.node_tree
        rbsdf = rnt.nodes.get("Principled BSDF")
        rtex = rnt.nodes.new("ShaderNodeTexImage")
        rtex.image = bpy.data.images.load(WEBP_VORLAGE, check_existing=True)
        if rbsdf:
            rnt.links.new(rtex.outputs["Color"], rbsdf.inputs["Base Color"])
            if "Alpha" in rbsdf.inputs:
                rnt.links.new(rtex.outputs["Alpha"], rbsdf.inputs["Alpha"])
            rbsdf.inputs["Roughness"].default_value = 1.0
        ref_plane.data.materials.append(ref_mat)

        aktualisiere_banner(
            "LIVE-BUILD 1/10: Studio & Vorlage (1,80 m)",
            "Vorlage rechts: alexander-ganz-zeigt.webp",
        )
        richte_viewport_ein(ziel=(0.35, 0.0, 0.95), distanz=3.0, yaw_grad=10.0, pitch_grad=80.0)

    def schritt_2_kopf_bueste_lebensgross(self) -> None:
        log("Schritt 2/10: Importiere Alex_Bueste & skaliere samt Shape-Keys auf 1,80 m (S=0.60, Z=1.20..1.80 m) ...")
        quell_fbx = ORIGINAL_FBX if os.path.exists(ORIGINAL_FBX) else ZIEL_FBX
        bpy.ops.import_scene.fbx(filepath=quell_fbx)

        self.bust_obj = bpy.data.objects.get("Alex_Bust")
        self.mund_obj = bpy.data.objects.get("Alex_Mundraum")
        self.rig_obj = bpy.data.objects.get("Alex_Rig")

        if self.rig_obj:
            self.rig_obj.show_in_front = False

        # Skaliere Mesh + alle Shape-Keys im lokalen Raum:
        # Unten an den äußeren Schulterkanten (ly < 0.35) verjüngen wir X sanft, damit die Büste
        # nahtlos in die Torso-Silhouette (rx=0.22 m) übergeht.
        for mobj in (self.bust_obj, self.mund_obj):
            if mobj is None or mobj.type != "MESH":
                continue
            mesh = mobj.data
            Basis_ly = np.zeros(len(mesh.vertices), dtype=np.float32)
            for idx, v in enumerate(mesh.vertices):
                Basis_ly[idx] = v.co.y
            # Unterhalb des Kapuzenkragens (ly < 0.36 -> Z < 1.416 m) verjüngen wir X und Z sanft nach innen,
            # damit Alex_Torso (Z = 0.85..1.43 m) den Brust-/Armbereich aus einem Guss übernimmt.
            t_collar = np.clip((Basis_ly - 0.10) / 0.28, 0.0, 1.0).astype(np.float32)
            taper_x = 0.52 + 0.48 * t_collar
            taper_z = 0.45 + 0.55 * t_collar

            if mesh.shape_keys and mesh.shape_keys.key_blocks:
                for kb in mesh.shape_keys.key_blocks:
                    n = len(kb.data)
                    co = np.zeros(n * 3, dtype=np.float32)
                    kb.data.foreach_get("co", co)
                    pts = co.reshape((-1, 3))
                    pts[:, 0] = pts[:, 0] * BUESTE_SKALIERUNG * taper_x + BUESTE_X_OFFSET
                    pts[:, 1] = pts[:, 1] * BUESTE_SKALIERUNG + BUESTE_Z_OFFSET
                    pts[:, 2] *= BUESTE_SKALIERUNG * 0.86 * taper_z
                    kb.data.foreach_set("co", pts.ravel())
                basis_co = np.zeros(len(mesh.vertices) * 3, dtype=np.float32)
                mesh.shape_keys.key_blocks[0].data.foreach_get("co", basis_co)
                mesh.vertices.foreach_set("co", basis_co)
            else:
                n = len(mesh.vertices)
                co = np.zeros(len(mesh.vertices) * 3, dtype=np.float32)
                mesh.vertices.foreach_get("co", co)
                pts = co.reshape((-1, 3))
                pts[:, 0] = pts[:, 0] * BUESTE_SKALIERUNG * taper_x + BUESTE_X_OFFSET
                pts[:, 1] = pts[:, 1] * BUESTE_SKALIERUNG + BUESTE_Z_OFFSET
                pts[:, 2] *= BUESTE_SKALIERUNG * 0.86 * taper_z
                mesh.vertices.foreach_set("co", pts.ravel())
            mesh.update()

        if self.bust_obj and os.path.exists(KOPF_TEXTUR_PFAD):
            kopf_img = bpy.data.images.load(KOPF_TEXTUR_PFAD, check_existing=True)
            for mat in self.bust_obj.data.materials:
                if mat and mat.use_nodes:
                    for node in mat.node_tree.nodes:
                        if node.type == "TEX_IMAGE":
                            node.image = kopf_img

        aktualisiere_banner(
            "LIVE-BUILD 2/10: Kopf-Bueste & 6 Shape-Keys (Z = 1.20..1.80 m)",
            "Exakte Kopf- und Schulterbreite wie in der Vorlage",
        )
        richte_viewport_ein(ziel=(0.25, 0.0, 1.48), distanz=1.65, yaw_grad=14.0, pitch_grad=82.0)

    def schritt_3_koerper_textur_und_torso(self) -> None:
        log("Schritt 3/10: Erzeuge Körpertextur & modelliere 3D-Torso (Hoodie) ...")
        self.koerper_img = erzeuge_koerper_textur()
        self.koerper_mat = erzeuge_koerper_material(self.koerper_img)

        # Torso-Profile von Z = 0.85 m bis Z = 1.435 m (reicht bis direkt unter den Kapuzenkragen von Alex_Bust)
        torso_profile = [
            (0.032,  0.004, 0.850, 0.175, 0.095),  # Unterkante Hoodie-Bund
            (0.038,  0.003, 0.895, 0.188, 0.102),  # Hoodie-Bund Oberkante
            (0.048,  0.000, 0.960, 0.202, 0.110),  # Bauch / Kängurutasche unten
            (0.052, -0.002, 1.040, 0.210, 0.114),  # Bauchmitte
            (0.045, -0.002, 1.130, 0.218, 0.116),  # Oberbauch / angewinkelter Unterarm
            (0.030, -0.001, 1.220, 0.222, 0.116),  # Untere Brust
            (0.015,  0.000, 1.310, 0.215, 0.114),  # Obere Brust & Kordelbereich
            (0.008,  0.002, 1.385, 0.185, 0.102),  # Schulteransatz
            (0.010,  0.004, 1.435, 0.135, 0.085),  # Nahtlos unter dem Kapuzenkragen von Alex_Bust
        ]
        torso = baue_loft_mesh(
            "Alex_Torso",
            torso_profile,
            segmente=48,
            deckel_unten=True,
            deckel_oben=True,
            falten_amp=0.010,
        )
        torso.data.materials.append(self.koerper_mat)
        weise_projektions_uvs_zu(torso)
        setze_vertex_gruppe(torso, "Spine", 1.0)
        self.koerper_teile.append(torso)

        aktualisiere_banner(
            "LIVE-BUILD 3/10: 3D-Torso & Hoodie (Z = 0.85..1.23 m)",
            "Nahtloser Uebergang in die Brust-Bueste",
        )
        richte_viewport_ein(ziel=(0.25, 0.0, 1.18), distanz=2.2, yaw_grad=-16.0, pitch_grad=79.0)

    def schritt_4_kaengurutasche_und_kordeln(self) -> None:
        log("Schritt 4/10: Modelliere plastische 3D-Kängurutasche ...")
        tasche_profile = [
            (0.025, -0.095, 0.915, 0.125, 0.022),
            (0.025, -0.104, 0.960, 0.132, 0.026),
            (0.025, -0.106, 1.020, 0.118, 0.026),
            (0.025, -0.100, 1.075, 0.096, 0.020),
        ]
        tasche = baue_loft_mesh(
            "Alex_Kaengurutasche",
            tasche_profile,
            segmente=32,
            deckel_unten=True,
            deckel_oben=True,
            falten_amp=0.006,
        )
        tasche.data.materials.append(self.koerper_mat)
        weise_projektions_uvs_zu(tasche)
        setze_vertex_gruppe(tasche, "Spine", 1.0)
        self.koerper_teile.append(tasche)

        aktualisiere_banner(
            "LIVE-BUILD 4/10: Plastische 3D-Kaengurutasche",
            "3D-Relief auf der Hoodie-Vorderseite",
        )
        richte_viewport_ein(ziel=(0.20, -0.05, 1.12), distanz=1.85, yaw_grad=14.0, pitch_grad=80.0)

    def schritt_5_linker_arm_und_taschenhand(self) -> None:
        log("Schritt 5/10: Modelliere linken Unterarm (+X) & Hand in der Hosentasche ...")
        # Linker Arm an der rechten Silhouettenseite (+X = +0.20..+0.26 m, Z = 0.86..1.22 m)
        arm_l_pts = [
            (Vector((0.205,  0.005, 1.220)), 0.052, 0.048),
            (Vector((0.238,  0.008, 1.120)), 0.050, 0.046),
            (Vector((0.242,  0.002, 1.010)), 0.046, 0.044),
            (Vector((0.215, -0.012, 0.920)), 0.040, 0.038),
            (Vector((0.195, -0.022, 0.875)), 0.032, 0.028),
        ]
        arm_l = baue_pfad_roehre("Alex_Arm_L", arm_l_pts, segmente=26, kappe_start=True, kappe_ende=True)
        arm_l.data.materials.append(self.koerper_mat)
        weise_projektions_uvs_zu(arm_l)
        setze_vertex_gruppe(arm_l, "Arm_L", 1.0)
        self.koerper_teile.append(arm_l)

        aktualisiere_banner(
            "LIVE-BUILD 5/10: Linker Arm & Taschen-Hand",
            "Laessig in die Hosentasche greifend",
        )
        richte_viewport_ein(ziel=(0.25, 0.0, 1.10), distanz=2.2, yaw_grad=24.0, pitch_grad=79.0)

    def schritt_6_rechter_zeigearm_und_3d_hand(self) -> None:
        log("Schritt 6/10: Modelliere rechten Zeigearm + zeigende 3D-Hand (X=+0.15..+0.38 m, Z=1.30..1.46 m) ...")
        # Zeigender Unterarm + Ärmelbündchen + voll ausmodellierte 3D-Zeigehand exakt an den WebP-Koordinaten
        unterarm_r_pts = [
            (Vector((0.115, -0.055, 1.285)), 0.048, 0.042),
            (Vector((0.158, -0.058, 1.325)), 0.045, 0.040),
            (Vector((0.198, -0.058, 1.358)), 0.040, 0.036),
        ]
        unterarm_r = baue_pfad_roehre("Alex_Unterarm_R", unterarm_r_pts, segmente=24, kappe_start=True, kappe_ende=True)
        unterarm_r.data.materials.append(self.koerper_mat)
        weise_projektions_uvs_zu(unterarm_r)
        setze_vertex_gruppe(unterarm_r, "Forearm_R", 1.0)
        self.koerper_teile.append(unterarm_r)

        hand_bauteile: list[bpy.types.Object] = []
        # Mittelhand (Palm)
        palm_pts = [
            (Vector((0.198, -0.058, 1.358)), 0.036, 0.025),
            (Vector((0.242, -0.058, 1.392)), 0.042, 0.026),
            (Vector((0.285, -0.058, 1.418)), 0.036, 0.022),
        ]
        hand_bauteile.append(baue_pfad_roehre("Alex_Hand_R_Palm", palm_pts, segmente=22, kappe_start=True, kappe_ende=True))

        # Ausgestreckter Zeigefinger bis X = +0.380 m, Z = 1.462 m
        index_pts = [
            (Vector((0.282, -0.058, 1.425)), 0.015, 0.013),
            (Vector((0.320, -0.058, 1.442)), 0.013, 0.012),
            (Vector((0.352, -0.058, 1.454)), 0.011, 0.010),
            (Vector((0.380, -0.058, 1.462)), 0.009, 0.009),
        ]
        hand_bauteile.append(baue_pfad_roehre("Alex_Hand_R_Index", index_pts, segmente=16, kappe_start=True, kappe_ende=True))

        # Daumen
        thumb_pts = [
            (Vector((0.228, -0.060, 1.395)), 0.015, 0.013),
            (Vector((0.248, -0.062, 1.430)), 0.013, 0.012),
            (Vector((0.264, -0.062, 1.455)), 0.010, 0.010),
        ]
        hand_bauteile.append(baue_pfad_roehre("Alex_Hand_R_Thumb", thumb_pts, segmente=14, kappe_start=True, kappe_ende=True))

        # Gebeugte Finger (Mittel-, Ring-, kleiner Finger)
        for idx, dz in enumerate([-0.016, -0.032, -0.046]):
            z0 = 1.412 + dz
            f_pts = [
                (Vector((0.278, -0.056, z0)), 0.013, 0.011),
                (Vector((0.305, -0.062, z0 - 0.004)), 0.012, 0.010),
                (Vector((0.292, -0.070, z0 - 0.015)), 0.010, 0.009),
            ]
            hand_bauteile.append(baue_pfad_roehre(f"Alex_Hand_R_Finger_{idx}", f_pts, segmente=14, kappe_start=True, kappe_ende=True))

        for hb in hand_bauteile:
            hb.data.materials.append(self.koerper_mat)
            weise_projektions_uvs_zu(hb)
            setze_vertex_gruppe(hb, "Hand_R", 1.0)
            self.koerper_teile.append(hb)

        aktualisiere_banner(
            "LIVE-BUILD 6/10: Rechter Zeigearm & 3D-Zeigehand",
            "Exakt auf die WebP-Zeigegeste gemappt",
        )
        richte_viewport_ein(ziel=(0.26, -0.04, 1.36), distanz=1.55, yaw_grad=-12.0, pitch_grad=82.0)

    def schritt_7_becken_und_jeans_beine(self) -> None:
        log("Schritt 7/10: Modelliere Becken & beide Jeans-Beine in A-Pose (Z = 0.11..0.86 m) ...")
        # 1. Becken / Hüftbereich (Z = 0.72 .. 0.86 m)
        becken_profile = [
            (0.025, 0.004, 0.720, 0.185, 0.094),
            (0.030, 0.004, 0.770, 0.192, 0.098),
            (0.035, 0.004, 0.820, 0.188, 0.096),
            (0.035, 0.004, 0.865, 0.176, 0.092),
        ]
        becken = baue_loft_mesh(
            "Alex_Jeans_Becken",
            becken_profile,
            segmente=40,
            deckel_unten=True,
            deckel_oben=True,
            falten_amp=0.008,
        )
        becken.data.materials.append(self.koerper_mat)
        weise_projektions_uvs_zu(becken)
        setze_vertex_gruppe(becken, "Root", 1.0)
        self.koerper_teile.append(becken)

        # 2. Rechtes Hosenbein (links im Bild, X = -0.195 .. -0.075 m) – exakt nach WebP-Messung
        bein_r_profile = [
            (-0.192,  0.004, 0.105, 0.062, 0.062),
            (-0.187,  0.003, 0.150, 0.062, 0.063),
            (-0.170,  0.000, 0.250, 0.068, 0.068),
            (-0.158, -0.002, 0.350, 0.070, 0.070),
            (-0.142, -0.004, 0.450, 0.068, 0.068),
            (-0.114, -0.002, 0.550, 0.074, 0.074),
            (-0.089,  0.002, 0.650, 0.086, 0.082),
            (-0.068,  0.004, 0.745, 0.092, 0.086),
        ]
        bein_r = baue_loft_mesh(
            "Alex_Jeans_Bein_R",
            bein_r_profile,
            segmente=32,
            deckel_unten=True,
            deckel_oben=True,
            falten_amp=0.012,
        )
        bein_r.data.materials.append(self.koerper_mat)
        weise_projektions_uvs_zu(bein_r)
        setze_vertex_gruppe(bein_r, "Leg_R", 1.0)
        self.koerper_teile.append(bein_r)

        # 3. Linkes Hosenbein (rechts im Bild, X = +0.111 .. +0.149 m) – exakt nach WebP-Messung
        bein_l_profile = [
            (0.148,  0.004, 0.105, 0.064, 0.062),
            (0.142,  0.003, 0.150, 0.064, 0.063),
            (0.114,  0.000, 0.250, 0.064, 0.064),
            (0.117, -0.002, 0.350, 0.064, 0.064),
            (0.122, -0.004, 0.450, 0.066, 0.067),
            (0.124, -0.002, 0.550, 0.070, 0.070),
            (0.131,  0.002, 0.650, 0.080, 0.080),
            (0.118,  0.004, 0.745, 0.088, 0.084),
        ]
        bein_l = baue_loft_mesh(
            "Alex_Jeans_Bein_L",
            bein_l_profile,
            segmente=32,
            deckel_unten=True,
            deckel_oben=True,
            falten_amp=0.012,
        )
        bein_l.data.materials.append(self.koerper_mat)
        weise_projektions_uvs_zu(bein_l)
        setze_vertex_gruppe(bein_l, "Leg_L", 1.0)
        self.koerper_teile.append(bein_l)

        aktualisiere_banner(
            "LIVE-BUILD 7/10: Becken & Blaue Jeans-Beine (A-Pose)",
            "Deckungsgleich mit den Bein-Achsen der Vorlage",
        )
        richte_viewport_ein(ziel=(0.28, 0.0, 0.68), distanz=2.45, yaw_grad=14.0, pitch_grad=79.0)

    def schritt_8_weisse_3d_sneaker(self) -> None:
        log("Schritt 8/10: Modelliere beide weißen 3D-Sneaker (Z = 0.00..0.12 m) ...")
        # 1. Rechter Sneaker (links im Bild bei X = -0.220 m, zeigt leicht nach vorne-links)
        schuh_r_profile = [
            (-0.222, -0.035, 0.000, 0.064, 0.115),
            (-0.222, -0.035, 0.024, 0.066, 0.118),
            (-0.215, -0.028, 0.054, 0.064, 0.105),
            (-0.202, -0.010, 0.086, 0.058, 0.078),
            (-0.194,  0.002, 0.112, 0.054, 0.060),
        ]
        schuh_r = baue_loft_mesh("Alex_Sneaker_R", schuh_r_profile, segmente=32, deckel_unten=True, deckel_oben=True)
        schuh_r.data.materials.append(self.koerper_mat)
        weise_projektions_uvs_zu(schuh_r)
        setze_vertex_gruppe(schuh_r, "Leg_R", 1.0)
        self.koerper_teile.append(schuh_r)

        # 2. Linker Sneaker (rechts im Bild bei X = +0.180 m, zeigt schräg nach rechts-vorne wie im WebP)
        schuh_l_profile = [
            (0.185, -0.022, 0.000, 0.112, 0.068),
            (0.185, -0.022, 0.024, 0.114, 0.070),
            (0.172, -0.016, 0.052, 0.098, 0.065),
            (0.152, -0.006, 0.084, 0.066, 0.058),
            (0.144,  0.002, 0.112, 0.054, 0.052),
        ]
        schuh_l = baue_loft_mesh("Alex_Sneaker_L", schuh_l_profile, segmente=32, deckel_unten=True, deckel_oben=True)
        schuh_l.data.materials.append(self.koerper_mat)
        weise_projektions_uvs_zu(schuh_l)
        setze_vertex_gruppe(schuh_l, "Leg_L", 1.0)
        self.koerper_teile.append(schuh_l)

        aktualisiere_banner(
            "LIVE-BUILD 8/10: Weisse 3D-Sneaker (Z = 0.00..0.12 m)",
            "Steht exakt auf dem Boden (Z = 0.00 m)",
        )
        richte_viewport_ein(ziel=(0.25, 0.0, 0.55), distanz=2.25, yaw_grad=16.0, pitch_grad=78.0)

    def schritt_9_rigging_und_animationen(self) -> None:
        log("Schritt 9/10: Vereine Alex_Body, erweitere Alex_Rig (9 Bones) & backe 3 Animationen ...")
        bpy.ops.object.select_all(action="DESELECT")
        aktive_teile = [o for o in self.koerper_teile if o and o.name in bpy.data.objects]
        for o in aktive_teile:
            o.select_set(True)
        bpy.context.view_layer.objects.active = aktive_teile[0]
        bpy.ops.object.join()
        self.body_obj = bpy.context.active_object
        self.body_obj.name = "Alex_Body"
        self.body_obj.data.name = "Alex_Body_Mesh"

        if self.rig_obj is None:
            arm_data = bpy.data.armatures.new("Alex_Rig_Data")
            self.rig_obj = bpy.data.objects.new("Alex_Rig", arm_data)
            bpy.context.scene.collection.objects.link(self.rig_obj)

        bpy.ops.object.select_all(action="DESELECT")
        self.rig_obj.select_set(True)
        bpy.context.view_layer.objects.active = self.rig_obj
        bpy.ops.object.mode_set(mode="EDIT")
        ebones = self.rig_obj.data.edit_bones
        for b in list(ebones):
            ebones.remove(b)

        def neuer_bone(bname: str, head: tuple[float, float, float], tail: tuple[float, float, float], parent_name: str | None = None) -> bpy.types.EditBone:
            eb = ebones.new(bname)
            eb.head = Vector(head)
            eb.tail = Vector(tail)
            if parent_name and parent_name in ebones:
                eb.parent = ebones[parent_name]
            return eb

        neuer_bone("Root",      ( 0.000,  0.000, 0.000), ( 0.000,  0.000, 0.850), None)
        neuer_bone("Spine",     ( 0.000,  0.000, 0.850), ( 0.018,  0.000, 1.380), "Root")
        neuer_bone("Head",      ( 0.018,  0.000, 1.380), ( 0.018,  0.000, 1.780), "Spine")
        neuer_bone("Arm_L",     ( 0.200,  0.005, 1.220), ( 0.195, -0.022, 0.875), "Spine")
        neuer_bone("Arm_R",     (-0.160,  0.004, 1.320), ( 0.115, -0.055, 1.285), "Spine")
        neuer_bone("Forearm_R", ( 0.115, -0.055, 1.285), ( 0.198, -0.058, 1.358), "Arm_R")
        neuer_bone("Hand_R",    ( 0.198, -0.058, 1.358), ( 0.380, -0.058, 1.462), "Forearm_R")
        neuer_bone("Leg_L",     ( 0.118,  0.004, 0.745), ( 0.165, -0.015, 0.020), "Root")
        neuer_bone("Leg_R",     (-0.068,  0.004, 0.745), (-0.210, -0.015, 0.020), "Root")

        bpy.ops.object.mode_set(mode="OBJECT")
        self.rig_obj.show_in_front = False
        self.rig_obj.data.display_type = "STICK"

        if self.bust_obj:
            vg_root = self.bust_obj.vertex_groups.get("Root")
            if vg_root and not self.bust_obj.vertex_groups.get("Spine"):
                vg_root.name = "Spine"

        for mobj in (self.bust_obj, self.mund_obj, self.body_obj):
            if mobj is None:
                continue
            mobj.parent = self.rig_obj
            hat_mod = False
            for mod in mobj.modifiers:
                if mod.type == "ARMATURE":
                    mod.object = self.rig_obj
                    hat_mod = True
            if not hat_mod:
                mod = mobj.modifiers.new(name="Armature", type="ARMATURE")
                mod.object = self.rig_obj

        for act in list(bpy.data.actions):
            bpy.data.actions.remove(act)

        self.rig_obj.animation_data_create()
        for pb in self.rig_obj.pose.bones:
            pb.rotation_mode = "XYZ"

        def backe_action(
            name: str,
            laenge: int,
            spine_kf: list[tuple[int, tuple[float, float, float]]],
            head_kf: list[tuple[int, tuple[float, float, float]]],
            forearm_r_kf: list[tuple[int, tuple[float, float, float]]],
            hand_r_kf: list[tuple[int, tuple[float, float, float]]],
        ) -> bpy.types.Action:
            act = bpy.data.actions.new(name=name)
            act.use_fake_user = True
            self.rig_obj.animation_data.action = act

            for pb in self.rig_obj.pose.bones:
                pb.location = (0.0, 0.0, 0.0)
                pb.rotation_euler = (0.0, 0.0, 0.0)

            spuren = {
                "Spine": spine_kf,
                "Head": head_kf,
                "Forearm_R": forearm_r_kf,
                "Hand_R": hand_r_kf,
            }
            for bname, keyframes in spuren.items():
                pb = self.rig_obj.pose.bones.get(bname)
                if not pb:
                    continue
                for frame, rot_deg in keyframes:
                    pb.rotation_euler = tuple(math.radians(g) for g in rot_deg)
                    pb.keyframe_insert(data_path="rotation_euler", frame=frame)

            track = self.rig_obj.animation_data.nla_tracks.new()
            track.name = name
            strip = track.strips.new(name, 1, act)
            strip.action_frame_start = 1
            strip.action_frame_end = laenge
            return act

        idle_act = backe_action(
            "Alex_Idle",
            90,
            spine_kf=[(1, (0.0, 0.0, 0.0)), (45, (1.2, 0.0, 0.5)), (90, (0.0, 0.0, 0.0))],
            head_kf=[(1, (0.0, 0.0, 0.0)), (30, (-1.6, 2.2, -1.2)), (65, (1.2, -1.8, 1.0)), (90, (0.0, 0.0, 0.0))],
            forearm_r_kf=[(1, (0.0, 0.0, 0.0)), (45, (2.5, -1.5, 2.0)), (90, (0.0, 0.0, 0.0))],
            hand_r_kf=[(1, (0.0, 0.0, 0.0)), (45, (3.0, 0.0, -3.5)), (90, (0.0, 0.0, 0.0))],
        )
        backe_action(
            "Alex_Explain",
            60,
            spine_kf=[(1, (0.0, 0.0, 0.0)), (20, (2.0, 0.8, -1.2)), (42, (-0.8, -1.0, 1.2)), (60, (0.0, 0.0, 0.0))],
            head_kf=[(1, (0.0, 0.0, 0.0)), (15, (3.2, -3.8, 2.2)), (35, (-2.4, 4.2, -1.8)), (50, (2.6, 1.4, 0.8)), (60, (0.0, 0.0, 0.0))],
            forearm_r_kf=[(1, (0.0, 0.0, 0.0)), (18, (6.0, -3.5, 5.0)), (38, (-3.0, 2.5, -3.5)), (60, (0.0, 0.0, 0.0))],
            hand_r_kf=[(1, (0.0, 0.0, 0.0)), (18, (7.5, -2.5, -5.5)), (38, (-4.5, 2.0, 4.5)), (60, (0.0, 0.0, 0.0))],
        )
        backe_action(
            "Alex_Listening",
            80,
            spine_kf=[(1, (0.0, 0.0, 0.0)), (40, (1.0, -0.8, 0.5)), (80, (0.0, 0.0, 0.0))],
            head_kf=[(1, (0.0, 0.0, 0.0)), (25, (3.8, 2.2, -2.5)), (55, (-1.2, 1.6, -1.8)), (80, (0.0, 0.0, 0.0))],
            forearm_r_kf=[(1, (0.0, 0.0, 0.0)), (40, (1.5, 0.0, 1.2)), (80, (0.0, 0.0, 0.0))],
            hand_r_kf=[(1, (0.0, 0.0, 0.0)), (40, (2.0, 0.0, -2.0)), (80, (0.0, 0.0, 0.0))],
        )

        self.rig_obj.animation_data.action = idle_act
        bpy.context.scene.frame_start = 1
        bpy.context.scene.frame_end = 90

        aktualisiere_banner(
            "LIVE-BUILD 9/10: Skelett-Rig (9 Bones) & Animationen",
            "Alex_Idle, Alex_Explain & Alex_Listening gebacken",
        )
        richte_viewport_ein(ziel=(0.32, 0.0, 0.94), distanz=2.75, yaw_grad=10.0, pitch_grad=80.0, zeige_knochen=False)

    def schritt_10_export_und_endlos_vorschau(self) -> None:
        log("Schritt 10/10: Exportiere FBX für Unity, speichere .blend & starte Live-Animation ...")
        if self.rig_obj:
            for pb in self.rig_obj.pose.bones:
                pb.location = (0.0, 0.0, 0.0)
                pb.rotation_euler = (0.0, 0.0, 0.0)
        if self.bust_obj and self.bust_obj.data.shape_keys:
            for kb in self.bust_obj.data.shape_keys.key_blocks[1:]:
                kb.value = 0.0
        if self.mund_obj and self.mund_obj.data.shape_keys:
            for kb in self.mund_obj.data.shape_keys.key_blocks[1:]:
                kb.value = 0.0

        bpy.ops.object.select_all(action="DESELECT")
        for exp_obj in (self.rig_obj, self.bust_obj, self.mund_obj, self.body_obj):
            if exp_obj is not None:
                exp_obj.select_set(True)
        if self.rig_obj:
            bpy.context.view_layer.objects.active = self.rig_obj

        bpy.ops.export_scene.fbx(
            filepath=ZIEL_FBX,
            use_selection=True,
            add_leaf_bones=False,
            bake_anim=True,
            bake_anim_use_all_bones=True,
            bake_anim_use_nla_strips=True,
            bake_anim_use_all_actions=True,
            bake_anim_force_startend_keying=True,
            mesh_smooth_type="FACE",
            path_mode="COPY",
            embed_textures=False,
        )
        log(f"FBX exportiert: {ZIEL_FBX}")

        aktualisiere_banner(
            "FERTIG (10/10): Alex (1,80 m) in Blender & Unity exportiert!",
            "Live-Vorschau aktiv: Atmen, Zeigegeste, Blinzeln & Sprechen",
        )
        richte_viewport_ein(ziel=(0.35, 0.0, 0.94), distanz=2.75, yaw_grad=8.0, pitch_grad=82.0, zeige_knochen=False)

        os.makedirs(os.path.dirname(DOCS_RENDER), exist_ok=True)
        bpy.context.scene.render.resolution_x = 1280
        bpy.context.scene.render.resolution_y = 960
        bpy.context.scene.render.filepath = DOCS_RENDER
        try:
            if not bpy.app.background:
                bpy.ops.render.opengl(write_still=True, view_context=True)
            log(f"Vorschaubild gerendert: {DOCS_RENDER}")
        except Exception as exc:
            log(f"Render-Hinweis: {exc}")

        bpy.ops.wm.save_as_mainfile(filepath=ZIEL_BLEND)
        log(f"Blender-Projekt gespeichert: {ZIEL_BLEND}")

    def tick_endlos_animation(self) -> float:
        self.endlos_zeit += 0.05
        t = self.endlos_zeit

        if self.rig_obj and self.rig_obj.pose:
            pb_spine = self.rig_obj.pose.bones.get("Spine")
            pb_head = self.rig_obj.pose.bones.get("Head")
            pb_forearm = self.rig_obj.pose.bones.get("Forearm_R")
            pb_hand = self.rig_obj.pose.bones.get("Hand_R")

            if pb_spine:
                pb_spine.rotation_euler = (
                    math.radians(1.2 * math.sin(t * 1.6)),
                    0.0,
                    math.radians(0.8 * math.cos(t * 0.9)),
                )
            if pb_head:
                pb_head.rotation_euler = (
                    math.radians(2.5 * math.sin(t * 1.2)),
                    math.radians(4.0 * math.sin(t * 0.7)),
                    math.radians(1.8 * math.cos(t * 1.0)),
                )
            if pb_forearm:
                pb_forearm.rotation_euler = (
                    math.radians(4.5 * math.sin(t * 2.1)),
                    math.radians(-2.5 * math.cos(t * 1.4)),
                    math.radians(3.5 * math.sin(t * 1.7)),
                )
            if pb_hand:
                pb_hand.rotation_euler = (
                    math.radians(5.5 * math.sin(t * 2.6)),
                    0.0,
                    math.radians(-4.0 * math.cos(t * 2.1)),
                )

        if self.bust_obj and self.bust_obj.data.shape_keys:
            kbs = self.bust_obj.data.shape_keys.key_blocks
            blink_phase = (t % 3.2)
            blink = max(0.0, 1.0 - abs(blink_phase - 0.15) / 0.12) if blink_phase < 0.30 else 0.0
            if "Blinzeln_L" in kbs:
                kbs["Blinzeln_L"].value = blink
            if "Blinzeln_R" in kbs:
                kbs["Blinzeln_R"].value = blink
            talk = max(0.0, 0.35 * math.sin(t * 6.5) + 0.20 * math.sin(t * 11.0))
            if "Mund_Auf" in kbs:
                kbs["Mund_Auf"].value = talk
            if "Mund_Schmal" in kbs:
                kbs["Mund_Schmal"].value = max(0.0, 0.25 * math.cos(t * 4.2))
            if self.mund_obj and self.mund_obj.data.shape_keys:
                mkbs = self.mund_obj.data.shape_keys.key_blocks
                if "Mund_Auf" in mkbs:
                    mkbs["Mund_Auf"].value = talk

        bpy.context.view_layer.update()
        wm = bpy.context.window_manager
        if wm:
            for win in wm.windows:
                for area in win.screen.areas:
                    if area.type == "VIEW_3D":
                        area.tag_redraw()
        return 0.05

    def timer_schritt(self) -> float | None:
        self.schritt += 1
        try:
            if self.schritt == 1:
                self.schritt_1_szene_und_referenz()
                return 0.9
            elif self.schritt == 2:
                self.schritt_2_kopf_bueste_lebensgross()
                return 1.0
            elif self.schritt == 3:
                self.schritt_3_koerper_textur_und_torso()
                return 1.0
            elif self.schritt == 4:
                self.schritt_4_kaengurutasche_und_kordeln()
                return 0.9
            elif self.schritt == 5:
                self.schritt_5_linker_arm_und_taschenhand()
                return 0.9
            elif self.schritt == 6:
                self.schritt_6_rechter_zeigearm_und_3d_hand()
                return 1.0
            elif self.schritt == 7:
                self.schritt_7_becken_und_jeans_beine()
                return 1.0
            elif self.schritt == 8:
                self.schritt_8_weisse_3d_sneaker()
                return 1.0
            elif self.schritt == 9:
                self.schritt_9_rigging_und_animationen()
                return 1.0
            elif self.schritt == 10:
                self.schritt_10_export_und_endlos_vorschau()
                return 0.1
            else:
                return self.tick_endlos_animation()
        except Exception as exc:
            import traceback
            log(f"FEHLER in Schritt {self.schritt}: {exc}")
            traceback.print_exc()
            return None


_LIVE_BUILDER: AlexLiveBuilder | None = None


def starte_live_modellierung() -> None:
    global _LIVE_BUILDER
    _LIVE_BUILDER = AlexLiveBuilder()
    if bpy.app.background:
        for _ in range(10):
            _LIVE_BUILDER.timer_schritt()
    else:
        bpy.app.timers.register(_LIVE_BUILDER.timer_schritt, first_interval=0.3)


if __name__ == "__main__":
    starte_live_modellierung()
