using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Eyelid-style blink: black bars close from the top and bottom of the screen.
public class ScreenBlink : MonoBehaviour
{
    public static ScreenBlink Instance { get; private set; }

    RectTransform top, bottom;

    void Awake()
    {
        Instance = this;
        var go = new GameObject("Blink Canvas");
        go.transform.SetParent(transform, false);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

        top = MakeLid(go.transform, new Vector2(0f, 1f));
        bottom = MakeLid(go.transform, new Vector2(0f, 0f));
        Apply(0f);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    static RectTransform MakeLid(Transform parent, Vector2 edge)
    {
        var rect = new GameObject("Lid", typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = edge;
        rect.anchorMax = new Vector2(1f, edge.y);
        rect.pivot = new Vector2(0.5f, edge.y);
        rect.anchoredPosition = Vector2.zero;
        var image = rect.gameObject.AddComponent<Image>();
        image.color = Color.black;
        image.raycastTarget = false;
        return rect;
    }

    // 0 = eyes open, 1 = fully closed.
    void Apply(float closed)
    {
        float height = Screen.height * 0.52f * closed;
        var canvas = top.GetComponentInParent<Canvas>();
        float scale = canvas != null ? canvas.scaleFactor : 1f;
        top.sizeDelta = new Vector2(0f, height / scale);
        bottom.sizeDelta = new Vector2(0f, height / scale);
        top.gameObject.SetActive(closed > 0f);
        bottom.gameObject.SetActive(closed > 0f);
    }

    public IEnumerator Close(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            Apply(Mathf.SmoothStep(0f, 1f, t / seconds));
            yield return null;
        }
        Apply(1f);
    }

    public IEnumerator Open(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            Apply(Mathf.SmoothStep(1f, 0f, t / seconds));
            yield return null;
        }
        Apply(0f);
    }

    public IEnumerator Blink(float closeSeconds, float openSeconds)
    {
        yield return Close(closeSeconds);
        yield return Open(openSeconds);
    }
}
