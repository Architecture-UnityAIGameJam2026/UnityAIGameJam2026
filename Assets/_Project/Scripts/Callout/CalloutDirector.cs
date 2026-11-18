using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// 호스트에서 두 플레이어의 체력 · 대쉬 개수 · 총알 개수를 지켜보다가 의미 있는 변화가 생기면
    /// Jev에게 lines.csv의 대사 중 하나(또는 none)를 고르게 하고, 고른 대사를 모든 화면 오른쪽 위에 띄운다.
    /// 인스펙터의 AI Mediation 체크로 켜고 끈다.
    /// 매 프레임 호출하지 않는다: 상태 단계가 바뀔 때만, 플레이어별 쿨타임을 두고 한 번에 하나씩 요청한다.
    /// </summary>
    public class CalloutDirector : NetworkBehaviour
    {
        [Header("AI Mediation")]
        [Tooltip("끄면 AI Mediation(Jev 판단 + 콜아웃 표시)을 쓰지 않는다. 플레이 중에도 바꿀 수 있다 (호스트 기준)")]
        [SerializeField] bool aiMediationEnabled = true;
        [SerializeField] JevSettings settings;
        [SerializeField] CalloutCatalog catalog;
        [SerializeField] CalloutView view;

        enum HealthLevel { Ok, Low, Critical, Downed }
        enum AmmoLevel { None, Full, Ok, Low, Empty }
        enum DashLevel { Full, Partial, Empty }

        struct Situation
        {
            public HealthLevel health;
            public AmmoLevel ammo;
            public DashLevel dash;
        }

        class Watch
        {
            public Situation last;
            public float settledAt;
            public string pendingEvent;
            public float nextAllowed;
            public int generation;
            public bool inFlight;
        }

        const float SpawnSettleSeconds = 1f;
        readonly Dictionary<Player, Watch> watches = new();
        bool warnedNoKey;

        void Update()
        {
            if (!IsServer) return;
            if (!aiMediationEnabled)
            {
                // 꺼져 있는 동안 생긴 변화는 쌓아 두지 않는다. 다시 켜면 그때 상태부터 지켜본다
                watches.Clear();
                return;
            }

            foreach (var p in Player.All)
            {
                if (!p.IsSpawned) continue;
                var now = Read(p);
                if (!watches.TryGetValue(p, out var w))
                {
                    watches[p] = new Watch { last = now, settledAt = Time.time + SpawnSettleSeconds };
                    continue;
                }
                // 막 생성된 직후에는 동기화 값(대쉬 개수 등)이 아직 0일 수 있어 변화로 보지 않는다
                if (Time.time < w.settledAt) { w.last = now; continue; }
                string evt = Describe(p, w.last, now);
                w.last = now;
                if (evt != null) { w.pendingEvent = evt; w.generation++; }

                if (w.pendingEvent != null && !w.inFlight && Time.time >= w.nextAllowed) Ask(p, w);
            }

            // 라운드 재시작 등으로 사라진 플레이어 정리
            var gone = new List<Player>();
            foreach (var p in watches.Keys) if (p == null || !p.IsSpawned) gone.Add(p);
            foreach (var p in gone) watches.Remove(p);
        }

        Situation Read(Player p)
        {
            var s = new Situation();
            float hp = p.Health.Ratio;
            s.health = p.Health.IsDead ? HealthLevel.Downed
                : hp <= catalog.criticalHealth ? HealthLevel.Critical
                : hp <= catalog.lowHealth ? HealthLevel.Low : HealthLevel.Ok;

            int dash = p.DashStacks, dashMax = p.Stats.dashMaxStacks;
            s.dash = dash <= 0 ? DashLevel.Empty : dash >= dashMax ? DashLevel.Full : DashLevel.Partial;

            if (HasGun(p))
            {
                int ammo = p.Ammo.Current, cap = p.Ammo.Capacity;
                s.ammo = ammo <= 0 ? AmmoLevel.Empty
                    : ammo >= cap ? AmmoLevel.Full
                    : (float)ammo / cap <= catalog.lowAmmo ? AmmoLevel.Low : AmmoLevel.Ok;
            }
            return s;
        }

        // 검 캐릭터의 자원은 총알이 아니라 검기 게이지이므로 총알 개수는 레일건만 보낸다
        static bool HasGun(Player p) => !p.Stats.showDashOnHud;

        /// <summary>알릴 만한 변화면 사건 설명을, 아니면 null을 돌려준다.</summary>
        static string Describe(Player p, Situation before, Situation now)
        {
            string who = p.Stats.displayName;
            if (now.health != before.health)
            {
                if (now.health == HealthLevel.Downed) return $"{who} just went down (health 0, downed, needs a revive).";
                if (now.health > before.health) return $"{who}'s health dropped to {Word(now.health)}.";
                if (now.health == HealthLevel.Ok) return $"{who}'s health recovered back to a safe level.";
                if (before.health == HealthLevel.Downed) return $"{who} was revived but health is still {Word(now.health)}.";
            }
            if (now.ammo != before.ammo)
            {
                if (now.ammo == AmmoLevel.Empty) return $"{who}'s magazine just ran empty.";
                if (now.ammo == AmmoLevel.Low && before.ammo != AmmoLevel.Empty) return $"{who} is running low on ammo.";
                if (now.ammo == AmmoLevel.Full && before.ammo is AmmoLevel.Low or AmmoLevel.Empty) return $"{who}'s magazine was refilled to full.";
            }
            if (now.dash != before.dash)
            {
                if (now.dash == DashLevel.Empty) return $"{who} used up every dash charge.";
                if (now.dash == DashLevel.Full && before.dash == DashLevel.Empty) return $"{who}'s dash charges are fully recharged.";
            }
            return null;
        }

        static string Word(HealthLevel h) => h switch
        {
            HealthLevel.Low => "low",
            HealthLevel.Critical => "critical (in the red)",
            HealthLevel.Downed => "zero",
            _ => "safe",
        };

        void Ask(Player subject, Watch w)
        {
            if (settings == null || !settings.HasKey)
            {
                if (!warnedNoKey) Debug.LogWarning("[Jev] JevSettings에 API 키가 없어 콜아웃을 끕니다. Assets/_Project/Secrets/JevSettings.asset 인스펙터에서 키를 넣으세요.");
                warnedNoKey = true;
                w.pendingEvent = null;
                return;
            }

            string evt = w.pendingEvent;
            int gen = w.generation;
            w.pendingEvent = null;
            w.inFlight = true;
            w.nextAllowed = Time.time + catalog.cooldownSeconds;

            StartCoroutine(JevClient.Choose(settings, BuildState(subject, evt), Instructions, Criteria(), r =>
            {
                w.inFlight = false;
                if (!aiMediationEnabled) return;   // 응답을 기다리는 사이 꺼졌으면 표시하지 않는다
                // 기다리는 동안 상황이 또 바뀌었으면 낡은 결과는 버린다 (새 상황으로 다시 묻는다)
                if (subject == null || !subject.IsSpawned || w.generation != gen) return;
                if (!r.ok || r.choice == "none") return;
                if (r.confidence >= 0f && r.confidence < settings.minConfidence) return;
                var cand = catalog.FindCandidate(r.choice);
                if (cand == null || !catalog.TryGetLine(r.choice, out _)) { Debug.LogWarning($"[Jev] 후보에 없는 ID: {r.choice}"); return; }
                ShowRpc(r.choice, subject.OwnerClientId);
            }));
        }

        const string Instructions =
            "Two players share a co-op game where each one carries the partner's HUD. " +
            "Pick the single prerecorded radio callout that best fits the event in the state. " +
            "Lines addressed with 'you/your' are said by the partner to the subject. " +
            "Lines in first person ('I'm down') are said by the subject. " +
            "Choose 'none' if no line fits.";

        string BuildState(Player subject, string evt)
        {
            var partner = subject.Partner;
            var sb = new StringBuilder();
            sb.AppendLine("Event: " + evt);
            sb.AppendLine($"Subject: {subject.Stats.displayName}");
            sb.AppendLine($"Partner (reads the subject's HUD out loud): {(partner ? partner.Stats.displayName : "none")}");
            sb.AppendLine($"Subject health: {Mathf.RoundToInt(subject.Health.Ratio * 100)}% ({subject.Health.Current:0}/{subject.Health.Max:0})");
            sb.AppendLine($"Subject downed: {(subject.Health.IsDead ? "yes" : "no")}");
            sb.AppendLine($"Subject dash charges (booster fuel): {subject.DashStacks}/{subject.Stats.dashMaxStacks}");
            if (HasGun(subject)) sb.AppendLine($"Subject ammo: {subject.Ammo.Current}/{subject.Ammo.Capacity} rounds in the magazine");
            else sb.AppendLine("Subject ammo: none (melee sword, no gun)");
            return sb.ToString();
        }

        Dictionary<string, string> criteria;

        Dictionary<string, string> Criteria()
        {
            if (criteria != null) return criteria;
            criteria = new Dictionary<string, string>();
            foreach (var c in catalog.candidates)
            {
                if (!catalog.TryGetLine(c.id, out var line)) continue;
                criteria[c.id] = $"\"{line.english}\" ({line.korean}). {c.usage}";
            }
            criteria["none"] = "No line fits the current event. Stay silent.";
            return criteria;
        }

        [Rpc(SendTo.ClientsAndHost)]
        void ShowRpc(string id, ulong subjectClientId)
        {
            if (!catalog.TryGetLine(id, out var line) || view == null) return;
            var subject = FindPlayer(subjectClientId);
            var partner = subject ? subject.Partner : null;
            var cand = catalog.FindCandidate(id);
            bool bySubject = cand != null && cand.spokenBySubject;
            var speaker = bySubject ? subject : partner;
            var listener = bySubject ? partner : subject;
            // 대사 앞에 "누구의 상태인지"를 캐릭터 이름표로 붙인다
            view.Show(Name(subject), subject ? subject.Stats.color : Color.white, Name(speaker), Name(listener),
                line.korean, line.english, catalog.displaySeconds);
        }

        static string Name(Player p) => p ? p.Stats.displayName : "JEV";

        static Player FindPlayer(ulong clientId)
        {
            foreach (var p in Player.All) if (p.OwnerClientId == clientId) return p;
            return null;
        }
    }
}
