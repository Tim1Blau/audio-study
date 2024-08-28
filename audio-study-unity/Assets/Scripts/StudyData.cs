using System;
using UnityEngine;

public enum AudioConfiguration
{
    Basic, // Transmission, No Pathing
    Pathing, // Pathing, No Transmission
    Mixed // Pathing & Transmission 
}

[Serializable]
public struct StudyData
{
    public AudioConfiguration audioConfiguration;
    public NavigationsForScene[] navigationTasks;
    public LocalizationsForScene[] localizationTasks;
}

[Serializable]
public struct Scene
{
    public string id;
}

[Serializable]
public struct LocalizationsForScene
{
    public Scene scene;
    public LocalizeAudioTask[] tasks;

    [Serializable]
    public struct LocalizeAudioTask
    {
        public float startTime;
        public float endTime;
        public Vector2 audioPosition;
        public Vector2 userLocalizationPosition;
    }
}

[Serializable]
public struct NavigationsForScene
{
    public Scene scene;
    public NavigateToAudioTask[] tasks;

    [Serializable]
    public struct NavigateToAudioTask
    {
        public float startTime;
        public float endTime;
        public Vector2 audioPosition;
        public MetricsFrame[] metrics;

        [Serializable]
        public struct MetricsFrame
        {
            public float time;
            public Vector2 position;
            public Vector2 rotation;

            public Vector2[] audioPath;
            // CALCULATED: Velocity, LastAudioPath, Efficiency
        }
    }
}

// Compiler fix for records
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}