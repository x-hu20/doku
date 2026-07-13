using System;

/// <summary>
/// 激励广告 Provider 抽象：调用方（失败弹窗的"3 more lives"按钮）只依赖本接口，
/// 后续接入真实 SDK（Unity Ads / AdMob / IronSource 等）时仅新增实现并注册到 AdManager，
/// 调用方零改动。本期由 <see cref="NullAdProvider"/> 占位，直接走 onRewarded 跑通流程。
/// </summary>
public interface IAdProvider
{
    /// <summary>激励广告是否就绪（可据此决定按钮可用性/文案，本期占位恒 true）</summary>
    bool IsRewardedAdReady { get; }

    /// <summary>
    /// 展示激励广告。广告播放完成且发放奖励 → <paramref name="onRewarded"/>；
    /// 广告加载失败/玩家中途关闭/无填充 → <paramref name="onFailedOrClosed"/>。
    /// 调用方据此决定补血（奖励）或保持弹窗（未获奖励）。
    /// </summary>
    void ShowRewardedAd(Action onRewarded, Action onFailedOrClosed);
}
