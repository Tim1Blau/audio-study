using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class UI : SingletonBehaviour<UI>
{
    [SerializeField] Text screenText;
    [SerializeField] Text bottomText;

    [SerializeField] float pressToConfirmSeconds = 0.5f;

    void Start()
    {
        bottomText.text = screenText.text = "";
    }
    
    public string SideText
    {
        get => bottomText.text;
        set => bottomText.text = value;
    }

    public IEnumerator Prompt(string message)
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