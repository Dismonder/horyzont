"""Builds the player character "Explorer" (space suit) in Blender and exports it to glTF binary.

Everything is generated from this script, so the asset has no external source (provenance: own work).

    blender --background --factory-startup --python tools/art/explorer_character.py -- \
        --out game/assets/characters/explorer.glb [--preview DIR]

Conventions: metres, Blender Z up, the character faces +Y (glTF/Godot -Z), feet on Z = 0, height 1.8 m.
Rigid skinning: every part follows one bone (the suit is made of hard segments), so no weight painting is needed.
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

def make_material(name, color, metallic=0.0, roughness=0.5, emission=None, emission_strength=0.0):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    if emission is not None:
        bsdf.inputs["Emission Color"].default_value = (*emission, 1.0)
        bsdf.inputs["Emission Strength"].default_value = emission_strength
    return mat


def srgb(hex_color):
    def channel(v):
        c = v / 255.0
        return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
    return tuple(channel(int(hex_color[i:i + 2], 16)) for i in (0, 2, 4))


MATERIALS = {}


def materials():
    MATERIALS["suit"] = make_material("Suit", srgb("E8E6DF"), roughness=0.55)
    MATERIALS["accent"] = make_material("Accent", srgb("E8762B"), roughness=0.45)
    MATERIALS["joint"] = make_material("Joint", srgb("3A3F47"), roughness=0.8)
    MATERIALS["metal"] = make_material("Metal", srgb("9AA3AD"), metallic=0.9, roughness=0.3)
    MATERIALS["visor"] = make_material("Visor", srgb("1B2A3A"), metallic=0.6, roughness=0.08)
    MATERIALS["light"] = make_material("Light", srgb("7FE3FF"), emission=srgb("7FE3FF"), emission_strength=4.0)


# ---------------------------------------------------------------- mesh parts

PARTS = []  # (object, bone name)


def finish(obj, bone, material, smooth=True, bevel=0.0, angle=0.7):
    obj.data.materials.append(MATERIALS[material])
    if bevel > 0:
        mod = obj.modifiers.new("Bevel", "BEVEL")
        mod.width = bevel
        mod.segments = 2
        mod.limit_method = "ANGLE"
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=mod.name)
    if smooth:
        for poly in obj.data.polygons:
            poly.use_smooth = True
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        bpy.ops.object.shade_smooth_by_angle(angle=angle)
        obj.select_set(False)
    PARTS.append((obj, bone))
    return obj


def box(name, bone, material, size, location, bevel=0.02, rotation=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=location, rotation=rotation)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish(obj, bone, material, bevel=bevel)


def sphere(name, bone, material, radius, location, scale=(1, 1, 1), segments=20, rings=12):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, radius=radius, location=location)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish(obj, bone, material, angle=1.2)


def limb(name, bone, material, start, end, r0, r1, vertices=16):
    """Tapered cylinder from start to end with radii r0 and r1."""
    start, end = Vector(start), Vector(end)
    axis = end - start
    bpy.ops.mesh.primitive_cone_add(vertices=vertices, radius1=r0, radius2=r1, depth=axis.length, location=(start + end) / 2)
    obj = bpy.context.active_object
    obj.name = name
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(axis.normalized())
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    return finish(obj, bone, material, angle=1.0)


def ring(name, bone, material, location, major, minor, rotation=(0, 0, 0)):
    bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor, major_segments=24, minor_segments=8,
                                     location=location, rotation=rotation)
    obj = bpy.context.active_object
    obj.name = name
    return finish(obj, bone, material, angle=1.2)


def cylinder(name, bone, material, radius, depth, location, rotation=(0, 0, 0), vertices=16):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=location, rotation=rotation)
    obj = bpy.context.active_object
    obj.name = name
    return finish(obj, bone, material, angle=0.8)


def visor_shell(center, radius):
    """Front window of the helmet: a band of a sphere slightly larger than the helmet."""
    bpy.ops.mesh.primitive_uv_sphere_add(segments=40, ring_count=24, radius=radius, location=center)
    obj = bpy.context.active_object
    obj.name = "Visor"
    mesh = bmesh.new()
    mesh.from_mesh(obj.data)
    doomed = [v for v in mesh.verts
              if v.co.y < radius * 0.28 or v.co.z < -radius * 0.42 or v.co.z > radius * 0.5 or abs(v.co.x) > radius * 0.86]
    bmesh.ops.delete(mesh, geom=doomed, context="VERTS")
    mesh.to_mesh(obj.data)
    mesh.free()
    solid = obj.modifiers.new("Solidify", "SOLIDIFY")
    solid.thickness = 0.012
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=solid.name)
    return finish(obj, "head", "visor", angle=1.2)


# Skeleton joints (Blender coordinates).
HIP_Z = 0.98
CHEST_Z = 1.17
NECK_Z = 1.47
HEAD_Z = 1.55
SHOULDER = (0.265, 0.0, 1.41)
ELBOW = (0.3, -0.01, 1.13)
WRIST = (0.315, 0.0, 0.87)
HAND_END = (0.32, 0.0, 0.76)
HIP_JOINT = (0.11, 0.0, 0.93)
KNEE = (0.11, 0.01, 0.52)
ANKLE = (0.11, -0.01, 0.12)
TOE = (0.11, 0.17, 0.04)


def mirror(p, side):
    return (p[0] * side, p[1], p[2])


def build_body():
    # Pelvis, abdomen and torso.
    box("Pelvis", "hips", "suit", (0.38, 0.25, 0.18), (0, 0, 0.93), bevel=0.05)
    box("Belt", "hips", "joint", (0.40, 0.265, 0.05), (0, 0, 1.0), bevel=0.02)
    box("BeltBuckle", "hips", "metal", (0.09, 0.03, 0.06), (0, 0.135, 1.0), bevel=0.01)
    limb("Abdomen", "spine", "joint", (0, 0, 1.0), (0, 0, 1.17), 0.17, 0.18)
    box("Torso", "chest", "suit", (0.46, 0.30, 0.34), (0, 0, 1.3), bevel=0.08)
    box("ChestPlate", "chest", "accent", (0.32, 0.04, 0.17), (0, 0.15, 1.33), bevel=0.02)
    box("ChestPanel", "chest", "joint", (0.14, 0.03, 0.07), (0.0, 0.17, 1.24), bevel=0.01)
    sphere("ChestLightL", "chest", "light", 0.012, (0.035, 0.187, 1.245))
    sphere("ChestLightR", "chest", "light", 0.012, (-0.035, 0.187, 1.245))
    ring("NeckRing", "chest", "metal", (0, 0, NECK_Z), 0.13, 0.025)

    # Life support backpack with two tanks.
    box("Backpack", "chest", "suit", (0.38, 0.16, 0.42), (0, -0.22, 1.28), bevel=0.04)
    box("BackpackStripe", "chest", "accent", (0.39, 0.165, 0.04), (0, -0.22, 1.4), bevel=0.01)
    for side in (1, -1):
        cylinder(f"Tank{side}", "chest", "metal", 0.055, 0.36, (0.1 * side, -0.32, 1.27))
        sphere(f"TankCap{side}", "chest", "metal", 0.055, (0.1 * side, -0.32, 1.45), scale=(1, 1, 0.6))
    box("Antenna", "chest", "joint", (0.015, 0.015, 0.18), (0.15, -0.24, 1.58), bevel=0.0)
    sphere("AntennaTip", "chest", "light", 0.016, (0.15, -0.24, 1.68))

    # Helmet.
    sphere("Helmet", "head", "suit", 0.19, (0, 0, 1.66), scale=(1, 1.02, 1.03), segments=28, rings=16)
    visor_shell((0, 0, 1.66), 0.2)
    box("HelmetRidge", "head", "accent", (0.045, 0.32, 0.05), (0, -0.02, 1.84), bevel=0.015)
    for side in (1, -1):
        cylinder(f"Ear{side}", "head", "metal", 0.045, 0.05, (0.185 * side, 0, 1.66), rotation=(0, math.pi / 2, 0))
        sphere(f"HeadLamp{side}", "head", "light", 0.02, (0.15 * side, 0.11, 1.75))

    for side, suffix in ((1, "L"), (-1, "R")):
        # Arms.
        sphere(f"Shoulder.{suffix}", f"upper_arm.{suffix}", "accent", 0.1, mirror(SHOULDER, side), scale=(1, 1, 0.9))
        limb(f"UpperArm.{suffix}", f"upper_arm.{suffix}", "suit", mirror(SHOULDER, side), mirror(ELBOW, side), 0.085, 0.074)
        sphere(f"Elbow.{suffix}", f"forearm.{suffix}", "joint", 0.072, mirror(ELBOW, side))
        limb(f"Forearm.{suffix}", f"forearm.{suffix}", "suit", mirror(ELBOW, side), mirror(WRIST, side), 0.074, 0.066)
        ring(f"Cuff.{suffix}", f"forearm.{suffix}", "accent", mirror((0.313, 0.0, 0.92), side), 0.07, 0.018)
        box(f"Glove.{suffix}", f"hand.{suffix}", "joint", (0.07, 0.10, 0.12), mirror((0.32, 0.005, 0.81), side), bevel=0.025)
        box(f"Thumb.{suffix}", f"hand.{suffix}", "joint", (0.035, 0.04, 0.065), mirror((0.29, 0.055, 0.82), side), bevel=0.012)
        box(f"Wristpad.{suffix}", f"forearm.{suffix}", "metal", (0.03, 0.07, 0.08), mirror((0.375, 0.0, 1.0), side), bevel=0.01)

        # Legs.
        limb(f"Thigh.{suffix}", f"thigh.{suffix}", "suit", mirror(HIP_JOINT, side), mirror(KNEE, side), 0.115, 0.092)
        sphere(f"Knee.{suffix}", f"shin.{suffix}", "accent", 0.082, mirror((0.11, 0.045, 0.52), side), scale=(1, 0.8, 1))
        limb(f"Shin.{suffix}", f"shin.{suffix}", "suit", mirror(KNEE, side), mirror((0.11, -0.01, 0.17), side), 0.09, 0.078)
        box(f"Boot.{suffix}", f"foot.{suffix}", "joint", (0.15, 0.30, 0.13), mirror((0.11, 0.05, 0.065), side), bevel=0.045)
        box(f"BootCuff.{suffix}", f"shin.{suffix}", "accent", (0.18, 0.19, 0.07), mirror((0.11, -0.005, 0.17), side), bevel=0.02)
        box(f"Sole.{suffix}", f"foot.{suffix}", "metal", (0.155, 0.31, 0.025), mirror((0.11, 0.05, 0.0125), side), bevel=0.008)


# ---------------------------------------------------------------- armature

BONES = [
    # name, head, tail, parent
    ("root", (0, 0, 0), (0, 0.25, 0), None),
    ("hips", (0, 0, HIP_Z), (0, 0, CHEST_Z - 0.17), "root"),
    ("spine", (0, 0, CHEST_Z - 0.17), (0, 0, CHEST_Z), "hips"),
    ("chest", (0, 0, CHEST_Z), (0, 0, NECK_Z), "spine"),
    ("head", (0, 0, HEAD_Z), (0, 0, 1.85), "chest"),
]
for _side, _s in ((1, "L"), (-1, "R")):
    BONES += [
        (f"upper_arm.{_s}", mirror(SHOULDER, _side), mirror(ELBOW, _side), "chest"),
        (f"forearm.{_s}", mirror(ELBOW, _side), mirror(WRIST, _side), f"upper_arm.{_s}"),
        (f"hand.{_s}", mirror(WRIST, _side), mirror(HAND_END, _side), f"forearm.{_s}"),
        (f"thigh.{_s}", mirror(HIP_JOINT, _side), mirror(KNEE, _side), "hips"),
        (f"shin.{_s}", mirror(KNEE, _side), mirror(ANKLE, _side), f"thigh.{_s}"),
        (f"foot.{_s}", mirror(ANKLE, _side), mirror(TOE, _side), f"shin.{_s}"),
    ]


def build_armature():
    data = bpy.data.armatures.new("ExplorerRig")
    rig = bpy.data.objects.new("Explorer", data)
    bpy.context.scene.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    for name, head, tail, parent in BONES:
        bone = data.edit_bones.new(name)
        bone.head = head
        bone.tail = tail
        bone.roll = 0.0
        if parent:
            bone.parent = data.edit_bones[parent]
            bone.use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    return rig


def join_and_skin(rig):
    bpy.ops.object.select_all(action="DESELECT")
    for obj, bone in PARTS:
        group = obj.vertex_groups.new(name=bone)
        group.add(list(range(len(obj.data.vertices))), 1.0, "REPLACE")
    for obj, _ in PARTS:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = PARTS[0][0]
    bpy.ops.object.join()
    body = bpy.context.active_object
    body.name = "ExplorerMesh"
    body.data.name = "ExplorerMesh"
    body.parent = rig
    mod = body.modifiers.new("Armature", "ARMATURE")
    mod.object = rig
    return body


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


def key(rig, frame, poses, hips_offset=None):
    """poses: bone -> (pitch, roll, yaw). Every bone is keyed on every key frame so actions are self-contained."""
    for pb in rig.pose.bones:
        p = poses.get(pb.name, (0.0, 0.0, 0.0))
        pb.rotation_quaternion = rot(rig, pb.name, *p)
        pb.keyframe_insert("rotation_quaternion", frame=frame)
        if pb.name == "hips":
            offset = Vector(hips_offset or (0, 0, 0))
            # Offset given in world metres; hips rest axes map it into bone space.
            pb.location = rig.data.bones["hips"].matrix_local.to_3x3().inverted() @ offset
            pb.keyframe_insert("location", frame=frame)


def mirror_pose(p):
    return {k.replace(".L", ".X").replace(".R", ".L").replace(".X", ".R"): (v[0], -v[1], -v[2]) for k, v in p.items()}


def cycle(rig, name, length, frames):
    """frames: list of (t in 0..1, poses, hips_offset); the first pose is repeated at the end for a seamless loop."""
    new_action(rig, name)
    for t, poses, hips in frames:
        key(rig, 1 + round(t * length), poses, hips)
    key(rig, 1 + length, frames[0][1], frames[0][2])


def gait(stride, knee, arm, elbow, lean, bob, contact_drop):
    """Half a gait cycle for the left leg forward; the second half is the mirror."""
    def pose(left_thigh, left_shin, right_thigh, right_shin, left_arm, right_arm, twist):
        return {
            "spine": (lean * 0.5, 0, twist * 0.5),
            "chest": (lean * 0.5, 0, twist),
            "head": (-lean * 0.8, 0, -twist * 1.2),
            "thigh.L": (left_thigh, 0, 0), "shin.L": (left_shin, 0, 0), "foot.L": (-left_thigh * 0.3 - left_shin * 0.4, 0, 0),
            "thigh.R": (right_thigh, 0, 0), "shin.R": (right_shin, 0, 0), "foot.R": (-right_thigh * 0.3 - right_shin * 0.4, 0, 0),
            "upper_arm.L": (left_arm, 6, 0), "forearm.L": (elbow + max(left_arm, 0) * 0.6, 0, 0),
            "upper_arm.R": (right_arm, -6, 0), "forearm.R": (elbow + max(right_arm, 0) * 0.6, 0, 0),
            "hand.L": (5, 0, 0), "hand.R": (5, 0, 0),
        }

    return [
        (0.0, pose(stride, -6, -stride, -knee * 0.35, -arm, arm, 6), (0, 0, -contact_drop)),
        (0.125, pose(stride * 0.5, -knee * 0.25, -stride * 0.6, -knee, -arm * 0.5, arm * 0.5, 3), (0, 0, 0)),
        (0.25, pose(0, -8, stride * 0.2, -knee * 0.9, 0, 0, 0), (0, 0, bob)),
        (0.375, pose(-stride * 0.5, -6, stride * 0.8, -knee * 0.5, arm * 0.5, -arm * 0.5, -3), (0, 0, 0)),
        (0.5, pose(-stride, -knee * 0.35, stride, -6, arm, -arm, -6), (0, 0, -contact_drop)),
        (0.625, pose(-stride * 0.6, -knee, stride * 0.5, -knee * 0.25, arm * 0.5, -arm * 0.5, -3), (0, 0, 0)),
        (0.75, pose(stride * 0.2, -knee * 0.9, 0, -8, 0, 0, 0), (0, 0, bob)),
        (0.875, pose(stride * 0.8, -knee * 0.5, -stride * 0.5, -6, -arm * 0.5, arm * 0.5, 3), (0, 0, 0)),
    ]


def build_animations(rig):
    rig.animation_data_create()
    rest_arms = {"upper_arm.L": (2, 7, 0), "upper_arm.R": (2, -7, 0), "forearm.L": (10, 0, 0), "forearm.R": (10, 0, 0),
                 "hand.L": (5, 0, 0), "hand.R": (5, 0, 0)}

    # Idle: breathing and a slow look around, 3 s.
    def idle(breath, look):
        p = dict(rest_arms)
        p.update({"chest": (-breath, 0, 0), "spine": (breath * 0.3, 0, 0), "head": (breath * 0.5, look * 0.3, look),
                  "upper_arm.L": (2 + breath, 7 + breath, 0), "upper_arm.R": (2 + breath, -7 - breath, 0),
                  "thigh.L": (2, 1, 0), "thigh.R": (-1, -1, 0), "shin.L": (-3, 0, 0), "foot.L": (1, 0, 0)})
        return p

    cycle(rig, "Idle", 90, [(0.0, idle(0, 0), (0, 0, 0)), (0.25, idle(2.5, 8), (0, 0, 0.006)),
                            (0.5, idle(0, 0), (0, 0, 0)), (0.75, idle(2.5, -6), (0, 0, 0.006))])

    cycle(rig, "Walk", 34, gait(stride=26, knee=55, arm=22, elbow=14, lean=4, bob=0.02, contact_drop=0.025))
    cycle(rig, "Run", 22, gait(stride=42, knee=100, arm=42, elbow=65, lean=14, bob=0.05, contact_drop=0.06))
    cycle(rig, "Sprint", 18, gait(stride=55, knee=115, arm=55, elbow=80, lean=22, bob=0.06, contact_drop=0.07))

    # Jump: crouch and push off, held at the end (not a loop).
    new_action(rig, "Jump")
    crouch = {"thigh.L": (40, 0, 0), "thigh.R": (40, 0, 0), "shin.L": (-70, 0, 0), "shin.R": (-70, 0, 0),
              "foot.L": (30, 0, 0), "foot.R": (30, 0, 0), "spine": (10, 0, 0), "chest": (8, 0, 0),
              "upper_arm.L": (-25, 10, 0), "upper_arm.R": (-25, -10, 0), "forearm.L": (30, 0, 0), "forearm.R": (30, 0, 0)}
    push = {"thigh.L": (10, 0, 0), "thigh.R": (-5, 0, 0), "shin.L": (-25, 0, 0), "shin.R": (-10, 0, 0),
            "foot.L": (-20, 0, 0), "foot.R": (-30, 0, 0), "spine": (-3, 0, 0),
            "upper_arm.L": (60, 20, 0), "upper_arm.R": (60, -20, 0), "forearm.L": (40, 0, 0), "forearm.R": (40, 0, 0)}
    key(rig, 1, crouch, (0, 0, -0.18))
    key(rig, 6, push, (0, 0, 0.02))
    key(rig, 12, push, (0, 0, 0.02))

    # Fall: arms out for balance, legs ready to land.
    def fall(f):
        return {"thigh.L": (25 + f, 3, 0), "thigh.R": (10 - f, -3, 0), "shin.L": (-40, 0, 0), "shin.R": (-30 - f, 0, 0),
                "foot.L": (10, 0, 0), "foot.R": (10, 0, 0), "spine": (-4, 0, 0),
                "upper_arm.L": (20 + f, 55 + f, 0), "upper_arm.R": (20 - f, -55 + f, 0),
                "forearm.L": (25, 0, 0), "forearm.R": (25, 0, 0), "head": (6, 0, 0)}
    cycle(rig, "Fall", 40, [(0.0, fall(0), (0, 0, 0)), (0.5, fall(6), (0, 0, 0.01))])

    # Fly (dev flight): streamlined, slow drift.
    def fly(f):
        return {"spine": (12, 0, 0), "chest": (6, 0, 0), "head": (-14, 0, 0),
                "thigh.L": (-8 + f, 0, 0), "thigh.R": (-12 - f, 0, 0), "shin.L": (-20, 0, 0), "shin.R": (-14, 0, 0),
                "foot.L": (-30, 0, 0), "foot.R": (-30, 0, 0),
                "upper_arm.L": (-10 + f, 18, 0), "upper_arm.R": (-10 - f, -18, 0), "forearm.L": (15, 0, 0), "forearm.R": (15, 0, 0)}
    cycle(rig, "Fly", 60, [(0.0, fly(0), (0, 0, 0)), (0.5, fly(5), (0, 0, 0.015))])

    rig.animation_data.action = bpy.data.actions["Idle"]


# ---------------------------------------------------------------- export and preview

def export(rig, path):
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    for child in rig.children:
        child.select_set(True)
    bpy.ops.export_scene.gltf(
        filepath=path,
        export_format="GLB",
        use_selection=True,
        export_apply=True,
        export_animations=True,
        export_animation_mode="ACTIONS",
        export_force_sampling=True,
        export_frame_step=1,
        export_def_bones=False,
        export_yup=True,
    )


def preview(rig, directory):
    os.makedirs(directory, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else "BLENDER_EEVEE"
    scene.render.resolution_x = 640
    scene.render.resolution_y = 800
    world = bpy.data.worlds.new("Preview")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.35, 0.42, 0.5, 1)
    scene.world = world
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 4
    sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(30))
    scene.collection.objects.link(sun)
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    shots = [("front", (2.2, 3.6, 1.4), "Idle", 1), ("side_walk", (4.2, 0.0, 1.1), "Walk", 9),
             ("run", (3.0, 3.0, 1.2), "Run", 6), ("back", (-1.5, -3.8, 1.6), "Idle", 1),
             ("jump", (3.0, 3.0, 1.2), "Jump", 1), ("fall", (3.0, 3.0, 1.2), "Fall", 1)]
    for name, position, action, frame in shots:
        rig.animation_data.action = bpy.data.actions[action]
        scene.frame_set(frame)
        cam.location = position
        direction = Vector((0, 0, 0.9)) - Vector(position)
        cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(directory, f"{name}.png")
        bpy.ops.render.render(write_still=True)


def main():
    args = parse_args()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = FPS
    materials()
    build_body()
    rig = build_armature()
    join_and_skin(rig)
    build_animations(rig)
    export(rig, args.out)
    if args.preview:
        preview(rig, args.preview)
    print("EXPLORER OK", args.out)


main()
