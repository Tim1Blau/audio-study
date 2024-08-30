using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.UI;

public class LocalizationMap : SingletonBehaviour<LocalizationMap>
{
    [SerializeField] public SpriteRenderer mapPin;
    [SerializeField] RawImage map;
    [SerializeField] Camera mapCamera;

    private void Start()
    {
        enabled = false;
    }

    void OnEnable()
    {
        References.Player.ShowMouse = true;
        var mapTransform = map.GetComponent<RectTransform>().rect;
        map.texture = new RenderTexture((int)mapTransform.width, (int)mapTransform.height,
            GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.D32_SFloat_S8_UInt);
        mapPin.enabled = map.enabled = mapCamera.enabled = true;
    }

    void OnDisable()
    {
        References.Player.ShowMouse = false;
        Destroy(map.texture);
        map.texture = null;
        mapPin.enabled = map.enabled = mapCamera.enabled = false;
    }

    public Vector3? PointerToWorldPosition()
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                map.rectTransform, Input.mousePosition, Camera.main, out var localCursor))
            return null;
        var mapRect = map.rectTransform.rect;
        var coord = (localCursor - mapRect.position) / mapRect.size;
        var localCursor01 = new Vector2(Mathf.Clamp01(coord.x), Mathf.Clamp01(coord.y));

        // Cast map ray to world
        mapCamera.aspect = map.rectTransform.rect.width / map.rectTransform.rect.height;
        var mapRay = mapCamera.ScreenPointToRay(localCursor01 * mapCamera.pixelRect.size);

        return XZIntersection(mapRay);

        Vector3? XZIntersection(Ray ray)
        {
            if (Mathf.Abs(ray.direction.y) < Mathf.Epsilon) // parallel to plane 
                return null;
            var distanceTo0 = -ray.origin.y / ray.direction.y;
            if (distanceTo0 < 0) // behind plane 
                return null;
            return ray.origin + distanceTo0 * ray.direction;
        }
    }

    #region Rendering

    void Update()
    {
        if (map.enabled) RenderMap();
    }

    void RenderMap()
    {
        mapCamera.targetTexture = map.texture as RenderTexture;
        mapCamera.enabled = true;
        mapCamera.cullingMask = LayerMask.GetMask("Default");
        mapCamera.Render();
        var clear = mapCamera.clearFlags;
        mapCamera.clearFlags = CameraClearFlags.Nothing;
        mapCamera.cullingMask = LayerMask.GetMask("MapIcon");
        mapCamera.Render();
        mapCamera.clearFlags = clear;
        mapCamera.targetTexture = null;
    }

    #endregion
}