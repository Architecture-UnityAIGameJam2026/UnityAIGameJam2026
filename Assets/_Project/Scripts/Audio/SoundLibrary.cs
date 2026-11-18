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
            [Tooltip("있으면 한 번 소리(Play)마다 clip과 이 목록 중 하나를 무작위로 고른다 (같은 소리 연속은 피함). 반복 소리(CreateLoop)는 clip만 쓴다")]
            public AudioClip[] variations = Array.Empty<AudioClip>();
            [Range(0, 1)] public float volume = 0.8f;
            [Tooltip("켜면 위치에서 나는 3D 소리, 끄면 화면 소리(UI · 내 상태)")]
            public bool spatial = true;
            public float minDistance = 3f, maxDistance = 60f;
            [Tooltip("재생마다 음높이를 이만큼 흔든다 (반복 소리가 덜 단조롭게)")]
            [Range(0, 0.3f)] public float pitchJitter = 0.05f;
            [Tooltip("같은 소리를 다시 낼 수 있는 최소 간격 (연사 · 연타 과다 재생 방지)")]
            public float minInterval;

            int lastPick;   // 직전에 고른 번호 + 1 (0 = 아직 없음), 저장하지 않는다

            /// <summary>이번에 낼 클립: clip과 variations 중 하나. 변형이 있으면 직전과 같은 것은 피한다.</summary>
            public AudioClip PickClip()
            {
                int count = 1 + (variations != null ? variations.Length : 0);
                if (count == 1) return clip;
                int last = lastPick - 1;
                int i = UnityEngine.Random.Range(0, last >= 0 ? count - 1 : count);
                if (last >= 0 && i >= last) i++;
                lastPick = i + 1;
                var picked = i == 0 ? clip : variations[i - 1];
                return picked ? picked : clip;
            }
        }

        public Entry[] entries = Array.Empty<Entry>();

        public Entry Get(SoundId id) => Array.Find(entries, e => e.id == id);
    }
}
