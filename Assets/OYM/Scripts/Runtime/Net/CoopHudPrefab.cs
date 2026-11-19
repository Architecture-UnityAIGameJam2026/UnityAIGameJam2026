using UnityEngine;

namespace OYM.Net
{
    /// Team repo port: points at the team's SenseLink HUD prefab (Assets/garam/UI/Prefabs - not a Resources folder in
    /// the team layout, unlike the local art project) so CoopHud can load it in a build too.
    /// Asset: Resources/OYM_CoopHudPrefab.
    public sealed class CoopHudPrefab : ScriptableObject
    {
        public GameObject hud;
    }
}
