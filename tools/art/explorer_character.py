"""Builds the player character "Explorer" (exosuit with jetpack) in Blender and exports it to glTF binary.

Everything is generated from this script (fixed seeds), so the asset has no external source (provenance: own work).

    blender --background --factory-startup --python tools/art/explorer_character.py -- \
        --out game/assets/characters/explorer.glb [--preview DIR]

Conventions: metres, Blender Z up, the character faces +Y (glTF/Godot -Z), feet on Z = 0, height about 1.85 m with the helmet.
Construction: a continuous under-suit body grown from a skin-modifier skeleton (smooth, automatic weights) and hard armour
pieces on top, each following one bone (the exosuit plates). Separate objects the game toggles: "JetFlame" (jetpack exhaust,
shown in the air) and "Multitool" (held in the right hand).
Animations (all loop except Jump): Idle, Walk, Run, Sprint, Jump, Fall, Fly.
"""

import argparse
import math
import os
import sys

import bmesh
import bpy
from mathutils import Quaternion, Vector

FPS = 30


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", required=True)
    parser.add_argument("--preview", default=None)
    return parser.parse_args(argv)


# ---------------------------------------------------------------- materials

def srgb(hex_color):
    def channel(v):
        c = v / 255.0
        return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
    return tuple(channel(int(hex_color[i:i + 2], 16)) for i in (0, 2, 4))


MATS = {}


def material(key, name, hex_color, roughness=0.5, metallic=0.0, emission=None, strength=0.0):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*srgb(hex_color), 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    if emission:
        bsdf.inputs["Emission Color"].default_value = (*srgb(emission), 1.0)
        bsdf.inputs["Emission Strength"].default_value = strength
    MATS[key] = mat


def materials():
    material("fabric", "UnderSuit", "34383F", roughness=0.85)
    material("armor", "Armor", "E6E3DA", roughness=0.42)
    material("accent", "Accent", "E07428", roughness=0.4)
    material("dark", "DarkPlate", "4A5058", roughness=0.55)
    material("metal", "Metal", "9AA3AD", roughness=0.28, metallic=0.9)
    material("visor", "Visor", "0D1117", roughness=0.04, metallic=0.55)
    material("light", "Light", "6FE6FF", emission="6FE6FF", strength=5.0)
    material("screen", "Screen", "1D3B44", emission="3FD1C8", strength=2.0)
    material("rubber", "Rubber", "1E2126", roughness=0.9)
    material("flame", "JetFlame", "FFB15C", emission="FF8A3D", strength=12.0)


# ---------------------------------------------------------------- skeleton

def mirror(p, side):
    return (p[0] * side, p[1], p[2])


HIP_Z = 0.99
SPINE_Z = 1.10
CHEST_Z = 1.24
NECK_Z = 1.48
HEAD_Z = 1.56
CLAVICLE = (0.035, 0.0, 1.43)
SHOULDER = (0.195, -0.005, 1.425)
ELBOW = (0.235, -0.025, 1.15)
WRIST = (0.255, 0.0, 0.905)
HAND_END = (0.262, 0.005, 0.83)
FINGER_END = (0.265, 0.01, 0.75)
HIP_JOINT = (0.1, 0.0, 0.94)
KNEE = (0.105, 0.025, 0.52)
ANKLE = (0.105, -0.02, 0.095)
TOE = (0.105, 0.115, 0.035)
TOE_END = (0.105, 0.19, 0.03)

BONES = [
    ("root", (0, 0, 0), (0, 0.25, 0), None),
    ("hips", (0, 0, HIP_Z), (0, 0, SPINE_Z), "root"),
    ("spine", (0, 0, SPINE_Z), (0, 0, CHEST_Z), "hips"),
    ("chest", (0, 0, CHEST_Z), (0, 0, NECK_Z - 0.03), "spine"),
    ("neck", (0, 0, NECK_Z - 0.03), (0, 0.01, HEAD_Z), "chest"),
    ("head", (0, 0.01, HEAD_Z), (0, 0.01, 1.82), "neck"),
]
for _side, _s in ((1, "L"), (-1, "R")):
    BONES += [
        (f"clavicle.{_s}", mirror(CLAVICLE, _side), mirror(SHOULDER, _side), "chest"),
        (f"upper_arm.{_s}", mirror(SHOULDER, _side), mirror(ELBOW, _side), f"clavicle.{_s}"),
        (f"forearm.{_s}", mirror(ELBOW, _side), mirror(WRIST, _side), f"upper_arm.{_s}"),
        (f"hand.{_s}", mirror(WRIST, _side), mirror(HAND_END, _side), f"forearm.{_s}"),
        (f"fingers.{_s}", mirror(HAND_END, _side), mirror(FINGER_END, _side), f"hand.{_s}"),
        (f"thumb.{_s}", mirror((0.245, 0.03, 0.88), _side), mirror((0.235, 0.075, 0.83), _side), f"hand.{_s}"),
        (f"thigh.{_s}", mirror(HIP_JOINT, _side), mirror(KNEE, _side), "hips"),
        (f"shin.{_s}", mirror(KNEE, _side), mirror(ANKLE, _side), f"thigh.{_s}"),
        (f"foot.{_s}", mirror(ANKLE, _side), mirror(TOE, _side), f"shin.{_s}"),
        (f"toe.{_s}", mirror(TOE, _side), mirror(TOE_END, _side), f"foot.{_s}"),
    ]


def build_armature():
    data = bpy.data.armatures.new("ExplorerRig")
    rig = bpy.data.objects.new("Explorer", data)
    bpy.context.scene.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    rig.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    for name, head, tail, parent in BONES:
        bone = data.edit_bones.new(name)
        bone.head = head
        bone.tail = tail
        bone.roll = 0.0
        if parent:
            bone.parent = data.edit_bones[parent]
            bone.use_connect = False
        bone.use_deform = name != "root"
    bpy.ops.object.mode_set(mode="OBJECT")
    rig.select_set(False)
    return rig


# ---------------------------------------------------------------- under-suit body (skin modifier)

# Skeleton of the body: points with radii (x, y) and the edges between them.
BODY_POINTS = {
    "pelvis": ((0, 0, 0.97), (0.15, 0.11)),
    "waist": ((0, 0, 1.1), (0.135, 0.1)),
    "chest": ((0, 0.005, 1.27), (0.165, 0.115)),
    "upper_chest": ((0, 0, 1.4), (0.15, 0.1)),
    "neck": ((0, 0.005, 1.5), (0.055, 0.055)),
    "head": ((0, 0.01, 1.62), (0.085, 0.09)),
}
BODY_EDGES = [("pelvis", "waist"), ("waist", "chest"), ("chest", "upper_chest"), ("upper_chest", "neck"), ("neck", "head")]
for _side, _s in ((1, "L"), (-1, "R")):
    BODY_POINTS.update({
        f"shoulder.{_s}": (mirror(SHOULDER, _side), (0.062, 0.062)),
        f"elbow.{_s}": (mirror(ELBOW, _side), (0.048, 0.048)),
        f"wrist.{_s}": (mirror(WRIST, _side), (0.036, 0.04)),
        f"hip.{_s}": (mirror(HIP_JOINT, _side), (0.085, 0.085)),
        f"knee.{_s}": (mirror(KNEE, _side), (0.058, 0.06)),
        f"ankle.{_s}": (mirror(ANKLE, _side), (0.045, 0.05)),
    })
    BODY_EDGES += [("upper_chest", f"shoulder.{_s}"), (f"shoulder.{_s}", f"elbow.{_s}"), (f"elbow.{_s}", f"wrist.{_s}"),
                   ("pelvis", f"hip.{_s}"), (f"hip.{_s}", f"knee.{_s}"), (f"knee.{_s}", f"ankle.{_s}")]


def build_body(rig):
    names = list(BODY_POINTS)
    mesh = bpy.data.meshes.new("UnderSuit")
    mesh.from_pydata([BODY_POINTS[n][0] for n in names], [(names.index(a), names.index(b)) for a, b in BODY_EDGES], [])
    body = bpy.data.objects.new("UnderSuit", mesh)
    bpy.context.scene.collection.objects.link(body)
    skin = body.modifiers.new("Skin", "SKIN")
    skin.use_smooth_shade = True
    skin.branch_smoothing = 0.6
    for i, n in enumerate(names):
        mesh.skin_vertices[0].data[i].radius = BODY_POINTS[n][1]
    mesh.skin_vertices[0].data[names.index("pelvis")].use_root = True
    sub = body.modifiers.new("Subdivision", "SUBSURF")
    sub.levels = 2
    sub.render_levels = 2
    bpy.context.view_layer.objects.active = body
    body.select_set(True)
    bpy.ops.object.modifier_apply(modifier="Skin")
    bpy.ops.object.modifier_apply(modifier="Subdivision")
    body.data.materials.append(MATS["fabric"])
    for p in body.data.polygons:
        p.use_smooth = True
    body.select_set(False)

    # Automatic weights to the deforming bones, falling back to envelopes if bone heat fails.
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    try:
        bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    except RuntimeError:
        bpy.ops.object.parent_set(type="ARMATURE_ENVELOPE")
    if not body.vertex_groups or all(len(g.name) == 0 for g in body.vertex_groups):
        bpy.ops.object.parent_set(type="ARMATURE_ENVELOPE")
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = body
    body.select_set(True)
    bpy.ops.object.mode_set(mode="WEIGHT_PAINT")
    bpy.ops.object.vertex_group_limit_total(limit=4)
    bpy.ops.object.vertex_group_normalize_all(lock_active=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    body.select_set(False)
    return body


# ---------------------------------------------------------------- armour pieces (rigid, one bone each)

PIECES = []  # (object, bone)


def finish(obj, bone, mat, angle=0.6, bevel=0.0, segments=2):
    obj.data.materials.append(MATS[mat])
    if bevel > 0:
        mod = obj.modifiers.new("Bevel", "BEVEL")
        mod.width = bevel
        mod.segments = segments
        mod.limit_method = "ANGLE"
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=mod.name)
    for p in obj.data.polygons:
        p.use_smooth = True
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.shade_smooth_by_angle(angle=angle)
    obj.select_set(False)
    PIECES.append((obj, bone))
    return obj


def box(name, bone, mat, size, loc, bevel=0.012, rot=(0, 0, 0), taper=None):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=rot)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if taper:
        # Narrow the top (+Z) face: taper = (x factor, y factor).
        for v in obj.data.vertices:
            if v.co.z > 0:
                v.co.x *= taper[0]
                v.co.y *= taper[1]
    return finish(obj, bone, mat, bevel=bevel)


def sphere(name, bone, mat, radius, loc, scale=(1, 1, 1), seg=24, rings=14):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=rings, radius=radius, location=loc)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish(obj, bone, mat, angle=1.2)


def tube(name, bone, mat, start, end, r0, r1, verts=16, angle=1.0):
    start, end = Vector(start), Vector(end)
    axis = end - start
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r0, radius2=r1, depth=axis.length, location=(start + end) / 2)
    obj = bpy.context.active_object
    obj.name = name
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(axis.normalized())
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    return finish(obj, bone, mat, angle=angle)


def ring(name, bone, mat, loc, major, minor, rot=(0, 0, 0), scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor, major_segments=28, minor_segments=8, location=loc,
                                     rotation=rot)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish(obj, bone, mat, angle=1.2)


def shell(name, bone, mat, center, radius, keep, scale=(1, 1, 1), thickness=0.01, seg=40, rings=24):
    """A cut-out piece of a sphere: keep(co) decides which vertices stay (co relative to the centre, before scale)."""
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=rings, radius=radius, location=center)
    obj = bpy.context.active_object
    obj.name = name
    mesh = bmesh.new()
    mesh.from_mesh(obj.data)
    bmesh.ops.delete(mesh, geom=[v for v in mesh.verts if not keep(v.co)], context="VERTS")
    mesh.to_mesh(obj.data)
    mesh.free()
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    solid = obj.modifiers.new("Solidify", "SOLIDIFY")
    solid.thickness = thickness
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=solid.name)
    return finish(obj, bone, mat, angle=1.2)


def build_armor():
    # Helmet: elongated dome, a wide dark visor band, side modules with lights, crest and chin guard.
    helmet_c = (0, 0.012, 1.655)
    sphere("Helmet", "head", "armor", 0.165, helmet_c, scale=(0.94, 1.08, 1.06), seg=32, rings=18)
    shell("Visor", "head", "visor", helmet_c, 0.172, lambda co: co.y > 0.035 and -0.075 < co.z < 0.07 and abs(co.x) < 0.15,
          scale=(0.95, 1.1, 1.07), thickness=0.008)
    shell("VisorFrame", "head", "dark", helmet_c, 0.174,
          lambda co: co.y > 0.02 and (-0.095 < co.z < -0.07 or 0.065 < co.z < 0.085) and abs(co.x) < 0.155,
          scale=(0.95, 1.1, 1.07), thickness=0.012)
    box("HelmetCrest", "head", "accent", (0.035, 0.27, 0.035), (0, -0.01, 1.835), bevel=0.012)
    box("ChinGuard", "head", "dark", (0.17, 0.08, 0.05), (0, 0.13, 1.545), bevel=0.018, taper=(1.15, 0.9))
    for side in (1, -1):
        tube(f"EarModule{side}", "head", "dark", (0.145 * side, 0.0, 1.65), (0.185 * side, 0.0, 1.65), 0.05, 0.042, verts=20)
        sphere(f"EarLight{side}", "head", "light", 0.016, (0.188 * side, 0.02, 1.665))
        box(f"Antenna{side}", "head", "metal", (0.008, 0.008, 0.11), (0.17 * side, -0.05, 1.73), bevel=0.0, rot=(0.25, 0, 0))
    ring("NeckSeal", "chest", "metal", (0, 0.005, NECK_Z - 0.02), 0.115, 0.022, scale=(1, 0.95, 1))

    # Torso: chest plate with a reactor light, abdomen segments, collar plates.
    box("ChestPlate", "chest", "armor", (0.37, 0.2, 0.25), (0, 0.035, 1.31), bevel=0.045, taper=(1.08, 0.9))
    box("ChestStripe", "chest", "accent", (0.31, 0.04, 0.05), (0, 0.13, 1.37), bevel=0.012)
    box("ChestCore", "chest", "dark", (0.1, 0.03, 0.07), (0.06, 0.137, 1.27), bevel=0.012)
    sphere("ChestLight", "chest", "light", 0.016, (0.06, 0.155, 1.27))
    box("Collar.L", "chest", "armor", (0.12, 0.17, 0.05), (0.1, 0.0, 1.45), bevel=0.02, rot=(0, -0.35, 0))
    box("Collar.R", "chest", "armor", (0.12, 0.17, 0.05), (-0.1, 0.0, 1.45), bevel=0.02, rot=(0, 0.35, 0))
    for i, z in enumerate((1.16, 1.1)):
        box(f"AbPlate{i}", "spine", "dark", (0.25 - i * 0.02, 0.05, 0.05), (0, 0.105, z), bevel=0.015)

    # Belt with pouches and a holster plate.
    box("Belt", "hips", "dark", (0.33, 0.24, 0.05), (0, 0.0, 1.02), bevel=0.02)
    box("Buckle", "hips", "metal", (0.08, 0.03, 0.05), (0, 0.125, 1.02), bevel=0.01)
    for side in (1, -1):
        box(f"Pouch{side}", "hips", "dark", (0.07, 0.06, 0.08), (0.15 * side, 0.08, 0.98), bevel=0.015)
    box("PelvisPlate", "hips", "armor", (0.2, 0.06, 0.1), (0, 0.105, 0.94), bevel=0.02, taper=(1.25, 1.0))

    # Exosuit backpack with jetpack thrusters, vents and a light strip.
    box("Backpack", "chest", "armor", (0.32, 0.15, 0.36), (0, -0.17, 1.3), bevel=0.05, taper=(0.92, 0.9))
    box("BackpackCore", "chest", "dark", (0.2, 0.04, 0.24), (0, -0.25, 1.3), bevel=0.012)
    box("BackpackStripe", "chest", "accent", (0.33, 0.155, 0.035), (0, -0.17, 1.43), bevel=0.01)
    box("LightStrip", "chest", "light", (0.12, 0.012, 0.012), (0, -0.272, 1.38), bevel=0.0)
    for side in (1, -1):
        tube(f"Thruster{side}", "chest", "metal", (0.12 * side, -0.24, 1.36), (0.12 * side, -0.24, 1.14), 0.045, 0.05, verts=20)
        tube(f"Nozzle{side}", "chest", "dark", (0.12 * side, -0.24, 1.14), (0.12 * side, -0.24, 1.1), 0.05, 0.062, verts=20)
        ring(f"ThrusterBand{side}", "chest", "accent", (0.12 * side, -0.24, 1.3), 0.05, 0.008)

    for side, s in ((1, "L"), (-1, "R")):
        # Shoulders: two layered plates.
        sphere(f"ShoulderPad.{s}", f"upper_arm.{s}", "armor", 0.085, mirror((0.215, 0.0, 1.43), side), scale=(1.1, 1.05, 0.75))
        box(f"ShoulderStripe.{s}", f"upper_arm.{s}", "accent", (0.05, 0.13, 0.02), mirror((0.24, 0.0, 1.47), side), bevel=0.008,
            rot=(0, -0.5 * side, 0))
        tube(f"BicepGuard.{s}", f"upper_arm.{s}", "armor", mirror((0.212, -0.01, 1.34), side), mirror((0.23, -0.02, 1.2), side),
             0.058, 0.052)
        sphere(f"ElbowPad.{s}", f"forearm.{s}", "dark", 0.05, mirror((0.236, -0.05, 1.15), side), scale=(1, 0.8, 1))
        tube(f"Forearm.{s}", f"forearm.{s}", "armor", mirror((0.24, -0.02, 1.1), side), mirror((0.252, -0.005, 0.95), side),
             0.052, 0.045)
        ring(f"Cuff.{s}", f"forearm.{s}", "accent", mirror((0.253, 0.0, 0.94), side), 0.046, 0.012)
        if side == 1:
            # Wrist computer on the left forearm.
            box("WristComputer", "forearm.L", "dark", (0.03, 0.08, 0.1), (0.29, 0.0, 1.0), bevel=0.01)
            box("WristScreen", "forearm.L", "screen", (0.006, 0.06, 0.075), (0.307, 0.0, 1.0), bevel=0.0)
        # Glove: palm, fingers in one block on their bone, thumb.
        box(f"Palm.{s}", f"hand.{s}", "rubber", (0.045, 0.085, 0.085), mirror((0.258, 0.008, 0.865), side), bevel=0.018)
        box(f"Knuckles.{s}", f"hand.{s}", "dark", (0.05, 0.088, 0.025), mirror((0.262, 0.008, 0.83), side), bevel=0.01)
        for i, y in enumerate((-0.03, -0.008, 0.014, 0.034)):
            box(f"Finger{i}.{s}", f"fingers.{s}", "rubber", (0.026, 0.02, 0.075 - abs(i - 1.5) * 0.008),
                mirror((0.262, y + 0.008, 0.79), side), bevel=0.008)
        box(f"Thumb.{s}", f"thumb.{s}", "rubber", (0.025, 0.026, 0.06), mirror((0.24, 0.055, 0.855), side), bevel=0.009,
            rot=(0.5, 0, 0))

        # Legs: thigh plate, knee pad, shin guard, boot with sole and treads.
        tube(f"ThighPlate.{s}", f"thigh.{s}", "armor", mirror((0.1, 0.015, 0.86), side), mirror((0.105, 0.03, 0.6), side),
             0.088, 0.072, verts=18)
        box(f"ThighStripe.{s}", f"thigh.{s}", "accent", (0.02, 0.03, 0.2), mirror((0.18, 0.02, 0.73), side), bevel=0.006)
        sphere(f"KneePad.{s}", f"shin.{s}", "dark", 0.065, mirror((0.105, 0.06, 0.52), side), scale=(1, 0.7, 1.1))
        box(f"ShinGuard.{s}", f"shin.{s}", "armor", (0.1, 0.06, 0.3), mirror((0.105, 0.05, 0.32), side), bevel=0.025,
            taper=(1.15, 1.0))
        tube(f"Calf.{s}", f"shin.{s}", "armor", mirror((0.105, -0.01, 0.46), side), mirror((0.105, -0.015, 0.2), side), 0.062, 0.05)
        box(f"Boot.{s}", f"foot.{s}", "dark", (0.12, 0.2, 0.12), mirror((0.105, 0.03, 0.075), side), bevel=0.035)
        box(f"BootCuff.{s}", f"shin.{s}", "accent", (0.13, 0.14, 0.04), mirror((0.105, -0.01, 0.15), side), bevel=0.015)
        box(f"ToeCap.{s}", f"toe.{s}", "armor", (0.115, 0.1, 0.075), mirror((0.105, 0.15, 0.045), side), bevel=0.03)
        box(f"Sole.{s}", f"foot.{s}", "rubber", (0.13, 0.3, 0.03), mirror((0.105, 0.06, 0.015), side), bevel=0.01)
        for i in range(4):
            box(f"Tread{i}.{s}", f"foot.{s}", "rubber", (0.125, 0.02, 0.012), mirror((0.105, -0.06 + i * 0.07, 0.002), side),
                bevel=0.0)


def build_extras(rig):
    """Objects the game toggles; each follows one bone."""
    extras = []
    # Jetpack exhaust: two cones under the nozzles.
    for side in (1, -1):
        obj = tube(f"JetFlame{side}", "chest", "flame", (0.12 * side, -0.24, 1.1), (0.12 * side, -0.24, 0.82), 0.045, 0.0,
                   verts=12)
        PIECES.remove((obj, "chest"))
        extras.append((obj, "chest"))
    # Multitool in the right hand (pointing forward).
    parts = [
        box("ToolBody", "hand.R", "armor", (0.05, 0.22, 0.07), (-0.27, 0.08, 0.83), bevel=0.018),
        box("ToolGrip", "hand.R", "dark", (0.035, 0.045, 0.1), (-0.265, 0.0, 0.79), bevel=0.01),
        tube("ToolBarrel", "hand.R", "metal", (-0.27, 0.18, 0.84), (-0.27, 0.26, 0.84), 0.022, 0.016, verts=14),
        sphere("ToolCore", "hand.R", "light", 0.018, (-0.27, 0.11, 0.875)),
        box("ToolStripe", "hand.R", "accent", (0.052, 0.06, 0.02), (-0.27, 0.03, 0.865), bevel=0.006),
    ]
    for obj in parts:
        PIECES.remove((obj, "hand.R"))
    bpy.ops.object.select_all(action="DESELECT")
    for obj in parts:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    tool = bpy.context.active_object
    tool.name = "Multitool"
    extras.append((tool, "hand.R"))

    flames = [obj for obj, _ in extras if obj.name.startswith("JetFlame")]
    bpy.ops.object.select_all(action="DESELECT")
    for obj in flames:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = flames[0]
    bpy.ops.object.join()
    flame = bpy.context.active_object
    flame.name = "JetFlame"
    for obj, bone in ((flame, "chest"), (tool, "hand.R")):
        rigid_skin(obj, bone, rig)
    return [flame, tool]


def rigid_skin(obj, bone, rig):
    group = obj.vertex_groups.new(name=bone)
    group.add(list(range(len(obj.data.vertices))), 1.0, "REPLACE")
    obj.parent = rig
    mod = obj.modifiers.new("Armature", "ARMATURE")
    mod.object = rig


def join_armor(body, rig):
    bpy.ops.object.select_all(action="DESELECT")
    for obj, bone in PIECES:
        group = obj.vertex_groups.new(name=bone)
        group.add(list(range(len(obj.data.vertices))), 1.0, "REPLACE")
        obj.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.join()
    merged = bpy.context.active_object
    merged.name = "ExplorerMesh"
    merged.data.name = "ExplorerMesh"
    return merged


# ---------------------------------------------------------------- animation

def axis_in_bone(rig, bone_name, world_axis):
    rest = rig.data.bones[bone_name].matrix_local.to_3x3()
    return (rest.inverted() @ Vector(world_axis)).normalized()


def rot(rig, bone_name, pitch=0.0, roll=0.0, yaw=0.0):
    """Rotation of a bone around the world axes at rest: pitch about X (positive swings a hanging limb forward),
    roll about Y (positive tilts towards +X), yaw about Z. Degrees."""
    q = Quaternion()
    for axis, angle in (((0, 0, 1), yaw), ((0, 1, 0), -roll), ((1, 0, 0), pitch)):
        if angle:
            q = q @ Quaternion(axis_in_bone(rig, bone_name, axis), math.radians(angle))
    return q


def new_action(rig, name):
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    rig.animation_data.action = action
    for pb in rig.pose.bones:
        pb.rotation_mode = "QUATERNION"
        pb.rotation_quaternion = Quaternion()
        pb.location = Vector()
    return action


# Relaxed hands: fingers and thumbs slightly curled towards the palm (palms face the body).
HANDS = {"fingers.L": (8, -25, 0), "fingers.R": (8, 25, 0), "thumb.L": (0, -15, 0), "thumb.R": (0, 15, 0),
         "hand.L": (4, -3, 0), "hand.R": (4, 3, 0)}


def key(rig, frame, poses, hips_offset=None):
    """poses: bone -> (pitch, roll, yaw). Every bone is keyed on every key frame so actions are self-contained."""
    full = dict(HANDS)
    full.update(poses)
    for pb in rig.pose.bones:
        p = full.get(pb.name, (0.0, 0.0, 0.0))
        pb.rotation_quaternion = rot(rig, pb.name, *p)
        pb.keyframe_insert("rotation_quaternion", frame=frame)
        if pb.name == "hips":
            offset = Vector(hips_offset or (0, 0, 0))
            pb.location = rig.data.bones["hips"].matrix_local.to_3x3().inverted() @ offset
            pb.keyframe_insert("location", frame=frame)


def cycle(rig, name, length, frames):
    """frames: list of (t in 0..1, poses, hips_offset); the first pose is repeated at the end for a seamless loop."""
    new_action(rig, name)
    for t, poses, hips in frames:
        key(rig, 1 + round(t * length), poses, hips)
    key(rig, 1 + length, frames[0][1], frames[0][2])


def gait(stride, knee, arm, elbow, lean, bob, drop, sway):
    """A full gait cycle in eight keys, left heel contact first."""
    def pose(lt, ls, rt, rs, la, ra, twist, hip_roll):
        return {
            "hips": (0, hip_roll, -twist * 0.6),
            "spine": (lean * 0.4, -hip_roll * 0.5, twist * 0.4),
            "chest": (lean * 0.4, -hip_roll * 0.3, twist),
            "neck": (-lean * 0.3, 0, -twist * 0.6),
            "head": (-lean * 0.5, 0, -twist * 0.6),
            "clavicle.L": (la * 0.12, 0, 0), "clavicle.R": (ra * 0.12, 0, 0),
            "thigh.L": (lt, -hip_roll, 0), "shin.L": (ls, 0, 0), "foot.L": (-lt * 0.3 - ls * 0.4, 0, 0),
            "toe.L": (min(0, ls * 0.3), 0, 0),
            "thigh.R": (rt, -hip_roll, 0), "shin.R": (rs, 0, 0), "foot.R": (-rt * 0.3 - rs * 0.4, 0, 0),
            "toe.R": (min(0, rs * 0.3), 0, 0),
            "upper_arm.L": (la, 7, 0), "forearm.L": (elbow + max(la, 0) * 0.7, 0, 0),
            "upper_arm.R": (ra, -7, 0), "forearm.R": (elbow + max(ra, 0) * 0.7, 0, 0),
        }

    return [
        (0.0, pose(stride, -5, -stride, -knee * 0.35, -arm, arm, 7, 0), (0, 0, -drop)),
        (0.125, pose(stride * 0.5, -knee * 0.25, -stride * 0.6, -knee, -arm * 0.5, arm * 0.5, 3, sway), (sway * 0.004, 0, 0)),
        (0.25, pose(0, -8, stride * 0.2, -knee * 0.9, 0, 0, 0, sway), (sway * 0.006, 0, bob)),
        (0.375, pose(-stride * 0.5, -5, stride * 0.8, -knee * 0.5, arm * 0.5, -arm * 0.5, -3, sway * 0.5), (0, 0, 0)),
        (0.5, pose(-stride, -knee * 0.35, stride, -5, arm, -arm, -7, 0), (0, 0, -drop)),
        (0.625, pose(-stride * 0.6, -knee, stride * 0.5, -knee * 0.25, arm * 0.5, -arm * 0.5, -3, -sway), (-sway * 0.004, 0, 0)),
        (0.75, pose(stride * 0.2, -knee * 0.9, 0, -8, 0, 0, 0, -sway), (-sway * 0.006, 0, bob)),
        (0.875, pose(stride * 0.8, -knee * 0.5, -stride * 0.5, -5, -arm * 0.5, arm * 0.5, 3, -sway * 0.5), (0, 0, 0)),
    ]


def build_animations(rig):
    rig.animation_data_create()

    def idle(breath, look, shift):
        return {
            "hips": (0, shift, 0), "spine": (breath * 0.4, -shift * 0.6, 0), "chest": (-breath, -shift * 0.3, look * 0.15),
            "neck": (breath * 0.3, 0, look * 0.4), "head": (breath * 0.4, look * 0.2, look * 0.6),
            "clavicle.L": (0, breath * 0.6, 0), "clavicle.R": (0, -breath * 0.6, 0),
            "upper_arm.L": (3 + breath, 8 + breath * 0.5, 0), "upper_arm.R": (3 + breath, -8 - breath * 0.5, 0),
            "forearm.L": (14, 0, 0), "forearm.R": (14, 0, 0),
            "thigh.L": (3, -shift, 0), "thigh.R": (-2, -shift, 0), "shin.L": (-5, 0, 0), "foot.L": (2, 0, 0),
        }

    cycle(rig, "Idle", 120, [(0.0, idle(0, 0, 0), (0, 0, 0)), (0.25, idle(2.5, 10, 1.5), (0.008, 0, 0.005)),
                             (0.5, idle(0, 4, 2), (0.012, 0, 0)), (0.75, idle(2.5, -8, 0.8), (0.005, 0, 0.005))])
    cycle(rig, "Walk", 34, gait(stride=27, knee=55, arm=20, elbow=16, lean=4, bob=0.02, drop=0.025, sway=2))
    cycle(rig, "Run", 22, gait(stride=42, knee=100, arm=40, elbow=70, lean=13, bob=0.05, drop=0.06, sway=2))
    cycle(rig, "Sprint", 18, gait(stride=55, knee=115, arm=55, elbow=85, lean=22, bob=0.06, drop=0.07, sway=1.5))

    # Jump: anticipation crouch, push off, tuck (held at the end).
    new_action(rig, "Jump")
    crouch = {"thigh.L": (45, 0, 0), "thigh.R": (45, 0, 0), "shin.L": (-80, 0, 0), "shin.R": (-80, 0, 0),
              "foot.L": (35, 0, 0), "foot.R": (35, 0, 0), "spine": (14, 0, 0), "chest": (10, 0, 0), "head": (-12, 0, 0),
              "upper_arm.L": (-30, 12, 0), "upper_arm.R": (-30, -12, 0), "forearm.L": (35, 0, 0), "forearm.R": (35, 0, 0)}
    push = {"thigh.L": (12, 0, 0), "thigh.R": (-4, 0, 0), "shin.L": (-28, 0, 0), "shin.R": (-12, 0, 0),
            "foot.L": (-22, 0, 0), "foot.R": (-35, 0, 0), "toe.R": (-10, 0, 0), "spine": (-4, 0, 0),
            "upper_arm.L": (70, 22, 0), "upper_arm.R": (70, -22, 0), "forearm.L": (40, 0, 0), "forearm.R": (40, 0, 0)}
    tuck = {"thigh.L": (40, 0, 0), "thigh.R": (25, 0, 0), "shin.L": (-60, 0, 0), "shin.R": (-50, 0, 0),
            "foot.L": (10, 0, 0), "foot.R": (10, 0, 0), "spine": (5, 0, 0),
            "upper_arm.L": (35, 28, 0), "upper_arm.R": (35, -28, 0), "forearm.L": (45, 0, 0), "forearm.R": (45, 0, 0)}
    key(rig, 1, crouch, (0, 0, -0.2))
    key(rig, 6, push, (0, 0, 0.03))
    key(rig, 16, tuck, (0, 0, 0.05))

    def fall(f):
        return {"thigh.L": (25 + f, 4, 0), "thigh.R": (10 - f, -4, 0), "shin.L": (-45, 0, 0), "shin.R": (-30 - f, 0, 0),
                "foot.L": (12, 0, 0), "foot.R": (12, 0, 0), "spine": (-5, 0, 0), "head": (8, 0, 0),
                "clavicle.L": (0, 6, 0), "clavicle.R": (0, -6, 0),
                "upper_arm.L": (20 + f, 55 + f, 0), "upper_arm.R": (20 - f, -55 + f, 0),
                "forearm.L": (30, 0, 0), "forearm.R": (30, 0, 0)}
    cycle(rig, "Fall", 40, [(0.0, fall(0), (0, 0, 0)), (0.5, fall(7), (0, 0, 0.01))])

    # Jetpack flight: leaning into the thrust, legs trailing, arms back.
    def fly(f):
        return {"hips": (8, 0, 0), "spine": (10, 0, 0), "chest": (6, 0, 0), "neck": (-8, 0, 0), "head": (-14, 0, 0),
                "thigh.L": (-6 + f, 3, 0), "thigh.R": (-12 - f, -3, 0), "shin.L": (-25, 0, 0), "shin.R": (-15, 0, 0),
                "foot.L": (-35, 0, 0), "foot.R": (-35, 0, 0), "toe.L": (-10, 0, 0), "toe.R": (-10, 0, 0),
                "upper_arm.L": (-18 + f, 20, 0), "upper_arm.R": (-18 - f, -20, 0), "forearm.L": (20, 0, 0), "forearm.R": (20, 0, 0)}
    cycle(rig, "Fly", 60, [(0.0, fly(0), (0, 0, 0)), (0.5, fly(5), (0, 0, 0.02))])

    rig.animation_data.action = bpy.data.actions["Idle"]


# ---------------------------------------------------------------- export and preview

def export(rig, path):
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    for child in rig.children:
        child.select_set(True)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_apply=True, export_animations=True,
                              export_animation_mode="ACTIONS", export_force_sampling=True, export_frame_step=1,
                              export_def_bones=False, export_yup=True)


def preview(rig, directory):
    os.makedirs(directory, exist_ok=True)
    scene = bpy.context.scene
    engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items]
    scene.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in engines else "BLENDER_EEVEE"
    scene.render.resolution_x = 640
    scene.render.resolution_y = 800
    world = bpy.data.worlds.new("Preview")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.3, 0.37, 0.45, 1)
    scene.world = world
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 4
    sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(30))
    scene.collection.objects.link(sun)
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    shots = [("front", (1.6, 3.0, 1.45), "Idle", 1, 0.95), ("side_walk", (3.6, 0.0, 1.1), "Walk", 9, 0.9),
             ("run", (2.6, 2.6, 1.2), "Run", 6, 0.9), ("back", (-1.4, -3.2, 1.6), "Fly", 1, 1.0),
             ("jump", (2.6, 2.6, 1.2), "Jump", 16, 0.95), ("head", (0.45, 0.75, 1.72), "Idle", 1, 1.6)]
    for name, position, action, frame, target_z in shots:
        rig.animation_data.action = bpy.data.actions[action]
        scene.frame_set(frame)
        cam.location = position
        cam.rotation_euler = (Vector((0, 0, target_z)) - Vector(position)).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(directory, f"{name}.png")
        bpy.ops.render.render(write_still=True)


def main():
    args = parse_args()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = FPS
    materials()
    rig = build_armature()
    body = build_body(rig)
    build_armor()
    build_extras(rig)
    join_armor(body, rig)
    build_animations(rig)
    export(rig, args.out)
    if args.preview:
        preview(rig, args.preview)
    print("EXPLORER OK", args.out, "bones", len(rig.data.bones))


main()
