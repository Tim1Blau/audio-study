#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public class StudyDataExport : MonoBehaviour
{
    [MenuItem("_Audio Study_/ExportExampleData", false, 1)]
    static void ExportExampleData()
    {
        var data = new StudyData();
        JsonData.Export(data);
    }
}
#endif