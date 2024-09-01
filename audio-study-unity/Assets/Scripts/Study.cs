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

    static void Message(string message) => Debug.Log("[STUDY] " + message);

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
        yield return WaitForPrompt("Welcome to the Study");

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
            yield return WaitForPrompt(
                "Task 1/2: Navigation\nHere you need to find audio sources as quickly as possible");
            yield return DoNavigationScenario(navigation);

            yield return WaitForPrompt(
                "Task 2/2: Navigation\nHere you need to guess the position of the audio source without moving");
            yield return DoLocalizationScenario(localization);
        }

        yield return WaitForPrompt("Export Data?");
        JsonData.Export(data);
    }


    #region Localization

    IEnumerator DoLocalizationScenario(LocalizationScenario scenario)
    {
        var map = LocalizationMap.Singleton;
        map.enabled = false;

        var index = 0;
        foreach (var task in scenario.tasks)
        {
            ++index;
            References.AudioPosition = task.audioPosition.XZ(y: Settings.spawnHeight);
            References.ListenerPosition = task.listenerPosition.XZ(y: 0);
            /*------------------------------------------------*/
            yield return WaitForPrompt($"Next: Localize audio source {index}/{scenario.tasks.Count} on the map");
            /*------------------------------------------------*/
            map.enabled = true;
            map.IsFocused = false;

            UI.Singleton.screenText.text = $"Localize the audio source on the map";

            task.startTime = References.Now;
            /*------------------------------------------------*/
            Vector3 localizedPosition = default;
            yield return WaitForSoundLocalized(res => localizedPosition = res);
            /*------------------------------------------------*/
            /*------------------------------------------------*/
            List<Vector2> audioPath = default;
            yield return PathingRecorder.WaitForPathingData(res => audioPath = res);
            /*------------------------------------------------*/
            map.mapPin.color = Color.clear;
            map.enabled = false;

            task.endTime = References.Now;
            task.guessedPosition = localizedPosition.XZ();
            task.audioPath = audioPath;

#if UNITY_EDITOR
            Message($"Localized Position {localizedPosition}");
            Message($"Audio Position {task.audioPosition}");
            Message($"Distance to source {Vector2.Distance(task.guessedPosition, task.audioPosition)}");
#endif
        }
    }

    IEnumerator WaitForSoundLocalized(Action<Vector3> result)
    {
        var map = LocalizationMap.Singleton;

        Vector3? chosenPosition = null;

        var checkMapKey = StartCoroutine(CheckMapKeyLoop());
        var choseLocation = StartCoroutine(MapInteractionLoop());
        while (chosenPosition is null || !map.IsFocused)
        {
            /*------------------------------------------------*/
            yield return UI.WaitForKeyHold(KeyCode.Space);
            /*------------------------------------------------*/
        }

        StopCoroutine(checkMapKey);
        StopCoroutine(choseLocation);
        result.Invoke(chosenPosition.Value);
        yield break;

        IEnumerator MapInteractionLoop()
        {
            while (Application.isPlaying)
            {
                if (!map.IsFocused && chosenPosition is null)
                    map.mapPin.color = Color.clear;
                UI.Singleton.bottomText.text = map.IsFocused
                    ? chosenPosition is null
                        ? "Click where you think the audio source is."
                        : "\nHold [Space] to confirm your guess"
                    : $"\nPress [{Settings.mapKey}] to open the map";
                /*------------------------------------------------*/
                yield return new WaitUntil(() => map.IsFocused);
                yield return new WaitForNextFrameUnit();
                /*------------------------------------------------*/
                if (map.PointerToWorldPosition() is not { } location) continue;

                if (Input.GetMouseButton((int)MouseButton.Left))
                {
                    chosenPosition = location;
                    map.mapPin.transform.position = location;
                    map.mapPin.color = Color.black;
                }
                else if (chosenPosition is null)
                {
                    map.mapPin.transform.position = location;
                    map.mapPin.color = Color.grey;
                }
                else
                {
                    map.mapPin.color = Color.red;
                }
            }
        }

        IEnumerator CheckMapKeyLoop()
        {
            while (Application.isPlaying)
            {
                // wait to prevent clash with player escape logic
                var escapedLastFrame = Input.GetKeyDown(KeyCode.Escape);
                if (Input.GetKeyDown(Settings.mapKey))
                    map.IsFocused = !map.IsFocused;
                /*------------------------------------------------*/
                yield return new WaitForNextFrameUnit();
                /*------------------------------------------------*/
                if (escapedLastFrame) map.IsFocused = false;
            }
        }
    }

    #endregion

    #region Navigation

    IEnumerator DoNavigationScenario(NavigationScenario scenario)
    {
        var index = 0;
        foreach (var task in scenario.tasks)
        {
            References.ListenerPosition = task.listenerStartPosition.XZ(y: 0);
            References.AudioPosition = task.audioPosition.XZ(y: Settings.spawnHeight);
            /*------------------------------------------------*/
            var objectiveText = $"Find audio source {++index}/{scenario.tasks.Count}";
            yield return WaitForPrompt(objectiveText);
            /*------------------------------------------------*/
            UI.Singleton.bottomText.text = objectiveText;

            task.startTime = References.Now;
            var recording = StartCoroutine(RecordNavFramesLoop(onNewFrame: task.metrics.Add));
            /*------------------------------------------------*/
            yield return new WaitUntil(() => HasFoundSource() || (Application.isEditor && Input.GetKeyDown(KeyCode.R)));
            /*------------------------------------------------*/
            StopCoroutine(recording);
            task.endTime = References.Now;

            bool HasFoundSource() =>
                Vector3.Distance(References.ListenerPosition, References.AudioPosition) <
                Settings.foundSourceDistance;
        }
    }

    IEnumerator RecordNavFramesLoop(Action<NavigationScenario.Task.MetricsFrame> onNewFrame)
    {
        while (Application.isPlaying)
        {
            var prevListenerPosition = References.ListenerPosition;
            /*------------------------------------------------*/
            NavigationScenario.Task.MetricsFrame frame = default;
            yield return PathingRecorder.WaitForNextNavFrame(res => frame = res);
            /*------------------------------------------------*/

            if (frame.audioPath.Count < 2)
            {
                Debug.LogError("audioPath has less then two elements");
                continue;
            }

            onNewFrame(frame);

#if UNITY_EDITOR
            var efficiency = Utils.Efficiency(
                moveDir: (References.ListenerPosition - prevListenerPosition).XZ(),
                optimalDir: frame.audioPath[^2] - frame.audioPath[^1]
            );

            UI.Singleton.bottomText.text = $"Efficiency: {efficiency:P}";
            Debug.DrawLine(prevListenerPosition, References.ListenerPosition, new Color(1 - efficiency, efficiency, 0),
                30f, true);

            var pre = frame.audioPath.First();
            foreach (var next in frame.audioPath.Skip(1))
            {
                const float y = 3.0f;
                Debug.DrawLine(pre.XZ(y), next.XZ(y), Color.magenta, 0.1f, true);
                pre = next;
            }
#endif
        }
    }

    #endregion

    static IEnumerator WaitForPrompt(string message)
    {
        References.Paused = true;
        Message(message);
        /*------------------------------------------------*/
        yield return UI.Singleton.Prompt(message);
        /*------------------------------------------------*/
        References.Paused = false;
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