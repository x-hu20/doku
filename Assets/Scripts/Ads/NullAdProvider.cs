using System;

/// <summary>
/// 激励广告占位实现：本期无真实 SDK，直接同步回调 <see cref="OnRewarded"/> 模拟"广告成功"，
/// 供失败弹窗的"3 more lives"复活流程跑通与 QA 验证。
/// 后续接入真实 SDK 时替换为 XxxAdProvider，调用方零改动。
/// </summary>
public class NullAdProvider : IAdProvider
{
    public bool IsRewardedAdReady => true;

    public void ShowRewardedAd(Action onRewarded, Action onFailedOrClosed)
    {
        // 占位：直接发放奖励（模拟玩家看完了广告）。
        // 真实 SDK 这里应先 Init/Load，Show 后在 onShow/completed 回调里分发 onRewarded，
        // 在 onClose/onError 回调里分发 onFailedOrClosed。
        DebugLog("[AdProvider/Null] 模拟激励广告 → 发放奖励（占位，无真实 SDK）");
        onRewarded?.Invoke();
    }

    private void DebugLog(string msg)
    {
        UnityEngine.Debug.Log(msg);
    }
}
