using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SteamAudio;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;
using Vector3 = UnityEngine.Vector3;

public class Study : MonoBehaviour
{
    public List<UnityEngine.SceneManagement.Scene> scenes = new();

    public StudyData data = new()
    {
        navigationScenarios = new List<NavigationScenario>(),
        localizationScenarios = new List<LocalizationScenario>()
    };

    static bool _instantiated;
    static StudySettings Settings => StudySettings.Singleton;

    public static void Initialize()
    {
        if (_instantiated) return;
        _instantiated = true;
        var studyManager = new GameObject(nameof(Study)).AddComponent<Study>();
        DontDestroyOnLoad(studyManager);
    }

    void Update()
    {
        // Backup Export
        if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.P))
        {
            JsonData.Export(data);
        }
    }

    IEnumerator Start() => DoStudy();

    IEnumerator DoStudy()
    {
        yield return UI.WaitForPrompt("Welcome to the Study");

        if (scenes.Count == 0) scenes.Add(SceneManager.GetActiveScene());
        foreach (var scene in scenes)
        {
            SceneManager.LoadScene(scene.name);
            var (navigation, localization) = CreateScenariosForCurrentScene();
            data.navigationScenarios.Add(navigation);
            data.localizationScenarios.Add(localization);

            navigation.audioConfiguration = localization.audioConfiguration = AudioConfiguration.Pathing; // make random

            yield return new WaitForNextFrameUnit();
            Setup(navigation.audioConfiguration);
            yield return UI.WaitForPrompt(
                "Task 1/2: Navigation\nHere you need to find audio sources as quickly as possible");
            yield return Navigation.DoScenario(navigation);

            yield return UI.WaitForPrompt(
                "Task 2/2: Navigation\nHere you need to guess the position of the audio source without moving");
            yield return Localization.DoScenario(localization);
        }

        yield return UI.WaitForPrompt("Export Data?");
        JsonData.Export(data);
    }

    void Setup(AudioConfiguration audioConfiguration)
    {
        switch (audioConfiguration)
        {
            case AudioConfiguration.Basic:
                break;
            case AudioConfiguration.Pathing:
                break;
            case AudioConfiguration.Mixed:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    static (NavigationScenario, LocalizationScenario) CreateScenariosForCurrentScene()
    {
        var scene = SceneManager.GetActiveScene();
        Random.InitState(Settings.seed);
        var positions = References.ProbeBatch.ProbeSpheres.Select(p => p.center)
            .Select(x => Common.ConvertVector(x).XZ()).ToArray();

        return (
            new NavigationScenario
            {
                scene = new Scene { name = scene.name },
                tasks = Pairwise(RandomAudioPositions(Settings.navPositions + 1, Settings.navAudioDistances).ToList())
                    .Select(x =>
                        new NavigationScenario.Task
                        {
                            startTime = -1,
                            endTime = -1,
                            listenerStartPosition = x.Item1,
                            audioPosition = x.Item2,
                            metrics = new()
                        }
                    ).ToList()
            },
            new LocalizationScenario
            {
                scene = new Scene { name = scene.name },
                tasks = RandomAudioPositions(Settings.locListenerPositions, Settings.locListenerDistances)
                    .SelectMany(listenerPosition =>
                        Enumerable.Range(0, Settings.locSourcePositionsPerListenerPos)
                            .Select(_ => RandomPosWithMinDistance(listenerPosition, Settings.locAudioDistances))
                            .Select(audioPos =>
                                new LocalizationScenario.Task
                                {
                                    startTime = -1,
                                    endTime = -1,
                                    listenerPosition = listenerPosition,
                                    audioPosition = audioPos,
                                    guessedPosition = Vector2.negativeInfinity,
                                    audioPath = new List<Vector2>(),
                                }
                            )
                    ).ToList()
            }
        );

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