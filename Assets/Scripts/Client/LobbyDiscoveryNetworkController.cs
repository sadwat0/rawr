using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Mirror;
using TMPro;

public class LobbyDiscoveryNetworkController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Transform serverListContainer;
    [SerializeField] private GameObject serverItemPrefab;
    [SerializeField] private Button refreshButton;
    [SerializeField] private Button createLobbyButton;

    [SerializeField] private TMP_Text errorMessageText;

    [Header("Nickname")]
    [SerializeField] private TMP_InputField nicknameInput;

    [SerializeField] private CustomNetworkDiscovery networkDiscovery;

    private NetworkManagerRawr nm = null;
    private readonly Dictionary<long, DiscoveryResponse> discoveredServers = new();

    private void Start()
    {
        nm = NetworkManager.singleton as NetworkManagerRawr;
        
        if (networkDiscovery == null && nm != null)
        {
            networkDiscovery = nm.GetComponent<CustomNetworkDiscovery>();
        }

        if (networkDiscovery != null)
        {
            networkDiscovery.OnServerFound.AddListener(OnServerFound);
        }
        else
        {
            Debug.LogError("CustomNetworkDiscovery not found! Make sure to add it to NetworkManager object.");
        }

        if (refreshButton != null) refreshButton.onClick.AddListener(RefreshServerList);
        if (createLobbyButton != null) createLobbyButton.onClick.AddListener(CreateLobby);

        if (nicknameInput != null)
            nicknameInput.text = PlayerPrefs.GetString("PlayerNickname", "");

        CheckAndDisplayDisconnectReason();

        RefreshServerList();
    }

    private void CheckAndDisplayDisconnectReason()
    {
        if (!string.IsNullOrEmpty(NetworkManagerRawr.lastDisconnectReason))
        {
            OnClientDisconnectedWithReason(NetworkManagerRawr.lastDisconnectReason);
            NetworkManagerRawr.lastDisconnectReason = "";
        }
    }

    private void OnClientDisconnectedWithReason(string reason)
    {
        if (errorMessageText == null) return;
        errorMessageText.text = reason;
        errorMessageText.gameObject.SetActive(true);
    }

    public void RefreshServerList()
    {
        discoveredServers.Clear();
        foreach (Transform t in serverListContainer) Destroy(t.gameObject);

        if (networkDiscovery != null)
        {
            networkDiscovery.StopDiscovery();
            networkDiscovery.StartDiscovery();
        }
    }

    private void OnServerFound(DiscoveryResponse info)
    {
        if (discoveredServers.ContainsKey(info.serverId)) return;

        discoveredServers[info.serverId] = info;
        
        CreateServerListItem(info);
    }

    private void CreateServerListItem(DiscoveryResponse info)
    {
        if (serverItemPrefab == null || serverListContainer == null) return;

        var go = Instantiate(serverItemPrefab, serverListContainer, false);
        go.SetActive(true);

        var rt = go.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.localScale = Vector3.one;
            rt.localPosition = Vector3.zero;
        }

        go.GetComponent<ServerListItem>()?.Setup(info, JoinServer);
    }

    private void JoinServer(DiscoveryResponse info)
    {
        SaveNickname();

        var mgr = NetworkManager.singleton as NetworkManagerRawr;
        networkDiscovery.StopDiscovery();
        
        mgr.StartClient(info.uri);
    }

    private void SaveNickname()
    {
        if (nicknameInput != null)
        {
            PlayerPrefs.SetString("PlayerNickname", nicknameInput.text);
            PlayerPrefs.Save();
        }
    }

    public void CreateLobby()
    {
        SaveNickname();

        var mgr = NetworkManager.singleton as NetworkManagerRawr;
        mgr.HostAndOpenLobby();
        
        networkDiscovery.AdvertiseServer();
    }

}