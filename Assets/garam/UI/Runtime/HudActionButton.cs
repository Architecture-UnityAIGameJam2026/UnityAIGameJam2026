using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace OnYourMark.UI
{
    public sealed class HudActionButton : Selectable, IPointerClickHandler, ISubmitHandler
    {
        public OnYourMarkHud hud;
        public string action;
        public Graphic glow;
        public Color hoverColor;
        float emphasis;
        protected override void Awake(){base.Awake();transition=Transition.None;}
        public void OnPointerClick(PointerEventData e){if(e.button==PointerEventData.InputButton.Left)Activate();}
        public void OnSubmit(BaseEventData e)=>Activate();
        void Activate(){if(IsActive()&&IsInteractable()&&hud)hud.ActivateAction(action);}
        void Update()
        {
            bool active=IsInteractable()&&(currentSelectionState==SelectionState.Highlighted||currentSelectionState==SelectionState.Selected||currentSelectionState==SelectionState.Pressed);
            float target=active?1:0;
            emphasis=hud&&hud.reducedMotion?target:Mathf.MoveTowards(emphasis,target,Time.unscaledDeltaTime*9);
            if(targetGraphic){var c=hoverColor;c.a=Mathf.Lerp(0,.15f,emphasis);targetGraphic.color=c;}
            if(glow){var c=glow.color;c.a=Mathf.Lerp(.4f,1,emphasis);glow.color=c;}
        }
    }
}
