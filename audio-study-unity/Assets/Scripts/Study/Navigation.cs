using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class Navigation
{
    public static IEnumerator DoTasks(List<NavigationTask> tasks)
    {
        if (tasks.Count == 0) yield break;
        References.PlayerPosition = tasks[0].listenerStartPosition;
        yield return UI.WaitForPrompt(
            "Task 1/2: Navigation\n" +
            "Find the audio source as quickly as possible");
        References.Player.canMove = true;

        var coroutineHolder = UnityEngine.Object.FindObjectOfType<Study>();
        var index = 0;
        foreach (var task in tasks)
        {
            ++index;
            References.PlayerPosition = task.listenerStartPosition;
            References.AudioPosition = task.audioPosition;

            UI.Singleton.screenText.text = $"{index}/{tasks.Count}";
            UI.Singleton.bottomText.text = "";
            /*------------------------------------------------*/
            References.PlayerAndAudioPaused = true;
            yield return UI.WaitForSeconds(StudySettings.NavPauseBetween);
            References.PlayerAndAudioPaused = false;
            /*------------------------------------------------*/
            UI.Singleton.screenText.text = "";
            UI.Singleton.bottomText.text = $"Find audio source {index}/{tasks.Count}";

            task.startTime = References.Now;
            var recording = coroutineHolder.StartCoroutine(RecordNavFramesLoop(onNewFrame: task.frames.Add));
            /*------------------------------------------------*/
            yield return new WaitUntil(() => HasFoundSource() || (Application.isEditor && Input.GetKeyDown(KeyCode.R)));
            /*------------------------------------------------*/
            coroutineHolder.StopCoroutine(recording);
            task.endTime = References.Now;
            continue;

            bool HasFoundSource() =>
                Vector2.Distance(References.PlayerPosition, References.AudioPosition) <
                StudySettings.FoundSourceDistance;
        }
    }

    static IEnumerator RecordNavFramesLoop(Action<NavigationTask.MetricsFrame> onNewFrame)
    {
        while (Application.isPlaying)
        {
            var prevListenerPosition = References.PlayerPosition;
            /*------------------------------------------------*/
            NavigationTask.MetricsFrame frame = default;
            yield return PathingRecorder.WaitForNextNavFrame(res => frame = res);
            /*------------------------------------------------*/

            if (frame.audioPath.Count < 2)
            {
                Debug.LogError("audioPath has less then two elements");
                continue;
            }

            onNewFrame(frame);

#if UNITY_EDITOR
            var efficiency = Utils.Efficiency(
                moveDir: References.PlayerPosition - prevListenerPosition,
                optimalDir: frame.audioPath[^2] - frame.audioPath[^1]
            );

            // UI.Singleton.bottomText.text = $"Efficiency: {efficiency:P}";
            Debug.DrawLine(prevListenerPosition.XZ(1), References.PlayerPosition.XZ(1),
                new Color(1 - efficiency, efficiency, 0),
                30f, true);
#endif
        }
    }
}