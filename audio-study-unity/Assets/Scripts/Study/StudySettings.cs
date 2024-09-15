using System;
using System.Collections.Generic;
using System.Linq;
using SteamAudio;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

public class StudySettings : SingletonBehaviour<StudySettings>
{
    public ScenarioSettings scenarioSettings;
    public int navigationSeed = 1;
    public int localizationSeed = 2;

    // UI
    public const KeyCode MapKey = KeyCode.E;
    public const KeyCode ConfirmKey = KeyCode.Space;
    public const float PressToConfirmDuration = 0.5f;
    public const float PressToSelectSceneOrderDuration = 2.0f;
    public const int MapSizeFocused = 700;
    public const int MapSizeUnfocused = 600;

    // Navigation
    public const float FoundSourceDistance = 0.8f;
    public const float NavPauseBetween = 1.25f;

    // Localization Reference Positions
    public const int LocReferencePosCount = 6;
    public const float LocReferencePosDuration = 1.6f;
    public const float LocReferencePauseBetween = 0.15f;
    public const float LocReferenceDistanceBetween = 10.0f;

    // Other
    public const float AudioYPosition = 1.5f;

    readonly Lazy<Vector2[]> _probePositions = new(() => References.Singleton.probeBatch.ProbeSpheres
        .Select(p => p.center)
        .Select(x => Common.ConvertVector(x).XZ()).ToArray());

    void Start()
    {
        Study.Initialize();
    }

    public Scenario GenerateScenario() =>
        new()
        {
            scene = SceneManager.GetActiveScene().name,
            navigationTasks = GenerateNavigationTasks(),
            localizationTasks = GenerateLocalizationTasks()
        };

    public List<NavigationTask> GenerateNavigationTasks()
    {
        Random.InitState(navigationSeed);
        return RandomPositionsWithDistance(
                _probePositions.Value,
                scenarioSettings.navPositions + 1,
                scenarioSettings.navAudioDistances)
            .ToList()
            .Pairwise()
            .Select(fromTo =>
                new NavigationTask
                {
                    listenerStartPosition = fromTo.Item1,
                    audioPosition = fromTo.Item2,
                })
            .ToList();
    }

    public List<LocalizationTask> GenerateLocalizationTasks()
    {
        Random.InitState(localizationSeed);
        var listenerPosition = References.PlayerPosition;
        return Enumerable.Range(0, scenarioSettings.locAudioPositionsPerListenerPos)
            .Select(_ => RandomPosWithDistance(
                positions: _probePositions.Value,
                from: listenerPosition,
                minDistance: scenarioSettings.locAudioDistanceFromListener))
            .Select(audioPos =>
                new LocalizationTask
                {
                    listenerPosition = listenerPosition,
                    audioPosition = audioPos,
                })
            .ToList();
    }

    public IEnumerable<Vector2> GenerateReferencePositions() =>
        RandomPositionsWithDistance(
            _probePositions.Value
                .Where(DistanceGreaterThan(References.PlayerPosition, scenarioSettings.locListenerDistances)).ToArray(),
            LocReferencePosCount,
            LocReferenceDistanceBetween
        );

    static IEnumerable<Vector2> RandomPositionsWithDistance(Vector2[] positions, int count, float minDistance)
    {
        var start = new Vector2(-100, -100);
        for (var i = 0; i < count; i++)
            yield return start = RandomPosWithDistance(positions, start, minDistance);
    }

    static Vector2 RandomPosWithDistance(Vector2[] positions, Vector2 from, float minDistance)
    {
        var position = positions.Where(DistanceGreaterThan(from, minDistance)).ToArray().RandomIndex();
        if (position is { } p) return p;
        Debug.LogWarning(
            $"No available probePositions further than the min distance {minDistance}m away from the listener");
        return positions.RandomIndex() ?? throw new Exception("No probePositions");
    }

    static Func<Vector2, bool> DistanceGreaterThan(Vector2 from, float distance) =>
        v => Vector2.Distance(from, v) > distance;
}