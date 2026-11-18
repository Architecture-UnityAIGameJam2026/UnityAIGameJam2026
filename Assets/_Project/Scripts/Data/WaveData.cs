using System;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>웨이브 하나의 적 구성.</summary>
    [CreateAssetMenu(menuName = "On Your Mark/Wave")]
    public class WaveData : ScriptableObject
    {
        [Serializable]
        public struct Group
        {
            public EnemyData enemy;
            public int count;
        }

        public Group[] groups;
    }
}
