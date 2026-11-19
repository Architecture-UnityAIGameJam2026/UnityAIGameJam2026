using System.Collections.Generic;
using UnityEngine;

namespace OYM
{
    /// Remembers the renderers EnemyShatter hid; gives them back when the enemy goes back to its pool.
    [AddComponentMenu("")]
    public class ShatterHidden : MonoBehaviour
    {
        public readonly List<Renderer> hidden = new List<Renderer>();
        void OnDisable() => RestoreNow();
        public void RestoreNow()
        {
            foreach (var r in hidden) if (r) r.enabled = true;
            hidden.Clear();
        }
    }
}
