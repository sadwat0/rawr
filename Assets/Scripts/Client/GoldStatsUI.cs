using UnityEngine;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class GoldStatsUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI goldStatsText;
    private Dictionary<int, PlayerNetwork> playerMap = new();
    private GridElement colorSource;

    private void OnEnable()
    {
        PlayerNetwork.ClientOnPlayerUpdated += OnPlayerUpdated;
        PlayerNetwork.ClientOnPlayerRemoved += OnPlayerRemoved;
        GridElement.ClientOnGridChanged += OnGridChanged;
    }

    private void OnDisable()
    {
        PlayerNetwork.ClientOnPlayerUpdated -= OnPlayerUpdated;
        PlayerNetwork.ClientOnPlayerRemoved -= OnPlayerRemoved;
        GridElement.ClientOnGridChanged -= OnGridChanged;
    }

    private void Start()
    {
        if (goldStatsText == null)
            goldStatsText = GetComponent<TextMeshProUGUI>();

        colorSource = FindFirstObjectByType<GridElement>();

        var players = FindObjectsByType<PlayerNetwork>(FindObjectsSortMode.None);
        foreach (var player in players)
            RegisterPlayer(player);

        UpdateUI();
    }

    private void OnPlayerUpdated(PlayerNetwork player)
    {
        RegisterPlayer(player);
        UpdateUI();
    }

    private void OnGridChanged() => UpdateUI();

    private void OnPlayerRemoved(PlayerNetwork player)
    {
        if (player?.playerId > 0)
            playerMap.Remove(player.playerId);
        UpdateUI();
    }

    private void RegisterPlayer(PlayerNetwork player)
    {
        if (player?.playerId > 0)
            playerMap[player.playerId] = player;
    }

    private void UpdateUI()
    {
        if (goldStatsText == null) return;

        if (colorSource == null)
            colorSource = FindFirstObjectByType<GridElement>();

        var lifeByPlayer = new Dictionary<int, ulong>();
        var cells = FindObjectsByType<GridElement>(FindObjectsSortMode.None);
        foreach (var cell in cells)
        {
            int owner = cell.Owner;
            if (owner <= 0) continue;
            lifeByPlayer[owner] = lifeByPlayer.TryGetValue(owner, out var sum) ? sum + cell.Force : cell.Force;
        }

        var sortedPlayers = playerMap.Values
            .Where(p => p.playerId > 0)
            .OrderByDescending(p => lifeByPlayer.TryGetValue(p.playerId, out var life) ? life : 0UL)
            .ThenBy(p => p.playerId)
            .ToList();

        if (sortedPlayers.Count == 0)
        {
            goldStatsText.text = "";
            return;
        }

        string statsText = "<b>Life</b>\n";
        foreach (var player in sortedPlayers)
        {
            Color color = GetPlayerColor(player.playerId);
            string colorHex = ColorUtility.ToHtmlStringRGB(color);
            lifeByPlayer.TryGetValue(player.playerId, out ulong life);
            statsText += $"<color=#{colorHex}>{player.playerName}: {life}</color>\n";
        }

        goldStatsText.text = statsText;
    }

    private Color GetPlayerColor(int playerId)
    {
        if (colorSource != null && colorSource.TryGetPlayerColor(playerId, out var color))
            return color;
        return Color.white;
    }
}

