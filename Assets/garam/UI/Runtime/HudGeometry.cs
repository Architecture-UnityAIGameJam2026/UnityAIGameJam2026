using UnityEngine;
using UnityEngine.UI;

namespace OnYourMark.UI
{
    [System.Serializable] public class HudLayout { public int width, height; public HudPalette colors; public HudGroup[] groups; public HudIconDefinition[] icons; }
    [System.Serializable] public class HudPalette { public string primary,dim,white,accent,danger,ink,panel,muted; }
    [System.Serializable] public class HudStroke { public float[] points; public bool fill; }
    [System.Serializable] public class HudIconDefinition { public string name; public float width=24,height=24; public HudStroke[] strokes; }
    [System.Serializable] public class HudGroup { public string name, state, action; public float x,y,w,h; public HudNode[] nodes; }
    [System.Serializable] public class HudNode
    {
        public string name, type, text, color, font, icon;
        public bool glow;
        public float x,y,w,h,size,start,end,stroke=1,opacity=1;
        public float[] points;
    }

    // Native UGUI geometry: crisp at any Canvas scale, no baked HUD texture.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class HudGeometry : MaskableGraphic
    {
        public string shape;
        public string iconName;
        public HudStroke[] strokes;
        public float iconWidth=24,iconHeight=24;
        public float[] points;
        public float startAngle, endAngle=360, thickness=1;
        public void Refresh() => SetVerticesDirty();
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (shape == "icon" && strokes!=null)
            {
                float sx=rectTransform.rect.width/iconWidth,sy=rectTransform.rect.height/iconHeight;
                foreach(var path in strokes)
                {
                    if(path.fill)
                    {
                        int first=vh.currentVertCount;
                        for(int i=0;i<path.points.Length;i+=2)vh.AddVert(new Vector3(path.points[i]*sx,-path.points[i+1]*sy),color,new Vector2(path.points[i]/iconWidth,1-path.points[i+1]/iconHeight));
                        for(int i=1;i<path.points.Length/2-1;i++)vh.AddTriangle(first,first+i,first+i+1);
                        continue;
                    }
                    for(int i=0;i+3<path.points.Length;i+=2)
                        Segment(vh,new Vector2(path.points[i]*sx,-path.points[i+1]*sy),new Vector2(path.points[i+2]*sx,-path.points[i+3]*sy));
                }
            }
            else if (shape == "arc")
            {
                float radius = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height)*.5f;
                Vector2 center = new Vector2(radius,-radius);
                int segments=Mathf.Max(2,Mathf.CeilToInt(Mathf.Abs(endAngle-startAngle)/3));
                for(int i=0;i<segments;i++)
                {
                    float a=Mathf.Lerp(startAngle,endAngle,(float)i/segments)*Mathf.Deg2Rad;
                    float b=Mathf.Lerp(startAngle,endAngle,(float)(i+1)/segments)*Mathf.Deg2Rad;
                    Segment(vh,center+new Vector2(Mathf.Cos(a),-Mathf.Sin(a))*radius,center+new Vector2(Mathf.Cos(b),-Mathf.Sin(b))*radius);
                }
            }
            else if(shape=="line" && points!=null)
            {
                for(int i=0;i+3<points.Length;i+=2) Segment(vh,new Vector2(points[i],-points[i+1]),new Vector2(points[i+2],-points[i+3]));
            }
            else if(shape=="polygon" && points!=null)
            {
                for(int i=0;i<points.Length;i+=2) vh.AddVert(new Vector3(points[i],-points[i+1]),color,new Vector2(points[i]/rectTransform.rect.width,1-points[i+1]/rectTransform.rect.height));
                for(int i=1;i<points.Length/2-1;i++) vh.AddTriangle(0,i,i+1);
            }
        }
        void Segment(VertexHelper vh,Vector2 a,Vector2 b)
        {
            if((b-a).sqrMagnitude<.00001f)return;
            Vector2 normal=new Vector2(-(b-a).y,(b-a).x).normalized*thickness*.5f;
            int i=vh.currentVertCount;
            vh.AddVert(a-normal,color,new Vector2(0,0)); vh.AddVert(a+normal,color,new Vector2(0,1));
            vh.AddVert(b+normal,color,new Vector2(1,1)); vh.AddVert(b-normal,color,new Vector2(1,0));
            vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);
        }
    }
}
