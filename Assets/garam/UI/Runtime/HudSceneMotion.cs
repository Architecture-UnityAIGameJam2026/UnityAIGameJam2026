using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace OnYourMark.UI
{
    public sealed class HudSceneMotion : MonoBehaviour
    {
        public OnYourMarkHud hud;
        public Transform reactor;
        public Volume volume;
        Vignette vignette;
        ChromaticAberration aberration;
        Vector3 cameraOrigin;
        void Start(){cameraOrigin=transform.localPosition;if(volume){volume.profile.TryGet(out vignette);volume.profile.TryGet(out aberration);}}
        void Update()
        {
            bool still=hud&&hud.reducedMotion;
            if(reactor&&!still)reactor.Rotate(0,0,7*Time.unscaledDeltaTime,Space.Self);
            transform.localPosition=cameraOrigin+(still?Vector3.zero:new Vector3(Mathf.Sin(Time.unscaledTime*.23f)*.018f,Mathf.Sin(Time.unscaledTime*.36f)*.012f,0));
            if(vignette!=null){bool critical=hud&&hud.state==OnYourMarkHud.PreviewState.Critical;vignette.intensity.value=critical?.43f:.28f;vignette.color.value=critical?new Color(.2f,.012f,.012f):new Color(.005f,.02f,.022f);}
            if(aberration!=null)aberration.intensity.value=0;
        }
    }
}
