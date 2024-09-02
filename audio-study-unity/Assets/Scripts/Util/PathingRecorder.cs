using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SteamAudio;
using UnityEngine;
using Ray = UnityEngine.Ray;
using Vector3 = UnityEngine.Vector3;

public class PathingRecorder
{
    public static IEnumerator WaitForNextNavFrame(Action<NavigationTask.MetricsFrame> result)
    {
        yield return WaitForPathingData(res =>
        {
            var cam = References.Player.camera.transform;
            var rotation = cam ? cam.rotation.eulerAngles : Vector3.zero;
            result.Invoke(
                new NavigationTask.MetricsFrame
                {
                    time = References.Now,
                    position = References.PlayerPosition.XZ(),
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

    PathingRecorder()
    {
    }

    List<(Vector3 From, Vector3 To)> _currentAudioPath = new();

    void PathingCallback(SteamAudio.Vector3 from, SteamAudio.Vector3 to, Bool occluded, IntPtr userData) =>
        _currentAudioPath.Add((Common.ConvertVector(from), Common.ConvertVector(to)));

    List<Vector2> GetSavedAudioPath()
    {
        var listenerPos = References.Singleton.listener.transform.position;
        var audioPos = References.AudioPosition;

        if (_currentAudioPath.Count == 0)
        {
            CheckError();
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
        if (res.Count >= 2) return res;
        Debug.LogError("audioPath has less than two elements");
        return new List<Vector2>();

        void CheckError()
        {
            if (Physics.RaycastAll(listenerPos, audioPos - listenerPos, Vector3.Distance(audioPos, listenerPos))
                    .Cast<RaycastHit?>()
                    .FirstOrDefault(h => h!.Value.transform.TryGetComponent<SteamAudioStaticMesh>(out _)) is { } hit)
            {
                Debug.DrawRay(listenerPos, audioPos - listenerPos, Color.cyan, 0.5f);
                Debug.DrawLine(listenerPos, hit.point, Color.red, 0.5f);
                Debug.LogWarning("No path!?");
            }
        }
    }
}