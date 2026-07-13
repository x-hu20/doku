using UnityEngine;

/// <summary>
/// 广告调度单例：持有当前 <see cref="IAdProvider"/>，对调用方暴露统一入口。
/// 本期默认注册 <see cref="NullAdProvider"/>；后续接真实 SDK 时在此处替换 Provider 注册，
/// 调用方（失败弹窗按钮）代码不变。
///
/// 采用与 FeedbackManager 一致的惰性单例模式：首次访问 Instance 时创建宿主 GameObject。
/// Awake 内必须直接写字段 _instance，不可走 Instance 属性，否则 AddComponent 触发 Awake 递归。
/// </summary>
public class AdManager : MonoBehaviour
{
    private static AdManager _instance;

    public static AdManager Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("[AdManager]");
                _instance = go.AddComponent<AdManager>(); // 触发 Awake，内部把 _instance 指向自身
            }
            return _instance;
        }
    }

    /// <summary>当前广告 Provider。启动时默认为 NullAdProvider；后续可由 SDK 初始化脚本替换。</summary>
    public IAdProvider Provider { get; private set; } = new NullAdProvider();

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>替换 Provider（后续真实 SDK 初始化完成后调用，注册 XxxAdProvider）。</summary>
    public void SetProvider(IAdProvider provider)
    {
        if (provider == null) return;
        Provider = provider;
    }

    /// <summary>便捷入口：展示激励广告。等价于 Provider.ShowRewardedAd，但容忍 Provider 为空。</summary>
    public void ShowRewardedAd(System.Action onRewarded, System.Action onFailedOrClosed)
    {
        if (Provider == null)
        {
            Debug.LogWarning("[AdManager] 无可用 Provider，按未获奖励处理。");
            onFailedOrClosed?.Invoke();
            return;
        }
        Provider.ShowRewardedAd(onRewarded, onFailedOrClosed);
    }
}
