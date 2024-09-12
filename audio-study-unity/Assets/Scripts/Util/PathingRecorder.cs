using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SteamAudio;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

public class PathingRecorder : IDisposable
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
                    position = References.PlayerPosition,
                    rotation = new Vector2(rotation.y, rotation.x),
                    audioPath = res
                }
            );
        });
    }

    public static IEnumerator WaitForPathingData(Action<AudioPath> result)
    {
        using var pathingRecorder = new PathingRecorder();
        /*------------------------------------------------*/
        yield return new WaitForSeconds(SteamAudioSettings.Singleton.simulationUpdateInterval);
        /*------------------------------------------------*/
        var audioPath = pathingRecorder.GetSavedAudioPath();

#if UNITY_EDITOR
        var pre = audioPath.points.FirstOrDefault();
        foreach (var next in audioPath.points.Skip(1))
        {
            const float y = 3.0f;
            Debug.DrawLine(pre.XZ(y), next.XZ(y), Color.magenta, 0.1f, true);
            pre = next;
        }
#endif

        result.Invoke(audioPath);
    }

    PathingRecorder() => SteamAudioManager.PathingVisCallback += AddPath;
    public void Dispose() => SteamAudioManager.PathingVisCallback -= AddPath;

    List<(Vector3 From, Vector3 To)> _currentAudioPath = new();

    void AddPath(SteamAudio.Vector3 from, SteamAudio.Vector3 to, Bool occluded, IntPtr userData) =>
        _currentAudioPath.Add((Common.ConvertVector(from), Common.ConvertVector(to)));

    AudioPath GetSavedAudioPath()
    {
        var listenerPos = References.Singleton.listener.transform.position;
        var audioPos = References.AudioPosition;

        if (_currentAudioPath.Count == 0)
            return new AudioPath { isOccluded = IsOccluded(), points = { audioPos, listenerPos.XZ() } };

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
        if (res.Count >= 2)
            return new AudioPath { isOccluded = false, points = res };

        // NOTE: Should be unreachable
        Debug.LogError("audioPath has less than two elements");
        return new AudioPath { isOccluded = true, points = new List<Vector2>() };

        bool IsOccluded()
        {
            var audioPos3 = audioPos.XZ(y: StudySettings.AudioYPosition);
            var hits = Physics.RaycastAll(
                listenerPos,
                audioPos3 - listenerPos,
                Vector3.Distance(audioPos3, listenerPos)
            );
            if (hits.Cast<RaycastHit?>()
                    .FirstOrDefault(h => h!.Value.transform.TryGetComponent<SteamAudioStaticMesh>(out _)) is not
                { } hit) return false;
            Debug.DrawRay(listenerPos, audioPos3 - listenerPos, Color.cyan, 0.5f);
            Debug.DrawLine(listenerPos, hit.point, Color.red, 0.5f);
            return true;
        }
    }
}