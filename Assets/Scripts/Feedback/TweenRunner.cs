using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 轻量协程补间工具：零外部依赖（替代 DOTween），用 Time.deltaTime 逐帧驱动。
/// 同一 Transform 上的新补间会自动终止该 Transform 上正在播放的旧补间，避免高频滑动时动画堆叠。
/// 所有方法静态调用，内部由一个常驻单宿主 MonoBehaviour 承载协程。
/// </summary>
public static class TweenRunner
{
    private class TweenHost : MonoBehaviour { }

    private static MonoBehaviour _host;
    // 同一 Transform 同时只允许一个补间；新补间 Kill 旧的
    private static readonly Dictionary<Transform, Coroutine> _active = new Dictionary<Transform, Coroutine>();

    private static MonoBehaviour Host
    {
        get
        {
            if (_host == null)
            {
                var go = new GameObject("[TweenRunner]");
                Object.DontDestroyOnLoad(go);
                _host = go.AddComponent<TweenHost>();
            }
            return _host;
        }
    }

    private static void Kill(Transform t)
    {
        if (t == null) return;
        if (_active.TryGetValue(t, out var c) && c != null) Host.StopCoroutine(c);
    }

    private static void RunExclusive(Transform t, IEnumerator routine)
    {
        Kill(t);
        _active[t] = Host.StartCoroutine(routine);
    }

    // ===================== 场景 1：叉号缩放回弹 0.2 -> 1.1 -> 1.0 =====================
    public static void CrossPunch(Transform t)
    {
        if (t == null) return;
        RunExclusive(t, CrossPunchRoutine(t));
    }

    private static IEnumerator CrossPunchRoutine(Transform t)
    {
        t.localScale = new Vector3(0.2f, 0.2f, 1f);
        yield return ScaleTo(t, 1.1f, 0.06f);
        yield return ScaleTo(t, 1.0f, 0.06f);
        t.localScale = Vector3.one;
    }

    // ===================== 场景 2：猫咪破壳弹跳 0 -> 1.25 -> 1.0 =====================
    public static void CatPop(Transform t)
    {
        if (t == null) return;
        RunExclusive(t, CatPopRoutine(t));
    }

    private static IEnumerator CatPopRoutine(Transform t)
    {
        t.localScale = Vector3.zero;
        yield return ScaleTo(t, 1.25f, 0.10f);
        yield return ScaleTo(t, 1.0f, 0.08f);
        t.localScale = Vector3.one;
    }

    // ===================== 场景 3：横向正弦衰减抖动 =====================
    public static void ShakeHorizontal(Transform t, float amplitude = 14f, float duration = 0.4f)
    {
        if (t == null) return;
        RunExclusive(t, ShakeRoutine(t, amplitude, duration));
    }

    private static IEnumerator ShakeRoutine(Transform t, float amp, float dur)
    {
        Vector3 basePos = t.localPosition;
        float elapsed = 0f;
        const float freq = 42f; // 角频率，配合 sin 产生快速正弦抖动
        while (elapsed < dur)
        {
            float k = 1f - elapsed / dur;            // 线性衰减包络
            float off = Mathf.Sin(elapsed * freq) * amp * k;
            t.localPosition = basePos + new Vector3(off, 0f, 0f);
            elapsed += Time.deltaTime;
            yield return null;
        }
        t.localPosition = basePos;
    }

    // ===================== 场景 3.5：引导双抖（两下柔和横向抖动，提示该格可双击）=====================
    /// <param name="t">格子 Transform</param>
    /// <param name="amplitude">单下抖动幅度（px），小于错误抖动以区分</param>
    /// <param name="eachMs">单下时长（毫秒），两下间有短暂停顿</param>
    public static void DoubleShake(Transform t, float amplitude = 7f, float eachMs = 180f)
    {
        if (t == null) return;
        RunExclusive(t, DoubleShakeRoutine(t, amplitude, eachMs / 1000f));
    }

    private static IEnumerator DoubleShakeRoutine(Transform t, float amp, float eachSec)
    {
        Vector3 basePos = t.localPosition;
        const float freq = 38f;
        for (int k = 0; k < 2; k++) // 两下
        {
            float elapsed = 0f;
            while (elapsed < eachSec)
            {
                float envelope = 1f - elapsed / eachSec; // 线性衰减
                float off = Mathf.Sin(elapsed * freq) * amp * envelope;
                t.localPosition = basePos + new Vector3(off, 0f, 0f);
                elapsed += Time.deltaTime;
                yield return null;
            }
            t.localPosition = basePos;
            if (k == 0) { float p = 0f; while (p < 0.07f) { p += Time.deltaTime; yield return null; } } // 两下间短暂停顿
        }
        t.localPosition = basePos;
    }

    // ===================== 场景 5：下一关按钮弹性亮相 0.0 ->1.25->0.90->1.0 =====================
    public static void ElasticButtonPop(Transform t)
    {
        if (t == null) return;
        RunExclusive(t, ElasticRoutine(t));
    }

    private static IEnumerator ElasticRoutine(Transform t)
    {
        t.localScale = Vector3.zero;
        yield return ScaleTo(t, 1.25f, 0.20f); // 200ms 视觉冲击
        yield return ScaleTo(t, 0.90f, 0.12f); // 120ms 惯性回弹
        yield return ScaleTo(t, 1.0f, 0.08f);  // 80ms 平滑稳定
        t.localScale = Vector3.one;
    }

    // ===================== 场景 6：按钮引导脉冲（循环 1 → peak → 1，吸引点击）=====================
    /// <summary>持续脉冲缩放，引导玩家点击。由调用方在点击/隐藏时调 <see cref="Stop"/> 停止并复位。</summary>
    /// <param name="t">按钮 Transform</param>
    /// <param name="peak">脉冲峰值倍率（相对当前 scale）</param>
    /// <param name="halfMs">单程时长（毫秒），一个完整脉冲 = 2×halfMs</param>
    public static void PulseLoop(Transform t, float peak = 1.12f, float halfMs = 280f)
    {
        if (t == null) return;
        RunExclusive(t, PulseLoopRoutine(t, peak, halfMs / 1000f));
    }

    private static IEnumerator PulseLoopRoutine(Transform t, float peak, float halfSec)
    {
        Vector3 baseScale = t.localScale;
        float peakX = baseScale.x * peak;
        while (true)
        {
            yield return ScaleTo(t, peakX, halfSec); // 放大（EaseOutCubic，起步快后缓）
            yield return ScaleTo(t, baseScale.x, halfSec); // 回落
        }
    }

    /// <summary>停止指定 Transform 上的活动补间（含 PulseLoop）。不复位 scale——调用方需自行复位到目标值。</summary>
    public static void Stop(Transform t)
    {
        Kill(t);
    }

    // ===================== 顶部 HUD 文本放大回弹 =====================
    public static void TextPunch(Transform t, float peak = 1.3f, float duration = 0.25f)
    {
        if (t == null) return;
        RunExclusive(t, TextPunchRoutine(t, peak, duration));
    }

    private static IEnumerator TextPunchRoutine(Transform t, float peak, float dur)
    {
        yield return ScaleTo(t, peak, dur * 0.5f);
        yield return ScaleTo(t, 1f, dur * 0.5f);
        t.localScale = Vector3.one;
    }

    // ===================== 场景 4：猫咪集体波浪起舞 LocalY 0 ->30->0 =====================
    /// <param name="transforms">所有已点出猫咪的 cat 节点；同步起跳，reps 控制往复次数</param>
    public static void WaveJump(List<Transform> transforms, float height = 30f, float upMs = 120f, float downMs = 120f, int reps = 2)
    {
        if (transforms == null) return;
        foreach (var tr in transforms)
        {
            if (tr == null) continue;
            Kill(tr);
            _active[tr] = Host.StartCoroutine(WaveRoutine(tr, height, upMs / 1000f, downMs / 1000f, reps));
        }
    }

    private static IEnumerator WaveRoutine(Transform t, float h, float upSec, float downSec, int reps)
    {
        // 起舞前归一缩放（避免上一只刚被点出的猫咪停在弹跳中途的缩放）
        t.localScale = Vector3.one;
        Vector3 baseLocal = t.localPosition;
        for (int r = 0; r < reps; r++)
        {
            yield return MoveLocalY(t, baseLocal.y + h, upSec);
            yield return MoveLocalY(t, baseLocal.y, downSec);
        }
        t.localPosition = baseLocal;
    }

    // ===================== 底层缓动原语 =====================
    private static IEnumerator ScaleTo(Transform t, float target, float dur)
    {
        Vector3 from = t.localScale;
        Vector3 to = new Vector3(target, target, from.z);
        float e = 0f;
        while (e < dur)
        {
            e += Time.deltaTime;
            float k = dur <= 0f ? 1f : Mathf.Clamp01(e / dur);
            t.localScale = Vector3.LerpUnclamped(from, to, EaseOutCubic(k));
            yield return null;
        }
        t.localScale = to;
    }

    private static IEnumerator MoveLocalY(Transform t, float targetY, float dur)
    {
        Vector3 p = t.localPosition;
        float fromY = p.y;
        float e = 0f;
        while (e < dur)
        {
            e += Time.deltaTime;
            float k = dur <= 0f ? 1f : Mathf.Clamp01(e / dur);
            p.y = Mathf.LerpUnclamped(fromY, targetY, EaseOutQuad(k));
            t.localPosition = p;
            yield return null;
        }
        p.y = targetY;
        t.localPosition = p;
    }

    private static float EaseOutCubic(float x) => 1f - Mathf.Pow(1f - x, 3f);
    private static float EaseOutQuad(float x) => 1f - (1f - x) * (1f - x);
}
