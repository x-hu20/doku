using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 欢迎页控制器：启动时全屏覆盖，展示随机 slogan（打字机逐字）+ 游客玩家ID + 加载进度条，
/// 三路条件（打字机完成 / 初始化就绪 / 最小展示时长）均满足后回调进入关卡。
///
/// 设计：
/// - slogan 从 i18n 表 slogan.1~slogan.9 随机取一条，逐字打出（打字机效果）。
/// - 玩家ID 取 SaveSystem.Data.playerId（GUID）前6位大写，显示 "Guest XXXXXX"。
/// - 进度条 fillAmount 在 minDuration 内 EaseOut 0→1；真实加载当前瞬间完成，实质等 minDuration，
///   MarkReady 为未来 SDK 异步初始化预留（ready 标志）。
/// - welcome 子节点直接归属父 Canvas（Canvas_Dynamic_HUD）渲染：WelcomeUI 节点不得挂自有 Canvas
///   组件，否则子节点不归父 Canvas 渲染、整树不显示。sortingOrder 由 Show 临时提升父 Canvas 到 2000
///   盖住棋盘/教程/手指，Hide 渐出末恢复。
/// - GameManager.Start 调 Show(onComplete)，完成后回调 GameManager 进起始关卡。
/// </summary>
public class WelcomeController : MonoBehaviour
{
    [Header("欢迎 UI 引用（Inspector 拖拽）")]
    [Tooltip("Slogan 文本（TMP_Text，逐字打出）")]
    [SerializeField] private TMP_Text sloganText;
    [Tooltip("玩家ID 文本（TMP_Text，显示 \"Guest XXXXXX\"）")]
    [SerializeField] private TMP_Text playerIdText;
    [Tooltip("加载进度条 Image（fillMethod=Horizontal, fillAmount 由本控制器驱动 0→1）")]
    [SerializeField] private Image progressBar;

    [Header("节奏")]
    [Tooltip("最小展示时长（秒）——真实加载与此时长取较大值")]
    [SerializeField] private float minDuration = 4f;
    [Tooltip("打字机每字间隔（秒）")]
    [SerializeField] private float typeInterval = 0.04f;
    [Tooltip("welcome 渐出时长（秒），Hide 时驱动 CanvasGroup alpha 1→0")]
    [SerializeField] private float fadeDur = 0.3f;

    private bool typingDone;
    private bool ready;
    private bool running;
    private System.Action pendingComplete;
    private CanvasGroup canvasGroup;        // 本节点上的 CanvasGroup（Show 时缓存），渐出用
    private Canvas parentCanvas;            // 父 Canvas（Canvas_Dynamic_HUD），welcome 子节点直接归属它渲染
    private int originalParentSortingOrder; // 父 Canvas 原 sortingOrder，welcome 结束后恢复
    private bool parentSortingBoosted;      // 父 Canvas sortingOrder 是否已被提升
    private Image bgImage;                   // 全屏深色背景（代码创建）：盖住游戏内容做欢迎页底色，并拦截点击穿透

    /// <summary>标记真实初始化已完成（GameManager 同步初始化末尾调；预留 SDK 异步加载）。</summary>
    public void MarkReady() => ready = true;

    /// <summary>缓存父 Canvas 引用，供 Show 提升 sortingOrder / Hide 恢复。</summary>
    private void CacheParentCanvas()
    {
        if (parentCanvas == null && transform.parent != null)
            parentCanvas = transform.parent.GetComponent<Canvas>();
    }

    /// <summary>惰性创建全屏深色背景 Image（welcome 首子节点，渲染在同层最底层）。</summary>
    private void EnsureBackground()
    {
        if (bgImage != null) return;
        var go = new GameObject("WelcomeBg", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = Vector2.zero;
        rt.SetAsFirstSibling();
        bgImage = go.GetComponent<Image>();
        bgImage.color = new Color(0.08f, 0.08f, 0.12f, 1f);
        bgImage.raycastTarget = true; // 拦截点击，避免穿透到下方棋盘/教程
    }

    /// <summary>显示欢迎页：随机 slogan 打字机 + 玩家ID + 进度条，三路条件满足后回调 onComplete。</summary>
    public void Show(System.Action onComplete)
    {
        CacheParentCanvas();
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>(); // 本节点必有 CanvasGroup
        pendingComplete = onComplete;
        typingDone = false;
        running = true;
        // ready 不在此重置：由 GameManager.MarkReady() 管理（同步初始化完成即置 true；未来 SDK 异步加载时延后置位）

        // 玩家ID：GUID 前6位大写 + 本地化 "Guest" 前缀
        if (playerIdText != null)
        {
            string pid = SaveSystem.Data != null ? SaveSystem.Data.playerId : "";
            string shortId = pid.Length >= 6 ? pid.Substring(0, 6).ToUpper() : pid.ToUpper();
            playerIdText.text = I18n.Get("welcome.guest_prefix") + " " + shortId;
        }

        // 随机 slogan.1~slogan.9
        string slogan = I18n.Get("slogan." + Random.Range(1, 10));
        if (sloganText != null) sloganText.text = "";
        if (progressBar != null) progressBar.fillAmount = 0f;
        EnsureBackground();
        gameObject.SetActive(true);
        transform.SetAsLastSibling(); // 渲染在父 Canvas 子节点最上层
        // 提升父 Canvas sortingOrder 到 2000，盖住棋盘/教程/手指等一切 UI；Hide 渐出末恢复
        if (parentCanvas != null && !parentSortingBoosted)
        {
            originalParentSortingOrder = parentCanvas.sortingOrder;
            parentCanvas.sortingOrder = 2000;
            parentSortingBoosted = true;
        }
        if (canvasGroup != null) canvasGroup.alpha = 1f; // 直接可见，无淡入

        StartCoroutine(TypeWriter(slogan));
        StartCoroutine(ProgressBarLoop());
        StartCoroutine(WaitAndComplete());
    }

    /// <summary>隐藏欢迎页：渐出 CanvasGroup（渐出期间保持 sortingOrder 2000 盖住下方关卡生成，避免白屏），
    /// 渐出完恢复父 sortingOrder 并失活。无 CanvasGroup 则直接失活。</summary>
    public void Hide()
    {
        running = false;
        if (canvasGroup != null) StartCoroutine(FadeOutThenDisable());
        else FinishHide();
    }

    /// <summary>恢复父 Canvas sortingOrder 并失活 welcome（渐出末或无渐出时调）。</summary>
    private void FinishHide()
    {
        if (parentSortingBoosted && parentCanvas != null)
        {
            parentCanvas.sortingOrder = originalParentSortingOrder;
            parentSortingBoosted = false;
        }
        gameObject.SetActive(false);
    }

    private IEnumerator FadeOutThenDisable()
    {
        float e = 0f;
        float from = canvasGroup.alpha;
        while (e < fadeDur)
        {
            e += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(from, 0f, Mathf.Clamp01(e / fadeDur));
            yield return null;
        }
        canvasGroup.alpha = 0f;
        FinishHide();
    }

    // 打字机：逐字显示 slogan，完成置 typingDone
    private IEnumerator TypeWriter(string full)
    {
        if (sloganText == null || string.IsNullOrEmpty(full)) { typingDone = true; yield break; }
        for (int i = 0; i < full.Length; i++)
        {
            sloganText.text = full.Substring(0, i + 1);
            yield return new WaitForSeconds(typeInterval);
        }
        typingDone = true;
    }

    // 进度条：fillAmount 在 minDuration 内 EaseOut 0→1
    private IEnumerator ProgressBarLoop()
    {
        if (progressBar == null) yield break;
        float e = 0f;
        while (e < minDuration)
        {
            e += Time.deltaTime;
            float k = Mathf.Clamp01(e / minDuration);
            progressBar.fillAmount = 1f - (1f - k) * (1f - k); // EaseOutQuad
            yield return null;
        }
        progressBar.fillAmount = 1f;
    }

    // 三路条件全满足后回调：打字机完成 && 初始化就绪 && 最小时长到
    private IEnumerator WaitAndComplete()
    {
        float elapsed = 0f;
        while (running && !(typingDone && ready && elapsed >= minDuration))
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        pendingComplete?.Invoke();
        pendingComplete = null;
    }
}
