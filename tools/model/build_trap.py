"""
Builds the Simple Fishing Trap in Blender (bpy), renders previews + icon, exports OBJ.

Blender axes: X = right, -Y = front (faces the player), Z = up. Pivot = world origin, ground at Z=0.
Export uses forward=-Z, up=Y; the mod's loader mirrors X, so the front ends up facing Unity +Z.
"""
import math
import random
import sys
import os

import bpy
import bmesh
from mathutils import Vector, Matrix

OUT = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else "/home/claude/trapmodel/out"
ATLAS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "SimpleFishingTrap_Albedo.png")
os.makedirs(OUT, exist_ok=True)

# ---------------------------------------------------------------- dimensions (metres)
CHEST_W, CHEST_H = 1.2, 0.75          # assumed vanilla wood chest size
TRAP_W, TRAP_H = CHEST_W * 1.5, CHEST_H * 2.0   # 1.8 x 1.5
STAKE_W = 0.15                         # chunky, like a wood beam
NET_THICK = 0.0058                     # strand radius
NU, NV = 10, 7                         # strand counts (vertical, horizontal)

random.seed(11)

REGIONS = {
    "wood":  (0.00, 0.00, 0.50, 1.00),
    "nylon": (0.50, 0.50, 0.75, 1.00),
    "twine": (0.75, 0.50, 1.00, 1.00),
    "stone": (0.50, 0.00, 0.75, 0.50),
    "cork":  (0.75, 0.25, 1.00, 0.50),
    "fish":  (0.75, 0.00, 1.00, 0.25),
}


def ruv(region, s, t, inset=0.01):
    u0, v0, u1, v1 = REGIONS[region]
    s = min(max(s, 0.0), 1.0)
    t = min(max(t, 0.0), 1.0)
    return (u0 + inset + s * (u1 - u0 - 2 * inset), v0 + inset + t * (v1 - v0 - 2 * inset))


def jit(a):
    return random.uniform(-a, a)


# ---------------------------------------------------------------- scene reset
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
EXPORT = []  # objects that go into the OBJ


def to_object(name, bm, smooth=False, export=True):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = smooth
    ob = bpy.data.objects.new(name, me)
    scene.collection.objects.link(ob)
    if export:
        EXPORT.append(ob)
    return ob


def set_uvs(bm, fn):
    """fn(face, loop) -> (u, v)"""
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        for l in f.loops:
            l[uv].uv = fn(f, l)


# ---------------------------------------------------------------- stakes
class Stake:
    def __init__(self, base, top):
        self.base, self.top = Vector(base), Vector(top)

    def at(self, z):
        t = (z - self.base.z) / (self.top.z - self.base.z)
        return self.base.lerp(self.top, t)

    @property
    def axis(self):
        return (self.top - self.base).normalized()


def build_stake(name, stake, seed):
    random.seed(seed)
    axis = stake.axis
    a = axis.cross(Vector((0, 0, 1)) if abs(axis.z) < 0.99 else Vector((1, 0, 0)))
    a = Vector((1, 0, 0)) if a.length < 1e-6 else a.normalized()
    # keep faces roughly aligned to X/Y so it reads as a squared beam
    a = Vector((1, 0, 0)) - axis * axis.dot(Vector((1, 0, 0)))
    a.normalize()
    b = axis.cross(a).normalized()

    length = (stake.top - stake.base).length
    levels = [0.0, 0.10, 0.30, 0.55, 0.80, 1.0]
    bm = bmesh.new()
    rings = []
    bend = Vector((0, 0, 0))
    for i, t in enumerate(levels):
        if 0 < i < len(levels) - 1:
            bend = bend + a * jit(0.010) + b * jit(0.010)
        center = stake.base + axis * (t * length) + bend
        hw = STAKE_W / 2 * (0.92 + 0.08 * t)  # slight taper toward the bottom
        ring = []
        for k, (sa, sb) in enumerate([(-1, -1), (1, -1), (1, 1), (-1, 1)]):
            p = center + a * (sa * hw * random.uniform(0.88, 1.08)) + b * (sb * hw * random.uniform(0.88, 1.08))
            if i == len(levels) - 1:
                p = p + axis * jit(0.025)  # hasty, uneven top cut
            ring.append(bm.verts.new(p))
        rings.append(ring)

    tip = bm.verts.new(stake.base - axis * 0.12 + a * jit(0.01))  # sharpened point (in the ground)

    sides = []
    for i in range(len(rings) - 1):
        r0, r1 = rings[i], rings[i + 1]
        for k in range(4):
            f = bm.faces.new((r0[k], r0[(k + 1) % 4], r1[(k + 1) % 4], r1[k]))
            f.material_index = 0
            sides.append((f, k, levels[i], levels[i + 1]))
    top = bm.faces.new(rings[-1])
    for k in range(4):
        bm.faces.new((rings[0][(k + 1) % 4], rings[0][k], tip))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)

    # UVs: each side gets its own vertical strip of the wood texture, grain along the beam.
    uv = bm.loops.layers.uv.verify()
    side_lookup = {f: (k, t0, t1) for f, k, t0, t1 in sides}
    off = random.uniform(0, 0.5)
    for f in bm.faces:
        if f in side_lookup:
            k, t0, t1 = side_lookup[f]
            for l in f.loops:
                vi = l.vert
                # which ring/corner
                for ri, ring in enumerate(rings):
                    if vi in ring:
                        ci = ring.index(vi)
                        t = levels[ri]
                        break
                s = 0.25 * k + (0.0 if ci == k else 0.22)
                l[uv].uv = ruv("wood", (s + off) % 1.0 * 0.98, 0.03 + t * 0.94)
        else:
            for n, l in enumerate(f.loops):
                l[uv].uv = ruv("wood", 0.1 + 0.08 * (n % 2), 0.02 + 0.03 * (n // 2))
    return to_object(name, bm)


GROUND = 0.0
left = Stake((-TRAP_W / 2 + 0.10, 0.0, -0.15), (-TRAP_W / 2 - 0.05, 0.06, TRAP_H - 0.07))
right = Stake((TRAP_W / 2 - 0.12, -0.02, -0.15), (TRAP_W / 2 - 0.02, -0.08, TRAP_H))
build_stake("StakeLeft", left, 1)
build_stake("StakeRight", right, 2)

# ---------------------------------------------------------------- net surface
NET_FRONT = -STAKE_W / 2 - 0.01           # tied to the front faces of the stakes
TOP_L, TOP_R, SAG = 1.30, 1.34, 0.17
BOT = 0.06
BELLY = 0.32

random.seed(5)
noise_grid = [[(jit(1), jit(1), jit(1)) for _ in range(64)] for _ in range(64)]


def noise3(u, v):
    x, y = u * 12, v * 9
    i, j = int(x), int(y)
    fx, fy = x - i, y - j
    def g(a, b):
        return Vector(noise_grid[a % 64][b % 64])
    return (g(i, j) * (1 - fx) * (1 - fy) + g(i + 1, j) * fx * (1 - fy)
            + g(i, j + 1) * (1 - fx) * fy + g(i + 1, j + 1) * fx * fy)


def net_point(u, v):
    s = math.sin(math.pi * u)
    top = TOP_L + (TOP_R - TOP_L) * u - SAG * s
    bot = BOT + 0.03 * s
    z = bot + (top - bot) * v
    pl, pr = left.at(z), right.at(z)
    x = pl.x + (pr.x - pl.x) * u
    y = NET_FRONT + (pl.y + pr.y) / 2 * 0 + BELLY * s * (1 - v) ** 1.3
    n = noise3(u, v) * 0.018 * s
    return Vector((x, y, z)) + n


def strand_curve(name, polylines, radius=NET_THICK):
    cu = bpy.data.curves.new(name, "CURVE")
    cu.dimensions = "3D"
    cu.bevel_depth = radius
    cu.bevel_resolution = 0
    cu.use_fill_caps = False
    for pts in polylines:
        sp = cu.splines.new("POLY")
        sp.points.add(len(pts) - 1)
        for i, p in enumerate(pts):
            sp.points[i].co = (p.x, p.y, p.z, 1.0)
    ob = bpy.data.objects.new(name + "_curve", cu)
    scene.collection.objects.link(ob)
    deps = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(deps))
    bpy.data.objects.remove(ob)
    me.name = name
    mob = bpy.data.objects.new(name, me)
    scene.collection.objects.link(mob)
    bm = bmesh.new()
    bm.from_mesh(me)
    bmin = Vector((min(v.co.x for v in bm.verts), 0, min(v.co.z for v in bm.verts)))
    set_uvs(bm, lambda f, l: ruv("nylon", (l.vert.co.x - bmin.x) * 0.6 % 1.0, (l.vert.co.z - bmin.z) * 0.6 % 1.0))
    bm.to_mesh(me)
    bm.free()
    for p in me.polygons:
        p.use_smooth = False
    EXPORT.append(mob)
    return mob


SEG = 12
lines = []
for i in range(NU):
    u = i / (NU - 1)
    lines.append([net_point(u, j / SEG) for j in range(SEG + 1)])
for j in range(NV):
    v = j / (NV - 1)
    lines.append([net_point(i / SEG, v) for i in range(SEG + 1)])
strand_curve("Net", lines)



# ---------------------------------------------------------------- rope lashings
def lashing(name, stake, z, seed):
    random.seed(seed)
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.verify()
    c0 = stake.at(z)
    ax = stake.axis
    for w in range(2):
        tilt = Matrix.Rotation(jit(0.25), 3, "X") @ Matrix.Rotation(jit(0.25), 3, "Y")
        R = STAKE_W * 0.62 + 0.01
        r = 0.013
        M, N = 8, 3
        center = c0 + ax * (w * 0.032)
        grid = []
        for i in range(M):
            th = 2 * math.pi * i / M
            row = []
            for k in range(N):
                ph = 2 * math.pi * k / N
                p = Vector(((R + r * math.cos(ph)) * math.cos(th), (R + r * math.cos(ph)) * math.sin(th), r * math.sin(ph)))
                row.append(bm.verts.new(center + tilt @ p))
            grid.append(row)
        for i in range(M):
            for k in range(N):
                f = bm.faces.new((grid[i][k], grid[(i + 1) % M][k], grid[(i + 1) % M][(k + 1) % N], grid[i][(k + 1) % N]))
                for n, l in enumerate(f.loops):
                    l[uvl].uv = ruv("twine", ((i + (n in (1, 2))) / M), (k + (n in (2, 3))) / N)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return to_object(name, bm)


lashing("LashLeftTop", left, TOP_L - 0.02, 21)
lashing("LashLeftBottom", left, 0.12, 22)
lashing("LashRightTop", right, TOP_R - 0.02, 23)
lashing("LashRightBottom", right, 0.12, 24)

# ---------------------------------------------------------------- cork floats
def cylinder(bm, start, end, radius, segs, region):
    uvl = bm.loops.layers.uv.verify()
    axis = (end - start)
    length = axis.length
    axis.normalize()
    a = axis.orthogonal().normalized()
    b = axis.cross(a)
    r0, r1 = [], []
    for i in range(segs):
        th = 2 * math.pi * i / segs
        d = a * math.cos(th) * radius + b * math.sin(th) * radius
        r0.append(bm.verts.new(start + d))
        r1.append(bm.verts.new(end + d))
    for i in range(segs):
        f = bm.faces.new((r0[i], r0[(i + 1) % segs], r1[(i + 1) % segs], r1[i]))
        for n, l in enumerate(f.loops):
            l[uvl].uv = ruv(region, (i + (n in (1, 2))) / segs, 1.0 if n in (2, 3) else 0.0)
    for ring in (r0, r1):
        f = bm.faces.new(ring)
        for n, l in enumerate(f.loops):
            l[uvl].uv = ruv(region, 0.5 + 0.3 * math.cos(n), 0.5 + 0.3 * math.sin(n))


random.seed(31)
bm = bmesh.new()
for k in range(6):
    u = 0.09 + k * 0.164 + jit(0.02)
    p = net_point(u, 1.0)
    tang = (net_point(min(u + 0.02, 1), 1.0) - net_point(max(u - 0.02, 0), 1.0)).normalized()
    tang = (tang + Vector((0, 0, jit(0.25)))).normalized()
    c = p + Vector((0, -0.012, 0))
    cylinder(bm, c - tang * 0.05, c + tang * 0.05, 0.022, 6, "cork")

bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
to_object("Floats", bm, smooth=False)

# ---------------------------------------------------------------- stone weights
def stone(bm, center, size, seed):
    random.seed(seed)
    uvl = bm.loops.layers.uv.verify()
    res = bmesh.ops.create_icosphere(bm, subdivisions=0, radius=size)
    vs = res["verts"]
    sx, sy, sz = random.uniform(1.0, 1.35), random.uniform(0.85, 1.1), random.uniform(0.6, 0.8)
    rot = Matrix.Rotation(random.uniform(0, 6.28), 3, "Z")
    for v in vs:
        co = Vector((v.co.x * sx, v.co.y * sy, v.co.z * sz))
        co += Vector((jit(size * 0.18), jit(size * 0.18), jit(size * 0.12)))
        v.co = center + rot @ co
    faces = {f for v in vs for f in v.link_faces}
    ox, oz = random.random(), random.random()
    for f in faces:
        for l in f.loops:
            d = l.vert.co - center
            l[uvl].uv = ruv("stone", (0.5 + d.x * 4 + ox) % 1, (0.5 + d.z * 4 + d.y * 2 + oz) % 1)


bm = bmesh.new()
seed = 40
for k in range(5):
    u = 0.12 + k * 0.19 + jit(0.03)
    p = net_point(u, 0.0)
    s = random.uniform(0.05, 0.062)
    stone(bm, Vector((p.x, p.y - 0.01, s * 0.55)), s, seed); seed += 1
# cluster at the bottom of the bunched net
for off in (Vector((0.04, -0.10, 0)), Vector((-0.04, -0.12, 0))):
    base = left.at(0.05)
    s = random.uniform(0.05, 0.064)
    stone(bm, Vector((base.x, base.y, 0)) + off + Vector((0, 0, s * 0.55)), s, seed); seed += 1
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
to_object("Weights", bm, smooth=False)

# ---------------------------------------------------------------- fish (shown as the trap fills)
def fish(name, center, yaw, roll, length, seed):
    random.seed(seed)
    bm = bmesh.new()
    L = length / 2
    res = bmesh.ops.create_icosphere(bm, subdivisions=1, radius=1.0)
    for v in res["verts"]:
        x, y, z = v.co
        taper = 1.0 - 0.35 * max(0.0, -x)  # thinner toward the tail
        v.co = Vector((x * L * 0.78, y * L * 0.22 * taper, z * L * 0.36 * taper))
    tx = -L * 0.70
    A = bm.verts.new((tx, L * 0.04, 0))
    B = bm.verts.new((tx, -L * 0.04, 0))
    C = bm.verts.new((tx - L * 0.55, 0, L * 0.42))
    D = bm.verts.new((tx - L * 0.55, 0, -L * 0.42))
    for tri in ((A, C, D), (B, D, C), (A, B, C), (A, D, B)):
        bm.faces.new(tri)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    zs = [v.co.z for v in bm.verts]
    zmin, zmax = min(zs), max(zs)
    set_uvs(bm, lambda f, l: ruv("fish", (l.vert.co.x / length + 0.5), (l.vert.co.z - zmin) / (zmax - zmin)))
    M = Matrix.Translation(center) @ Matrix.Rotation(yaw, 4, "Z") @ Matrix.Rotation(roll, 4, "X") @ Matrix.Rotation(jit(0.3), 4, "Y")
    bmesh.ops.transform(bm, matrix=M, verts=bm.verts)
    return to_object(name, bm, smooth=False)


for i, (u, v, yaw, ln) in enumerate([(0.32, 0.22, 0.25, 0.30), (0.58, 0.14, -0.35, 0.34),
                                      (0.72, 0.38, 0.15, 0.27), (0.45, 0.48, -0.2, 0.29)]):
    p = net_point(u, v) + Vector((0, -0.045, 0))
    fish(f"FishVisual{i + 1}", p, yaw, random.uniform(-0.3, 0.3), ln, 60 + i)

# ---------------------------------------------------------------- collider
bm = bmesh.new()
bmesh.ops.create_cube(bm, size=1.0)
for v in bm.verts:
    v.co = Vector((v.co.x * (TRAP_W + 0.1), 0.06 + v.co.y * 0.42, 0.75 + v.co.z * 1.5))
collider = to_object("Collider", bm)

# ---------------------------------------------------------------- stats
tris = 0
for ob in EXPORT:
    if ob.name == "Collider":
        continue
    tris += sum(len(p.vertices) - 2 for p in ob.data.polygons)
print(f"[trap] objects={len(EXPORT)} triangles={tris}")
xs = [ (ob.matrix_world @ v.co) for ob in EXPORT if ob.name != "Collider" for v in ob.data.vertices]
print("[trap] bounds x %.2f..%.2f  y %.2f..%.2f  z %.2f..%.2f" % (
    min(p.x for p in xs), max(p.x for p in xs), min(p.y for p in xs), max(p.y for p in xs),
    min(p.z for p in xs), max(p.z for p in xs)))

# ---------------------------------------------------------------- export OBJ
for ob in bpy.data.objects:
    ob.select_set(ob in EXPORT)
bpy.ops.wm.obj_export(filepath=os.path.join(OUT, "SimpleFishingTrap.obj"), export_selected_objects=True,
                      forward_axis="NEGATIVE_Z", up_axis="Y", export_materials=False, export_uv=True,
                      export_normals=True, export_triangulated_mesh=True, apply_modifiers=True)

# ---------------------------------------------------------------- preview material + scene
mat = bpy.data.materials.new("Atlas")
mat.use_nodes = True
nt = mat.node_tree
bsdf = nt.nodes["Principled BSDF"]
bsdf.inputs["Roughness"].default_value = 0.85
tex = nt.nodes.new("ShaderNodeTexImage")
tex.image = bpy.data.images.load(ATLAS)
nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
for ob in EXPORT:
    ob.data.materials.append(mat)
collider.hide_render = True

# chest-size reference box (preview only)
bpy.ops.mesh.primitive_cube_add(size=1, location=(TRAP_W / 2 + 0.9, 0.3, CHEST_H / 2))
ref = bpy.context.active_object
ref.name = "ChestRef"
ref.scale = (CHEST_W, 0.65, CHEST_H)
rmat = bpy.data.materials.new("Ref")
rmat.use_nodes = True
rmat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.35, 0.22, 0.12, 1)
ref.data.materials.append(rmat)

# ground + water strip
bpy.ops.mesh.primitive_plane_add(size=12, location=(0, 0, 0))
ground = bpy.context.active_object
gmat = bpy.data.materials.new("Ground")
gmat.use_nodes = True
gmat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.42, 0.40, 0.33, 1)
ground.data.materials.append(gmat)

world = bpy.data.worlds.new("W")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.78, 0.80, 0.84, 1)
world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.9
scene.world = world

sun_data = bpy.data.lights.new("Sun", "SUN")
sun_data.energy = 3.2
sun_data.angle = math.radians(8)
sun = bpy.data.objects.new("Sun", sun_data)
sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(-35))
scene.collection.objects.link(sun)

scene.render.engine = "CYCLES"
scene.cycles.device = "CPU"
scene.cycles.samples = 48
try:
    scene.cycles.use_denoising = True
except Exception:
    pass
scene.view_settings.view_transform = "Standard"

cam_data = bpy.data.cameras.new("Cam")
cam = bpy.data.objects.new("Cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam


def look_at(ob, target):
    d = Vector(target) - ob.location
    ob.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()


def render(path, loc, target, lens, w, h, show_ref=False, transparent=False, show_ground=True):
    cam.location = Vector(loc)
    look_at(cam, target)
    cam_data.lens = lens
    scene.render.resolution_x, scene.render.resolution_y = w, h
    scene.render.film_transparent = transparent
    ref.hide_render = not show_ref
    ground.hide_render = not show_ground
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


mid = (0, 0.05, 0.7)
render(os.path.join(OUT, "preview_front.png"), (0, -5.2, 0.85), mid, 50, 1000, 760)
render(os.path.join(OUT, "preview_side.png"), (-4.4, -2.2, 1.1), (-0.2, 0.1, 0.7), 50, 760, 900)
render(os.path.join(OUT, "preview_threequarter.png"), (3.6, -4.4, 2.0), (0.4, 0.1, 0.6), 42, 1100, 760, show_ref=True)

# icon: 3/4 view, transparent, no ground
scene.cycles.samples = 64
render(os.path.join(OUT, "icon_256.png"), (2.6, -3.6, 1.7), (0, 0.1, 0.72), 50, 256, 256, transparent=True, show_ground=False)
print("[trap] done")
