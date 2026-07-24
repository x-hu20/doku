using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 道具数量 / 红点 / 阶段宝箱 / 广告门控的纯逻辑容器。
///
/// 封装 magicWandCount / tipCount / levelsSinceChest / chest 奖励 / badge 刷新。
/// 锁猫/打叉的棋盘执行仍由 GameManager 处理（依赖棋盘状态），本类只管"数量记账 + 广告门控 + 红点 + 宝箱进度"。
///
/// 广告门控分两步（Gate → Spend）：先 Gate 判数量是否够/触广告，执行成功后再 Spend 扣减，
/// 避免选格失败（picked==null）却已扣减。
/// </summary>
public class ItemInventory
{
    private int magicWandCount;
    private int tipCount;
    private int levelsSinceChest; // 自上次开箱后已通关数（0~9，满10触发开箱展示）

    private readonly int chestRewardMagic;
    private readonly int chestRewardTip;

    // 红点 UI 引用（构造时注入）
    private readonly Image magicWandBadge;
    private readonly TMP_Text magicWandBadgeText;
    private readonly Image tipBadge;
    private readonly TMP_Text tipBadgeText;

    public int MagicWandCount => magicWandCount;
    public int TipCount => tipCount;
    public int LevelsSinceChest => levelsSinceChest;
    public bool ChestReadyThisShow { get; private set; }
    public int ChestRewardMagic => chestRewardMagic;
    public int ChestRewardTip => chestRewardTip;

    /// <summary>任一持久字段（数量/宝箱进度）变更时触发，供 GameManager 订阅写档。本类不直接调 SaveSystem（与文件 IO 解耦）。</summary>
    public event System.Action OnChanged;

    public ItemInventory(int magicWandCount, int tipCount, int levelsSinceChest,
        int chestRewardMagic, int chestRewardTip,
        Image magicWandBadge, TMP_Text magicWandBadgeText, Image tipBadge, TMP_Text tipBadgeText)
    {
        this.magicWandCount = magicWandCount;
        this.tipCount = tipCount;
        this.levelsSinceChest = levelsSinceChest;
        this.chestRewardMagic = chestRewardMagic;
        this.chestRewardTip = chestRewardTip;
        this.magicWandBadge = magicWandBadge;
        this.magicWandBadgeText = magicWandBadgeText;
        this.tipBadge = tipBadge;
        this.tipBadgeText = tipBadgeText;
    }

    private void RaiseChanged() => OnChanged?.Invoke();

    // ===== 红点刷新 =====
    public void RefreshBadges()
    {
        RefreshMagicWandBadge();
        RefreshTipBadge();
    }
    public void RefreshMagicWandBadge() => RefreshBadge(magicWandCount, magicWandBadge, magicWandBadgeText);
    public void RefreshTipBadge() => RefreshBadge(tipCount, tipBadge, tipBadgeText);

    private static void RefreshBadge(int count, Image badge, TMP_Text text)
    {
        if (badge != null) badge.gameObject.SetActive(count > 0);
        if (text != null && count > 0) text.text = count.ToString();
    }

    // ===== 广告门控（Gate：判数量/触广告，不扣减）=====
    /// <summary>魔法棒门控：数量>0 返回 true（调用方继续选格+锁猫，成功后调 SpendMagicWand）；
    /// 数量 0 触发激励广告（+1 刷新红点，不立即执行）返回 false。</summary>
    public bool GateMagicWand()
    {
        if (magicWandCount > 0) return true;
        AdManager.Instance?.ShowRewardedAd(
            onRewarded: () => { magicWandCount++; RefreshMagicWandBadge(); RaiseChanged(); },
            onFailedOrClosed: () => { /* 未获奖励：红点保持隐藏，玩家可再点重看 */ });
        return false;
    }

    /// <summary>提示门控：引导关免费返回 true；数量>0 返回 true；数量 0 触发广告返回 false。</summary>
    public bool GateTip(bool isTutorial)
    {
        if (isTutorial) return true; // 引导关免费使用，不走广告也不扣数量
        if (tipCount > 0) return true;
        AdManager.Instance?.ShowRewardedAd(
            onRewarded: () => { tipCount++; RefreshTipBadge(); RaiseChanged(); },
            onFailedOrClosed: () => { });
        return false;
    }

    // ===== 扣减（选格+执行成功后调）=====
    public void SpendMagicWand()
    {
        magicWandCount--;
        RefreshMagicWandBadge();
        RaiseChanged();
    }

    public void SpendTip()
    {
        tipCount--;
        RefreshTipBadge();
        RaiseChanged();
    }

    // ===== 阶段宝箱（每10关一轮）=====
    /// <summary>本关通关：计入宝箱进度，满10关标记开箱展示。</summary>
    public void OnLevelCompleted()
    {
        levelsSinceChest++;
        if (levelsSinceChest >= 10) ChestReadyThisShow = true;
        RaiseChanged();
    }

    /// <summary>宝箱打开（奖励遮罩弹出）时发放道具并归0宝箱进度（下一轮从0计）。
    /// 进度归0与发放同步落盘：避免玩家领奖后未关闭遮罩即退出后台，levelsSinceChest 未归0，
    /// 下关结算页仍显示宝箱待开启（领取看似失败）。发放是同步 Persist，退出前已写盘。</summary>
    public void GrantChestReward()
    {
        magicWandCount += chestRewardMagic;
        tipCount += chestRewardTip;
        levelsSinceChest = 0; // 归0宝箱进度：发放即归0，与遮罩关闭解耦（关闭路径 OnRewardClosed 幂等兜底）
        RefreshBadges();
        RaiseChanged();
    }

    /// <summary>奖励遮罩退出时归0进度（兜底；主归0已在 GrantChestReward 发放时完成，此处 0→0 幂等）。</summary>
    public void OnRewardClosed()
    {
        levelsSinceChest = 0;
        RaiseChanged();
    }

    /// <summary>本结算页开箱展示消费后清除（下关重新判定）。</summary>
    public void ConsumeChestReady()
    {
        ChestReadyThisShow = false;
    }
}
