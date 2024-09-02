using System;
using System.Collections.Generic;
using System.IO;
using JetBrains.Annotations;
using UnityEngine;

public enum AudioConfiguration
{
    Basic,   // Transmission, No Pathing
    Pathing, // Pathing, No Transmission
    Mixed    // Pathing & Transmission 
}

[Serializable]
public record StudyData
{
    public List<Scenario> scenarios = new();
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
    public List<Vector2> audioPath = new();
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
        public List<Vector2> audioPath = new();
    }
}

public static class JsonData
{
    const string DefaultPath = "StudyData";

    public static void Export(StudyData data, string path = DefaultPath, bool addDateToPath = true)
    {
        var json = JsonUtility.ToJson(data, prettyPrint: Application.isEditor);
        if (addDateToPath) path += DateTime.Now.ToString(" (dd.MM.yyyy-HH.mm)");
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