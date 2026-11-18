using UnityEngine;

namespace OnYourMark
{
    /// <summary>역할별 스폰 위치. 씬에 놓인 SpawnPoint 중 같은 RoleDefinition을 가진 것을 찾는다.</summary>
    public class SpawnPoint : MonoBehaviour
    {
        [SerializeField] RoleDefinition role;

        public static Transform For(RoleDefinition role)
        {
            foreach (var point in FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None))
                if (point.role == role) return point.transform;
            Debug.LogError($"{role.name}에 해당하는 SpawnPoint가 씬에 없습니다.");
            return null;
        }
    }
}
