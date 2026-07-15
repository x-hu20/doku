using System.Collections.Generic;

/// <summary>
/// 解集/选格公共工具：MagicWandSolver 与 TipSolver 共用的纯逻辑原语。
/// 提取此处避免两 Solver 重复实现漂移（阵营判定、颜色可能位置、邻域冲突、距中心选格）。
/// </summary>
public static class SolverUtil
{
    /// <summary>locked 是否全在解 s 内（s 含所有已锁猫 → s 属阵营）。</summary>
    public static bool LockedSubsetOf(bool[] s, HashSet<int> locked)
    {
        if (s == null) return false;
        if (locked == null || locked.Count == 0) return true;
        foreach (int idx in locked)
            if (idx < 0 || idx >= s.Length || !s[idx]) return false;
        return true;
    }

    /// <summary>解集并集（true=该格属至少一个解）。</summary>
    public static bool[] UnionOfSolutions(List<bool[]> solutions, int len)
    {
        bool[] u = new bool[len];
        if (solutions == null) return u;
        foreach (var s in solutions)
        {
            if (s == null) continue;
            for (int i = 0; i < s.Length && i < len; i++)
                if (s[i]) u[i] = true;
        }
        return u;
    }

    /// <summary>解 s 是否含 idxs 中任一格。</summary>
    public static bool SolutionHasAny(bool[] s, List<int> idxs)
    {
        if (idxs == null || idxs.Count == 0) return false;
        foreach (int i in idxs)
            if (i >= 0 && i < s.Length && s[i]) return true;
        return false;
    }

    /// <summary>颜色 color 在锁猫约束下可放猫的格子数（含候选自身，不读叉号）。
    /// 排除：已锁猫格、与已锁猫同行/同列、与已锁猫相邻（3x3 邻域）。
    /// MagicWand 的 P(g) 与 Tip 的 RemoveCross 选格共用。</summary>
    public static int ColorPossibleCount(int gridSize, int[] map, int color, HashSet<int> locked)
    {
        int count = 0;
        HashSet<int> usedRows = null, usedCols = null;
        if (locked != null && locked.Count > 0)
        {
            usedRows = new HashSet<int>();
            usedCols = new HashSet<int>();
            foreach (int idx in locked) { usedRows.Add(idx / gridSize); usedCols.Add(idx % gridSize); }
        }
        int total = gridSize * gridSize;
        for (int i = 0; i < total; i++)
        {
            if (map[i] != color) continue;
            if (locked != null && locked.Contains(i)) continue;
            int r = i / gridSize, c = i % gridSize;
            if (usedRows != null && usedRows.Contains(r)) continue;
            if (usedCols != null && usedCols.Contains(c)) continue;
            if (AdjacentToAnyLocked(r, c, gridSize, locked)) continue;
            count++;
        }
        return count;
    }

    /// <summary>(r,c) 是否与任一已锁猫上下/左右/对角相邻（3x3 邻域）。</summary>
    public static bool AdjacentToAnyLocked(int r, int c, int gridSize, HashSet<int> locked)
    {
        if (locked == null || locked.Count == 0) return false;
        for (int dr = -1; dr <= 1; dr++)
            for (int dc = -1; dc <= 1; dc++)
            {
                if (dr == 0 && dc == 0) continue;
                int nr = r + dr, nc = c + dc;
                if (nr < 0 || nr >= gridSize || nc < 0 || nc >= gridSize) continue;
                if (locked.Contains(nr * gridSize + nc)) return true;
            }
        return false;
    }

    /// <summary>格 idx 距棋盘中心的距离（切比雪夫为主 ×100 + 欧氏平方 tiebreak），
    /// 用于并列候选选格（可能位置数相同时取距中心最近）。</summary>
    public static float CenterDistance(int idx, int gridSize)
    {
        float cx = (gridSize - 1) * 0.5f;
        float cy = (gridSize - 1) * 0.5f;
        int r = idx / gridSize;
        int c = idx % gridSize;
        float cheb = System.Math.Max(System.Math.Abs(r - cx), System.Math.Abs(c - cy));
        return cheb * 100f + (r - cx) * (r - cx) + (c - cy) * (c - cy);
    }
}
