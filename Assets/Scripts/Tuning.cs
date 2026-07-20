using UnityEngine;

/// <summary>
/// 全局调参常量集中处：跨文件共享的时长/振幅/颜色等魔法数统一引用此处，避免多处字面量复制漂移。
/// 仅放"多处复用"的值；组件内部一次性参数仍就地定义。
/// </summary>
public static class Tuning
{
    // ===== 错误反馈（双击错格：红框 + 抖动 + 叉号染红）=====
    /// <summary>错误抖动振幅（px）。BlockController 错误抖动 + 关闭宝箱抖动共用 14f。</summary>
    public const float ErrorShakeAmplitude = 14f;
    /// <summary>错误抖动时长（秒）= 红框持续时长 = HideErrorFrameAfter 延迟，三者须同步。</summary>
    public const float ErrorShakeDuration = 0.4f;
    /// <summary>错误红框/叉号染红的统一颜色。</summary>
    public static readonly Color ErrorColor = new Color(0.98f, 0.36f, 0.20f, 1f);

    // ===== 按钮引导脉冲（持续缩放吸引点击）=====
    /// <summary>脉冲峰值倍率。start game / got it / next level 按钮共用。</summary>
    public const float ButtonPulsePeak = 1.10f;
    /// <summary>脉冲单程时长（毫秒），一个完整脉冲 = 2×halfMs。</summary>
    public const float ButtonPulseHalfMs = 600f;
}
