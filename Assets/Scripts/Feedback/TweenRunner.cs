using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 轻量协程补间工具：零外部依赖，用 Time.deltaTime 逐帧驱动。
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

    // ===================== 场景 3.6：手指双击循环（引导关模拟双击手势，缩放2次循环直到 Stop）=====================
    /// <param name="t">手指 Transform</param>
    /// <param name="pressScale">按下时缩放（<1 模拟按压）</param>
    /// <param name="tapMs">单次点击时长（毫秒）</param>
    /// <param name="gapMs">双击与双击之间的间隔（毫秒），拉大避免像连续点击</param>
    public static void FingerDoubleTapLoop(Transform t, float pressScale = 0.85f, float tapMs = 120f, float gapMs = 975f)
        => FingerDoubleTapLoop(t, null, pressScale, tapMs, gapMs);

    /// <param name="onPress">每次按下到底时回调（引导关触发目标格水波纹）。null 无副作用。</param>
    public static void FingerDoubleTapLoop(Transform t, System.Action onPress, float pressScale = 0.85f, float tapMs = 120f, float gapMs = 975f)
    {
        if (t == null) return;
        RunExclusive(t, FingerDoubleTapRoutine(t, onPress, pressScale, tapMs / 1000f, gapMs / 1000f));
    }

    private static IEnumerator FingerDoubleTapRoutine(Transform t, System.Action onPress, float press, float tapSec, float gapSec)
    {
        Vector3 baseScale = Vector3.one;
        // 弹入亮相（0 → 1），避免手指瞬切出现
        t.localScale = Vector3.zero;
        yield return ScaleTo(t, 1f, 0.25f);
        t.localScale = baseScale;
        while (true)
        {
            for (int k = 0; k < 2; k++) // 双击两次
            {
                yield return ScaleTo(t, press, tapSec); // 按下
                onPress?.Invoke();                        // 按到底触发目标格水波纹
                yield return ScaleTo(t, 1f, tapSec);    // 抬起
                if (k == 0) { float p = 0f; while (p < 0.06f) { p += Time.deltaTime; yield return null; } } // 两下间短停顿，区分两次点击
            }
            t.localScale = baseScale;
            float g = 0f;
            while (g < gapSec) { g += Time.deltaTime; yield return null; } // 双击之间的间隔
        }
    }

    // ===================== 场景 3.7：水波纹扩散淡出（引导关手指按下时格子上的按压波纹）=====================
    /// <param name="g">波纹 Graphic（RoundedImage 圆形）；scale 从 fromScale 扩到 toScale，alpha 从 peakA 淡到 0。</param>
    /// <param name="dur">扩散时长（秒）。非 exclusive——允许双击两圈波纹叠加扩散，协程自然结束不累积。</param>
    public static void RipplePunch(Graphic g, float fromScale = 0.3f, float toScale = 1.0f, float peakA = 0.6f, float dur = 0.28f)
    {
        if (g == null) return;
        Host.StartCoroutine(RippleRoutine(g, fromScale, toScale, peakA, dur));
    }

    private static IEnumerator RippleRoutine(Graphic g, float fromScale, float toScale, float peakA, float dur)
    {
        Transform t = g.transform;
        t.localScale = new Vector3(fromScale, fromScale, 1f);
        Color c = g.color; c.a = peakA; g.color = c;
        float e = 0f;
        while (e < dur)
        {
            e += Time.deltaTime;
            float k = dur <= 0f ? 1f : Mathf.Clamp01(e / dur);
            float s = Mathf.LerpUnclamped(fromScale, toScale, EaseOutCubic(k));
            t.localScale = new Vector3(s, s, 1f);
            c.a = Mathf.Lerp(peakA, 0f, EaseOutCubic(k));
            g.color = c;
            yield return null;
        }
        c.a = 0f; g.color = c;
        t.localScale = Vector3.one;
    }

    // ===================== 场景 3.8：手指单击循环（引导关排除阶段逐格单击引导，节奏与双击一致）=====================
    public static void FingerSingleTapLoop(Transform t, float pressScale = 0.85f, float tapMs = 120f, float gapMs = 975f)
        => FingerSingleTapLoop(t, null, pressScale, tapMs, gapMs);

    /// <param name="onPress">每次按下到底时回调（触发当前引导格水波纹）。</param>
    public static void FingerSingleTapLoop(Transform t, System.Action onPress, float pressScale = 0.85f, float tapMs = 120f, float gapMs = 975f)
    {
        if (t == null) return;
        RunExclusive(t, FingerSingleTapRoutine(t, onPress, pressScale, tapMs / 1000f, gapMs / 1000f));
    }

    private static IEnumerator FingerSingleTapRoutine(Transform t, System.Action onPress, float press, float tapSec, float gapSec)
    {
        Vector3 baseScale = Vector3.one;
        t.localScale = Vector3.zero;
        yield return ScaleTo(t, 1f, 0.25f); // 弹入亮相
        t.localScale = baseScale;
        while (true)
        {
            yield return ScaleTo(t, press, tapSec); // 按下
            onPress?.Invoke();                        // 触发当前引导格水波纹
            yield return ScaleTo(t, 1f, tapSec);    // 抬起
            float g = 0f;
            while (g < gapSec) { g += Time.deltaTime; yield return null; } // 单击之间的间隔（与双击循环间隔一致）
        }
    }

    // ===================== 场景 3.9：手指沿路径滑动循环（引导关 Step3 滑动演示）=====================
    /// <param name="anchors">路径节点世界坐标（finger 不旋转，世界=局部）。</param>
    /// <param name="segMs">单段滑动时长（毫秒）——值越大滑动越慢。</param>
    /// <param name="pauseMs">终点停顿时长（毫秒），滑动循环间隔。</param>
    public static void FingerSwipeLoop(Transform t, Vector3[] anchors, float segMs = 520f, float pauseMs = 780f)
    {
        if (t == null || anchors == null || anchors.Length < 2) return;
        RunExclusive(t, FingerSwipeRoutine(t, anchors, segMs / 1000f, pauseMs / 1000f));
    }

    private static IEnumerator FingerSwipeRoutine(Transform t, Vector3[] anchors, float segSec, float pauseSec)
    {
        t.localScale = Vector3.zero;
        yield return ScaleTo(t, 1f, 0.25f); // 弹入亮相
        t.localScale = Vector3.one;
        t.position = anchors[0];
        int n = anchors.Length;
        while (true)
        {
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 from = anchors[i], to = anchors[i + 1];
                float e = 0f;
                while (e < segSec)
                {
                    e += Time.deltaTime;
                    t.position = Vector3.Lerp(from, to, EaseOutQuad(Mathf.Clamp01(e / segSec)));
                    yield return null;
                }
                t.position = to;
            }
            float p = 0f;
            while (p < pauseSec) { p += Time.deltaTime; yield return null; } // 终点停顿后回起点重来
            t.position = anchors[0];
        }
    }

    // ===================== 场景 3.10：箭头沿路径滑动循环（Step3 箭头拖尾，延迟手指 delayMs 后独立划过同一路径）=====================
    /// <param name="anchors">与手指相同的路径节点世界坐标。</param>
    /// <param name="delayMs">整体延迟启动时长（毫秒）——箭头比手指晚启动，之后同节奏保持固定相位差。</param>
    public static void ArrowSwipeLoop(Transform t, Vector3[] anchors, float segMs = 520f, float pauseMs = 780f, float delayMs = 500f)
    {
        if (t == null || anchors == null || anchors.Length < 2) return;
        RunExclusive(t, ArrowSwipeRoutine(t, anchors, segMs / 1000f, pauseMs / 1000f, delayMs / 1000f));
    }

    private static IEnumerator ArrowSwipeRoutine(Transform t, Vector3[] anchors, float segSec, float pauseSec, float delaySec)
    {
        // 箭头先隐藏，延迟 delaySec 后弹入亮相（手指已先行），再与手指同节奏循环，保持固定 0.5s 相位差
        t.localScale = Vector3.zero;
        t.position = anchors[0];
        float d = 0f;
        while (d < delaySec) { d += Time.deltaTime; yield return null; }
        yield return ScaleTo(t, 1f, 0.25f); // 延迟后弹入亮相
        t.localScale = Vector3.one;
        int n = anchors.Length;
        while (true)
        {
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 from = anchors[i], to = anchors[i + 1];
                Vector3 dir = to - from;
                float len = dir.magnitude;
                Vector3 ndir = len > 0.0001f ? dir / len : Vector3.right;
                // 箭头素材原方向朝上(+y)：atan2 算出 +x 基准角后补偿 90° 对齐段方向
                t.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(ndir.y, ndir.x) * Mathf.Rad2Deg - 90f);
                float e = 0f;
                while (e < segSec)
                {
                    e += Time.deltaTime;
                    t.position = Vector3.Lerp(from, to, EaseOutQuad(Mathf.Clamp01(e / segSec)));
                    yield return null;
                }
                t.position = to;
            }
            float p = 0f;
            while (p < pauseSec) { p += Time.deltaTime; yield return null; }
            t.position = anchors[0];
        }
    }


    public static void ShakeLoop(Transform t, float amplitude = 10f, float eachMs = 300f)
    {
        if (t == null) return;
        RunExclusive(t, ShakeLoopRoutine(t, amplitude, eachMs / 1000f));
    }

    private static IEnumerator ShakeLoopRoutine(Transform t, float amp, float eachSec)
    {
        Vector3 basePos = t.localPosition;
        const float freq = 40f;
        while (true)
        {
            float elapsed = 0f;
            while (elapsed < eachSec)
            {
                float k = 1f - elapsed / eachSec;
                float off = Mathf.Sin(elapsed * freq) * amp * k;
                t.localPosition = basePos + new Vector3(off, 0f, 0f);
                elapsed += Time.deltaTime;
                yield return null;
            }
            t.localPosition = basePos;
            float pause = 0f;
            while (pause < 0.1f) { pause += Time.deltaTime; yield return null; } // 抖动间隔
        }
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

    // ===================== 渐入/渐出（alpha，引导关文案/遮罩/手指缓慢出现与消失）=====================
    // exclusive=true（默认）走 RunExclusive，与同 Transform 的其他补间互斥；
    // exclusive=false 不入注册表，可与同 Transform 的缩放循环并存（手指 alpha 淡入淡出 vs 双击缩放循环）。
    public static void FadeIn(Graphic g, float dur = 0.3f, bool exclusive = true)
    {
        if (g == null) return;
        if (dur <= 0f) { var c = g.color; c.a = 1f; g.color = c; return; }
        if (exclusive) RunExclusive(g.transform, FadeRoutine(g, dur));
        else Host.StartCoroutine(FadeRoutine(g, dur));
    }

    public static void FadeOut(Graphic g, float dur = 0.3f, bool exclusive = true)
    {
        if (g == null) return;
        if (dur <= 0f) { var c = g.color; c.a = 0f; g.color = c; return; }
        if (exclusive) RunExclusive(g.transform, FadeOutRoutine(g, dur));
        else Host.StartCoroutine(FadeOutRoutine(g, dur));
    }

    private static IEnumerator FadeRoutine(Graphic g, float dur)
    {
        Color c = g.color;
        c.a = 0f;
        g.color = c;
        float e = 0f;
        while (e < dur)
        {
            e += Time.deltaTime;
            c.a = Mathf.Lerp(0f, 1f, EaseOutCubic(Mathf.Clamp01(e / dur)));
            g.color = c;
            yield return null;
        }
        c.a = 1f;
        g.color = c;
    }

    private static IEnumerator FadeOutRoutine(Graphic g, float dur)
    {
        Color c = g.color;
        float startA = c.a;
        float e = 0f;
        while (e < dur)
        {
            e += Time.deltaTime;
            c.a = Mathf.Lerp(startA, 0f, EaseOutCubic(Mathf.Clamp01(e / dur)));
            g.color = c;
            yield return null;
        }
        c.a = 0f;
        g.color = c;
    }

    /// <summary>CanvasGroup 渐入/渐出（整组遮罩淡入淡出，不触碰子节点 raycast/scale）。</summary>
    public static void FadeIn(CanvasGroup cg, float dur = 0.3f)
    {
        if (cg == null) return;
        if (dur <= 0f) { cg.alpha = 1f; return; }
        RunExclusive(cg.transform, FadeCGRoutine(cg, dur, 1f));
    }

    public static void FadeOut(CanvasGroup cg, float dur = 0.3f)
    {
        if (cg == null) return;
        if (dur <= 0f) { cg.alpha = 0f; return; }
        RunExclusive(cg.transform, FadeCGRoutine(cg, dur, 0f));
    }

    private static IEnumerator FadeCGRoutine(CanvasGroup cg, float dur, float target)
    {
        float startA = cg.alpha;
        float e = 0f;
        while (e < dur)
        {
            e += Time.deltaTime;
            cg.alpha = Mathf.Lerp(startA, target, EaseOutCubic(Mathf.Clamp01(e / dur)));
            yield return null;
        }
        cg.alpha = target;
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
