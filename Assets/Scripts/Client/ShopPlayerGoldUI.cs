using TMPro;
using UnityEngine;

public class ShopPlayerGoldUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI goldText;

    private void Awake() => goldText ??= GetComponent<TextMeshProUGUI>();

    private void OnEnable()
    {
        PlayerNetwork.ClientOnGoldChanged += Refresh;
        PlayerNetwork.ClientOnPlayerUpdated += Refresh;
    }

    private void OnDisable()
    {
        PlayerNetwork.ClientOnGoldChanged -= Refresh;
        PlayerNetwork.ClientOnPlayerUpdated -= Refresh;
    }

    private void Start() => Refresh(null);

    private void Refresh(PlayerNetwork _)
    {
        if (goldText == null) return;
        
        var player = PlayerNetwork.localPlayer;
        if (player == null)
        {
            goldText.text = "Gold: -";
            return;
        }

        int tenths = player.goldTenths;
        goldText.text = $"Gold: {(tenths % 10 == 0 ? $"{tenths / 10}" : $"{tenths / 10f:0.0}")}";
    }
}


