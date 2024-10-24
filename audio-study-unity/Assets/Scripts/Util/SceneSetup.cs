using System;
using System.Linq;
using SteamAudio;
using UnityEditor;
#if UNITY_EDITOR
using System.Collections;
using UnityEditor.SceneManagement;
#endif
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneSetup : MonoBehaviour
{
    [SerializeField] public Mesh mesh;
}

#if UNITY_EDITOR
/// Provides an additional inspector button to <see cref="SceneSetup"/> to quickly setup new scenes with the set mesh.
/// 1. Sets the displayed and collision mesh in the study prefab
/// 2. Exports the SteamAudio Scene
/// 3. Generates SteamAudio Probes
[CustomEditor(typeof(SceneSetup))]
public class SceneSetupEditor : Editor
{
    SceneSetup Config => (SceneSetup)target;

    static void Log(string msg) => Debug.Log("Setting up scene: " + msg);

    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        if (GUILayout.Button("Setup Scene")) SetupScene();
        
        GUILayout.Space(10);
        GUILayout.Label("Debug");
        
        if (GUILayout.Button("Visualize Localization")) VisualizeLocalization();
        if (GUILayout.Button("Visualize Navigation")) VisualizeNavigation();

        if(Application.isPlaying)
            Study.AudioConfig = (AudioConfiguration)EditorGUILayout.EnumPopup("Audio Configuration", Study.AudioConfig);
    }

    void SetupScene()
    {
        try
        {
            Log("START");
            SetMesh();
            ExportScene();
            GenerateProbes();
            serializedObject.ApplyModifiedProperties();
            Log("FINISHED");
        }
        catch (Exception e)
        {
            Log("FAIL");
            Debug.LogException(e);
        }
    }

    void SetMesh()
    {
        if (Config.mesh is not { } mesh)
            throw new Exception("Mesh is not set");

        References.Singleton.roomMeshCollider.sharedMesh = References.Singleton.roomMeshFilter.sharedMesh = mesh;
        EditorUtility.SetDirty(References.Singleton.roomMeshCollider);
        EditorUtility.SetDirty(References.Singleton.roomMeshFilter);
        Log("Set Mesh");
    }

    static void ExportScene()
    {
        var scene = SceneManager.GetActiveScene();

        References.Singleton.steamAudioStaticMesh.asset = CreateAsset(ExportPaths.Scene);
        EditorUtility.SetDirty(References.Singleton.steamAudioStaticMesh);
        Log("Created Scene Asset");
        SteamAudioManager.ExportScene(scene, false);
        Log("Exported Scene Asset");
    }

    static void GenerateProbes()
    {
        var scene = SceneManager.GetActiveScene();

        var probes = References.Singleton.probeBatch;
        probes.asset = CreateAsset(ExportPaths.Probes);
        EditorUtility.SetDirty(probes);
        Log("Created Probe Asset");

        probes.GenerateProbes();
        EditorSceneManager.MarkSceneDirty(scene);
        Log("Generated Probes");

        if (probes.GetNumProbes() == 0)
            throw new Exception("Problem with generated probes: generated 0 probes");

        probes.BeginBake();
        EditorSceneManager.MarkSceneDirty(scene);
        Log("Generated Probes");
    }

    static SerializedData CreateAsset(string path)
    {
        var asset = CreateInstance<SerializedData>();
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }
    
    static void VisualizeNavigation()
    {
        var studySettings = FindObjectOfType<StudySettings>();
        var tasks = studySettings.GenerateNavigationTasks();
        int index = 0;
        foreach (var task in tasks)
        {
            var c = (float)++index / tasks.Count;
            var color = new Color(c, c, c);
            Debug.DrawLine(task.listenerStartPosition.XZ(1), task.audioPosition.XZ(1), color, 3.0f);
        }
        Debug.Log("Visualized Navigation");
    }

    static void VisualizeLocalization()
    {
        var studySettings = FindObjectOfType<StudySettings>();
        var tasks = studySettings.GenerateLocalizationTasks();
        int index = 0;
        foreach (var task in tasks)
        {
            var c = (float)++index / tasks.Count;
            var color = new Color(c, c, c);
            Debug.DrawLine(task.listenerPosition.XZ(1), task.audioPosition.XZ(1), color, 3.0f);
        }
        Debug.Log("Visualized Localization");
    }
}
#endif