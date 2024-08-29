using System;
using System.Collections.Generic;
using System.Linq;
using SteamAudio;
using UnityEngine;
using Random = UnityEngine.Random;
using Vector3 = UnityEngine.Vector3;

public static class Utils
{
    public static Vector2 XZ(this Vector3 v) => new(v.x, v.z);
    public static Vector3 XZ(this Vector2 v, float y = 0f) => new(v.x, y, v.y);

    public static float Efficiency(Vector2 moveDir, Vector2 optimalDir)
    {
        if (moveDir.magnitude < 0.1f) return 0f;
        var angle01 = Vector2.Angle(moveDir, optimalDir) / 180f;
        return 1f - angle01 * 2f;
    }
    
    public static List<Vector3> RandomAudioPositions(int numSources, float minDistance, int randomSeed)
    {
        var result = new List<Vector3>();
        Random.InitState(randomSeed);

        var possiblePositions = References.ProbeBatch.ProbeSpheres
            .Select(p => p.center)
            .Select(Common.ConvertVector).ToArray();

        if (possiblePositions.Length == 0)
            throw new Exception($"Can't reposition audio source, there are no generated probe spheres");

        var prev = References.ListenerPosition;
        for (var i = 0; i < numSources; i++)
        {
            var distanceFiltered = possiblePositions.Where(v => Vector3.Distance(v, prev) > minDistance)
                .ToArray();
            if (distanceFiltered.Length != 0)
            {
                result.Add(prev = RandomIndex(distanceFiltered));
            }
            else
            {
                Debug.LogWarning(
                    $"No available positions further than the min distance {minDistance}m away from the listener");
                result.Add(prev = RandomIndex(possiblePositions));
            }
        }

        return result;

        Vector3 RandomIndex(Vector3[] l) => l[Random.Range(0, l.Length - 1)]; // Note: ignore empty case
    }
}