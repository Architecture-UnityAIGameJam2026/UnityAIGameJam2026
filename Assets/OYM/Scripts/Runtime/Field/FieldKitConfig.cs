using UnityEngine;

namespace OYM.Field
{
    /// Tuning + models of the field kit in the game scene (Resources/OYM_FieldKit): the link relays that give the
    /// crossed visor HUD back a piece at a time, and the floating heal packs. FieldKit reads it when the scene starts;
    /// change the numbers in the inspector.
    [CreateAssetMenu(menuName = "OYM/Field Kit", fileName = "OYM_FieldKit")]
    public class FieldKitConfig : ScriptableObject
    {
        [Header("Models (Props/Prefabs)")]
        [Tooltip("the relay's terminal box (debris_junction_box)")] public GameObject relayBox;
        [Tooltip("the relay's mast, beacon lamp on top (debris_beacon_post)")] public GameObject relayMast;
        [Tooltip("the floating heal pack (pk_medkit_closed)")] public GameObject medkit;
        public float relayBoxScale = 1.6f, relayMastScale = 2.6f, medkitScale = 2.2f;

        [Header("Sounds (the team's AudioEffect slots, as picked in the SFX picker)")]
        [Tooltip("36_revive_progress_loop: while a pilot calibrates a relay")] public AudioClip calibrateLoop;
        [Tooltip("40_terminal_unlock: a relay done, part of the HUD comes back")] public AudioClip relayDone;
        [Tooltip("24_health_absorb: a heal pack taken")] public AudioClip healTaken;
        [Range(0f, 1f)] public float calibrateVolume = 0.55f, relayDoneVolume = 0.9f, healVolume = 0.6f;

        [Header("Link relays, in route order")]
        public RelayStep[] relays =
        {
            new RelayStep { at = 0.4f, modules = new[] { 1 }, title = "미니맵이 돌아왔다", body = "체력과 무기 상태는 아직 꼬여 있습니다" },
            new RelayStep { at = 0.67f, modules = new[] { 2, 3 }, title = "무기·스킬 표시가 돌아왔다", body = "체력은 아직 상대방 화면에 뜹니다" },
            new RelayStep { at = 0.93f, modules = new[] { 0 }, title = "HUD 링크 복구 완료", body = "이제 내 체력은 내 화면에 뜹니다" },
        };
        [Tooltip("a pilot's chest this close (m) to the relay's lamp calibrates it")] public float relayRadius = 6f;
        [Tooltip("seconds of calibration for one relay")] public float relayHold = 1.8f;
        [Tooltip("the first time a pilot comes this close (m) to a relay, a line says what it is for")] public float relayHintRange = 45f;

        [Header("Heal packs")]
        [Tooltip("HP one pack gives back (the pilot's max is HuskEncounterConfig.playerHP)")] public float healAmount = 60f;
        [Tooltip("seconds until a taken pack is back")] public float respawn = 20f;
        [Tooltip("a pilot's chest this close (m) takes it - only when hurt")] public float packRadius = 2.6f;
        [Tooltip("height of the pack over the stone (m)")] public float packHover = 1.3f;
        [Tooltip("stepping stones (PathStone_NN) that carry a heal pack")] public int[] packStones = { 1, 3, 5, 7, 9, 11, 13, 15 };
    }

    [System.Serializable]
    public class RelayStep
    {
        [Tooltip("where on the stepping-stone path: 0 = the first stone, 1 = the last")] [Range(0f, 1f)] public float at = 0.5f;
        [Tooltip("visor HUD modules it gives back: 0 vitals, 1 minimap, 2 reserves, 3 skills")] public int[] modules = { 0 };
        [Tooltip("notice title / line when they come back")] public string title, body;
    }
}
