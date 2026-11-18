using System;
using UnityEngine;

namespace OnYourMark
{
    // The existing voice pack stays at its original GUIDs. This manifest also includes the clips in player builds.
    public sealed class RadioAssets : ScriptableObject
    {
        public TextAsset lines, actions;
        public AudioClip[] clips;
    }
}
