using System;
using SteamAudio;
using UnityEditor;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(SteamAudioStaticMesh), typeof(StudySettings))]
public class SceneSetup : MonoBehaviour
{
    [SerializeField] public Mesh mesh;
    [SerializeField] public MeshFilter geometryObject;
}

#if UNITY_EDITOR
[CustomEditor(typeof(SceneSetup))]
public class SceneSetupEditor : Editor
{
    SceneSetup Config => (SceneSetup)target;
    
    static void Err(string reason) => Debug.LogError("Failed to setup scene: " + reason);
    static void Log(string msg) => Debug.Log("Setting up scene: " + msg);
    
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        if (GUILayout.Button("Setup Scene"))
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
                Err(e.Message);
            }
        }
    }

    void SetMesh()
    {
        if (Config.mesh is not { } mesh)
        {
            Err("Mesh is not set");
            return;
        }

        if (Config.geometryObject is not { } geo)
        {
            Err("GeometryObject is not set");
            return;
        }

        if (geo.GetComponent<MeshCollider>() is not { } collider)
        {
            Err("GeometryObject has no MeshCollider");
            return;
        }

        if (geo.GetComponent<MeshFilter>() is not { } filter)
        {
            Err("GeometryObject has no MeshCollider");
            return;
        }

        collider.sharedMesh = filter.sharedMesh = mesh;
        EditorUtility.SetDirty(collider);
        EditorUtility.SetDirty(filter);
        Log("Set Mesh");
    }

    void ExportScene()
    {
        var scene = SceneManager.GetActiveScene();

        if (Config.GetComponent<SteamAudioStaticMesh>() is not { } steamMesh)
            throw new Exception("SteamAudioStaticMesh is not set");
        steamMesh.asset = CreateAsset(ExportPaths.Scene);
        EditorUtility.SetDirty(steamMesh);
        Log("Created Scene Asset");
        SteamAudioManager.ExportScene(scene, false);
        Log("Exported Scene Asset");
    }

    void GenerateProbes()
    {
        var scene = SceneManager.GetActiveScene();

        var probes = References.ProbeBatch;
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

    SerializedData CreateAsset(string path)
    {
        var asset = CreateInstance<SerializedData>();
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }
}
#endif