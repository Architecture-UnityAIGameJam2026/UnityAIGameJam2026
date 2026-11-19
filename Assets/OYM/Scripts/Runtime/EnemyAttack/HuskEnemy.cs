using System.Reflection;
using UnityEngine;

namespace OYM
{
    /// One live HUSK in the game scene (single-player, local, zero-g: no navmesh, no physics forces). Drives its attack
    /// rig (WickAttackFX / PrismAttackFX), takes the players' hits through a ZeroGTarget (the KESTREL slash and the
    /// RG-77 only know ZeroGTarget), dies into EnemyShatter pieces. Spawned and woken by HuskEncounter.
    ///   WICK : Dormant -> Rush (leads the player, weaves) -> Fuse (latched, capped speed: a dash shakes it off) -> blast
    ///   PRISM: Dormant -> Station (keeps ~21 m, circles, stays above) -> Charge (lagging aim = dodge window) -> Recover
    /// Co-op (OYM.Net): it goes for the nearest pilot that is up (HuskEncounter.PickTarget, re-picked now and then);
    /// on the guest it is a `mirror` (no AI: the host's state, pose and one-shot looks come in through CoopWorld, this
    /// PC's hits on it go to the host).
    public class HuskEnemy : MonoBehaviour
    {
        public enum Kind { Wick, Prism }
        enum S { Dormant, Alert, Inbound, Rush, Fuse, Station, Charge, Recover, Dead }

        public Kind kind;
        public WickAttackFX wick;
        public PrismAttackFX prism;
        [Tooltip("keeps acting while the level holds everyone for a cutaway")] public bool scripted;
        [Tooltip("PRISM: preferred distance / height above the player (0 = config)")] public float stationDistance, stationHeight;

        HuskEncounter _d;
        HuskEncounterConfig _c;
        S _s = S.Dormant;
        float _hp, _stateT, _wakeIn = -1f, _cool, _phase, _phaseDir = 1f, _height, _deadT, _avoidT, _bob;
        Vector3 _vel, _avoid, _latchOff, _aimDir, _home, _lanceAim;
        bool _lance;
        float _alertDelay;
        ZeroGTarget _target;
        Collider _col;
        HitFlash _flash;
        int _seed;

        static FieldInfo s_flashField;
        static readonly RaycastHit[] s_hits = new RaycastHit[24];

        // ---------------------------------------------------------------- co-op (OYM.Net)
        Transform _tgt;
        float _retarget;
        /// the co-op guest's copy: follows the host's one, no AI of its own
        public bool mirror;
        /// co-op number: the host numbers its HUSK, the guest's copies carry the same one
        public int netId = -1;
        /// (enemy, damage after the weak-point multiplier, dir, power) for every hit that lands (the co-op host sends it on)
        public static event System.Action<HuskEnemy, float, Vector3, float> Damaged;
        /// (enemy, look, a, b): one-shot looks the co-op host sends on
        public static event System.Action<HuskEnemy, int, Vector3, Vector3> NetFx;
        /// NetFx looks: ping (sensor ignites), charge (a = aim, b.x = seconds), fire (a = end, b = normal), cancel, blast (a = point)
        public const int FxPing = 0, FxCharge = 1, FxFire = 2, FxCancel = 3, FxBlast = 4;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetNet() { Damaged = null; NetFx = null; }

        /// the pilot it goes for: the nearest one that is up (only this PC's pilot in single-player)
        Transform Target
        {
            get
            {
                if (!_tgt || !_tgt.gameObject.activeInHierarchy || !_d.AliveOf(_tgt)) _tgt = _d.PickTarget(transform.position, null);
                return _tgt;
            }
        }

        Vector3 Chest => _d.ChestOf(Target);

        public bool Alive => _s != S.Dead;
        public bool IsAwake => _s != S.Dormant && _s != S.Dead;
        public bool Dormant => _s == S.Dormant;
        public bool Latched => _s == S.Fuse;
        public bool Charging => _s == S.Charge;
        public bool Rushing => _s == S.Rush || _s == S.Inbound;
        public Vector3 Velocity => _vel;
        public float HP => _hp;

        // ---------------------------------------------------------------- setup
        public void Setup(HuskEncounter d, Kind k, float wakeIn)
        {
            _d = d; _c = d.config; kind = k;
            wick = GetComponent<WickAttackFX>();
            prism = GetComponent<PrismAttackFX>();
            _hp = k == Kind.Wick ? _c.wickHP : _c.prismHP;
            _wakeIn = wakeIn; _s = S.Dormant; _home = transform.position;
            _seed = Random.Range(0, 10000);
            _phaseDir = Random.value < 0.5f ? -1f : 1f;
            _height = Random.Range(2.5f, 6.5f);
            _bob = Random.value * 10f;

            _col = GetComponent<Collider>();
            if (!_col)
            {
                var cc = gameObject.AddComponent<CapsuleCollider>();
                cc.radius = 0.85f; cc.height = 3.4f; cc.direction = 1;
                _col = cc;
            }
            var rb = GetComponent<Rigidbody>();
            if (!rb) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false;
            _target = gameObject.AddComponent<ZeroGTarget>();
            // ZeroGTarget paints its hit flash straight into _EmissiveColor (and leaves the hull's own glow black
            // afterwards): point it at an invisible dummy, the HitFlash overlay shows the hits instead
            var dummy = new GameObject("[hit target dummy]");
            dummy.transform.SetParent(transform, false);
            var dr = dummy.AddComponent<MeshRenderer>(); dr.enabled = false;
            _target.renderers = new Renderer[] { dr };
            _flash = gameObject.AddComponent<HitFlash>();
            _flash.renderers = ModelRenderers();
            if (prism) prism.autoFire = false;
            if (wick)
            {
                wick.fuseTime = _c.wickFuse;
                wick.blastRadius = _c.wickBlastRadius;
                wick.Detonated += OnBlast;
            }
            if (s_flashField == null) s_flashField = typeof(ZeroGTarget).GetField("_flash", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        Renderer[] ModelRenderers()
        {
            var list = new System.Collections.Generic.List<Renderer>();
            GameObject a = wick ? wick.closedModel : prism ? prism.closedModel : null;
            GameObject b = wick ? wick.openModel : prism ? prism.openModel : null;
            foreach (var g in new[] { a, b })
                if (g) foreach (var r in g.GetComponentsInChildren<MeshRenderer>(true)) list.Add(r);
            return list.ToArray();
        }

        void OnDestroy() { if (wick) wick.Detonated -= OnBlast; }

        public void WakeIn(float s) { if (_s == S.Dormant) _wakeIn = Mathf.Max(0f, s); }

        /// Reveal beat: the sensor ignites but it stays put (Go() / WakeIn() later sends it).
        public void PingOnly()
        {
            if (kind != Kind.Wick || !wick) return;
            wick.Ping();
            NetFx?.Invoke(this, FxPing, Vector3.zero, Vector3.zero);
        }

        /// WICK: wake now and bolt without the alert beat.
        public void Go()
        {
            if (_s == S.Dead) return;
            _wakeIn = -1f; _stateT = 0f;
            if (kind == Kind.Wick) { wick.SetAlert(1f); wick.SetRush(true); _s = S.Rush; }
            else Wake();
        }

        /// Comes in from far away at transit speed (out of the boss hatch, out of the dark), then rushes / takes station.
        public void Inbound(Vector3 initialVelocity)
        {
            _wakeIn = -1f; _stateT = 0f; _s = S.Inbound;
            _vel = initialVelocity;
            if (wick) { wick.SetAlert(1f); wick.SetRush(true); }
        }

        /// PRISM, scripted: charge for `charge` s with the aim nailed on `aimPoint` (no tracking) and fire there.
        /// The sniper reveal: a lance that tears past the player on purpose.
        public void ScriptedLance(Vector3 aimPoint, float charge)
        {
            if (kind != Kind.Prism || _s == S.Dead) return;
            if (_s == S.Dormant) Wake();
            Vector3 lens = prism.LensWorld;
            _lanceAim = aimPoint; _lance = true;
            _aimDir = (aimPoint - lens).normalized;
            prism.BeginCharge(Mathf.Max(0.8f, charge), aimPoint);
            NetFx?.Invoke(this, FxCharge, aimPoint, new Vector3(Mathf.Max(0.8f, charge), 0f, 0f));
            _s = S.Charge; _stateT = 0f;
        }

        void Wake()
        {
            _wakeIn = -1f; _stateT = 0f;
            if (kind == Kind.Wick) { wick.Ping(); _s = S.Alert; _alertDelay = Random.Range(0.25f, 0.45f); }
            else
            {
                Vector3 rel = transform.position - Chest;
                _phase = Mathf.Atan2(rel.x, rel.z) * Mathf.Rad2Deg;
                _s = S.Station; _cool = Random.Range(0.8f, 2f);
            }
        }

        // ---------------------------------------------------------------- loop
        void Update()
        {
            if (_d == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            _stateT += dt;
            PollHits();
            if (mirror)
            {
                // the co-op guest's copy: pose and looks come from the host (CoopWorld)
                if (_s == S.Dead) { _deadT += dt; if (_deadT > 3f) Destroy(gameObject); }
                return;
            }
            _retarget -= dt;
            if (_retarget <= 0f)
            {
                _retarget = 1.1f + (_seed % 9) * 0.07f;
                if (_s != S.Fuse && _s != S.Charge) _tgt = _d.PickTarget(transform.position, _tgt);
            }
            bool hostile = _d.Player && (_d.Hostile || (scripted && _d.Gate));
            switch (_s)
            {
                case S.Dormant: Sleep(dt, hostile); break;
                case S.Alert: Alert(dt, hostile); break;
                case S.Inbound: Inbound(dt, hostile); break;
                case S.Rush: Rush(dt, hostile); break;
                case S.Fuse: Fuse(dt); break;
                case S.Station: Station(dt, hostile); break;
                case S.Charge: Charge(dt, hostile); break;
                case S.Recover: Recover(dt); break;
                case S.Dead:
                    _deadT += dt;
                    if (_deadT > 3f) Destroy(gameObject);
                    break;
            }
        }

        void Sleep(float dt, bool hostile)
        {
            _bob += dt;
            transform.position = _home + new Vector3(Mathf.Sin(_bob * 0.4f), Mathf.Sin(_bob * 0.55f + 1f), 0f) * 0.12f;
            if (hostile && _wakeIn >= 0f)
            {
                _wakeIn -= dt;
                if (_wakeIn <= 0f) Wake();
            }
        }

        void Alert(float dt, bool hostile)
        {
            Move(Vector3.zero, 6f, dt);
            Face(Chest - transform.position, 6f, dt);                 // turns onto the player, sensor lit
            if (hostile && _stateT >= _alertDelay) { wick.SetRush(true); _s = S.Rush; _stateT = 0f; }
        }

        /// transit: fast straight run in, braking into the attack range
        void Inbound(float dt, bool hostile)
        {
            Vector3 pos = transform.position, chest = Chest;
            Vector3 to = chest - pos;
            float dist = to.magnitude;
            float handOff = kind == Kind.Wick ? 30f : (stationDistance > 0f ? stationDistance : _c.prismDistance) + 6f;
            if (dist <= handOff)
            {
                _stateT = 0f;
                if (kind == Kind.Wick) _s = S.Rush;
                else { Vector3 rel = pos - chest; _phase = Mathf.Atan2(rel.x, rel.z) * Mathf.Rad2Deg; _s = S.Station; _cool = Random.Range(1f, 2f); }
                return;
            }
            float speed = Mathf.Lerp(_c.wickSpeed, 36f, Mathf.Clamp01((dist - handOff) / 40f));
            Move(to / dist * speed + Avoid(dt, kind == Kind.Wick ? 0.6f : 1.3f), 60f, dt);
            Face(_vel.sqrMagnitude > 1f ? _vel : to, 6f, dt);
        }

        // ---- WICK
        void Rush(float dt, bool hostile)
        {
            if (!hostile)
            {
                wick.SetRush(false);
                Move(Vector3.zero, 8f, dt);
                return;
            }
            wick.SetRush(true);
            Vector3 pos = transform.position, chest = Chest;
            Vector3 to = chest - pos;
            float dist = to.magnitude;
            if (dist <= _c.wickTrigger) { Arm(chest); return; }
            float lead = Mathf.Clamp(dist / _c.wickSpeed, 0f, 0.7f);
            Vector3 aim = chest + _d.VelocityOf(Target) * lead;
            Vector3 dir = (aim - pos).normalized;
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            side = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.right;
            Vector3 up2 = Vector3.Cross(side, dir);
            float weave = Mathf.Clamp01((dist - 4f) / 14f);
            Vector3 lateral = (side * (Mathf.Sin(_stateT * 2.1f + _seed) * 5.5f) + up2 * (Mathf.Sin(_stateT * 1.3f + _seed * 0.7f) * 2.5f)) * weave;
            float speed = _c.wickSpeed * Mathf.Lerp(0.55f, 1f, HuskFX.Smooth(0f, 1.2f, _stateT));
            Vector3 desired = dir * speed + lateral + Avoid(dt, 0.55f) + Separation(2.5f) * 4f;
            Move(desired, _c.wickAccel, dt);
            Face(_vel.sqrMagnitude > 0.5f ? _vel : to, 10f, dt);
        }

        void Arm(Vector3 chest)
        {
            _s = S.Fuse; _stateT = 0f;
            Vector3 off = transform.position - chest;
            _latchOff = off.sqrMagnitude > 1e-4f ? off.normalized * 1.25f : Vector3.forward * 1.25f;
            wick.Arm(_c.wickFuse);
        }

        void Fuse(float dt)
        {
            if (wick.Blasted) { if (_s != S.Dead) Kill(false); return; }
            Vector3 chest = Chest;
            Vector3 want = chest + _latchOff - transform.position;
            Vector3 desired = Vector3.ClampMagnitude(want * 6f, _c.wickLatchSpeed);
            Move(desired, 40f, dt);
            Face(chest - transform.position, 14f, dt);
        }

        void OnBlast(Vector3 p, float radius)
        {
            if (_s == S.Dead) return;
            if (!mirror)                                             // the guest's copy only shows the host's blast
            {
                NetFx?.Invoke(this, FxBlast, p, Vector3.zero);
                _d.BlastPilots(p, radius, _c.wickDamage, 11f);      // every pilot caught in it (falloff from 1 m)
                // chain reaction: the blast hurts other HUSK caught in it
                foreach (var e in _d.Enemies)
                    if (e && e != this && e.Alive && Vector3.Distance(e.transform.position, p) < radius)
                        e.TakeDamage(_c.wickDamage, (e.transform.position - p), 1.2f);
            }
            Kill(false);
        }

        // ---- PRISM
        void Station(float dt, bool hostile)
        {
            Vector3 pos = transform.position, chest = Chest;
            _phase += _phaseDir * 7f * dt;
            Vector3 flat = Quaternion.Euler(0f, _phase, 0f) * Vector3.forward;
            float sd = stationDistance > 0f ? stationDistance : _c.prismDistance;
            float sh = stationHeight > 0f ? stationHeight : _height;
            Vector3 station = chest + flat * sd + Vector3.up * sh;
            Vector3 desired = Vector3.ClampMagnitude((station - pos) * 0.8f, _c.prismSpeed) + Avoid(dt, 1.2f) + Separation(7f) * 3f;
            Move(desired, 6f, dt);
            FaceYaw(chest - pos, 3f, dt);
            _cool -= dt;
            float dist = Vector3.Distance(prism.LensWorld, chest);
            if (hostile && _cool <= 0f && dist < _c.prismRange && LineOfSight(prism.LensWorld, chest)) BeginCharge(chest);
        }

        void BeginCharge(Vector3 chest)
        {
            Vector3 lens = prism.LensWorld;
            Vector3 to = chest - lens;
            float dist = to.magnitude;
            Vector3 dir = to / Mathf.Max(1e-3f, dist);
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            side = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.right;
            // the sighting beam starts off to one side and drags onto the target
            _aimDir = Quaternion.AngleAxis(Random.Range(12f, 20f) * (Random.value < 0.5f ? -1f : 1f), Vector3.up)
                      * Quaternion.AngleAxis(Random.Range(-6f, 6f), side) * dir;
            prism.BeginCharge(_c.prismCharge, lens + _aimDir * dist);
            NetFx?.Invoke(this, FxCharge, lens + _aimDir * dist, new Vector3(_c.prismCharge, 0f, 0f));
            _s = S.Charge; _stateT = 0f;
        }

        void Charge(float dt, bool hostile)
        {
            Vector3 chest = Chest;
            Move(Vector3.zero, 4f, dt);
            FaceYaw(chest - transform.position, 2f, dt);
            Vector3 lens = prism.LensWorld;
            if (_lance)
            {
                _aimDir = (_lanceAim - lens).normalized;
                prism.SetAim(_lanceAim);
                if (prism.Clock >= prism.chargeTime)
                {
                    float range = Vector3.Distance(lens, _lanceAim) + 30f;     // past the player, whatever the distance
                    if (RaycastWorld(lens, _aimDir, range, out var lh)) PrismFire(lh.point, lh.normal, lh.collider);
                    else PrismFire(lens + _aimDir * range, -_aimDir, null);
                    _lance = false; scripted = false;
                    _s = S.Recover; _stateT = 0f; _cool = _c.prismCooldown + 1.5f;
                }
                return;
            }
            Vector3 want = (chest - lens).normalized;
            _aimDir = Vector3.RotateTowards(_aimDir, want, _c.prismTracking * Mathf.Deg2Rad * dt, 0f);
            prism.SetAim(lens + _aimDir * Vector3.Distance(lens, chest));
            if (!hostile) { PrismCancel(); _s = S.Recover; _stateT = 0f; _cool = 2f; return; }
            if (prism.Clock >= prism.chargeTime) Fire(lens, chest);
        }

        void Fire(Vector3 lens, Vector3 chest)
        {
            float range = _c.prismRange + 15f;
            bool worldHit = RaycastWorld(lens, _aimDir, range, out var wh);
            float worldDist = worldHit ? wh.distance : range;
            Vector3 rel = chest - lens;
            float along = Vector3.Dot(rel, _aimDir);
            float miss = (rel - _aimDir * along).magnitude;
            if (along > 0f && along < worldDist && miss < _c.prismHitRadius && _d.AliveOf(Target))
            {
                PrismFire(lens + _aimDir * Mathf.Max(0.5f, along - 0.25f), -_aimDir, null);
                _d.DamagePilot(Target, _c.prismDamage, lens, 6f);
            }
            else if (worldHit) PrismFire(wh.point, wh.normal, wh.collider);
            else PrismFire(lens + _aimDir * range, -_aimDir, null);
            _s = S.Recover; _stateT = 0f;
            _cool = _c.prismCooldown + Random.Range(0f, 1.5f);
        }

        void Recover(float dt)
        {
            Vector3 chest = Chest;
            Move(Vector3.zero, 3f, dt);
            FaceYaw(chest - transform.position, 2f, dt);
            _cool -= dt;
            if (!prism.Busy || _stateT > 3f) { _s = S.Station; _stateT = 0f; }
        }

        /// a pilot went down: a PRISM charging at that pilot lets go
        public void OnPilotDown(Transform pilot)
        {
            if (_s == S.Charge && !_lance && (!_tgt || _tgt == pilot)) { PrismCancel(); _s = S.Recover; _stateT = 0f; _cool = 3f; }
        }

        public void OnPlayerDown() => OnPilotDown(_d ? _d.Player : null);

        void PrismFire(Vector3 end, Vector3 n, Collider hit)
        {
            prism.Fire(end, n, hit);
            NetFx?.Invoke(this, FxFire, end, n);
        }

        void PrismCancel()
        {
            prism.Cancel();
            NetFx?.Invoke(this, FxCancel, Vector3.zero, Vector3.zero);
        }

        // ---------------------------------------------------------------- getting hit
        void PollHits()
        {
            if (_s == S.Dead || !_target || s_flashField == null) return;
            if ((float)s_flashField.GetValue(_target) < 0.5f) return;
            s_flashField.SetValue(_target, 0f);                     // consumed: ZeroGTarget will not paint anything
            var pf = _d.PlayerFlight;
            bool slash = pf is KestrelController;
            Vector3 from = _d.PlayerChest;                           // this PC's pilot made the hit
            float dmg = slash ? _c.slashDamage : _c.railDamage, power = slash ? 1.2f : 1.4f;
            Vector3 dir = transform.position - from;
            if (HuskEncounter.RouteHit(this, dmg, transform.position, dir, power)) return;   // co-op guest: the host applies it
            TakeDamage(dmg, dir, power);
        }

        public void TakeDamage(float dmg, Vector3 dir, float power)
        {
            if (_s == S.Dead) return;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : transform.forward;
            if (kind == Kind.Prism && _s == S.Charge && !_lance && !mirror)
            {
                dmg *= _c.prismWeakMul;                                 // lens open: weak point
                PrismCancel();
                _s = S.Recover; _stateT = 0f; _cool = 1.8f;
            }
            Damaged?.Invoke(this, dmg, dir, power);
            Hurt(dmg, dir, power, true);
        }

        void Hurt(float dmg, Vector3 dir, float power, bool react)
        {
            Vector3 p = _col ? _col.ClosestPoint(transform.position - dir * 3f) : transform.position;
            _hp -= dmg;
            if (_flash) _flash.AddHit(p, new Color(6f, 3.4f, 1.6f), kind == Kind.Wick ? 0.5f : 0.9f, 0.5f);
            _vel += dir * (kind == Kind.Wick ? 7f : 2.5f);             // zero-g shove
            if (react && (_s == S.Dormant || _s == S.Alert)) Go();
            if (_hp <= 0f) Die(dir, power);
        }

        // ---------------------------------------------------------------- co-op mirror (OYM.Net.CoopWorld)
        /// host: the state the snapshot carries
        public int NetState => (int)_s;
        /// host: where a PRISM's sighting beam points right now
        public Vector3 NetAim => prism ? prism.LensWorld + _aimDir * 40f : transform.position;

        /// guest: this one copies the host's HUSK number `id` (no AI, no fuse of its own)
        public void MakeMirror(int id)
        {
            mirror = true; netId = id;
            if (wick) wick.autoDetonate = false;
        }

        /// guest: the host's state; a change brings its look (sensor ping, thruster, fuse)
        public void MirrorState(int state)
        {
            var s = (S)state;
            if (_s == S.Dead || s == _s || s == S.Dead) return;      // deaths come as hits / blasts
            var was = _s;
            _s = s; _stateT = 0f;
            if (!wick) return;
            if (s == S.Alert && was == S.Dormant) wick.Ping();
            if (s == S.Rush || s == S.Inbound) { wick.SetAlert(1f); wick.SetRush(true); }
            else if (s == S.Fuse) { if (!wick.Armed) wick.Arm(_c.wickFuse); }
            else wick.SetRush(false);
        }

        /// guest: a one-shot look of the host's copy (NetFx)
        public void MirrorFx(int fx, Vector3 a, Vector3 b)
        {
            if (_s == S.Dead) return;
            switch (fx)
            {
                case FxPing: if (wick) wick.Ping(); break;
                case FxCharge: if (prism) { prism.BeginCharge(Mathf.Max(0.3f, b.x), a); _s = S.Charge; } break;
                case FxFire: if (prism) prism.Fire(a, b, null); break;
                case FxCancel: if (prism) prism.Cancel(); break;
                case FxBlast: if (wick) wick.Detonate(); else Kill(false); break;   // -> Detonated -> OnBlast -> Kill
            }
        }

        /// guest: a charging PRISM's aim on the host
        public void MirrorAim(Vector3 aim) { if (prism && _s == S.Charge && prism.Busy) prism.SetAim(aim); }

        /// guest: the host's hit, already final (no weak-point multiplier, no AI reaction)
        public void MirrorDamage(float dmg, Vector3 dir, float power)
        {
            if (_s == S.Dead) return;
            dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : transform.forward;
            Hurt(dmg, dir, power, false);
        }

        /// guest: the host's copy is gone and no hit said so (safety net)
        public void MirrorGone() { if (_s != S.Dead) Die(transform.forward, 1f); }

        void Die(Vector3 dir, float power)
        {
            GameObject model = null;
            if (wick) model = wick.openModel && wick.openModel.activeInHierarchy ? wick.openModel : wick.closedModel;
            if (prism) model = prism.openModel && prism.openModel.activeInHierarchy ? prism.openModel : prism.closedModel;
            bool shattered = model && HuskFX.Shatter(model, dir, power, true);   // copies the pose, hides the renderers
            if (!shattered)
            {
                HuskFX.PlayHit(HitKind.Explosion, transform.position, -dir, null, kind == Kind.Wick ? 0.5f : 1f, null);
                if (wick) { if (wick.closedModel) wick.closedModel.SetActive(false); if (wick.openModel) wick.openModel.SetActive(false); }
                if (prism) { if (prism.closedModel) prism.closedModel.SetActive(false); if (prism.openModel) prism.openModel.SetActive(false); }
            }
            Kill(true);
        }

        void Kill(bool byPlayer)
        {
            _s = S.Dead; _deadT = 0f;
            if (_col) _col.enabled = false;
            if (wick) { wick.SetRush(false); if (byPlayer) wick.enabled = false; }   // a cut fuse never blows
            if (prism) prism.enabled = false;
            _d.OnEnemyGone(this, byPlayer);
        }

        // ---------------------------------------------------------------- motion helpers
        void Move(Vector3 desired, float accel, float dt)
        {
            _vel = Vector3.MoveTowards(_vel, desired, accel * dt);
            transform.position += _vel * dt;
        }

        void Face(Vector3 dir, float rate, float dt)
        {
            if (dir.sqrMagnitude < 1e-4f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 1f - Mathf.Exp(-rate * dt));
        }

        void FaceYaw(Vector3 dir, float rate, float dt)
        {
            dir.y = 0f;
            Face(dir, rate, dt);
        }

        Vector3 Separation(float radius)
        {
            Vector3 push = Vector3.zero;
            foreach (var e in _d.Enemies)
            {
                if (!e || e == this || !e.Alive || e.kind != kind) continue;
                Vector3 d = transform.position - e.transform.position;
                float m = d.magnitude;
                if (m < radius && m > 1e-3f) push += d / m * (1f - m / radius);
            }
            return push;
        }

        /// steer round debris ahead (sphere cast along the velocity a few times a second)
        Vector3 Avoid(float dt, float radius)
        {
            _avoidT -= dt;
            if (_avoidT <= 0f)
            {
                _avoidT = 0.12f;
                _avoid = Vector3.zero;
                float sp = _vel.magnitude;
                if (sp > 1f)
                {
                    Vector3 dir = _vel / sp;
                    float look = sp * 0.7f + 2f;
                    if (CastWorld(transform.position, radius, dir, look, out var h))
                    {
                        Vector3 away = Vector3.ProjectOnPlane(h.normal, dir);
                        if (away.sqrMagnitude < 1e-3f) away = Vector3.Cross(dir, Vector3.up);
                        _avoid = (away.normalized * 1.4f + h.normal * 0.6f) * sp * (1f - h.distance / look);
                    }
                }
            }
            return _avoid;
        }

        bool Ignore(Collider c)
        {
            if (!c || c.isTrigger) return true;
            if (c.GetComponentInParent<HuskEnemy>()) return true;
            return _d.IsPilotCollider(c);                            // either pilot (co-op)
        }

        bool RaycastWorld(Vector3 o, Vector3 dir, float dist, out RaycastHit best)
        {
            best = default;
            int n = Physics.RaycastNonAlloc(o, dir, s_hits, dist, ~0, QueryTriggerInteraction.Ignore);
            float bd = float.MaxValue; bool any = false;
            for (int i = 0; i < n; i++)
            {
                if (Ignore(s_hits[i].collider) || s_hits[i].distance >= bd) continue;
                bd = s_hits[i].distance; best = s_hits[i]; any = true;
            }
            return any;
        }

        bool CastWorld(Vector3 o, float r, Vector3 dir, float dist, out RaycastHit best)
        {
            best = default;
            int n = Physics.SphereCastNonAlloc(o, r, dir, s_hits, dist, ~0, QueryTriggerInteraction.Ignore);
            float bd = float.MaxValue; bool any = false;
            for (int i = 0; i < n; i++)
            {
                if (Ignore(s_hits[i].collider) || s_hits[i].distance <= 0f || s_hits[i].distance >= bd) continue;
                bd = s_hits[i].distance; best = s_hits[i]; any = true;
            }
            return any;
        }

        bool LineOfSight(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float m = d.magnitude;
            return m < 1e-3f || !RaycastWorld(from, d / m, m - 0.5f, out _);
        }
    }
}
