using UnityEngine;
using TMPro;
using System;
using Mirror;

public enum CellType
{
    Blank,
    Resource,
    Powerup
}

public class GridElement : NetworkBehaviour
{
    [SerializeField] private TextMeshPro forceText;
    [SerializeField] private Color[] playerColors;
    [SerializeField] private CellType cellType = CellType.Blank;
    
    [SyncVar(hook = nameof(OnOwnerChanged))]
    private int owner;
    
    [SyncVar(hook = nameof(OnForceChanged))]
    private uint force;

    [SyncVar(hook = nameof(OnIsGoldMineChanged))]
    private bool isGoldMine;

    [SyncVar(hook = nameof(OnEmpEndTimeChanged))]
    private double empEndTime;

    [SyncVar(hook = nameof(OnTrapOwnerChanged))]
    private int trapOwnerId;
    
    private SpriteRenderer spriteRenderer;
    private LineRenderer goldMineOutlineCore;
    private LineRenderer goldMineOutlineGlow;
    private LineRenderer trapOutline;
    
    public int Owner => owner;
    public uint Force => force;
    public bool IsGoldMine => isGoldMine;
    public CellType CellType => cellType;
    public bool IsEmpActive => NetworkTime.time < empEndTime;
    public int TrapOwnerId => trapOwnerId;
    public bool HasTrap => trapOwnerId > 0;

    public static event Action ClientOnGridChanged;

    public bool TryGetPlayerColor(int playerId, out Color color)
    {
        if (playerColors == null || playerColors.Length == 0)
        {
            color = Color.white;
            return false;
        }

        if (playerId >= 0 && playerId < playerColors.Length)
        {
            color = playerColors[playerId];
            return true;
        }

        color = playerColors[0];
        return false;
    }
    
    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }
    
    private void OnMouseDown()
    {
        if (!isClient) return;
        
        var player = PlayerNetwork.localPlayer;
        if (player == null) return;
        player.ClientTryClickCell(netId);
    }
    
    public void Initialize(int initialOwner, uint initialForce)
    {
        if (!NetworkServer.active) return;
        owner = initialOwner;
        force = initialForce;
        UpdateVisuals();
    }
    
    public override void OnStartClient()
    {
        base.OnStartClient();
        UpdateVisuals();
        ClientOnGridChanged?.Invoke();
    }
    
    [Server]
    public void SetOwner(int newOwner) => owner = newOwner;
    
    [Server]
    public void SetForce(uint newForce) => force = newForce;

    [Server]
    public void SetGoldMine(bool value) => isGoldMine = value;

    [Server]
    public void SetEmpFrozen(double endTime) => empEndTime = endTime;

    [Server]
    public void SetTrapOwner(int ownerId) => trapOwnerId = ownerId;

    [Server]
    public void ClearTrap() => trapOwnerId = 0;

    void OnOwnerChanged(int oldOwner, int newOwner) => UpdateAndNotify();
    void OnForceChanged(uint oldForce, uint newForce) => UpdateAndNotify();
    void OnIsGoldMineChanged(bool oldValue, bool newValue) => UpdateAndNotify();
    void OnEmpEndTimeChanged(double _, double __) => UpdateAndNotify();
    void OnTrapOwnerChanged(int _, int __) => UpdateAndNotify();

    private void UpdateAndNotify()
    {
        UpdateVisuals();
        ClientOnGridChanged?.Invoke();
    }
    
    private void UpdateVisuals()
    {
        spriteRenderer ??= GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
            spriteRenderer.color = GetBaseColor();

        if (isGoldMine)
            EnsureGoldMineOutline();
        SetGoldMineOutlineVisible(isGoldMine);

        UpdateTrapVisual();
        
        forceText ??= GetComponentInChildren<TextMeshPro>();
        if (forceText != null) forceText.text = force.ToString();
    }

    private bool empVisualWasActive;

    private void Update()
    {
        if (!isClient || empEndTime <= 0) return;

        bool active = NetworkTime.time < empEndTime;
        if (active != empVisualWasActive)
        {
            empVisualWasActive = active;
            UpdateAndNotify();
        }
    }

    private Color GetBaseColor()
    {
        Color color = Color.white;
        if (playerColors != null && playerColors.Length > 0)
        {
            int idx = (owner >= 0 && owner < playerColors.Length) ? owner : 0;
            color = playerColors[idx];
        }

        if (!IsEmpActive) return color;

        var ice = new Color(0.6f, 0.8f, 1f, 1f);
        return Color.Lerp(color, ice, 0.75f);
    }

    private void EnsureGoldMineOutline()
    {
        if (goldMineOutlineCore != null && goldMineOutlineGlow != null) return;

        var existing = transform.Find("GoldMineOutline");
        if (existing == null)
        {
            var root = new GameObject("GoldMineOutline");
            root.transform.SetParent(transform, false);
            existing = root.transform;
        }

        goldMineOutlineGlow = EnsureLine(existing, "Glow", 0.065f, new Color(1f, 0.8f, 0.1f, 0.25f));
        goldMineOutlineCore = EnsureLine(existing, "Core", 0.03f, new Color(1f, 0.85f, 0.2f, 0.9f));

        ApplyHexOutline(goldMineOutlineGlow, 0.86f);
        ApplyHexOutline(goldMineOutlineCore, 0.86f);
        SyncOutlineSorting();
    }

    private void SyncOutlineSorting()
    {
        if (spriteRenderer == null) return;

        if (goldMineOutlineGlow != null)
        {
            goldMineOutlineGlow.sortingLayerID = spriteRenderer.sortingLayerID;
            goldMineOutlineGlow.sortingOrder = spriteRenderer.sortingOrder + 1;
        }

        if (goldMineOutlineCore != null)
        {
            goldMineOutlineCore.sortingLayerID = spriteRenderer.sortingLayerID;
            goldMineOutlineCore.sortingOrder = spriteRenderer.sortingOrder + 2;
        }
    }

    private void SetGoldMineOutlineVisible(bool visible)
    {
        if (goldMineOutlineGlow != null) goldMineOutlineGlow.enabled = visible;
        if (goldMineOutlineCore != null) goldMineOutlineCore.enabled = visible;
    }

    private static LineRenderer EnsureLine(Transform root, string name, float width, Color color)
    {
        Transform child = root.Find(name);
        if (child == null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            child = go.transform;
        }

        var lr = child.GetComponent<LineRenderer>();
        if (lr == null)
            lr = child.gameObject.AddComponent<LineRenderer>();

        lr.useWorldSpace = false;
        lr.loop = true;
        lr.numCornerVertices = 0;
        lr.numCapVertices = 0;
        lr.alignment = LineAlignment.TransformZ;
        lr.textureMode = LineTextureMode.Stretch;
        lr.widthMultiplier = width;
        lr.startColor = color;
        lr.endColor = color;
        lr.sharedMaterial = GoldMineLineMaterial.Get();

        return lr;
    }

    private void ApplyHexOutline(LineRenderer lr, float scale)
    {
        if (lr == null) return;

        if (TryGetComponent<PolygonCollider2D>(out var poly) && poly != null)
        {
            var points = poly.points;
            lr.positionCount = points.Length;
            for (int i = 0; i < points.Length; i++)
                lr.SetPosition(i, new Vector3(points[i].x * scale, points[i].y * scale, 0f));
            return;
        }

        Vector3[] fallback =
        {
            new(0.25f, 0.43359375f, 0f),
            new(0.5f, 0f, 0f),
            new(0.25f, -0.43359375f, 0f),
            new(-0.2536364f, -0.43359375f, 0f),
            new(-0.5f, 0f, 0f),
            new(-0.25f, 0.43359375f, 0f),
        };

        lr.positionCount = fallback.Length;
        for (int i = 0; i < fallback.Length; i++)
            lr.SetPosition(i, fallback[i] * scale);
    }

    private static class GoldMineLineMaterial
    {
        private static Material material;

        public static Material Get()
        {
            if (material != null) return material;
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) return null;
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            return material;
        }
    }

    private void UpdateTrapVisual()
    {
        if (!isClient) return;

        var local = PlayerNetwork.localPlayer;
        bool show = local != null && local.playerId > 0 && trapOwnerId == local.playerId;

        if (!show)
        {
            if (trapOutline != null) trapOutline.enabled = false;
            return;
        }

        if (trapOutline == null)
        {
            var root = transform.Find("TrapOutline");
            if (root == null)
            {
                var go = new GameObject("TrapOutline");
                go.transform.SetParent(transform, false);
                root = go.transform;
            }

            trapOutline = EnsureLine(root, "Core", 0.04f, new Color(1f, 0.2f, 0.2f, 0.9f));
            ApplyHexOutline(trapOutline, 0.74f);
            if (spriteRenderer != null)
            {
                trapOutline.sortingLayerID = spriteRenderer.sortingLayerID;
                trapOutline.sortingOrder = spriteRenderer.sortingOrder + 3;
            }
        }

        trapOutline.enabled = true;
    }
}