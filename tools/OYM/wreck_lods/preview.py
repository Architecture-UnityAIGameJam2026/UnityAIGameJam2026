"""Workbench render of LOD0 / LOD1 / LOD2 side by side: blender -b -P preview.py -- <png> <lod0> <lod1> <lod2> [...]"""
import bpy, math, struct, sys
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
png, files = argv[0], argv[1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene


def read(path):
    b = open(path, 'rb').read()
    nv, ns = struct.unpack_from('<ii', b, 8)
    counts = struct.unpack_from('<' + 'i' * ns, b, 16)
    o = 16 + 4 * ns
    pos = np.frombuffer(b, np.float32, nv * 3, o).reshape(nv, 3); o += nv * 24
    tris = []
    for c in counts:
        tris.append(np.frombuffer(b, np.int32, c, o).reshape(-1, 3)); o += c * 4
    return pos, np.concatenate(tris)


objs = []
for f in files:
    pos, tri = read(f)
    p = pos.copy(); p[:, 2] = pos[:, 1]; p[:, 1] = pos[:, 2]  # Unity Y-up -> Blender Z-up for viewing only
    tri = tri[:, ::-1]
    me = bpy.data.meshes.new('m'); me.from_pydata(p.tolist(), [], tri.tolist()); me.update()
    for poly in me.polygons: poly.use_smooth = True
    ob = bpy.data.objects.new(f.split('/')[-1].split('\\')[-1], me); sc.collection.objects.link(ob); objs.append((ob, len(tri)))

size = max(max(o.dimensions) for o, _ in objs)
for i, (o, n) in enumerate(objs):
    c = sum((o.matrix_world @ Vector(v) for v in o.bound_box), Vector()) / 8
    o.location = Vector((i * size * 1.15, 0, 0)) - c + o.location
cam_d = bpy.data.cameras.new('c'); cam_d.type = 'ORTHO'; cam_d.ortho_scale = size * 1.15 * len(objs)
cam = bpy.data.objects.new('c', cam_d); sc.collection.objects.link(cam); sc.camera = cam
cam.location = ((len(objs) - 1) * size * 0.575, -size * 3, size * 0.6); cam.rotation_euler = (math.radians(80), 0, 0)
sc.render.engine = 'BLENDER_WORKBENCH'; sc.display.shading.light = 'STUDIO'; sc.display.shading.show_cavity = True
sc.render.resolution_x = 600 * len(objs); sc.render.resolution_y = 600
sc.render.filepath = png
bpy.ops.render.render(write_still=True)
print('[preview] tris', [n for _, n in objs])
