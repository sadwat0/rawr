using UnityEngine;
using UnityEngine.UI;

public class ClickCooldownUI : MonoBehaviour
{
    [SerializeField] private Image radialImage;

    private RectTransform rt;
    private Sprite generatedCircleSprite;

    private void Awake()
    {
        rt = GetComponent<RectTransform>();
        radialImage ??= GetComponent<Image>();

        if (rt != null)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(20f, -20f);
            rt.sizeDelta = new Vector2(48f, 48f);
        }

        if (radialImage != null)
        {
            if (radialImage.sprite == null)
            {
                generatedCircleSprite = GenerateCircleSprite(64);
                radialImage.sprite = generatedCircleSprite;
            }

            radialImage.type = Image.Type.Filled;
            radialImage.fillMethod = Image.FillMethod.Radial360;
            radialImage.fillOrigin = (int)Image.Origin360.Top;
            radialImage.fillClockwise = true;
            radialImage.fillAmount = 0f;
            radialImage.preserveAspect = true;
            radialImage.raycastTarget = false;
            if (radialImage.color.a <= 0.001f)
                radialImage.color = new Color(1f, 1f, 1f, 0.6f);
        }
    }

    private void Update()
    {
        if (radialImage == null) return;

        var p = PlayerNetwork.localPlayer;
        float remaining = p != null ? p.GetCellClickCooldownRemaining() : 0f;

        radialImage.enabled = remaining > 0f;
        if (radialImage.enabled)
            radialImage.fillAmount = 1f - (remaining / 0.5f);
    }

    private static Sprite GenerateCircleSprite(int size)
    {
        size = Mathf.Clamp(size, 16, 256);
        var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        float r = (size - 1) * 0.5f;
        float cx = r;
        float cy = r;
        float inner = r - 1.5f;

        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                float d = Mathf.Sqrt(dx * dx + dy * dy);

                byte a;
                if (d <= inner) a = 255;
                else if (d <= r) a = (byte)Mathf.RoundToInt(255f * (1f - (d - inner) / (r - inner)));
                else a = 0;

                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, true);

        var rect = new Rect(0, 0, size, size);
        var pivot = new Vector2(0.5f, 0.5f);
        return Sprite.Create(tex, rect, pivot, 100f);
    }
}
