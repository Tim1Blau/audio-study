using System;
using System.Collections.Generic;
using System.IO;
using JetBrains.Annotations;
using UnityEngine;

public enum AudioConfiguration
{
    Basic = 0,   // Transmission, No Pathing
    Pathing = 1, // Pathing, No Transmission
    Mixed = 2    // Pathing & Transmission 
}

[Serializable]
public record StudyData
{
    public QuestionData answers = new();
    public List<Scenario> scenarios = new();
}

[Serializable]
public record QuestionData
{
    public enum Frequency
    {
        Undefined = 0,
        Never = 1,
        Rarely = 2,
        Occasionally = 3,
        Often = 4
    }

    public int age;
    public Frequency gamingExperience;
    public Frequency firstPersonExperience;
    public Frequency headphoneUsage;
}

[Serializable]
public record Scenario
{
    public AudioConfiguration audioConfiguration;
    public string scene = "";
    public List<NavigationTask> navigationTasks = new();
    public List<LocalizationTask> localizationTasks = new();
}

[Serializable]
public record LocalizationTask
{
    public Vector2 listenerPosition;
    public Vector2 audioPosition;

    public float startTime = -1;
    public float endTime = -1;
    public Vector2 guessedPosition = Vector2.zero;
    public AudioPath audioPath = new();
}

[Serializable]
public record NavigationTask
{
    public Vector2 listenerStartPosition;
    public Vector2 audioPosition;

    public float startTime = -1;
    public float endTime = -1;
    public List<MetricsFrame> frames = new();

    [Serializable]
    public record MetricsFrame
    {
        public float time;
        public Vector2 position;
        public Vector2 rotation;
        public AudioPath audioPath = new();
    }
}

[Serializable]
public record AudioPath
{
    public bool isOccluded; // if true, the path may be invalid
    public List<Vector2> points = new();
}


public static class JsonData
{
    const string DefaultPath = "StudyData";

    public static void Export(StudyData data, string path = DefaultPath)
    {
        var json = JsonUtility.ToJson(data, prettyPrint: true);
        path += ".json";
        File.WriteAllText(path, json);
        Debug.Log($"Exported Data to {path}");
    }

    [CanBeNull]
    public static StudyData Import(string path = DefaultPath)
    {
        if (!File.Exists(path))
        {
            Debug.LogError($"File not found: {path}");
            return null;
        }

        var json = File.ReadAllText(path);
        return JsonUtility.FromJson<StudyData>(json);
    }
}