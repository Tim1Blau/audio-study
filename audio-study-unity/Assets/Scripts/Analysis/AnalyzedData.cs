#if UNITY_EDITOR
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;

[Serializable]
public record AnalyzedData
{
    public QuestionData answers = new();
    public List<AnalyzedScenario> scenarios = new();
}

[Serializable]
public record AnalyzedScenario
{
    public AudioConfiguration audioConfiguration;
    public string scene = "";
    public List<NavigationTask> navigationTasks = new();
    public List<AnalyzedLocalizationTask> localizationTasks = new();
}

[Serializable]
public record AnalyzedLocalizationTask
{
    public Vector2 listenerPosition;
    public Vector2 audioPosition;
    public float startTime = -1;
    public float endTime = -1;
    public Vector2 guessedPosition = Vector2.zero;
    public AudioPath audioPath = new();

    /* Analysis */

    public AudioPath playerToGuessStraight = new();
    public AudioPath playerToAudioStraight = new();

    public AudioPath playerToGuessPathing = new();
    public AudioPath playerToAudioPathing = new();
    public AudioPath guessToAudioPathing = new();

    public float guessAngleDifference;
}

public static class Analysis
{
    public static IEnumerator Analyze(this StudyData data, Action<AnalyzedData> result)
    {
        var res = new AnalyzedData { answers = data.answers };
        foreach (var scenario in data.scenarios)
        {
            yield return scenario.Analyze(res.scenarios.Add);
        }

        result.Invoke(res);
    }

    static IEnumerator Analyze(this Scenario scenario, Action<AnalyzedScenario> result)
    {
        SceneManager.LoadScene(scenario.scene);
        yield return new WaitForNextFrameUnit();
        Study.AudioConfig = AudioConfiguration.Pathing;
        yield return new WaitForNextFrameUnit();

        var aScenario = new AnalyzedScenario
        {
            audioConfiguration = scenario.audioConfiguration,
            scene = scenario.scene,
            navigationTasks = scenario.navigationTasks,
        };

        int i = 0;
        foreach (var t in scenario.localizationTasks)
        {
            var playerToGuess = t.guessedPosition - t.listenerPosition;
            var playerToActual = t.audioPosition - t.listenerPosition;
            var aLoc = new AnalyzedLocalizationTask
            {
                listenerPosition = t.listenerPosition,
                audioPosition = t.audioPosition,
                startTime = t.startTime,
                endTime = t.endTime,
                guessedPosition = t.guessedPosition,
                audioPath = t.audioPath,

                playerToGuessStraight = new AudioPath
                    { isOccluded = true, points = { t.listenerPosition, t.guessedPosition } },
                playerToAudioStraight = new AudioPath
                    { isOccluded = true, points = { t.listenerPosition, t.audioPosition } },
                guessAngleDifference = Vector2.Angle(playerToActual, playerToGuess)
            };
            UI.Singleton.screenText.text = $"task {i + 1} / {scenario.localizationTasks.Count}";
            aScenario.localizationTasks.Add(aLoc);

            yield return Path(t.guessedPosition, t.listenerPosition, r => aLoc.playerToGuessPathing = r);
            yield return Path(t.audioPosition, t.listenerPosition, r => aLoc.playerToAudioPathing = r);
            i++;
        }

        foreach (var t in aScenario.localizationTasks)
        {
            yield return Path(t.guessedPosition, t.audioPosition, r => t.guessToAudioPathing = r);
        }

        UI.Singleton.screenText.text = "Finished scenario";

        result.Invoke(aScenario);
        yield break;

        IEnumerator Path(Vector2 audio, Vector2 listener, Action<AudioPath> result)
        {
            References.AudioPosition = audio;
            References.PlayerPosition = listener;
            yield return new WaitForNextFrameUnit();
            var res = new AudioPath() { isOccluded = true };
            var start = Time.realtimeSinceStartup;
            yield return PathingRecorder.WaitForPathingData(r => res = r);
            while (res.isOccluded && start + 0.5f > Time.realtimeSinceStartup)
            {
                UI.Singleton.screenText.text = "Occluded, trying again";
                yield return PathingRecorder.WaitForPathingData(r => res = r);
            }

            if (res.isOccluded)
            {
                Debug.LogWarning("Still Occluded after retries");
            }

            result.Invoke(res);
        }
    }
}
#endif