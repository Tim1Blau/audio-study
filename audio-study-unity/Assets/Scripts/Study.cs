using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using Vector3 = UnityEngine.Vector3;

public class Study : MonoBehaviour
{
    [Header("Study Parameters")]
    [SerializeField] AudioConfiguration audioConfiguration = AudioConfiguration.Pathing;

    [Header("Random Audio Positions")]
    [SerializeField] int numSourcesToFind = 10;

    [SerializeField] float nextPositionMinDistance = 5.0f;
    [SerializeField] int seed = 12345678;

    [Header("Utility Parameters")]
    [SerializeField] float foundSourceDistance = 1.0f;

    public StudyData data;

    bool HasFoundSource =>
        Vector3.Distance(References.ListenerPosition, References.AudioPosition) < foundSourceDistance;

    static void Message(string message) => Debug.Log("[STUDY] " + message);

    IEnumerable<Vector3> audioPositions => Utils.RandomAudioPositions(numSourcesToFind, nextPositionMinDistance,
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
            navigationScenarios = new(),
            localizationScenarios = new()
        };
        StartCoroutine(DoStudy());
    }

    IEnumerator DoStudy()
    {
        // yield return WaitForPrompt("Welcome to the Study");
        // yield return WaitForPrompt("Task 1: Navigation\n" +
        //                            "Here you need to find audio sources as quickly as possible");
        yield return DoLocalizationScenario();
        //yield return DoNavigationScenario();
        yield return WaitForPrompt("Completed Navigation Scenario 1");
    }
    const KeyCode mapKey = KeyCode.V;

    IEnumerator DoLocalizationScenario()
    {
        var scenario = new LocalizationScenario
        {
            scene = new Scene { name = SceneManager.GetActiveScene().name },
            tasks = new List<LocalizationScenario.Task>()
        };

        data.localizationScenarios.Add(scenario);
        var index = 0;
        foreach (var audioPosition in audioPositions)
        {
            References.AudioPosition = audioPosition;
            /*------------------------------------------------*/
            var objectiveText = $"Localize audio source {++index}/{numSourcesToFind} on the map";
            yield return WaitForPrompt(objectiveText);
            /*------------------------------------------------*/
            UI.Singleton.SideText = objectiveText;

            var startTime = References.Now;
            /*------------------------------------------------*/
            Vector3 localizedPosition = default;
            yield return WaitForSoundLocalized(objectiveText, res => localizedPosition = res);
            /*------------------------------------------------*/

            scenario.tasks.Add(new LocalizationScenario.Task
            {
                startTime = startTime,
                endTime = References.Now,
                audioPosition = audioPosition.XZ(),
                userLocalizationPosition = localizedPosition.XZ()
            });
            
            Message($"Localized Position {localizedPosition}");
            Message($"Audio Position {audioPosition}");
            Message($"Distance to source {Vector2.Distance(localizedPosition.XZ(), audioPosition.XZ())}");
        }
    }

    IEnumerator WaitForSoundLocalized(string objectiveText, Action<Vector3> result)
    {
        var map = LocalizationMap.Singleton;

        Vector3? chosenPosition = null;

        var checkMapKey = StartCoroutine(CheckMapKeyLoop());
        var choseLocation = StartCoroutine(MapInteractionLoop());
        while (chosenPosition is null || !map.enabled)
        {
            /*------------------------------------------------*/
            yield return UI.WaitForKeyHold(KeyCode.Space);
            /*------------------------------------------------*/
        }

        StopCoroutine(checkMapKey);
        StopCoroutine(choseLocation);
        map.enabled = false;
        result.Invoke(chosenPosition.Value);
        yield break;

        IEnumerator MapInteractionLoop()
        {
            while (Application.isPlaying)
            {
                /*------------------------------------------------*/
                yield return new WaitForNextFrameUnit();
                /*------------------------------------------------*/
                if (map.PointerToWorldPosition() is not { } location) continue;
                if (Input.GetMouseButtonUp((int)MouseButton.Left))
                {
                    chosenPosition = location;
                    map.mapPin.transform.position = location;
                }
                
                if (Input.GetMouseButton((int)MouseButton.Left))
                {
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
                UI.Singleton.SideText = objectiveText + (map.enabled
                    ? $" Click on map and hold [Space] to confirm"
                    : $" Press {mapKey} to open the map");
                /*------------------------------------------------*/
                yield return new WaitForNextFrameUnit();
                yield return new WaitUntil(() => Input.GetKeyDown(mapKey));
                /*------------------------------------------------*/
                map.enabled = !map.enabled;
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
        foreach (var audioPosition in audioPositions)
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
                metrics = new()
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