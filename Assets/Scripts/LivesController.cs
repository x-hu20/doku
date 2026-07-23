using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 关卡生命值模块：每关初始 3 条命，双击错误格扣 1 点，归零触发关卡失败。
///
/// 设计要点：
/// 1. 状态独立——血量与本关锁定的小猫数（GameManager.currentCatCount）解耦，
///    GameManager 只在 ExecuteDoubleClick 的错误分支调 LoseLife()，不直接持有血量字段。
/// 2. 事件解耦——血量归零经 OnLivesZero 事件通知 GameManager 触发失败流程，
///    LivesController 不反向依赖 GameManager（遵循 PRD §7.1）。
/// 3. 字段化数值——maxLives / 单次扣血量均在 Inspector 可调，后续难度分级/道具不改代码。
/// 4. 血点显隐用 Image.enabled（不 SetActive），保持 Canvas 顶点拓扑稳定，遵循现有
///    BlockController.ApplyVisualState 模式。
/// </summary>
public class LivesController : UnityEngine.MonoBehaviour
{
    [Header("血量配置")]
    [Tooltip("每关初始血量（条命）")]
    public int maxLives = 3;
    [Tooltip("每次双击错误格扣除的血量")]
    public int losePerMistake = 1;

    [Header("血点 UI 引用（Inspector 拖拽，按从左到右顺序 = 第1/2/3 条命）")]
    [Tooltip("血点 Image 数组。扣血时从最右侧（数组末尾）依次消失：lifeIcons[i].enabled = i < currentLives")]
    public Image[] lifeIcons;

    /// <summary>当前剩余血量（只读视图）</summary>
    public int CurrentLives => _currentLives;
    private int _currentLives;

    /// <summary>血量归零时触发（仅在本关内触发一次，ResetLives 后可再次触发）。GameManager 订阅以进入失败流程。</summary>
    public event Action OnLivesZero;

    // 节流：同一关内 OnLivesZero 只发一次，避免 LoseLife 在 0 后被误调导致重复触发失败流程
    private bool _zeroFired;

    /// <summary>关卡加载/重开时调用：血量回满、复位节流标志、刷新血点显示。</summary>
    public void ResetLives()
    {
        _currentLives = maxLives;
        _zeroFired = false;
        RefreshIcons();
    }

    /// <summary>
    /// 双击错误格时调用：扣血并刷新血点。血量降到 0 时触发 OnLivesZero（同关仅一次）。
    /// 已为 0 时再调为空操作（防御复活前残留调用）。
    /// </summary>
    public void LoseLife()
    {
        if (_currentLives <= 0) return; // 已归零，复活前不再扣
        _currentLives -= losePerMistake;
        if (_currentLives < 0) _currentLives = 0;
        RefreshIcons();
        if (_currentLives == 0 && !_zeroFired)
        {
            _zeroFired = true;
            OnLivesZero?.Invoke();
        }
    }

    /// <summary>"3 more lives" 复活时调用：血量补满至 maxLives、复位节流标志（允许再次归零触发失败）、刷新血点。
    /// 注意：复位 _zeroFired 使复活后若再扣光可再次进入失败流程（复活次数无上限，PRD F18）。</summary>
    public void RefillLives()
    {
        _currentLives = maxLives;
        _zeroFired = false;
        RefreshIcons();
    }

    /// <summary>从存档恢复血量：设为指定值（钳制到 [0,maxLives]）、复位归零节流、刷新血点。
    /// 仅用于退出后台后恢复在玩关卡血量，不触发 OnLivesZero（恢复值由调用方保证 >0）。</summary>
    public void RestoreLives(int lives)
    {
        _currentLives = Mathf.Clamp(lives, 0, maxLives);
        _zeroFired = false;
        RefreshIcons();
    }

    /// <summary>刷新血点显隐：index &lt; currentLives 的亮，其余灭。
    /// 数组从左到右对应第1/2/3 条命，currentLives 减少时末尾（右侧）先灭，符合"从右边消失"。</summary>
    private void RefreshIcons()
    {
        if (lifeIcons == null) return;
        for (int i = 0; i < lifeIcons.Length; i++)
        {
            if (lifeIcons[i] == null) continue;
            lifeIcons[i].enabled = i < _currentLives;
        }
    }

    private void Awake()
    {
        // 血点是纯展示节点，关闭 RaycastTarget 减少输入遍历开销（遵循 GameManager.DisableUnneededRaycastTargets 模式）
        if (lifeIcons != null)
        {
            for (int i = 0; i < lifeIcons.Length; i++)
            {
                if (lifeIcons[i] != null) lifeIcons[i].raycastTarget = false;
            }
        }
    }
}
