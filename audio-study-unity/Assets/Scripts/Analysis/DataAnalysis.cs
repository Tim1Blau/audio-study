#if UNITY_EDITOR
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Wordprocessing;
using SteamAudio;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using Color = UnityEngine.Color;

[Serializable]
public record Toolbar<TEnum> where TEnum : Enum
{
    public static readonly string[] Choices = Enum.GetNames(typeof(TEnum));
    [SerializeField] public int index = 0;
    [SerializeField] public TEnum value = Enum.GetValues(typeof(TEnum)).Cast<TEnum>().First();

    public bool Gui()
    {
        var i = GUILayout.Toolbar(index, Choices);
        if (i == index) return false;
        index = i;
        value = (TEnum)Enum.Parse(typeof(TEnum), Choices[index]);
        return true;
    }
}

[Serializable]
public record Toolbar
{
    [SerializeField] public string[] choices;
    [SerializeField] public int index = 0;

    public bool IsAll => choices[index] == "All";

    public Toolbar(int count, bool allOption = false)
    {
        var numbers = Enumerable.Range(1, count).Select(x => x.ToString());
        if (allOption) numbers = numbers.Append("All");
        choices = numbers.ToArray();
    }

    public bool Gui()
    {
        var i = GUILayout.Toolbar(index, choices);
        if (i == index) return false;
        index = i;
        return true;
    }
}

public class DataAnalysis : EditorWindow
{
    [MenuItem("Audio Study/Analysis Window")]
    public static void ShowWindow() => GetWindow<DataAnalysis>("Analysis");

    string? _folderPath;
    StudyData[]? _data;

    bool _shouldAnalyze;
    [NonSerialized] Progress? _analysisProgress;
    AnalyzedData[]? _analyzedData;

    enum VisualizationType { None, Navigation, Localization }

    enum Scenes { Scene1, Scene2, Scene3 }

    enum LocPathVisType { Player_to_Guess_Path, Source_to_Guess_Path }

    enum TrimAverage
    {
        All,
        Best,
        NoWorst,
        NoWorst2,
        NoBestAndWorst
    }

    enum NavVisType { Path, Heatmap }

    readonly Toolbar<VisualizationType> _visTypeToolbar = new();
    readonly Toolbar<Scenes> _visSceneToolbar = new();
    readonly Toolbar _visLocTaskToolbar = new(count: 7, true);
    readonly Toolbar _visNavTaskToolbar = new(count: 10, true);
    readonly Toolbar<LocPathVisType> _locPathVisTypeToolbar = new();
    readonly Toolbar<NavVisType> _navVisTypeToolbar = new();
    readonly Toolbar<AudioConfiguration> _heatmapAudioConfigToolbar = new();
    readonly Toolbar<TrimAverage> _trimAverageToolbar = new();
    float _heatMapFloor = 0.0f;
    float _heatMapCeil = 1.0f;

    bool _shouldUpdateVisualization = true;
    const int NavPathSampleRate = 5;

    IEnumerable<T> Trim<T>(IEnumerable<T> enumerable) => _trimAverageToolbar.value switch
    {
        TrimAverage.All            => enumerable,
        TrimAverage.Best           => enumerable.Take(1),
        TrimAverage.NoWorst        => enumerable.SkipLast(1),
        TrimAverage.NoWorst2       => enumerable.SkipLast(2),
        TrimAverage.NoBestAndWorst => enumerable.Skip(1).SkipLast(1),
        _                          => throw new ArgumentOutOfRangeException()
    };

    struct A
    {
        public int TaskIndex;
        public AudioConfiguration AudioConfig;
    }

    List<IGrouping<int, List<IGrouping<AudioConfiguration, NavigationTask>>>> currentNavigationTasks
    {
        get
        {
            var scenarios = _analyzedData!.Select(data => data.scenarios[_visSceneToolbar.index]);
            var tasksForData = scenarios.SelectMany(scenario =>
            {
                var tasks = _visNavTaskToolbar.IsAll
                    ? scenario.navigationTasks.AsEnumerable()
                    : new[] { scenario.navigationTasks[_visNavTaskToolbar.index] };
                return tasks.Select((task, taskIndex) => (taskIndex, scenario.audioConfiguration, task));
            });
            var group = tasksForData.GroupBy(x => x.taskIndex, x => (x.audioConfiguration, x.task))
                .Select(tasks =>
                {
                    var trimmedTasks = tasks.GroupBy(t => t.audioConfiguration, t => t.task);
                    trimmedTasks = trimmedTasks.SelectMany(group =>
                    {
                        var ordered = group.OrderBy(task => task.Duration());
                        var trimmed = Trim(ordered);
                        return trimmed.Select(task => (group.Key, task));
                    }).GroupBy(x => x.Key, x => x.task);
                    return (taskIndex: tasks.Key, tasks: trimmedTasks.ToList());
                }).GroupBy(x => x.taskIndex, x => x.tasks);
            return group.ToList();
            // var group = tasksForData.GroupBy(x => new A { TaskIndex = x.taskIndex, AudioConfig = x.audioConfiguration }, x => x.task);
            // var ordered = group.OrderBy(x => x.Select(y => y.Duration()));
            // var trimmed = Trim(ordered);
            //
            // return trimmed;
        }
    }

    void OnEnable()
    {
        foreach (var go in FindObjectsOfType<GameObject>().Where(g => g.name == "TempPin"))
        {
            DestroyImmediate(go);
        }
    }

    void Update()
    {
        if (_analyzedData is null || _visTypeToolbar.value is not VisualizationType.Navigation) return;
        if (_navVisTypeToolbar.value is NavVisType.Path)
        {
            foreach (var tasksForConfig in currentNavigationTasks.SelectMany(x => x.SelectMany(y => y.AsEnumerable())))
            {
                var audioConfiguration = tasksForConfig.Key;
                foreach (var task in tasksForConfig)
                {
                    var last = task.frames[0];
                    var color = ColorForAudioConfig(audioConfiguration);
                    for (var i = NavPathSampleRate; i < task.frames.Count; i += NavPathSampleRate)
                    {
                        const int y = 3;
                        var frame = task.frames[i];
                        Debug.DrawLine(last.position.XZ(y), frame.position.XZ(y), color,
                            Time.deltaTime * NavPathSampleRate);
                        last = frame;
                    }
                }
            }
        }
    }

    void DataVisGui()
    {
        if (_visTypeToolbar.Gui())
        {
            _shouldUpdateVisualization = true;
        }

        switch (_visTypeToolbar.value)
        {
            case VisualizationType.Navigation:
                NavigationGui();
                break;
            case VisualizationType.Localization:
                LocalizationGui();
                break;
            case VisualizationType.None:
            default:
                MapPin.Clear();
                HeatmapTile.Clear();
                break;
        }
    }

    void LocalizationGui()
    {
        if (_analyzedData is null) return;

        if (_visSceneToolbar.Gui()) _shouldUpdateVisualization = true;
        if (_visLocTaskToolbar.Gui()) _shouldUpdateVisualization = true;

        if (_locPathVisTypeToolbar.Gui())
            MapPin.showPrimaryPath = _locPathVisTypeToolbar.value == LocPathVisType.Player_to_Guess_Path;

        _trimAverageToolbar.Gui();

        var tasks = _analyzedData.Select(data =>
        {
            var scenario = data.scenarios[_visSceneToolbar.index];
            return (
                data,
                scenario,
                tasks: _visLocTaskToolbar.index < scenario.localizationTasks.Count
                    ? new[] { scenario.localizationTasks[_visLocTaskToolbar.index] }
                    : scenario.localizationTasks.ToArray()
            );
        }).ToArray();

        var allTasksForConfig = tasks.SelectMany(x => x.tasks.Select(task => (x.scenario.audioConfiguration, task)))
            .GroupBy(g => g.audioConfiguration, g => g.task)
            .OrderBy(x => (int)x.Key).ToArray();

        var averageGuessDistancePath = allTasksForConfig
            .Select(group => (
                config: group.Key,
                distance: Trim(group.Select(task => task.guessToAudioPathing.Length).OrderBy(x => x)).Average())
            )
            .ToArray();

        var averageGuessDistanceStraight = allTasksForConfig
            .Select(group => (
                config: group.Key,
                distance: Trim(group.Select(task => task.guessToAudioStraight.Length).OrderBy(x => x)).Average())
            )
            .ToArray();

        var averageGuessDistancePathMax = averageGuessDistancePath.Max(x => x.distance);
        var averageGuessDistanceStraightMax = averageGuessDistanceStraight.Max(x => x.distance);

        GUILayout.Label("Average Path Distance");
        foreach (var (config, distance) in averageGuessDistancePath)
        {
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), distance / averageGuessDistancePathMax,
                $"{config}: {distance:F1}m");
        }

        GUILayout.Label("Average Straight Distance");
        foreach (var (config, distance) in averageGuessDistanceStraight)
        {
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), distance / averageGuessDistanceStraightMax,
                $"{config}: {distance:F1}m");
        }

        if (_visLocTaskToolbar.IsAll)
        {
            var numTasks = tasks.First().tasks.Length;

            var ranksPerTask = Enumerable.Range(0, numTasks).Select(i =>
            {
                var currentTaskForConfig = tasks.GroupBy(x => x.scenario.audioConfiguration, x => x.tasks[i]).ToArray();
                var distancesForConfig = currentTaskForConfig.Select(g =>
                {
                    var pathDist = Trim(g.Select(task => task.guessToAudioPathing.Length).OrderBy(x => x)).Average();
                    var straightDist = Trim(g.Select(task => task.guessToAudioStraight.Length).OrderBy(x => x))
                        .Average();
                    return (
                        config: g.Key,
                        pathDist,
                        straightDist
                    );
                }).ToArray();
                var pathRanks = distancesForConfig.OrderBy(x => x.pathDist).Select((x, rank) => (x.config, rank))
                    .ToDictionary(x => x.config, x => x.rank);
                var straightRanks = distancesForConfig.OrderBy(x => x.straightDist)
                    .Select((x, rank) => (x.config, rank)).ToDictionary(x => x.config, x => x.rank);

                var ranksForConfig = pathRanks.Keys.ToDictionary(config => config,
                    config => (pathRank: pathRanks[config], straightRank: straightRanks[config]));
                return ranksForConfig;
            }).ToList();


            var configs = ranksPerTask.First().Keys.OrderBy(x => (int)x).ToArray();
            GUILayout.Label("Average Path Distance Rank");
            foreach (var config in configs)
            {
                var pathRank = (float)ranksPerTask.Average(x => x[config].pathRank);
                EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), pathRank / 2f, $"{config}: {pathRank + 1:F1}");
            }

            GUILayout.Label("Average Straight Distance Rank");
            foreach (var config in configs)
            {
                var straightRank = (float)ranksPerTask.Average(x => x[config].straightRank);
                EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), straightRank / 2f,
                    $"{config}: {straightRank + 1:F1}");
            }
        }


        if (_shouldUpdateVisualization)
        {
            HeatmapTile.Clear();
            MapPin.Clear();
            References.PlayerAndAudioPaused = true;

            var firstScenario = _analyzedData.First().scenarios[_visSceneToolbar.index];

            if (SceneManager.GetActiveScene().name != firstScenario.scene)
            {
                SceneManager.LoadScene(firstScenario.scene);
                return;
            }

            var firstTask = firstScenario.localizationTasks.First();
            References.PlayerPosition = firstTask.listenerPosition;

            var taskDefinition = tasks.First().tasks;
            foreach (var task in taskDefinition)
            {
                MapPin.Create(task.audioPosition, Color.black, 1.0f, new[] { task.playerToAudioPathing });
            }

            foreach (var (data, scenario, currentTasks) in tasks)
            {
                var color = ColorForAudioConfig(scenario.audioConfiguration);
                foreach (var task in currentTasks)
                {
                    MapPin.Create(task.guessedPosition, color, 0.6f,
                        new[] { task.playerToGuessPathing, task.guessToAudioPathing });
                }
            }

            _shouldUpdateVisualization = false;
        }
    }

    void NavigationGui()
    {
        if (_analyzedData is null) return;
        MapPin.showPrimaryPath = true;

        var tasks = currentNavigationTasks;
        if (_shouldUpdateVisualization)
        {
            MapPin.Clear();
            foreach (var x in tasks.AsEnumerable().Reverse())
            {
                var task = x.First().First().First();
                References.PlayerPosition = task.listenerStartPosition;
                MapPin.Create(task.audioPosition, Color.black, 1f, new[] { task.frames.First().audioPath });
            }

            switch (_navVisTypeToolbar.value)
            {
                case NavVisType.Path:
                    HeatmapTile.Clear();
                    break;
                case NavVisType.Heatmap:
                    const float tileSize = 1.25f;

                    HeatmapTile.Clear();

                    var tasksWithConfig = tasks.SelectMany(x =>
                        x.SelectMany(y => y.Single(x => x.Key == _heatmapAudioConfigToolbar.value)));

                    var probe = References.Singleton.probeBatch;
                    var taskPositions = tasksWithConfig.Select(task => Enumerable.ToHashSet(task.frames
                        .Select(f => f.position / tileSize)
                        .Select(pos => new Vector2Int((int)pos.x, (int)pos.y))
                        .Distinct())).ToList();
                    var counts = probe.ProbeSpheres.Select(sphere =>
                    {
                        var pos = Common.ConvertVector(sphere.center).XZ();
                        var count = taskPositions.Count(t =>
                        {
                            var scaled = pos / tileSize;
                            var intVec = new Vector2Int((int)scaled.x, (int)scaled.y);
                            return t.Contains(intVec);
                        });
                        return (pos, count);
                    }).ToList();

                    var max = counts.Max(x => x.count);
                    foreach (var (pos, count) in counts)
                    {
                        if (count == 0) continue;
                        var value = count / (float)max; //0-1

                        value += _heatMapFloor;
                        value /= _heatMapCeil + _heatMapFloor;
                        value = Mathf.Clamp01(value);
                        HeatmapTile.Create(pos, new Color(1, 0, 0, value));
                    }


                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            _shouldUpdateVisualization = false;
        }

        if (_visSceneToolbar.Gui())
        {
            var newScene = _analyzedData.First().scenarios[_visSceneToolbar.index].scene;
            if (SceneManager.GetActiveScene().name != newScene)
            {
                SceneManager.LoadScene(newScene);
                _shouldUpdateVisualization = true;
            }
        }

        if (_visNavTaskToolbar.Gui())
        {
            _shouldUpdateVisualization = true;
        }

        if (_navVisTypeToolbar.Gui())
            _shouldUpdateVisualization = true;

        if (_navVisTypeToolbar.value is NavVisType.Heatmap)
        {
            if (_heatmapAudioConfigToolbar.Gui())
                _shouldUpdateVisualization = true;

            var ceil = _heatMapCeil;
            var floor = _heatMapFloor;
            GUILayout.Label($"Range: {_heatMapFloor:F2}-{_heatMapCeil:F2}");
            EditorGUI.MinMaxSlider(EditorGUILayout.GetControlRect(), ref _heatMapFloor, ref _heatMapCeil, 0f, 1f);
            if (!Mathf.Approximately(ceil, _heatMapCeil) || !Mathf.Approximately(floor, _heatMapFloor))
            {
                _shouldUpdateVisualization = true;
            }
        }

        if (_trimAverageToolbar.Gui()) _shouldUpdateVisualization = true;

        var averages = tasks.SelectMany(group =>
            {
                var tasksForConfig = group.SelectMany(x => x.AsEnumerable());
                var durationForConfig =
                    tasksForConfig.GroupBy(task => task.Key, task => task.Average(x => x.Duration()));
                return durationForConfig.SelectMany(x => x.Select(t => (config: x.Key, duration: t)));
            })
            .GroupBy(x => x.config, x => x.duration)
            .OrderBy(x => (int)x.Key)
            .Select(x => (config: x.Key, duration: x.Average(time => time)))
            .OrderBy(x => (int)x.config).ToList();

        var geoAverages = tasks.SelectMany(group =>
            {
                var tasksForConfig = group.SelectMany(x => x.AsEnumerable());
                var durationForConfig =
                    tasksForConfig.GroupBy(task => task.Key, task => task.GeoAverage(x => x.Duration()));
                return durationForConfig.SelectMany(x => x.Select(t => (config: x.Key, duration: t)));
            })
            .GroupBy(x => x.config, x => x.duration)
            .OrderBy(x => (int)x.Key)
            .Select(x => (config: x.Key, duration: x.Average(time => time)))
            .OrderBy(x => (int)x.config).ToList();

        var maxDuration = averages.Max(x => x.duration);
        var maxGeoDuration = geoAverages.Max(x => x.duration);

        GUILayout.Label("Geometric Average Time");
        foreach (var (config, duration) in geoAverages)
        {
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(),
                (float)(duration.TotalSeconds / maxGeoDuration.TotalSeconds),
                $"{config}: {duration.TotalSeconds:F3}s");
        }

        GUILayout.Label("Average Time");
        foreach (var (config, duration) in averages)
        {
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(),
                (float)(duration.TotalSeconds / maxDuration.TotalSeconds),
                $"{config}: {duration.TotalSeconds:F3}s");
        }
    }

    void OnGUI()
    {
        GUILayout.Label("Analysis", EditorStyles.boldLabel);
        FileSelectionGui();
        GUILayout.Space(10);

        if (_data is null) return;
        if (_data.Length == 0)
        {
            _folderPath = null;
            _data = null;
            return;
        }

        /* Start Analysis */

        if (_analyzedData is null)
        {
            if (GUILayout.Button("Analyze"))
            {
                EditorApplication.EnterPlaymode();
                _shouldAnalyze = true;
                return;
            }
        }
        else
        {
            if (!Application.isPlaying)
            {
                if (GUILayout.Button("Start Playmode"))
                {
                    EditorApplication.EnterPlaymode();
                    return;
                }
            }

            if (GUILayout.Button("Redo Analysis"))
            {
                EditorApplication.EnterPlaymode();
                _analyzedData = null;
                _shouldAnalyze = true;
                return;
            }
        }

        GUILayout.Space(10);

        /* Analysis Progress */

        if (_analysisProgress is { } prog)
        {
            if (Application.isPlaying)
            {
                var progress = prog.TotalProgress;

                var elapsed = TimeSpan.FromSeconds(Time.realtimeSinceStartup - prog.StartTime);
                var estimated = elapsed / (progress + 0.0001f);
                GUILayout.Label($"Analyzing - {elapsed.Format()} / ~{estimated.Format()}");

                EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), progress, $"Total Progress: {progress:P}");
                EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), prog.Datas.Progress, $"Data {prog.Datas}");
                EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), prog.Scenarios.Progress,
                    $"Scenario {prog.Scenarios}");
                EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), prog.LocalizationTasks.Progress,
                    $"Task {prog.LocalizationTasks}");
                GUILayout.Space(10);

                return;
            }

            _analysisProgress = null;
            _analyzedData = null;
        }
        else if (_analyzedData is null)
        {
            if (!_shouldAnalyze) return;
            if (!Application.isPlaying) return;

            MapPin.Clear();
            _analysisProgress = new Progress { OnUpdate = Repaint };
            _shouldAnalyze = false;
            SceneManager.LoadScene(_data.First().scenarios.First().scene);

            var co = FindObjectOfType<Study>();
            co.StartCoroutine(AnalyzeAll());
            return;

            IEnumerator AnalyzeAll()
            {
                yield return _data.Analyze(res => _analyzedData = res, _analysisProgress);
                _analysisProgress = null;
                References.PlayerAndAudioPaused = true;
                Repaint();
            }
        }

        /* Visualization */

        DataVisGui();

        //        GUILayout.Space(10);
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

    void FileSelectionGui()
    {
        if (GUILayout.Button("Select Folder"))
        {
            var folderPath = EditorUtility.OpenFolderPanel("Select StudyData", "", "");
            if (!string.IsNullOrEmpty(folderPath))
            {
                try
                {
                    var filesInFolder = Directory.GetFiles(folderPath, "StudyData*.json");
                    if (filesInFolder.Length == 0)
                    {
                        EditorUtility.DisplayDialog("Could not Select Folder",
                            "There are no StudyData.json files in this folder.", "OK");
                        return;
                    }

                    var dataInFolder = filesInFolder.Select(JsonData.Import).ToArray();
                    if (dataInFolder.All(x => x is not null))
                    {
                        _folderPath = folderPath;
                        _data = dataInFolder;
                        _analyzedData = null;
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }

        var labelText = _folderPath is not null
            ? "Selected Folder: " + $"\"{Path.GetFileName(_folderPath)}\" ({_data!.Length} Files)"
            : "No StudyData Loaded";
        GUILayout.Label(labelText);
    }

    static Color ColorForAudioConfig(AudioConfiguration config) => config switch
    {
        AudioConfiguration.Basic   => Color.cyan,
        AudioConfiguration.Pathing => Color.yellow,
        AudioConfiguration.Mixed   => Color.magenta,
        _                          => throw new ArgumentOutOfRangeException(nameof(config), config, null)
    };

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
        TimeSpan.FromSeconds(source.Average(s => func(s).TotalSeconds));

    public static TimeSpan GeoAverage<TSource>(this IEnumerable<TSource> source, Func<TSource, TimeSpan> func)
    {
        source = source.ToList();
        var value = source.Select(func).Aggregate(1.0, (current, timeSpan) => current * timeSpan.TotalSeconds);
        var root = Math.Pow(value, 1 / (double)source.Count());
        return TimeSpan.FromSeconds(root);
    }

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
#endif