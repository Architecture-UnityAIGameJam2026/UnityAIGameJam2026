using System.Collections.Generic;
using UnityEngine;

namespace OnYourMark.UI
{
    /// <summary>
    /// 네 모서리 HUD 묶음을 화면 가운데를 향해 3D로 기울인다 (헬멧 바이저처럼 바깥 모서리는 가깝고 안쪽은 멀다).
    /// Screen Space - Camera 캔버스의 원근을 그대로 쓴다.
    /// 그룹 계층은 바꾸지 않는다: OnYourMarkHud가 "Vitals/Health bar" 같은 경로로 찾기 때문에, 같은 모서리의 그룹들을
    /// 하나의 기울어진 판처럼 화면 모서리 축으로 함께 돌린다. 런타임 전용이라 프리팹 · 씬에는 평면 배치가 그대로 남는다.
    /// HudSwapGlitch · HudBreak 고스트는 모듈의 anchoredPosition3D · localRotation을 복사해 같은 기울기로 겹친다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HudPerspective : MonoBehaviour
    {
        [Tooltip("좌우 기울기(도): 안쪽 가장자리가 멀어지는 정도")]
        [SerializeField, Range(0f, 40f)] float yaw = 18f;
        [Tooltip("상하 기울기(도)")]
        [SerializeField, Range(0f, 30f)] float pitch = 9f;
        [Tooltip("중심이 화면 가운데 이 비율 안에 있는 그룹은 기울이지 않는다 (조준점 · 아군 표시)")]
        [SerializeField, Range(0f, 0.9f)] float centerZone = 0.45f;
        [Tooltip("이름으로 제외할 그룹")]
        [SerializeField] string[] flat = { "Reticle", "AllyMarker" };

        struct Entry { public RectTransform rt; public Vector3 pos; public Quaternion rot; public bool tilted; }

        readonly List<Entry> _entries = new List<Entry>(32);
        float _yaw = -1f, _pitch = -1f;
        bool _applied;

        public float Yaw { get => yaw; set => yaw = value; }
        public float Pitch { get => pitch; set => pitch = value; }

        void Start() => Apply();

        void OnDisable() => Restore();

        void LateUpdate()
        {
            // 인스펙터 조정을 바로 보여 준다 (값이 그대로면 아무것도 하지 않는다)
            if (_applied && (_yaw != yaw || _pitch != pitch)) Apply();
        }

        /// <summary>평면 배치에서 다시 계산한다. 런타임에 그룹을 더 만든 뒤(예: HudSkillPanel) 불러도 된다.</summary>
        public void Apply()
        {
            if (!Application.isPlaying) return;
            Restore();
            var root = (RectTransform)transform;
            Rect r = root.rect;
            if (r.width <= 0f || r.height <= 0f) return;

            for (int i = 0; i < root.childCount; i++)
            {
                if (!(root.GetChild(i) is RectTransform rt) || Known(rt) || rt.GetComponent<Canvas>()) continue;   // 중첩 캔버스 = 연출 레이어
                _entries.Add(new Entry { rt = rt, pos = rt.localPosition, rot = rt.localRotation });
            }

            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (!e.rt || IsFlat(e.rt.name)) continue;
                Vector3 c = root.InverseTransformPoint(e.rt.TransformPoint(e.rt.rect.center));
                float nx = (c.x - r.center.x) / (r.width * 0.5f), ny = (c.y - r.center.y) / (r.height * 0.5f);
                if (Mathf.Abs(nx) < centerZone && Mathf.Abs(ny) < centerZone) continue;
                bool left = nx < 0f, top = ny > 0f;
                // 화면 모서리를 축으로: 안쪽(가운데 쪽) 가장자리가 +Z(카메라에서 먼 쪽)로 들어간다
                var pivot = new Vector3(left ? r.xMin : r.xMax, top ? r.yMax : r.yMin, 0f);
                var q = Quaternion.Euler(top ? -pitch : pitch, left ? -yaw : yaw, 0f);
                e.rt.localPosition = pivot + q * (e.pos - pivot);
                e.rt.localRotation = q * e.rot;
                e.tilted = true;
                _entries[i] = e;
            }
            _yaw = yaw; _pitch = pitch;
            _applied = true;
        }

        /// <summary>평면 배치로 되돌린다.</summary>
        public void Restore()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (!e.rt || !e.tilted) continue;
                e.rt.localPosition = e.pos;
                e.rt.localRotation = e.rot;
                e.tilted = false;
                _entries[i] = e;
            }
            _applied = false;
        }

        bool Known(RectTransform rt)
        {
            for (int i = 0; i < _entries.Count; i++) if (_entries[i].rt == rt) return true;
            return false;
        }

        bool IsFlat(string n)
        {
            if (flat == null) return false;
            for (int i = 0; i < flat.Length; i++) if (flat[i] == n) return true;
            return false;
        }
    }
}
