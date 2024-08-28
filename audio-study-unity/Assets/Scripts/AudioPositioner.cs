using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SteamAudio;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Random = UnityEngine.Random;
using Vector3 = UnityEngine.Vector3;

public static class ExportPaths
{
    public static string Scene => SceneManager.GetActiveScene().path[..^".asset".Length] + "_Export.asset";
    public static string Probes => SceneManager.GetActiveScene().path[..^".asset".Length] + "_Probes.asset";
}

public class AudioPositioner : MonoBehaviour
{
    [Header("Source Parameters")]
    [SerializeField] int numSourcesToFind = 10;

    [SerializeField] float nextPositionMinDistance = 5.0f;
    [SerializeField] int seed = 12345678;

    [Header("Utility Parameters")]
    [SerializeField] float foundSourceDistance = 1.0f;

    [SerializeField] float pressToConfirmSeconds = 0.5f;

    [Header("References")]
    [SerializeField] SteamAudioSource steamAudioSource;

    [SerializeField] internal SteamAudioProbeBatch probeBatch;
    [SerializeField] SteamAudioListener listener;
    [SerializeField] Text promptText;
    [SerializeField] Text sideText;
    AudioSource _audioSource;

    IReadOnlyCollection<Vector3> _audioPositions;

    bool HasFoundSource => Vector3.Distance(ListenerPosition, AudioPosition) < foundSourceDistance;

    static float Now => Time.realtimeSinceStartup;

    internal StudyData _data;

    void Start()
    {
        steamAudioSource.pathingProbeBatch = probeBatch;
        _audioSource = steamAudioSource.GetComponent<AudioSource>();
        _audioPositions = GetRandomPositions(seed);
        StartCoroutine(StudyLoop());
        StartCoroutine(EfficiencyLoop());
        return;

        IEnumerator StudyLoop()
        {
            while (Application.isPlaying)
                yield return Study();
        }
    }

    IEnumerator Study()
    {
        sideText.text = promptText.text = "";
        yield return Prompt("Welcome to the Study");
        yield return Prompt("Task 1: Navigation\n" +
                            "Here you need to find audio sources as quickly as possible");

        Message("Started the Study");
        var startTime = Now;

        var toFind = new Stack<Vector3>(_audioPositions);
        while (toFind.Count > 0)
        {
            AudioPosition = toFind.Pop();
            var objectiveText = $"Find audio source {numSourcesToFind - toFind.Count}/{numSourcesToFind}";
            yield return Prompt(objectiveText);

            sideText.text = objectiveText;
            yield return new WaitUntil(() => HasFoundSource || (Application.isEditor && Input.GetKeyDown(KeyCode.R)));

            var newPos = AudioPosition;
            newPos.y = ListenerPosition.y;
            ListenerPosition = newPos;
            Message("Found source!");
        }

        var result = $"Found all sources in {Now - startTime:0.0} seconds";

        sideText.text = "Done";
        yield return Prompt(result);
    }

    struct Path
    {
        public Vector3 From, To;
    }

    Path? _lastAudioPath = null;

    IEnumerator EfficiencyLoop()
    {
        SteamAudioManager.Singleton.PathingVisCallback += (from, to, _, _) =>
            _lastAudioPath ??= new Path { From = Common.ConvertVector(from), To = Common.ConvertVector(to) };

        while (Application.isPlaying)
        {
            _lastAudioPath = null;
            var prevListenerPosition = ListenerPosition;

            // wait for `PathingVisCallback` to set `lastAudioPath`
            yield return new WaitForSeconds(SteamAudioSettings.Singleton.simulationUpdateInterval);
            if (Paused)
            {
                yield return new WaitWhile(() => Paused);
                continue;
            }

            if (_lastAudioPath is null)
            {
                if (Physics.RaycastAll(ListenerPosition, AudioPosition)
                    .Any(h => h.transform.TryGetComponent<SteamAudioStaticMesh>(out _))) Debug.LogWarning("No path!?");
            }

            var optimalPath = _lastAudioPath ?? new Path { From = AudioPosition, To = ListenerPosition };

            var optimalPathDir = ListenerPosition - optimalPath.From;
            var moveDir = ListenerPosition - prevListenerPosition;

            var angle = Vector3.Angle(-optimalPathDir, moveDir);
            var efficiency = 1 - angle / 180f;
            if (moveDir.magnitude < 0.1f) efficiency = 0.5f;

            sideText.text = $"Efficiency: {2 * (efficiency - 0.5):P}";
            Debug.Log($"Efficiency: {efficiency:P}");
            Debug.DrawLine(prevListenerPosition, ListenerPosition, new Color(1 - efficiency, efficiency, 0), 30f, true);
            Debug.DrawLine(optimalPath.From, ListenerPosition, Color.blue, 0.1f, true);
        }
    }

    IEnumerator Prompt(string message)
    {
        Paused = true;

        Message(message);
        promptText.text = message;
        sideText.text = "[Hold space to continue]";

        while (true)
        {
            yield return new WaitUntil(() => Input.GetKeyDown(KeyCode.Space) ||
                                             (Application.isEditor && Input.GetKey(KeyCode.X)));
            if (Input.GetKey(KeyCode.X))
                break;
            var pressStart = Now;
            yield return new WaitUntil(() => !Input.GetKey(KeyCode.Space) || Now - pressStart > pressToConfirmSeconds);
            if (Input.GetKey(KeyCode.Space))
                break;
        }

        promptText.text = "";
        Paused = false;
    }

    void OnValidate()
    {
        var valid = Validate().ToList();
        if (valid.Count <= 0) return;

        Debug.LogError("AudioPositioner Validation failed");
        valid.ForEach(Debug.LogError);
        return;

        IEnumerable<string> Validate()
        {
            if (steamAudioSource is null) yield return "Audio Source is null";
            if (probeBatch is null) yield return "Probe Batch is null";
            else if (probeBatch.asset is null &&
                     (probeBatch.asset = AssetDatabase.LoadAssetAtPath<SerializedData>(ExportPaths.Probes)) is null)
                yield return "Probe Batch asset is null, you need to bake the probes";
            else if (probeBatch.GetNumProbes() == 0)
                yield return "Probe Batch has no probes, try to re-bake the probes";
            if (listener is null) yield return "Listener is null";
        }
    }

    List<Vector3> GetRandomPositions(int seed)
    {
        var result = new List<Vector3>();
        Random.InitState(seed);

        var possiblePositions = probeBatch.ProbeSpheres
            .Select(p => p.center)
            .Select(Common.ConvertVector).ToArray();

        if (possiblePositions.Length == 0)
            throw new Exception($"Can't reposition audio source, there are no generated probe spheres");

        var prev = ListenerPosition;
        for (var i = 0; i < numSourcesToFind; i++)
        {
            var distanceFiltered = possiblePositions.Where(v => Vector3.Distance(v, prev) > nextPositionMinDistance)
                .ToArray();
            if (distanceFiltered.Length != 0)
            {
                result.Add(prev = RandomIndex(distanceFiltered));
            }
            else
            {
                Debug.LogWarning(
                    $"No available positions further than the min distance {nextPositionMinDistance}m away from the listener");
                result.Add(prev = RandomIndex(possiblePositions));
            }
        }

        return result;

        Vector3 RandomIndex(Vector3[] l) => l[Random.Range(0, l.Length - 1)]; // Note: ignore empty case
    }

    static void Message(string message) => Debug.Log("[STUDY] " + message);

    bool Paused
    {
        get => !DemoPlayerController.Singleton.canMove;
        set
        {
            if (Paused == value) return;
            DemoPlayerController.Singleton.canMove = !value;
            if (Paused) _audioSource.Pause();
            else _audioSource.UnPause();
        }
    }

    Vector3 ListenerPosition
    {
        get => listener.transform.position;
        set => listener.transform.position = value;
    }

    Vector3 AudioPosition
    {
        get => steamAudioSource.transform.position;
        set => steamAudioSource.transform.position = value;
    }
}