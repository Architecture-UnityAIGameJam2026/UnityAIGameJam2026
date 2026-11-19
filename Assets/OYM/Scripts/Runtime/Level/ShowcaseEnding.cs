using System.Collections;
using OYM.Net;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace OYM
{
    // Instantiated from the result canvas of the original arena, without its network game manager.
    public sealed class ShowcaseEnding : MonoBehaviour
    {
        [SerializeField] GameObject panel;
        [SerializeField] TMP_Text title, detail, returnLabel;
        [SerializeField] Button returnButton;
        AudioClip suitLoop;

        public static void Begin(HuskEncounter encounter)
        {
            var layout = HuskZones.Current;
            if (!layout || !layout.ending || encounter.GetComponentInChildren<ShowcaseEnding>(true)) return;
            var view = Instantiate(layout.ending, encounter.transform, false);
            view.StartCoroutine(view.Present(encounter));
        }

        IEnumerator Present(HuskEncounter encounter)
        {
            BgmPlayer.FadeOut(.4f);
            var audio = gameObject.AddComponent<AudioSource>();
            suitLoop = OnYourMark.WaveManager.CreateEndingAudio();
            audio.clip = suitLoop;
            audio.loop = true;
            audio.volume = .32f;
            audio.Play();
            yield return new WaitForSecondsRealtime(3f);

            if (encounter.director)
            {
                encounter.director.enabled = false;
                foreach (var pilot in encounter.director.controllers) if (pilot) pilot.enabled = false;
            }
            if (encounter.FlightCam) encounter.FlightCam.enabled = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            var game = CoopGame.Current;
            bool canReturn = !game || game.IsHost;
            panel.SetActive(true);
            title.text = "MISSION CLEAR";
            title.color = new Color(.5f, .95f, .6f);
            detail.text = "Upper deck secured";
            returnLabel.text = canReturn ? "RETURN TO TITLE" : "WAITING FOR HOST";
            returnButton.interactable = canReturn;
            returnButton.onClick = new Button.ButtonClickedEvent();
            if (!canReturn) yield break;
            returnButton.onClick.AddListener(() =>
            {
                returnButton.interactable = false;
                if (game) game.ReturnTeamToTitle(); else GameSession.ReturnToTitle();
            });
            if (!EventSystem.current)
            {
                var events = new GameObject("Result input", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
            }
            EventSystem.current.SetSelectedGameObject(returnButton.gameObject);
        }

        void OnDestroy() { if (suitLoop) Destroy(suitLoop); }
    }
}
