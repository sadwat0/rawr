using UnityEngine;
using Mirror;
using System;

public class LobbyState : NetworkBehaviour
{
    public static LobbyState Instance { get; private set; }

    [SyncVar(hook = nameof(OnMapNameChanged))]
    public string mapName = "Default Map";

    public event Action<string> ClientOnMapChanged;

    private void Awake()
    {
        Instance = this;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        mapName = "Default Map";
        FindFirstObjectByType<NetworkManagerRawr>()?.ServerSetSelectedMapName(mapName);
    }

    [Server]
    public void SetMapName(string newMap)
    {
        mapName = string.IsNullOrEmpty(newMap) ? "Default Map" : newMap;
        FindFirstObjectByType<NetworkManagerRawr>()?.ServerSetSelectedMapName(mapName);
    }

    private void OnMapNameChanged(string oldValue, string newValue)
    {
        ClientOnMapChanged?.Invoke(newValue);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}