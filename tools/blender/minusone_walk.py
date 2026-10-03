# Blender: blender -b -P walk.py -- <minusone_rigged.glb> <out_dir>
# Gives the rigged Minus One model a looping in-place walk on its own skeleton (and a roar for
# the neck, head and jaw only, played on top of the walk by the mod) and exports
# <out_dir>/godzilla.glb for the Unity bundle build, plus preview renders (walk_sheet.png,
# walk.mp4). Legs use IK targets (planted feet, no sliding when the game's stride matches
# STRIDE), hips bob and sway, spine counter-rotates, arms swing, tail waves; then everything
# is baked to plain keyframes. Plate materials are renamed to contain "Scales" so the mod
# can light them for the atomic breath.
import bpy, sys, math, os
import numpy as np
from mathutils import Vector, Matrix, Quaternion

src, outdir = sys.argv[sys.argv.index("--") + 1:][:2]
STRIDE = 0.40   # ground covered per loop, in body heights (KaijuVisual.StrideHeights)
DUTY = 0.62     # fraction of the loop each foot is planted
N = 48          # frames per loop at 24 fps

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
for o in list(bpy.data.objects):
    if o.type == 'MESH' and o.name.startswith('Icosphere'):
        bpy.data.objects.remove(o, do_unlink=True)
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
arm.animation_data_clear()
P = arm.pose.bones
# The model ships posed (roaring). Start from the rest pose, but the eye bones need their offsets.
KEEP_LOC = {'Bone.026_8', 'Bone.027_9'}
for pb in P:
    pb.rotation_mode = 'QUATERNION'
    if pb.name not in KEEP_LOC:
        pb.location = (0, 0, 0)
    pb.rotation_quaternion = (1, 0, 0, 0)
    pb.scale = (1, 1, 1)
bpy.context.view_layer.update()
base = {pb.name: (pb.location.copy(), pb.rotation_quaternion.copy()) for pb in P}
W = arm.matrix_world.copy()
Wi = W.inverted()
meshes = [o for o in bpy.data.objects if o.type == 'MESH']


def wmat(name):
    return W @ P[name].matrix


def whead(name):
    return wmat(name).translation.copy()


def wtail(name):
    pb = P[name]
    return W @ pb.matrix @ Vector((0, pb.bone.length, 0))


def bounds():
    dg = bpy.context.evaluated_depsgraph_get()
    lo, hi = np.full(3, 1e9), np.full(3, -1e9)
    for o in meshes:
        e = o.evaluated_get(dg)
        me = e.to_mesh()
        co = np.empty(len(me.vertices) * 3)
        me.vertices.foreach_get('co', co)
        m = np.array(e.matrix_world)
        w = co.reshape(-1, 3) @ m[:3, :3].T + m[:3, 3]
        lo, hi = np.minimum(lo, w.min(0)), np.maximum(hi, w.max(0))
        e.to_mesh_clear()
    return lo, hi


lo, hi = bounds()
GROUND, H = lo[2], hi[2] - lo[2]
L = STRIDE * H
print("bounds", lo.round(3), hi.round(3), "H", round(H, 3), "loop distance", round(L, 3))

HIPS = 'Bone_32'
SPINE = ['Bone.002_14', 'Bone.011_13', 'Bone.012_12']
NECK, HEAD = 'Bone.013_11', 'Bone.014_10'
ARMS = {'L': ('Bone.015_2', 'Bone.017_1'), 'R': ('Bone.016_5', 'Bone.018_4')}
LEGS = {'L': ('Bone.005_17', 'Bone.007_16', 'Bone.009_15', 0.0),
        'R': ('Bone.006_21', 'Bone.008_20', 'Bone.010_19', 0.5)}
TAIL = ['Bone.021_31', 'Bone.023_30', 'Bone.025_29', 'Bone.028_28', 'Bone.029_27',
        'Bone.030_26', 'Bone.031_25', 'Bone.032_24', 'Bone.033_23']
HIP_PIVOT = wtail(HIPS)  # pelvis turns about the hip joints, not the bone head on the ground

legs0 = {}
for side, (thigh, shin, foot, phase) in LEGS.items():
    hip, ankle = whead(thigh), whead(foot)
    reach = P[thigh].bone.length + P[shin].bone.length
    reach *= W.to_scale()[0]
    q = wmat(foot).to_3x3().normalized().to_quaternion()
    legs0[side] = (hip, ankle, reach, q)
    print("leg", side, "hip", tuple(hip.round(3) if hasattr(hip, 'round') else hip), "ankle", tuple(ankle), "reach", round(reach, 3))


def rot(axis, deg):
    return Quaternion(Vector(axis), math.radians(deg))


def turn(name, q, pivot=None, offset=Vector()):
    """Rotates a bone (and its children) by world rotation q about pivot (default: its head), then shifts it."""
    cur = wmat(name)
    piv = cur.translation if pivot is None else pivot
    new = Matrix.Translation(piv + offset) @ q.to_matrix().to_4x4() @ Matrix.Translation(-piv) @ cur
    P[name].matrix = Wi @ new
    bpy.context.view_layer.update()


def smooth(s):
    return s * s * (3 - 2 * s)


def foot_at(side, t):
    hip, ankle, reach, q0 = legs0[side]
    u = (t + LEGS[side][3]) % 1.0
    half = L * DUTY / 2
    y0 = (hip.y + ankle.y) / 2   # track centred a little behind the hip
    # Forward is -y. Planted: the foot slides back under the moving body at body speed.
    if u < DUTY:
        s = u / DUTY
        return Vector((ankle.x, y0 - half + 2 * half * s, ankle.z)), q0, 0.0
    s = (u - DUTY) / (1 - DUTY)
    y = y0 + half - 2 * half * smooth(s)
    lift = 0.07 * H * math.sin(math.pi * s)
    pitch = 14 * math.sin(math.pi * s)
    return Vector((ankle.x, y, ankle.z + lift)), rot((1, 0, 0), pitch) @ q0, lift


# IK targets: ankle position empties and foot rotation empties.
targets = {}
for side, (thigh, shin, foot, phase) in LEGS.items():
    ik = bpy.data.objects.new("ik_" + side, None)
    fr = bpy.data.objects.new("rot_" + side, None)
    fr.rotation_mode = 'QUATERNION'
    for o in (ik, fr):
        bpy.context.scene.collection.objects.link(o)
    targets[side] = (ik, fr)

scene = bpy.context.scene
scene.render.fps = 24
for f in range(N + 1):
    t = (f % N) / N
    scene.frame_set(f)
    for pb in P:
        pb.location, pb.rotation_quaternion = base[pb.name][0].copy(), base[pb.name][1].copy()
    bpy.context.view_layer.update()
    c1, s1 = math.cos(2 * math.pi * t), math.sin(2 * math.pi * t)
    c2 = math.cos(4 * math.pi * t)
    side_c = math.cos(2 * math.pi * (t - DUTY / 2))     # +1 mid left stance, -1 mid right stance
    # Pelvis: lowest at each heel strike, over the planted foot, left hip forward at left strike.
    yaw = -4.0 * c1
    roll = -2.5 * side_c
    turn(HIPS, rot((0, 0, 1), yaw) @ rot((0, 1, 0), roll), HIP_PIVOT,
         Vector((0.02 * H * side_c, 0, -0.018 * H * c2)))
    for b in SPINE:
        turn(b, rot((0, 0, 1), -yaw * 0.3) @ rot((0, 1, 0), -roll * 0.3) @ rot((1, 0, 0), 0.8 * c2))
    turn(NECK, rot((1, 0, 0), -1.2 * c2) @ rot((0, 0, 1), 1.0 * s1))
    turn(HEAD, rot((1, 0, 0), -1.5 * math.cos(4 * math.pi * t - 0.5)))
    for side, sign in (('L', 1), ('R', -1)):
        sh, el = ARMS[side]
        turn(sh, rot((1, 0, 0), sign * 6 * c1))
        turn(el, rot((1, 0, 0), sign * 5 * math.cos(2 * math.pi * t - 0.6)))
    for i, b in enumerate(TAIL):
        sway = (2.2 + 0.5 * i) * math.sin(2 * math.pi * t - 0.55 * i - 0.8)
        lift = (2.0 if i == 0 else 0.0) + 0.8 * math.sin(4 * math.pi * t - 0.6 * i)
        turn(b, rot((0, 0, 1), sway) @ rot((1, 0, 0), lift))
    for pb in P:
        pb.keyframe_insert("location", frame=f)
        pb.keyframe_insert("rotation_quaternion", frame=f)
    for side in LEGS:
        pos, q, _ = foot_at(side, t)
        ik, fr = targets[side]
        ik.location = pos
        fr.rotation_quaternion = q
        ik.keyframe_insert("location", frame=f)
        fr.keyframe_insert("rotation_quaternion", frame=f)
    if f % 12 == 0:
        hipL = whead(LEGS['L'][0])
        print("frame", f, "L ankle target dist", round((foot_at('L', t)[0] - hipL).length, 3), "reach", round(legs0['L'][2], 3))

for side, (thigh, shin, foot, phase) in LEGS.items():
    ik, fr = targets[side]
    c = P[shin].constraints.new('IK')
    c.target = ik
    c.chain_count = 2
    c = P[foot].constraints.new('COPY_ROTATION')
    c.target = fr
    c.target_space = 'WORLD'
    c.owner_space = 'WORLD'

scene.frame_start, scene.frame_end = 0, N
bpy.context.view_layer.objects.active = arm
arm.select_set(True)
bpy.ops.object.mode_set(mode='POSE')
bpy.ops.pose.select_all(action='SELECT')
bpy.ops.nla.bake(frame_start=0, frame_end=N, only_selected=True, visual_keying=True,
                 clear_constraints=True, use_current_action=True, bake_types={'POSE'})
bpy.ops.object.mode_set(mode='OBJECT')
arm.animation_data.action.name = "Walk"
for side in LEGS:
    for o in targets[side]:
        bpy.data.objects.remove(o, do_unlink=True)

# Check the planted feet: toe height and drift per frame.
for f in (0, 6, 12, 18, 24, 30, 36, 42):
    scene.frame_set(f)
    print("check", f, "L toe", tuple(round(c, 3) for c in wtail(LEGS['L'][2])), "R toe", tuple(round(c, 3) for c in wtail(LEGS['R'][2])))

# Plates glow: their materials share the Material_029 textures.
plate_objs = {'Object_15', 'Object_17', 'Object_19', 'Object_21', 'Object_23', 'Object_25'}
for o in meshes:
    for s in o.material_slots:
        if s.material and o.name in plate_objs and "Scales" not in s.material.name:
            s.material.name = "Scales_NoSplit_" + s.material.name
        print("material", o.name, s.material.name if s.material else None)

# ---- Roar: neck and head tip back, jaw opens wide, trembles, closes. Only these bones are
# keyed, so in the game it layers over the walk without touching the legs, arms or tail.
from mathutils import Euler

walk_action = arm.animation_data.action
ROAR_N = 96  # 4 s at 24 fps
ROAR_POSE = {'Bone.013_11': -12.0,  # neck: pitch (degrees, bone X), from the model's own roar pose
             'Bone.014_10': -30.0,  # head tips back
             'Bone.022_7': 36.0,    # jaw opens
             'Bone.024_6': -6.0}    # lower jaw tip
roar = bpy.data.actions.new("Roar")
arm.animation_data.action = roar
OPEN = [(0, 0.0), (6, 0.12), (16, 1.0), (78, 1.0), (96, 0.0)]


def openness(f):
    for (f0, k0), (f1, k1) in zip(OPEN, OPEN[1:]):
        if f0 <= f <= f1:
            u = (f - f0) / (f1 - f0)
            return k0 + (k1 - k0) * (u * u * (3 - 2 * u))
    return 0.0


for f in range(0, ROAR_N + 1, 2):
    k = openness(f)
    shake = 0.06 * math.sin(f * 1.9) if 16 < f < 78 else 0.0  # roaring tremble
    for name, deg in ROAR_POSE.items():
        amount = k + (shake if name in ('Bone.022_7', 'Bone.014_10') else 0.0)
        P[name].rotation_quaternion = Euler((math.radians(deg * amount), 0, 0)).to_quaternion()
        P[name].keyframe_insert("rotation_quaternion", frame=f)

# Both actions on NLA tracks so the exporter writes each as its own animation.
ad = arm.animation_data
tracks = []
for act in (walk_action, roar):
    tr = ad.nla_tracks.new()
    tr.name = act.name
    tr.strips.new(act.name, 0, act)
    tracks.append(tr)
ad.action = None
for pb in P:
    pb.location, pb.rotation_quaternion = base[pb.name][0].copy(), base[pb.name][1].copy()

os.makedirs(outdir, exist_ok=True)
scene.frame_set(0)
bpy.ops.export_scene.gltf(filepath=os.path.join(outdir, "godzilla.glb"), export_format='GLB',
                          export_animations=True, export_animation_mode='ACTIONS',
                          export_force_sampling=True)
print("exported", os.path.join(outdir, "godzilla.glb"))

# Previews play the actions directly, not the NLA tracks.
for tr in tracks:
    tr.mute = True


def head_shot(path, f):
    ad.action = roar
    scene.frame_set(f)
    head = W @ P[HEAD].head
    cam.location = head + Vector((3.5, -3.5, 0.6))
    tgt.location = head + Vector((0, -0.6, 0.1))
    scene.render.resolution_x, scene.render.resolution_y = 480, 360
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    ad.action = walk_action

# ---- Previews -------------------------------------------------------------------------
def fcurves(action):
    try:
        return list(action.fcurves)
    except AttributeError:
        out = []
        for layer in action.layers:
            for strip in layer.strips:
                for cb in strip.channelbags:
                    out += list(cb.fcurves)
        return out


ad.action = walk_action
for fc in fcurves(walk_action):
    fc.modifiers.new('CYCLES')

bpy.ops.mesh.primitive_plane_add(size=60, location=(-4.7, 2, GROUND))
plane = bpy.context.active_object
pm = bpy.data.materials.new("ground")
pm.diffuse_color = (0.35, 0.33, 0.3, 1)
plane.data.materials.append(pm)
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
scene.collection.objects.link(cam)
tgt = bpy.data.objects.new("tgt", None)
scene.collection.objects.link(tgt)
con = cam.constraints.new('TRACK_TO')
con.target = tgt
con.track_axis = 'TRACK_NEGATIVE_Z'
con.up_axis = 'UP_Y'
cam.data.lens = 35
scene.camera = cam
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN'))
sun.data.energy = 3.5
sun.rotation_euler = (0.7, 0.2, 0.9)
scene.collection.objects.link(sun)
scene.world = bpy.data.worlds.new("w")
scene.world.color = (0.55, 0.6, 0.68)


def shoot(path, f, loc, look, w=640, h=480):
    cam.location = loc
    tgt.location = look
    scene.frame_set(f)
    scene.render.resolution_x, scene.render.resolution_y = w, h
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


tmp = os.path.join(outdir, "frames")
os.makedirs(tmp, exist_ok=True)
side_cam, side_look = (-4.7 + 15, 1.0, 3.0), (-4.7, 1.0, 2.4)
front_cam, front_look = (2.5, -10.0, 4.0), (-4.7, 0.5, 2.4)
tiles = []
for f in range(0, N, 8):
    for name, cl in (("side", (side_cam, side_look)), ("front", (front_cam, front_look))):
        p = os.path.join(tmp, "%s_%02d.png" % (name, f))
        shoot(p, f, *cl, w=480, h=360)
        tiles.append((name, f, p))
# Contact sheet: side views on the top row, three-quarter views below.
cols = N // 8
sheet = np.zeros((2 * 360, cols * 480, 4), dtype=np.float32)
for name, f, p in tiles:
    img = bpy.data.images.load(p)
    px = np.array(img.pixels[:], dtype=np.float32).reshape(360, 480, 4)
    r = 1 if name == "side" else 0   # image rows run bottom-up
    c = f // 8
    sheet[r * 360:(r + 1) * 360, c * 480:(c + 1) * 480] = px
    bpy.data.images.remove(img)
out = bpy.data.images.new("sheet", cols * 480, 2 * 360, alpha=True)
out.pixels[:] = sheet.ravel()
out.filepath_raw = os.path.join(outdir, "walk_sheet.png")
out.file_format = 'PNG'
out.save()
print("sheet", out.filepath_raw)

for f in (0, 16, 50, 96):
    head_shot(os.path.join(outdir, "frames", "roar_%02d.png" % f), f)

# Video: two loops from the three-quarter view.
try:
    scene.render.image_settings.media_type = 'VIDEO'
except Exception:
    pass
scene.render.image_settings.file_format = 'FFMPEG'
scene.render.ffmpeg.format = 'MPEG4'
scene.render.ffmpeg.codec = 'H264'
scene.render.resolution_x, scene.render.resolution_y = 960, 720
scene.frame_start, scene.frame_end = 0, 2 * N - 1
cam.location, tgt.location = (3.5, -11.0, 4.5), (-4.7, 0.8, 2.4)
scene.render.filepath = os.path.join(outdir, "walk.mp4")
bpy.ops.render.render(animation=True)
print("video", scene.render.filepath)
