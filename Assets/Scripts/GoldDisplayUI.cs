using UnityEngine;
using TMPro;
using System.Text;

public class GoldDisplayUI : MonoBehaviour
{
    private TextMeshProUGUI goldText;

    private void Start()
    {
        goldText = GetComponent<TextMeshProUGUI>();
        if (goldText == null)
        {
            Debug.LogError("[GoldDisplayUI] TextMeshProUGUI component not found on " + gameObject.name);
            return;
        }

        PlayerNetwork.ClientOnPlayerUpdated += UpdateAllPlayers;
        PlayerNetwork.ClientOnGoldChanged += UpdateAllPlayers;

        UpdateAllPlayers();
    }

    private void OnDestroy()
    {
        PlayerNetwork.ClientOnPlayerUpdated -= UpdateAllPlayers;
        PlayerNetwork.ClientOnGoldChanged -= UpdateAllPlayers;
    }

    private void UpdateAllPlayers(PlayerNetwork _ = null)
    {
        var allPlayers = FindObjectsByType<PlayerNetwork>(FindObjectsSortMode.None);

        if (allPlayers.Length == 0)
        {
            goldText.text = "";
            return;
        }

        var sb = new StringBuilder(128);
        for (int i = 0; i < allPlayers.Length; i++)
        {
            var player = allPlayers[i];
            int tenths = player.goldTenths;
            string goldStr = tenths % 10 == 0 ? (tenths / 10).ToString() : (tenths / 10f).ToString("0.0");
            
            string colorTag = player.isLocalPlayer ? "yellow" : "";
            if (!string.IsNullOrEmpty(colorTag))
                sb.Append($"<color={colorTag}>{player.playerName}: <color=gold>{goldStr}</color></color>");
            else
                sb.Append($"{player.playerName}: <color=gold>{goldStr}</color>");
            
            if (i < allPlayers.Length - 1) sb.Append('\n');
        }

        goldText.text = sb.ToString();
    }
}

