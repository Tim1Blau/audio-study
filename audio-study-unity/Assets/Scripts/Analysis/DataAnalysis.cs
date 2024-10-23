#if UNITY_EDITOR
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using SteamAudio;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Color = UnityEngine.Color;

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
    
    enum AudioConfigOrAll { All, Basic, Pathing, Mixed}
    
    enum Scenes { Scene1, Scene2, Scene3 }

    enum LocPathVisType { Direct, Path }

    enum TrimAverage
    {
        All,
        Best,
        NoWorst,
        NoWorst2,
        NoBestAndWorst
    }

    enum NavVisType { Path, Heatmap, None }

    readonly Toolbar<VisualizationType> _visTypeToolbar = new();
    readonly Toolbar<Scenes> _visSceneToolbar = new();
    readonly Toolbar _visLocTaskToolbar = new(count: 7, true);
    readonly Toolbar _visNavTaskToolbar = new(count: 10, true);
    readonly Toolbar<LocPathVisType> _locPathVisTypeToolbar = new();
    readonly Toolbar<NavVisType> _navVisTypeToolbar = new();
    readonly Toolbar<AudioConfiguration> _heatmapAudioConfigToolbar = new();
    readonly Toolbar<AudioConfigOrAll> _locAudioConfigToolbar = new();
    readonly Toolbar<TrimAverage> _trimAverageToolbar = new();
    float _heatMapFloor = 0.0f;
    float _heatMapCeil = 1.0f;
    
    [SerializeField] float minDistance = 2;
    [SerializeField] float pow = 4;


    bool _shouldUpdateVisualization = true;
    const int NavPathSampleRate = 1;

    IEnumerable<T> Trim<T>(IEnumerable<T> enumerable) => _trimAverageToolbar.value switch
    {
        TrimAverage.All            => enumerable,
        TrimAverage.Best           => enumerable.Take(1),
        TrimAverage.NoWorst        => enumerable.SkipLast(1),
        TrimAverage.NoWorst2       => enumerable.SkipLast(2),
        TrimAverage.NoBestAndWorst => enumerable.Skip(1).SkipLast(1),
        _                          => throw new ArgumentOutOfRangeException()
    };

    Dictionary<int, Dictionary<AudioConfiguration, (List<AnalyzedNavigationTask> tasks, TimeSpan avgTime, TimeSpan
        geoAvgTime)>> CurrentNavigationTasks
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
            Dictionary<int, Dictionary<AudioConfiguration, (List<AnalyzedNavigationTask> tasks, TimeSpan avgTime,
                TimeSpan geoAvgTime)>> result = tasksForIndices.Select(taskForIndex =>
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
        }
    }

    void OnEnable()
    {
        if (_analyzedData is { Length: 0 }) _analyzedData = null;
        foreach (var go in FindObjectsOfType<GameObject>().Where(g => g.name == MapPin.Name))
        {
            DestroyImmediate(go);
        }
    }

    void Update()
    {
        if (_analyzedData is null || _visTypeToolbar.value is not VisualizationType.Navigation ||
            _navVisTypeToolbar.value is not NavVisType.Path) return;
        foreach (var task in CurrentNavigationTasks.SelectMany(x =>
                     x.Value.Values.SelectMany(y => y.tasks)))
        {
            const float y = 0.2f;
            var last = task.frames[0].position.XZ(y);
            var color = ColorForAudioConfig(task.audioConfiguration);
            color.a = 0.2f;
            for (var i = NavPathSampleRate; i < task.frames.Count; i += NavPathSampleRate)
            {
                var frame = task.frames[i];
                var pos = frame.position.XZ(y);
                color.a = NearPin(last.XZ());
                {
                    Debug.DrawLine(last, pos, color, Time.deltaTime * 5, true);
                }
                    
                last = pos;
            }
        }
        float NearPin(Vector2 position)
        {
            const float offset = 1.1f;
            if (MapPin.Objects.Count == 0) return 1f;

            var playerDistance = Vector2.Distance(References.PlayerPosition, position);
            position.y -= offset;
            var distance = MapPin.Objects.Min(x =>
            {
                var pos = x.transform.position.XZ();
                var distance = Vector2.Distance(pos, position);
                return distance;
            });
            distance = Mathf.Min(distance, playerDistance);
            var res = Mathf.Min(1f, distance / minDistance);
            res = Mathf.Pow(res, pow);
            res = Mathf.Min(1f, res);
            return res;
        }
    }

    void DataVisGui()
    {
        GUILayout.Label("Visualization", EditorStyles.boldLabel);
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

        GUILayout.Space(10);
        GUILayout.Label("Export", EditorStyles.boldLabel);
        if (GUILayout.Button("Export Localization")) ExportLocalization();
        if (GUILayout.Button("Export Navigation")) ExportNavigation();
        if (GUILayout.Button("Export Demographics")) ExportDemographics();
    }

    void LocalizationGui()
    {
        if (_analyzedData is null) return;

        if (_visSceneToolbar.Gui()) _shouldUpdateVisualization = true;
        if (_visLocTaskToolbar.Gui()) _shouldUpdateVisualization = true;

        if (_locPathVisTypeToolbar.Gui())
            MapPin.showPrimaryPath = _locPathVisTypeToolbar.value == LocPathVisType.Direct;
        
        if(_locAudioConfigToolbar.Gui())
            _shouldUpdateVisualization = true;

        _trimAverageToolbar.Gui();

        var allTasksForConfig = _analyzedData.SelectMany(data =>
        {
            var scenario = data.scenarios[_visSceneToolbar.index];
            var audioConfiguration = scenario.audioConfiguration;
            var tasks = _visLocTaskToolbar.IsAll
                ? scenario.localizationTasks.Select((task, i) => (audioConfiguration, i, task))
                : new[]
                {
                    (audioConfiguration, _visLocTaskToolbar.index, scenario.localizationTasks[_visLocTaskToolbar.index])
                };

            return tasks;
        }).GroupBy(x => new { x.i, x.audioConfiguration }).ToArray();


        var averageGuessDistancePath = allTasksForConfig
            .Select(group =>
            {
                var lengths = group.Select(x => x.task.guessToAudioPathing.Length);
                var trimmed = Trim(lengths.OrderBy(x => x)).ToList();
                var average = trimmed.GeoAverage();
                return (group.Key, average);
            }).GroupBy(x => x.Key.audioConfiguration, x => x.average)
            .Select(x => (config: x.Key, distance: x.Average()))
            .OrderBy(x => x.config)
            .ToArray();

        var averageGuessDistanceStraight = allTasksForConfig
            .Select(group =>
            {
                var lengths = group.Select(x => x.task.guessToAudioStraight.Length);
                var trimmed = Trim(lengths.OrderBy(x => x)).ToList();
                var average = trimmed.GeoAverage();
                return (group.Key, average);
            }).GroupBy(x => x.Key.audioConfiguration, x => x.average)
            .Select(x => (config: x.Key, distance: x.Average()))
            .OrderBy(x => x.config)
            .ToArray();

        var averageGuessDistancePathMax = averageGuessDistancePath.Max(x => x.distance);
        var averageGuessDistanceStraightMax = averageGuessDistanceStraight.Max(x => x.distance);

        GUILayout.Label("Geometric Mean Direct Distance");
        foreach (var (config, distance) in averageGuessDistanceStraight)
        {
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), distance / averageGuessDistanceStraightMax,
                $"{config}: {distance:F1}m");
        }

        GUILayout.Label("Geometric Mean Path Distance");
        foreach (var (config, distance) in averageGuessDistancePath)
        {
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), distance / averageGuessDistancePathMax,
                $"{config}: {distance:F1}m");
        }

        if (_shouldUpdateVisualization)
        {
            var firstScenario = _analyzedData.First().scenarios[_visSceneToolbar.index];

            if (SceneManager.GetActiveScene().name != firstScenario.scene)
            {
                SceneManager.LoadScene(firstScenario.scene);
                return;
            }

            MapPin.showPrimaryPath = _locPathVisTypeToolbar.value == LocPathVisType.Direct;
            HeatmapTile.Clear();
            MapPin.Clear();
            References.PlayerAndAudioPaused = true;

            var firstTask = firstScenario.localizationTasks.First();
            References.PlayerPosition = firstTask.listenerPosition;

            var taskDefinition = allTasksForConfig.Select(x => x.First())
                .DistinctBy(x => x.i);
            foreach (var (_, i, task) in taskDefinition)
            {
                MapPin.CreateNumbered(task.audioPosition, Color.black, 1.3f, i + 1, true,
                    new[] { task.playerToAudioPathing });
            }

            var tasks = _locAudioConfigToolbar.value switch
            {
                AudioConfigOrAll.All => allTasksForConfig,
                AudioConfigOrAll.Basic => allTasksForConfig.Where(x =>
                    x.Key.audioConfiguration is AudioConfiguration.Basic),
                AudioConfigOrAll.Pathing => allTasksForConfig.Where(x =>
                    x.Key.audioConfiguration is AudioConfiguration.Pathing),
                AudioConfigOrAll.Mixed => allTasksForConfig.Where(x =>
                    x.Key.audioConfiguration is AudioConfiguration.Mixed),
                _ => throw new ArgumentOutOfRangeException()
            };

            foreach (var group in tasks)
            {
                const bool createNumbered = true;
                const bool useParticipantIdInstead = false;
                var color = ColorForAudioConfig(group.Key.audioConfiguration);
                var i = group.Key.i + 1;
                foreach (var tuple in group)
                {
                    var task = tuple.task;

                    if(useParticipantIdInstead)
                        i = task.dataID;
                    if(createNumbered)
                        MapPin.CreateNumbered(task.guessedPosition, color, 0.8f, i, false,
                            new[] { task.guessToAudioStraight, task.guessToAudioPathing });
                    else
                        MapPin.Create(task.guessedPosition, color, 0.4f, new[] { task.guessToAudioStraight, task.guessToAudioPathing });
                }
            }

            _shouldUpdateVisualization = false;
        }
    }

    void NavigationGui()
    {
        if (_analyzedData is null) return;
        MapPin.showPrimaryPath = true;


        if (_visSceneToolbar.Gui()) _shouldUpdateVisualization = true;

        if (_visNavTaskToolbar.Gui()) _shouldUpdateVisualization = true;

        if (_navVisTypeToolbar.Gui()) _shouldUpdateVisualization = true;

        if (_trimAverageToolbar.Gui()) _shouldUpdateVisualization = true;
        
        if (_navVisTypeToolbar.value is NavVisType.Heatmap)
        {
            if (_heatmapAudioConfigToolbar.Gui()) _shouldUpdateVisualization = true;

            var ceil = _heatMapCeil;
            var floor = _heatMapFloor;
            GUILayout.Label($"Range: {_heatMapFloor:F2}-{_heatMapCeil:F2}");
            EditorGUI.MinMaxSlider(EditorGUILayout.GetControlRect(), ref _heatMapFloor, ref _heatMapCeil, 0f, 1f);
            if (!Mathf.Approximately(ceil, _heatMapCeil) || !Mathf.Approximately(floor, _heatMapFloor))
            {
                _shouldUpdateVisualization = true;
            }
        } else if (_navVisTypeToolbar.value is NavVisType.Path)
        {
            minDistance = EditorGUI.FloatField(EditorGUILayout.GetControlRect(), "Fade Path Distance", minDistance);
            pow = EditorGUI.FloatField(EditorGUILayout.GetControlRect(), "Fade Path Pow", pow);
        }


        var tasks = CurrentNavigationTasks;
        var averages = tasks.Values.SelectMany(t => t)
            .GroupBy(p => p.Key)
            .OrderBy(p => (int)p.Key)
            .Select(t =>
            {
                return (config: t.Key,
                        geoAverage: t.Average(x => x.Value.geoAvgTime)
                    );
            }).ToList();

        var maxGeoDuration = averages.Max(x => x.geoAverage);

        GUILayout.Label("Geometric Mean Navigation Time");
        foreach (var x in averages)
        {
            var seconds = x.geoAverage.TotalSeconds;
            var value = (float)(seconds / maxGeoDuration.TotalSeconds);
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), value, $"{x.config}: {seconds:F3}s");
        }

        // GUILayout.Label("Average Time");
        // foreach (var x in averages)
        // {
        //     var seconds = x.average.TotalSeconds;
        //     var value = (float)(seconds / maxDuration.TotalSeconds);
        //     EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), value, $"{x.config}: {seconds:F3}s");
        // }

        if (_shouldUpdateVisualization)
        {
            var newScene = _analyzedData.First().scenarios[_visSceneToolbar.index].scene;
            if (SceneManager.GetActiveScene().name != newScene)
            {
                SceneManager.LoadScene(newScene);
                return;
            }

            tasks = CurrentNavigationTasks;
            References.PlayerAndAudioPaused = true;
            MapPin.Clear();
            HeatmapTile.Clear();
            foreach (var taskDef in tasks.Reverse())
            {
                var task = taskDef.Value.Values.First().tasks.First();
                References.PlayerPosition = task.listenerStartPosition;
                MapPin.CreateNumbered(task.audioPosition, Color.black, 1f, taskDef.Key + 1, true,
                    new[] { task.frames.First().audioPath });
            }

            if (_navVisTypeToolbar.value == NavVisType.Heatmap)
            {
                const float tileSize = 1.25f;

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
            }

            _shouldUpdateVisualization = false;
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

        if (_analysisProgress is { } prog)
        {
            if (!Application.isPlaying)
            {
                _analysisProgress = null;
                _analyzedData = null;
            }
            else
            {
                AnalysisProgressGui(prog);
            }
        }
        else if (_analyzedData is null)
        {
            if (GUILayout.Button("Analyze"))
            {
                EditorApplication.EnterPlaymode();
                _shouldAnalyze = true;
                return;
            }

            if (_shouldAnalyze && Application.isPlaying) StartAnalysis();
        }
        else
        {
            if (GUILayout.Button("Reanalyze"))
            {
                EditorApplication.EnterPlaymode();
                _analyzedData = null;
                _shouldAnalyze = true;
                return;
            }
            if (!Application.isPlaying)
            {
                if (GUILayout.Button("Start Playmode"))
                {
                    EditorApplication.EnterPlaymode();
                    return;
                }
            }
            else
            {
                GUILayout.Space(10);

                DataVisGui();               
            }
        }
    }

    void StartAnalysis()
    {
        if (_data is null) return;
        _shouldAnalyze = false;
        MapPin.Clear();
        SceneManager.LoadScene(_data.First().scenarios.First().scene);

        var co = FindObjectOfType<Study>();
        co.StartCoroutine(AnalyzeAll());
        return;

        IEnumerator AnalyzeAll()
        {
            yield return new WaitForNextFrameUnit();
            _analysisProgress = new Progress { OnUpdate = Repaint };
            yield return _data.Analyze(res => _analyzedData = res, _analysisProgress);
            _analysisProgress = null;
            References.PlayerAndAudioPaused = true;
            Repaint();
        }
    }

    static void AnalysisProgressGui(Progress prog)
    {
        var progress = prog.TotalProgress;

        var elapsed = TimeSpan.FromSeconds(Time.realtimeSinceStartup - prog.StartTime);
        var estimated = elapsed / (progress + 0.0001f);
        EditorGUI.LabelField(EditorGUILayout.GetControlRect(),
            $"Analyzing - {elapsed.Format()} / ~{estimated.Format()}");

        EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), progress, $"Total Progress: {progress:P}");
        EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), prog.Datas.Progress, $"Data {prog.Datas}");
        EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), prog.Scenarios.Progress,
            $"Scenario {prog.Scenarios}");
        EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), prog.LocalizationTasks.Progress,
            $"Task {prog.LocalizationTasks}");
        GUILayout.Space(10);
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
                    else
                    {
                        throw new Exception("Failed to load StudyData.json file.");
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
        foreach (var data in _analyzedData)
        {
            age = age.CellBelow();
            gamingExperience = gamingExperience.CellBelow();
            firstPersonExperience = firstPersonExperience.CellBelow();
            headphoneUsage = headphoneUsage.CellBelow();

            var answers = data.answers;
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

    static Color ColorForAudioConfig(AudioConfiguration config) => config switch
    {
        AudioConfiguration.Basic   => Color.cyan,
        AudioConfiguration.Pathing => Color.yellow,
        AudioConfiguration.Mixed   => Color.magenta,
        _                          => throw new ArgumentOutOfRangeException(nameof(config), config, null)
    };
}

[Serializable]
public record Toolbar<TEnum> where TEnum : Enum
{
    public static readonly string[] Choices = Enum.GetNames(typeof(TEnum));
    [SerializeField] public int index;
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
    [SerializeField] public int index;

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

    public static float GeoAverage(this IEnumerable<float> source)
    {
        source = source.ToList();
        var value = source.Aggregate(1.0, (current, value) => current * value);
        var root = Math.Pow(value, 1 / (double)source.Count());
        return (float)root;
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