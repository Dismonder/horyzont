"""Builds the low-poly flora and rocks of the home planet in Blender and exports them to one glTF binary.

Everything is generated from this script (fixed random seed), so the asset has no external source (provenance: own work).

    blender --background --factory-startup --python tools/art/planet_flora.py -- \
        --out game/assets/planet/flora.glb [--preview DIR]

Each top-level mesh object is one instancer item (game/src/Planet/Rendering/PlanetFlora.cs reads them by name): its origin is
the point where it meets the ground, Blender Z up (Godot +Y), sizes in metres before the random scale of the instancer.
"""

import argparse
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Vector, noise

RNG = random.Random(4207)


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", required=True)
    parser.add_argument("--preview", default=None)
    return parser.parse_args(argv)


def srgb(hex_color):
    def channel(v):
        c = v / 255.0
        return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4
    return tuple(channel(int(hex_color[i:i + 2], 16)) for i in (0, 2, 4))


def material(name, hex_color, roughness=0.85, emission=None, strength=0.0, metallic=0.0):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*srgb(hex_color), 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    if emission:
        bsdf.inputs["Emission Color"].default_value = (*srgb(emission), 1.0)
        bsdf.inputs["Emission Strength"].default_value = strength
    return mat


MATS = {}


def materials():
    MATS["bark"] = material("Bark", "5A3F2B")
    MATS["bark_light"] = material("BarkLight", "8A7560")
    MATS["pine"] = material("PineNeedles", "2F5A32")
    MATS["leaves"] = material("Leaves", "4E8A34")
    MATS["leaves_autumn"] = material("LeavesAutumn", "C2722C")
    MATS["bush"] = material("Bush", "3E7030")
    MATS["grass"] = material("Grass", "5C8F2E")
    MATS["flower_a"] = material("FlowerA", "E9D34A", roughness=0.6)
    MATS["flower_b"] = material("FlowerB", "D9577A", roughness=0.6)
    MATS["flower_c"] = material("FlowerC", "8FA8F0", roughness=0.6)
    MATS["rock"] = material("Rock", "7B7873", roughness=0.95)
    MATS["rock_moss"] = material("RockMoss", "4E6B35", roughness=0.95)
    MATS["crystal"] = material("Crystal", "57E4F2", roughness=0.15, emission="3FD8F0", strength=2.5, metallic=0.1)


def new_object(name, mesh):
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def bm_to_object(name, bm, mats):
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    for m in mats:
        mesh.materials.append(MATS[m])
    obj = new_object(name, mesh)
    return obj


def add_cone(bm, base, height, r0, r1, segments, mat_index, jitter=0.0, tilt=Vector((0, 0, 0))):
    geom = bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segments, radius1=r0, radius2=r1, depth=height)
    verts = geom["verts"]
    for v in verts:
        v.co.z += height / 2
        v.co += Vector((RNG.uniform(-jitter, jitter), RNG.uniform(-jitter, jitter), RNG.uniform(-jitter, jitter) * 0.5))
        v.co += tilt * (v.co.z / max(height, 1e-3))
        v.co += Vector(base)
    for f in {f for v in verts for f in v.link_faces}:
        f.material_index = mat_index
    return verts


def add_blob(bm, center, radius, subdivisions, mat_index, squash=1.0, roughness=0.25, seed=0.0):
    geom = bmesh.ops.create_icosphere(bm, subdivisions=subdivisions, radius=radius)
    verts = geom["verts"]
    for v in verts:
        n = v.co.normalized()
        bump = noise.noise(n * 2.3 + Vector((seed, seed * 1.7, seed * 0.3))) * roughness * radius
        v.co = n * (radius + bump)
        v.co.z *= squash
        v.co += Vector(center)
    for f in {f for v in verts for f in v.link_faces}:
        f.material_index = mat_index
    return verts


def shade(obj, smooth):
    for p in obj.data.polygons:
        p.use_smooth = smooth


# ---------------------------------------------------------------- items

def pine(name, height, tiers):
    bm = bmesh.new()
    add_cone(bm, (0, 0, -0.4), height * 0.35 + 0.4, 0.22, 0.14, 7, 0, jitter=0.02)
    base = height * 0.18
    for i in range(tiers):
        t = i / max(tiers - 1, 1)
        radius = (1.0 - t * 0.75) * height * 0.24
        tier_h = height * (0.42 - t * 0.12)
        add_cone(bm, (0, 0, base), tier_h, radius, 0.04, 9, 1, jitter=radius * 0.08,
                 tilt=Vector((RNG.uniform(-0.15, 0.15), RNG.uniform(-0.15, 0.15), 0)))
        base += height * (0.82 - 0.18) / tiers
    obj = bm_to_object(name, bm, ["bark", "pine"])
    shade(obj, False)
    return obj


def broadleaf(name, height, leaves="leaves"):
    bm = bmesh.new()
    trunk_h = height * 0.5
    add_cone(bm, (0, 0, -0.4), trunk_h + 0.4, 0.32, 0.2, 8, 0, jitter=0.03)
    # Two branches.
    for angle in (0.6, 3.4):
        direction = Vector((math.cos(angle), math.sin(angle), 1.2)).normalized()
        start = Vector((0, 0, trunk_h * 0.7))
        verts = add_cone(bm, (0, 0, 0), height * 0.3, 0.12, 0.06, 6, 0)
        rot = Vector((0, 0, 1)).rotation_difference(direction).to_matrix()
        for v in verts:
            v.co = rot @ v.co + start
    crown = Vector((0, 0, trunk_h + height * 0.22))
    add_blob(bm, crown, height * 0.3, 2, 1, squash=0.85, seed=RNG.random() * 10)
    for _ in range(4):
        offset = Vector((RNG.uniform(-1, 1), RNG.uniform(-1, 1), RNG.uniform(-0.3, 0.5))).normalized() * height * 0.22
        add_blob(bm, crown + offset, height * RNG.uniform(0.17, 0.23), 2, 1, squash=0.85, seed=RNG.random() * 10)
    obj = bm_to_object(name, bm, ["bark", leaves])
    shade(obj, False)
    return obj


def bush(name, size, mat="bush"):
    bm = bmesh.new()
    for _ in range(5):
        offset = Vector((RNG.uniform(-0.5, 0.5), RNG.uniform(-0.5, 0.5), 0)) * size
        add_blob(bm, offset + Vector((0, 0, size * 0.35)), size * RNG.uniform(0.35, 0.5), 1, 0, squash=0.8, seed=RNG.random() * 10)
    obj = bm_to_object(name, bm, [mat])
    shade(obj, False)
    return obj


def grass(name, blades, height, flowers=None):
    bm = bmesh.new()
    for _ in range(blades):
        angle = RNG.uniform(0, math.tau)
        r = RNG.uniform(0.0, 0.18)
        base = Vector((math.cos(angle) * r, math.sin(angle) * r, -0.05))
        h = height * RNG.uniform(0.6, 1.2)
        width = RNG.uniform(0.025, 0.045)
        side = Vector((-math.sin(angle), math.cos(angle), 0)) * width
        lean = Vector((math.cos(angle), math.sin(angle), 0)) * h * RNG.uniform(0.15, 0.4)
        v0 = bm.verts.new(base - side)
        v1 = bm.verts.new(base + side)
        v2 = bm.verts.new(base + lean * 0.5 + Vector((0, 0, h * 0.55)) + side * 0.6)
        v3 = bm.verts.new(base + lean * 0.5 + Vector((0, 0, h * 0.55)) - side * 0.6)
        tip = bm.verts.new(base + lean + Vector((0, 0, h)))
        bm.faces.new((v0, v1, v2, v3)).material_index = 0
        bm.faces.new((v3, v2, tip)).material_index = 0
    mats = ["grass"]
    if flowers:
        mats.append(flowers)
        for _ in range(3):
            angle = RNG.uniform(0, math.tau)
            pos = Vector((math.cos(angle) * 0.15, math.sin(angle) * 0.15, height * RNG.uniform(0.9, 1.3)))
            add_blob(bm, pos, 0.045, 1, 1, squash=0.7, roughness=0.0)
    obj = bm_to_object(name, bm, mats)
    shade(obj, False)
    return obj


def rock(name, size, moss=False, squash=0.65):
    bm = bmesh.new()
    seed = RNG.random() * 100
    geom = bmesh.ops.create_icosphere(bm, subdivisions=2, radius=size)
    for v in geom["verts"]:
        n = v.co.normalized()
        d = noise.noise(n * 1.6 + Vector((seed, 0, 0))) * 0.35 + noise.noise(n * 4.0 + Vector((0, seed, 0))) * 0.12
        v.co = n * size * (1.0 + d)
        v.co.z = v.co.z * squash + size * 0.25
    if moss:
        for f in bm.faces:
            f.material_index = 1 if f.normal.z > 0.55 else 0
    obj = bm_to_object(name, bm, ["rock", "rock_moss"] if moss else ["rock"])
    shade(obj, False)
    return obj


def crystals(name, size):
    bm = bmesh.new()
    for i in range(6):
        h = size * RNG.uniform(0.5, 1.2) * (1.4 if i == 0 else 1.0)
        r = size * RNG.uniform(0.08, 0.14)
        verts = add_cone(bm, (0, 0, 0), h, r, r * 0.9, 6, 0)
        tip = bmesh.ops.create_cone(bm, cap_ends=True, segments=6, radius1=r * 0.9, radius2=0.0, depth=r * 1.6)["verts"]
        for v in tip:
            v.co.z += h + r * 0.8
        verts = list(verts) + list(tip)
        direction = Vector((RNG.uniform(-0.6, 0.6), RNG.uniform(-0.6, 0.6), 1.0)).normalized() if i else Vector((0, 0, 1))
        rot = Vector((0, 0, 1)).rotation_difference(direction).to_matrix()
        offset = Vector((RNG.uniform(-0.15, 0.15), RNG.uniform(-0.15, 0.15), -0.1)) * size
        for v in verts:
            v.co = rot @ v.co + offset
    obj = bm_to_object(name, bm, ["crystal"])
    shade(obj, False)
    return obj


def build():
    items = [
        pine("Pine", 9.0, 4),
        pine("PineTall", 13.0, 5),
        broadleaf("Broadleaf", 8.0),
        broadleaf("BroadleafAutumn", 7.0, leaves="leaves_autumn"),
        bush("Bush", 1.3),
        grass("Grass", 14, 0.45),
        grass("GrassFlowersYellow", 10, 0.4, flowers="flower_a"),
        grass("GrassFlowersPink", 10, 0.4, flowers="flower_b"),
        grass("GrassFlowersBlue", 10, 0.4, flowers="flower_c"),
        rock("Rock", 0.6),
        rock("Boulder", 1.6, moss=True),
        crystals("Crystals", 1.2),
    ]
    # Spread them out for the preview only; export keeps every origin at the ground point.
    return items


def export(items, path):
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in items:
        obj.select_set(True)
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True, export_apply=True, export_yup=True,
                              export_animations=False)


def preview(items, directory):
    os.makedirs(directory, exist_ok=True)
    scene = bpy.context.scene
    for i, obj in enumerate(items):
        obj.location = ((i % 6) * 6.0 - 15.0, (i // 6) * 9.0, 0)
    scene.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else "BLENDER_EEVEE"
    scene.render.resolution_x = 1200
    scene.render.resolution_y = 700
    world = bpy.data.worlds.new("Preview")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.45, 0.55, 0.65, 1)
    scene.world = world
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 4
    sun.rotation_euler = (math.radians(45), math.radians(15), math.radians(35))
    scene.collection.objects.link(sun)
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.location = (0, -26, 14)
    cam.rotation_euler = (Vector((0, 4.5, 2.5)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(directory, "flora.png")
    bpy.ops.render.render(write_still=True)
    for obj in items:
        obj.location = (0, 0, 0)


def main():
    args = parse_args()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    materials()
    items = build()
    export(items, args.out)
    if args.preview:
        preview(items, args.preview)
    print("FLORA OK", args.out, len(items))


main()
