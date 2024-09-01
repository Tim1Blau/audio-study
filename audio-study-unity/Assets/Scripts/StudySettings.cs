using UnityEngine;

public class StudySettings : SingletonBehaviour<StudySettings>
{
    [Header("Task Generation Settings")]
    public int seed = 12345678;

    public int locListenerPositions = 3;
    public int locSourcePositionsPerListenerPos = 3;
    public int navPositions = 10;
    public float locListenerDistances = 10;
    public float locAudioDistances = 0;
    public float navAudioDistances = 5;

    [Header("Utility Parameters")]
    public float foundSourceDistance = 1.0f;
    public float spawnHeight = 0.8f;
    public KeyCode mapKey = KeyCode.E;
    
    void Start() => Study.Initialize();
}