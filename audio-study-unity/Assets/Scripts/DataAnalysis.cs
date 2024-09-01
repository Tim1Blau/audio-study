#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using ClosedXML.Excel;
using UnityEditor;
using UnityEngine;

public class DataAnalysis : MonoBehaviour
{
    public static IEnumerable<(NavigationScenario.Task.MetricsFrame frame, float Efficiency)> TaskEfficiency(NavigationScenario.Task task)
    {
        if (task.metrics.Count == 0) yield break;
        var lastFrame = task.metrics.First();
        yield return (lastFrame, Efficiency: 0f); // initial efficiency is 0%
        foreach (var frame in task.metrics.Skip(1))
        {
            yield return (
                frame,
                Utils.Efficiency(
                    moveDir: frame.position - lastFrame.position,
                    optimalDir: lastFrame.audioPath[^2] - lastFrame.audioPath[^1]
                )
            );
            lastFrame = frame;
        }
    }

    public static float PathLength(List<Vector2> path)
    {
        if(path.Count == 0) return 0;
        var last = path.First();
        var distance = 0.0f;
        foreach (var pos in path)
        {
            distance += Vector2.Distance(last, pos);
            last = pos;
        }
        return distance;
    }
    
    [MenuItem("_Audio Study_/AnalyzeDemoToExcel", false, 1)]
    public static void AnalyzeDemoToExcel()
    {
        if (JsonData.Import("DemoStudyData.json") is not {} data)
        {
            Debug.LogWarning("DemoStudyData.json could not be found");
            return;
        }
        var task = data.navigationScenarios.First().tasks.First(); // assume
        var efficiency = TaskEfficiency(task);
        
        
        using var workbook = new XLWorkbook();
        var x = workbook.Worksheets.Add("Sample Sheet");
        var row = x.Row(1);
        var c = 0;
        row.Cell(++c).Value = "Time";
        row.Cell(++c).Value = "Efficiency";
        row.Cell(++c).Value = "NumberPaths";
        row.Cell(++c).Value = "OptimalDistance";
        foreach (var d in efficiency)
        {
            c = 0;
            row = row.RowBelow();
            row.Cell(++c).Value = d.frame.time;
            row.Cell(++c).Value = d.Efficiency;
            row.Cell(++c).Value = d.frame.audioPath.Count;
            row.Cell(++c).Value = PathLength(d.frame.audioPath);
        }
        workbook.SaveAs("DemoStudyData_Efficiency.xlsx");
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
            // x.Cell(2, 2).Value = data.audioConfiguration.ToString();
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

            foreach (var navigationsForScene in data.navigationScenarios)
            {
                var taskIndex = 0;
                foreach (var task in navigationsForScene.tasks)
                {
                    row = x.Row(r++);

                    row.Cell(cScene).Value = navigationsForScene.scene.name;
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
#endif