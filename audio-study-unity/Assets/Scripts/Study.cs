using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Vector3 = UnityEngine.Vector3;

[RequireComponent(typeof(References), typeof(UI))]
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
            localizationTasks = new()
        };
        StartCoroutine(DoStudy());
    }

    IEnumerator DoStudy()
    {
        yield return WaitForPrompt("Welcome to the Study");
        yield return WaitForPrompt("Task 1: Navigation\n" +
                                   "Here you need to find audio sources as quickly as possible");
        yield return DoNavigationScenario();
        yield return WaitForPrompt("Completed Navigation Scenario 1");
    }

    IEnumerator DoNavigationScenario()
    {
        var scenario = new NavigationScenario
        {
            scene = new Scene { name = SceneManager.GetActiveScene().name },
            tasks = new ()
        };
        data.navigationScenarios.Add(scenario);
        var index = 0;
        foreach (var audioPosition in Utils.RandomAudioPositions(numSourcesToFind, nextPositionMinDistance,
                     seed + SceneManager.GetActiveScene().name.GetHashCode()))
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
                metrics = new ()
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
            NavigationScenario.Task.MetricsFrame frame = default;
            /*------------------------------------------------*/
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
            Debug.Log($"Efficiency: {efficiency:P}");
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