using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace OnYourMark.UI
{
    public sealed class OnYourMarkHud : MonoBehaviour
    {
        public TextAsset layoutAsset;
        public Font displayFont, monoFont;
        public Material hologramMaterial, accentMaterial;
        [Tooltip("Demo telemetry and keyboard shortcuts; disable when binding the game.")]
        public bool demoInput=true;
        [Range(0,1)] public float partnerHealth=.78f;
        public int partnerAmmo=6;
        [Range(0,1)] public float localHealth=.92f;
        public int localAmmo=8;
        public bool reducedMotion;
        public enum PreviewState { Field, Request, Critical, Awakened }
        public enum Panel { Closed, Channels, Support }
        public PreviewState state;
        public Panel openPanel;
        public bool[] returned=new bool[3];
        public int pendingChannel=-1;
        public bool sectorComplete;
        public System.Action<int> ExchangeRequested;
        public System.Action<int> SupportRequested;
        public System.Action<int> HudReturned;
        /// <summary>채널이 주인을 바꾸기 직전(화면에는 아직 이전 데이터). (channel, 이제 내 정보를 보는지). HudSwapGlitch가 구독한다.</summary>
        public System.Action<int,bool> ChannelSwapping;
        public bool PartnerNeedsSupport=>partnerHealth<=.15f||partnerAmmo<=1;
        readonly Dictionary<string,Text> texts=new Dictionary<string,Text>();
        readonly Dictionary<string,CanvasGroup> groups=new Dictionary<string,CanvasGroup>();
        readonly Dictionary<string,string> visibility=new Dictionary<string,string>();
        readonly Dictionary<string,HudIconDefinition> icons=new Dictionary<string,HudIconDefinition>();
        readonly Dictionary<string,HudGeometry> iconGraphics=new Dictionary<string,HudGeometry>();
        readonly Graphic[] ammoCells=new Graphic[8];
        Graphic healthBar;
        float healthBarWidth;
        float started,requestAt,qHeld,toastUntil,noticeUntil;
        float shownHealth=.78f;
        Color primaryColor,dimColor,accentColor,dangerColor;
        bool supportSelectionMade;

        public void Build()
        {
            var layout=JsonUtility.FromJson<HudLayout>(layoutAsset.text);
            LoadIcons(layout);
            foreach(var g in layout.groups)
            {
                var root=Rect(g.name,transform,g.x,g.y,g.w,g.h);
                root.gameObject.AddComponent<CanvasGroup>();
                foreach(var n in g.nodes)BuildNode(root,n,g);
                if(!string.IsNullOrEmpty(g.action))
                {
                    var hit=root.gameObject.AddComponent<Image>();hit.color=Color.clear;hit.raycastTarget=true;hit.material=hologramMaterial;
                    var button=root.gameObject.AddComponent<HudActionButton>();button.hud=this;button.action=g.action;button.targetGraphic=hit;
                    ColorUtility.TryParseHtmlString(layout.colors.accent,out button.hoverColor);
                    button.glow=root.Find("Glow")?.GetComponent<Graphic>();
                    button.navigation=new Navigation{mode=Navigation.Mode.None};
                }
            }
            Bind();SetPreviewState(PreviewState.Field);
        }
        public static RectTransform Rect(string name,Transform parent,float x,float y,float w,float h)
        {
            var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);
            var rt=(RectTransform)go.transform;rt.anchorMin=rt.anchorMax=new Vector2(0,1);rt.pivot=new Vector2(0,1);
            rt.anchoredPosition=new Vector2(x,-y);rt.sizeDelta=new Vector2(w,h);return rt;
        }
        void BuildNode(Transform parent,HudNode n,HudGroup g)
        {
            bool path=n.type=="line"||n.type=="polygon";
            var rt=Rect(n.name,parent,path?0:n.x,path?0:n.y,path?g.w:n.w,path?g.h:n.h);
            ColorUtility.TryParseHtmlString(n.color,out Color color);color.a=n.opacity;
            Graphic graphic;
            if(n.type=="text")
            {
                var t=rt.gameObject.AddComponent<Text>();t.font=n.font=="display"?displayFont:monoFont;
                t.text=n.text;t.fontSize=Mathf.RoundToInt(n.size);t.supportRichText=false;t.alignment=TextAnchor.UpperLeft;
                t.horizontalOverflow=HorizontalWrapMode.Overflow;t.verticalOverflow=VerticalWrapMode.Overflow;graphic=t;
            }
            else if(n.type=="rect")graphic=rt.gameObject.AddComponent<Image>();
            else
            {
                var geo=rt.gameObject.AddComponent<HudGeometry>();geo.shape=n.type;geo.points=n.points;
                geo.startAngle=n.start;geo.endAngle=n.end;geo.thickness=n.stroke;graphic=geo;
                if(n.type=="icon"&&icons.TryGetValue(n.icon,out var definition)){geo.iconName=n.icon;geo.strokes=definition.strokes;geo.iconWidth=definition.width;geo.iconHeight=definition.height;}
            }
            graphic.color=color;graphic.raycastTarget=false;graphic.material=n.glow&&accentMaterial?accentMaterial:hologramMaterial;
        }
        void Awake()
        {
            var canvas=GetComponent<Canvas>();if(canvas&&canvas.renderMode==RenderMode.ScreenSpaceCamera&&!canvas.worldCamera)canvas.worldCamera=Camera.main;
            Bind();started=Time.unscaledTime;
        }
        void Bind()
        {
            texts.Clear();groups.Clear();visibility.Clear();
            iconGraphics.Clear();
            var layout=JsonUtility.FromJson<HudLayout>(layoutAsset.text);LoadIcons(layout);
            ColorUtility.TryParseHtmlString(layout.colors.primary,out primaryColor);
            ColorUtility.TryParseHtmlString(layout.colors.dim,out dimColor);
            ColorUtility.TryParseHtmlString(layout.colors.accent,out accentColor);
            ColorUtility.TryParseHtmlString(layout.colors.danger,out dangerColor);
            foreach(var icon in GetComponentsInChildren<HudGeometry>(true))if(icon.shape=="icon")iconGraphics[icon.transform.parent.name+"/"+icon.name]=icon;
            foreach(var t in GetComponentsInChildren<Text>(true))texts[t.transform.parent.name+"/"+t.name]=t;
            foreach(var cg in GetComponentsInChildren<CanvasGroup>(true))groups[cg.name]=cg;
            foreach(var g in layout.groups)visibility[g.name]=g.state;
            healthBar=transform.Find("Vitals/Health bar")?.GetComponent<Graphic>();
            var healthTrack=transform.Find("Vitals/Health track") as RectTransform;
            healthBarWidth=healthTrack?healthTrack.sizeDelta.x:0;
            for(int i=0;i<8;i++)ammoCells[i]=transform.Find("Reserves/Cell "+i)?.GetComponent<Graphic>();
        }
        void SetText(string key,string value){if(texts.TryGetValue(key,out var t)&&t.text!=value)t.text=value;}
        void LoadIcons(HudLayout layout){icons.Clear();if(layout.icons!=null)foreach(var icon in layout.icons)icons[icon.name]=icon;}
        void SetIcon(string key,string kind,bool local=false)
        {
            if(!iconGraphics.TryGetValue(key,out var graphic)||!icons.TryGetValue(kind,out var definition))return;
            if(graphic.iconName!=kind){graphic.iconName=kind;graphic.strokes=definition.strokes;graphic.iconWidth=definition.width;graphic.iconHeight=definition.height;graphic.Refresh();}
            if(key.EndsWith("Ownership icon"))graphic.color=local?accentColor:primaryColor;
        }
        void Toast(){toastUntil=Time.unscaledTime+2.5f;}
        public void ActivateAction(string action)
        {
            if(action=="channels")openPanel=openPanel==Panel.Channels?Panel.Closed:Panel.Channels;
            else if(action=="support")openPanel=openPanel==Panel.Support?Panel.Closed:Panel.Support;
            else if(action=="close")openPanel=Panel.Closed;
            else if(action=="cancel")CancelExchange();
            else if(action.StartsWith("exchange")&&int.TryParse(action.Substring(8),out int channel))RequestExchange(channel);
            else if(action.StartsWith("send")&&int.TryParse(action.Substring(4),out int support))SendSupport(support);
        }
        public void SetPartnerTelemetry(float health01,int ammo)
        {
            partnerHealth=Mathf.Clamp01(health01);partnerAmmo=Mathf.Clamp(ammo,0,8);
            if(state==PreviewState.Field||state==PreviewState.Critical)state=PartnerNeedsSupport?PreviewState.Critical:PreviewState.Field;
        }
        public void RequestExchange(int channel)
        {
            if(channel<0||channel>2)return;
            openPanel=Panel.Closed;pendingChannel=channel;requestAt=Time.unscaledTime;state=PreviewState.Request;
            SetIcon("Exchange/Channel icon",new[]{"heart","map","ammo"}[channel]);
            ExchangeRequested?.Invoke(channel);
        }
        // Invoke only after authoritative same-channel consent from both players.
        public void ConfirmExchange(int channel)
        {
            if(channel!=pendingChannel||channel<0||channel>2)return;
            if(Time.unscaledTime-requestAt>10){CancelExchange();return;}
            ChannelSwapping?.Invoke(channel,!returned[channel]);
            returned[channel]=!returned[channel];pendingChannel=-1;state=PartnerNeedsSupport?PreviewState.Critical:PreviewState.Field;
            RefreshOwnership();Toast();
        }
        public void CancelExchange(){pendingChannel=-1;state=PartnerNeedsSupport?PreviewState.Critical:PreviewState.Field;}
        public void SendSupport(int channel)
        {
            if(channel<0||channel>2)return;
            SupportRequested?.Invoke(channel);openPanel=Panel.Closed;supportSelectionMade=true;
            Toast();
        }
        public bool ReturnHud(int channel)
        {
            if(!sectorComplete||channel<0||channel>2)return false;
            if(!returned[channel])ChannelSwapping?.Invoke(channel,true);
            returned[channel]=true;pendingChannel=-1;openPanel=Panel.Closed;state=PreviewState.Awakened;noticeUntil=Time.unscaledTime+4;
            SetIcon("Awakened/Restored channel",new[]{"heart","map","ammo"}[channel]);
            RefreshOwnership();HudReturned?.Invoke(channel);return true;
        }
        public void SetPreviewState(PreviewState value)
        {
            state=value;pendingChannel=-1;sectorComplete=false;returned=new bool[3];partnerHealth=.78f;partnerAmmo=6;
            openPanel=Panel.Closed;qHeld=0;supportSelectionMade=false;toastUntil=0;
            if(value==PreviewState.Request)RequestExchange(0);
            if(value==PreviewState.Critical)partnerHealth=.12f;
            if(value==PreviewState.Awakened){sectorComplete=true;ReturnHud(0);}
            RefreshOwnership();ApplyVisuals();
        }
        void RefreshOwnership()
        {
            string[] modules={"Vitals","Spatial","Reserves"};
            for(int i=0;i<3;i++)SetIcon(modules[i]+"/Ownership icon",returned[i]?"self":"partner",returned[i]);
        }
        void Update()
        {
            if(demoInput)ReadInput();
            if(pendingChannel>=0&&Time.unscaledTime-requestAt>10)CancelExchange();
            ApplyVisuals();
        }
        void ReadInput()
        {
            var k=Keyboard.current;if(k==null)return;
            if(k.f1Key.wasPressedThisFrame)SetPreviewState(PreviewState.Field);
            if(k.f2Key.wasPressedThisFrame)SetPreviewState(PreviewState.Request);
            if(k.f3Key.wasPressedThisFrame)SetPreviewState(PreviewState.Critical);
            if(k.f4Key.wasPressedThisFrame)SetPreviewState(PreviewState.Awakened);
            if(k.tabKey.wasPressedThisFrame)ActivateAction("channels");
            if(k.escapeKey.wasPressedThisFrame){CancelExchange();openPanel=Panel.Closed;}
            if(k.enterKey.wasPressedThisFrame&&pendingChannel>=0)ConfirmExchange(pendingChannel);
            if(k.qKey.wasPressedThisFrame)supportSelectionMade=false;
            if(k.qKey.isPressed){qHeld+=Time.unscaledDeltaTime;if(qHeld>.25f&&!supportSelectionMade)openPanel=Panel.Support;}
            if(k.qKey.wasReleasedThisFrame){if(qHeld<=.25f)SendSupport(partnerHealth<=.15f?0:2);qHeld=0;openPanel=Panel.Closed;}
            var keys=new[]{k.digit1Key,k.digit2Key,k.digit3Key};
            for(int i=0;i<3;i++)if(keys[i].wasPressedThisFrame){if(openPanel==Panel.Support)SendSupport(i);else RequestExchange(i);}
            if(k.eKey.wasPressedThisFrame)ReturnHud(0);
            if(k.rKey.wasPressedThisFrame)reducedMotion=!reducedMotion;
            if(state==PreviewState.Critical&&Mouse.current!=null&&Mouse.current.rightButton.wasPressedThisFrame){SendSupport(partnerHealth<=.15f?0:2);SetPartnerTelemetry(.78f,6);Toast();}
        }
        bool Show(string mode)
        {
            switch(mode)
            {
                case "channels":return openPanel==Panel.Channels;
                case "support":return openPanel==Panel.Support;
                case "tray":return openPanel!=Panel.Closed;
                case "request":return state==PreviewState.Request;
                case "critical":return state==PreviewState.Critical;
                case "awakened":return state==PreviewState.Awakened&&Time.unscaledTime<noticeUntil;
                case "toast":return Time.unscaledTime<toastUntil&&openPanel==Panel.Closed;
                default:return true;
            }
        }
        void ApplyVisuals()
        {
            float intro=reducedMotion?1:Mathf.SmoothStep(0,1,(Time.unscaledTime-started)/.55f);
            foreach(var pair in groups)
            {
                string mode=visibility.TryGetValue(pair.Key,out var m)?m:"all";
                bool show=Show(mode);var cg=pair.Value;
                float goal=show?intro:0;
                cg.alpha=Application.isPlaying&&!reducedMotion&&mode!="all"?Mathf.MoveTowards(cg.alpha,goal,Time.unscaledDeltaTime*9):goal;
                cg.interactable=show&&cg.alpha>.95f;cg.blocksRaycasts=cg.interactable;
                if(mode!="all")cg.transform.localScale=Vector3.one*(reducedMotion?1:Mathf.Lerp(.97f,1,cg.alpha));
            }
            float target=returned[0]?localHealth:partnerHealth;
            shownHealth=reducedMotion?target:Mathf.MoveTowards(shownHealth,target,Time.unscaledDeltaTime*1.4f);
            SetText("Vitals/Health number",Mathf.RoundToInt(shownHealth*100).ToString("00"));
            if(healthBar)
            {
                var size=healthBar.rectTransform.sizeDelta;size.x=healthBarWidth*shownHealth;
                healthBar.rectTransform.sizeDelta=size;
                healthBar.color=returned[0]?accentColor:target<=.15f?dangerColor:primaryColor;
            }
            int ammo=returned[2]?localAmmo:partnerAmmo;
            SetText("Reserves/Ammo number",ammo.ToString("00"));
            for(int i=0;i<8;i++)if(ammoCells[i]){var color=i<ammo?primaryColor:dimColor;color.a=i<ammo?.85f:.25f;ammoCells[i].color=color;}
            SetIcon("Critical/Support icon",partnerHealth<=.15f?"shield":"supply");
        }
    }
}
