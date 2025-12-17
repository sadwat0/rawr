using UnityEngine;
using Mirror;
using System;
using System.Collections;
using TMPro;

public enum ActiveSkill : byte
{
    None = 0,
    Artillery = 1,
    Emp = 2,
    DeepStrike = 3,
    Trap = 4,
}

public class PlayerNetwork : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnNameChanged))] public string playerName = "Player";
    [SyncVar] public int playerId = -1;
    [SyncVar(hook = nameof(OnHostChanged))] public bool isHost = false;
    [SyncVar(hook = nameof(OnGoldChanged))] public int goldTenths = 0;
    [SyncVar(hook = nameof(OnSharpeningLevelChanged))] public int sharpeningLevel = 0;
    [SyncVar(hook = nameof(OnClickPowerChanged))] public uint clickPower = 10;
    [SyncVar(hook = nameof(OnGoldRushLevelChanged))] public int goldRushLevel = 0;
    [SyncVar(hook = nameof(OnHasVampirismChanged))] public bool hasVampirism = false;
    [SyncVar(hook = nameof(OnActiveSkillChanged))] public ActiveSkill activeSkill = ActiveSkill.None;
    [SyncVar(hook = nameof(OnEliminatedChanged))] public bool isEliminated = false;

    public static PlayerNetwork localPlayer;

    public static event Action<PlayerNetwork> ClientOnPlayerUpdated;
    public static event Action<PlayerNetwork> ClientOnPlayerRemoved;
    public static event Action<PlayerNetwork> ClientOnGoldChanged;
    public static event Action<PlayerNetwork> ClientOnUpgradesChanged;

    private const float CellClickCooldownSeconds = 0.5f;
    private float cellClickCooldownEndClientTime = 0f;
    private double nextAllowedCellClickServerTime = 0.0;

    public void ClientTryClickCell(uint cellNetId)
    {
        if (!isLocalPlayer) return;
        if (isEliminated) return;

        if (activeSkill == ActiveSkill.None && Time.unscaledTime < cellClickCooldownEndClientTime)
            return;

        CmdClickCell(cellNetId);
    }

    public float GetCellClickCooldownRemaining()
    {
        if (!isLocalPlayer) return 0f;
        if (isEliminated) return 0f;
        if (activeSkill != ActiveSkill.None) return 0f;
        return Mathf.Max(0f, cellClickCooldownEndClientTime - Time.unscaledTime);
    }

    [TargetRpc]
    private void TargetStartCellClickCooldown(NetworkConnection target, float durationSeconds)
    {
        cellClickCooldownEndClientTime = Time.unscaledTime + Mathf.Max(0f, durationSeconds);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (!isLocalPlayer)
        {
            ClientOnPlayerUpdated?.Invoke(this);
        }
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        localPlayer = this;

        string saved = PlayerPrefs.GetString("PlayerNickname", "");
        if (!string.IsNullOrWhiteSpace(saved))
            CmdSetName(saved);

        ClientOnPlayerUpdated?.Invoke(this);
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        ClientOnPlayerRemoved?.Invoke(this);
        if (isLocalPlayer) localPlayer = null;
    }

    void OnNameChanged(string oldName, string newName) => ClientOnPlayerUpdated?.Invoke(this);
    void OnHostChanged(bool oldValue, bool newValue) => ClientOnPlayerUpdated?.Invoke(this);
    void OnGoldChanged(int oldGoldTenths, int newGoldTenths) => ClientOnGoldChanged?.Invoke(this);
    void OnSharpeningLevelChanged(int oldValue, int newValue) => ClientOnUpgradesChanged?.Invoke(this);
    void OnClickPowerChanged(uint oldValue, uint newValue) => ClientOnUpgradesChanged?.Invoke(this);
    void OnGoldRushLevelChanged(int oldValue, int newValue) => ClientOnUpgradesChanged?.Invoke(this);
    void OnHasVampirismChanged(bool oldValue, bool newValue) => ClientOnUpgradesChanged?.Invoke(this);
    void OnActiveSkillChanged(ActiveSkill oldValue, ActiveSkill newValue) => ClientOnUpgradesChanged?.Invoke(this);
    
    void OnEliminatedChanged(bool oldValue, bool newValue)
    {
        ClientOnPlayerUpdated?.Invoke(this);
        ClientOnUpgradesChanged?.Invoke(this);
    }

    [Command]
    public void CmdSetName(string newName)
    {
        if (!isServer) return;
        
        string trimmedName = string.IsNullOrWhiteSpace(newName) ? $"Player{playerId}" : newName.Trim();
        
        if (IsNicknameTaken(trimmedName, this))
        {
            string reason = $"Nickname '{trimmedName}' is already taken. Please choose a different name.";
            DisconnectReasonMessage msg = new DisconnectReasonMessage { reason = reason };
            connectionToClient?.Send(msg);
            
            StartCoroutine(DelayedDisconnect(connectionToClient, 0.1f));
            return;
        }
        
        playerName = trimmedName;
    }
    
    [Server]
    private bool IsNicknameTaken(string nickname, PlayerNetwork excludePlayer)
    {
        if (string.IsNullOrWhiteSpace(nickname)) return false;
        
        var allPlayers = FindObjectsByType<PlayerNetwork>(FindObjectsSortMode.None);
        
        foreach (var player in allPlayers)
        {
            if (player == excludePlayer) continue;
            
            if (player.playerName == nickname)
            {
                return true;
            }
        }
        
        return false;
    }

    [Server]
    private IEnumerator DelayedDisconnect(NetworkConnectionToClient conn, float delay)
    {
        yield return new WaitForSeconds(delay);
        conn?.Disconnect();
    }

    [Command]
    public void CmdRequestLeave()
    {
        connectionToClient?.Disconnect();
    }

    [Command]
    public void CmdRequestStartGame()
    {
        if (!isHost) return;

        var nm = FindFirstObjectByType<NetworkManagerRawr>();
        if (nm != null)
            nm.StartGameFromLobby();
    }

    [Command]
    public void CmdRequestSetMap(string newMap)
    {
        if (!isHost) return;
        var lobbyState = FindFirstObjectByType<LobbyState>();
        if (lobbyState != null)
            lobbyState.SetMapName(newMap);
    }

    [Server]
    public void AddGold(uint amount) => AddGoldTenths((int)amount * 10);

    [Server]
    public bool TrySpendGold(uint amount) => TrySpendGoldTenths((int)amount * 10);

    [Server]
    public void AddGoldTenths(int amountTenths) => goldTenths += amountTenths;

    [Server]
    public bool TrySpendGoldTenths(int amountTenths)
    {
        if (goldTenths < amountTenths) return false;
        goldTenths -= amountTenths;
        return true;
    }

    [Command]
    public void CmdClickCell(uint cellNetId)
    {
        if (!isServer) return;
        if (isEliminated) return;

        bool isNormalClick = activeSkill == ActiveSkill.None;
        if (isNormalClick)
        {
            double now = NetworkTime.time;
            if (now < nextAllowedCellClickServerTime) return;
        }

        if (!NetworkServer.spawned.TryGetValue(cellNetId, out var identity)) return;
        var cell = identity.GetComponent<GridElement>();
        if (cell == null) return;

        var gridManager = FindFirstObjectByType<GridManager>();
        if (gridManager == null) return;

        bool didAction = gridManager.HandleCellAction(cell, this);

        if (isNormalClick && didAction)
        {
            double now = NetworkTime.time;
            nextAllowedCellClickServerTime = now + CellClickCooldownSeconds;
            TargetStartCellClickCooldown(connectionToClient, CellClickCooldownSeconds);
        }
    }

    public static uint GetSharpeningCost(int currentLevel)
    {
        if (currentLevel < 0) currentLevel = 0;
        return 10u + 5u * (uint)currentLevel;
    }

    [Command]
    public void CmdBuySharpening()
    {
        if (!isServer) return;
        if (isEliminated) return;

        uint cost = GetSharpeningCost(sharpeningLevel);
        if (!TrySpendGold(cost)) return;

        sharpeningLevel += 1;
        clickPower += 1;
    }

    public static uint GetGoldRushCost(int currentLevel) => 20u + 10u * (uint)Mathf.Max(0, currentLevel);

    [Command]
    public void CmdBuyGoldRush()
    {
        if (!isServer) return;
        if (isEliminated) return;

        uint cost = GetGoldRushCost(goldRushLevel);
        if (!TrySpendGold(cost)) return;

        goldRushLevel += 1;
    }

    [Server]
    public void AddPassiveGold(uint baseAmount)
    {
        int level = Mathf.Max(0, goldRushLevel);
        int percent = 100 + 10 * level;
        int addedTenths = (int)((long)baseAmount * 10 * percent / 100);
        AddGoldTenths(addedTenths);
    }

    public static uint GetVampirismCost() => 70;

    [Command]
    public void CmdBuyVampirism()
    {
        if (!isServer) return;
        if (isEliminated) return;
        if (hasVampirism) return;
        if (!TrySpendGold(GetVampirismCost())) return;
        hasVampirism = true;
    }

    public static uint GetArtilleryCost() => 55;
    public static uint GetEmpCost() => 70;
    public static uint GetDeepStrikeCost() => 75;
    public static uint GetTrapCost() => 60;

    [Command]
    public void CmdBuyArtillery() => CmdBuyActiveSkill(ActiveSkill.Artillery, GetArtilleryCost());

    [Command]
    public void CmdBuyEmp() => CmdBuyActiveSkill(ActiveSkill.Emp, GetEmpCost());

    [Command]
    public void CmdBuyDeepStrike() => CmdBuyActiveSkill(ActiveSkill.DeepStrike, GetDeepStrikeCost());

    [Command]
    public void CmdBuyTrap() => CmdBuyActiveSkill(ActiveSkill.Trap, GetTrapCost());

    private void CmdBuyActiveSkill(ActiveSkill skill, uint cost)
    {
        if (!isServer || isEliminated) return;

        if (activeSkill == skill)
        {
            AddGoldTenths((int)cost * 10);
            activeSkill = ActiveSkill.None;
            return;
        }

        if (activeSkill != ActiveSkill.None || !TrySpendGold(cost)) return;
        activeSkill = skill;
    }
}