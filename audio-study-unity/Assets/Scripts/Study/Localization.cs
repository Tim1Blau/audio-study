using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public static class Localization
{
    public static IEnumerator DoTasks(List<LocalizationTask> tasks)
    {
        if (tasks.Count == 0) yield break;
        Map.enabled = false;
        References.PlayerPosition = tasks[0].listenerPosition;
        yield return UI.WaitForPrompt(
            "Task 2/2: Localization\n"
            + "Guess the position of the audio source without moving\n");
        References.Player.canMove = false;
        Map.enabled = true;

        var index = 0;
        foreach (var task in tasks)
        {
            ++index;
            Map.IsFocused = false;
            References.PlayerPosition = task.listenerPosition;
            // BREAK //
            UI.Singleton.screenText.text = $"{index}/{tasks.Count}";
            UI.Singleton.bottomText.text = "";
            References.AudioPaused = true;
            yield return UI.WaitForSeconds(2.0f);
            /*------------------------------------------------*/
            // REFERENCES //
            yield return ShowReferencePositions();
            /*------------------------------------------------*/
            // BREAK //
            References.AudioPaused = true;
            UI.Singleton.screenText.text = "Next: Localize";
            yield return UI.WaitForSeconds(2.0f);
            /*------------------------------------------------*/
            // LOCALIZATION //
            References.AudioPaused = false;
            References.AudioPosition = task.audioPosition;
            Map.IsFocused = false;
            UI.Singleton.screenText.text = $"Localize the audio source on the map";
            task.startTime = References.Now;
            /*------------------------------------------------*/
            yield return WaitForSoundLocalized(res => task.guessedPosition = res.XZ());
            /*------------------------------------------------*/
            yield return PathingRecorder.WaitForPathingData(res => task.audioPath = res);
            /*------------------------------------------------*/
            task.endTime = References.Now;
            // DISPLAY ACTUAL POSITION //
            yield return ShowCorrectPosition(task.audioPosition);
            /*------------------------------------------------*/
            Map.mapPin.color = Color.clear;
        }

        Map.enabled = false;
    }

    static Map Map => Map.Singleton;

    static IEnumerator ShowReferencePositions()
    {
        Map.enabled = true;
        Map.IsFocused = false;
        UI.Singleton.screenText.text = "Reference Positions..."
                                       + "\nTip: Pay attention to direction and volume changes";
        var referencePositions = StudySettings.Singleton.RandomAudioPositions(StudySettings.NumLocPrimingPositions,
            StudySettings.Singleton.locAudioDistances);
        foreach (var position in referencePositions)
        {
            Map.mapPin.color = Color.green;
            References.AudioPaused = false;
            Map.mapPin.transform.position = position.XZ();
            References.AudioPosition = position;
            yield return UI.WaitForSeconds(StudySettings.LocPrimingPositionDuration);
            /*------------------------------------------------*/
            Map.mapPin.color = Color.clear;
            References.AudioPaused = true;
            yield return new WaitForSeconds(0.2f);
            /*------------------------------------------------*/
        }
    }

    static IEnumerator ShowCorrectPosition(Vector2 position)
    {
        const float seconds = 0.9f;
        const float beepDuration = 0.1f;
        Map.mapPin.transform.position = position.XZ();
        for (var i = 0; i < seconds / beepDuration; i++)
        {
            Map.mapPin.color = i % 2 == 0 ? Color.green : Color.black;
            yield return new WaitForSeconds(beepDuration);
        }
    }

    static IEnumerator WaitForSoundLocalized(Action<Vector3> result)
    {
        var co = UnityEngine.Object.FindObjectOfType<Study>();

        Vector3? chosenPosition = null;

        var checkMapKey = co.StartCoroutine(CheckMapKeyLoop());

        var confirmed = false;
        while (!confirmed)
        {
            if (chosenPosition is null) Map.mapPin.color = Color.clear;
            UI.Singleton.bottomText.text = $"\nPress [{StudySettings.MapKey}] to open the map";
            /*------------------------------------------------*/
            yield return new WaitUntil(() => Map.IsFocused);
            /*------------------------------------------------*/
            var choseLocation = co.StartCoroutine(MapInteractionLoop());

            /*------------------------------------------------*/
            yield return new WaitUntil(() => chosenPosition.HasValue || !Map.IsFocused);
            /*------------------------------------------------*/
            if (Map.IsFocused)
            {
                /*------------------------------------------------*/
                var hold = co.StartCoroutine(UI.WaitForKeyHold(KeyCode.Space, onFinished: () => confirmed = true));
                yield return new WaitUntil(() => !Map.IsFocused || confirmed);
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
                    : $"\nHold [{StudySettings.ConfirmKey}] to confirm your guess";
                yield return new WaitForNextFrameUnit();
                if (Map.PointerToWorldPosition() is not { } location) continue;

                if (Input.GetMouseButton((int)MouseButton.Left))
                {
                    chosenPosition = location;
                    Map.mapPin.transform.position = location;
                    Map.mapPin.color = Color.black;
                }
                else if (chosenPosition is null)
                {
                    Map.mapPin.transform.position = location;
                    Map.mapPin.color = Color.grey;
                }
                else
                {
                    Map.mapPin.color = Color.red;
                }
            }
        }

        IEnumerator CheckMapKeyLoop()
        {
            while (Application.isPlaying)
            {
                // wait to prevent clash with player escape logic
                var escapedLastFrame = Input.GetKeyDown(KeyCode.Escape);
                if (Input.GetKeyDown(StudySettings.MapKey))
                    Map.IsFocused = !Map.IsFocused;
                /*------------------------------------------------*/
                yield return new WaitForNextFrameUnit();
                /*------------------------------------------------*/
                if (escapedLastFrame) Map.IsFocused = false;
            }
        }
    }
}