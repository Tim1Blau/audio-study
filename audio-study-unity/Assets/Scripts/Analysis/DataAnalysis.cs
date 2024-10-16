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

    Dictionary<int, Dictionary<AudioConfiguration, (List<AnalyzedNavigationTask> tasks, TimeSpan averageDuration,
        TimeSpan geoAverageTime)>> currentNavigationTasks
    {
        get
        {
            var scenarios = _analyzedData!.Select(data => data.scenarios[_visSceneToolbar.index]);
            var tasksForData = scenarios.SelectMany(scenario =>
            {
                var tasks = _visNavTaskToolbar.IsAll
                    ? scenario.navigationTasks.Select((task, taskIndex) => (taskIndex, task))
                    : new[] { (_visNavTaskToolbar.index, scenario.navigationTasks[_visNavTaskToolbar.index]) };
                return tasks;
            });

            var tasksForIndices = tasksForData.GroupBy(x => x.taskIndex, x => x.task);
            var result = tasksForIndices.Select(taskForIndex =>
            {
                var tasksForConfigs = taskForIndex.GroupBy(t => t.audioConfiguration, t => t);
                var trimmedTasks = tasksForConfigs.Select(taskForConfig =>
                {
                    var ordered = taskForConfig.OrderBy(task => task.Duration);
                    var trimmed = Trim(ordered).ToList();
                    var averageDuration = trimmed.Average(t => t.Duration);
                    var geoAverageTime = trimmed.GeoAverage(t => t.Duration);
                    return (tasks: trimmed, averageDuration, geoAverageTime);
                }).ToDictionary(x => x.tasks.First().audioConfiguration);
                return (taskIndex: taskForIndex.Key, tasks: trimmedTasks);
            }).ToDictionary(x => x.taskIndex, x => x.tasks);
            return result;
            // var group = tasksForData.GroupBy(x => new A { TaskIndex = x.taskIndex, AudioConfig = x.audioConfiguration }, x => x.task);
            // var ordered = group.OrderBy(x => x.Select(y => y.Duration()));
            // var trimmed = Trim(ordered);
            //
            // return trimmed;
        }
    }

    void OnEnable()
    {
        foreach (var go in FindObjectsOfType<GameObject>().Where(g => g.name == MapPin.Name))
        {
            DestroyImmediate(go);
        }
    }

    void Update()
    {
        if (_analyzedData is null || _visTypeToolbar.value is not VisualizationType.Navigation) return;
        if (_navVisTypeToolbar.value is NavVisType.Path)
        {
            foreach (var task in currentNavigationTasks.SelectMany(x =>
                         x.Value.Values.SelectMany(y => y.tasks)))
            {
                var last = task.frames[0];
                var color = ColorForAudioConfig(task.audioConfiguration);
                for (var i = NavPathSampleRate; i < task.frames.Count; i += NavPathSampleRate)
                {
                    const float y = 0.2f;
                    var frame = task.frames[i];
                    Debug.DrawLine(last.position.XZ(y), frame.position.XZ(y), color, Time.deltaTime * NavPathSampleRate,
                        true);
                    last = frame;
                }
            }
        }
    }

    void DataVisGui()
    {
        if (GUILayout.Button("Export Localization")) ExportLocalization();
        if (GUILayout.Button("Export Navigation")) ExportNavigation();
        if (GUILayout.Button("Export Demographics")) ExportDemographics();
        GUILayout.Space(10);

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

    void ExportDemographics()
    {
        if (_analyzedData is null) return;
        var path = EditorUtility.SaveFilePanel("Export", "", "Demographics", "xlsx");

        using var workbook = new XLWorkbook();
        var x = workbook.Worksheets.Add("Demographics");
        var row = x.Row(1);
        var age = row.Cell(1);
        var gamingExperience = row.Cell(2);
        var firstPersonExperience = row.Cell(3);
        var headphoneUsage = row.Cell(4);

        age.Value = "Age";
        gamingExperience.Value = "GamingExperience";
        firstPersonExperience.Value = "FirstPersonExperience";
        headphoneUsage.Value = "HeadphoneUsage";
        foreach (var answers in _analyzedData.Select(x => x.answers))
        {
            age = age.CellBelow();
            gamingExperience = gamingExperience.CellBelow();
            firstPersonExperience = firstPersonExperience.CellBelow();
            headphoneUsage = headphoneUsage.CellBelow();

            age.Value = answers.age.ToString();
            gamingExperience.Value = answers.gamingExperience.ToString();
            firstPersonExperience.Value = answers.firstPersonExperience.ToString();
            headphoneUsage.Value = answers.headphoneUsage.ToString();
        }


        workbook.SaveAs(path);
    }

    void ExportNavigation()
    {
        if (_analyzedData is null) return;

        var path = EditorUtility.SaveFilePanel("Export", "", "Navigation", "xlsx");

        using var workbook = new XLWorkbook();
        var x = workbook.Worksheets.Add($"Navigation");
        var row = x.Row(1);

        foreach (var scene in _analyzedData.SelectMany(d => d.scenarios).GroupBy(s => s.scene))
        {
            row.Cell(1).Value = scene.Key;
            row = row.RowBelow();
            var numTasks = scene.First().navigationTasks.Count;
            var audioConfigs = typeof(AudioConfiguration).GetEnumNames();

            // Numbers
            var cell = row.Cell(1);
            for (var i = 0; i < numTasks; i++)
            {
                var taskIndex = i + 1;
                foreach (var config in audioConfigs)
                {
                    var value = $"{taskIndex} {config}";
                    cell.Value = value;
                    cell.Style.Font.Bold = true;
                    cell = cell.CellRight();
                }

                cell = cell.CellRight();
            }

            row = row.RowBelow();


            // Values
            var configCell = row.Cell(1);
            foreach (var config in scene.GroupBy(s => s.audioConfiguration).OrderBy(s => (int)s.Key))
            {
                var participantCell = configCell;
                foreach (var participant in config)
                {
                    var taskCell = participantCell;
                    foreach (var task in participant.navigationTasks)
                    {
                        taskCell.Value = task.Duration.TotalSeconds;

                        const string format = "#,##0.00";
                        taskCell.Style.NumberFormat.SetFormat(format);
                        taskCell = taskCell.CellRight(4);
                    }

                    participantCell = participantCell.CellBelow();
                }

                configCell = configCell.CellRight();
            }

            row = row.RowBelow(6);
        }

        workbook.SaveAs(path);
    }

    void ExportLocalization()
    {
        if (_analyzedData is null) return;

        var path = EditorUtility.SaveFilePanel("Export", "", "Localization", "xlsx");
        using var workbook = new XLWorkbook();
        foreach (var scene in _analyzedData.SelectMany(d => d.scenarios).GroupBy(s => s.scene))
        {
            var x = workbook.Worksheets.Add($"Localization {scene.Key}");
            var row = x.Row(1);
            var cell = row.Cell(1);
            var numTasks = scene.First().localizationTasks.Count;
            var audioConfigs = typeof(AudioConfiguration).GetEnumNames();
            var otherValuesOffset = (numTasks * (audioConfigs.Length + 1) + 1);

            // Numbers
            for (var i = 0; i < numTasks; i++)
            {
                var taskIndex = i + 1;
                foreach (var config in audioConfigs)
                {
                    var otherCell = cell.CellRight(otherValuesOffset);

                    var value = $"{taskIndex} {config}";
                    cell.Value = value;
                    otherCell.Value = value;
                    cell.Style.Font.Bold = true;
                    otherCell.Style.Font.Bold = true;
                    cell = cell.CellRight();
                }

                cell = cell.CellRight();
            }

            row = row.RowBelow();


            // Values
            var configCell = row.Cell(1);
            foreach (var config in scene.GroupBy(s => s.audioConfiguration).OrderBy(s => (int)s.Key))
            {
                var participantCell = configCell;
                foreach (var participant in config)
                {
                    var taskCell = participantCell;
                    foreach (var task in participant.localizationTasks)
                    {
                        var otherCell = taskCell.CellRight(otherValuesOffset);

                        taskCell.Value = task.guessToAudioStraight.Length;
                        otherCell.Value = task.guessToAudioPathing.Length;

                        const string format = "#,##0.00";
                        taskCell.Style.NumberFormat.SetFormat(format);
                        otherCell.Style.NumberFormat.SetFormat(format);
                        taskCell = taskCell.CellRight(4);
                    }

                    participantCell = participantCell.CellBelow();
                }

                configCell = configCell.CellRight();
            }
        }

        workbook.SaveAs(path);
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
            var startIndex = _visLocTaskToolbar.IsAll ? 1 : _visLocTaskToolbar.index + 1;
            var i = startIndex;
            foreach (var task in taskDefinition)
            {
                MapPin.CreateNumbered(task.audioPosition, Color.black, 1.3f, i++, true,
                    new[] { task.playerToAudioPathing });
            }

            foreach (var (data, scenario, currentTasks) in tasks)
            {
                var color = ColorForAudioConfig(scenario.audioConfiguration);
                i = startIndex;
                foreach (var task in currentTasks)
                {
                    MapPin.CreateNumbered(task.guessedPosition, color, 0.8f, i++, false,
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
            References.PlayerAndAudioPaused = true;
            MapPin.Clear();
            foreach (var taskDef in tasks.Reverse())
            {
                var task = taskDef.Value.Values.First().tasks.First();
                References.PlayerPosition = task.listenerStartPosition;
                MapPin.CreateNumbered(task.audioPosition, Color.black, 1f, taskDef.Key + 1, true,
                    new[] { task.frames.First().audioPath });
            }

            switch (_navVisTypeToolbar.value)
            {
                case NavVisType.Path:
                    HeatmapTile.Clear();
                    break;
                case NavVisType.Heatmap:
                    const float tileSize = 1.25f;

                    HeatmapTile.Clear();

                    var tasksWithConfig = tasks.Values.SelectMany(tasksForConfig =>
                        tasksForConfig[_heatmapAudioConfigToolbar.value].tasks);

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

        var averages = tasks.Values.SelectMany(t => t)
            .GroupBy(p => p.Key)
            .OrderBy(p => (int)p.Key)
            .Select(t =>
            {
                return (config: t.Key,
                        average: t.Average(x => x.Value.averageDuration),
                        geoAverage: t.Average(x => x.Value.geoAverageTime)
                    );
            }).ToList();

        var maxDuration = averages.Max(x => x.average);
        var maxGeoDuration = averages.Max(x => x.geoAverage);

        GUILayout.Label("Geometric Average Time");
        foreach (var x in averages)
        {
            var seconds = x.geoAverage.TotalSeconds;
            var value = (float)(seconds / maxGeoDuration.TotalSeconds);
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), value, $"{x.config}: {seconds:F3}s");
        }

        GUILayout.Label("Average Time");
        foreach (var x in averages)
        {
            var seconds = x.average.TotalSeconds;
            var value = (float)(seconds / maxDuration.TotalSeconds);
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), value, $"{x.config}: {seconds:F3}s");
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

                        // 5.2 can still happen under normal circumstances, analyze fr later
                        var speed = _data.SelectMany(s => s.scenarios).SelectMany(s => s.navigationTasks).SelectMany(
                            nav =>
                            {
                                var first = nav.frames.FirstOrDefault();
                                var prevPosition = first?.position ?? Vector2.zero;
                                var prevTime = first?.time ?? 0f;
                                return nav.frames.Select(frame =>
                                {
                                    var res = Vector2.Distance(frame.position, prevPosition) /
                                              (frame.time - prevTime + float.Epsilon);
                                    if (res > 5.5) throw null;
                                    prevPosition = frame.position;
                                    prevTime = frame.time;
                                    return res;
                                });
                            }).ToList();
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
}

internal static class TaskDuration
{
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

#endif