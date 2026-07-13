using System;
using UnityEngine;

/// <summary>
/// 弹窗动画默认实现：瞬时显示/隐藏，无补间。本期失败弹窗用它跑通流程；
/// 后续加动画时新建 TweenModalAnimator（基于 TweenRunner）注入 ModalPopup 即可。
/// </summary>
public class InstantModalAnimator : IModalAnimator
{
    public void AnimateShow(Transform panel, Action onComplete)
    {
        if (panel != null) panel.localScale = Vector3.one;
        onComplete?.Invoke();
    }

    public void AnimateHide(Transform panel, Action onComplete)
    {
        onComplete?.Invoke();
    }
}
