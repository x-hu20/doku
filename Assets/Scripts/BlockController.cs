using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class BlockController : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler
{
    public Image bgImage;    // 格子背景（根 RoundedImage，保留 Raycast Target 接收输入）
    public GameObject cat;   // 猫咪图标节点（其 Image 关闭 Raycast Target，显隐由 catImage.enabled 控制）
    public GameObject cross; // 叉号图标节点（同上）

    [HideInInspector] public int row, col;
    [HideInInspector] public int colorID;
    [HideInInspector] public bool isCorrect = false;
    [HideInInspector] public bool isGiven = false;  // 关卡初始给出的小猫位（锁定，不响应任何操作）
    public bool hasCat = false;     // 已锁定的小猫（given 或双击锁定），显示猫
    public bool hasCross = false;   // 排除标记，显示叉号

    [HideInInspector] public bool isErrorLocked = false; // 双击点错后该关锁定，不再响应点击

    private GameManager gameManager; // 交互协调由 GameManager 统一处理
    private Image catImage;   // 缓存：避免每次显隐 GetComponent；Awake 时一次性获取
    private Image crossImage;
    private Color crossOriginalColor; // 叉号原始颜色（错误反馈染红后，Setup 复位防对象池串色）
    private Outline errorOutline; // 错误红框（Outline 特效，挂在 bgImage 上，错误反馈期间临时启用）
    private Coroutine errorFeedbackRoutine; // 红框消失+叉号染红的延迟协程（Setup 时停掉，防重开关卡残留串色）

    // 红框持续时长 = 抖动时长（Tuning.ErrorShakeDuration），抖动结束即关红框、叉号染红
    private const float ErrorFeedbackDuration = Tuning.ErrorShakeDuration;

    private void Awake()
    {
        // cat/cross 在预制体中作为 GameObject 引用绑定，此处一次性取其 Image 组件用于 enabled 控制
        catImage = cat != null ? cat.GetComponent<Image>() : null;
        crossImage = cross != null ? cross.GetComponent<Image>() : null;
        if (crossImage != null) crossOriginalColor = crossImage.color;
        EnsureErrorOutline();
    }

    // 错误红框：复用 bgImage 上的 Outline 特效作边框，初始禁用；Setup 时复位。
    // 优先 GetComponent 复用预制体上已挂的 Outline；缺失才运行时 AddComponent 兜底（池复用后仅首次实例化触发）。
    private void EnsureErrorOutline()
    {
        if (bgImage == null) return;
        errorOutline = bgImage.GetComponent<Outline>();
        if (errorOutline == null) errorOutline = bgImage.gameObject.AddComponent<Outline>();
        errorOutline.effectColor = Tuning.ErrorColor;
        errorOutline.effectDistance = new Vector2(5f, -5f);
        errorOutline.enabled = false;
    }

    public void Setup(int r, int c, int cID, Color color, GameManager manager)
    {
        // 重置所有持久状态：对象池复用时本格可能带着上一关的猫/叉号/given 标记
        isCorrect = false;
        isGiven = false;
        hasCat = false;
        hasCross = false;
        isErrorLocked = false;
        if (errorOutline != null) errorOutline.enabled = false;
        if (crossImage != null) crossImage.color = crossOriginalColor; // 复位叉号颜色（上一关可能染红）
        if (errorFeedbackRoutine != null) { StopCoroutine(errorFeedbackRoutine); errorFeedbackRoutine = null; } // 停残留协程

        row = r;
        col = c;
        colorID = cID;
        bgImage.color = color;
        gameManager = manager;

        ApplyVisualState(); // 仅切显隐、不播动画（载入时无声显示）
    }

    // ===================== 显隐（纯状态，无动画副作用）=====================
    // ApplyVisualState 只切 Image.enabled 提交轻量渲染指令，不调用 GameObject.SetActive。
    // enabled 仅脏标记当前 Graphic 的顶点，不破坏 Canvas 顶点拓扑结构，
    // 高速滑动时 Canvas.SendWillRenderCanvases 耗时接近 0ms。
    // 动画与显隐解耦：需要动画时由调用方显式调 Play* 方法，载入/滑动只刷新显隐不播动画。
    public void ApplyVisualState()
    {
        if (catImage != null) catImage.enabled = hasCat;
        if (crossImage != null)
        {
            crossImage.enabled = hasCross;
            if (!hasCross) crossImage.transform.localScale = Vector3.one; // 叉号隐藏时复位缩放，避免下次出拳从中途开始
        }
    }

    // ===================== 动画（显式触发，调用方决定时机）=====================
    /// <summary>猫咪破壳弹跳 0→1.25→1.0（双击锁猫成功时调）</summary>
    public void PlayCatPop()
    {
        if (catImage != null) TweenRunner.CatPop(catImage.transform);
    }

    /// <summary>叉号缩放回弹 0.2→1.1→1.0（确认单击出叉 / 滑动提交叉号时调）</summary>
    public void PlayCrossPunch()
    {
        if (crossImage != null) TweenRunner.CrossPunch(crossImage.transform);
    }

    /// <summary>横向正弦衰减抖动（双击点错时调）</summary>
    public void PlayErrorShake()
    {
        TweenRunner.ShakeHorizontal(transform, Tuning.ErrorShakeAmplitude, Tuning.ErrorShakeDuration);
    }

    /// <summary>两下柔和横向抖动（提示道具取消 cross 后引导双击该格）</summary>
    public void PlayAttentionShake()
    {
        TweenRunner.DoubleShake(transform);
    }

    /// <summary>本关锁定（不再响应点击）。视觉反馈由 <see cref="PlayErrorFeedback"/> 单独驱动。</summary>
    public void LockAsError()
    {
        isErrorLocked = true;
    }

    /// <summary>从存档恢复错误锁定格：置 isErrorLocked + hasCross，叉号染红（不播抖动/红框动画）。
    /// 与 PlayErrorFeedback 的区别：后者是首次犯错时的动效；本方法仅还原持久视觉状态（红色叉号）。</summary>
    public void RestoreErrorLocked()
    {
        isErrorLocked = true;
        hasCross = true;
        if (crossImage != null) crossImage.color = Tuning.ErrorColor;
        ApplyVisualState();
    }

    /// <summary>双击点错的完整视觉反馈：红框出现 + 横向抖动，抖动结束后红框消失、叉号染红（保留至本关结束）。
    /// 红框仅抖动期间可见，错误格的持久标识为红色叉号。锁定状态由 LockAsError 置位。</summary>
    public void PlayErrorFeedback()
    {
        if (errorOutline != null) errorOutline.enabled = true; // 红框出现
        PlayErrorShake();                                      // 横向正弦衰减抖动
        if (errorFeedbackRoutine != null) StopCoroutine(errorFeedbackRoutine);
        errorFeedbackRoutine = StartCoroutine(HideErrorFrameAfter(ErrorFeedbackDuration));
    }

    /// <summary>抖动结束后：红框消失 + 叉号染红（红色叉号作为错误格的持久标识，保留至 Setup 复位）。</summary>
    private IEnumerator HideErrorFrameAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        errorFeedbackRoutine = null;
        if (errorOutline != null) errorOutline.enabled = false; // 红框消失
        if (crossImage != null) crossImage.color = Tuning.ErrorColor; // 叉号染红（与红框同色）
    }

    // ====== 指针事件：全部转发给 GameManager 统一协调单击/双击/滑动 ======
    public void OnPointerDown(PointerEventData eventData)
    {
        if (gameManager != null) gameManager.HandlePointerDown(this);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (gameManager != null) gameManager.HandlePointerUp(this);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (gameManager != null) gameManager.HandlePointerEnter(this);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (gameManager != null) gameManager.HandlePointerClick(this);
    }
}
