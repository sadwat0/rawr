using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using Mirror.Discovery;

public struct WinnerMessage : NetworkMessage
{
    public string winnerName;
}

public struct DisconnectReasonMessage : NetworkMessage
{
    public string reason;
}

public class NetworkManagerRawr : NetworkManager
{
    [Header("Scenes")]
    [SerializeField] private string lobbySceneName = "Lobby";
    [SerializeField] private string gameSceneName = "Game";
    [SerializeField] private string endGameSceneName = "EndGame";

    [Header("Discovery")]
    [Tooltip("Optional NetworkDiscovery component (add Mirror Discovery to same GameObject).")]
    public CustomNetworkDiscovery networkDiscovery; 

    [Header("Player")]
    [Tooltip("Player prefab spawned for each connected client (lobby/game player).")]
    public GameObject playerNetworkPrefab;

    private Dictionary<int, int> connectionIdToPlayerId = new Dictionary<int, int>();
    private int nextPlayerId = 1;
    private HashSet<int> availablePlayerIds = new HashSet<int>();
    private const int MAX_PLAYERS = 4;

    public static string lastDisconnectReason = "";
    public static int lastWinnerId = -1;
    public static string lastWinnerName = "";

    public static string SelectedMapName { get; private set; } = "Default Map";

    public override void Awake()
    {
        base.Awake();
        DontDestroyOnLoad(gameObject);

        if (networkDiscovery == null)
            networkDiscovery = GetComponent<CustomNetworkDiscovery>();

        if (playerNetworkPrefab != null)
            playerPrefab = playerNetworkPrefab;

        autoCreatePlayer = false;

        if (string.IsNullOrWhiteSpace(SelectedMapName))
            SelectedMapName = "Default Map";
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        
        ResetPlayerIds();

        SelectedMapName = "Default Map";
    }
    
    public override void OnStopClient()
    {
        base.OnStopClient();
        
        if (!NetworkServer.active)
        {
            ResetPlayerIds();
        }
    }
    
    private void ResetPlayerIds()
    {
        nextPlayerId = 1;
        connectionIdToPlayerId.Clear();
        availablePlayerIds.Clear();
    }

    [Server]
    public void ServerSetSelectedMapName(string mapName) => 
        SelectedMapName = string.IsNullOrWhiteSpace(mapName) ? "Default Map" : mapName.Trim();

    public bool HostAndOpenLobby()
    {
        if (NetworkServer.active || NetworkClient.active)
        {
            Debug.LogWarning("[NetworkManagerRawr] HostAndOpenLobby: already running.");
            return false;
        }

        StartHost();
        StartCoroutine(WaitForServerThenOpenLobby());
        return true;
    }
    private IEnumerator WaitForServerThenOpenLobby()
    {
        yield return new WaitUntil(() => NetworkServer.active);
        if (NetworkServer.isLoadingScene || networkSceneName == lobbySceneName)
        {
            Debug.LogWarning($"Server is already loading/using scene {lobbySceneName}, skipping ServerChangeScene.");
        }
        else
        {
            AdvertiseServer();
            ServerChangeScene(lobbySceneName);
        }
    }

    public override void OnClientConnect()
    {
        base.OnClientConnect();

        if (NetworkServer.active) 
        {
            return;
        }

        if (!autoCreatePlayer && NetworkClient.isConnected && NetworkClient.ready)
        {
            if (NetworkClient.localPlayer == null)
            {
                NetworkClient.AddPlayer();
            }
        }
    }

    public void JoinAddress(string address)
    {
        networkAddress = address;
        StartClient();
    }

    public void StopHostOrClient()
    {
        if (NetworkServer.active && NetworkClient.isConnected)
            StopHost();
        else if (NetworkClient.isConnected)
            StopClient();
        else if (NetworkServer.active)
            StopServer();
    }

    public void AdvertiseServer()
    {
        if (networkDiscovery == null)
        {
            Debug.LogError("[NetworkManagerRawr] AdvertiseServer: no NetworkDiscovery instance assigned.");
            return;
        }

        try
        {
            var t = networkDiscovery.GetType();
            var advertiseMethod = t.GetMethod("AdvertiseServer") ?? t.GetMethod("Advertise");
            if (advertiseMethod != null)
            {
                advertiseMethod.Invoke(networkDiscovery, null);
            }
            else
            {
                Debug.LogWarning("[NetworkManagerRawr] Advertise method not found on NetworkDiscovery component; check Mirror.Discovery version.");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[NetworkManagerRawr] AdvertiseServer error: " + ex);
        }
    }

    public override void OnServerConnect(NetworkConnectionToClient conn)
    {
        base.OnServerConnect(conn);

        int pid;
        
        if (availablePlayerIds.Count > 0)
        {
            var enumerator = availablePlayerIds.GetEnumerator();
            enumerator.MoveNext();
            pid = enumerator.Current;
            availablePlayerIds.Remove(pid);
        }
        else
        {
            if (nextPlayerId > MAX_PLAYERS)
            {
                Debug.LogError($"[NetworkManagerRawr] Maximum players ({MAX_PLAYERS}) reached! Cannot assign new playerId.");
                conn.Disconnect();
                return;
            }
            pid = nextPlayerId++;
        }
        
        connectionIdToPlayerId[conn.connectionId] = pid;
    }

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        if (connectionIdToPlayerId.TryGetValue(conn.connectionId, out int pid))
        {
            if (pid > 0 && pid <= MAX_PLAYERS)
            {
                availablePlayerIds.Add(pid);
            }
            
            connectionIdToPlayerId.Remove(conn.connectionId);
        }

        base.OnServerDisconnect(conn);
    }

    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        if (conn.identity != null)
        {
            Debug.LogWarning($"[NetworkManagerRawr] OnServerAddPlayer: Connection {conn.connectionId} already has a player (netId={conn.identity.netId}), skipping spawn.");
            return;
        }

        if (playerNetworkPrefab == null)
        {
            Debug.LogError("[NetworkManagerRawr] OnServerAddPlayer: playerNetworkPrefab is not assigned.");
            base.OnServerAddPlayer(conn); // fallback (may be null)
            return;
        }

        int pid = GetPlayerIdFromConnection(conn);

        GameObject playerObj = Instantiate(playerNetworkPrefab);
        var pn = playerObj.GetComponent<PlayerNetwork>();
        if (pn != null)
        {
            pn.playerId = pid;
        }
        else
        {
            Debug.LogError("[NetworkManagerRawr] OnServerAddPlayer: PlayerNetwork component not found on prefab!");
        }

        NetworkServer.AddPlayerForConnection(conn, playerObj);
        
        if (pn != null && conn == NetworkServer.localConnection)
        {
            pn.isHost = true;
        }

        var gridManager = FindFirstObjectByType<GridManager>();
        if (gridManager != null)
            gridManager.EnsureStartingCornerForPlayer(pid);
    }

    public int GetPlayerIdFromConnection(NetworkConnectionToClient conn) =>
        conn != null && connectionIdToPlayerId.TryGetValue(conn.connectionId, out int pid) ? pid : -1;

    public uint GetPlayerPower(int playerId)
    {
        var players = FindObjectsByType<PlayerNetwork>(FindObjectsSortMode.None);
        foreach (var p in players)
            if (p?.playerId == playerId)
                return p.clickPower;
        return 10;
    }

    public void StartGameFromLobby()
    {
        if (!NetworkServer.active)
        {
            Debug.LogWarning("StartGameFromLobby must be called on server.");
            return;
        }
        ServerChangeScene(gameSceneName);
    }
    
    [Server]
    public void EndGame(int winnerId, string winnerName)
    {
        if (!NetworkServer.active)
        {
            Debug.LogWarning("EndGame must be called on server.");
            return;
        }
        
        lastWinnerId = winnerId;
        lastWinnerName = winnerName;
        Debug.Log($"[NetworkManagerRawr] Winner: {winnerName} (ID: {winnerId})");
        
        WinnerMessage msg = new WinnerMessage { winnerName = winnerName };
        NetworkServer.SendToAll(msg);
        
        StartCoroutine(DelayedSceneChange(endGameSceneName));
    }
    
    private IEnumerator DelayedSceneChange(string sceneName)
    {
        yield return new WaitForSeconds(0.1f);
        ServerChangeScene(sceneName);
    }
    
    public override void OnServerReady(NetworkConnectionToClient conn)
    {
        base.OnServerReady(conn);
        if (conn.identity == null)
        {
            OnServerAddPlayer(conn);
        }
    }

    public override void OnClientSceneChanged()
    {
        base.OnClientSceneChanged();
        
        if (!NetworkServer.active && !autoCreatePlayer && NetworkClient.isConnected && NetworkClient.ready)
        {
            if (NetworkClient.localPlayer == null)
            {
                NetworkClient.AddPlayer();
            }
        }
    }

    public override void OnClientDisconnect()
    {
        if (string.IsNullOrEmpty(lastDisconnectReason))
        {
            lastDisconnectReason = "Disconnected from server.";
        }

        base.OnClientDisconnect();
        
        StartCoroutine(LoadDiscoverySceneAfterDisconnect());
    }

    private IEnumerator LoadDiscoverySceneAfterDisconnect()
    {
        yield return null;
        
        yield return new WaitUntil(() => !NetworkServer.active && !NetworkClient.active);
        
        string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (currentScene != "LobbyDiscovery")
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("LobbyDiscovery");
        }
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        
        NetworkClient.RegisterHandler<WinnerMessage>(OnWinnerMessage);
        
        NetworkClient.RegisterHandler<DisconnectReasonMessage>(OnDisconnectReasonMessage);
    }
    
    private void OnWinnerMessage(WinnerMessage msg)
    {
        lastWinnerName = msg.winnerName;
    }
    
    private void OnDisconnectReasonMessage(DisconnectReasonMessage msg)
    {
        lastDisconnectReason = msg.reason;
    }
}