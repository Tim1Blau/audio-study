using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public enum AudioConfiguration
{
    Basic, // Transmission, No Pathing
    Pathing, // Pathing, No Transmission
    Mixed // Pathing & Transmission 
}

[Serializable]
public record StudyData
{
    public AudioConfiguration audioConfiguration;
    public List<NavigationScenario> navigationScenarios;
    [FormerlySerializedAs("localizationTasks")] public List<LocalizationScenario> localizationScenarios;
}

[Serializable]
public record Scene
{
    public string name;
}

[Serializable]
public record LocalizationScenario
{
    public Scene scene;
    public List<Task> tasks;

    [Serializable]
    public record Task
    {
        public float startTime;
        public float endTime;
        public Vector2 audioPosition;
        public Vector2 userLocalizationPosition;
    }
}

[Serializable]
public record NavigationScenario
{
    public Scene scene;
    public List<Task> tasks;

    [Serializable]
    public record Task
    {
        public float startTime;
        public float endTime;
        public Vector2 audioPosition;
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