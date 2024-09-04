using System;
using System.Collections;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;

/// Persistent Singleton spawned by StudySettings.Start() 
public class Study : MonoBehaviour
{
    public StudyData data = new();

    const string TutorialScene = "Tutorial";

    readonly string[] _scenes =
    {
        "Room2", "Room3", "Room1",
    };

    readonly AudioConfiguration[] _audioConfigurations =
    {
        AudioConfiguration.Basic,
        AudioConfiguration.Pathing,
        AudioConfiguration.Mixed
    };


    static bool _instantiated;

    string _exportPath;

    public static void Initialize()
    {
        if (_instantiated) return;
        _instantiated = true;
        DontDestroyOnLoad(new GameObject(nameof(Study)).AddComponent<Study>());
    }

    void Update()
    {
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
        _audioConfigurations.Shuffle(); // observe random
        _exportPath = "StudyData"
                      + (Application.isEditor ? "_Editor" : "")
                      + DateTime.Now.ToString(" (dd.MM.yyyy-HH.mm)");
        return DoStudy();
    }

    IEnumerator DoStudy()
    {
        References.AudioPaused = true;
        References.Player.canMove = false;
        /*------------------------------------------------*/
        yield return UI.WaitForPrompt("Welcome to the Study");
        /*------------------------------------------------*/
        // yield return DoTutorial();
        /*------------------------------------------------*/
        foreach (var (scene, audioConfig) in _scenes.Zip(_audioConfigurations, (s, a) => (s, a)))
        {
            /*------------------------------------------------*/
            yield return DoScenario(scene, audioConfig);
            /*------------------------------------------------*/
        }

        Export("Final");
        UI.Singleton.screenText.text = "Completed the study!";
    }

    IEnumerator DoTutorial()
    {
        /*------------------------------------------------*/
        yield return DoScenario(TutorialScene, AudioConfiguration.Basic);
        /*------------------------------------------------*/
        data.scenarios.Clear();
        /*------------------------------------------------*/
        yield return UI.WaitForPrompt("Completed the tutorial!\nNow the study can begin.");
        /*------------------------------------------------*/
    }

    IEnumerator DoScenario(string scene, AudioConfiguration audioConfiguration)
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
        yield return UI.WaitForPrompt($"{scene}\nAudio config: {(int)audioConfiguration}");
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