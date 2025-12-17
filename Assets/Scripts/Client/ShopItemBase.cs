using TMPro;
using UnityEngine;
using UnityEngine.UI;

public abstract class ShopItemBase : MonoBehaviour
{
    [SerializeField] protected Button buyButton;
    [SerializeField] protected Image overlayImage;
    [SerializeField] protected TextMeshProUGUI titleText;
    [SerializeField] protected TextMeshProUGUI descText;
    [SerializeField] protected TextMeshProUGUI costText;
    
    protected abstract KeyCode Hotkey { get; }
    
    protected virtual void Awake()
    {
        buyButton ??= GetComponent<Button>();
        overlayImage ??= transform.Find("ActiveOverlay")?.GetComponent<Image>() 
                       ?? transform.Find("PurchasedOverlay")?.GetComponent<Image>();
        titleText ??= transform.Find("Title")?.GetComponent<TextMeshProUGUI>();
        descText ??= transform.Find("Desc")?.GetComponent<TextMeshProUGUI>();
        costText ??= transform.Find("Cost")?.GetComponent<TextMeshProUGUI>();
    }
    
    protected virtual void OnEnable()
    {
        PlayerNetwork.ClientOnGoldChanged += OnPlayerChanged;
        PlayerNetwork.ClientOnUpgradesChanged += OnPlayerChanged;
        PlayerNetwork.ClientOnPlayerUpdated += OnPlayerChanged;
    }
    
    protected virtual void OnDisable()
    {
        PlayerNetwork.ClientOnGoldChanged -= OnPlayerChanged;
        PlayerNetwork.ClientOnUpgradesChanged -= OnPlayerChanged;
        PlayerNetwork.ClientOnPlayerUpdated -= OnPlayerChanged;
    }
    
    protected virtual void Start()
    {
        if (buyButton != null)
            buyButton.onClick.AddListener(Buy);
        Refresh();
    }
    
    protected virtual void Update()
    {
        if (Input.GetKeyDown(Hotkey))
            Buy();
    }
    
    protected void OnPlayerChanged(PlayerNetwork _) => Refresh();
    
    protected abstract void Buy();
    protected abstract void Refresh();
    
    protected bool HasPlayer(out PlayerNetwork player)
    {
        player = PlayerNetwork.localPlayer;
        return player != null && player.playerId > 0 && !player.isEliminated;
    }

    protected void SetTitle(string text)
    {
        if (titleText != null) titleText.text = text;
    }

    protected void SetDesc(string text)
    {
        if (descText != null) descText.text = text;
    }

    protected void SetCost(string text)
    {
        if (costText != null) costText.text = text;
    }

    protected void SetInteractable(bool value)
    {
        if (buyButton != null) buyButton.interactable = value;
    }

    protected void SetOverlay(bool value)
    {
        if (overlayImage != null) overlayImage.enabled = value;
    }

    protected void SetNoPlayer(string title, string desc)
    {
        SetTitle(title);
        SetDesc(desc);
        SetCost("-");
        SetInteractable(false);
        SetOverlay(false);
    }
}
