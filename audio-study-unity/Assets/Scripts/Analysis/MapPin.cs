#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

public class MapPin : MonoBehaviour
{
    public const string Name = "TempPin";
    public static bool showPrimaryPath;
    public static readonly List<GameObject> Objects = new();
    [SerializeField] AudioPath[] pathsToDraw = Array.Empty<AudioPath>();
    [SerializeField] Color color;

    void Update()
    {
        var toDraw = showPrimaryPath ? pathsToDraw.Take(1) : pathsToDraw.Skip(1);
        var col = color;
        col.a = 0.6f;
        foreach (var audioPath in toDraw) DebugDrawPath(audioPath, col, Time.deltaTime * 3);
    }
    
    static void DebugDrawPath(AudioPath path, Color color, float duration)
    {
        var pre = path.points.FirstOrDefault();
        foreach (var next in path.points.Skip(1))
        {
            const float y = 3.0f;
            Debug.DrawLine(pre.XZ(y), next.XZ(y), color, duration, true);
            pre = next;
        }
    }

    public static void Create(Vector2 position, Color color, float size, AudioPath[] paths)
    {
        var go = new GameObject(Name);
        var pin = go.AddComponent<MapPin>();
        pin.pathsToDraw = paths;
        pin.color = color;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Resources.Load<Sprite>("Location");
        sr.color = color;
        go.transform.position = position.XZ(0.8f);
        go.transform.rotation = Quaternion.Euler(90, 0, 0);
        go.transform.localScale = new Vector3(size, size, size);
        
        Objects.Add(go);

        pin.pathsToDraw = paths.Select(x => x with
        {
            points = x.points.Select((p, i) =>
            {
                const float offset = 0.1f;
                if (i == x.points.Count - 1 || i == 0) return p;
                var randomOffset = new Vector2(Random.Range(-offset, offset), Random.Range(-offset, offset));
                return p + randomOffset;
            }).ToList()
        }).ToArray();
    }

    public static void CreateNumbered(Vector2 position, Color color, float size, int number, bool highlight,
        AudioPath[] paths)
    {
        var prefab = References.Singleton.numberedMapPinPrefab;
        var go = Instantiate(prefab);
        go.transform.position = position.XZ(highlight ? 1f : 0.8f);
        go.transform.localScale = new Vector3(size, size, size);
        go.name = Name;
        var pin = go.AddComponent<MapPin>();
        pin.color = color;
        foreach (var text in go.GetComponentsInChildren<TextMesh>())
        {
            if (highlight) text.fontStyle = FontStyle.Bold;
            if (text.name == "Number") text.text = number.ToString();
            text.color = color;
        }

        Objects.Add(go);

        pin.pathsToDraw = paths.Select(x => x with
        {
            points = x.points.Select((p, i) =>
            {
                const float offset = 0.1f;
                if (i == x.points.Count - 1 || i == 0) return p;
                var randomOffset = new Vector2(Random.Range(-offset, offset), Random.Range(-offset, offset));
                return p + randomOffset;
            }).ToList()
        }).ToArray();
    }

    void OnDestroy()
    {
        Objects.Remove(gameObject);
    }

    public static void Clear()
    {
        var obj = Objects.ToList();
        obj.ForEach(DestroyImmediate);
        Objects.Clear();
    }
}