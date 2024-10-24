using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
            new LocalText(
                "Localization:"
                + "\nGuess the position of the audio source without moving",
                "Lokalisierung:"
                + "\nErraten Sie die Position der Audioquelle, ohne sich zu bewegen"
            ));
        if (Application.isEditor && Input.GetKey(KeyCode.R)) yield break;
        References.Player.canMove = false;
        Map.enabled = true;

        var index = 0;
        foreach (var task in tasks)
        {
            ++index;
            Map.IsFocused = false;
            References.PlayerPosition = task.listenerPosition;
            // BREAK //
            yield return UI.WaitForPrompt(new LocalText(
                "Next: Example audio positions"
                + "\nUse these as reference to determine the position of the audio source"
                + "\nTip: Pay close attention to the direction and distance of the sound",
                "Es folgen Beispielaudiopositonen."
                + "\nNutzen Sie diese als Referenz, um die Position der Audioquelle bestimmen zu können"
                + "\nTipp: Achten Sie auf die Richtung und Distanz des Tons"
            ));
            /*------------------------------------------------*/
            // REFERENCES //
            yield return ShowReferencePositions();
            /*------------------------------------------------*/
            // BREAK //
            yield return UI.WaitForPrompt(new LocalText(
                $"Next: Guess the position of the audio source {index}/{tasks.Count}",
                $"Es folgt: Erraten Sie die Position der Audioquelle {index}/{tasks.Count}"
            ));
            /*------------------------------------------------*/
            // LOCALIZATION //
            References.AudioPaused = false;
            References.AudioPosition = task.audioPosition;
            Map.IsFocused = false;
            UI.Singleton.screenText.text = new LocalText(
                "Localize the audio source on the map",
                "Lokalisieren Sie die Audioquelle auf der Karte");
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
        Map.IsFocused = true;
        Map.map.color = new Color(1, 1, 1, 0.8f);
        References.Player.ShowMouse = false;
        UI.Singleton.screenText.text = new LocalText(
            "Example audio positions...",
            "Beispielaudiopositonen..."
        );
        foreach (var position in StudySettings.Singleton.GenerateReferencePositions())
        {
            Map.mapPin.color = Color.green;
            Map.mapPin.transform.position = position.XZ();
            References.AudioPosition = position;
            References.AudioPaused = false;
            yield return UI.WaitForSeconds(StudySettings.LocReferencePosDuration);
            /*------------------------------------------------*/
            Map.mapPin.color = Color.clear;
            References.AudioPaused = true;
            yield return new WaitForSeconds(StudySettings.LocReferencePauseBetween);
            /*------------------------------------------------*/
        }

        Map.IsFocused = false;
        Map.map.color = Color.white;
    }
    
    static IEnumerator ShowCorrectPosition(Vector2 position)
    {
        const float seconds = 0.7f;
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
            UI.Singleton.bottomText.text = new LocalText(
                $"Press [{StudySettings.MapKey}] to open the map",
                $"Drücken Sie [{StudySettings.MapKey}] um die Karte zu öffnen");
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
                    ? new LocalText(
                        "Click where you think the audio source is.",
                        "Klicken Sie auf die Stelle an der sie denken, dass sich die Audioquelle befindet.")
                    : new LocalText(
                        $"Hold [{StudySettings.ConfirmKey}] to confirm your guess",
                        $"Halten Sie [{StudySettings.ConfirmKey}] um die Position zu bestätigen");
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