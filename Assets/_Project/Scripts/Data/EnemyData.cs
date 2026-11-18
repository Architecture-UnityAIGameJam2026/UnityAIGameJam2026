using Unity.Netcode;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>적 공통 데이터. 웨이브 구성과 스포너가 이 에셋으로 적 종류를 구분한다.</summary>
    public abstract class EnemyData : ScriptableObject
    {
        public NetworkObject prefab;
        public float moveSpeed = 5f;
        [Tooltip("공격 대상(플레이어)과 시야를 막는 지형 레이어")]
        public LayerMask targetMask;
    }
}
