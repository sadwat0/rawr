using UnityEngine;
using Mirror;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class GridManager : NetworkBehaviour
{
    [SerializeField] private int gridWidth = 10;
    [SerializeField] private int gridHeight = 10;
    [SerializeField] private GameObject hexagonPrefab;
    [SerializeField] private float hexSize = 1f;
    [SerializeField] private int maxPlayers = 4;
    
    private GridElement[,] grid;
    private Dictionary<GridElement, Vector2Int> elementToCoord = new();
    private readonly Dictionary<int, Vector2Int> spawnByPlayerId = new();
    
    public static GridManager Instance { get; private set; }
    
    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }
    
    public override void OnStartServer()
    {
        base.OnStartServer();
        InitializeGrid();
        DetermineHexSizeFromPrefab();
        StartCoroutine(WaitForPlayersThenAssignCorners());
    }
    
    [Server]
    public void EnsureStartingCornerForPlayer(int playerId)
    {
        if (TryAssignStartingCornerNow(playerId))
            return;

        StartCoroutine(AssignStartingCornerWhenReady(playerId, 5f));
    }

    [Server]
    private bool TryAssignStartingCornerNow(int playerId)
    {
        if (playerId <= 0 || playerId > maxPlayers)
            return true;

        if (grid == null)
            return false;

        Vector2Int spawnCoord = GetRuntimeSpawnCoord(playerId);
        if (spawnCoord.x < 0 || spawnCoord.x >= gridWidth || spawnCoord.y < 0 || spawnCoord.y >= gridHeight)
            return false;

        GridElement cornerCell = grid[spawnCoord.x, spawnCoord.y];
        if (cornerCell == null)
            return false;

        if (cornerCell.Owner != 0 && cornerCell.Owner != playerId)
            return true;

        if (cornerCell.Owner != playerId)
            cornerCell.SetOwner(playerId);

        // Starting cell should have strong initial "life"
        if (cornerCell.Force != 200)
            cornerCell.SetForce(200);

        return true;
    }

    [Server]
    private IEnumerator AssignStartingCornerWhenReady(int playerId, float timeoutSeconds)
    {
        float elapsed = 0f;
        while (elapsed < timeoutSeconds)
        {
            if (TryAssignStartingCornerNow(playerId))
                yield break;

            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        Debug.LogWarning($"[GridManager] Failed to assign starting corner for playerId={playerId} (timeout).");
    }

    private Vector2Int GetCornerForPlayerId(int playerId) => (playerId - 1) switch
    {
        0 => new Vector2Int(0, 0),
        1 => new Vector2Int(gridWidth - 1, 0),
        2 => new Vector2Int(0, gridHeight - 1),
        3 => new Vector2Int(gridWidth - 1, gridHeight - 1),
        _ => new Vector2Int(0, 0)
    };

    private System.Collections.IEnumerator WaitForPlayersThenAssignCorners()
    {
        yield return new WaitForSeconds(0.5f);
        
        float timeout = 10f;
        float elapsed = 0f;
        
        while (elapsed < timeout)
        {
            var players = FindObjectsByType<PlayerNetwork>(FindObjectsSortMode.None).ToList();
            if (players.Count > 0)
            {
                AssignCornerCells();
                yield break;
            }
            
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }
        
        Debug.LogWarning("[GridManager] Timeout waiting for players to assign corners!");
    }
    
    private void DetermineHexSizeFromPrefab()
    {
        if (hexagonPrefab == null) return;
        
        var sr = hexagonPrefab.GetComponentInChildren<SpriteRenderer>();
        if (sr?.sprite != null)
        {
            var scale = hexagonPrefab.transform.localScale;
            float width = sr.sprite.bounds.size.x * Mathf.Abs(scale.x);
            float height = sr.sprite.bounds.size.y * Mathf.Abs(scale.y);
            hexSize = Mathf.Max(width, height) * 0.5f;
        }
    }

    [Server]
    private void InitializeGrid()
    {
        elementToCoord.Clear();
        spawnByPlayerId.Clear();

        grid = new GridElement[gridWidth, gridHeight];

        string mapName = NetworkManagerRawr.SelectedMapName;
        var activeCells = GenerateActiveCellsForMap(mapName);

        // Precompute spawns so mines don't appear on start cells (and so we can place players even on "holes").
        for (int pid = 1; pid <= maxPlayers; pid++)
        {
            var corner = GetCornerForPlayerId(pid);
            var spawn = FindNearestAllowedCoord(corner, activeCells);
            if (spawn.x >= 0)
                spawnByPlayerId[pid] = spawn;
        }

        var goldMines = GenerateGoldMineCoords(activeCells);
        
        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                var coord = new Vector2Int(x, y);
                if (!activeCells.Contains(coord))
                {
                    grid[x, y] = null;
                    continue;
                }

                Vector3 position = GetHexagonWorldPosition(x, y);
                
                GameObject hexGo = Instantiate(hexagonPrefab, position, Quaternion.identity, transform);
                
                GridElement element = hexGo.GetComponent<GridElement>();
                if (element == null)
                {
                    Debug.LogError($"[GridManager] Prefab at ({x}, {y}) doesn't have GridElement component!");
                    Destroy(hexGo);
                    continue;
                }
                
                element.SetGoldMine(goldMines.Contains(coord));
                uint neutralForce = (uint)Random.Range(1, 51);
                element.Initialize(0, neutralForce);
                
                NetworkServer.Spawn(hexGo);
                
                grid[x, y] = element;
                elementToCoord[element] = coord;
            }
        }
        
        Debug.Log($"[GridManager] Created {elementToCoord.Count} grid cells (map='{mapName}')");
    }

    [Server]
    private HashSet<Vector2Int> GenerateGoldMineCoords(HashSet<Vector2Int> activeCells)
    {
        int cellCount = activeCells != null ? activeCells.Count : 0;
        int desired = Mathf.Max(maxPlayers, cellCount / 25);

        var blocked = new HashSet<Vector2Int>
        {
            GetPlannedSpawnCoord(1),
            GetPlannedSpawnCoord(2),
            GetPlannedSpawnCoord(3),
            GetPlannedSpawnCoord(4),
        };

        blocked.RemoveWhere(c => c.x < 0 || c.x >= gridWidth || c.y < 0 || c.y >= gridHeight);
        blocked.RemoveWhere(c => activeCells == null || !activeCells.Contains(c));
        int available = Mathf.Max(0, cellCount - blocked.Count);
        int mineCount = Mathf.Clamp(desired, 0, available);

        var result = new HashSet<Vector2Int>();
        if (mineCount <= 0) return result;

        var rng = new System.Random();
        var activeList = activeCells.ToList();
        int guard = 0;
        while (result.Count < mineCount && guard < Mathf.Max(50, cellCount * 20))
        {
            guard++;
            var coord = activeList[rng.Next(0, activeList.Count)];
            if (blocked.Contains(coord)) continue;
            result.Add(coord);
        }

        Debug.Log($"[GridManager] Gold mines: {result.Count}");
        return result;
    }
    
    [Server]
    private void AssignCornerCells()
    {
        var players = FindObjectsByType<PlayerNetwork>(FindObjectsSortMode.None)
            .OrderBy(p => p.playerId)
            .ToList();
        
        if (players.Count == 0)
        {
            Debug.LogWarning("[GridManager] No players found to assign corners!");
            return;
        }
        
        foreach (var player in players)
            EnsureStartingCornerForPlayer(player.playerId);
    }
    
    private Vector3 GetHexagonWorldPosition(int x, int y)
    {
        float xPos = x * hexSize * 0.75f;
        float yPos = y * hexSize * Mathf.Sqrt(3) / 2f;
        if (x % 2 == 1) yPos += hexSize * Mathf.Sqrt(3) / 4f;
        return new Vector3(xPos, yPos, 0);
    }
    
    [Server]
    public bool HandleCellAction(GridElement cell, PlayerNetwork player)
    {
        if (!elementToCoord.TryGetValue(cell, out Vector2Int coord))
        {
            Debug.LogWarning("[GridManager] Cell not found in grid!");
            return false;
        }
        
        int x = coord.x;
        int y = coord.y;
        int playerId = player.playerId;

        if (player.isEliminated)
            return false;

        if (cell.IsEmpActive)
            return false;

        if (player.activeSkill != ActiveSkill.None)
        {
            if (TryApplyActiveSkill(player, cell, x, y))
                player.activeSkill = ActiveSkill.None;
            return player.activeSkill == ActiveSkill.None;
        }

        uint playerPower = GetPlayerPower(playerId);
        
        if (cell.Owner == playerId)
        {
            cell.SetForce(cell.Force + playerPower);
            CheckWinCondition();
            return true;
        }
        
        if (!IsAdjacentToPlayerCell(x, y, playerId))
            return false;

        // Capturing a neutralized cell is a separate action from reducing it to 0.
        if (cell.Owner == 0 && cell.Force == 0)
        {
            CaptureNeutralCell(cell, player, playerPower);
            CheckWinCondition();
            return true;
        }
        
        ApplyAttackToEnemyCell(cell, player, playerPower);
        
        CheckWinCondition();
        return true;
    }

    [Server]
    private void CaptureNeutralCell(GridElement cell, PlayerNetwork capturer, uint baseForce)
    {
        if (cell?.IsEmpActive != false || capturer?.playerId <= 0 || capturer.isEliminated) return;

        int trapOwnerId = cell.TrapOwnerId;
        uint capturedForce = baseForce + (capturer.hasVampirism ? 20u : 0u);

        cell.SetOwner(capturer.playerId);
        cell.SetForce(capturedForce);

        if (trapOwnerId > 0 && trapOwnerId != capturer.playerId)
        {
            TryTriggerTrapExplosion(cell, capturer.playerId);
            cell.ClearTrap();
        }
    }

    [Server]
    private void ApplyAttackToEnemyCell(GridElement cell, PlayerNetwork attacker, uint damage)
    {
        if (cell.IsEmpActive) return;

        int newForce = (int)cell.Force - (int)damage;
        if (newForce <= 0)
        {
            cell.SetOwner(0);
            cell.SetForce(0);
        }
        else
            cell.SetForce((uint)newForce);
    }

    [Server]
    private void TryTriggerTrapExplosion(GridElement trapCell, int capturerId)
    {
        if (!elementToCoord.TryGetValue(trapCell, out var coord)) return;

        int trapDamage = Mathf.Clamp(Mathf.RoundToInt(GetPlayerPower(trapCell.TrapOwnerId) * 1.5f), 15, 45);
        foreach (var n in GetHexagonNeighbors(coord.x, coord.y))
        {
            if (n.x < 0 || n.x >= gridWidth || n.y < 0 || n.y >= gridHeight) continue;
            var cell = grid[n.x, n.y];
            if (cell == null || cell.IsEmpActive || cell.Owner != capturerId) continue;

            int newForce = (int)cell.Force - trapDamage;
            if (newForce <= 0)
            {
                cell.SetOwner(0);
                cell.SetForce(0);
            }
            else
                cell.SetForce((uint)newForce);
        }
    }

    [Server]
    private bool TryApplyActiveSkill(PlayerNetwork player, GridElement target, int x, int y)
    {
        if (player?.playerId <= 0 || target == null) return false;

        return player.activeSkill switch
        {
            ActiveSkill.Artillery => TryArtillery(player, target, x, y),
            ActiveSkill.Emp => TryEmp(player, target, x, y),
            ActiveSkill.DeepStrike => TryDeepStrike(player, target, x, y),
            ActiveSkill.Trap => TryPlaceTrap(player, target),
            _ => false
        };
    }

    [Server]
    private bool TryPlaceTrap(PlayerNetwork player, GridElement target)
    {
        if (target == null || target.IsEmpActive || target.Owner != player.playerId || target.HasTrap) 
            return false;
        target.SetTrapOwner(player.playerId);
        return true;
    }

    [Server]
    private bool TryArtillery(PlayerNetwork player, GridElement target, int x, int y)
    {
        int playerId = player.playerId;
        if (target.Owner <= 0 || target.Owner == playerId) return false;

        uint damage = GetPlayerPower(playerId);
        ApplyAttackToEnemyCell(target, player, damage);

        foreach (var n in GetHexagonNeighbors(x, y))
        {
            if (n.x >= 0 && n.x < gridWidth && n.y >= 0 && n.y < gridHeight)
            {
                var cell = grid[n.x, n.y];
                if (cell?.Owner > 0 && cell.Owner != playerId)
                    ApplyAttackToEnemyCell(cell, player, damage);
            }
        }

        CheckWinCondition();
        return true;
    }

    [Server]
    private bool TryEmp(PlayerNetwork player, GridElement target, int x, int y)
    {
        double endTime = NetworkTime.time + 5.0;
        ApplyEmpToCell(target, endTime);

        foreach (var n in GetHexagonNeighbors(x, y))
        {
            if (n.x >= 0 && n.x < gridWidth && n.y >= 0 && n.y < gridHeight)
                ApplyEmpToCell(grid[n.x, n.y], endTime);
        }

        return true;
    }

    [Server]
    private void ApplyEmpToCell(GridElement cell, double endTime) => cell?.SetEmpFrozen(endTime);

    [Server]
    private bool TryDeepStrike(PlayerNetwork player, GridElement target, int x, int y)
    {
        if (!IsReachableWithinSteps(player.playerId, new Vector2Int(x, y), 2) || target.IsEmpActive)
            return false;

        uint damage = GetPlayerPower(player.playerId) * 2;

        if (target.Owner == player.playerId)
            target.SetForce(target.Force + damage);
        else if (target.Owner == 0)
        {
            target.SetOwner(player.playerId);
            target.SetForce(damage);
        }
        else
            ApplyAttackToEnemyCell(target, player, damage);

        CheckWinCondition();
        return true;
    }

    private bool IsReachableWithinSteps(int playerId, Vector2Int target, int steps)
    {
        if (steps < 0) return false;
        if (target.x < 0 || target.x >= gridWidth || target.y < 0 || target.y >= gridHeight) return false;

        var frontier = new Queue<Vector2Int>();
        var dist = new Dictionary<Vector2Int, int>();

        for (int gx = 0; gx < gridWidth; gx++)
        {
            for (int gy = 0; gy < gridHeight; gy++)
            {
                if (grid[gx, gy] == null || grid[gx, gy].Owner != playerId) continue;
                var start = new Vector2Int(gx, gy);
                frontier.Enqueue(start);
                dist[start] = 0;
            }
        }

        while (frontier.Count > 0)
        {
            var cur = frontier.Dequeue();
            int d = dist[cur];
            if (cur == target) return true;
            if (d >= steps) continue;

            foreach (var n in GetHexagonNeighbors(cur.x, cur.y))
            {
                if (n.x < 0 || n.x >= gridWidth || n.y < 0 || n.y >= gridHeight) continue;
                if (dist.ContainsKey(n)) continue;
                dist[n] = d + 1;
                frontier.Enqueue(n);
            }
        }

        return false;
    }
    
    private bool IsAdjacentToPlayerCell(int x, int y, int playerId)
    {
        foreach (var n in GetHexagonNeighbors(x, y))
        {
            if (n.x >= 0 && n.x < gridWidth && n.y >= 0 && n.y < gridHeight && grid[n.x, n.y]?.Owner == playerId)
                return true;
        }
        return false;
    }
    
    private Vector2Int[] GetHexagonNeighbors(int x, int y)
    {
        if (x % 2 == 0)
        {
            return new Vector2Int[]
            {
                new(x + 1, y), new(x - 1, y),
                new(x, y + 1), new(x, y - 1),
                new(x + 1, y - 1), new(x - 1, y - 1)
            };
        }
        else
        {
            return new Vector2Int[]
            {
                new(x + 1, y), new(x - 1, y),
                new(x, y + 1), new(x, y - 1),
                new(x + 1, y + 1), new(x - 1, y + 1)
            };
        }
    }

    private Vector2Int GetPlannedSpawnCoord(int playerId)
    {
        if (playerId <= 0) return new Vector2Int(-1, -1);
        if (spawnByPlayerId.TryGetValue(playerId, out var cached))
            return cached;
        return GetCornerForPlayerId(playerId);
    }

    private Vector2Int GetRuntimeSpawnCoord(int playerId)
    {
        if (playerId <= 0) return new Vector2Int(-1, -1);
        if (grid == null) return GetPlannedSpawnCoord(playerId);

        // Prefer planned spawn if it exists on this map.
        var planned = GetPlannedSpawnCoord(playerId);
        if (planned.x >= 0 && planned.x < gridWidth && planned.y >= 0 && planned.y < gridHeight && grid[planned.x, planned.y] != null)
            return planned;

        // Otherwise: nearest existing cell to the corner.
        var corner = GetCornerForPlayerId(playerId);
        var found = FindNearestExistingCoord(corner);
        if (found.x >= 0) return found;

        return new Vector2Int(-1, -1);
    }

    private HashSet<Vector2Int> GenerateActiveCellsForMap(string mapName) => 
        mapName?.Trim() == "Circles" ? GenerateCirclesMap() : GenerateFullMap();

    private HashSet<Vector2Int> GenerateFullMap()
    {
        var all = new HashSet<Vector2Int>(gridWidth * gridHeight);
        for (int x = 0; x < gridWidth; x++)
            for (int y = 0; y < gridHeight; y++)
                all.Add(new Vector2Int(x, y));
        return all;
    }

    private HashSet<Vector2Int> GenerateCirclesMap()
    {
        int min = Mathf.Min(gridWidth, gridHeight);
        int rCorner = Mathf.Clamp(min / 6, 2, 5);
        int rCenter = Mathf.Clamp(rCorner + 1, 3, 7);
        int corridorRadius = 1;

        int inset = Mathf.Clamp(rCorner, 0, Mathf.Min(gridWidth - 1, gridHeight - 1));
        var center = ClampToGrid(new Vector2Int(gridWidth / 2, gridHeight / 2));

        var corners = new[]
        {
            ClampToGrid(new Vector2Int(inset, inset)),
            ClampToGrid(new Vector2Int(gridWidth - 1 - inset, inset)),
            ClampToGrid(new Vector2Int(inset, gridHeight - 1 - inset)),
            ClampToGrid(new Vector2Int(gridWidth - 1 - inset, gridHeight - 1 - inset)),
        };

        var active = new HashSet<Vector2Int>();
        foreach (var c in corners) AddDisk(active, c, rCorner);
        AddDisk(active, center, rCenter);
        foreach (var c in corners) AddCorridor(active, c, center, corridorRadius);
        return active;
    }

    private Vector2Int ClampToGrid(Vector2Int p) => 
        new Vector2Int(Mathf.Clamp(p.x, 0, gridWidth - 1), Mathf.Clamp(p.y, 0, gridHeight - 1));

    private void AddDisk(HashSet<Vector2Int> set, Vector2Int center, int radius)
    {
        int r = Mathf.Max(0, radius);
        int pad = r * 2 + 2;
        int minX = Mathf.Max(0, center.x - pad);
        int maxX = Mathf.Min(gridWidth - 1, center.x + pad);
        int minY = Mathf.Max(0, center.y - pad);
        int maxY = Mathf.Min(gridHeight - 1, center.y + pad);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                var p = new Vector2Int(x, y);
                if (HexDistanceOddQ(p, center) <= r)
                    set.Add(p);
            }
        }
    }

    private void AddCorridor(HashSet<Vector2Int> set, Vector2Int a, Vector2Int b, int radius)
    {
        int r = Mathf.Max(0, radius);
        foreach (var p in HexLineOddQ(a, b))
            AddDisk(set, p, r);
    }

    private Vector2Int FindNearestAllowedCoord(Vector2Int start, HashSet<Vector2Int> allowed)
    {
        if (allowed == null || allowed.Count == 0) return new Vector2Int(-1, -1);
        if (allowed.Contains(start)) return start;

        var q = new Queue<Vector2Int>();
        var seen = new HashSet<Vector2Int>();
        q.Enqueue(start);
        seen.Add(start);

        int guard = 0;
        while (q.Count > 0 && guard < 100000)
        {
            guard++;
            var cur = q.Dequeue();
            foreach (var n in GetHexagonNeighbors(cur.x, cur.y))
            {
                if (n.x < 0 || n.x >= gridWidth || n.y < 0 || n.y >= gridHeight) continue;
                if (seen.Contains(n)) continue;
                seen.Add(n);
                if (allowed.Contains(n)) return n;
                q.Enqueue(n);
            }
        }

        return allowed.First();
    }

    private Vector2Int FindNearestExistingCoord(Vector2Int start)
    {
        if (grid == null) return new Vector2Int(-1, -1);
        if (start.x >= 0 && start.x < gridWidth && start.y >= 0 && start.y < gridHeight && grid[start.x, start.y] != null)
            return start;

        var q = new Queue<Vector2Int>();
        var seen = new HashSet<Vector2Int>();
        q.Enqueue(start);
        seen.Add(start);

        int guard = 0;
        while (q.Count > 0 && guard < 100000)
        {
            guard++;
            var cur = q.Dequeue();
            foreach (var n in GetHexagonNeighbors(cur.x, cur.y))
            {
                if (n.x < 0 || n.x >= gridWidth || n.y < 0 || n.y >= gridHeight) continue;
                if (seen.Contains(n)) continue;
                seen.Add(n);
                if (grid[n.x, n.y] != null) return n;
                q.Enqueue(n);
            }
        }

        return new Vector2Int(-1, -1);
    }

    // Odd-q offset <-> axial helpers and distance/line.
    private static (int q, int r) OddQToAxial(Vector2Int p)
    {
        int q = p.x;
        int r = p.y - (p.x - (p.x & 1)) / 2;
        return (q, r);
    }

    private static int HexDistanceOddQ(Vector2Int a, Vector2Int b)
    {
        var (aq, ar) = OddQToAxial(a);
        var (bq, br) = OddQToAxial(b);
        int dq = aq - bq;
        int dr = ar - br;
        int ds = (aq + ar) - (bq + br);
        return (Mathf.Abs(dq) + Mathf.Abs(dr) + Mathf.Abs(ds)) / 2;
    }

    private static List<Vector2Int> HexLineOddQ(Vector2Int a, Vector2Int b)
    {
        var (aq, ar) = OddQToAxial(a);
        var (bq, br) = OddQToAxial(b);
        int n = HexDistanceOddQ(a, b);

        var result = new List<Vector2Int>(n + 1);
        for (int i = 0; i <= n; i++)
        {
            float t = n == 0 ? 0f : (float)i / n;
            float qf = Mathf.Lerp(aq, bq, t);
            float rf = Mathf.Lerp(ar, br, t);
            float sf = Mathf.Lerp(-(aq + ar), -(bq + br), t);
            var (rq, rr, _) = CubeRound(qf, rf, sf);
            var p = AxialToOddQ(rq, rr);
            if (result.Count == 0 || result[result.Count - 1] != p)
                result.Add(p);
        }

        return result;
    }

    private static Vector2Int AxialToOddQ(int q, int r)
    {
        int x = q;
        int y = r + (q - (q & 1)) / 2;
        return new Vector2Int(x, y);
    }

    private static (int q, int r, int s) CubeRound(float q, float r, float s)
    {
        int rq = Mathf.RoundToInt(q);
        int rr = Mathf.RoundToInt(r);
        int rs = Mathf.RoundToInt(s);

        float qDiff = Mathf.Abs(rq - q);
        float rDiff = Mathf.Abs(rr - r);
        float sDiff = Mathf.Abs(rs - s);

        if (qDiff > rDiff && qDiff > sDiff)
            rq = -rr - rs;
        else if (rDiff > sDiff)
            rr = -rq - rs;
        else
            rs = -rq - rr;

        return (rq, rr, rs);
    }
    
    private uint GetPlayerPower(int playerId)
    {
        var nm = FindFirstObjectByType<NetworkManagerRawr>();
        return nm != null ? nm.GetPlayerPower(playerId) : 10;
    }
    
    public GridElement GetCell(int x, int y)
    {
        if (x >= 0 && x < gridWidth && y >= 0 && y < gridHeight)
            return grid[x, y];
        return null;
    }

    [Server]
    private void CheckWinCondition()
    {
        var playerCellCounts = new Dictionary<int, int>();
        
        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                var cell = grid[x, y];
                if (cell == null) continue;
                int owner = cell.Owner;
                if (owner > 0)
                    playerCellCounts[owner] = playerCellCounts.TryGetValue(owner, out int count) ? count + 1 : 1;
            }
        }
        
        var playersWithCells = playerCellCounts.Keys.ToList();

        UpdateEliminations(playerCellCounts);
        
        if (playersWithCells.Count == 1)
        {
            int winnerId = playersWithCells[0];
            Debug.Log($"[GridManager] Player {winnerId} wins! They control all cells.");
            EndGame(winnerId);
        }
        else if (playersWithCells.Count == 0)
        {
            Debug.LogWarning("[GridManager] No players have cells! This shouldn't happen.");
        }
    }

    [Server]
    private static void UpdateEliminations(Dictionary<int, int> playerCellCounts)
    {
        var players = FindObjectsByType<PlayerNetwork>(FindObjectsSortMode.None);
        foreach (var p in players)
        {
            if (p.playerId <= 0 || p.isEliminated) continue;
            if (playerCellCounts.ContainsKey(p.playerId)) continue;

            p.isEliminated = true;
            p.activeSkill = ActiveSkill.None;
        }
    }
    
    [Server]
    private void EndGame(int winnerId)
    {
        string winnerName = $"Player {winnerId}";
        
        var players = FindObjectsByType<PlayerNetwork>(FindObjectsSortMode.None);
        foreach (var player in players)
        {
            if (player.playerId == winnerId)
            {
                winnerName = player.playerName;
                break;
            }
        }
        
        var nm = FindFirstObjectByType<NetworkManagerRawr>();
        if (nm != null)
        {
            nm.EndGame(winnerId, winnerName);
        }
    }
}