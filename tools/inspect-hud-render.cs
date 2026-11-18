using System.Linq;
var m=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>("Assets/garam/UI/Materials/Ion emission.mat");
var p=UnityEngine.Object.FindFirstObjectByType<UnityEngine.Rendering.Volume>().profile;
var pipe=UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
return new {emission=m.GetColor("_EmissionColor").ToString(),keywords=m.shaderKeywords,shader=m.shader.name,camHdr=UnityEngine.Camera.main.allowHDR,pipe=pipe.name,hdr=pipe.supportsHDR,overrides=p.components.Select(x=>x.name).ToArray(),ambient=UnityEngine.RenderSettings.ambientLight.ToString(),fog=UnityEngine.RenderSettings.fog,screen=new[]{UnityEngine.Screen.width,UnityEngine.Screen.height}};
