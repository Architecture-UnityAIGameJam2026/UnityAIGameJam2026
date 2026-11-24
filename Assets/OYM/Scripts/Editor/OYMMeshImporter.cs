// Imports .oymmesh files written by skybox/export_assets.py (Surface-Nets meshes from the wreck simulation).
using System;
using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

namespace OYM.EditorTools
{
    [ScriptedImporter(5, "oymmesh")]
    public class OYMMeshImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            byte[] b = File.ReadAllBytes(ctx.assetPath);
            if (b.Length < 16 || System.Text.Encoding.ASCII.GetString(b, 0, 8) != "OYMMESH1")
            {
                ctx.LogImportError("Not an OYMMESH1 file");
                return;
            }
            int o = 8;
            int nv = BitConverter.ToInt32(b, o); o += 4;
            int nsub = BitConverter.ToInt32(b, o); o += 4;
            var counts = new int[nsub];
            for (int s = 0; s < nsub; s++) { counts[s] = BitConverter.ToInt32(b, o); o += 4; }

            var f = new float[nv * 3];
            Buffer.BlockCopy(b, o, f, 0, nv * 12); o += nv * 12;
            var pos = new Vector3[nv];
            for (int i = 0; i < nv; i++) pos[i] = new Vector3(f[3 * i], f[3 * i + 1], f[3 * i + 2]);
            Buffer.BlockCopy(b, o, f, 0, nv * 12); o += nv * 12;
            var nrm = new Vector3[nv];
            bool hasNormals = false;
            for (int i = 0; i < nv; i++)
            {
                nrm[i] = new Vector3(f[3 * i], f[3 * i + 1], f[3 * i + 2]);
                if (!hasNormals && nrm[i].sqrMagnitude > 0.5f) hasNormals = true;
            }

            var mesh = new Mesh { name = Path.GetFileNameWithoutExtension(ctx.assetPath) };
            mesh.indexFormat = nv > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(pos);
            mesh.subMeshCount = nsub;
            for (int s = 0; s < nsub; s++)
            {
                var idx = new int[counts[s]];
                Buffer.BlockCopy(b, o, idx, 0, counts[s] * 4); o += counts[s] * 4;
                mesh.SetTriangles(idx, s, false);
            }
            if (hasNormals) mesh.SetNormals(nrm); else mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            // Web build: ~3,100 wreck fragments were ~310 MB of float vertices in the download. Quantize them in the
            // build data (decompressed on load, like the FBX models' Medium setting); the HDRP / Windows import stays exact.
            if (ctx.selectedBuildTarget == UnityEditor.BuildTarget.WebGL)
                UnityEditor.MeshUtility.SetMeshCompression(mesh, UnityEditor.ModelImporterMeshCompression.Medium);
            ctx.AddObjectToAsset("mesh", mesh);
            ctx.SetMainObject(mesh);
        }
    }
}
