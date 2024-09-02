using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class Navigation
{
    public static IEnumerator DoTasks(List<NavigationTask> tasks)
    {
        var coroutineHolder = UnityEngine.Object.FindObjectOfType<Study>();
        var index = 0;
        foreach (var task in tasks)
        {
            References.PlayerPosition = task.listenerStartPosition.XZ(y: 0);
            References.AudioPosition = task.audioPosition.XZ(y: StudySettings.Singleton.spawnHeight);
            /*------------------------------------------------*/
            var objectiveText = $"Find audio source {++index}/{tasks.Count}";
            yield return UI.WaitForPrompt(objectiveText);
            /*------------------------------------------------*/
            UI.Singleton.bottomText.text = objectiveText;

            task.startTime = References.Now;
            var recording = coroutineHolder.StartCoroutine(RecordNavFramesLoop(onNewFrame: task.metrics.Add));
            /*------------------------------------------------*/
            yield return new WaitUntil(() => HasFoundSource() || (Application.isEditor && Input.GetKeyDown(KeyCode.R)));
            /*------------------------------------------------*/
            coroutineHolder.StopCoroutine(recording);
            task.endTime = References.Now;

            bool HasFoundSource() =>
                Vector3.Distance(References.PlayerPosition, References.AudioPosition) <
                StudySettings.Singleton.foundSourceDistance;
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
                moveDir: (References.PlayerPosition - prevListenerPosition).XZ(),
                optimalDir: frame.audioPath[^2] - frame.audioPath[^1]
            );

            UI.Singleton.bottomText.text = $"Efficiency: {efficiency:P}";
            Debug.DrawLine(prevListenerPosition, References.PlayerPosition, new Color(1 - efficiency, efficiency, 0),
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