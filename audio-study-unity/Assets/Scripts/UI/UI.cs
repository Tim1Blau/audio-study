using System;
using System.Collections;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class UI : SingletonBehaviour<UI>
{
    [SerializeField] public Text screenText;
    [SerializeField] public Text bottomText;
    [SerializeField] public Text scenarioText;
    [SerializeField] Slider breakProgress;
    [SerializeField] Slider confirmProgress;

    new void Awake()
    {
        base.Awake();
        scenarioText.text = bottomText.text = screenText.text = "";
    }

    void Start()
    {
        breakProgress.gameObject.SetActive(false);
        confirmProgress.gameObject.SetActive(false);
    }

    void Update()
    {
        Map.Singleton.playerPin.transform.SetPositionAndRotation(
            References.Player.transform.position.WithY(0.1f),
            Quaternion.Euler(90, References.Player.camera.transform.rotation.eulerAngles.y, 0)
        );
    }

    public static IEnumerator WaitForSeconds(float seconds)
    {
        if (seconds <= 0) yield break;
        var start = References.Now;
        var slider = Singleton.breakProgress;
        slider.gameObject.SetActive(true);
        slider.value = 0;
        while (References.Now - start < seconds)
        {
            slider.value = (References.Now - start) / seconds;
            yield return new WaitForNextFrameUnit();
        }

        slider.value = 1;
        yield return new WaitForNextFrameUnit();

        slider.gameObject.SetActive(false);
    }

    public static IEnumerator WaitForPrompt(string message)
    {
        var audioPaused = References.AudioPaused;
        var canMove = References.Player.canMove;
        References.AudioPaused = true;
        References.Player.canMove = false;
        /*------------------------------------------------*/
        yield return Singleton.Prompt(message);
        /*------------------------------------------------*/
        References.AudioPaused = audioPaused;
        References.Player.canMove = canMove;
    }

    IEnumerator Prompt(string message)
    {
        screenText.text = message;
        bottomText.text = new LocalText(
            $"[Hold {StudySettings.ConfirmKey} to continue]",
            $"[Halten Sie {StudySettings.ConfirmKey} um fortzufahren]");
        yield return WaitForKeyHold(StudySettings.ConfirmKey);
        bottomText.text = screenText.text = "";
    }

    public static IEnumerator WaitForKeyHold(KeyCode keyCode, Action onFinished = null)
    {
        var progress = Singleton.confirmProgress;
        yield return new WaitUntil(() => !Input.GetKey(keyCode));
        yield return new WaitUntil(() => Input.GetKey(keyCode));

        while (Application.isPlaying)
        {
            yield return new WaitUntil(() => Input.GetKeyDown(keyCode));
            progress.gameObject.SetActive(true);
            var pressStart = References.Now;
            while (Input.GetKey(keyCode) && References.Now - pressStart < StudySettings.PressToConfirmDuration)
            {
                yield return new WaitForNextFrameUnit();
                progress.value = (References.Now - pressStart) / StudySettings.PressToConfirmDuration;
            }

            progress.gameObject.SetActive(false);
            if (Input.GetKey(keyCode))
                break;
        }

        onFinished?.Invoke();
    }

    public static IEnumerator WaitForKeySelect(Action<KeyCode> onFinished, params KeyCode[] keyCodes)
    {
        var progress = UI.Singleton.confirmProgress;

        while (Application.isPlaying)
        {
            yield return new WaitUntil(() => !KeyPressed());
            yield return new WaitUntil(KeyPressed);
            progress.gameObject.SetActive(true);
            var pressStart = References.Now;
            while (KeyPressed() && References.Now - pressStart < StudySettings.PressToSelectSceneOrderDuration)
            {
                yield return new WaitForNextFrameUnit();
                progress.value = (References.Now - pressStart) / StudySettings.PressToSelectSceneOrderDuration;
            }

            progress.gameObject.SetActive(false);
            if (KeyPressed())
                break;
        }
        onFinished?.Invoke(keyCodes.First(Input.GetKey));

        yield break;
        bool KeyPressed() => keyCodes.Any(Input.GetKey);
    }
}