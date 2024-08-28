using System;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
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

        JsonData.Export(singleton._data);
    }

    [MenuItem("AudioStudy/ExportExampleData", false, 1)]
    static void ExportExampleData()
    {
        var data = ExampleData();
        JsonData.Export(data);
    }

    static StudyData ExampleData() => new()
    {
        audioConfiguration = AudioConfiguration.Pathing,
        navigationTasks = new[]
        {
            new NavigationsForScene
            {
                scene = new Scene { id = "Scene 1" },
                tasks = new[]
                {
                    new NavigationsForScene.NavigateToAudioTask
                    {
                        startTime = 0.0f,
                        endTime = 5.0f,
                        audioPosition = new Vector2(2, 2),
                        metrics = new[]
                        {
                            new NavigationsForScene.NavigateToAudioTask.MetricsFrame
                            {
                                time = 0.0f,
                                position = new Vector2(0, 0),
                                rotation = new Vector2(0, 0),
                                audioPath = new[]
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
        localizationTasks = new LocalizationsForScene[0]
    };
}

public static class JsonData
{
    const string DefaultPath = "StudyData.json";

    public static void Export(StudyData data, string path = DefaultPath)
    {
        var json = JsonUtility.ToJson(data);
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

public static class XLUtils
{
    const string ExportPath = "ExampleExport1.xlsx";

    static XLColor TitleColor => XLColor.AshGrey;

    static void SetTitle(this IXLCell cell, string title)
    {
        cell.Style.Fill.BackgroundColor = TitleColor;
        cell.Value = title;
    }
    
    static void ExportToExcel(StudyData data)
    {
        using var workbook = new XLWorkbook();
        var x = workbook.Worksheets.Add("Sample Sheet");
        WriteMeta();
        WriteNavigationTask();

        workbook.SaveAs(ExportPath);

        void WriteMeta()
        {
            x.Cell(1, 1).SetTitle("Date");
            x.Cell(2, 1).Value = DateTime.Now.ToShortDateString();
            x.Cell(3, 1).Value = DateTime.Now.ToShortTimeString();

            x.Cell(1, 2).SetTitle("AudioConfiguration");
            x.Cell(2, 2).Value = data.audioConfiguration.ToString();
        }

        void WriteNavigationTask()
        {
            const int cNavigationTasksStart = 4;
            const int cScene = cNavigationTasksStart;
            const int cTask = cScene + 1;
            const int cStartTime = cScene + 1;
            const int cEndTime = cStartTime + 1;
            const int cAudioPositionX = cEndTime + 1;
            const int cAudioPositionY = cAudioPositionX + 1;
            var r = 1;
            var row = x.Row(r++);
            row.Cell(cNavigationTasksStart).SetTitle("Navigation");

            row = x.Row(r++);
            row.Cell(cScene).SetTitle("Scene");
            row.Cell(cTask).SetTitle("Task");
            row.Cell(cStartTime).SetTitle("StartTime");
            row.Cell(cEndTime).SetTitle("EndTime");
            row.Cell(cAudioPositionX).SetTitle("AudioPosX");
            row.Cell(cAudioPositionY).SetTitle("AudioPosY");

            foreach (var navigationsForScene in data.navigationTasks)
            {
                var taskIndex = 0;
                foreach (var task in navigationsForScene.tasks)
                {
                    row = x.Row(r++);

                    row.Cell(cScene).Value = navigationsForScene.scene.id;
                    row.Cell(cTask).Value = taskIndex++;
                    row.Cell(cStartTime).Value = task.startTime;
                    row.Cell(cEndTime).Value = task.endTime;
                    row.Cell(cAudioPositionX).Value = task.audioPosition.x;
                    row.Cell(cAudioPositionY).Value = task.audioPosition.y;

                    foreach (var metricsFrame in task.metrics)
                    {
                    }
                }
            }
        }
    }
}