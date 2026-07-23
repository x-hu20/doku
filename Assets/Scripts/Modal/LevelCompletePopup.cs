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
/// 4. 默认 SetActive(false)，由 Show/Hide 控制显隐——与 ModalPopup 同样不在 Awake 里 SetActive(false)，
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

    [Header("宝箱奖励遮罩（满10关宝箱就绪后点击宝箱弹出，点击任意位置退出）")]
    [Tooltip("全屏半透明遮罩，点击任意位置退出回结算页。需铺满 CompletePopup 且 raycastTarget=true（代码强制开启）")]
    [SerializeField] private Image rewardOverlay;
    [Tooltip("奖励魔法棒图标 Image（Sprite 预制体设 magic_image）。数量 0 时隐藏")]
    [SerializeField] private Image rewardMagicIcon;
    [Tooltip("奖励魔法棒数量文字（如 ×1）")]
    [SerializeField] private TMP_Text rewardMagicText;
    [Tooltip("奖励提示道具图标 Image（Sprite 设 tip_image）")]
    [SerializeField] private Image rewardTipIcon;
    [Tooltip("奖励提示道具数量文字")]
    [SerializeField] private TMP_Text rewardTipText;

    [Header("宝箱物品说明（仅宝箱关闭状态可预览宝箱内道具）")]
    [Tooltip("问号按钮（Image+Button，摆放在宝箱上方）。仅在宝箱关闭状态显示；点击弹出物品说明小弹窗；宝箱开启状态自动隐藏")]
    [SerializeField] private Button questionButton;
    [Tooltip("物品说明小弹窗面板（默认隐藏，点击问号后在其上方弹出，展示宝箱内魔法棒/提示道具数量）")]
    [SerializeField] private GameObject infoPopup;
    [Tooltip("说明弹窗内魔法棒图标 Image（Sprite 复用 rewardMagicIcon 同款 magic_image）")]
    [SerializeField] private Image infoMagicIcon;
    [Tooltip("说明弹窗内魔法棒数量文字（如 ×1）")]
    [SerializeField] private TMP_Text infoMagicText;
    [Tooltip("说明弹窗内提示道具图标 Image（Sprite 复用 rewardTipIcon 同款 tip_image）")]
    [SerializeField] private Image infoTipIcon;
    [Tooltip("说明弹窗内提示道具数量文字")]
    [SerializeField] private TMP_Text infoTipText;
    private bool _chestReady;          // 宝箱是否就绪等待点击（满10关开箱后置 true，点击后/归0置 false）
    private int _pendingRewardMagic;  // 待发放的魔法棒奖励（点击宝箱时读）
    private int _pendingRewardTip;    // 待发放的提示道具奖励
    // 宝箱内道具配置（Show 时记录，物品说明弹窗展示用；不受开箱消费/HideReward 归0影响）
    private int _chestRewardMagic;
    private int _chestRewardTip;
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
        // 问号按钮：点击切换物品说明小弹窗（仅宝箱关闭状态可用）
        if (questionButton != null)
        {
            questionButton.onClick.RemoveAllListeners();
            questionButton.onClick.AddListener(ToggleChestInfo);
        }
    }

    /// <summary>奖励遮罩激活时，点击弹窗任意位置（遮罩 Image 拦截 raycast）退出回结算页。
    /// 宝箱就绪（满10关开箱循环抖动）时，点击宝箱区域弹出奖励遮罩并发放道具。</summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        // 物品说明弹窗打开时，点弹窗外部（遮罩/面板背景/宝箱区域）任意位置即关闭
        if (infoPopup != null && infoPopup.activeSelf)
        {
            infoPopup.SetActive(false);
            return;
        }
        if (rewardOverlay != null && rewardOverlay.gameObject.activeSelf)
        {
            HideReward();
            return;
        }
        // 宝箱就绪：点击宝箱区域才弹奖励遮罩（需玩家点击宝箱，非自动）
        if (_chestReady && chestBox != null &&
            RectTransformUtility.RectangleContainsScreenPoint(chestBox.rectTransform, eventData.position, eventData.pressEventCamera))
        {
            _chestReady = false; // 防重复点击
            FeedbackManager.Instance?.Tap();
            ShowReward(_pendingRewardMagic, _pendingRewardTip);
        }
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
    /// <param name="nextInteractable">按钮是否可点击：最后一关置 false（待后续「全通关」需求再定）</param>
    /// <param name="onNext">下一关按钮点击回调（通常 GameManager.LoadNextLevel）</param>
    /// <param name="progress">当前轮已通关数（自上次开箱后），满10关传10。归0在遮罩退出回调后由 GameManager 执行</param>
    /// <param name="chestReady">本关是否触发开箱（满10关）：是则进度条全亮+宝箱抖动切 openbox+弹遮罩</param>
    public void Show(string title, string nextLabel, bool nextInteractable, Action onNext, int progress, bool chestReady, int rewardMagic, int rewardTip, Action onRewardShown = null, Action onRewardClosed = null)
    {
        if (titleText != null) titleText.text = title ?? "";
        if (nextButtonText != null) nextButtonText.text = nextLabel ?? "";

        if (nextButton != null)
        {
            nextButton.onClick.RemoveAllListeners();
            nextButton.onClick.AddListener(() =>
            {
                // 奖励遮罩激活时，点击穿透到本按钮：转而关闭遮罩，不切关
                if (rewardOverlay != null && rewardOverlay.gameObject.activeSelf)
                {
                    HideReward();
                    return;
                }
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
            TweenRunner.PulseLoop(nextButton.transform, peak: Tuning.ButtonPulsePeak, halfMs: Tuning.ButtonPulseHalfMs);

        _onRewardClosed = onRewardClosed; // 奖励遮罩退出时回调（GameManager 归0进度）
        _onRewardShown = onRewardShown;   // 奖励遮罩弹出时回调（GameManager 发放道具）
        // 宝箱内道具配置（物品说明弹窗展示用；HideReward 归0后仍保留配置值供预览）
        _chestRewardMagic = rewardMagic;
        _chestRewardTip = rewardTip;
        UpdateChest(progress, chestReady, rewardMagic, rewardTip);
    }

    /// <summary>更新阶段宝箱：进度条染色 progress 格（chestReady 时全亮），宝箱图与抖动。
    /// chestReady 时宝箱切 openbox 并循环抖动吸引点击，等待玩家点击宝箱再弹奖励遮罩（需点击，非自动）。</summary>
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
                // 宝箱循环抖动吸引点击，等待玩家点击宝箱再弹奖励遮罩（需点击，非自动）
                TweenRunner.ShakeLoop(chestBox.transform, Tuning.ErrorShakeAmplitude, 300f);
                _chestReady = true;
                _pendingRewardMagic = rewardMagic;
                _pendingRewardTip = rewardTip;
            }
            else
            {
                TweenRunner.Stop(chestBox.transform); // 停循环抖动（奖励已领/归0重绘）
                _chestReady = false;
            }
        }
        // 问号预览：仅宝箱关闭状态显示问号；开启状态隐藏问号与说明弹窗（开启后改点宝箱本身看奖励）
        bool showQuestion = !chestReady;
        if (questionButton != null) questionButton.gameObject.SetActive(showQuestion);
        if (!showQuestion && infoPopup != null) infoPopup.SetActive(false);
    }

    /// <summary>弹出奖励遮罩：显示魔法棒/提示图标与数量（数量 0 隐藏该项），遮罩点击任意位置退出。
    /// 弹出时触发 onRewardShown 回调（GameManager 据此发放道具——宝箱被点击打开后才+1）。
    /// 遮罩移到 CompletePopup 根下最末，确保事件层级最高、铺满全屏拦截所有点击（含 NextButton 区域）。</summary>
    private void ShowReward(int rewardMagic, int rewardTip)
    {
        if (rewardOverlay == null) return;
        if (rewardMagicIcon != null) rewardMagicIcon.gameObject.SetActive(rewardMagic > 0);
        if (rewardMagicText != null && rewardMagic > 0) rewardMagicText.text = "×" + rewardMagic;
        if (rewardTipIcon != null) rewardTipIcon.gameObject.SetActive(rewardTip > 0);
        if (rewardTipText != null && rewardTip > 0) rewardTipText.text = "×" + rewardTip;
        // 移到最末子节点（最后渲染 = 事件层级最高），确保遮罩盖住 NextButton 等所有下层 UI
        rewardOverlay.transform.SetAsLastSibling();
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

    /// <summary>问号按钮点击：切换宝箱物品说明小弹窗（仅宝箱关闭状态可用，开启状态问号已隐藏按钮不可点）。</summary>
    private void ToggleChestInfo()
    {
        if (infoPopup == null || _chestReady) return; // 宝箱开启状态不展示说明
        bool show = !infoPopup.activeSelf;
        if (show) FillChestInfo();
        infoPopup.SetActive(show);
        FeedbackManager.Instance?.Tap();
    }

    /// <summary>填充物品说明弹窗：展示宝箱内魔法棒/提示道具数量（数量 0 隐藏该项）。
    /// 奖励取 Show 时记录的宝箱配置值，不受开箱消费/HideReward 归0影响。</summary>
    private void FillChestInfo()
    {
        if (infoMagicIcon != null) infoMagicIcon.gameObject.SetActive(_chestRewardMagic > 0);
        if (infoMagicText != null && _chestRewardMagic > 0) infoMagicText.text = "×" + _chestRewardMagic;
        if (infoTipIcon != null) infoTipIcon.gameObject.SetActive(_chestRewardTip > 0);
        if (infoTipText != null && _chestRewardTip > 0) infoTipText.text = "×" + _chestRewardTip;
    }

    /// <summary>关闭弹窗：播放出场动画 → SetActive(false)。已隐藏时直接返回，避免对 inactive 弹窗调 AnimateHide。</summary>
    public void Hide()
    {
        if (!gameObject.activeSelf) return; // 场景默认 inactive / 已关，无需再关
        StopPulse(); // 兜底停脉冲并复位 scale，避免下次 Show 时按钮停在非1缩放
        _chestReady = false;
        if (chestBox != null) TweenRunner.Stop(chestBox.transform); // 停宝箱循环抖动
        if (rewardOverlay != null) rewardOverlay.gameObject.SetActive(false); // 收起奖励遮罩
        if (infoPopup != null) infoPopup.SetActive(false); // 收起物品说明弹窗
        if (questionButton != null) questionButton.gameObject.SetActive(false); // 隐藏问号
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
