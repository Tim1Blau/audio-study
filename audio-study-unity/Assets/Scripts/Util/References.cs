using System;
using System.Collections.Generic;
using SteamAudio;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Scene = UnityEngine.SceneManagement.Scene;
using Vector3 = UnityEngine.Vector3;

public static class ExportPaths
{
    public static string Scene => SceneManager.GetActiveScene().ExportPath("_Scene.asset");
    public static string Probes => SceneManager.GetActiveScene().ExportPath("_Probes.asset");

    static string ExportPath(this Scene scene, string ending) =>
        string.Join(null,scene.path[..^(".asset".Length + scene.name.Length)], "Export/", scene.name, ending);
}

public class References : SingletonBehaviour<References>
{
    [SerializeField] public SteamAudioSource steamAudioSource;
    [SerializeField] public AudioSource audioSource;
    [SerializeField] public SteamAudioProbeBatch probeBatch;
    [SerializeField] public SteamAudioListener listener;
    [SerializeField] public Player player;
    [SerializeField] public MeshFilter roomMeshFilter;
    [SerializeField] public MeshCollider roomMeshCollider;
    [SerializeField] public SteamAudioStaticMesh steamAudioStaticMesh;

    public static Player Player => Singleton.player;

    public static float Now => Time.realtimeSinceStartup;

    public static Vector3 PlayerPosition
    {
        get => Singleton.player.transform.position;
        set
        {
            Singleton.player.transform.position = value;
            Physics.SyncTransforms();
        }
    }

    public static Vector3 AudioPosition
    {
        get => Singleton.steamAudioSource.transform.position;
        set => Singleton.steamAudioSource.transform.position = value;
    }

    public static bool Paused
    {
        get => !Player.canMove;
        set
        {
            if (Paused == value) return;
            Player.canMove = !value;
            if (Paused) Singleton.audioSource.Pause();
            else Singleton.audioSource.UnPause();
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
    static T _singleton;

    public static T Singleton
    {
        get
        {
            if (_singleton == null)
                _singleton = FindObjectOfType<T>() ??
                             throw new Exception("There is no References object in the scene.");
            return _singleton;
        }
        private set => _singleton = value;
    }

    protected void Awake()
    {
        Singleton = this as T;
    }
}