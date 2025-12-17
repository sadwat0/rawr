using UnityEngine;
using Mirror;
using System.Collections.Generic;

public class GoldManager : MonoBehaviour
{
    private const float GoldTickRate = 1f;
    private float goldTimer = 0f;

    private void Start()
    {
        if (!NetworkServer.active) enabled = false;
    }

    private void Update()
    {
        goldTimer += Time.deltaTime;
        while (goldTimer >= GoldTickRate)
        {
            goldTimer -= GoldTickRate;
            DistributePassiveGold();
        }
    }

    private void DistributePassiveGold()
    {
        var players = FindObjectsByType<PlayerNetwork>(FindObjectsSortMode.None);
        if (players.Length == 0) return;

        var hasCellsByOwner = new HashSet<int>();
        var minesByOwner = new Dictionary<int, int>();
        var cells = FindObjectsByType<GridElement>(FindObjectsSortMode.None);
        foreach (var cell in cells)
        {
            int owner = cell.Owner;
            if (owner <= 0) continue;
            hasCellsByOwner.Add(owner);
            if (cell.IsGoldMine && !cell.IsEmpActive)
                minesByOwner[owner] = minesByOwner.TryGetValue(owner, out int c) ? c + 1 : 1;
        }

        foreach (var player in players)
        {
            if (player.playerId <= 0 || player.isEliminated) continue;
            if (!hasCellsByOwner.Contains(player.playerId)) continue;
            minesByOwner.TryGetValue(player.playerId, out int mines);
            player.AddPassiveGold((uint)(1 + mines));
        }
    }
}

