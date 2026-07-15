using System.Collections.Generic;

/// <summary>
/// 魔法棒道具选格器：在当前关卡解集与玩家已锁猫约束下，选出下一个该填入的正解猫格。
///
/// 选择规则（统一）：可能位置数最少 → 并列取距棋盘中心最近。
///  - 「可能位置数」P(g) = 候选格所属颜色在当前约束下能放猫的格子数（越少越确定，优先填）；
///    约束基准=已锁猫，不读叉号（叉号是玩家猜测，不参与确定度计算）。
///  - 「阵营」A = 含当前所有已锁猫的解集合：仅用于决定候选集合来源。
///    · 多解已偏向（|A|==1，A={Si}）：候选 = Si 未锁猫格（给 Si 阵营下一只猫）。
///    · 多解未偏向（|A|≥2）：候选 = A 中各解未锁猫格并集（去重）。
///    · 单解：候选 = 该解未锁猫格。
/// 纯逻辑、无 MonoBehaviour 依赖；输入解集来自 MapSolver.EnumerateAll。
/// </summary>
public static class MagicWandSolver
{
    /// <summary>
    /// 选出下一个该填入的正解猫格一维索引；无候选（已全部锁定/通关）返回 null。
    /// </summary>
    public static int? Pick(int gridSize, int[] map, int paletteLength, List<bool[]> solutions, HashSet<int> locked)
    {
        if (solutions == null || solutions.Count == 0) return null;

        // 阵营集 A：含当前所有已锁猫的解
        List<bool[]> a = new List<bool[]>();
        foreach (var s in solutions)
        {
            if (ContainsAll(s, locked)) a.Add(s);
        }
        if (a.Count == 0) a = solutions; // 兜底：理论不会发生（given⊆所有解）

        // 候选集合 C：A 中各解的未锁猫格并集
        // 选 P(g) 最小；并列取距中心最近
        int? bestIdx = null;
        int bestP = int.MaxValue;
        float bestDist = float.MaxValue;
        float cx = (gridSize - 1) * 0.5f;
        float cy = (gridSize - 1) * 0.5f;

        HashSet<int> seen = new HashSet<int>();
        foreach (var s in a)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (!s[i]) continue;
                if (locked != null && locked.Contains(i)) continue;
                if (!seen.Add(i)) continue; // 并集去重

                int color = map[i];
                int p = ColorPossibleCount(gridSize, map, paletteLength, color, locked);
                int r = i / gridSize;
                int c = i % gridSize;
                // 切比雪夫距离 + 欧氏平方 tiebreak
                float cheb = (System.Math.Abs(r - cx) > System.Math.Abs(c - cy))
                             ? System.Math.Abs(r - cx) : System.Math.Abs(c - cy);
                float euclid = (r - cx) * (r - cx) + (c - cy) * (c - cy);
                float dist = cheb * 100f + euclid; // 切比雪夫为主，欧氏平方作并列 tiebreak

                if (p < bestP || (p == bestP && dist < bestDist))
                {
                    bestP = p;
                    bestDist = dist;
                    bestIdx = i;
                }
            }
        }
        return bestIdx;
    }

    /// <summary>解 s 是否包含 locked 中所有已锁猫格。</summary>
    private static bool ContainsAll(bool[] s, HashSet<int> locked)
    {
        if (locked == null) return true;
        foreach (int idx in locked)
        {
            if (idx < 0 || idx >= s.Length || !s[idx]) return false;
        }
        return true;
    }

    /// <summary>
    /// 颜色 color 在当前已锁猫约束下可放猫的格子数（含候选自身）：
    /// 未锁、颜色==color、不与任何已锁猫同行/同列/相邻。基准=已锁猫，不读叉号。
    /// </summary>
    private static int ColorPossibleCount(int gridSize, int[] map, int paletteLength, int color, HashSet<int> locked)
    {
        int count = 0;
        int total = gridSize * gridSize;
        // 预计算已锁猫的占用行/列，供 O(1) 冲突判定
        HashSet<int> usedRows = new HashSet<int>();
        HashSet<int> usedCols = new HashSet<int>();
        HashSet<int> lockedSet = locked ?? new HashSet<int>();
        foreach (int idx in lockedSet)
        {
            usedRows.Add(idx / gridSize);
            usedCols.Add(idx % gridSize);
        }

        for (int i = 0; i < total; i++)
        {
            if (lockedSet.Contains(i)) continue;
            if (map[i] != color) continue;
            int r = i / gridSize;
            int c = i % gridSize;
            if (usedRows.Contains(r)) continue;
            if (usedCols.Contains(c)) continue;
            if (AdjacentToAnyLocked(r, c, gridSize, lockedSet)) continue;
            count++;
        }
        return count;
    }

    /// <summary>(r,c) 是否与任一已锁猫上下/左右/对角相邻（3x3 邻域）。</summary>
    private static bool AdjacentToAnyLocked(int r, int c, int gridSize, HashSet<int> lockedSet)
    {
        for (int dr = -1; dr <= 1; dr++)
        {
            for (int dc = -1; dc <= 1; dc++)
            {
                if (dr == 0 && dc == 0) continue;
                int nr = r + dr;
                int nc = c + dc;
                if (nr < 0 || nr >= gridSize || nc < 0 || nc >= gridSize) continue;
                if (lockedSet.Contains(nr * gridSize + nc)) return true;
            }
        }
        return false;
    }
}
