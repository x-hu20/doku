using System;
using UnityEngine;

/// <summary>
/// 弹窗入场/出场动画抽象。本期默认实现 <see cref="InstantModalAnimator"/> 为瞬时显示；
/// 后续动画迭代只新增实现（基于 TweenRunner 协程补间或 DOTween）注入 ModalPopup，
/// ModalPopup 主流程零改动。与现有反馈系统"显式触发动画、显隐与动画解耦"原则一致。
/// </summary>
public interface IModalAnimator
{
    /// <summary>入场动画：操作面板 Transform 播放补间，完成后调 onComplete（调用方据此启用按钮交互等）。</summary>
    void AnimateShow(Transform panel, Action onComplete);

    /// <summary>出场动画：操作面板 Transform 播放补间，完成后调 onComplete（调用方据此 SetActive(false)）。</summary>
    void AnimateHide(Transform panel, Action onComplete);
}
