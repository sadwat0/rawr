using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ServerListItem : MonoBehaviour
{
    [Header("UI Components")]
    public TMP_Text hostNameText;
    public TMP_Text playersCountText;
    public Button joinButton;

    public void Setup(DiscoveryResponse info, System.Action<DiscoveryResponse> onJoinClick)
    {
        if (hostNameText != null) 
            hostNameText.text = string.IsNullOrEmpty(info.hostName) ? "Unknown" : info.hostName;

        if (playersCountText != null) 
            playersCountText.text = $"Players: {info.currentPlayerCount}/{info.maxPlayerCount}";

        if (joinButton != null)
        {
            joinButton.onClick.RemoveAllListeners();
            joinButton.onClick.AddListener(() => onJoinClick(info));
        }
    }
}