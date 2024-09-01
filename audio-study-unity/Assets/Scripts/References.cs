using System;
using System.Collections.Generic;
using SteamAudio;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using Vector3 = UnityEngine.Vector3;

public static class ExportPaths
{
    public static string Scene => SceneManager.GetActiveScene().path[..^".asset".Length] + "_Export.asset";
    public static string Probes => SceneManager.GetActiveScene().path[..^".asset".Length] + "_Probes.asset";
}

public class References : SingletonBehaviour<References>
{
    [SerializeField] SteamAudioSource steamAudioSource;
    [SerializeField] AudioSource audioSource;
    [SerializeField] SteamAudioProbeBatch probeBatch;
    [SerializeField] SteamAudioListener listener;
    [SerializeField] Player player;

    public static SteamAudioSource SteamAudioSource => Singleton.steamAudioSource;
    public static AudioSource AudioSource => Singleton.audioSource;
    public static SteamAudioProbeBatch ProbeBatch => Singleton.probeBatch;
    public static SteamAudioListener Listener => Singleton.listener;
    public static Player Player => Singleton.player;

    public static float Now => Time.realtimeSinceStartup;

    public static Vector3 ListenerPosition
    {
        get => Listener.transform.position;
        set
        {
            Listener.transform.position = value;
            Physics.SyncTransforms();
        }
    }

    public static Vector3 AudioPosition
    {
        get => SteamAudioSource.transform.position;
        set => SteamAudioSource.transform.position = value;
    }

    public static bool Paused
    {
        get => !Player.canMove;
        set
        {
            if (Paused == value) return;
            Player.canMove = !value;
            if (Paused) AudioSource.Pause();
            else AudioSource.UnPause();
        }
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        var hasErrors = false;
        foreach (var error in Validate())
        {
            Debug.LogError(error);
            hasErrors = true;
        }

        if (hasErrors) return;

        steamAudioSource.pathingProbeBatch = probeBatch;
        return;

        IEnumerable<string> Validate()
        {
            if (steamAudioSource is null) yield return "Audio Source is null";
            if (probeBatch is null) yield return "Probe Batch is null";
            else if (probeBatch.asset is null &&
                     (probeBatch.asset = AssetDatabase.LoadAssetAtPath<SerializedData>(ExportPaths.Probes)) is null)
                yield return "Probe Batch asset is null, you need to bake the probes";
            else if (probeBatch.GetNumProbes() == 0)
                yield return "Probe Batch has no probes, try to re-bake the probes";
            if (listener is null) yield return "Listener is null";
        }
    }
#endif       
}

public class SingletonBehaviour<T> : MonoBehaviour where T : SingletonBehaviour<T>
{
    public static T Singleton { get; private set; }

    protected void Awake()
    {
        Singleton = this as T;
    }
}