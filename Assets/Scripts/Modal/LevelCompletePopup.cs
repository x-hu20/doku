using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// 通关结算弹窗：遮罩 + 居中面板 + 标题 + 下一关按钮。结构与 <see cref="ModalPopup"/> 平行，
/// 但承载结算专属元素（下一关按钮直接为面板内按钮槽，而非 ModalPopup 的通用 buttonSlots 数组）。
///
/// 设计要点：
/// 1. 复用 ModalPopup 的「预制体静态摆放 + Inspector 拖拽」风格：节点结构在场景预制体中摆好，
///    运行时仅填充 .text 与按钮回调，不 Instantiate。
/// 2. 入场/出场动画经 IModalAnimator 注入，本期 InstantModalAnimator 瞬时显示；
///    后续换 TweenModalAnimator 加弹性亮相，主流程零改动（与 ModalPopup 一致）。
/// 3. 遮罩 Image 拦截下层点击，独立子 Canvas 带 GraphicRaycaster。
/// 4. 预留 victoryAnimRoot / chestSlot 两个空槽位（本期不使用）：后续在此挂胜利动画与关卡阶段宝箱，
///    Show 时直接操作这些根节点即可，无需再改弹窗骨架。
/// 5. 默认 SetActive(false)，由 Show/Hide 控制显隐——与 ModalPopup 同样不在 Awake 里 SetActive(false)，
///    避免首次 Show 触发 Awake 时立刻自关导致弹窗永显不出。
/// </summary>
public class LevelCompletePopup : MonoBehaviour, IPointerClickHandler
{
    [Header("弹窗节点引用（Inspector 拖拽）")]
    [Tooltip("全屏半透明遮罩，raycastTarget=true 拦截下层点击")]
    [SerializeField] private Image dimOverlay;
    [Tooltip("居中面板容器，动画操作目标")]
    [SerializeField] private Transform panel;
    [Tooltip("标题文本节点（如 “Level 3 Completed!”）")]
    [SerializeField] private TMP_Text titleText;
    [Tooltip("下一关按钮")]
    [SerializeField] private Button nextButton;
    [Tooltip("NextButton 上的文字节点（留空则在 Awake 时从按钮自动获取）")]
    [SerializeField] private TMP_Text nextButtonText;

    [Header("预留扩展槽（本期不使用，后续接胜利动画 / 关卡阶段宝箱）")]
    [Tooltip("胜利动画根节点：后续在此挂集体庆祝特效/动画，本期空占位")]
    [SerializeField] private Transform victoryAnimRoot;
    [Tooltip("关卡阶段宝箱槽：后续在此生成/开启宝箱，本期空占位")]
    [SerializeField] private Transform chestSlot;

    [Header("阶段宝箱（每10关一轮）")]
    [Tooltip("进度条格子 Image[]，按从左到右顺序，每通关1关染色1格。建议10格")]
    [SerializeField] private Image[] chestCells;
    [Tooltip("宝箱 Image：未满显示 closebox，满10关抖动后切换为 openbox")]
    [SerializeField] private Image chestBox;
    [Tooltip("未满时宝箱图（closebox_image）")]
    [SerializeField] private Sprite closeChestSprite;
    [Tooltip("满10关开箱后宝箱图（openbox_image）")]
    [SerializeField] private Sprite openChestSprite;
    [Tooltip("进度条已染色格颜色")]
    [SerializeField] private Color chestFilledColor = new Color(1f, 0.84f, 0.27f, 1f);
    [Tooltip("进度条未染色格颜色")]
    [SerializeField] private Color chestEmptyColor = new Color(0.3f, 0.3f, 0.3f, 0.4f);

    [Header("宝箱奖励遮罩（满10关自动弹出，点击任意位置退出）")]
    [Tooltip("全屏半透明遮罩，点击任意位置退出回结算页。需挂 Button（或其子节点）接收点击")]
    [SerializeField] private Image rewardOverlay;
    [Tooltip("遮罩上的 Button（点击=退出遮罩），可与 rewardOverlay 同节点")]
    [SerializeField] private Button rewardOverlayButton;
    [Tooltip("奖励魔法棒图标 Image（Sprite 预制体设 magic_image）。数量 0 时隐藏")]
    [SerializeField] private Image rewardMagicIcon;
    [Tooltip("奖励魔法棒数量文字（如 ×1）")]
    [SerializeField] private TMP_Text rewardMagicText;
    [Tooltip("奖励提示道具图标 Image（Sprite 设 tip_image）")]
    [SerializeField] private Image rewardTipIcon;
    [Tooltip("奖励提示道具数量文字")]
    [SerializeField] private TMP_Text rewardTipText;
    private Coroutine chestRewardRoutine;
    private Action _onRewardClosed; // 奖励遮罩退出回调（GameManager 据此归0宝箱进度）
    private Action _onRewardShown;  // 奖励遮罩弹出回调（GameManager 据此发放道具）

    /// <summary>当前动画器。字段初始化器赋默认实现——即便弹窗默认 SetActive(false)（Awake 未运行）也能安全调 Hide()。</summary>
    private IModalAnimator _animator = new InstantModalAnimator();

    private void Awake()
    {
        // 标题/按钮子文字关闭 raycastTarget（按钮自身 Graphic 保留以接收点击），遵循现有 NextButtonText 模式
        if (titleText != null) titleText.raycastTarget = false;
        EnsureNextButtonText();
        if (nextButtonText != null) nextButtonText.raycastTarget = false;
        // 奖励遮罩自身 Image 必须接收点击（退出用）；运行时确保 raycastTarget=true
        if (rewardOverlay != null) rewardOverlay.raycastTarget = true;
        // 兼容：若拖了 Button 也绑定（双保险，IPointerClickHandler 已覆盖主路径）
        if (rewardOverlayButton != null)
        {
            rewardOverlayButton.onClick.RemoveAllListeners();
            rewardOverlayButton.onClick.AddListener(HideReward);
        }
    }

    /// <summary>奖励遮罩激活时，点击弹窗任意位置（遮罩 Image 拦截 raycast）退出回结算页。</summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        if (rewardOverlay != null && rewardOverlay.gameObject.activeSelf)
            HideReward();
    }

    /// <summary>nextButtonText 未在 Inspector 绑定时，从 nextButton 子节点兜底获取。</summary>
    private void EnsureNextButtonText()
    {
        if (nextButtonText == null && nextButton != null)
            nextButtonText = nextButton.GetComponentInChildren<TMP_Text>();
    }

    /// <summary>替换动画器（后续接入补间动画时调用）。</summary>
    public void SetAnimator(IModalAnimator animator)
    {
        if (animator != null) _animator = animator;
    }

    /// <summary>
    /// 显示结算弹窗：填标题与按钮文案 → 绑定下一关回调 → SetActive(true) → 播入场动画 → 更新阶段宝箱。
    /// </summary>
    /// <param name="title">标题文案（如 “Level 3 Completed!” / “All Levels Completed!”）</param>
    /// <param name="nextLabel">下一关按钮文案（如 “Level 4” / “All Complete!”）</param>
    /// <param name="nextInteractable">按钮是否可点击：最后一关沿用原语义置 false（待后续「全通关」需求再定）</param>
    /// <param name="onNext">下一关按钮点击回调（通常 GameManager.LoadNextLevel）</param>
    /// <param name="progress">当前轮已通关数（自上次开箱后），0~9，满10在 GameManager 已归0并置 chestReady</param>
    /// <param name="chestReady">本关是否触发开箱（满10关）：是则进度条全亮+宝箱抖动切 openbox</param>
    public void Show(string title, string nextLabel, bool nextInteractable, Action onNext, int progress, bool chestReady, int rewardMagic, int rewardTip, Action onRewardShown = null, Action onRewardClosed = null)
    {
        if (titleText != null) titleText.text = title ?? "";
        if (nextButtonText != null) nextButtonText.text = nextLabel ?? "";

        if (nextButton != null)
        {
            nextButton.onClick.RemoveAllListeners();
            nextButton.onClick.AddListener(() =>
            {
                FeedbackManager.Instance?.Tap(); // 按钮点击统一触觉反馈（PRD §6）
                StopPulse(); // 点击即停引导脉冲，避免切关前残留
                onNext?.Invoke();
            });
            nextButton.interactable = nextInteractable;
        }

        gameObject.SetActive(true);
        _animator.AnimateShow(panel, null);

        // 下一关按钮持续脉冲引导点击；末关按钮不可点则不引导（无意义）
        if (nextButton != null && nextInteractable)
            TweenRunner.PulseLoop(nextButton.transform, peak: 1.10f, halfMs: 600f);

        _onRewardClosed = onRewardClosed; // 奖励遮罩退出时回调（GameManager 归0进度）
        _onRewardShown = onRewardShown;   // 奖励遮罩弹出时回调（GameManager 发放道具）
        UpdateChest(progress, chestReady, rewardMagic, rewardTip);
    }

    /// <summary>更新阶段宝箱：进度条染色 progress 格（chestReady 时全亮），宝箱图与抖动。
    /// chestReady 时宝箱抖动一次后自动弹出奖励遮罩（不需玩家点击宝箱）。</summary>
    private void UpdateChest(int progress, bool chestReady, int rewardMagic, int rewardTip)
    {
        if (chestCells != null)
        {
            for (int i = 0; i < chestCells.Length; i++)
            {
                if (chestCells[i] == null) continue;
                bool filled = chestReady || i < progress; // 开箱时全亮
                chestCells[i].color = filled ? chestFilledColor : chestEmptyColor;
            }
        }
        if (chestBox != null)
        {
            chestBox.sprite = chestReady ? openChestSprite : closeChestSprite;
            if (chestReady)
            {
                TweenRunner.ShakeHorizontal(chestBox.transform, 14f, 0.45f);
                if (chestRewardRoutine != null) StopCoroutine(chestRewardRoutine);
                chestRewardRoutine = StartCoroutine(ShowRewardAfter(0.55f, rewardMagic, rewardTip));
            }
        }
    }

    private System.Collections.IEnumerator ShowRewardAfter(float delay, int rewardMagic, int rewardTip)
    {
        yield return new WaitForSeconds(delay);
        chestRewardRoutine = null;
        ShowReward(rewardMagic, rewardTip);
    }

    /// <summary>弹出奖励遮罩：显示魔法棒/提示图标与数量（数量 0 隐藏该项），遮罩点击任意位置退出。
    /// 弹出时触发 onRewardShown 回调（GameManager 据此发放道具——宝箱打开后才+1）。</summary>
    private void ShowReward(int rewardMagic, int rewardTip)
    {
        if (rewardOverlay == null) return;
        if (rewardMagicIcon != null) rewardMagicIcon.gameObject.SetActive(rewardMagic > 0);
        if (rewardMagicText != null && rewardMagic > 0) rewardMagicText.text = "×" + rewardMagic;
        if (rewardTipIcon != null) rewardTipIcon.gameObject.SetActive(rewardTip > 0);
        if (rewardTipText != null && rewardTip > 0) rewardTipText.text = "×" + rewardTip;
        rewardOverlay.gameObject.SetActive(true);
        _onRewardShown?.Invoke(); // 宝箱打开，发放奖励
        _onRewardShown = null;
    }

    /// <summary>关闭奖励遮罩，返回结算页；通知 GameManager 归0宝箱进度（下次结算页起从0格），
    /// 并立即重绘进度条为归0状态（宝箱回 closebox、进度格全暗）。</summary>
    private void HideReward()
    {
        if (rewardOverlay != null) rewardOverlay.gameObject.SetActive(false);
        _onRewardClosed?.Invoke();
        _onRewardClosed = null;
        UpdateChest(progress: 0, chestReady: false, rewardMagic: 0, rewardTip: 0); // 归0后重绘
    }

    /// <summary>关闭弹窗：播放出场动画 → SetActive(false)。已隐藏时直接返回，避免对 inactive 弹窗调 AnimateHide。</summary>
    public void Hide()
    {
        if (!gameObject.activeSelf) return; // 场景默认 inactive / 已关，无需再关
        StopPulse(); // 兜底停脉冲并复位 scale，避免下次 Show 时按钮停在非1缩放
        if (chestRewardRoutine != null) { StopCoroutine(chestRewardRoutine); chestRewardRoutine = null; }
        if (chestBox != null) TweenRunner.Stop(chestBox.transform); // 停宝箱抖动
        if (rewardOverlay != null) rewardOverlay.gameObject.SetActive(false); // 收起奖励遮罩
        _animator.AnimateHide(panel, () => gameObject.SetActive(false));
    }

    /// <summary>停止下一关按钮的引导脉冲并复位 scale=1。点击与 Hide 时调用。</summary>
    private void StopPulse()
    {
        if (nextButton == null) return;
        TweenRunner.Stop(nextButton.transform);
        nextButton.transform.localScale = Vector3.one;
    }
}
