using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SteamAudio;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

/// Persistent Singleton spawned by StudySettings.Start() 
public class Study : MonoBehaviour
{
    public StudyData data = new();

    const string TutorialScene = "Tutorial";

    readonly string[] _scenes =
    {
        "Room1",
        "Room2",
        "Room3"
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
        yield return UI.WaitForPrompt("Welcome to the Study");
        yield return UI.WaitForPrompt("Next: Tutorial");
        References.Paused = true;

        yield return DoScenario(TutorialScene, AudioConfiguration.Basic);
        Export("Tutorial");
        data.scenarios.Clear();
        yield return UI.WaitForPrompt("Completed the tutorial!\nNow the study can begin.");

        var index = 0;
        foreach (var (scene, audioConfig) in _scenes.Zip(_audioConfigurations, (s, a) => (s, a)))
        {
            yield return DoScenario(scene, audioConfig);

            ++index;
            Export($"Scenario {index}");
            yield return UI.WaitForPrompt($"Finished scenario {index}/{_scenes.Length}");
        }

        Export("Final");
        UI.Singleton.screenText.text = "Completed the study!";
    }

    IEnumerator DoScenario(string scene, AudioConfiguration audioConfiguration)
    {
        if (SceneManager.GetActiveScene().name != scene)
        {
            SceneManager.LoadScene(scene);
            yield return new WaitForNextFrameUnit();
        }

        var scenario = GenerateScenarioForCurrentScene();
        data.scenarios.Add(scenario);

        Setup(scenario.audioConfiguration = audioConfiguration);

        yield return Navigation.DoTasks(scenario.navigationTasks);
        yield return Localization.DoTasks(scenario.localizationTasks);
    }

    static void Setup(AudioConfiguration audioConfiguration)
    {
        var audio = References.Singleton.steamAudioSource;
        (audio.transmission, audio.pathingMixLevel) = audioConfiguration switch
        {
            AudioConfiguration.Basic   => (transmission: true, pathing: 0),
            AudioConfiguration.Pathing => (transmission: false, pathing: 1),
            AudioConfiguration.Mixed   => (transmission: true, pathing: 1),
            _                          => throw new ArgumentOutOfRangeException()
        };
    }

    static Scenario GenerateScenarioForCurrentScene()
    {
        var settings = StudySettings.Singleton;
        var scene = SceneManager.GetActiveScene();
        Random.InitState(settings.seed);
        var positions = References.Singleton.probeBatch.ProbeSpheres.Select(p => p.center)
            .Select(x => Common.ConvertVector(x).XZ()).ToArray();

        return new Scenario
        {
            scene = scene.name,
            navigationTasks =
                Pairwise(RandomAudioPositions(settings.navPositions + 1, settings.navAudioDistances).ToList())
                    .Select(fromTo =>
                        new NavigationTask
                        {
                            listenerStartPosition = fromTo.Item1,
                            audioPosition = fromTo.Item2,
                        }
                    ).ToList(),
            localizationTasks =
                RandomAudioPositions(settings.locListenerPositions, settings.locListenerDistances)
                    .SelectMany(listenerPosition =>
                        Enumerable.Range(0, settings.locSourcePositionsPerListenerPos)
                            .Select(_ => RandomPosWithMinDistance(listenerPosition, settings.locAudioDistances))
                            .Select(audioPos =>
                                new LocalizationTask
                                {
                                    listenerPosition = listenerPosition,
                                    audioPosition = audioPos,
                                }
                            )
                    ).ToList()
        };

        IEnumerable<Vector2> RandomAudioPositions(int count, float minDistance)
        {
            var start = new Vector2(-100, -100);
            for (var i = 0; i < count; i++)
                yield return start = RandomPosWithMinDistance(start, minDistance);
        }

        Vector2 RandomPosWithMinDistance(Vector2 from, float minDistance)
        {
            var distanceFiltered = positions.Where(v => Vector2.Distance(from, v) > minDistance)
                .ToArray();
            if (distanceFiltered.Length != 0) return RandomIndex(distanceFiltered);
            Debug.LogWarning(
                $"No available positions further than the min distance {minDistance}m away from the listener");
            return RandomIndex(positions);
        }

        static IEnumerable<(T, T)> Pairwise<T>(IReadOnlyCollection<T> input) =>
            input.Zip(input.Skip(1), (a, b) => (a, b));

        static Vector2 RandomIndex(Vector2[] l) => l[Random.Range(0, l.Length - 1)]; // Note: ignore empty case
    }
}