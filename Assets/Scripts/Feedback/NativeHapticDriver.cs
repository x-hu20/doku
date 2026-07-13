using UnityEngine;

/// <summary>
/// 底层原生触觉桥接类（无任何外部资源依赖）。
/// iOS：直接调用系统内置 UIFeedbackGenerator 预设（Selection / Notification）。
/// Android：API 26+ 走 VibrationEffect（createOneShot / createWaveform，振幅 -1=DEFAULT_AMPLITUDE），
///   首次触发时缓存 SDK_INT、Vibrator、VibrationEffect 类与各预设 effect（不可变可复用），
///   触发时仅一次 vibrate 调用、零 JNI 分配；API&lt;26 或缓存失效回退 deprecated vibrate(long)。
/// Editor / 其他平台：静默 no-op，保证交互逻辑不受影响且无报错。
/// </summary>
public static class NativeHapticDriver
{
    public enum HapticType
    {
        Selection = 0, // 轻度机械刻度（UISelectionFeedbackGenerator）
        Success = 1,   // 成功通知
        Error = 2,     // 错误通知
        Warning = 3,   // 警告通知
        Light = 4,     // 轻触
        Medium = 5,    // 中触
        Heavy = 6      // 重触
    }

    public static bool IsAvailable
    {
        get
        {
#if UNITY_IOS && !UNITY_EDITOR
            return true;
#elif UNITY_ANDROID && !UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// 预热发生器（iOS 上让 UIFeedbackGenerator 提前就绪，降低首次触发延迟）。
    /// 在 FeedbackManager 启动时调用一次即可。
    /// </summary>
    public static void Prepare()
    {
#if UNITY_IOS && !UNITY_EDITOR
        try { MeowHaptic_Prepare(); } catch { /* 忽略：旧系统无此能力不应阻塞 */ }
#endif
    }

    public static void Trigger(HapticType type)
    {
#if UNITY_IOS && !UNITY_EDITOR
        try { MeowHaptic_Trigger((int)type); } catch { }
#elif UNITY_ANDROID && !UNITY_EDITOR
        AndroidTrigger(type);
#elif UNITY_EDITOR
        // Editor 无真机震动：仅打印，便于 QA 确认触觉代码路径是否走通（与音频同帧发射）
        Debug.Log($"[NativeHaptic] (Editor 无真机震动) 触发: {type}");
#endif
    }

    // ===================== iOS =====================
#if UNITY_IOS && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void MeowHaptic_Trigger(int type);

    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void MeowHaptic_Prepare();
#endif

    // ===================== Android =====================
#if UNITY_ANDROID && !UNITY_EDITOR
    // —— JNI 缓存：高频滑动每格都触发震动，若每次 new AndroidJavaClass/AndroidJavaObject 会产生
    //   大量 GC 分配与 JNI round-trip。这里把 SDK 版本、Vibrator 实例、VibrationEffect 类与各预设 effect
    //   缓存为静态字段；VibrationEffect 不可变可安全长期复用，触发时仅一次 vibrate 调用、零分配。
    //   静态字段常驻 app 生命周期（Activity/Vibrator 同生命周期稳定），无需 OnDestroy 清理。
    private static int _sdkInt = -1;
    private static AndroidJavaObject _cachedActivity;
    private static AndroidJavaObject _cachedVib;
    private static AndroidJavaClass _effectCls;
    private static AndroidJavaObject _effSelection; // createOneShot(20ms, DEFAULT)
    private static AndroidJavaObject _effWarning;   // createOneShot(40ms, DEFAULT)
    private static AndroidJavaObject _effSuccess;   // waveform [0,20,40,60]
    private static AndroidJavaObject _effError;     // waveform [0,30,50,90]
    private static bool _disabled;                  // 触发/初始化失败后置位，避免滑动时每格刷屏 LogError

    private static void AndroidTrigger(HapticType type)
    {
        if (_disabled) return; // 已确认此机不可用，静默跳过
        try
        {
            if (_cachedVib == null && !EnsureReady())
            {
                _disabled = true; // Vibrator 获取失败 → 禁用，避免高频滑动反复重试刷屏
                Debug.LogWarning("[NativeHaptic] Vibrator 不可用，已禁用触觉");
                return;
            }

            AndroidJavaObject effect = (_sdkInt >= 26) ? GetCachedEffect(type) : null;
            if (effect != null)
            {
                _cachedVib.Call("vibrate", effect); // 仅这一次 JNI 调用，零分配
                Debug.Log($"[NativeHaptic] 触发 type={type} api={_sdkInt} effectOk=True (复用缓存)");
            }
            else
            {
                VibrateSimple(_cachedVib, type); // 回退：deprecated vibrate(long)，全 API 可用
                Debug.Log($"[NativeHaptic] 触发 type={type} api={_sdkInt} effectOk=False (回退 vibrate(long))");
            }
        }
        catch (System.Exception e)
        {
            // 驱动层绝不上抛。异常多由权限缺失/系统不可用导致，置位 _disabled 避免高频滑动刷屏
            _disabled = true;
            Debug.LogError("[NativeHaptic] Android 触发异常，已禁用触觉: " + e);
        }
    }

    // 首次触发时一次性初始化所有缓存。返回 false 表示 Vibrator 获取失败。
    private static bool EnsureReady()
    {
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                _cachedActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"); // 不 using：需常驻
            if (_cachedActivity == null) return false;

            using (var ver = new AndroidJavaClass("android.os.Build$VERSION"))
                _sdkInt = ver.GetStatic<int>("SDK_INT");

            _cachedVib = GetVibrator(_cachedActivity, _sdkInt);
            if (_cachedVib == null) return false;

            // API 26+ 预创建各 VibrationEffect（不可变，可长期复用）；创建失败保持 null，触发时回退 vibrate(long)
            if (_sdkInt >= 26)
            {
                _effectCls = new AndroidJavaClass("android.os.VibrationEffect");
                _effSelection = CreateOneShot(20L, -1);
                _effWarning = CreateOneShot(40L, -1);
                _effSuccess = CreateWaveform(new long[] { 0, 20, 40, 60 }, new int[] { 0, 255, 0, 255 });
                _effError = CreateWaveform(new long[] { 0, 30, 50, 90 }, new int[] { 0, 255, 0, 255 });
            }
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[NativeHaptic] JNI 缓存初始化失败: " + e.Message);
            return false;
        }
    }

    private static AndroidJavaObject GetCachedEffect(HapticType type)
    {
        switch (type)
        {
            case HapticType.Selection:
            case HapticType.Light: return _effSelection;
            case HapticType.Success: return _effSuccess;
            case HapticType.Error: return _effError;
            case HapticType.Warning: return _effWarning;
            default: return _effSelection; // 复用轻震
        }
    }

    // API 31+ 走 VibratorManager.getDefaultVibrator()；旧版直接 getSystemService("vibrator")
    private static AndroidJavaObject GetVibrator(AndroidJavaObject activity, int api)
    {
        try
        {
            if (api >= 31)
            {
                using (var mgr = activity.Call<AndroidJavaObject>("getSystemService", "vibrator_manager"))
                    if (mgr != null) return mgr.Call<AndroidJavaObject>("getDefaultVibrator");
            }
            return activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
        }
        catch { return null; }
    }

    // createOneShot 返回不可变 VibrationEffect，缓存复用；失败返回 null
    private static AndroidJavaObject CreateOneShot(long ms, int amplitude)
    {
        try { return _effectCls.CallStatic<AndroidJavaObject>("createOneShot", ms, amplitude); }
        catch { return null; }
    }

    // createWaveform 返回不可变 VibrationEffect，缓存复用；失败回退 createOneShot
    private static AndroidJavaObject CreateWaveform(long[] timings, int[] amplitudes)
    {
        try { return _effectCls.CallStatic<AndroidJavaObject>("createWaveform", timings, amplitudes, -1); }
        catch { return CreateOneShot(40L, -1); }
    }

    // 回退：deprecated vibrate(long)。API26+ 虽标记 deprecated 但仍生效（内部转 VibrationEffect），全版本可用
    private static void VibrateSimple(AndroidJavaObject vib, HapticType type)
    {
        long ms;
        switch (type)
        {
            case HapticType.Selection:
            case HapticType.Light: ms = 20; break;
            case HapticType.Success: ms = 40; break;
            case HapticType.Error: ms = 60; break;
            case HapticType.Warning: ms = 50; break;
            default: ms = 30; break;
        }
        vib.Call("vibrate", ms);
    }
#endif
}
