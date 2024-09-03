using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.UI;

public class Map : SingletonBehaviour<Map>
{
    [SerializeField] public SpriteRenderer mapPin;
    [SerializeField] public SpriteRenderer playerPin;
    [SerializeField] public RawImage map;
    [SerializeField] public Camera mapCamera;
    void Start()
    {
        enabled = false;
        mapPin.color = Color.clear;
    }

    bool _isFocused = true;

    public bool IsFocused
    {
        get => _isFocused;
        set
        {
            if (_isFocused == value) return;
            _isFocused = value;
            if (value) enabled = true;
            References.Player.ShowMouse = value;
            UI.Singleton.screenText.enabled = !value;
            if (value)
            {
                map.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, StudySettings.MapSizeFocused);
                map.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, StudySettings.MapSizeFocused);
                map.rectTransform.pivot =
                    map.rectTransform.anchorMin = map.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                map.rectTransform.localPosition =
                    new Vector3(0, 0, 0);
            }
            else
            {
                map.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, StudySettings.MapSizeUnfocused);
                map.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, StudySettings.MapSizeUnfocused);
                map.rectTransform.localPosition = new Vector3(0, 0, 0);
                map.rectTransform.pivot = map.rectTransform.anchorMin = map.rectTransform.anchorMax = new Vector2(1, 1);
            }
        }
    }

    void OnEnable()
    {
        var mapTransform = map.GetComponent<RectTransform>().rect;
        map.texture = new RenderTexture((int)mapTransform.width, (int)mapTransform.height,
            GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.D32_SFloat_S8_UInt);
        mapPin.enabled = map.enabled = mapCamera.enabled = true;
    }

    void OnDisable()
    {
        Destroy(map.texture);
        map.texture = null;
        mapPin.enabled = map.enabled = mapCamera.enabled = false;
        IsFocused = false;
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
        playerPin.transform.position = References.Player.transform.position.WithY(0);
        playerPin.transform.rotation =
            Quaternion.Euler(90, References.Player.camera.transform.rotation.eulerAngles.y, 0);
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