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
        [Tooltip("적을 한 마리씩 내보내는 간격(초). 한꺼번에 몰려 한 명이 순식간에 쓰러지는 것을 막는다")]
        public float spawnInterval = 0.6f;
    }
}
