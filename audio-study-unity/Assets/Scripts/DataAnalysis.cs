#if UNITY_EDITOR
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// ReSharper disable MemberCanBePrivate.Global

namespace Analysis
{
    [Serializable]
    public record AnalyzedData
    {
        public QuestionData answers = new();
        public List<AnalyzedScenario> scenarios = new();

        public static IEnumerator From(StudyData data, Action<AnalyzedData> result)
        {
            var res = new AnalyzedData { answers = data.answers };
            foreach (var scenario in data.scenarios)
            {
                yield return AnalyzedScenario.From(scenario, res.scenarios.Add);
            }

            result.Invoke(res);
        }
    }

    [Serializable]
    public record AnalyzedScenario
    {
        public AudioConfiguration audioConfiguration;
        public string scene = "";
        public List<NavigationTask> navigationTasks = new();
        public List<AnalyzedLocalizationTask> localizationTasks = new();

        public static IEnumerator From(Scenario scenario, Action<AnalyzedScenario> result)
        {
            SceneManager.LoadScene(scenario.scene);
            yield return new WaitForNextFrameUnit();
            Study.AudioConfig = AudioConfiguration.Pathing;
            yield return new WaitForNextFrameUnit();

            var aScenario = new AnalyzedScenario
            {
                audioConfiguration = scenario.audioConfiguration,
                scene = scenario.scene,
                navigationTasks = scenario.navigationTasks,
            };

            int i = 0;
            foreach (var t in scenario.localizationTasks)
            {
                var playerToGuess = t.guessedPosition - t.listenerPosition;
                var playerToActual = t.audioPosition - t.listenerPosition;
                var aLoc = new AnalyzedLocalizationTask
                {
                    listenerPosition = t.listenerPosition,
                    audioPosition = t.audioPosition,
                    startTime = t.startTime,
                    endTime = t.endTime,
                    guessedPosition = t.guessedPosition,
                    audioPath = t.audioPath,

                    guessStraightDistance = playerToGuess.magnitude,
                    actualStraightDistance = playerToActual.magnitude,
                    guessAngleDifference = Vector2.Angle(playerToActual, playerToGuess)
                };
                UI.Singleton.screenText.text = $"task {i + 1} / {scenario.localizationTasks.Count}";

                yield return Path(t.guessedPosition, t.listenerPosition, r => aLoc.guessPathingDistance = r);
                yield return Path(t.audioPosition, t.listenerPosition, r => aLoc.actualPathingDistance = r);
                yield return Path(t.guessedPosition, t.audioPosition, r => aLoc.guessToActualPathingDistance = r);
                aScenario.localizationTasks.Add(aLoc);
                i++;
            }

            UI.Singleton.screenText.text = "Finished scenario";

            result.Invoke(aScenario);
            yield break;

            IEnumerator Path(Vector2 from, Vector2 to, Action<float> result)
            {
                References.AudioPosition = from;
                References.PlayerPosition = to;
                yield return new WaitForNextFrameUnit();
                yield return PathingRecorder.WaitForPathingData(r => result.Invoke(DataAnalysis.PathLength(r)));
            }
        }
    }

    [Serializable]
    public record AnalyzedLocalizationTask
    {
        public Vector2 listenerPosition;
        public Vector2 audioPosition;
        public float startTime = -1;
        public float endTime = -1;
        public Vector2 guessedPosition = Vector2.zero;
        public AudioPath audioPath = new();

        /* Analysis */

        public float guessStraightDistance;
        public float actualStraightDistance;

        public float guessPathingDistance;
        public float actualPathingDistance;
        public float guessToActualPathingDistance;

        public float guessAngleDifference;
    }


    public class DataAnalysis : EditorWindow
    {
        private string? _path;
        private StudyData? _data;

        private string? _multiPath;
        private StudyData[]? _multiData;

        private bool singleMode;

        [NonSerialized] private bool analyzing = false;

        [MenuItem("Audio Study/Analysis Window")]
        public static void ShowWindow() => GetWindow<DataAnalysis>("Analysis");

        void OnGUI()
        {
            GUILayout.Label("Analysis", EditorStyles.boldLabel);
            singleMode = GUILayout.Toggle(singleMode, "Single Mode");
            if (singleMode)
            {
                if (_path is not null) GUILayout.Label("Selected File: " + Path.GetFileName(_path));
                else GUILayout.Label("No StudyData Loaded");
                
                if (GUILayout.Button("Select File"))
                {
                    var p = EditorUtility.OpenFilePanel("Select StudyData", "", "json");
                    if (!string.IsNullOrEmpty(p))
                    {
                        var d = JsonData.Import(p);
                        if (d is not null)
                        {
                            this._path = p;
                            this._data = d;
                        }
                    }
                }
            }
            else
            {
                if (_multiPath is not null) GUILayout.Label("Selected File: " + Path.GetFileName(_multiPath));
                else GUILayout.Label("No StudyData Loaded");

                if (GUILayout.Button("Select File"))
                {
                    var p = EditorUtility.OpenFolderPanel("Select StudyData", "", "");
                    if (!string.IsNullOrEmpty(p))
                    {
                        try
                        {
                            var files = Directory.GetFiles(p, "StudyData*.json");
                            var d = files.Select(JsonData.Import).ToArray();
                            if (d.All(x => x is not null))
                            {
                                _multiPath = p;
                                _multiData = d;
                            }
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                        }
                    }
                }
            }

            StudyData[] data;
            if (singleMode)
            {
                if (_data is null) return;
                data = new[] { _data };
            }
            else
            {
                if(_multiData is null) return;
                data = _multiData;
            }
            
            GUILayout.Space(10);


            if (_data is null) return;


            GUILayout.Space(10);

            if (GUILayout.Button("Debug"))
            {
                Debug.Log("DEBUG DATA");
            }

            if (analyzing)
            {
                GUILayout.Button("Analyzing...");
            }
            else if (GUILayout.Button("Localization Performance"))
            {
                analyzing = true;
                EditorApplication.EnterPlaymode();
                SceneManager.LoadScene(_data.scenarios.First().scene);
                var co = FindObjectOfType<Study>();
                foreach (var studyData in data)
                {
                    co.StartCoroutine(AnalyzedData.From(studyData, res =>
                    {
                        analyzing = false;
                    }));
                }
            }

            if (GUILayout.Button("Normalisis"))
            {
                // 5.2 can still happen under normal circumstances, analyze fr later
                var speed = _data.scenarios.SelectMany(s => s.navigationTasks).SelectMany(nav =>
                {
                    var first = nav.frames.FirstOrDefault();
                    var prevPosition = first?.position ?? Vector2.zero;
                    var prevTime = first?.time ?? 0f;
                    return nav.frames.Select(frame =>
                    {
                        var res = Vector2.Distance(frame.position, prevPosition) / (frame.time - prevTime);
                        prevPosition = frame.position;
                        prevTime = frame.time;
                        return res;
                    });
                }).ToList();

                speed.Sort();
                Debug.Log(speed);
            }
        }

        [MenuItem("Audio Study/Export Example Data", false, 1)]
        public static void ExportExampleData()
        {
            JsonData.Export(new StudyData());
        }

        [MenuItem("Audio Study/Analyze Duration", false, 1)]
        public static void Duration()
        {
            var path = EditorUtility.OpenFilePanel("Select StudyData", "", "json");
            var data = JsonData.Import(path) ?? throw new Exception("Failed to load data");

            Debug.Log($"Data {path}\n" + string.Join("\n--------------\n",
                data.scenarios.Select(s =>
                    $"Scenario {s.scene}-{s.audioConfiguration}: {s.Duration().Format()}"
                    + $"\n Navigation   {s.navigationTasks.Duration().Format()} for {s.navigationTasks.Count} positions | avg nav time:   {s.navigationTasks.Average(t => t.Duration()).Format()}"
                    + $"\n Localization {s.localizationTasks.Duration().Format()} for {s.localizationTasks.Count} positions | avg guess time: {s.localizationTasks.Average(t => t.Duration()).Format()}"
                ))
            );
        }

        [MenuItem("Audio Study/Normalize", false, 1)]
        public static void Normalize()
        {
            var path = EditorUtility.OpenFilePanel("Select StudyData", "", "json");
            var data = JsonData.Import(path) ?? throw new Exception("Failed to load data");
            data.Normalize();
            JsonData.Export(data, path[..^5] + " Norm");
        }

        public static float PathLength(AudioPath path)
        {
            if (path.points.Count == 0) return 0;
            var last = path.points.First();
            var distance = 0.0f;
            foreach (var pos in path.points)
            {
                distance += Vector2.Distance(last, pos);
                last = pos;
            }

            return distance;
        }
    }

    public record DataBatch(IReadOnlyList<StudyData> Data);

    internal static class TaskDuration
    {
        public static TimeSpan Duration(this LocalizationTask task) =>
            TimeSpan.FromSeconds(task.endTime - task.startTime);

        public static TimeSpan Duration(this NavigationTask task) =>
            TimeSpan.FromSeconds(task.endTime - task.startTime);

        public static TimeSpan Duration(this Scenario scenario) =>
            scenario.navigationTasks.Duration() + scenario.localizationTasks.Duration();
        //
        // public static TimeSpan StartTime(this Scenario scenario) =>
        //     scenario.navigationTasks.FirstOrDefault(t => t.Started())?.startTime ??
        //     scenario.localizationTasks.FirstOrDefault(t => t.Started())?.startTime ?? 0.0f;
        //
        // public static TimeSpan EndTime(this Scenario scenario) =>
        //     scenario.localizationTasks.LastOrDefault(t => t.Completed())?.endTime ??
        //     scenario.navigationTasks.LastOrDefault(t => t.Completed())?.endTime ?? 0.0f;

        public static TimeSpan Duration(this List<NavigationTask> tasks) =>
            tasks.Count == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(tasks.Last().endTime - tasks.First().startTime);

        public static TimeSpan Duration(this List<LocalizationTask> tasks) =>
            tasks.Count == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(tasks.Last().endTime - tasks.First().startTime);

        public static TimeSpan Average<TSource>(this IEnumerable<TSource> source, Func<TSource, TimeSpan> func) =>
            TimeSpan.FromSeconds(source.Average(s => func(s).Seconds));

        public static string Format(this TimeSpan timeSpan) => $"[{timeSpan:mm\\:ss}]";
    }

    internal static class TaskEfficiency
    {
        public static IEnumerable<(NavigationTask.MetricsFrame frame, float Efficiency)> Efficiency(
            this NavigationTask task)
        {
            if (task.frames.Count == 0) yield break;
            var lastFrame = task.frames.First();
            yield return (lastFrame, Efficiency: 0f); // initial efficiency is 0%
            foreach (var frame in task.frames.Skip(1))
            {
                yield return (
                    frame,
                    Utils.Efficiency(
                        moveDir: frame.position - lastFrame.position,
                        optimalDir: lastFrame.audioPath.points[^2] - lastFrame.audioPath.points[^1]
                    )
                );
                lastFrame = frame;
            }
        }
    }

    internal static class Penality
    {
        public static void Normalize(this StudyData data)
        {
            var penalty = 0f;
            (int normal, int exploit) count = (0, 0);
            foreach (var scenario in data.scenarios)
            {
                foreach (var navigation in scenario.navigationTasks)
                {
                    navigation.startTime += penalty;
                    var first = navigation.frames.FirstOrDefault();
                    var prevPosition = first?.position ?? Vector2.zero;
                    var prevTime = first?.time ?? 0f;
                    foreach (var frame in navigation.frames)
                    {
                        var deltaTime = frame.time - prevTime;
                        prevTime = frame.time;
                        frame.time += penalty;
                        var distance = Vector2.Distance(prevPosition, frame.position);
                        if (distance > (Player.movementSpeed + 1f) * deltaTime)
                        {
                            penalty += distance / Player.movementSpeed;
                            count.exploit++;
                        }
                        else
                        {
                            if (distance > 0.1f) count.normal++;
                        }

                        prevPosition = frame.position;
                    }

                    navigation.endTime += penalty;
                }

                foreach (var localization in scenario.localizationTasks)
                {
                    localization.startTime += penalty;
                    localization.endTime += penalty;
                }
            }

            Debug.Log($"Total penalty: {penalty}s");
            Debug.Log($"Abuse rate: {(float)count.exploit / (count.normal + count.exploit) * 100f}%");
        }
    }


    public static class XLUtils
    {
        const string ExportPath = "ExampleExport1.xlsx";

        [MenuItem("Audio Study/AnalyzeDemoToExcel", false, 1)]
        public static void AnalyzeDemoToExcel()
        {
            if (JsonData.Import("DemoStudyData.json") is not { } data)
            {
                Debug.LogWarning("DemoStudyData.json could not be found");
                return;
            }

            var task = data.scenarios.First().navigationTasks.First(); // assume
            var efficiency = task.Efficiency();


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
                row.Cell(++c).Value = d.frame.audioPath.points.Count;
                row.Cell(++c).Value = DataAnalysis.PathLength(d.frame.audioPath);
            }

            workbook.SaveAs("DemoStudyData_Efficiency.xlsx");
        }

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

                foreach (var scenario in data.scenarios)
                {
                    var taskIndex = 0;
                    foreach (var task in scenario.navigationTasks)
                    {
                        row = x.Row(r++);

                        row.Cell(cScene).Value = scenario.scene;
                        row.Cell(cTask).Value = taskIndex++;
                        row.Cell(cStartTime).Value = task.startTime;
                        row.Cell(cEndTime).Value = task.endTime;
                        row.Cell(cAudioPositionX).Value = task.audioPosition.x;
                        row.Cell(cAudioPositionY).Value = task.audioPosition.y;

                        foreach (var metricsFrame in task.frames)
                        {
                        }
                    }
                }
            }
        }
    }
}
#endif