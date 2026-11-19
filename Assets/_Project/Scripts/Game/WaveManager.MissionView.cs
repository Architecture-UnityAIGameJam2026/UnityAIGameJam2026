using UnityEngine;

namespace OnYourMark
{
    public partial class WaveManager
    {
        static readonly Color Signal = new(.45f, 1f, .88f);
        LineRenderer beam;
        AudioClip suitLoop;
        AudioSource suitAudio;
        double viewStarted;
        GUIStyle missionText;

        void StageChanged(MissionStage from, MissionStage to)
        {
            if (!Route) return;
            viewStarted = Time.unscaledTimeAsDouble;
            if (beam) Destroy(beam.gameObject);
            if (to == MissionStage.Split)
            {
                var fx = new GameObject("Mission beam"); fx.transform.SetParent(transform);
                beam = fx.AddComponent<LineRenderer>(); beam.sharedMaterial = Route.signalMaterial;
                beam.startWidth = beam.endWidth = .34f;
                beam.positionCount = 2;
            }
            if (Route.finalPlate && to == MissionStage.Climb) Route.finalPlate.localRotation = Route.ClosedPlate;
            if (to == MissionStage.Ending)
            {
                OYM.BgmPlayer.FadeOut(.4f);
                if (radio) radio.Silence();
                if (!suitAudio) suitAudio = gameObject.AddComponent<AudioSource>();
                suitAudio.clip = suitLoop = CreateEndingAudio(); suitAudio.loop = true; suitAudio.volume = .32f; suitAudio.Play();
            }
        }
        internal static AudioClip CreateEndingAudio()
        {
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
            var clip = AudioClip.Create("Suit breathing and open radio", samples.Length, 1, rate, false);
            clip.SetData(samples, 0); return clip;
        }
        void ClearMissionView()
        {
            if (suitAudio) Destroy(suitAudio);
            if (suitLoop) Destroy(suitLoop);
            if (beam) Destroy(beam.gameObject);
        }
        void LateUpdate()
        {
            if (!IsSpawned || !Route) return;
            if (Stage == MissionStage.Split)
                Route.MoveLanding(8f + 4f * Mathf.Sin((float)Clock * .5f));
            else Route.MoveLanding(0);
            if (!beam) return;
            float elapsed = (float)(Time.unscaledTimeAsDouble - viewStarted);
            beam.enabled = elapsed < 1.9f;
            if (!beam.enabled) return;
            Vector3 mid = Route.deck[3].position + Vector3.up + Vector3.forward * (elapsed - .95f) * 3;
            beam.SetPosition(0, mid + Vector3.left * 20 + Vector3.up * 4);
            beam.SetPosition(1, mid + Vector3.right * 20 - Vector3.up * 2);
        }
        public string ObjectiveText(Player p) => Stage switch
        {
            MissionStage.FirstCrossing => Sword(p) ? "Reach the first hull fragment" : "Cover Kestrel, then jump",
            MissionStage.ApproachLaser => "Clear the approach and regroup at the laser",
            MissionStage.Split => Sword(p) ? "Suit link crossed  /  Follow the upper hull" : "Suit link crossed  /  Follow the lower truss",
            MissionStage.Reunion => "Clear the landing and cross the platform",
            MissionStage.Climb => "Fight through to the upper deck",
            _ => ""
        };
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
            string hint = p.Health.IsDead ? "GROGGY    [C] SPECTATE    SKILLS AVAILABLE" :
                $"[V] TEAM RADIO     {dist:F0} m   {(Mathf.Abs(bearing)<12 ? "AHEAD" : bearing<0 ? "LEFT" : "RIGHT")}";
            GUI.color = Color.white;
            GUI.Label(new Rect(0,y+24*scale,Screen.width,24*scale),hint,missionText);
            GUI.color=prev;
        }
    }
}
