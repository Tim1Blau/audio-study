using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using Vector3 = UnityEngine.Vector3;

public class Study : MonoBehaviour
{
    [Header("Study Parameters")]
    [SerializeField] AudioConfiguration audioConfiguration = AudioConfiguration.Pathing;

    [Header("Random Audio Positions")]
    [SerializeField] int numSourcesToFind = 3;

    [SerializeField] float nextPositionMinDistance = 5.0f;
    [SerializeField] int seed = 12345678;

    [Header("Utility Parameters")]
    [SerializeField] float foundSourceDistance = 1.0f;

    [SerializeField] KeyCode mapKey = KeyCode.E;

    public StudyData data;

    bool HasFoundSource =>
        Vector3.Distance(References.ListenerPosition, References.AudioPosition) < foundSourceDistance;

    static void Message(string message) => Debug.Log("[STUDY] " + message);

    IEnumerable<Vector3> AudioPositions => Utils.RandomAudioPositions(numSourcesToFind, nextPositionMinDistance,
        seed + SceneManager.GetActiveScene().name.GetHashCode());

    void Start()
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

        data = new StudyData
        {
            audioConfiguration = audioConfiguration,
            navigationScenarios = new List<NavigationScenario>(),
            localizationScenarios = new List<LocalizationScenario>()
        };
        StartCoroutine(DoStudy());
    }

    void Update()
    {
        // Backup Export
        if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.P))
        {
            JsonData.Export(data);
        }
    }

    IEnumerator DoStudy()
    {
        yield return WaitForPrompt("Welcome to the Study");
        // yield return WaitForPrompt("Task 1: Navigation\n" +
        //                            "Here you need to find audio sources as quickly as possible");
        yield return DoLocalizationScenario();
        //yield return DoNavigationScenario();
        yield return WaitForPrompt("Completed Navigation Scenario 1");
        yield return WaitForPrompt("Export Data?");
        JsonData.Export(data);
    }

    IEnumerator DoLocalizationScenario()
    {
        var scenario = new LocalizationScenario
        {
            scene = new Scene { name = SceneManager.GetActiveScene().name },
            tasks = new List<LocalizationScenario.Task>()
        };

        data.localizationScenarios.Add(scenario);

        var map = LocalizationMap.Singleton;
        map.enabled = false;

        var index = 0;
        foreach (var audioPosition in AudioPositions)
        {
            ++index;
            References.AudioPosition = audioPosition;
            /*------------------------------------------------*/
            yield return WaitForPrompt($"Next: Localize audio source {index}/{numSourcesToFind} on the map");
            /*------------------------------------------------*/
            map.enabled = true;
            map.IsFocused = false;

            UI.Singleton.screenText.text = $"Localize the audio source on the map";

            var startTime = References.Now;
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
            
            scenario.tasks.Add(new LocalizationScenario.Task
            {
                startTime = startTime,
                endTime = References.Now,
                listenerPosition = References.ListenerPosition.XZ(),
                audioPosition = audioPosition.XZ(),
                guessedPosition = localizedPosition.XZ(),
                audioPath = audioPath,
            });

            Message($"Localized Position {localizedPosition}");
            Message($"Audio Position {audioPosition}");
            Message($"Distance to source {Vector2.Distance(localizedPosition.XZ(), audioPosition.XZ())}");
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
                if (!map.IsFocused && chosenPosition is null)
                    map.mapPin.color = Color.clear;
                UI.Singleton.SideText = map.IsFocused
                    ? chosenPosition is null
                        ? "Click where you think the audio source is."
                        : "\nHold [Space] to confirm your guess"
                    : $"\nPress [{mapKey}] to open the map";
                
                // wait to prevent clash with player escape logic
                var escapedLastFrame = Input.GetKeyDown(KeyCode.Escape);  
                if (Input.GetKeyDown(mapKey))
                    map.IsFocused = !map.IsFocused;
                /*------------------------------------------------*/
                yield return new WaitForNextFrameUnit();
                /*------------------------------------------------*/
                if (escapedLastFrame) map.IsFocused = false;
            }
        }
    }


    IEnumerator DoNavigationScenario()
    {
        var scenario = new NavigationScenario
        {
            scene = new Scene { name = SceneManager.GetActiveScene().name },
            tasks = new List<NavigationScenario.Task>()
        };
        data.navigationScenarios.Add(scenario);
        var index = 0;
        foreach (var audioPosition in AudioPositions)
        {
            References.AudioPosition = audioPosition;
            /*------------------------------------------------*/
            var objectiveText = $"Find audio source {++index}/{numSourcesToFind}";
            yield return WaitForPrompt(objectiveText);
            /*------------------------------------------------*/
            UI.Singleton.SideText = objectiveText;

            var task = new NavigationScenario.Task
            {
                startTime = References.Now,
                endTime = -1,
                audioPosition = References.AudioPosition.XZ(),
                metrics = new List<NavigationScenario.Task.MetricsFrame>()
            };

            scenario.tasks.Add(task);
            var recording = StartCoroutine(RecordNavFramesLoop(onNewFrame: task.metrics.Add));
            /*------------------------------------------------*/
            yield return new WaitUntil(() => HasFoundSource || (Application.isEditor && Input.GetKeyDown(KeyCode.R)));
            /*------------------------------------------------*/

            task.endTime = References.Now;
            StopCoroutine(recording);
            References.ListenerPosition = References.AudioPosition.XZ().XZ(References.ListenerPosition.y);
            Message("Found source!");
        }
    }

    static IEnumerator WaitForPrompt(string message)
    {
        References.Paused = true;
        Message(message);
        /*------------------------------------------------*/
        yield return UI.Singleton.Prompt(message);
        /*------------------------------------------------*/
        References.Paused = false;
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
            onNewFrame(frame);

            if (frame.audioPath.Count < 2)
            {
                Debug.LogError("audioPath has less then two elements");
                continue;
            }

            data.navigationScenarios.Last().tasks.Last().metrics.Add(frame);

#if UNITY_EDITOR
            var efficiency = Utils.Efficiency(
                moveDir: (References.ListenerPosition - prevListenerPosition).XZ(),
                optimalDir: frame.audioPath[^2] - frame.audioPath[^1]
            );

            UI.Singleton.SideText = $"Efficiency: {efficiency:P}";
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
}