using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Experimental.Rendering;
using UnityEngine.UI;

public class UI : SingletonBehaviour<UI>, IPointerClickHandler
{
    [SerializeField] Text screenText;
    [SerializeField] Text bottomText;
    [SerializeField] RawImage map;
    [SerializeField] Camera mapCamera;
    [SerializeField] float pressToConfirmSeconds = 0.5f;

    [SerializeField] private Vector2 Offset;

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
    
    public void OnPointerClick(PointerEventData eventData)
    {
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(map.rectTransform, eventData.pressPosition, eventData.pressEventCamera, out var localCursor))
        {
            var texSize = new Vector2(map.texture.width, map.texture.height);
            var r = map.rectTransform.rect;
            
            //Using the size of the texture and the local cursor, clamp the X,Y coords between 0 and width - height of texture
            var coord = (localCursor - r.position) * texSize / r.size;
            var coordX = Mathf.Clamp(coord.x, 0, texSize.x);
            var coordY = Mathf.Clamp(coord.y, 0, texSize.y);
            
            //Convert coordX and coordY to % (0.0-1.0) with respect to texture width and height
            var recalcX = coordX / texSize.x;
            var recalcY = coordY / texSize.y;
            
            localCursor = new Vector2(recalcX, recalcY);
            
            CastMiniMapRayToWorld(localCursor);
        }
        
        void CastMiniMapRayToWorld(Vector2 localCursor)
        {
            mapCamera.aspect = map.rectTransform.rect.width / map.rectTransform.rect.height;
            var mapRay = mapCamera.ScreenPointToRay(new Vector2(localCursor.x * mapCamera.pixelWidth, localCursor.y * mapCamera.pixelHeight));

            Debug.DrawRay(mapRay.origin, mapRay.direction * 1000, Color.red, 3.0f);
            if (XZIntersection(mapRay) is { } result) References.AudioPosition = result;
            return;
            
            Vector3? XZIntersection(Ray ray)
            {
                // Check if the ray is parallel to the XZ plane (i.e., its direction has no y component)
                if (Mathf.Abs(ray.direction.y) < Mathf.Epsilon)
                    return null; // The ray is parallel to the XZ plane and never intersects

                // Calculate the distance along the ray where it intersects the XZ plane
                var t = -ray.origin.y / ray.direction.y;

                // If t is negative, the intersection is behind the ray's origin
                if (t < 0)
                    return null;

                return ray.origin + t * ray.direction;
            }
        }
    }

    void Update()
    {
        

        if (map.enabled)
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