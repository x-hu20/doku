using System.Collections.Generic;

/// <summary>
/// 本关解集与锁猫状态的纯逻辑容器（不依赖 Unity）。
///
/// 封装解集 / 锁猫索引 / 已锁数 / 地图，提供"沿解可锁判定 / 通关校验 / 解集并集查询 / 锁猫记账"。
/// 切关时 Reset 后由 GameManager 重新 Init 填充。逻辑与 BlockController 视觉解耦：
/// LockCat 仅记账，视觉显隐由 GameManager 调 BlockController.ApplyVisualState 处理。
///
/// 多解支持：Solutions 含全部合法解（MapSolver.EnumerateAll），玩家走出任一解即通关。
/// </summary>
public class LevelState
{
    /// <summary>当前关卡尺寸 N（每行/列恰好一只猫）。</summary>
    public int GridSize { get; private set; }
    /// <summary>当前关卡地图颜色数组（供魔法棒/提示选格器读取颜色）。</summary>
    public int[] Map { get; private set; }
    /// <summary>本关所有合法解（每个 bool[] 长度 N*N，true=正解猫）。</summary>
    public List<bool[]> Solutions { get; private set; }
    /// <summary>已锁猫一维索引集（given + 双击 + 魔法棒），阵营判定与沿解校验基准。</summary>
    public HashSet<int> LockedIndices { get; private set; }
    /// <summary>解集并集（true=该格属至少一个解），供 isCorrect 赋值，预计算避免每格扫描。</summary>
    public bool[] SolutionUnion { get; private set; }
    /// <summary>已锁猫数（given + 双击 + 魔法棒），O(1) 读取（无需实时遍历）。</summary>
    public int LockedCount { get; private set; }

    /// <summary>切关时初始化：填充关卡数据并预计算解集并集。locked 状态归零。</summary>
    public void Init(int gridSize, int[] map, List<bool[]> solutions, int totalCells)
    {
        GridSize = gridSize;
        Map = map;
        Solutions = solutions;
        LockedIndices = new HashSet<int>();
        LockedCount = 0;
        SolutionUnion = SolverUtil.UnionOfSolutions(solutions, totalCells);
    }

    /// <summary>清空状态（切关/离关时调用，Init 前置空防误读）。</summary>
    public void Reset()
    {
        GridSize = 0;
        Map = null;
        Solutions = null;
        LockedIndices = null;
        SolutionUnion = null;
        LockedCount = 0;
    }

    /// <summary>该格是否属于至少一个合法解（解集并集）。用于 isCorrect 赋值（调试/视觉提示）。</summary>
    public bool IsInUnion(int idx)
    {
        return SolutionUnion != null && idx >= 0 && idx < SolutionUnion.Length && SolutionUnion[idx];
    }

    /// <summary>锁一只猫：记账已锁索引与计数（不触碰视觉）。已锁则幂等不重复计数。</summary>
    public void LockCat(int idx)
    {
        if (LockedIndices == null) LockedIndices = new HashSet<int>();
        if (LockedIndices.Add(idx)) LockedCount++;
    }

    /// <summary>该格是否可锁定：存在某个合法解同时包含该格与当前所有已锁猫（沿解可锁）。
    /// 单解关等价于 IsInUnion；多解关玩家只能沿某解推进，走错（与已锁猫不在同一解）→ false。
    /// 保证玩家锁的猫始终是某解子集，N 只即构成某解通关。</summary>
    public bool CanLockAlongSolution(int idx)
    {
        if (Solutions == null || Solutions.Count == 0) return IsInUnion(idx); // 无解集兜底
        foreach (var s in Solutions)
        {
            if (s == null || idx >= s.Length || !s[idx]) continue;
            if (SolverUtil.LockedSubsetOf(s, LockedIndices)) return true;
        }
        return false;
    }

    /// <summary>已锁猫是否恰好构成某个合法解（多解关走出任一解即通关）。
    /// 因双击/魔法棒只锁沿解猫，LockedCount==GridSize 时此条件必真，加它作防 bug 保险。</summary>
    public bool IsSatisfied()
    {
        if (Solutions == null || Solutions.Count == 0) return false;
        if (LockedIndices == null || LockedIndices.Count != GridSize) return false; // 每解恰好 GridSize 只
        foreach (var s in Solutions)
        {
            if (s != null && SolverUtil.LockedSubsetOf(s, LockedIndices)) return true;
        }
        return false;
    }
}
