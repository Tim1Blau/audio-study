using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;

/// Persistent Singleton spawned by StudySettings.Start() 
public class Study : MonoBehaviour
{
    public StudyData data = new();

    const string TutorialScene = "Tutorial";

    static bool _instantiated;

    string _exportPath;

    public static void Initialize()
    {
        if (_instantiated) return;
        _instantiated = true;
        DontDestroyOnLoad(new GameObject(nameof(Study)).AddComponent<Study>());
    }

    private Vector2 position;

    void Update()
    {
        Debug.Log($"Speed: {Vector2.Distance(position, References.PlayerPosition) /Time.deltaTime }m/s");
        
        position = References.PlayerPosition;
        if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.P))
            Export("Backup");

#if UNITY_EDITOR
        if (Input.GetKeyDown(KeyCode.Keypad0)) AudioConfig = AudioConfiguration.Basic;
        if (Input.GetKeyDown(KeyCode.Keypad1)) AudioConfig = AudioConfiguration.Pathing;
        if (Input.GetKeyDown(KeyCode.Keypad2)) AudioConfig = AudioConfiguration.Mixed;
#endif
    }

    void Export(string stage) => JsonData.Export(data, _exportPath + " " + stage);

    IEnumerator Start()
    {
        return DoStudy();
    }

    IEnumerator DoStudy()
    {
        References.AudioPaused = true;
        References.Player.canMove = false;
        /*------------------------------------------------*/
        var orderIndex = -1;
        var sceneOrder = default(SceneOrder[]);
        yield return SceneOrder.WaitForChoice(
            new LocalText(
                "Welcome to the Study!\nWait for instructions to proceed",
                "Willkommen zur Studie!\nWarten sie auf Anweisungen um fortzufahren"
            ),
            (s, i) =>
            {
                sceneOrder = s;
                orderIndex = i;
            });
        /*------------------------------------------------*/

        _exportPath = "StudyData"
                      + (Application.isEditor ? "_Editor" : "")
                      + $" Order{orderIndex}"
                      + DateTime.Now.ToString(" (dd.MM.yyyy-HH.mm)");
        /*------------------------------------------------*/
        yield return DoTutorial();
        /*------------------------------------------------*/
        var scenarioNumber = 1;
        foreach (var (scene, audioConfig) in sceneOrder)
        {
            /*------------------------------------------------*/
            yield return DoScenario(scene, audioConfig,
                new LocalText(
                    $"Scenario {scenarioNumber}/{sceneOrder.Length}",
                    $"Szenario {scenarioNumber}/{sceneOrder.Length}"));
            /*------------------------------------------------*/
            ++scenarioNumber;
        }

        Export("Final");
        UI.Singleton.screenText.text = new LocalText("Completed the study!", "Studie abgeschlossen!");
        UI.Singleton.bottomText.text = "";
        References.AudioPaused = true;
    }

    IEnumerator DoTutorial()
    {
        /*------------------------------------------------*/
        yield return DoScenario(TutorialScene, AudioConfiguration.Basic, "Tutorial");
        /*------------------------------------------------*/
        data.scenarios.Clear();
        /*------------------------------------------------*/
        yield return UI.WaitForPrompt(new LocalText(
            "Completed the tutorial!\nNow the study can begin.",
            "Tutorial abgeschlossen!\nJetzt kann die Studie beginnen."));
        /*------------------------------------------------*/
    }

    IEnumerator DoScenario(string scene, AudioConfiguration audioConfiguration, string title)
    {
        if (SceneManager.GetActiveScene().name != scene)
        {
            SceneManager.LoadScene(scene);
            /*------------------------------------------------*/
            yield return new WaitForNextFrameUnit();
            /*------------------------------------------------*/
        }

        References.AudioPaused = true;

        UI.Singleton.scenarioText.text = $"{scene}\nAudio config: {(int)audioConfiguration}";

        var scenario = StudySettings.Singleton.GenerateScenario();
        data.scenarios.Add(scenario);
        if (scenario.navigationTasks.Count + scenario.localizationTasks.Count == 0) yield break;

        AudioConfig = scenario.audioConfiguration = audioConfiguration;
        /*------------------------------------------------*/
        yield return UI.WaitForPrompt(
            title
            + "\n" + new LocalText(
                "Info: The environment and audio configuration have changed",
                "Info: Die Umgebung und Audio Konfiguration wurden geändert"));
        /*------------------------------------------------*/
        yield return Navigation.DoTasks(scenario.navigationTasks);
        yield return Localization.DoTasks(scenario.localizationTasks);
        /*------------------------------------------------*/
        Export($"{scene}-{(int)audioConfiguration}");
    }

    public static AudioConfiguration AudioConfig
    {
        get
        {
            var audio = References.Singleton.steamAudioSource;
            return (audio.transmission, audio.pathingMixLevel) switch
            {
                (true, 0)  => AudioConfiguration.Basic,
                (false, 1) => AudioConfiguration.Pathing,
                (true, 1)  => AudioConfiguration.Mixed,
                _          => AudioConfiguration.Basic,
            };
        }
        set
        {
            if (value == AudioConfig) return;
            var audio = References.Singleton.steamAudioSource;
            (audio.transmission, audio.transmissionLow, audio.pathingMixLevel) = value switch
            {
                AudioConfiguration.Basic   => (transmission: true, transmissionLow: 1, pathing: 0),
                AudioConfiguration.Pathing => (transmission: false, transmissionLow: 0, pathing: 1),
                AudioConfiguration.Mixed   => (transmission: true, transmissionLow: 0.4f, pathing: 1),
                _                          => throw new ArgumentOutOfRangeException()
            };
        }
    }
}


public record SceneOrder(string Scene, AudioConfiguration AudioConfiguration)
{
    public static IEnumerator WaitForChoice(string instructions, Action<SceneOrder[], int> chosenOrder)
    {
        var ui = UI.Singleton;
        ui.screenText.text = instructions;
        yield return UI.WaitForKeySelect(
            key => chosenOrder.Invoke(PossibleSceneOrders[Keys[key]], Keys[key]),
            Keys.Keys.ToArray());
        ui.bottomText.text = ui.screenText.text = "";
    }

    static readonly SceneOrder[][] PossibleSceneOrders =
    {
        new SceneOrder[]
        {
            new("Room1", AudioConfiguration.Basic),
            new("Room2", AudioConfiguration.Pathing),
            new("Room3", AudioConfiguration.Mixed)
        },
        new SceneOrder[]
        {
            new("Room1", AudioConfiguration.Basic),
            new("Room2", AudioConfiguration.Mixed),
            new("Room3", AudioConfiguration.Pathing)
        },
        new SceneOrder[]
        {
            new("Room1", AudioConfiguration.Pathing),
            new("Room2", AudioConfiguration.Basic),
            new("Room3", AudioConfiguration.Mixed)
        },
        new SceneOrder[]
        {
            new("Room1", AudioConfiguration.Pathing),
            new("Room2", AudioConfiguration.Mixed),
            new("Room3", AudioConfiguration.Basic)
        },
        new SceneOrder[]
        {
            new("Room1", AudioConfiguration.Mixed),
            new("Room2", AudioConfiguration.Basic),
            new("Room3", AudioConfiguration.Pathing)
        },
        new SceneOrder[]
        {
            new("Room1", AudioConfiguration.Mixed),
            new("Room2", AudioConfiguration.Pathing),
            new("Room3", AudioConfiguration.Basic)
        },
    };

    static readonly IReadOnlyDictionary<KeyCode, int> Keys = new Dictionary<KeyCode, int>
    {
        [KeyCode.Alpha1] = 0,
        [KeyCode.Alpha2] = 1,
        [KeyCode.Alpha3] = 2,
        [KeyCode.Alpha4] = 3,
        [KeyCode.Alpha5] = 4,
        [KeyCode.Alpha6] = 5,
    };
}