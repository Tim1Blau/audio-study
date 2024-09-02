using System.Collections;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class UI : SingletonBehaviour<UI>
{
    [SerializeField] public Text screenText;
    [SerializeField] public Text bottomText;
    [SerializeField] Slider progressSlider;

    [SerializeField] float pressToConfirmSeconds = 0.5f;

    new void Awake()
    {
        base.Awake();
        bottomText.text = screenText.text = "";
    }

    void Start()
    {
        progressSlider.gameObject.SetActive(false);
    }

    public static IEnumerator TakeABreak(float seconds)
    {
        if (seconds <= 0) yield break;
        var start = References.Now;
        var slider = Singleton.progressSlider;
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
        bottomText.text = "[Hold space to continue]";
        yield return WaitForKeyHold(KeyCode.Space, Application.isEditor ? KeyCode.X : KeyCode.None);
        bottomText.text = screenText.text = "";
    }

    public static IEnumerator WaitForKeyHold(KeyCode keyCode, KeyCode skip = KeyCode.None)
    {
        while (Application.isPlaying)
        {
            yield return new WaitUntil(() => Input.GetKeyDown(keyCode) || Input.GetKey(skip));
            if (Input.GetKey(skip))
                break;
            var pressStart = References.Now;
            yield return new WaitUntil(() =>
                !Input.GetKey(keyCode) || References.Now - pressStart > Singleton.pressToConfirmSeconds);
            if (Input.GetKey(keyCode))
                break;
        }
    }
}