using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 通用弹窗载体（非"失败弹窗专用"）：遮罩 + 居中面板 + 标题 + 按钮槽。
/// 后续暂停/设置/通关结算等弹窗复用本结构，不另建第二套弹窗。
///
/// 设计要点：
/// 1. 预制体摆放按钮槽节点，运行时按 ModalConfig 激活对应数量并设文案/回调——
///    遵循现有 TopUI"预制体静态摆放 + Inspector 拖拽绑定"风格，不运行时 Instantiate 按钮。
///    扩展更多按钮时在预制体加槽位 + 数组扩容即可。
/// 2. 入场/出场动画经 IModalAnimator 注入，本期 InstantModalAnimator 瞬时显示；
///    后续换 TweenModalAnimator 加动画，主流程零改动（PRD F20）。
/// 3. 遮罩 Image 拦截下层点击（Modal 语义），独立子 Canvas 带 GraphicRaycaster。
/// 4. 默认 SetActive(false)，由 Show/Hide 控制显隐。
/// </summary>
public class ModalPopup : MonoBehaviour
{
    [Header("弹窗节点引用（Inspector 拖拽）")]
    [Tooltip("全屏半透明遮罩，raycastTarget=true 拦截下层点击")]
    [SerializeField] private Image dimOverlay;
    [Tooltip("居中面板容器，动画操作目标")]
    [SerializeField] private Transform panel;
    [Tooltip("标题文本节点")]
    [SerializeField] private TMP_Text titleText;
    [Tooltip("按钮槽：每个槽含 Button + 其子 TMP_Text。按 ModalConfig.buttons 顺序激活，多余的隐藏")]
    [SerializeField] private Button[] buttonSlots;

    /// <summary>当前动画器。字段初始化器赋默认实现——即便弹窗默认 SetActive=false（Awake 未运行）也能安全调 Hide()。</summary>
    private IModalAnimator _animator = new InstantModalAnimator();

    private void Awake()
    {
        // 注意：此处不再 SetActive(false)。弹窗默认隐藏由场景节点 inactive 保证；
        // 若在 Awake 里 SetActive(false)，Show() 首次 SetActive(true) 触发 Awake 时会立刻把自己关掉，弹窗永远显示不出来。
        // 静态文本/遮罩不影响：遮罩需保留 raycastTarget 拦截点击；标题文本关闭 raycastTarget
        if (titleText != null) titleText.raycastTarget = false;
        // 按钮子文字关闭 raycastTarget（按钮自身 Graphic 保留以接收点击），遵循现有 NextButtonText 模式
        if (buttonSlots != null)
        {
            foreach (var btn in buttonSlots)
            {
                if (btn == null) continue;
                var lbl = btn.GetComponentInChildren<TMP_Text>();
                if (lbl != null) lbl.raycastTarget = false;
            }
        }
    }

    /// <summary>替换动画器（后续接入补间动画时调用）。</summary>
    public void SetAnimator(IModalAnimator animator)
    {
        if (animator != null) _animator = animator;
    }

    /// <summary>显示弹窗：填入配置 → 激活按钮槽 → SetActive(true) → 播入场动画。</summary>
    public void Show(ModalConfig config)
    {
        if (config == null) return;

        if (titleText != null) titleText.text = config.title ?? "";

        // 按配置激活对应数量按钮槽，多余的隐藏；清空旧监听后绑定新回调
        if (buttonSlots != null)
        {
            int count = config.buttons != null ? config.buttons.Count : 0;
            for (int i = 0; i < buttonSlots.Length; i++)
            {
                if (buttonSlots[i] == null) continue;
                buttonSlots[i].onClick.RemoveAllListeners();
                if (i < count)
                {
                    var def = config.buttons[i];
                    var lbl = buttonSlots[i].GetComponentInChildren<TMP_Text>();
                    if (lbl != null) lbl.text = def.label ?? "";
                    // 捕获到局部变量避免闭包捕获循环变量 i 的经典陷阱
                    var action = def.onClick;
                    buttonSlots[i].onClick.AddListener(() =>
                    {
                        FeedbackManager.Instance?.Tap(); // 按钮点击统一触觉反馈（PRD §6）
                        action?.Invoke();
                    });
                    buttonSlots[i].gameObject.SetActive(true);
                }
                else
                {
                    buttonSlots[i].gameObject.SetActive(false);
                }
            }
        }

        gameObject.SetActive(true);
        _animator.AnimateShow(panel, null);
    }

    /// <summary>关闭弹窗：播放出场动画 → SetActive(false)。已隐藏时直接返回，避免对 inactive 弹窗调 AnimateHide。</summary>
    public void Hide()
    {
        if (!gameObject.activeSelf) return; // 场景默认 inactive / 已关，无需再关
        _animator.AnimateHide(panel, () => gameObject.SetActive(false));
    }
}

/// <summary>弹窗配置：标题 + 按钮定义列表。由调用方（GameManager）构造后传给 ModalPopup.Show。</summary>
public class ModalConfig
{
    public string title;
    public List<ModalButtonDef> buttons;

    public ModalConfig(string title, List<ModalButtonDef> buttons)
    {
        this.title = title;
        this.buttons = buttons;
    }
}

/// <summary>单个按钮定义：文案 + 点击回调。回调以 Action 注入，可由调用方组合多步逻辑（如广告→补血→Hide）。</summary>
public class ModalButtonDef
{
    public string label;
    public Action onClick;

    public ModalButtonDef(string label, Action onClick)
    {
        this.label = label;
        this.onClick = onClick;
    }
}
