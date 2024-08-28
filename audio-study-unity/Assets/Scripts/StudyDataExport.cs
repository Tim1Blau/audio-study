using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public class StudyDataExport : MonoBehaviour
{
    [MenuItem("AudioStudy/Export", false, 1)]
    static void ExportData()
    {
        if (SceneManager.GetActiveScene()
                .GetRootGameObjects().Select(o => o.GetComponent<AudioPositioner>())
                .FirstOrDefault(x => x != null) is not { } singleton)
        {
            Debug.LogError($"Can't Export Study Data: No {nameof(AudioPositioner)} found.");
            return;
        }

        JsonData.Export(singleton.Data);
    }

    [MenuItem("AudioStudy/ExportExampleData", false, 1)]
    static void ExportExampleData()
    {
        var data = new StudyData();
        JsonData.Export(data);
    }
}

public static class JsonData
{
    const string DefaultPath = "StudyData.json";

    public static void Export(StudyData data, string path = DefaultPath)
    {
        var json = JsonUtility.ToJson(data, prettyPrint: Application.isEditor);
        File.WriteAllText(path, json);
        Debug.Log("Exported Data!");
    }

    public static StudyData? Import(string path = DefaultPath)
    {
        if (!File.Exists(path))
        {
            Debug.LogError($"File not found: {path}");
            return null;
        }

        var json = File.ReadAllText(path);
        return JsonUtility.FromJson<StudyData>(json);
    }
}