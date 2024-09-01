using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public static class Localization
{
    public static IEnumerator DoScenario(LocalizationScenario scenario)
    {
        var map = LocalizationMap.Singleton;
        map.enabled = false;

        var index = 0;
        foreach (var task in scenario.tasks)
        {
            ++index;
            References.AudioPosition = task.audioPosition.XZ(y: StudySettings.Singleton.spawnHeight);
            References.ListenerPosition = task.listenerPosition.XZ(y: 0);
            /*------------------------------------------------*/
            yield return UI.WaitForPrompt($"Next: Localize audio source {index}/{scenario.tasks.Count} on the map");
            /*------------------------------------------------*/
            map.enabled = true;
            map.IsFocused = false;

            UI.Singleton.screenText.text = $"Localize the audio source on the map";

            task.startTime = References.Now;
            /*------------------------------------------------*/
            Vector3 localizedPosition = default;
            yield return WaitForSoundLocalized(res => localizedPosition = res);
            /*------------------------------------------------*/
            /*------------------------------------------------*/
            List<Vector2> audioPath = default;
            yield return PathingRecorder.WaitForPathingData(res => audioPath = res);
            /*------------------------------------------------*/
            map.mapPin.color = Color.clear;
            map.enabled = false;

            task.endTime = References.Now;
            task.guessedPosition = localizedPosition.XZ();
            task.audioPath = audioPath;

#if UNITY_EDITOR
            Debug.Log($"Localized Position {localizedPosition}");
            Debug.Log($"Audio Position {task.audioPosition}");
            Debug.Log($"Distance to source {Vector2.Distance(task.guessedPosition, task.audioPosition)}");
#endif
        }
    }

    static IEnumerator WaitForSoundLocalized(Action<Vector3> result)
    {
        var coroutineHolder = UnityEngine.Object.FindObjectOfType<Study>();
        var map = LocalizationMap.Singleton;

        Vector3? chosenPosition = null;

        var checkMapKey = coroutineHolder.StartCoroutine(CheckMapKeyLoop());
        var choseLocation = coroutineHolder.StartCoroutine(MapInteractionLoop());
        while (chosenPosition is null || !map.IsFocused)
        {
            /*------------------------------------------------*/
            yield return UI.WaitForKeyHold(KeyCode.Space);
            /*------------------------------------------------*/
        }

        coroutineHolder.StopCoroutine(checkMapKey);
        coroutineHolder.StopCoroutine(choseLocation);
        result.Invoke(chosenPosition.Value);
        yield break;

        IEnumerator MapInteractionLoop()
        {
            while (Application.isPlaying)
            {
                if (!map.IsFocused && chosenPosition is null)
                    map.mapPin.color = Color.clear;
                UI.Singleton.bottomText.text = map.IsFocused
                    ? chosenPosition is null
                        ? "Click where you think the audio source is."
                        : "\nHold [Space] to confirm your guess"
                    : $"\nPress [{StudySettings.Singleton.mapKey}] to open the map";
                /*------------------------------------------------*/
                yield return new WaitUntil(() => map.IsFocused);
                yield return new WaitForNextFrameUnit();
                /*------------------------------------------------*/
                if (map.PointerToWorldPosition() is not { } location) continue;

                if (Input.GetMouseButton((int)MouseButton.Left))
                {
                    chosenPosition = location;
                    map.mapPin.transform.position = location;
                    map.mapPin.color = Color.black;
                }
                else if (chosenPosition is null)
                {
                    map.mapPin.transform.position = location;
                    map.mapPin.color = Color.grey;
                }
                else
                {
                    map.mapPin.color = Color.red;
                }
            }
        }

        IEnumerator CheckMapKeyLoop()
        {
            while (Application.isPlaying)
            {
                // wait to prevent clash with player escape logic
                var escapedLastFrame = Input.GetKeyDown(KeyCode.Escape);
                if (Input.GetKeyDown(StudySettings.Singleton.mapKey))
                    map.IsFocused = !map.IsFocused;
                /*------------------------------------------------*/
                yield return new WaitForNextFrameUnit();
                /*------------------------------------------------*/
                if (escapedLastFrame) map.IsFocused = false;
            }
        }
    }
}