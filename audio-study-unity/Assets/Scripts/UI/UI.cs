using System;
using System.Collections;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class UI : SingletonBehaviour<UI>
{
    [SerializeField] public Text screenText;
    [SerializeField] public Text bottomText;
    [SerializeField] Slider breakProgress;
    [SerializeField] Slider confirmProgress;
    
    new void Awake()
    {
        base.Awake();
        bottomText.text = screenText.text = "";
    }

    void Start()
    {
        breakProgress.gameObject.SetActive(false);
        confirmProgress.gameObject.SetActive(false);
    }

    public static IEnumerator TakeABreak(float seconds)
    {
        if (seconds <= 0) yield break;
        var start = References.Now;
        var slider = Singleton.breakProgress;
        References.Paused = true;
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
        References.Paused = false;
    }

    public static IEnumerator WaitForPrompt(string message)
    {
        References.Paused = true;
        /*------------------------------------------------*/
        yield return UI.Singleton.Prompt(message);
        /*------------------------------------------------*/
        References.Paused = false;
    }

    IEnumerator Prompt(string message)
    {
        screenText.text = message;
        bottomText.text = $"[Hold {StudySettings.Singleton.confirmKey} to continue]";
        yield return WaitForKeyHold(StudySettings.Singleton.confirmKey);
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
            while (Input.GetKey(keyCode) && References.Now - pressStart < StudySettings.Singleton.pressToConfirmSeconds)
            {
                yield return new WaitForNextFrameUnit();
                progress.value = (References.Now - pressStart) / StudySettings.Singleton.pressToConfirmSeconds;
            }
            
            progress.gameObject.SetActive(false);
            if (Input.GetKey(keyCode))
                break;
        }
        onFinished?.Invoke();
    }
}