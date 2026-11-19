"""Write .meta files for Generated/WreckLod/*.oymmesh and the Resources/OYM_WreckLods table (no Unity needed).

  python make_table.py <unity_project_root>

GUIDs are derived from file names (md5), so re-running after a re-bake keeps every reference stable.
The table references each LOD mesh with the fileID OYMMeshImporter gives its "mesh" sub-asset.
"""
import hashlib, os, sys

root = sys.argv[1]
lod_dir = os.path.join(root, 'Assets', 'OYM', 'Generated', 'WreckLod')
res_dir = os.path.join(root, 'Assets', 'OYM', 'Resources')
IMPORTER_GUID = 'd11838abdfca4394c95f62ff0fa549c5'  # OYMMeshImporter.cs
MESH_FILE_ID = -8670151273213183110                    # AddObjectToAsset("mesh", ...) in OYMMeshImporter
TABLE_SCRIPT = os.path.join(root, 'Assets', 'OYM', 'Scripts', 'Runtime', 'Perf', 'WreckLodTable.cs')


def guid(key):
    return hashlib.md5(('oym-perf:' + key).encode()).hexdigest()


def write(path, text):
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        f.write(text)


def mono_meta(path, g):
    write(path + '.meta', f"""fileFormatVersion: 2
guid: {g}
MonoImporter:
  externalObjects: {{}}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {{instanceID: 0}}
  userData:
  assetBundleName:
  assetBundleVariant:
""")


# folder + per-mesh metas
write(lod_dir + '.meta', f"""fileFormatVersion: 2
guid: {guid('folder:WreckLod')}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
""")
files = sorted(f for f in os.listdir(lod_dir) if f.endswith('.oymmesh'))
for f in files:
    write(os.path.join(lod_dir, f + '.meta'), f"""fileFormatVersion: 2
guid: {guid('mesh:' + f)}
ScriptedImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 2
  userData:
  assetBundleName:
  assetBundleVariant:
  script: {{fileID: 11500000, guid: {IMPORTER_GUID}, type: 3}}
""")

# script metas for the runtime types (stable GUIDs too)
table_script_guid = guid('script:WreckLodTable.cs')
mono_meta(TABLE_SCRIPT, table_script_guid)
mono_meta(os.path.join(os.path.dirname(TABLE_SCRIPT), 'WreckLods.cs'), guid('script:WreckLods.cs'))

# table asset
bases = sorted({f.rsplit('_lod', 1)[0] for f in files})
lines = []
for b in bases:
    l1, l2 = b + '_lod1.oymmesh', b + '_lod2.oymmesh'
    if l1 not in files or l2 not in files:
        continue
    lines.append(f"  - name: {b}\n"
                 f"    lod1: {{fileID: {MESH_FILE_ID}, guid: {guid('mesh:' + l1)}, type: 3}}\n"
                 f"    lod2: {{fileID: {MESH_FILE_ID}, guid: {guid('mesh:' + l2)}, type: 3}}\n")
write(os.path.join(res_dir, 'OYM_WreckLods.asset'), f"""%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 0}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {table_script_guid}, type: 3}}
  m_Name: OYM_WreckLods
  m_EditorClassIdentifier: Assembly-CSharp::OYM.WreckLodTable
  entries:
""" + ''.join(lines))
write(os.path.join(res_dir, 'OYM_WreckLods.asset.meta'), f"""fileFormatVersion: 2
guid: {guid('asset:OYM_WreckLods')}
NativeFormatImporter:
  externalObjects: {{}}
  mainObjectFileID: 11400000
  userData:
  assetBundleName:
  assetBundleVariant:
""")
print(f'{len(files)} LOD meshes, {len(lines)} table entries')
