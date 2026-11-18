using System.Collections.Generic;
using UnityEngine;

namespace OnYourMark
{
    /// <summary>적 스폰 위치. 어떤 적이 나오는 자리인지 EnemyData로 표시한다.</summary>
    public class Spawner : MonoBehaviour
    {
        [SerializeField] EnemyData enemy;

        public static List<Transform> For(EnemyData enemy)
        {
            var points = new List<Transform>();
            foreach (var s in FindObjectsByType<Spawner>(FindObjectsSortMode.InstanceID))
                if (s.enemy == enemy) points.Add(s.transform);
            return points;
        }
    }
}
