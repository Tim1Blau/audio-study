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
    public List<string> scenes = new()
    {
        "Room1", "Room2", "Room3"
    };

    public StudyData data = new();

    static bool _instantiated;

    public static void Initialize()
    {
        if (_instantiated) return;
        _instantiated = true;
        DontDestroyOnLoad(new GameObject(nameof(Study)).AddComponent<Study>());
    }

    void Update()
    {
        if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.P))
            JsonData.Export(data); // Backup Export
    }

    IEnumerator Start() => DoStudy();

    IEnumerator DoStudy()
    {
        yield return UI.WaitForPrompt("Welcome to the Study");

        if (scenes.Count == 0)
        {
            yield return DoScenario();
        }
        else
        {
            var index = 0;
            foreach (var scene in scenes)
            {
                SceneManager.LoadScene(scene);
                yield return new WaitForNextFrameUnit();
                yield return DoScenario();
                yield return UI.WaitForPrompt($"Finished scenario {++index}/{scenes.Count}");
            }
        }

        yield return UI.WaitForPrompt("Export Data?");
        JsonData.Export(data);
        UI.Singleton.screenText.text = "Finished the Study";
    }

    IEnumerator DoScenario()
    {
        var scenario = GenerateScenarioForCurrentScene();
        data.scenarios.Add(scenario);

        Setup(scenario.audioConfiguration = AudioConfiguration.Pathing);

        yield return UI.WaitForPrompt(
            "Task 1/2: Navigation\nHere you need to find audio sources as quickly as possible");
        yield return Navigation.DoTasks(scenario.navigationTasks);

        yield return UI.WaitForPrompt(
            "Task 2/2: Navigation\nHere you need to guess the position of the audio source without moving");
        yield return Localization.DoTasks(scenario.localizationTasks);
    }

    static void Setup(AudioConfiguration audioConfiguration)
    {
        var audio = References.Singleton.steamAudioSource;
        (audio.transmission, audio.pathing) = audioConfiguration switch
        {
            AudioConfiguration.Basic   => (transmission: true, pathing: false),
            AudioConfiguration.Pathing => (transmission: false, pathing: true),
            AudioConfiguration.Mixed   => (transmission: true, pathing: true),
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