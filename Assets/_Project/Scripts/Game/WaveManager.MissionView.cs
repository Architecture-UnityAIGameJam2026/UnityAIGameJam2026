using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace OnYourMark
{
    public partial class WaveManager
    {
        static readonly Color Signal = new(.45f, 1f, .88f);
        float normalScale, normalFixed;
        bool slowed;
        GameObject[] fragments;
        LineRenderer beam;
        Texture2D ringTexture;
        AudioClip suitLoop;
        AudioSource suitAudio;
        double viewStarted;
        GUIStyle missionText;

        void StageChanged(MissionStage from, MissionStage to)
        {
            if (!Route) return;
            viewStarted = Time.unscaledTimeAsDouble;
            if (InQte && !slowed)
            {
                normalScale = Time.timeScale; normalFixed = Time.fixedDeltaTime; slowed = true;
                Time.timeScale = normalScale * ringTimeScale;
                Time.fixedDeltaTime = normalFixed * ringTimeScale;
            }
            if (!InQte) ReleaseSlowMotion();
            if (fragments != null) { foreach (var part in fragments) if (part) Destroy(part); fragments = null; }
            if (beam) Destroy(beam.gameObject);
            if (to == MissionStage.Deflect && Route.fragmentArt)
            {
                fragments = new GameObject[2];
                for (int i = 0; i < 2; i++)
                {
                    var part = Instantiate(Route.fragmentArt);
                    foreach (var col in part.GetComponentsInChildren<Collider>()) col.enabled = false;
                    fragments[i] = part;
                }
            }
            if (to is MissionStage.Split or MissionStage.Ending)
            {
                var fx = new GameObject("Mission beam"); fx.transform.SetParent(transform);
                beam = fx.AddComponent<LineRenderer>(); beam.sharedMaterial = Route.signalMaterial;
                beam.startWidth = to == MissionStage.Split ? .34f : .11f; beam.endWidth = beam.startWidth;
                beam.positionCount = 2;
            }
            if (Route.finalPlate && to == MissionStage.Climb) Route.finalPlate.localRotation = Route.ClosedPlate;
            if (to == MissionStage.Ending)
            {
                OYM.BgmPlayer.FadeOut(.4f);
                if (radio) radio.Silence();
                if (!suitAudio) suitAudio = gameObject.AddComponent<AudioSource>();
                suitAudio.clip = MakeSuitAudio(); suitAudio.loop = true; suitAudio.volume = .32f; suitAudio.Play();
            }
        }
        void ReleaseSlowMotion()
        {
            if (!slowed) return;
            Time.timeScale = normalScale; Time.fixedDeltaTime = normalFixed; slowed = false;
        }
        AudioClip MakeSuitAudio()
        {
            if (suitLoop) return suitLoop;
            const int rate = 16000;
            var samples = new float[4 * rate];
            var random = new System.Random(53); float filtered = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float noise = (float)random.NextDouble() * 2 - 1;
                filtered = Mathf.Lerp(filtered, noise, .075f);
                float breath = Mathf.Pow(Mathf.Abs(Mathf.Sin(i * Mathf.PI / samples.Length * 2)), 2);
                samples[i] = filtered * breath * .8f + noise * .012f;
            }
            suitLoop = AudioClip.Create("Suit breathing and open radio", samples.Length, 1, rate, false);
            suitLoop.SetData(samples, 0); return suitLoop;
        }
        void ClearMissionView()
        {
            if (ringTexture) Destroy(ringTexture);
            if (suitAudio) Destroy(suitAudio);
            if (suitLoop) Destroy(suitLoop);
            if (beam) Destroy(beam.gameObject);
            if (fragments != null) foreach (var part in fragments) if (part) Destroy(part);
        }
        void LateUpdate()
        {
            if (!IsSpawned || !Route) return;
            if (fragments != null && Player.All.Count == 2)
            {
                for (int i = 0; i < fragments.Length; i++)
                {
                    var target = Pilot(i == 1); if (!target) continue;
                    Vector3 end = target.transform.position + Vector3.up * 1.2f;
                    Vector3 start = end + Vector3.forward * (i == 0 ? 12 : -12) + Vector3.up * 6;
                    float t = Mathf.Clamp01(1f - (float)(beat.Value - Clock) / ringLead);
                    fragments[i].transform.position = Vector3.Lerp(start,end,t);
                    fragments[i].transform.Rotate(Vector3.up, 90 * Time.unscaledDeltaTime);
                    fragments[i].SetActive((hits.Value & (1 << i)) == 0);
                }
            }
            if (Stage == MissionStage.Split)
                Route.MoveLanding(8f + 4f * Mathf.Sin((float)Clock * .5f));
            else if (Stage == MissionStage.Land)
                Route.MoveLanding(8f * Mathf.Clamp01((float)(RingTime(false) - Clock) / (ringLead + 1)));
            else Route.MoveLanding(0);
            if (Route.finalPlate && Stage == MissionStage.Strike)
            {
                bool opened = (hits.Value & (1 << firstRole.Value)) != 0;
                var rot = Route.ClosedPlate * Quaternion.Euler(opened && skillRole.Value == 255 ? -74 : 0, 0, 0);
                Route.finalPlate.localRotation = Quaternion.RotateTowards(Route.finalPlate.localRotation, rot, Time.unscaledDeltaTime * 170);
            }
            if (beam)
            {
                float elapsed = (float)(Time.unscaledTimeAsDouble - viewStarted);
                beam.enabled = elapsed < (Stage == MissionStage.Split ? 1.9f : .3f);
                if (!beam.enabled) return;
                if (Stage == MissionStage.Split)
                {
                    Vector3 mid = Route.deck[3].position + Vector3.up + Vector3.forward * (elapsed - .95f) * 3;
                    beam.SetPosition(0, mid + Vector3.left * 20 + Vector3.up * 4);
                    beam.SetPosition(1, mid + Vector3.right * 20 - Vector3.up * 2);
                }
                else
                {
                    var finisher = Pilot(firstRole.Value != 0);
                    if (finisher) beam.SetPosition(0, finisher.transform.position + Vector3.up);
                    beam.SetPosition(1, Route.finalPlate.position);
                }
            }
        }
        public string ObjectiveText(Player p) => Stage switch
        {
            MissionStage.FirstCrossing => Sword(p) ? "Reach the first hull fragment" : "Cover Kestrel, then jump",
            MissionStage.Deflect => Sword(p) ? "Cut the fragment threatening Orbit" : "Shoot the fragment behind Kestrel",
            MissionStage.ApproachLaser => "Clear the approach and regroup at the laser",
            MissionStage.Split => Sword(p) ? "Suit link crossed  /  Follow the upper hull" : "Suit link crossed  /  Follow the lower truss",
            MissionStage.Land => "Jump onto the passing deck",
            MissionStage.Reunion => "Clear the landing and cross the platform",
            MissionStage.Climb => "Fight through to the upper deck",
            MissionStage.Strike => RingIsSkill(Sword(p)) ? "Trigger your partner's skill" : Sword(p) ? "Strike the hull plate" : "Fire down the open lane",
            _ => ""
        };
        static Texture2D Ring()
        {
            const int size = 128; var texture = new Texture2D(size,size,TextureFormat.RGBA32,false);
            var pixels = new Color32[size * size];
            for (int y=0;y<size;y++) for(int x=0;x<size;x++)
            {
                float r = new Vector2(x-63.5f,y-63.5f).magnitude;
                pixels[y*size+x] = new Color(1,1,1,Mathf.Clamp01(2.1f - Mathf.Abs(r-60)));
            }
            texture.SetPixels32(pixels); texture.Apply(false,true); return texture;
        }
        void OnGUI()
        {
            if (!IsSpawned || !Route || !Player.Local || Phase == 0 || MissionSilent) return;
            var p = Player.Local;
            missionText ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            float scale = Mathf.Clamp(Screen.height / 820f, .7f, 1.5f);
            missionText.fontSize = Mathf.RoundToInt(17 * scale);
            float centerX = Screen.width * .5f; float y = Screen.height * .18f;
            Color prev = GUI.color;
            GUI.color = new Color(.02f,.05f,.09f,.77f);
            GUI.DrawTexture(new Rect(centerX-220*scale,y,440*scale,54*scale),Texture2D.whiteTexture);
            GUI.color = Signal;
            GUI.Label(new Rect(0,y,Screen.width,27*scale),$"PHASE {Phase}  /  {ObjectiveText(p)}",missionText);
            Vector3 dest = Objective(p); float dist = Vector3.Distance(p.transform.position,dest);
            var cam = Camera.main;
            float bearing = cam ? Vector3.SignedAngle(Vector3.ProjectOnPlane(cam.transform.forward,Vector3.up),Vector3.ProjectOnPlane(dest-p.transform.position,Vector3.up),Vector3.up) : 0;
            string hint = p.Health.IsDead ? "GROGGY    [C] SPECTATE    QTE / SKILLS AVAILABLE" :
                InQte ? "Match the rings" : $"[V] TEAM RADIO     {dist:F0} m   {(Mathf.Abs(bearing)<12 ? "AHEAD" : bearing<0 ? "LEFT" : "RIGHT")}";
            GUI.color = Color.white;
            GUI.Label(new Rect(0,y+24*scale,Screen.width,24*scale),hint,missionText);
            if (InQte)
            {
                ringTexture ??= Ring();
                float dt=(float)(RingTime(Sword(p))-Clock);
                float cy=Screen.height*.68f;
                void DrawRing(float size,Color color) {GUI.color=color;size*=scale;GUI.DrawTexture(new Rect(centerX-size/2,cy-size/2,size,size),ringTexture);}
                DrawRing(68,Color.white); DrawRing(Mathf.Clamp(68+dt*60,35,180),Mathf.Abs(dt)<=ringWindow ? Color.green : Signal);
                int bit=Sword(p)?1:2; bool done=(tried.Value&bit)!=0;
                string key=done ? ((hits.Value&bit)!=0 ? "HIT" : "MISS") : Stage==MissionStage.Land ? "SPACE" : RingIsSkill(Sword(p)) ? "Q" : "LMB";
                GUI.color=Color.white;GUI.Label(new Rect(0,cy-13*scale,Screen.width,26*scale),key,missionText);
            }
            GUI.color=prev;
        }
    }
}
