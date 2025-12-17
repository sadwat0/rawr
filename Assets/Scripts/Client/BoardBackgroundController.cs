using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Client-side board background: a tiled ocean under the board and a sand "beach" ring around it.
/// It builds itself once the network-spawned GridElement objects exist on the client.
/// </summary>
public sealed class BoardBackgroundController : MonoBehaviour
{
    [Header("Ocean")]
    [Tooltip("Resources path without extension. Example: Textures/OceanPixel")]
    [SerializeField] private string oceanTextureResourcePath = "Textures/OceanPixel";

    [Tooltip("Pixels Per Unit for the ocean sprite (higher => smaller tile in world)")]
    [SerializeField] private float oceanPixelsPerUnit = 256f;

    [Tooltip("Make ocean cover camera movement borders when possible")]
    [SerializeField] private bool oceanFitToCameraBorders = true;

    [Tooltip("Extra world padding for ocean on EACH side")]
    [SerializeField] private float oceanExtraPadding = 30f;

    [Header("Beach")]
    [Tooltip("Scale multiplier for sand underlay hexes")]
    [SerializeField] private float beachHexScale = 1.5f;

    [Tooltip("Sorting order offset below the hex sprite")]
    [SerializeField] private int beachSortingOffset = -10;

    [Tooltip("Sand color")]
    [SerializeField] private Color32 sandColor = new(216, 198, 155, 255);

    [Header("Timing")]
    [Tooltip("How long to wait for GridElement to appear on client")]
    [SerializeField] private float waitForGridTimeoutSeconds = 10f;

    [Tooltip("Small delay after first GridElement appears (to let the rest spawn)")]
    [SerializeField] private float settleDelaySeconds = 0.25f;

    private SpriteRenderer oceanRenderer;
    private Transform sandContainer;
    private Coroutine buildRoutine;

    private void Start()
    {
        StartBuild();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene _, LoadSceneMode __)
    {
        // Mirror scene changes won't re-run RuntimeInitializeOnLoadMethod, so we rebuild on each scene load.
        StartBuild();
    }

    private void StartBuild()
    {
        if (buildRoutine != null) StopCoroutine(buildRoutine);
        buildRoutine = StartCoroutine(BuildWhenReady());
    }

    private IEnumerator BuildWhenReady()
    {
        // Clear any previous sprites so menu scenes don't keep an old board background.
        if (oceanRenderer != null) oceanRenderer.sprite = null;
        ClearSand();

        float until = Time.unscaledTime + Mathf.Max(0.1f, waitForGridTimeoutSeconds);

        while (Time.unscaledTime < until)
        {
            var cells = FindObjectsByType<GridElement>(FindObjectsSortMode.None);
            if (cells.Length > 0)
                break;

            yield return null;
        }

        var allCells = FindObjectsByType<GridElement>(FindObjectsSortMode.None);
        if (allCells.Length == 0)
            yield break;

        if (settleDelaySeconds > 0f)
            yield return new WaitForSecondsRealtime(settleDelaySeconds);

        Build(allCells);
    }

    private void Build(GridElement[] cells)
    {
        if (!TryGetGridBounds(cells, out var bounds, out var sampleSpriteRenderer)) return;

        int sortingLayerId = sampleSpriteRenderer != null ? sampleSpriteRenderer.sortingLayerID : 0;
        int baseSortingOrder = sampleSpriteRenderer != null ? sampleSpriteRenderer.sortingOrder : 0;

        BuildSandUnderlay(cells);

        EnsureOceanRenderer();
        oceanRenderer.sortingLayerID = sortingLayerId;
        oceanRenderer.sortingOrder = baseSortingOrder - 100;
        oceanRenderer.transform.position = new Vector3(bounds.center.x, bounds.center.y, 0f);

        Texture2D oceanTex = Resources.Load<Texture2D>(oceanTextureResourcePath);
        ConfigureOceanTexture(oceanTex);
        
        oceanRenderer.sprite = oceanTex != null 
            ? CreateOceanSprite(oceanTex) 
            : CreateSolidSprite(new Color32(20, 80, 140, 255), 8);
        
        oceanRenderer.drawMode = SpriteDrawMode.Tiled;
        ApplyOceanSizing(bounds);
    }

    private void ConfigureOceanTexture(Texture2D tex)
    {
        if (tex == null) return;
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Point;
        tex.anisoLevel = 0;
    }

    private Sprite CreateOceanSprite(Texture2D tex)
    {
        return Sprite.Create(
            tex,
            new Rect(0, 0, tex.width, tex.height),
            new Vector2(0.5f, 0.5f),
            Mathf.Max(1f, oceanPixelsPerUnit),
            0,
            SpriteMeshType.FullRect
        );
    }

    private static bool TryGetGridBounds(GridElement[] cells, out Bounds bounds, out SpriteRenderer sample)
    {
        bounds = default;
        sample = null;

        if (cells.Length == 0) return false;

        var firstSr = cells[0].GetComponent<SpriteRenderer>();
        sample = firstSr;
        bounds = firstSr != null ? firstSr.bounds : new Bounds(cells[0].transform.position, Vector3.zero);

        for (int i = 1; i < cells.Length; i++)
        {
            var sr = cells[i].GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sample ??= sr;
                bounds.Encapsulate(sr.bounds);
            }
            else
            {
                bounds.Encapsulate(cells[i].transform.position);
            }
        }

        return true;
    }

    private void EnsureOceanRenderer()
    {
        if (oceanRenderer != null) return;
        var go = new GameObject("Ocean");
        go.transform.SetParent(transform, false);
        oceanRenderer = go.AddComponent<SpriteRenderer>();
    }

    private void ApplyOceanSizing(Bounds gridBounds)
    {
        float pad = Mathf.Max(0f, oceanExtraPadding);

        // Prefer covering the whole camera movement range (from CameraControl) so the ocean is never clipped.
        if (oceanFitToCameraBorders)
        {
            var camControl = FindFirstObjectByType<CameraControl>();
            if (camControl != null)
            {
                Vector4 b = camControl.borders; // (minX, minY, maxX, maxY)
                float w = b.z - b.x;
                float h = b.w - b.y;
                if (w > 0.01f && h > 0.01f)
                {
                    oceanRenderer.transform.position = new Vector3((b.x + b.z) * 0.5f, (b.y + b.w) * 0.5f, 0f);
                    oceanRenderer.size = new Vector2(w + pad * 2f, h + pad * 2f);
                    return;
                }
            }
        }

        // Fallback: just cover the grid bounds with padding.
        oceanRenderer.transform.position = new Vector3(gridBounds.center.x, gridBounds.center.y, 0f);
        oceanRenderer.size = new Vector2(gridBounds.size.x + pad * 2f, gridBounds.size.y + pad * 2f);
    }

    private void EnsureSandContainer()
    {
        if (sandContainer != null) return;
        var go = new GameObject("Sand");
        go.transform.SetParent(transform, false);
        sandContainer = go.transform;
    }

    private void ClearSand()
    {
        if (sandContainer == null) return;
        for (int i = sandContainer.childCount - 1; i >= 0; i--)
            Destroy(sandContainer.GetChild(i).gameObject);
    }

    private void BuildSandUnderlay(GridElement[] cells)
    {
        EnsureSandContainer();
        ClearSand();

        float scale = Mathf.Max(1f, beachHexScale);

        foreach (var cell in cells)
        {
            var sr = cell.GetComponent<SpriteRenderer>();
            if (sr == null || sr.sprite == null) continue;

            var go = new GameObject("SandUnderlay");
            go.transform.SetParent(sandContainer, false);
            go.transform.SetPositionAndRotation(sr.transform.position, sr.transform.rotation);
            go.transform.localScale = sr.transform.lossyScale * scale;

            var sandSr = go.AddComponent<SpriteRenderer>();
            sandSr.sprite = sr.sprite;
            sandSr.color = sandColor;
            sandSr.sortingLayerID = sr.sortingLayerID;
            sandSr.sortingOrder = sr.sortingOrder + beachSortingOffset;
        }
    }

    private static Sprite CreateSolidSprite(Color32 color, int sizePx)
    {
        int s = Mathf.Clamp(sizePx, 2, 64);
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Point;

        var pixels = new Color32[s * s];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        tex.SetPixels32(pixels);
        tex.Apply(false, true);

        return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 16f);
    }
}

public static class BoardBackgroundBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateIfMissing()
    {
        if (Object.FindFirstObjectByType<BoardBackgroundController>() != null)
            return;

        var go = new GameObject("BoardBackground");
        go.AddComponent<BoardBackgroundController>();
        Object.DontDestroyOnLoad(go);
    }
}
