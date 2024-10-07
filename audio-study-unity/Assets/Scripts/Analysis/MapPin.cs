#if UNITY_EDITOR
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

public class MapPin : MonoBehaviour
{
    public static bool showPrimaryPath;
    static readonly List<GameObject> Objects = new();
    AudioPath[] _pathsToDraw = Array.Empty<AudioPath>();
    Color _color;
    
    void Update()
    {
        var toDraw = showPrimaryPath ? _pathsToDraw.Take(1) : _pathsToDraw.Skip(1);
        foreach (var audioPath in toDraw) DrawPath(audioPath, _color, Time.deltaTime);
    }

    public static void Create(Vector2 position, Color color, float size, AudioPath[] paths)
    {
        var go = new GameObject("TempPin");
        var pin = go.AddComponent<MapPin>();
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
#endif