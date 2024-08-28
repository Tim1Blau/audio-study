using System;
using System.Linq;
using ClosedXML.Excel;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public class StudyDataExport : MonoBehaviour
{
    static void Export(ref StudyData data)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Sample Sheet");
        worksheet.Cell("A1").Value = "Hello World!";
        worksheet.Cell("A2").FormulaA1 = "=MID(A1, 7, 5)";
        workbook.SaveAs("HelloWorld.xlsx");
    }

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

        Export(ref singleton._data);
    }

    [MenuItem("AudioStudy/ExportExampleData", false, 1)]
    static void ExportExampleData()
    {
        var data = ExampleData();
        Export(ref data);
    }

    static StudyData ExampleData() => new()
    {
        AudioConfiguration = AudioConfiguration.Pathing,
        NavigationTasks = new[]
        {
            new NavigationsForScene
            {
                Scene = new Scene { SceneID = 1 },
                Tasks = new[]
                {
                    new NavigationsForScene.NavigateToAudioTask
                    {
                        StartTime = 0.0f,
                        EndTime = 5.0f,
                        AudioPosition = new Vector2(2, 2),
                        Metrics = new[]
                        {
                            new NavigationsForScene.NavigateToAudioTask.MetricsFrame
                            {
                                Time = 0.0f,
                                Position = new Vector2(0, 0),
                                Rotation = new Vector2(0, 0),
                                AudioPath = new[]
                                {
                                    new Vector2(0, 0),
                                    new Vector2(0, 1)
                                },
                            }
                        }
                    }
                }
            }
        },
        LocalizationTasks = new LocalizationsForScene[0]
    };
}