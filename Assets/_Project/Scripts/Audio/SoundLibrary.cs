using System;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>
    /// 효과음 데이터: 종류마다 클립 · 볼륨 · 3D 여부 · 들리는 거리 · 음높이 흔들림.
    /// Resources/SoundLibrary.asset 하나를 Sfx가 읽는다. 값은 인스펙터에서 조정한다.
    /// </summary>
    [CreateAssetMenu(menuName = "On Your Mark/Sound Library")]
    public class SoundLibrary : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public SoundId id;
            public AudioClip clip;
            [Range(0, 1)] public float volume = 0.8f;
            [Tooltip("켜면 위치에서 나는 3D 소리, 끄면 화면 소리(UI · 내 상태)")]
            public bool spatial = true;
            public float minDistance = 3f, maxDistance = 60f;
            [Tooltip("재생마다 음높이를 이만큼 흔든다 (반복 소리가 덜 단조롭게)")]
            [Range(0, 0.3f)] public float pitchJitter = 0.05f;
            [Tooltip("같은 소리를 다시 낼 수 있는 최소 간격 (연사 · 연타 과다 재생 방지)")]
            public float minInterval;
        }

        public Entry[] entries = Array.Empty<Entry>();

        public Entry Get(SoundId id) => Array.Find(entries, e => e.id == id);
    }
}
