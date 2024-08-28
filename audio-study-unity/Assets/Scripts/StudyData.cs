using UnityEngine;

public enum AudioConfiguration
{
    Basic, // Transmission, No Pathing
    Pathing, // Pathing, No Transmission
    Mixed // Pathing & Transmission 
}

public struct StudyData
{
    public AudioConfiguration AudioConfiguration;
    public NavigationsForScene[] NavigationTasks;
    public LocalizationsForScene[] LocalizationTasks;
}

public struct Scene
{
    public int SceneID;
}

public struct LocalizationsForScene
{
    public Scene Scene;
    public LocalizeAudioTask[] Tasks;
    public struct LocalizeAudioTask
    {
        public float StartTime;
        public float EndTime;
        public Vector2 AudioPosition;
        public Vector2 UserLocalizationPosition;
    }
}

public struct NavigationsForScene
{
    public Scene Scene;
    public NavigateToAudioTask[] Tasks;
    
    public struct NavigateToAudioTask
    {
        public float StartTime;
        public float EndTime;
        public Vector2 AudioPosition;
        public MetricsFrame[] Metrics;
        
        public struct MetricsFrame
        {
            public float Time;
            public Vector2 Position;
            public Vector2 Rotation;

            public Vector2[] AudioPath;
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