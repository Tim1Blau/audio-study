using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SteamAudio;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

public class PathingRecorder
{
    public static IEnumerator WaitForNextNavFrame(Action<NavigationScenario.Task.MetricsFrame> result)
    {
        yield return WaitForPathingData(res =>
        {
            var cam = References.Player.mainCamera.transform;
            var rotation = cam ? cam.rotation.eulerAngles : Vector3.zero;
            if (res.Count < 2)
            {
                Debug.LogError("audioPath has less then two elements");
            }
            result(
                new NavigationScenario.Task.MetricsFrame
                {
                    time = References.Now,
                    position = References.ListenerPosition.XZ(),
                    rotation = new Vector2(rotation.y, rotation.x),
                    audioPath = res
                }
            );
        });
    }

    public static IEnumerator WaitForPathingData(Action<List<Vector2>> result)
    {
        var instance = new PathingRecorder();
        SteamAudioManager.Singleton.PathingVisCallback += instance.PathingCallback;
        /*------------------------------------------------*/
        yield return new WaitForSeconds(SteamAudioSettings.Singleton.simulationUpdateInterval);
        /*------------------------------------------------*/
        result(instance.GetSavedAudioPath());
        SteamAudioManager.Singleton.PathingVisCallback -= instance.PathingCallback;
    }
    
    private PathingRecorder(){}

    List<(Vector3 From, Vector3 To)> _currentAudioPath = new();

    void PathingCallback(SteamAudio.Vector3 from, SteamAudio.Vector3 to, Bool occluded, IntPtr userData)
    {
        _currentAudioPath.Add((Common.ConvertVector(from), Common.ConvertVector(to)));
    }

    List<Vector2> GetSavedAudioPath()
    {
        var listenerPos = References.ListenerPosition;
        var audioPos = References.AudioPosition;

        if (_currentAudioPath.Count == 0)
        {
            if (Physics.RaycastAll(listenerPos, audioPos - listenerPos)
                .Any(h => h.transform.TryGetComponent<SteamAudioStaticMesh>(out _)))
            {
                Debug.LogWarning("No path!?");
            }

            return new List<Vector2> { audioPos.XZ(), listenerPos.XZ() };
        }

        _currentAudioPath.Reverse();
        // prune alternative paths
        var first = _currentAudioPath[0];
        var lastNonRepeatedIndex = 1 + _currentAudioPath.Skip(1).TakeWhile(x => first.From != x.From).Count();
        if (lastNonRepeatedIndex < _currentAudioPath.Count)
            _currentAudioPath = _currentAudioPath.GetRange(0, lastNonRepeatedIndex);

        // replace approximate last position with actual listener position
        _currentAudioPath[^1] = (_currentAudioPath[^1].From, listenerPos);

        // transform to compact version
        var res = _currentAudioPath.Select(x => x.To).Prepend(_currentAudioPath[0].From)
            .Select(x => x.XZ()).ToList();
        _currentAudioPath.Clear();
        if (res.Count < 2)
        {
            Debug.LogError("audioPath has less than two elements");
        }
        return res;
    }
}