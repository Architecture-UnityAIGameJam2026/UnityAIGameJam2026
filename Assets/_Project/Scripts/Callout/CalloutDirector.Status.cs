using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace OnYourMark
{
    public partial class CalloutDirector
    {
        [SerializeField] CalloutCatalog catalog;
        readonly Dictionary<Player,float> nextStatus = new();

        void ObserveStatus(Player p, Reading previous, Reading now)
        {
            if (!catalog || !aiMediationEnabled || evaluating || Time.unscaledTime < busyUntil ||
                !nextStatus.TryGetValue(p, out float after) || Time.unscaledTime < after) return;
            string id = null;
            if (now.down != previous.down) id = now.down ? "downed" : "thanks";
            else if (now.beingRevived && !previous.beingRevived) id = "reviving";
            else if (now.hp != previous.hp) id = now.hp == 0 ? "heal_crit" : now.hp == 1 ? "heal_now" : "heal_ok";
            else if (p.Role == "ranged" && previous.ammo > 0 && now.ammo == 0) id = "ammo_empty";
            else if (p.Role == "ranged" && previous.ammo > p.Ammo.Capacity * catalog.lowAmmo && now.ammo <= p.Ammo.Capacity * catalog.lowAmmo) id = "ammo_low";
            else if (previous.dash > 0 && now.dash == 0) id = "fuel_low";
            else if (previous.dash < p.DashCapacity && now.dash == p.DashCapacity) id = "fuel_full";
            if (id == null || !catalog.TryGetLine(id, out _)) return;
            nextStatus[p] = Time.unscaledTime + catalog.cooldownSeconds;
            StartCoroutine(StatusChoice(p,now,id));
        }

        IEnumerator StatusChoice(Player p, Reading snapshot, string id)
        {
            int generation = revision;
            evaluating = true;
            try
            {
                if (settings && settings.HasKey)
                {
                    var choices = catalog.candidates.Where(c => catalog.TryGetLine(c.id,out _)).ToDictionary(c=>c.id,c=>c.usage);
                    choices["none"] = "Stay silent";
                    JevClient.Result answer = default;
                    yield return JevClient.Choose(settings,State(p.Role+" status changed: "+id,null),
                        "Select a prerecorded status callout only if it accurately describes the current change.",choices,r=>answer=r);
                    if (answer.ok)
                    {
                        if (answer.choice == "none" || !choices.ContainsKey(answer.choice) ||
                            (answer.confidence >= 0 && answer.confidence < settings.minConfidence)) yield break;
                        id = answer.choice;
                    }
                }
                if (!p || !p.IsSpawned || !aiMediationEnabled || revision != generation || !p.Partner ||
                    (waves && waves.MissionSilent) || !history.TryGetValue(p,out var latest)) yield break;
                if (settings && settings.HasKey && (latest.down != snapshot.down || latest.hp != snapshot.hp ||
                    (id.StartsWith("ammo_") && latest.ammo != snapshot.ammo) ||
                    (id.StartsWith("fuel_") && latest.dash != snapshot.dash) ||
                    (id == "reviving" && !latest.beingRevived))) yield break;
                var clip = library.StatusClip(id);
                if (!clip) yield break;
                busyUntil = Time.unscaledTime + clip.length + radioGap;
                StatusRadioRpc(p.Role,id);
            }
            finally { evaluating = false; }
        }
        [Rpc(SendTo.ClientsAndHost)]
        void StatusRadioRpc(string role,string id)
        {
            var p = Player.All.FirstOrDefault(pilot=>pilot.Role==role);
            if (!p || !p.Partner || !catalog || !catalog.TryGetLine(id,out var line)) return;
            var candidate=catalog.FindCandidate(id);
            var clip=library.StatusClip(id);
            if (candidate == null || !clip) return;
            var speaker=candidate.spokenBySubject?p:p.Partner;
            channel.clip=clip;channel.Play();
            view.Show(p.Stats.displayName,p.Stats.color,speaker.Stats.displayName,speaker.Partner.Stats.displayName,
                line.korean,line.english,Mathf.Max(catalog.displaySeconds,clip.length));
        }
    }
}
