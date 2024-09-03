using System;
using System.Collections.Generic;
using System.Linq;
using SteamAudio;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

public class StudySettings : SingletonBehaviour<StudySettings>
{
    [Header("Task Generation Settings")]
    public int seed = 12345678;

    public int locListenerPositions = 1;
    public int locSourcePositionsPerListenerPos = 1;
    public float locListenerDistances = 10;
    public float locAudioDistances = 4;
    public int navPositions = 1;
    public float navAudioDistances = 7;

    [Header("Utility Parameters")]
    public const float FoundSourceDistance = 1.0f;
    public const float SpawnHeight = 1.5f;
    public const KeyCode MapKey = KeyCode.E;
    public const KeyCode ConfirmKey = KeyCode.Space;
    public const float PressToConfirmDuration = 0.5f;
    public const int NumLocPrimingPositions = 10;
    public const float LocPrimingPositionDuration = 2.0f;
    public const int MapSizeFocused = 700;
    public const int MapSizeUnfocused = 600;

    Vector2[] _probePositions;

    void Start()
    {
        _probePositions = References.Singleton.probeBatch.ProbeSpheres.Select(p => p.center)
            .Select(x => Common.ConvertVector(x).XZ()).ToArray();
        Study.Initialize();
    }

    public Scenario GenerateScenario()
    {
        var settings = this;
        var scene = SceneManager.GetActiveScene();
        Random.InitState(settings.seed);

        return new Scenario
        {
            scene = scene.name,
            navigationTasks =
                RandomAudioPositions(settings.navPositions + 1, settings.navAudioDistances).ToList().Pairwise()
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
    }
    
    public IEnumerable<Vector2> RandomAudioPositions(int count, float minDistance)
    {
        var start = new Vector2(-100, -100);
        for (var i = 0; i < count; i++)
            yield return start = RandomPosWithMinDistance(start, minDistance);
    }

    public Vector2 RandomPosWithMinDistance(Vector2 from, float minDistance)
    {
        var position = _probePositions.Where(v => Vector2.Distance(from, v) > minDistance).ToArray().RandomIndex();
        if (position is {} p) return p;
        Debug.LogWarning(
            $"No available probePositions further than the min distance {minDistance}m away from the listener");
        return _probePositions.RandomIndex() ?? throw new Exception("No probePositions");
    }
}