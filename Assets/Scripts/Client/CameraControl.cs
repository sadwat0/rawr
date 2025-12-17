using UnityEngine;
using UnityEngine.InputSystem;

public class CameraControl : MonoBehaviour
{
    public Transform cameraTransform;
    public float moveSpeed = 20f;
    public float edgeScrollingMargin = 15f;
    public Vector4 borders;

    [Header("Auto center")]
    public bool centerOnStartCell = true;
    public float centerRetrySeconds = 2f;

    [Header("Auto borders")]
    public bool autoBordersFromGrid = true;
    public float borderPadding = 2f;

    private bool centeredOnce;
    private float centerRetryUntil;
    private bool bordersInitialized;

    private Vector2 moveInput;
    private Vector2 edgeScrollInput;
    
    void OnMove(InputValue value) => moveInput = value.Get<Vector2>();

    private void OnEnable()
    {
        PlayerNetwork.ClientOnPlayerUpdated += OnPlayerUpdated;
        GridElement.ClientOnGridChanged += OnGridChanged;
    }

    private void OnDisable()
    {
        PlayerNetwork.ClientOnPlayerUpdated -= OnPlayerUpdated;
        GridElement.ClientOnGridChanged -= OnGridChanged;
    }

    private void Start()
    {
        if (centerOnStartCell)
            centerRetryUntil = Time.unscaledTime + centerRetrySeconds;
    }

    void LateUpdate()
    {
        float deltaT = Time.unscaledDeltaTime;

        if (autoBordersFromGrid && !bordersInitialized)
            TryUpdateBordersFromGrid();

        if (centerOnStartCell && !centeredOnce && Time.unscaledTime <= centerRetryUntil)
            TryCenterOnStartCell();

        if (!Application.isEditor)
        {
            UpdateScroll();
        }
        UpdateMovement(deltaT);
    }

    private void OnPlayerUpdated(PlayerNetwork _) => RefreshCameraState();

    private void OnGridChanged() => RefreshCameraState();

    private void RefreshCameraState()
    {
        if (autoBordersFromGrid) TryUpdateBordersFromGrid();
        if (centerOnStartCell && !centeredOnce) TryCenterOnStartCell();
    }

    private void TryCenterOnStartCell()
    {
        if (cameraTransform == null) return;

        var p = PlayerNetwork.localPlayer;
        if (p == null || p.playerId <= 0) return;

        if (!TryGetBestOwnedCell(p.playerId, out var targetCell)) return;

        var pos = cameraTransform.position;
        var target = targetCell.transform.position;
        cameraTransform.position = new Vector3(
            Mathf.Clamp(target.x, borders.x, borders.z),
            Mathf.Clamp(target.y, borders.y, borders.w),
            pos.z);
        centeredOnce = true;
    }

    private bool TryGetBestOwnedCell(int playerId, out GridElement best)
    {
        best = null;
        uint bestForce = 0;

        var cells = FindObjectsByType<GridElement>(FindObjectsSortMode.None);
        if (cells.Length == 0) return false;

        foreach (var c in cells)
        {
            if (c.Owner != playerId) continue;
            if (best == null || c.Force > bestForce)
            {
                best = c;
                bestForce = c.Force;
            }
        }

        return best != null;
    }

    private bool TryGetGridBounds(out Bounds bounds)
    {
        bounds = default;
        var cells = FindObjectsByType<GridElement>(FindObjectsSortMode.None);
        if (cells.Length == 0) return false;

        bounds = new Bounds(cells[0].transform.position, Vector3.zero);
        for (int i = 1; i < cells.Length; i++)
            bounds.Encapsulate(cells[i].transform.position);

        return true;
    }

    private void TryUpdateBordersFromGrid()
    {
        if (!TryGetGridBounds(out var b)) return;
        UpdateBordersFromBounds(b);
        bordersInitialized = true;
    }

    private void UpdateBordersFromBounds(Bounds bounds)
    {
        float viewX = 0f, viewY = 0f;
        
        if (cameraTransform.TryGetComponent<Camera>(out var cam) && cam.orthographic)
        {
            viewY = cam.orthographicSize;
            viewX = viewY * cam.aspect;
        }

        float totalPadX = borderPadding + viewX;
        float totalPadY = borderPadding + viewY;

        borders = new Vector4(
            bounds.min.x - totalPadX,
            bounds.min.y - totalPadY,
            bounds.max.x + totalPadX,
            bounds.max.y + totalPadY
        );
    }

    void UpdateScroll()
    {
        if (Mouse.current == null) return;
        
        var mousePos = Mouse.current.position.ReadValue();
        edgeScrollInput.x = GetEdgeScroll(mousePos.x, Screen.width);
        edgeScrollInput.y = GetEdgeScroll(mousePos.y, Screen.height);
    }

    float GetEdgeScroll(float pos, float size)
    {
        if (pos <= edgeScrollingMargin) return -1f;
        if (pos >= size - edgeScrollingMargin) return 1f;
        return 0f;
    }

    void UpdateMovement(float deltaT)
    {
        var delta = (moveInput + edgeScrollInput).normalized * moveSpeed * deltaT;
        var pos = cameraTransform.position + new Vector3(delta.x, delta.y, 0);
        
        cameraTransform.position = new Vector3(
            Mathf.Clamp(pos.x, borders.x, borders.z),
            Mathf.Clamp(pos.y, borders.y, borders.w),
            pos.z);
    }
}
