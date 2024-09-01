#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public class StudyDataExport : MonoBehaviour
{
    [MenuItem("_Audio Study_/Export", false, 1)]
    static void ExportData()
    {
        if (SceneManager.GetActiveScene()
                .GetRootGameObjects().Select(o => o.GetComponent<Study>())
                .FirstOrDefault(x => x != null) is not { } singleton)
        {
            Debug.LogError($"Can't Export Study Data: No {nameof(Study)} found.");
            return;
        }

        JsonData.Export(singleton.data);
    }

    [MenuItem("_Audio Study_/ExportExampleData", false, 1)]
    static void ExportExampleData()
    {
        var data = new StudyData();
        JsonData.Export(data);
    }
}
#endif