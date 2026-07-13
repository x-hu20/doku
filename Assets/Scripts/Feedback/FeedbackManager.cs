using UnityEngine;

/// <summary>
/// 统一反馈调度中心：音频 + 触觉在同一入口同步发射，保证“声感同步率”。
/// 手势状态机（GameManager）只调用本类的语义接口（Exclude/Success/Error/Finish），
/// 无需关心音频预加载、静音开关与原生触觉底层差异。
///
/// 核心原则：
/// 1. 延迟控制——触觉与音频在触控响应第一帧同时发射；AudioSource 一次性常驻，clip 由
///    GameManager 在 Start 注入（被场景内 MonoBehaviour 引用即随场景预加载，避免动态加载滞后）。
/// 2. 静音开关——IsAudioEnabled / IsHapticsEnabled 关闭时仅跳过对应通道，交互逻辑不受影响。
/// </summary>
public class FeedbackManager : MonoBehaviour
{
    private static FeedbackManager _instance;

    /// <summary>
    /// 惰性单例：首次访问时自动创建宿主 GameObject + AudioSource。
    /// 必须用字段 _instance 直接判断，Awake 内不得走本属性，否则 AddComponent 触发 Awake 时
    /// 会再次进入 getter 创建第二个 GameObject，陷入无限递归。
    /// </summary>
    public static FeedbackManager Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("[FeedbackManager]");
                _instance = go.AddComponent<FeedbackManager>(); // 触发 Awake，内部把 _instance 指向自身
            }
            return _instance;
        }
    }

    [Header("音频素材（由 GameManager.Init 注入，也可在 Inspector 直接绑定）")]
    public AudioClip excludeClip;  // 单击/滑动排除
    public AudioClip successClip;  // 双击解锁成功
    public AudioClip errorClip;    // 双击解锁失败/点错
    public AudioClip finishClip;   // 通关

    [Header("音频参数")]
    public float basePitch = 1f;

    [Header("开关（持久化 PlayerPrefs）")]
    public bool isAudioEnabled = true;
    public bool isHapticsEnabled = true;

    private AudioSource _src;
    private const string KEY_AUDIO = "Meow_AudioOn";
    private const string KEY_HAPTIC = "Meow_HapticOn";

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);

        // 降低音频 DSP 缓冲：默认 1024 采样 @ 44100Hz ≈ 23ms/缓冲，Android 上经 2-3 缓冲管道后
        // PlayOneShot 到出声延迟 ~50-70ms，滑动"嗒嗒嗒"跟不上手指。降到 256 后 ≈ 5.8ms/缓冲，
        // 管道延迟 ~15-20ms，跟手。代价：音频线程更频繁处理，CPU 占用微增（UI 音效游戏无感）。
        // 必须在创建 AudioSource 之前 Reset，确保 Source 配置与新 DSP 设置同步。
        var config = AudioSettings.GetConfiguration();
        config.dspBufferSize = 256;
        AudioSettings.Reset(config);

        _src = gameObject.AddComponent<AudioSource>();
        ConfigureSource();

        // 还原持久化开关
        isAudioEnabled = PlayerPrefs.GetInt(KEY_AUDIO, 1) == 1;
        isHapticsEnabled = PlayerPrefs.GetInt(KEY_HAPTIC, 1) == 1;

#if UNITY_IOS && !UNITY_EDITOR
        NativeHapticDriver.Prepare(); // 预热 UIFeedbackGenerator
#endif
    }

    // AudioSettings.Reset 可能因机型不支持目标缓冲而重置音频系统；配置变更后需重新设置 Source。
    void OnAudioConfigurationChanged(bool deviceWasChanged)
    {
        if (_src != null) ConfigureSource();
    }

    private void ConfigureSource()
    {
        _src.playOnAwake = false;
        _src.spatialBlend = 0f;   // 2D，纯 UI 反馈
        _src.dopplerLevel = 0f;
        _src.priority = 0;        // 最高优先级，降低被其它音源挤占的概率
        _src.pitch = basePitch;
    }

    /// <summary>
    /// 由 GameManager.Start 调用，把 Inspector 绑定的 4 个 AudioClip 注入进来（实现预加载引用）。
    /// </summary>
    public void Init(AudioClip exclude, AudioClip success, AudioClip error, AudioClip finish)
    {
        if (exclude != null) excludeClip = exclude;
        if (success != null) successClip = success;
        if (error != null) errorClip = error;
        if (finish != null) finishClip = finish;
    }

    // ===================== 静音开关 API（设置界面调用） =====================
    public void SetAudioEnabled(bool on)
    {
        isAudioEnabled = on;
        PlayerPrefs.SetInt(KEY_AUDIO, on ? 1 : 0);
    }

    public void SetHapticsEnabled(bool on)
    {
        isHapticsEnabled = on;
        PlayerPrefs.SetInt(KEY_HAPTIC, on ? 1 : 0);
#if UNITY_IOS && !UNITY_EDITOR
        if (on) NativeHapticDriver.Prepare();
#endif
    }

    // ===================== 统一反馈接口（手势状态机调用） =====================

    /// <summary>场景1：单击/滑动排除——音频（固定 pitch）+ 轻度刻度震动</summary>
    public void Exclude()
    {
        Play(excludeClip);
        Haptic(NativeHapticDriver.HapticType.Selection);
    }

    /// <summary>场景2：双击解锁成功——成功音效 + 与单击一致的 Selection 轻刻度（棋盘点击触觉统一）</summary>
    public void Success()
    {
        Play(successClip);
        Haptic(NativeHapticDriver.HapticType.Selection);
    }

    /// <summary>场景3：双击解锁失败/点错——错误音效 + 与单击一致的 Selection 轻刻度（棋盘点击触觉统一）</summary>
    public void Error()
    {
        Play(errorClip);
        Haptic(NativeHapticDriver.HapticType.Selection);
    }

    /// <summary>纯触觉轻刻度（无音频）：用于已锁定猫格/错误锁定格的点击回应。
    /// 不改变状态，仅保证"每次点击棋盘都有一致的触觉反馈"。</summary>
    public void Tap()
    {
        Haptic(NativeHapticDriver.HapticType.Selection);
    }

    /// <summary>仅播 excludeClip 音频（固定 pitch，无触觉）：延迟判定模型下，叉号在阈值后才显示，
    /// excludeClip 同步延迟到"确认单击 / 滑动提交叉号"时才播。触觉已在按下第一帧由 Tap() 发出。
    /// 这样双击的第一次按下待定不发声，阈值内第二次按下直接走 Success/Error，全程无 excludeClip。</summary>
    public void ExcludeAudioOnly()
    {
        Play(excludeClip);
    }

    /// <summary>场景4：解锁完所有小猫，通关——通关是独立于棋盘点击的胜利时刻，保留 Success 通知震动以强化仪式感</summary>
    public void Finish()
    {
        Play(finishClip);
        Haptic(NativeHapticDriver.HapticType.Success);
    }

    // ===================== 内部播放原语 =====================
    private void Play(AudioClip clip)
    {
        if (!isAudioEnabled || clip == null || _src == null) return;
        _src.pitch = basePitch;
        _src.PlayOneShot(clip);
    }

    private void Haptic(NativeHapticDriver.HapticType type)
    {
        if (!isHapticsEnabled) return;
        NativeHapticDriver.Trigger(type);
    }

#if UNITY_EDITOR
    // QA 调试快捷键：M 切音频 / V 切震动（仅 Editor，不随包发布）
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.M))
        {
            SetAudioEnabled(!isAudioEnabled);
            Debug.Log($"[Feedback] Audio = {isAudioEnabled}");
        }
        if (Input.GetKeyDown(KeyCode.V))
        {
            SetHapticsEnabled(!isHapticsEnabled);
            Debug.Log($"[Feedback] Haptics = {isHapticsEnabled}");
        }
    }
#endif
}
