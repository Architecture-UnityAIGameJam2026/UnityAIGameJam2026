using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace OnYourMark.UI.Editor
{
    public static class HudShowcaseBuilder
    {
        const string Root="Assets/garam/UI";
        static Material metal,dark,cyan,amber,floor;
        static GameObject Box(string name,Vector3 pos,Vector3 size,Material mat,Transform parent=null)
        {
            GameObject go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=pos;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=mat;return go;
        }
        static Material Material(string name,Color color,float metallic=0,float smooth=.4f,Color? emission=null)
        {
            var m=new Material(Shader.Find("Universal Render Pipeline/Lit"));m.name=name;m.SetColor("_BaseColor",color);m.SetFloat("_Metallic",metallic);m.SetFloat("_Smoothness",smooth);
            if(emission.HasValue){m.shader=Shader.Find("OnYourMark/IonSurface");m.SetColor("_EmissionColor",emission.Value);}
            AssetDatabase.CreateAsset(m,Root+"/Materials/"+name+".mat");return m;
        }
        static void Point(string name,Vector3 pos,Color color,float intensity,float range)
        {
            var go=new GameObject(name);go.transform.position=pos;var l=go.AddComponent<Light>();l.type=LightType.Point;l.color=color;l.intensity=intensity;l.range=range;
        }
        [MenuItem("On Your Mark/Create HUD Showcase")]
        public static string Create()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Leave Play mode before creating assets.");
            if(SceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Save your current scene before generating the showcase.");
            if(AssetDatabase.LoadAssetAtPath<SceneAsset>(Root+"/Scenes/HudShowcase.unity"))throw new InvalidOperationException("Showcase already exists. Open the saved scene instead of overwriting it.");
            foreach(string d in new[]{"Materials","Prefabs","Scenes"})Directory.CreateDirectory(Root+"/"+d);
            AssetDatabase.Refresh();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            metal=Material("Graphite alloy",new Color(.085f,.13f,.14f),.7f,.55f);
            dark=Material("Dark titanium",new Color(.025f,.042f,.046f),.6f,.52f);
            floor=Material("Obsidian floor",new Color(.045f,.065f,.069f),.68f,.68f);
            cyan=Material("Ion emission",new Color(.13f,.57f,.51f),.25f,.35f,new Color(.08f,1.2f,.9f)*2.3f);
            amber=Material("Core emission",new Color(.8f,.46f,.2f),.3f,.4f,new Color(1.8f,.72f,.2f));
            var cameraGo=new GameObject("Showcase Camera");cameraGo.tag="MainCamera";
            Camera camera=cameraGo.AddComponent<Camera>();camera.transform.position=new Vector3(0,2,-14);camera.transform.rotation=Quaternion.Euler(2,0,0);
            camera.fieldOfView=62;camera.nearClipPlane=.05f;camera.farClipPlane=180;camera.backgroundColor=new Color(.013f,.033f,.037f);camera.clearFlags=CameraClearFlags.SolidColor;camera.allowHDR=true;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
            camera.GetUniversalAdditionalCameraData().antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.08f,.14f,.15f);RenderSettings.reflectionIntensity=.15f;RenderSettings.fog=true;RenderSettings.fogColor=new Color(.018f,.057f,.063f);RenderSettings.fogMode=FogMode.ExponentialSquared;RenderSettings.fogDensity=.022f;
            var sun=new GameObject("Soft ceiling key").AddComponent<Light>();sun.type=LightType.Directional;sun.color=new Color(.49f,.72f,.74f);sun.intensity=.75f;sun.transform.rotation=Quaternion.Euler(48,-25,0);sun.shadows=LightShadows.Soft;
            BuildChamber();
            var profile=ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom=profile.Add<Bloom>(true);bloom.intensity.Override(.65f);bloom.threshold.Override(.9f);bloom.scatter.Override(.58f);
            var vignette=profile.Add<Vignette>(true);vignette.intensity.Override(.28f);vignette.smoothness.Override(.5f);
            var color=profile.Add<ColorAdjustments>(true);color.postExposure.Override(.15f);color.contrast.Override(15);color.saturation.Override(-12);
            var tone=profile.Add<Tonemapping>(true);tone.mode.Override(TonemappingMode.ACES);
            var grain=profile.Add<FilmGrain>(true);grain.intensity.Override(.08f);grain.response.Override(.7f);
            var aberration=profile.Add<ChromaticAberration>(true);aberration.intensity.Override(.025f);
            AssetDatabase.CreateAsset(profile,Root+"/Materials/Showcase Post FX.asset");
            foreach(var component in profile.components)AssetDatabase.AddObjectToAsset(component,profile);
            var volume=new GameObject("Atmosphere and HUD post processing").AddComponent<Volume>();volume.isGlobal=true;volume.sharedProfile=profile;
            var mat=new Material(Shader.Find("OnYourMark/HologramUI"));mat.name="SenseLink Hologram";mat.SetFloat("_Emission",1.2f);mat.SetFloat("_ScanStrength",.035f);AssetDatabase.CreateAsset(mat,Root+"/Materials/SenseLink Hologram.mat");
            var canvasGo=new GameObject("SenseLink HUD",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            var canvas=canvasGo.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=.5f;canvas.sortingOrder=20;
            var scaler=canvasGo.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;scaler.matchWidthOrHeight=.5f;
            var hud=canvasGo.AddComponent<OnYourMarkHud>();hud.layoutAsset=AssetDatabase.LoadAssetAtPath<TextAsset>(Root+"/Resources/HudLayout.json");hud.displayFont=AssetDatabase.LoadAssetAtPath<Font>(Root+"/Fonts/Rajdhani-SemiBold.ttf");hud.monoFont=AssetDatabase.LoadAssetAtPath<Font>(Root+"/Fonts/IBMPlexMono-Regular.ttf");hud.hologramMaterial=mat;hud.Build();
            if(!hud.displayFont||!hud.monoFont)throw new InvalidOperationException("HUD font import failed.");
            var events=new GameObject("Event System",typeof(EventSystem),typeof(InputSystemUIInputModule));
            var motion=cameraGo.AddComponent<HudSceneMotion>();motion.hud=hud;motion.volume=volume;motion.reactor=GameObject.Find("Relay ring").transform;
            // Prefab has no scene camera reference: consumers assign their own camera.
            canvas.worldCamera=null;PrefabUtility.SaveAsPrefabAsset(canvasGo,Root+"/Prefabs/SenseLinkHUD.prefab");canvas.worldCamera=camera;
            EditorSceneManager.SaveScene(scene,Root+"/Scenes/HudShowcase.unity");AssetDatabase.SaveAssets();
            Selection.activeGameObject=canvasGo;
            return "Created 1920x1080 UGUI prefab and URP showcase scene.";
        }
        static void BuildChamber()
        {
            var env=new GameObject("Relay chamber").transform;
            Box("Floor",new Vector3(0,-.3f,30),new Vector3(25,.6f,100),floor,env);
            Box("Ceiling",new Vector3(0,11,32),new Vector3(25,1,100),dark,env);
            for(int i=0;i<10;i++)
            {
                float z=-8+i*9;
                for(int side=-1;side<=1;side+=2)
                {
                    Box("Rib "+side+" "+i,new Vector3(side*11,4,z),new Vector3(1.1f,9,1.5f),metal,env);
                    Box("Wall bay",new Vector3(side*12,4,z+3),new Vector3(1,8,7.2f),dark,env);
                    Box("Ion strip",new Vector3(side*10.39f,4,z-.4f),new Vector3(.06f,7,.09f),cyan,env);
                    Box("Floor guide",new Vector3(side*5.6f,.02f,z+2),new Vector3(.035f,.03f,6.4f),cyan,env);
                    Box("Floor panel",new Vector3(side*8,-.01f,z+3),new Vector3(4,.07f,7.4f),metal,env);
                    if(i%2==0)Point("Bay light",new Vector3(side*9,5,z),new Color(.2f,.8f,.75f),5,17);
                }
                Box("Ceiling beam",new Vector3(0,9.8f,z),new Vector3(23,.8f,1.2f),metal,env);
                if(i>1)Box("Overhead strip",new Vector3(0,9.3f,z),new Vector3(8,.045f,.14f),cyan,env);
                Box("Floor seam",new Vector3(0,.01f,z),new Vector3(11,.015f,.025f),dark,env);
            }
            for(int side=-1;side<=1;side+=2)
            {
                Box("Forward bulkhead",new Vector3(side*7.1f,3,19),new Vector3(3.8f,6,6),metal,env);
                Box("Bulkhead cap",new Vector3(side*7.1f,6.15f,19),new Vector3(4.1f,.3f,6.3f),dark,env);
                for(int i=0;i<5;i++)Box("Service vent",new Vector3(side*7.1f,1.7f+i*.47f,15.95f),new Vector3(2.8f,.14f,.08f),dark,env);
                Box("Terminal screen",new Vector3(side*6.3f,3.1f,15.86f),new Vector3(.64f,.9f,.04f),cyan,env);
            }
            Box("End wall",new Vector3(0,5,67),new Vector3(25,12,1),dark,env);
            Box("Exit portal",new Vector3(0,3,66.4f),new Vector3(5,6,.1f),amber,env);
            Box("Exit inset",new Vector3(0,2.8f,66.3f),new Vector3(4.65f,5.6f,.1f),dark,env);
            Point("Warm exit",new Vector3(0,3,59),new Color(1,.48f,.15f),8,24);
            var ring=new GameObject("Relay ring").transform;ring.position=new Vector3(0,4.6f,33);
            for(int i=0;i<48;i++)
            {
                float angle=i*Mathf.PI*2/48;
                var seg=Box("Relay segment",new Vector3(Mathf.Cos(angle)*4.1f,Mathf.Sin(angle)*4.1f,0),new Vector3(.22f,.52f,.4f),i%4==0?amber:metal,ring);
                seg.transform.localRotation=Quaternion.Euler(0,0,angle*Mathf.Rad2Deg);
                var led=Box("Relay rim light",new Vector3(Mathf.Cos(angle)*3.9f,Mathf.Sin(angle)*3.9f,-.24f),new Vector3(.055f,.42f,.055f),cyan,ring);led.transform.localRotation=seg.transform.localRotation;
            }
            Point("Relay glow",new Vector3(0,4,29),new Color(.2f,.8f,.7f),6,20);
            // Abstract mannequin is a native, editable stand-in for the partner.
            var ally=new GameObject("NOVA / partner silhouette").transform;ally.position=new Vector3(3.6f,0,9);
            Box("Torso",new Vector3(0,1.35f,0),new Vector3(.65f,.95f,.36f),metal,ally);
            Box("Pack",new Vector3(0,1.4f,.23f),new Vector3(.45f,.6f,.24f),dark,ally);
            var head=GameObject.CreatePrimitive(PrimitiveType.Sphere);head.name="Helmet";head.transform.SetParent(ally,false);head.transform.localPosition=new Vector3(0,2,0);head.transform.localScale=new Vector3(.43f,.48f,.42f);head.GetComponent<Renderer>().sharedMaterial=metal;
            Box("Visor",new Vector3(0,2,-.2f),new Vector3(.35f,.1f,.05f),cyan,ally);
            for(int s=-1;s<=1;s+=2){Box("Leg",new Vector3(s*.21f,.5f,0),new Vector3(.25f,1,.28f),dark,ally);Box("Arm",new Vector3(s*.47f,1.3f,-.08f),new Vector3(.22f,.82f,.23f),metal,ally);Box("Shoulder light",new Vector3(s*.46f,1.7f,-.18f),new Vector3(.18f,.05f,.05f),cyan,ally);}
            Point("Ally rim",new Vector3(3.6f,2,7),new Color(.3f,1,.8f),2.5f,5);
        }
        public static string Validate()
        {
            var hud=UnityEngine.Object.FindFirstObjectByType<OnYourMarkHud>();if(!hud)throw new Exception("HUD not found.");
            var canvas=hud.GetComponent<Canvas>();var scaler=hud.GetComponent<CanvasScaler>();
            if(scaler.referenceResolution!=new Vector2(1920,1080)||canvas.renderMode!=RenderMode.ScreenSpaceCamera)throw new Exception("Canvas contract mismatch.");
            if(!hud.displayFont||!hud.monoFont)throw new Exception("Missing fonts.");
            hud.SetPreviewState(OnYourMarkHud.PreviewState.Field);
            if(hud.returned.Any(v=>v))throw new Exception("Initial ownership must be crossed.");
            hud.RequestExchange(1);hud.ConfirmExchange(0);if(hud.returned[1])throw new Exception("Mismatched consent accepted.");
            hud.ConfirmExchange(1);if(!hud.returned[1])throw new Exception("Matching consent rejected.");
            hud.SetPreviewState(OnYourMarkHud.PreviewState.Field);if(hud.ReturnHud(0))throw new Exception("Return accepted before objective.");
            hud.SetPartnerTelemetry(.12f,0);if(hud.state!=OnYourMarkHud.PreviewState.Critical)throw new Exception("Critical alert missing.");
            hud.SetPartnerTelemetry(.78f,1);if(hud.state!=OnYourMarkHud.PreviewState.Critical)throw new Exception("Low reserves alert missing.");
            hud.SetPartnerTelemetry(.78f,2);if(hud.state!=OnYourMarkHud.PreviewState.Field)throw new Exception("Recovered reserves did not clear alert.");
            hud.SetPreviewState(OnYourMarkHud.PreviewState.Awakened);if(!hud.returned[0])throw new Exception("Awakening missing.");
            hud.SetPreviewState(OnYourMarkHud.PreviewState.Field);
            return "PASS: 1920x1080 camera Canvas, font references, cross ownership, matching consent, objective gate, critical threshold, awakening. Native graphics: "+hud.GetComponentsInChildren<Graphic>(true).Length;
        }
        public static string RepairRenderAssets()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Repair in Edit mode.");
            var hud=UnityEngine.Object.FindFirstObjectByType<OnYourMarkHud>();
            foreach(var g in hud.GetComponentsInChildren<HudGeometry>(true))if(!g.GetComponent<CanvasRenderer>())g.gameObject.AddComponent<CanvasRenderer>();
            var volume=UnityEngine.Object.FindFirstObjectByType<Volume>();var p=volume.sharedProfile;
            p.components.RemoveAll(c=>!c);
            if(p.components.Count==0)
            {
                var bloom=p.Add<Bloom>(true);bloom.intensity.Override(.7f);bloom.threshold.Override(.75f);bloom.scatter.Override(.55f);
                var vignette=p.Add<Vignette>(true);vignette.intensity.Override(.28f);vignette.smoothness.Override(.5f);
                var color=p.Add<ColorAdjustments>(true);color.postExposure.Override(-.25f);color.contrast.Override(20);color.saturation.Override(-8);
                p.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
                var grain=p.Add<FilmGrain>(true);grain.intensity.Override(.07f);grain.response.Override(.7f);
                p.Add<ChromaticAberration>(true).intensity.Override(.025f);
            }
            foreach(var c in p.components)if(!AssetDatabase.Contains(c))AssetDatabase.AddObjectToAsset(c,p);
            EditorUtility.SetDirty(p);AssetDatabase.SaveAssets();
            var canvas=hud.GetComponent<Canvas>();var cam=canvas.worldCamera;canvas.worldCamera=null;PrefabUtility.SaveAsPrefabAsset(hud.gameObject,Root+"/Prefabs/SenseLinkHUD.prefab");canvas.worldCamera=cam;
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            return "Repaired native CanvasRenderers and persisted "+p.components.Count+" Volume overrides.";
        }
        public static string Polish()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Polish in Edit mode.");
            foreach(string name in new[]{"Ion emission","Core emission"})
            {
                var m=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/"+name+".mat");
                var emission=m.GetColor("_EmissionColor");m.shader=Shader.Find("OnYourMark/IonSurface");m.SetColor("_EmissionColor",emission);EditorUtility.SetDirty(m);
            }
            RenderSettings.reflectionIntensity=.15f;
            var profile=UnityEngine.Object.FindFirstObjectByType<Volume>().sharedProfile;
            if(profile.TryGet<ChromaticAberration>(out var aberration))aberration.intensity.value=0;
            EditorUtility.SetDirty(profile);AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            var flags=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.FlattenHierarchy;
            var assembly=typeof(UnityEditor.Editor).Assembly;
            var sizesType=assembly.GetType("UnityEditor.GameViewSizes");
            var sizes=sizesType.GetProperty("instance",flags).GetValue(null);
            var group=sizesType.GetMethod("GetGroup",flags).Invoke(sizes,new object[]{Enum.Parse(assembly.GetType("UnityEditor.GameViewSizeGroupType"),"Standalone")});
            var texts=(string[])group.GetType().GetMethod("GetDisplayTexts",flags).Invoke(group,null);
            int index=Array.FindIndex(texts,t=>t.Contains("SenseLink 1920"));
            if(index<0)
            {
                var size=Activator.CreateInstance(assembly.GetType("UnityEditor.GameViewSize"),flags,null,new object[]{Enum.Parse(assembly.GetType("UnityEditor.GameViewSizeType"),"FixedResolution"),1920,1080,"SenseLink 1920 x 1080"},null);
                group.GetType().GetMethod("AddCustomSize",flags).Invoke(group,new[]{size});
                index=(int)group.GetType().GetMethod("GetTotalCount",flags).Invoke(group,null)-1;
            }
            var view=EditorWindow.GetWindow(assembly.GetType("UnityEditor.GameView"));view.GetType().GetProperty("selectedSizeIndex",flags).SetValue(view,index);view.Repaint();
            return "HDR emissive surfaces, restrained reflections, and native 1920x1080 Game view configured.";
        }
        public static string LightChamber()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Light chamber in Edit mode.");
            string[] names={"Graphite alloy","Dark titanium","Obsidian floor"};
            Color[] colors={new Color(.24f,.32f,.34f),new Color(.1f,.15f,.17f),new Color(.15f,.21f,.23f)};
            for(int i=0;i<names.Length;i++)
            {
                var m=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/"+names[i]+".mat");
                m.SetColor("_BaseColor",colors[i]);m.SetFloat("_Metallic",.25f);m.SetFloat("_Smoothness",.4f);EditorUtility.SetDirty(m);
            }
            RenderSettings.ambientLight=new Color(.24f,.34f,.36f);
            foreach(var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if(r.name=="Overhead strip"&&r.transform.position.z<10)r.enabled=false;
            foreach(var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if(l.type==LightType.Directional){l.shadows=LightShadows.None;l.intensity=.9f;}
                else if(l.name=="Bay light")l.intensity=12;
                else if(l.name=="Ally rim")l.intensity=10;
                EditorUtility.SetDirty(l);
            }
            var profile=UnityEngine.Object.FindFirstObjectByType<Volume>().sharedProfile;
            if(profile.TryGet<ColorAdjustments>(out var color)){color.postExposure.value=.2f;color.contrast.value=8;color.saturation.value=-8;}
            var uiMat=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/SenseLink Hologram.mat");
            uiMat.SetFloat("_Emission",1.6f);EditorUtility.SetDirty(uiMat);EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            return "Balanced diffuse surfaces, ambient fill, HDR UI, and scene key light.";
        }
        public static string RebuildInteractiveHud()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Rebuild in Edit mode.");
            var scene=SceneManager.GetActiveScene();
            if(scene.path!=Root+"/Scenes/HudShowcase.unity")throw new InvalidOperationException("Open HudShowcase.");
            var hud=UnityEngine.Object.FindFirstObjectByType<OnYourMarkHud>();
            Undo.RegisterFullObjectHierarchyUndo(hud.gameObject,"Refine SenseLink HUD");
            var accent=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/SenseLink Accent.mat");
            if(!accent){accent=new Material(hud.hologramMaterial);AssetDatabase.CreateAsset(accent,Root+"/Materials/SenseLink Accent.mat");}
            accent.SetFloat("_Emission",3.2f);accent.SetFloat("_ScanStrength",.025f);hud.accentMaterial=accent;
            hud.hologramMaterial.SetFloat("_Emission",1.1f);hud.hologramMaterial.SetFloat("_ScanStrength",.012f);
            for(int i=hud.transform.childCount-1;i>=0;i--)Undo.DestroyObjectImmediate(hud.transform.GetChild(i).gameObject);
            hud.Build();
            var profile=UnityEngine.Object.FindFirstObjectByType<Volume>().sharedProfile;
            if(profile.TryGet<Bloom>(out var bloom)){bloom.intensity.value=.85f;bloom.threshold.value=1.1f;bloom.scatter.value=.65f;}
            if(profile.TryGet<Vignette>(out var vignette))vignette.intensity.value=.22f;
            EditorUtility.SetDirty(accent);EditorUtility.SetDirty(hud.hologramMaterial);EditorUtility.SetDirty(profile);
            var canvas=hud.GetComponent<Canvas>();var cam=canvas.worldCamera;canvas.worldCamera=null;
            PrefabUtility.SaveAsPrefabAsset(hud.gameObject,Root+"/Prefabs/SenseLinkHUD.prefab");canvas.worldCamera=cam;
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            return "Rebuilt four-corner HUD: top-left health bar, top-right map and objective, bottom-left quick slots, bottom-right floating weapons.";
        }
    }
}
