using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SteamAudio;
using UnityEngine;
using UnityEngine.Assertions;
using Random = UnityEngine.Random;
using Vector3 = UnityEngine.Vector3;

public static class Utils
{
    public static Vector2 XZ(this Vector3 v) => new(v.x, v.z);

    public static Vector3 WithY(this Vector3 v, float y)
    {
        v.y = y;
        return v;
    }
    public static Vector3 XZ(this Vector2 v, float y = 0f) => new(v.x, y, v.y);

    public static float Efficiency(Vector2 moveDir, Vector2 optimalDir)
    {
        if (moveDir.magnitude < 0.1f) return 0f;
        var angle01 = Vector2.Angle(moveDir, optimalDir) / 180f;
        return 1f - angle01 * 2f;
    }
}


// Compiler fix for records
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}