using System.Collections.Generic;
using UnityEngine;

public class HeatmapTile : MonoBehaviour
{
    const float Size = 16f;
    static readonly List<GameObject> Objects = new();
    
    public static void Create(Vector2 position, Color color)
    {
        var go = new GameObject("TempPin");
        var pin = go.AddComponent<HeatmapTile>();
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Resources.Load<Sprite>("white8x8");
        sr.color = color;
        go.transform.position = position.XZ();
        go.transform.rotation = Quaternion.Euler(90, 0, 0);
        go.transform.localScale = new Vector3(Size, Size, Size);
        Objects.Add(go);
    }
    
    public static void Clear()
    {
        Objects.ForEach(DestroyImmediate);
        Objects.Clear();
    }
}