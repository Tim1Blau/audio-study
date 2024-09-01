using System;
using System.Collections.Generic;
using System.IO;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.Serialization;

public enum AudioConfiguration
{
    Basic = 0, // Transmission, No Pathing
    Pathing = 1, // Pathing, No Transmission
    Mixed = 2 // Pathing & Transmission 
}

[Serializable]
public record StudyData
{
    public List<NavigationScenario> navigationScenarios;
    public List<LocalizationScenario> localizationScenarios;
}

[Serializable]
public record Scene
{
    public string name;
}

[Serializable]
public record LocalizationScenario
{
    public AudioConfiguration audioConfiguration;
    public Scene scene;
    public List<Task> tasks;

    [Serializable]
    public record Task
    {
        public Vector2 listenerPosition;
        public Vector2 audioPosition;

        public float startTime;
        public float endTime;
        public Vector2 guessedPosition;
        public List<Vector2> audioPath;
    }
}

[Serializable]
public record NavigationScenario
{
    public AudioConfiguration audioConfiguration;
    public Scene scene;
    public List<Task> tasks;

    [Serializable]
    public record Task
    {
        public Vector2 listenerStartPosition;
        public Vector2 audioPosition;

        public float startTime;
        public float endTime;
        public List<MetricsFrame> metrics;

        [Serializable]
        public record MetricsFrame
        {
            public float time;
            public Vector2 position;
            public Vector2 rotation;

            public List<Vector2> audioPath;
            // CALCULATED: Velocity, LastAudioPath, Efficiency
        }
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