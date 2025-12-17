using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Mirror;
using System.Collections;

public class Lobby : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Transform playerListContainer;
    [SerializeField] private GameObject playerListItemPrefab;
    [SerializeField] private Button startButton;
    [SerializeField] private Button leaveButton;
    [SerializeField] private TMP_Dropdown mapDropdown;

    private readonly Dictionary<uint, GameObject> spawnedPlayers = new Dictionary<uint, GameObject>();
    private NetworkManagerRawr nm;

    private void Start()
    {
        nm = NetworkManager.singleton as NetworkManagerRawr;

        if (startButton != null)
            startButton.onClick.AddListener(OnStartClicked);

        if (leaveButton != null)
            leaveButton.onClick.AddListener(OnLeaveClicked);

        if (mapDropdown != null)
            mapDropdown.onValueChanged.AddListener(OnMapDropdownChanged);

        PlayerNetwork.ClientOnPlayerUpdated += OnPlayerUpdated;
        PlayerNetwork.ClientOnPlayerRemoved += OnPlayerRemoved;

        var existingPlayers = FindObjectsByType<PlayerNetwork>(FindObjectsSortMode.None);
        foreach (var pn in existingPlayers)
        {
            OnPlayerUpdated(pn);
        }

        var ls = FindFirstObjectByType<LobbyState>();
        if (ls != null)
        {
            OnMapNameChanged(ls.mapName);
            ls.ClientOnMapChanged += OnMapNameChanged;
        }

        UpdateStartInteractable();
    }

    private void OnDestroy()
    {
        PlayerNetwork.ClientOnPlayerUpdated -= OnPlayerUpdated;
        PlayerNetwork.ClientOnPlayerRemoved -= OnPlayerRemoved;

        if (startButton != null)
            startButton.onClick.RemoveListener(OnStartClicked);

        if (leaveButton != null)
            leaveButton.onClick.RemoveListener(OnLeaveClicked);

        if (mapDropdown != null)
            mapDropdown.onValueChanged.RemoveListener(OnMapDropdownChanged);

        var ls = FindFirstObjectByType<LobbyState>();
        if (ls != null)
        {
            ls.ClientOnMapChanged -= OnMapNameChanged;
        }
    }

    private void OnPlayerUpdated(PlayerNetwork pn)
    {
        if (pn == null) return;

        uint key = pn.netId;
        bool isNewItem = !spawnedPlayers.TryGetValue(key, out var item);
        
        if (isNewItem)
        {
            if (playerListItemPrefab == null || playerListContainer == null)
            {
                Debug.LogError("[Lobby] Missing playerListItemPrefab/playerListContainer.");
                return;
            }
            item = Instantiate(playerListItemPrefab, playerListContainer);
            spawnedPlayers[key] = item;
        }

        var nameText = FindNameText(item.transform);
        if (nameText != null)
        {
            string formattedText = FormatPlayerText(pn);
            nameText.text = formattedText;
        }

        var hostBadge = item.transform.Find("HostBadge");
        if (hostBadge != null)
        {
            hostBadge.gameObject.SetActive(pn.isHost);
        }

        if (pn.isLocalPlayer)
        {
            var t = item.GetComponentInChildren<TMP_Text>();
            if (t != null)
            {
                t.fontStyle = FontStyles.Bold;
            }
        }

        UpdateStartInteractable();
    }

    private void OnPlayerRemoved(PlayerNetwork pn)
    {
        if (pn == null) return;

        uint key = pn.netId;

        if (spawnedPlayers.TryGetValue(key, out var go))
        {
            spawnedPlayers.Remove(key);
            Destroy(go);
        }

        UpdateStartInteractable();
    }

    private TMP_Text FindNameText(Transform parent) => 
        parent.Find("NameText")?.GetComponent<TMP_Text>() ?? parent.GetComponentInChildren<TMP_Text>();

    private string FormatPlayerText(PlayerNetwork pn) => 
        pn.playerName + (pn.isHost ? " (Host)" : "");

    private void UpdateStartInteractable()
    {
        bool localIsHost = PlayerNetwork.localPlayer != null && PlayerNetwork.localPlayer.isHost;
        bool hasMultiplePlayers = spawnedPlayers.Count > 1;
        
        if (startButton != null) 
            startButton.interactable = localIsHost && NetworkServer.active && hasMultiplePlayers;
        if (mapDropdown != null) 
            mapDropdown.interactable = localIsHost && NetworkServer.active;
    }

    public void OnStartClicked() => PlayerNetwork.localPlayer?.CmdRequestStartGame();

    public void OnLeaveClicked()
    {
        var nm = NetworkManager.singleton as NetworkManagerRawr;

        if (NetworkServer.active && NetworkClient.isConnected)
        {
            NetworkManagerRawr.lastDisconnectReason = "Host disconnected from the lobby.";
            nm.StopHost();
        }
        else
        {
            NetworkManagerRawr.lastDisconnectReason = "You left the lobby.";
            nm.StopClient();
            StartCoroutine(LoadDiscoverySceneAfterClientLeave());
        }
    }

    private IEnumerator LoadDiscoverySceneAfterClientLeave()
    {
        yield return null;
        
        yield return new WaitUntil(() => !NetworkClient.active);
        
        string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (currentScene != "LobbyDiscovery")
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("LobbyDiscovery");
        }
    }

    public void OnMapDropdownChanged(int idx)
    {
        if (mapDropdown == null) return;
        if (PlayerNetwork.localPlayer != null)
        {
            string chosen = mapDropdown.options[idx].text;
            PlayerNetwork.localPlayer.CmdRequestSetMap(chosen);
        }
    }

    private void OnMapNameChanged(string newMap)
    {
        if (mapDropdown != null)
        {
            int idx = mapDropdown.options.FindIndex(o => o.text == newMap);
            if (idx >= 0)
                mapDropdown.SetValueWithoutNotify(idx);
        }
    }
}