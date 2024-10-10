#if UNITY_EDITOR
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
    public AudioPath guessToAudioStraight = new();

    public AudioPath playerToGuessPathing = new();
    public AudioPath playerToAudioPathing = new();
    public AudioPath guessToAudioPathing = new();

    public float guessAngleDifference;
}

public class Progress
{
    public Action? OnUpdate;
    public Progress() => LocalizationTasks = Scenarios = Datas = new TaskProgress { Index = 0, Count = 1 };

    public struct TaskProgress
    {
        public int Index;
        public int Count;

        public float Progress => Index / (float)Count;
        public override string ToString() => $"{Index}/{Count}";
    }

    public readonly float StartTime = Time.realtimeSinceStartup;
    public TaskProgress Datas;
    public TaskProgress Scenarios;
    public TaskProgress LocalizationTasks;

    public float TotalProgress => Datas.Progress
                                  + (1 / (float)Datas.Count) * Scenarios.Progress
                                  + (1 / (float)Datas.Count) * (1 / (float)Scenarios.Count) *
                                  LocalizationTasks.Progress;
}

public static class Analysis
{
    public static IEnumerator Analyze(this StudyData[] data, Action<AnalyzedData[]> result, Progress? progress = null)
    {
        progress ??= new Progress();
        progress.Datas.Index = 0;
        progress.Datas.Count = data.Length;
        Study.AudioConfig = AudioConfiguration.Pathing;

        var res = new List<AnalyzedData>();
        foreach (var d in data)
        {
            yield return d.Analyze(res.Add, progress);
            progress.Datas.Index++;
            progress.OnUpdate?.Invoke();
        }

        result.Invoke(res.ToArray());
    }

    public static IEnumerator Analyze(this StudyData data, Action<AnalyzedData> result, Progress? progress = null)
    {
        progress ??= new Progress();
        progress.Scenarios.Count = data.scenarios.Count;

        var res = new AnalyzedData { answers = data.answers };
        progress.Scenarios.Index = 0;
        const float maxMoveSpeed = 5.5f;
        var penalty = 0f;
        foreach (var scenario in data.scenarios)
        {
            progress.LocalizationTasks.Count = scenario.localizationTasks.Count;
            progress.LocalizationTasks.Index = 0;

            SceneManager.LoadScene(scenario.scene);
            yield return new WaitForNextFrameUnit();

            var aScenario = new AnalyzedScenario
            {
                audioConfiguration = scenario.audioConfiguration,
                scene = scenario.scene,
                navigationTasks = scenario.navigationTasks,
            };
            res.scenarios.Add(aScenario);

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
                    guessToAudioStraight = new AudioPath
                        { isOccluded = true, points = { t.guessedPosition, t.audioPosition } },
                    guessAngleDifference = Vector2.Angle(playerToActual, playerToGuess)
                };
                
                aScenario.localizationTasks.Add(aLoc);

                // yield return Path(t.guessedPosition, t.listenerPosition, r => aLoc.playerToGuessPathing = r);
                // yield return Path(t.audioPosition, t.listenerPosition, r => aLoc.playerToAudioPathing = r);
                
            }
            int i = 0;
            foreach (var t in aScenario.localizationTasks)
            {
                UI.Singleton.screenText.text = $"task {i + 1} / {scenario.localizationTasks.Count}";
                yield return Path(t.guessedPosition, t.audioPosition, r => t.guessToAudioPathing = r);
                i++;
                progress.LocalizationTasks.Index++;
                progress.OnUpdate?.Invoke();
            }

            foreach (var navigation in scenario.navigationTasks)
            {
                navigation.startTime += penalty;
                var first = navigation.frames.First();
                var prevPosition = first?.position ?? Vector2.zero;
                var prevTime = first?.time ?? 0f;
                var hasStartedMoving = false;
                const float notStartedMovingTimeReduction = 0.9f;
                foreach (var frame in navigation.frames)
                {
                    var deltaTime = frame.time - prevTime;
                    prevTime = frame.time;
                    frame.time += penalty;
                    var distance = Vector2.Distance(prevPosition, frame.position);
                    if (distance > (maxMoveSpeed) * deltaTime)
                    {
                        penalty += distance / Player.movementSpeed;
                    }

                    if (!hasStartedMoving)
                    {
                        if (distance > 1.0f)
                        {
                            hasStartedMoving = true;
                        }
                        else
                        {
                            penalty -= deltaTime * notStartedMovingTimeReduction;
                        }
                    }

                    prevPosition = frame.position;
                }

                navigation.endTime += penalty;
            }

            foreach (var localization in scenario.localizationTasks)
            {
                localization.startTime += penalty;
                localization.endTime += penalty;
            }

            UI.Singleton.screenText.text = "Finished scenario";
            progress.Scenarios.Index++;
            progress.OnUpdate?.Invoke();
        }

        result.Invoke(res);
        yield break;

        IEnumerator Path(Vector2 audio, Vector2 listener, Action<AudioPath> result)
        {
            References.AudioPosition = audio;
            References.PlayerPosition = listener;
            yield return new WaitForNextFrameUnit();
            var res = new AudioPath { isOccluded = true };
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