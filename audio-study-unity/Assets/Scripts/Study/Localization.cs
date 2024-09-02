using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Assertions;

public static class Localization
{
    public static IEnumerator DoTasks(List<LocalizationTask> tasks)
    {
        if (tasks.Count == 0) yield break;
        var map = LocalizationMap.Singleton;
        map.enabled = false;
        References.PlayerPosition = tasks[0].listenerPosition.XZ();
        yield return UI.WaitForPrompt(
            "Task 2/2: Localization\n" +
            "Guess the position of the audio source without moving");

        var index = 0;
        foreach (var task in tasks)
        {
            ++index;
            References.AudioPosition = task.audioPosition.XZ(y: StudySettings.Singleton.spawnHeight);
            References.PlayerPosition = task.listenerPosition.XZ();
            /*------------------------------------------------*/
            // yield return UI.WaitForPrompt($"Next: Localize audio source {index}/{tasks.Count} on the map");
            UI.Singleton.screenText.text = $"{index}/{tasks.Count}";
            yield return UI.TakeABreak(seconds: 2.0f);
            UI.Singleton.screenText.text = "";
            /*------------------------------------------------*/
            References.Player.canMove = false;
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
            task.endTime = References.Now;
            task.guessedPosition = localizedPosition.XZ();
            task.audioPath = audioPath;
            /*------------------------------------------------*/
            yield return DisplayActualPosition(task.audioPosition);
            /*------------------------------------------------*/
            map.mapPin.color = Color.clear;
            map.enabled = false;

#if UNITY_EDITOR
            Debug.Log($"Localized Position {localizedPosition}");
            Debug.Log($"Audio Position {task.audioPosition}");
            Debug.Log($"Distance to source {Vector2.Distance(task.guessedPosition, task.audioPosition)}");
#endif
        }
    }

    static IEnumerator DisplayActualPosition(Vector2 position)
    {
        const float seconds = 0.9f;
        const float beepDuration = 0.1f;
        var map = LocalizationMap.Singleton;
        map.mapPin.transform.position = position.XZ();
        for (var i = 0; i < seconds / beepDuration; i++)
        {
            map.mapPin.color = i % 2 == 0 ? Color.green : Color.black;
            yield return new WaitForSeconds(beepDuration);
        }
    }

    static IEnumerator WaitForSoundLocalized(Action<Vector3> result)
    {
        var co = UnityEngine.Object.FindObjectOfType<Study>();
        var map = LocalizationMap.Singleton;

        Vector3? chosenPosition = null;

        var checkMapKey = co.StartCoroutine(CheckMapKeyLoop());

        var confirmed = false;
        while (!confirmed)
        {
            if (chosenPosition is null) map.mapPin.color = Color.clear;
            UI.Singleton.bottomText.text = $"\nPress [{StudySettings.Singleton.mapKey}] to open the map";
            /*------------------------------------------------*/
            yield return new WaitUntil(() => map.IsFocused);
            /*------------------------------------------------*/
            var choseLocation = co.StartCoroutine(MapInteractionLoop());

            /*------------------------------------------------*/
            yield return new WaitUntil(() => chosenPosition.HasValue || !map.IsFocused);
            /*------------------------------------------------*/
            if (map.IsFocused)
            {
                /*------------------------------------------------*/
                var hold = co.StartCoroutine(UI.WaitForKeyHold(KeyCode.Space, onFinished: () => confirmed = true));
                yield return new WaitUntil(() => !map.IsFocused || confirmed);
                co.StopCoroutine(hold);
                /*------------------------------------------------*/
            }
            co.StopCoroutine(choseLocation);
        }

        co.StopCoroutine(checkMapKey);
        result.Invoke(chosenPosition.Value);
        yield break;

        IEnumerator MapInteractionLoop()
        {
            while (Application.isPlaying)
            {
                UI.Singleton.bottomText.text = chosenPosition is null
                    ? "Click where you think the audio source is."
                    : $"\nHold [{StudySettings.Singleton.confirmKey}] to confirm your guess";
                yield return new WaitForNextFrameUnit();
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