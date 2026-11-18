using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
var mats = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Select(r=>r.sharedMaterial).Distinct();
var volume=Object.FindFirstObjectByType<Volume>().sharedProfile;
return new {
    materials=mats.Select(m=>new {m.name,shader=m.shader.name,m.shaderKeywords,baseColor=m.HasProperty("_BaseColor")?m.GetColor("_BaseColor").ToString():"",metallic=m.HasProperty("_Metallic")?m.GetFloat("_Metallic"):0,passes=m.passCount}).ToArray(),
    lights=Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Select(l=>new {l.name,l.enabled,l.intensity,l.range,l.shadows}).ToArray(),
    fx=volume.components.Select(c=>new {c.name,c.active}).ToArray()
};
