#if UNITY_EDITOR
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SteamAudio;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using Vector3 = UnityEngine.Vector3;

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
    public List<AnalyzedNavigationTask> navigationTasks = new();
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

[Serializable]
public record AnalyzedNavigationTask
{
    public AudioConfiguration audioConfiguration;
    public Vector2 listenerStartPosition;
    public Vector2 audioPosition;

    public float startTime = -1;
    public float endTime = -1;
    public TimeSpan Duration => TimeSpan.FromSeconds(endTime - startTime);
    public List<NavigationTask.MetricsFrame> frames = new();
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
                scene = scenario.scene
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

            var i = 0;
            foreach (var t in aScenario.localizationTasks)
            {
                UI.Singleton.screenText.text = $"task {i + 1} / {scenario.localizationTasks.Count}";
                // yield return Path(t.guessedPosition, t.audioPosition, r => t.guessToAudioPathing = r,
                //     snappedAudioPos => t.guessedPosition = snappedAudioPos);
                t.guessToAudioPathing = new AudioPath{ isOccluded = true, points = { t.guessedPosition, t.audioPosition } };
                i++;
                progress.LocalizationTasks.Index++;
                progress.OnUpdate?.Invoke();
            }

            foreach (var nav in scenario.navigationTasks)
            {
                var navigation = new AnalyzedNavigationTask
                {
                    listenerStartPosition = nav.listenerStartPosition,
                    audioPosition = nav.audioPosition,

                    startTime = nav.startTime,
                    endTime = nav.endTime,
                    frames = nav.frames,

                    audioConfiguration = scenario.audioConfiguration,
                };
                navigation.startTime += penalty;
                var first = navigation.frames.First();
                var prevPosition = first?.position ?? Vector2.zero;
                var prevTime = first?.time ?? 0f;
                var hasStartedMoving = false;
                const float notStartedMovingTimeReduction = 0.9f;
                const float maxMoveSpeed = 5.5f;
                foreach (var frame in navigation.frames)
                {
                    var deltaTime = frame.time - prevTime;

                    if (deltaTime < 0) 
                        throw new Exception("Negative delta time, data is corrupted");

                    var distance = Vector2.Distance(prevPosition, frame.position);
                    if (distance > maxMoveSpeed * deltaTime)
                    {
                        var penaltyAdded = distance / Player.movementSpeed - deltaTime;
                        penalty += penaltyAdded;
                        if(penaltyAdded < 0) throw new Exception("penalty added is negative");
                        if(penaltyAdded > 0.15) throw new Exception("penalty added is too large");
                    }

                    if (!hasStartedMoving)
                    {
                        if (distance > 1f * deltaTime)
                        {
                            hasStartedMoving = true;
                        }
                        else
                        {
                            penalty -= deltaTime * notStartedMovingTimeReduction;
                        }
                    }
                    
                    
                    prevPosition = frame.position;
                    prevTime = frame.time;
                    frame.time += penalty;
                }

                navigation.endTime += penalty;
                if(navigation.startTime > navigation.endTime)
                    throw new Exception("Duration is negative");
                
                aScenario.navigationTasks.Add(navigation);
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

        IEnumerator Path(Vector2 audio, Vector2 listener, Action<AudioPath> result,
            Action<Vector2>? snappedAudioPosition)
        {
            References.AudioPosition = audio;
            References.PlayerPosition = listener;
            yield return new WaitForNextFrameUnit();
            var res = new AudioPath { isOccluded = true };
            yield return PathingRecorder.WaitForPathingData(r => res = r);
            if (res.isOccluded)
            {
                Debug.LogWarning("No path to audio position, snapping position");
                audio = References.Singleton.probeBatch.ProbeSpheres.Select(s => Common.ConvertVector(s.center).XZ())
                    .OrderBy(s => Vector3.Distance(audio, s)).First();

                References.AudioPosition = audio;
                snappedAudioPosition?.Invoke(audio);
                yield return new WaitForNextFrameUnit();

                yield return PathingRecorder.WaitForPathingData(r => res = r);

                var start = Time.realtimeSinceStartup;
                while (res.isOccluded && start + 0.5f > Time.realtimeSinceStartup)
                {
                    Debug.LogWarning("Retry Occlusion");
                    UI.Singleton.screenText.text = "Occluded, trying again";
                    yield return PathingRecorder.WaitForPathingData(r => res = r);
                }

                if (res.isOccluded)
                {
                    Debug.LogError("Bug: still no path to audio position");
                }
            }

            result.Invoke(res);
        }
    }
}
#endif