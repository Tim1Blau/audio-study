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

    IReadOnlyCollection<Vector3> _audioPositions;

    bool HasFoundSource =>
        Vector3.Distance(References.ListenerPosition, References.AudioPosition) < foundSourceDistance;

    void Start()
    {
        _audioPositions = Utils.RandomAudioPositions(numSourcesToFind, nextPositionMinDistance, seed);
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
        StartCoroutine(StudyLoop());
        return;

        IEnumerator StudyLoop()
        {
            while (Application.isPlaying)
                yield return DoStudy();
        }
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

    IEnumerator DoStudy()
    {
        /*------------------------------------------------*/
        yield return WaitForPrompt("Welcome to the Study");
        yield return WaitForPrompt("Task 1: Navigation\n" +
                            "Here you need to find audio sources as quickly as possible");
        /*------------------------------------------------*/

        Message("Started the Study");
        var startTime = References.Now;

        var toFind = new Stack<Vector3>(_audioPositions);

        var scenario = new NavigationScenario
        {
            scene = new Scene { name = SceneManager.GetActiveScene().name },
            tasks = new List<NavigationScenario.Task>()
        };
        data.navigationScenarios.Add(scenario);
        while (toFind.Count > 0)
        {
            References.AudioPosition = toFind.Pop();
            var objectiveText = $"Find audio source {numSourcesToFind - toFind.Count}/{numSourcesToFind}";
            /*------------------------------------------------*/
            yield return WaitForPrompt(objectiveText);
            /*------------------------------------------------*/

            var task = new NavigationScenario.Task
            {
                startTime = References.Now,
                endTime = -1,
                audioPosition = References.AudioPosition.XZ(),
                metrics = new()
            };

            scenario.tasks.Add(task);
            UI.Singleton.SideText = objectiveText;

            var recording = StartCoroutine(RecordNavFramesLoop(onNewFrame: task.metrics.Add));
            /*------------------------------------------------*/
            yield return new WaitUntil(() => HasFoundSource || (Application.isEditor && Input.GetKeyDown(KeyCode.R)));
            /*------------------------------------------------*/

            Message("Found source!");
            StopCoroutine(recording);
            data.navigationScenarios.Last().tasks.Last().endTime = References.Now;
            References.ListenerPosition = References.AudioPosition.XZ().XZ(References.ListenerPosition.y);
        }

        var result = $"Found all sources in {References.Now - startTime:0.0} seconds";

        UI.Singleton.SideText = "Done";
        /*------------------------------------------------*/
        yield return WaitForPrompt(result);
        /*------------------------------------------------*/
    }

    List<(Vector3 From, Vector3 To)> _currentAudioPath = new();


    IEnumerator WaitForPrompt(string message)
    {
        References.Paused = true;
        Message(message);
        /*------------------------------------------------*/
        yield return UI.Singleton.Prompt(message);
        /*------------------------------------------------*/
        References.Paused = false;
    }

    static void Message(string message) => Debug.Log("[STUDY] " + message);
}