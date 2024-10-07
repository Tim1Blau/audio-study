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
using UnityEngine.Serialization;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

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

                    playerToGuessStraight = new AudioPath
                        { isOccluded = true, points = { t.listenerPosition, t.guessedPosition } },
                    playerToAudioStraight = new AudioPath
                        { isOccluded = true, points = { t.listenerPosition, t.audioPosition } },
                    guessAngleDifference = Vector2.Angle(playerToActual, playerToGuess)
                };
                UI.Singleton.screenText.text = $"task {i + 1} / {scenario.localizationTasks.Count}";
                aScenario.localizationTasks.Add(aLoc);

                yield return Path(t.guessedPosition, t.listenerPosition, r => aLoc.playerToGuessPathing = r);
                yield return Path(t.audioPosition, t.listenerPosition, r => aLoc.playerToAudioPathing = r);
                i++;
            }

            foreach (var t in aScenario.localizationTasks)
            {
                yield return Path(t.guessedPosition, t.audioPosition, r => t.guessToAudioPathing = r);
            }

            UI.Singleton.screenText.text = "Finished scenario";

            result.Invoke(aScenario);
            yield break;

            IEnumerator Path(Vector2 audio, Vector2 listener, Action<AudioPath> result)
            {
                References.AudioPosition = audio;
                References.PlayerPosition = listener;
                yield return new WaitForNextFrameUnit();
                var res = new AudioPath() { isOccluded = true };
                var start = Time.realtimeSinceStartup;
                yield return PathingRecorder.WaitForPathingData(r => res = r);
                while (res.isOccluded && start + 0.5f > Time.realtimeSinceStartup)
                {
                    UI.Singleton.screenText.text = "Occluded, trying again";
                    yield return PathingRecorder.WaitForPathingData(r => res = r);
                }

                if (res.isOccluded)
                {
                    Debug.LogWarning("Still Occluded after retries");
                }

                result.Invoke(res);
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

        public AudioPath playerToGuessStraight;
        public AudioPath playerToAudioStraight;

        public AudioPath playerToGuessPathing;
        public AudioPath playerToAudioPathing;
        public AudioPath guessToAudioPathing;

        public float guessAngleDifference;
    }

    public class TempPin : MonoBehaviour
    {
        public static readonly List<GameObject> Objects = new();
        private AudioPath[] _pathsToDraw = Array.Empty<AudioPath>();
        private Color _color;
        public static bool showPrimaryPath;

        void Update()
        {
            if (showPrimaryPath)
            {
                foreach (var audioPath in _pathsToDraw.Take(1)) DrawPath(audioPath, _color, Time.deltaTime);
            }
            else
            {
                foreach (var audioPath in _pathsToDraw.Skip(1)) DrawPath(audioPath, _color, Time.deltaTime);
            }
        }

        public static void Create(Vector2 position, Color color, float size, AudioPath[] paths)
        {
            var go = new GameObject("TempPin");
            var pin = go.AddComponent<TempPin>();
            pin._pathsToDraw = paths;
            pin._color = color;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Resources.Load<Sprite>("Location");
            sr.color = color;
            go.transform.position = position.XZ(0.8f);
            go.transform.rotation = Quaternion.Euler(90, 0, 0);
            go.transform.localScale = new Vector3(size, size, size);
            Objects.Add(go);
            for (var i = 0; i < paths.Length; i++)
            {
                var curr = paths[i];
                var offset = new Vector2(Random.Range(-0.1f, 0.1f), Random.Range(-0.1f, 0.1f));
                paths[i] = curr with
                {
                    points = curr.points.Select(p => p + offset).ToList()
                };
            }
        }

        public static void Clear()
        {
            foreach (var gameObject in Objects)
            {
                Object.Destroy(gameObject);
            }

            Objects.Clear();
        }

        static void DrawPath(AudioPath path, Color color, float duration)
        {
            var pre = path.points.FirstOrDefault();
            foreach (var next in path.points.Skip(1))
            {
                const float y = 3.0f;
                Debug.DrawLine(pre.XZ(y), next.XZ(y), color, duration, true);
                pre = next;
            }
        }
    }

    public class DataAnalysis : EditorWindow
    {
        private string? _folderPath;
        private StudyData[]? _data;


        [NonSerialized] private bool _analyzing;
        private bool _shouldAnalyze;
        [NonSerialized] private List<AnalyzedData>? _analyzedData;

        private bool _visualizeLocalization;
        private int _visualizeSceneIndex;
        private int _visualizeTaskIndex;
        private static readonly string[] SceneChoices = { "Scene 1", "Scene 2", "Scene 3" };
        private bool _shouldUpdateVisualization = true;

        [MenuItem("Audio Study/Analysis Window")]
        public static void ShowWindow() => GetWindow<DataAnalysis>("Analysis");

        void OnGUI()
        {
            GUILayout.Label("Analysis", EditorStyles.boldLabel);
            FileSelection();
            GUILayout.Space(10);

            if (_data is null) return;
            if (_data.Length == 0) throw new Exception("data is empty");

            if (_analyzing)
            {
                if (Application.isPlaying)
                {
                    GUILayout.Label("Analyzing...");
                    return;
                }

                _analyzing = false;
            }

            if (_analyzedData is null)
            {
                if (!_shouldAnalyze)
                {
                    if (GUILayout.Button("Start Analysis"))
                    {
                        EditorApplication.EnterPlaymode();
                        _shouldAnalyze = true;
                    }

                    return;
                }

                if (!Application.isPlaying)
                {
                    GUILayout.Label("Start Playmode to analyze");
                    return;
                }

                TempPin.Clear();
                _analyzing = true;
                _shouldAnalyze = false;
                SceneManager.LoadScene(_data.First().scenarios.First().scene);
                var co = FindObjectOfType<Study>();

                _analyzedData = new();
                co.StartCoroutine(AnalyzeAll());


                return;
            }

            IEnumerator AnalyzeAll()
            {
                foreach (var studyData in _data)
                {
                    yield return AnalyzedData.From(studyData, res => _analyzedData.Add(res));
                    if (_analyzedData.Count == _data.Length)
                    {
                        _analyzing = false;
                        References.PlayerAndAudioPaused = true;
                        this.Repaint();
                    }
                }
            }

            _visualizeLocalization = GUILayout.Toggle(_visualizeLocalization, "Visualize Localization");
            if (_visualizeLocalization)
            {
                var newSceneIndex = GUILayout.Toolbar(_visualizeSceneIndex, SceneChoices);
                if (newSceneIndex != _visualizeSceneIndex) _shouldUpdateVisualization = true;
                _visualizeSceneIndex = newSceneIndex;

                var newTaskIndex = GUILayout.Toolbar(_visualizeTaskIndex,
                    Enumerable.Range(1, _analyzedData.First().scenarios[_visualizeSceneIndex].localizationTasks.Count)
                        .Select(x => x.ToString()).ToArray());
                if (newTaskIndex != _visualizeTaskIndex) _shouldUpdateVisualization = true;
                _visualizeTaskIndex = newTaskIndex;

                var firstScenario = _analyzedData.First().scenarios[_visualizeSceneIndex];
                var firstTask = firstScenario.localizationTasks[_visualizeTaskIndex];

                TempPin.showPrimaryPath = GUILayout.Toolbar(TempPin.showPrimaryPath ? 1 : 0,
                    new[] { "Source to Guess Path", "Player to Guess Path" }) == 1;
                if (_shouldUpdateVisualization)
                {
                    if (SceneManager.GetActiveScene().name != firstScenario.scene)
                    {
                        SceneManager.LoadScene(firstScenario.scene);
                        return; // wait until loaded
                    }

                    References.PlayerAndAudioPaused = true;
                    TempPin.Clear();
                    TempPin.Create(firstTask.audioPosition, Color.black, 1.0f,
                        new[] { firstTask.playerToAudioPathing });

                    References.PlayerPosition = firstTask.listenerPosition;
                    foreach (var data in _analyzedData)
                    {
                        var scenario = data.scenarios[_visualizeSceneIndex];
                        var task = scenario.localizationTasks[_visualizeTaskIndex];
                        var color = ColorForAudioConfig(scenario.audioConfiguration);
                        TempPin.Create(task.guessedPosition, color, 0.6f,
                            new[] { task.playerToGuessPathing, task.guessToAudioPathing });
                    }

                    _shouldUpdateVisualization = false;
                }
            }
            else
            {
                TempPin.Clear();
            }

            Color ColorForAudioConfig(AudioConfiguration config) => config switch
            {
                AudioConfiguration.Basic   => Color.cyan,
                AudioConfiguration.Pathing => Color.yellow,
                AudioConfiguration.Mixed   => Color.magenta,
                _                          => throw new ArgumentOutOfRangeException(nameof(config), config, null)
            };

            GUILayout.Space(10);


            // if (GUILayout.Button("Normalisis"))
            // {
            //     // 5.2 can still happen under normal circumstances, analyze fr later
            //     var speed = _data.scenarios.SelectMany(s => s.navigationTasks).SelectMany(nav =>
            //     {
            //         var first = nav.frames.FirstOrDefault();
            //         var prevPosition = first?.position ?? Vector2.zero;
            //         var prevTime = first?.time ?? 0f;
            //         return nav.frames.Select(frame =>
            //         {
            //             var res = Vector2.Distance(frame.position, prevPosition) / (frame.time - prevTime);
            //             prevPosition = frame.position;
            //             prevTime = frame.time;
            //             return res;
            //         });
            //     }).ToList();
            //
            //     speed.Sort();
            //     Debug.Log(speed);
            // }
        }

        void FileSelection()
        {
            if (GUILayout.Button("Select Folder"))
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
                            _folderPath = p;
                            _data = d;
                            _analyzedData = null;
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }
            }

            if (_folderPath is not null)
                GUILayout.Label("Selected Folder: " +
                                $"\"{Path.GetFileName(_folderPath)}\" ({_data!.Length} Files)");
            else GUILayout.Label("No StudyData Loaded");
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
    }

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
                row.Cell(++c).Value = d.frame.audioPath.Length;
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