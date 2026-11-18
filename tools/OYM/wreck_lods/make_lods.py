"""Bake LOD1/LOD2 .oymmesh files for the wreck fragments (Blender, headless).

  blender -b --factory-startup -P make_lods.py -- <wreck_dir> <out_dir> [min_tris] [ratio1] [ratio2]

Reads frag_NNNN.oymmesh (not *_col), collapse-decimates to ratio1 / ratio2 keeping the 3 submeshes
(outer hull / inside / hot cut) as material slots, writes frag_NNNN_lod1.oymmesh / _lod2.oymmesh in the same
binary format (positions and triangle winding untouched: no axis conversion, the data never leaves Blender's
memory as a scene). Normals are written as zero so OYMMeshImporter recomputes smooth normals.

OYMMESH1 layout: "OYMMESH1", int32 nv, int32 nsub, int32 count[nsub], float32 pos[nv*3], float32 nrm[nv*3],
int32 idx[sum(count)].
"""
import bpy, bmesh, os, struct, sys, time
import numpy as np

argv = sys.argv[sys.argv.index('--') + 1:]
src, out = argv[0], argv[1]
min_tris = int(argv[2]) if len(argv) > 2 else 1500
ratios = [float(argv[3]) if len(argv) > 3 else 0.25, float(argv[4]) if len(argv) > 4 else 0.06]
os.makedirs(out, exist_ok=True)


def read(path):
    b = open(path, 'rb').read()
    assert b[:8] == b'OYMMESH1', path
    nv, ns = struct.unpack_from('<ii', b, 8)
    counts = struct.unpack_from('<' + 'i' * ns, b, 16)
    o = 16 + 4 * ns
    pos = np.frombuffer(b, np.float32, nv * 3, o).reshape(nv, 3); o += nv * 12
    o += nv * 12  # normals
    subs = []
    for c in counts:
        subs.append(np.frombuffer(b, np.int32, c, o).reshape(-1, 3)); o += c * 4
    return pos, subs


def write(path, pos, subs):
    nv = len(pos)
    with open(path, 'wb') as f:
        f.write(b'OYMMESH1')
        f.write(struct.pack('<ii', nv, len(subs)))
        f.write(struct.pack('<' + 'i' * len(subs), *[s.size for s in subs]))
        f.write(np.ascontiguousarray(pos, np.float32).tobytes())
        f.write(np.zeros((nv, 3), np.float32).tobytes())
        for s in subs:
            f.write(np.ascontiguousarray(s, np.int32).tobytes())


def build_object(pos, subs):
    faces = np.concatenate([s for s in subs if len(s)]) if any(len(s) for s in subs) else np.zeros((0, 3), np.int32)
    mats = np.concatenate([np.full(len(s), i, np.int32) for i, s in enumerate(subs)])
    me = bpy.data.meshes.new('m')
    me.vertices.add(len(pos)); me.vertices.foreach_set('co', pos.ravel())
    me.loops.add(faces.size); me.loops.foreach_set('vertex_index', faces.ravel())
    me.polygons.add(len(faces))
    me.polygons.foreach_set('loop_start', np.arange(0, faces.size, 3, dtype=np.int32))
    me.polygons.foreach_set('loop_total', np.full(len(faces), 3, np.int32))
    me.polygons.foreach_set('material_index', mats)
    for i in range(len(subs)):
        me.materials.append(bpy.data.materials.get(f'slot{i}') or bpy.data.materials.new(f'slot{i}'))
    me.update(calc_edges=True)
    me.validate(clean_customdata=False)
    ob = bpy.data.objects.new('o', me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def decimated(ob, ratio, nsub):
    mod = ob.modifiers.new('dec', 'DECIMATE')
    mod.decimate_type = 'COLLAPSE'
    mod.ratio = ratio
    mod.use_collapse_triangulate = True
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    me = ev.to_mesh()
    me.calc_loop_triangles()
    nv = len(me.vertices)
    pos = np.empty(nv * 3, np.float32); me.vertices.foreach_get('co', pos)
    nt = len(me.loop_triangles)
    tri = np.empty(nt * 3, np.int32); me.loop_triangles.foreach_get('vertices', tri)
    mat = np.empty(nt, np.int32); me.loop_triangles.foreach_get('material_index', mat)
    ev.to_mesh_clear()
    ob.modifiers.remove(mod)
    tri = tri.reshape(-1, 3)
    # drop vertices no triangle uses (keeps UInt16 index format for most fragments)
    used = np.unique(tri)
    remap = np.full(nv, -1, np.int32); remap[used] = np.arange(len(used), dtype=np.int32)
    pos = pos.reshape(-1, 3)[used]
    tri = remap[tri]
    subs = [tri[mat == i] for i in range(nsub)]
    return pos, subs


bpy.ops.wm.read_factory_settings(use_empty=True)
files = sorted(f for f in os.listdir(src) if f.startswith('frag_') and f.endswith('.oymmesh') and '_col' not in f and '_lod' not in f)
t0 = time.time(); done = skipped = 0; tri_in = tri_out1 = tri_out2 = 0
for n, f in enumerate(files):
    pos, subs = read(os.path.join(src, f))
    ntri = sum(len(s) for s in subs)
    if ntri < min_tris:
        skipped += 1
        continue
    ob = build_object(pos, subs)
    base = f[:-len('.oymmesh')]
    for li, r in enumerate(ratios, 1):
        p2, s2 = decimated(ob, r, len(subs))
        write(os.path.join(out, f'{base}_lod{li}.oymmesh'), p2, s2)
        if li == 1: tri_out1 += sum(len(s) for s in s2)
        else: tri_out2 += sum(len(s) for s in s2)
    tri_in += ntri
    me = ob.data
    bpy.data.objects.remove(ob); bpy.data.meshes.remove(me)
    done += 1
    if done % 50 == 0:
        print(f'[lods] {done} done ({n + 1}/{len(files)}) {time.time() - t0:.0f}s', flush=True)
print(f'[lods] finished: {done} fragments with LODs, {skipped} below {min_tris} tris; '
      f'tris {tri_in} -> lod1 {tri_out1} / lod2 {tri_out2}; {time.time() - t0:.0f}s', flush=True)
