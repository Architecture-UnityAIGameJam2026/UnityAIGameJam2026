using System.IO;
using System.Text;
using UnityEngine;

namespace OYM.Net
{
    /// Message types on the co-op wire (first byte of a frame) and small binary writer / reader helpers.
    /// H = host -> guest, G = guest -> host, B = both ways.
    public static class CoopMsg
    {
        public const byte Version = 5;

        // title / session
        public const byte Hello = 1;        // G  u8 version, u8 preferred pilot
        public const byte Welcome = 2;      // H  u8 host pilot, u8 guest pilot (= deploy now)
        public const byte Reject = 3;       // H  string reason
        public const byte Ping = 5;         // B
        public const byte Bye = 6;          // B
        public const byte Ready = 7;        // B  game scene loaded, waiting at the curtain
        public const byte StartCine = 8;    // H  both start the arrival cutscene now
        public const byte SkipCine = 9;     // H  the host skipped it

        // pilots
        public const byte Pilot = 10;       // B  PilotState
        public const byte PilotHit = 11;    // H  f amount, v3 from, f knock (an enemy hit the guest's pilot)
        public const byte SkillInput = 12;  // B  u8 E-key edge for the partner's pilot while skill HUDs are crossed

        // HUSK
        public const byte Spawn = 20;       // H  u16 id, u8 kind, v3 pos, q rot
        public const byte EnemySnap = 21;   // H  u16 n, n x (u16 id, u8 state, v3 pos, q rot, v3 aim)
        public const byte EnemyFx = 22;     // H  u16 id, u8 fx, v3 a, v3 b
        public const byte EnemyDamage = 23; // H  u16 id, f dmg, v3 dir, f power
        public const byte HitEnemy = 24;    // G  u16 id, f dmg, v3 dir, f power

        // level presentation
        public const byte Level = 30;       // H  u8 beat, u8 flags (1 engaged, 8 hostile)
        public const byte Bark = 31;        // H  string speaker, string line
        public const byte ArenaDeckCut = 33; // H  laser start on the shared level clock (f, absent on older hosts)

        // the CITADEL
        public const byte BossStart = 40;   // H  u8 actors (1 = host pilot, 2 = guest pilot)
        public const byte BossLive = 41;    // H  weak points + pods installed
        public const byte BossPose = 42;    // H  v3 pos, q rot
        public const byte WeakHit = 43;     // H  u8 index, f dmg, v3 p, v3 n
        public const byte PodHit = 44;      // H  u8 pod, f dmg, v3 p, v3 n
        public const byte HitWeak = 45;     // G  u8 index, f dmg, v3 p, v3 n
        public const byte HitPod = 46;      // G  u8 pod, f dmg, v3 p, v3 n
        public const byte Victory = 47;     // H  u8 phase (0 sinks, 1 gone)
        public const byte ReturnTitle = 48; // H  both leave the mission result
        public const byte Dart = 50;        // H  v3 tube, v3 dir, u8 target (0 host pilot, 1 guest pilot), f flight, v3 off, i32 seed, f blast, f trail

        // the field kit (OYM.Field.FieldKit): whichever PC's pilot used one tells the other
        public const byte FieldRelay = 60;  // B  u8 relay (calibrated: its visor HUD modules come back on both PCs)
        public const byte FieldMed = 61;    // B  u8 heal pack (taken: gone on both PCs until it respawns)

        public static byte[] RejectMsg(string reason) => new W(Reject).Str(reason).Done();
        public static byte[] Simple(byte type) => new[] { type };

        /// payload builder: new W(type).F(..).V3(..).Done()
        public sealed class W
        {
            readonly MemoryStream _ms = new MemoryStream(64);
            readonly BinaryWriter _w;
            public W(byte type) { _w = new BinaryWriter(_ms, Encoding.UTF8); _w.Write(type); }
            public W U8(int v) { _w.Write((byte)v); return this; }
            public W U16(int v) { _w.Write((ushort)v); return this; }
            public W I32(int v) { _w.Write(v); return this; }
            public W F(float v) { _w.Write(v); return this; }
            public W V3(Vector3 v) { _w.Write(v.x); _w.Write(v.y); _w.Write(v.z); return this; }
            public W Q(Quaternion q) { _w.Write(q.x); _w.Write(q.y); _w.Write(q.z); _w.Write(q.w); return this; }
            public W Str(string s) { _w.Write(s ?? ""); return this; }
            public byte[] Done() { _w.Flush(); return _ms.ToArray(); }
        }

        /// payload reader (skips the type byte)
        public sealed class R
        {
            readonly BinaryReader _r;
            public readonly byte Type;
            public long Remaining => _r.BaseStream.Length - _r.BaseStream.Position;
            public R(byte[] frame) { _r = new BinaryReader(new MemoryStream(frame), Encoding.UTF8); Type = _r.ReadByte(); }
            public int U8() => _r.ReadByte();
            public int U16() => _r.ReadUInt16();
            public int I32() => _r.ReadInt32();
            public float F() => _r.ReadSingle();
            public Vector3 V3() => new Vector3(_r.ReadSingle(), _r.ReadSingle(), _r.ReadSingle());
            public Quaternion Q()
            {
                var q = new Quaternion(_r.ReadSingle(), _r.ReadSingle(), _r.ReadSingle(), _r.ReadSingle());
                float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
                return m > 1e-5f ? new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m) : Quaternion.identity;
            }
            public string Str() => _r.ReadString();
        }
    }
}
