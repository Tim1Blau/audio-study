using UnityEngine;

public class StudySettings : SingletonBehaviour<StudySettings>
{
    [Header("Task Generation Settings")]
    public int seed = 12345678;

    public int locListenerPositions = 1;
    public int locSourcePositionsPerListenerPos = 1;
    public float locListenerDistances = 10;
    public float locAudioDistances = 4;
    public int navPositions = 1;
    public float navAudioDistances = 7;

    [Header("Utility Parameters")]
    public float foundSourceDistance = 1.0f;
    public float spawnHeight = 1.5f;
    public KeyCode mapKey = KeyCode.E;
    
    void Start() => Study.Initialize();
}