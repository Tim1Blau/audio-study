using UnityEngine;

[CreateAssetMenu(menuName = "Audio Study/Scenario Settings")]
public class ScenarioSettings : ScriptableObject
{
    [Header("Navigation")]
    public int navPositions = 8;
    public float navAudioDistances = 7;

    [Header("Localization")]
    public float locListenerDistances = 10;
    public int locListenerPositions = 1;
    public int locAudioPositionsPerListenerPos = 9;
    public float locAudioDistanceFromListener = 5;
}