using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Experimental.Rendering;
using UnityEngine.UI;
using UnityEngine.UIElements;

public class UI : SingletonBehaviour<UI>, IPointerClickHandler, IPointerMoveHandler
{
    [SerializeField] Text screenText;
    [SerializeField] Text bottomText;
    [SerializeField] SpriteRenderer mapPin;
    [SerializeField] RawImage map;
    [SerializeField] Camera mapCamera;
    [SerializeField] float pressToConfirmSeconds = 0.5f;

    void OnEnable()
    {
        bottomText.text = screenText.text = "";
        var mapTransform = map.GetComponent<RectTransform>().rect;
        map.texture = new RenderTexture((int)mapTransform.width, (int)mapTransform.height,
            GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.D32_SFloat_S8_UInt);
    }

    void OnDisable()
    {
        Destroy(map.texture);
        map.texture = null;
    }

    Vector3? PointerMapLocation(PointerEventData pointer)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                map.rectTransform, pointer.position, pointer.pressEventCamera, out var localCursor))
            return null;
        var mapRect = map.rectTransform.rect;
        var coord = (localCursor - mapRect.position) / mapRect.size;
        var localCursor01 = new Vector2(Mathf.Clamp01(coord.x), Mathf.Clamp01(coord.y));
        
        if (localCursor != coord)
        {
            Debug.Log("Out of map");
            return null;
        }

        // Cast map ray to world
        mapCamera.aspect = map.rectTransform.rect.width / map.rectTransform.rect.height;
        var mapRay = mapCamera.ScreenPointToRay(localCursor01 * mapCamera.pixelRect.size);

        Debug.DrawRay(mapRay.origin, mapRay.direction * 1000, Color.red, 1.0f);
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

    void IPointerMoveHandler.OnPointerMove(PointerEventData evt)
    {
        if (PointerMapLocation(evt) is { } result) mapPin.transform.position = result;
    }

    void IPointerClickHandler.OnPointerClick(PointerEventData evt)
    {
        if (PointerMapLocation(evt) is { } result) mapPin.transform.position = result;
    }

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

    public string SideText
    {
        get => bottomText.text;
        set => bottomText.text = value;
    }

    public IEnumerator Prompt(string message)
    {
        screenText.text = message;
        bottomText.text = "[Hold space to continue]";
        yield return WaitForKeyHold(KeyCode.Space, Application.isEditor ? KeyCode.X : KeyCode.None);
        bottomText.text = screenText.text = "";
    }


    IEnumerator WaitForKeyHold(KeyCode keyCode, KeyCode skip = KeyCode.None)
    {
        while (true)
        {
            yield return new WaitUntil(() => Input.GetKeyDown(keyCode) || Input.GetKey(skip));
            if (Input.GetKey(skip))
                break;
            var pressStart = References.Now;
            yield return new WaitUntil(() =>
                !Input.GetKey(KeyCode.Space) || References.Now - pressStart > pressToConfirmSeconds);
            if (Input.GetKey(KeyCode.Space))
                break;
        }
    }
}