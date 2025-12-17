using UnityEngine;

public enum ShopItemType
{
    Sharpening,
    GoldRush,
    Vampirism,
    Artillery,
    Emp,
    DeepStrike,
    Trap
}

public class ShopItemUI : ShopItemBase
{
    [SerializeField] private ShopItemType itemType;
    [SerializeField] private string displayName = "Item";
    [SerializeField] private string description = "Description";
    [SerializeField] private KeyCode hotkey = KeyCode.Alpha1;

    protected override KeyCode Hotkey => hotkey;

    protected override void Buy()
    {
        if (!HasPlayer(out var player)) return;

        switch (itemType)
        {
            case ShopItemType.Sharpening: player.CmdBuySharpening(); break;
            case ShopItemType.GoldRush: player.CmdBuyGoldRush(); break;
            case ShopItemType.Vampirism: player.CmdBuyVampirism(); break;
            case ShopItemType.Artillery: player.CmdBuyArtillery(); break;
            case ShopItemType.Emp: player.CmdBuyEmp(); break;
            case ShopItemType.DeepStrike: player.CmdBuyDeepStrike(); break;
            case ShopItemType.Trap: player.CmdBuyTrap(); break;
        }
    }

    protected override void Refresh()
    {
        if (!HasPlayer(out var player))
        {
            SetNoPlayer(displayName, description);
            return;
        }

        switch (itemType)
        {
            case ShopItemType.Sharpening: RefreshSharpening(player); break;
            case ShopItemType.GoldRush: RefreshGoldRush(player); break;
            case ShopItemType.Vampirism: RefreshVampirism(player); break;
            case ShopItemType.Artillery: RefreshActiveSkill(player, ActiveSkill.Artillery, PlayerNetwork.GetArtilleryCost()); break;
            case ShopItemType.Emp: RefreshActiveSkill(player, ActiveSkill.Emp, PlayerNetwork.GetEmpCost()); break;
            case ShopItemType.DeepStrike: RefreshActiveSkill(player, ActiveSkill.DeepStrike, PlayerNetwork.GetDeepStrikeCost()); break;
            case ShopItemType.Trap: RefreshTrap(player); break;
        }
    }

    private void RefreshSharpening(PlayerNetwork player)
    {
        uint cost = PlayerNetwork.GetSharpeningCost(player.sharpeningLevel);
        SetTitle($"{displayName} (Lv {player.sharpeningLevel})");
        SetDesc($"{description} ({player.clickPower})");
        SetCost(cost.ToString());
        SetInteractable(player.goldTenths >= (int)cost * 10);
    }

    private void RefreshGoldRush(PlayerNetwork player)
    {
        int level = player.goldRushLevel;
        uint cost = PlayerNetwork.GetGoldRushCost(level);
        int percent = 100 + 10 * level;
        SetTitle($"{displayName} (Lv {level})");
        SetDesc($"{description} ({percent}%)");
        SetCost(cost.ToString());
        SetInteractable(player.goldTenths >= (int)cost * 10);
    }

    private void RefreshVampirism(PlayerNetwork player)
    {
        uint cost = PlayerNetwork.GetVampirismCost();
        bool purchased = player.hasVampirism;
        SetTitle(displayName);
        SetDesc(description);
        SetCost(purchased ? "" : cost.ToString());
        SetInteractable(!purchased && player.goldTenths >= (int)cost * 10);
        SetOverlay(purchased);
    }

    private void RefreshActiveSkill(PlayerNetwork player, ActiveSkill skill, uint cost)
    {
        bool isActive = player.activeSkill == skill;
        SetTitle(displayName);
        SetDesc(description);
        SetCost(cost.ToString());
        SetInteractable(player.activeSkill == ActiveSkill.None && player.goldTenths >= (int)cost * 10);
        SetOverlay(isActive);
    }

    private void RefreshTrap(PlayerNetwork player)
    {
        uint cost = PlayerNetwork.GetTrapCost();
        bool isActive = player.activeSkill == ActiveSkill.Trap;
        bool canBuy = (player.activeSkill == ActiveSkill.None || isActive) && (isActive || player.goldTenths >= (int)cost * 10);
        SetTitle(displayName);
        SetDesc(description);
        SetCost(cost.ToString());
        SetInteractable(canBuy);
        SetOverlay(isActive);
    }
}
