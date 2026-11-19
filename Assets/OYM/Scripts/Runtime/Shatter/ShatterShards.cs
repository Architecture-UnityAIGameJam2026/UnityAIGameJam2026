using System;
using System.Collections.Generic;
using UnityEngine;

namespace OYM
{
    /// Pre-fractured pieces of one model, baked in the editor by OYM ▸ FX ▸ Shatter (no Read/Write mesh needed at
    /// runtime, no fracture cost on the kill frame). Every piece mesh is centred on its own pivot (`center`, in the
    /// source mesh's space). Submeshes of a piece:
    ///   0 .. materialCount-1 : the source renderer's own surface (drawn with the enemy's own materials)
    ///   materialCount        : the inside of the shell, reversed (charred metal that glows while hot)
    ///   materialCount + 1    : break-edge heat overlay (exterior triangles near the fracture; vertex alpha = edge heat)
    public class ShatterShards : ScriptableObject
    {
        [Serializable]
        public class Source
        {
            [Tooltip("Mesh this source was baked from; matched against the enemy's MeshFilters at runtime")]
            public Mesh mesh;
            public int materialCount = 1;
            [Tooltip("Model centre / radius in this mesh's space")] public Vector3 center;
            public float radius = 1f;
        }

        [Serializable]
        public class Piece
        {
            public Mesh mesh;
            public int source;
            [Tooltip("Pivot in the source mesh's space")] public Vector3 center;
            public float radius;
            [Tooltip("Share of the source's surface area (0..1): big pieces fly slower, get trails and pops")]
            public float share;
        }

        public Source[] sources = Array.Empty<Source>();
        public Piece[] pieces = Array.Empty<Piece>();
    }
}
