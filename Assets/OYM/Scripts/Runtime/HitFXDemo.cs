using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace OYM
{
    /// Play-mode test bench for HitFXTest.unity.
    ///   LMB            fire the selected hit type at whatever is under the cursor (PRISM, hull plate, KESTREL, deck)
    ///   1 / 2 / 4      select KINETIC / EXPLOSION / ENERGY BOLT (and fire once at its showcase target)
    ///   3 (hold)       PRISM's laser on the hull plate
    ///   P              auto showcase loop on / off (any manual shot turns it off)
    ///   RMB + WASD     fly (FreeFlyCamera), Space / Ctrl up / down, Shift fast
    public class HitFXDemo : MonoBehaviour
    {
        public Transform kineticTarget, explosionTarget, energyTarget;
        public Transform shooter;            // where the showcase shots come from (camera side)
        public BeamFX beam;
        public bool autoLoop = true;
        public float period = 1.4f;
        public HitKind selected = HitKind.Kinetic;
        public bool showHelp = true;

        float _t;
        int _step;

        void Update()
        {
            bool k1 = false, k2 = false, k3 = false, k4 = false, toggle = false, fire = false, help = false;
            Vector2 mouse = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current; var m = Mouse.current;
            if (kb != null)
            {
                k1 = kb.digit1Key.wasPressedThisFrame; k2 = kb.digit2Key.wasPressedThisFrame;
                k3 = kb.digit3Key.isPressed; k4 = kb.digit4Key.wasPressedThisFrame;
                toggle = kb.pKey.wasPressedThisFrame; help = kb.hKey.wasPressedThisFrame;
            }
            if (m != null) { fire = m.leftButton.wasPressedThisFrame; mouse = m.position.ReadValue(); }
#else
            k1 = Input.GetKeyDown(KeyCode.Alpha1); k2 = Input.GetKeyDown(KeyCode.Alpha2); k3 = Input.GetKey(KeyCode.Alpha3);
            k4 = Input.GetKeyDown(KeyCode.Alpha4); toggle = Input.GetKeyDown(KeyCode.P); help = Input.GetKeyDown(KeyCode.H);
            fire = Input.GetMouseButtonDown(0); mouse = Input.mousePosition;
#endif
            if (toggle) autoLoop = !autoLoop;
            if (help) showHelp = !showHelp;
            if (k1) { Manual(); selected = HitKind.Kinetic; Fire(HitKind.Kinetic, kineticTarget); }
            if (k2) { Manual(); selected = HitKind.Explosion; Fire(HitKind.Explosion, explosionTarget); }
            if (k4) { Manual(); selected = HitKind.EnergyBolt; Fire(HitKind.EnergyBolt, energyTarget); }
            if (k3) Manual();
            if (fire) { Manual(); FireAtScreen(mouse); }

            bool beamOn = k3;
            if (autoLoop)
            {
                _t += Time.deltaTime;
                if (_t >= period)
                {
                    _t = 0f;
                    switch (_step++ % 5)
                    {
                        case 0: Fire(HitKind.Kinetic, kineticTarget); break;
                        case 1: Fire(HitKind.Kinetic, kineticTarget); break;
                        case 2: Fire(HitKind.Explosion, explosionTarget); break;
                        case 3: Fire(HitKind.EnergyBolt, energyTarget); break;
                    }
                }
                if (_step % 5 == 0 && _step > 0) beamOn = true;   // the 5th slot holds PRISM's beam
            }
            if (beam) beam.firing = beamOn;
        }

        void Manual() { autoLoop = false; }

        void FireAtScreen(Vector2 screen)
        {
            var cam = Camera.main;
            if (!cam) return;
            var ray = cam.ScreenPointToRay(screen);
            if (!Physics.Raycast(ray, out var hit, 2000f, ~0, QueryTriggerInteraction.Ignore)) return;
            var kind = selected == HitKind.LaserBurn ? HitKind.Kinetic : selected;
            HitFX.Play(kind, hit.point, hit.normal, hit.collider);
            if (kind == HitKind.EnergyBolt && energyTarget && hit.collider.transform.IsChildOf(energyTarget)) HitScreenFX.PlayerHit(0.6f);
        }

        public void Fire(HitKind kind, Transform target)
        {
            if (!target) return;
            Vector3 from = shooter ? shooter.position : (Camera.main ? Camera.main.transform.position : Vector3.zero);
            var b = Bounds(target);
            Vector3 aim = b.center + Random.insideUnitSphere * b.extents.magnitude * 0.2f;
            if (RaycastTarget(target, new Ray(from, (aim - from).normalized), out var hit))
            {
                HitFX.Play(kind, hit.point, hit.normal, hit.collider);
                if (kind == HitKind.EnergyBolt) HitScreenFX.PlayerHit(0.6f);
            }
        }

        public static Bounds Bounds(Transform root)
        {
            var cs = root.GetComponentsInChildren<Collider>();
            if (cs.Length == 0) return new Bounds(root.position, Vector3.one);
            var b = cs[0].bounds; foreach (var c in cs) b.Encapsulate(c.bounds);
            return b;
        }

        /// Nearest hit on any collider under `root` (ignores everything else in the scene).
        public static bool RaycastTarget(Transform root, Ray ray, out RaycastHit best)
        {
            best = default; bool any = false;
            foreach (var c in root.GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger || !c.enabled) continue;
                if (c.Raycast(ray, out var h, 1000f) && (!any || h.distance < best.distance)) { best = h; any = true; }
            }
            return any;
        }

        static GUIStyle _style;

        void OnGUI()
        {
            if (!showHelp) return;
            if (_style == null) _style = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true };
            string sel(HitKind k) => selected == k ? "<color=#ffd24a>> " : "<color=#c8c8c8>  ";
            string text =
                "<b>HIT FX TEST</b>   (H: hide)\n" +
                sel(HitKind.Kinetic) + "1  KINETIC   railgun slug</color>\n" +
                sel(HitKind.Explosion) + "2  EXPLOSION missile / WICK</color>\n" +
                "<color=#c8c8c8>  3  LASER     hold: PRISM beam</color>\n" +
                sel(HitKind.EnergyBolt) + "4  ENERGY    HUSK bolt</color>\n" +
                "<color=#c8c8c8>LMB fire at cursor   P auto loop: " + (autoLoop ? "ON" : "OFF") + "\n" +
                "RMB+WASD fly   Space/Ctrl up/down   Shift fast</color>";
            GUI.Label(new Rect(16, 12, 520, 200), text, _style);
        }
    }
}
