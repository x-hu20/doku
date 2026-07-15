using System.Collections.Generic;

/// <summary>提示道具动作类型：填入锁猫 / 打叉 / 取消叉。</summary>
public enum TipActionType { FillCat, SetCross, RemoveCross }

/// <summary>提示道具选定的动作：类型 + 目标格一维索引列表（SetCross/RemoveCross 可多格，FillCat 单格）。</summary>
public class TipAction
{
    public TipActionType type;
    public List<int> indices;

    public TipAction(TipActionType type, List<int> indices)
    {
        this.type = type;
        this.indices = indices ?? new List<int>();
    }
    public TipAction(TipActionType type, int single)
    {
        this.type = type;
        this.indices = new List<int> { single };
    }
}

/// <summary>
/// 提示道具选格器（与魔法棒平行）：按棋盘状态给出不同动作（填入/打叉/取消叉），引导玩家推理。
///
/// 概念（已确认）：
///  - 区域 = 颜色组；最小区域 = 可能位置数最少的色。
///  - 叉号 = 排除标记；占用正确格子 = 错叉（叉在正解猫格上）。
///  - 分支1触发：lockedCatIndices 为空（棋盘完全无锁猫）。
///  - 1.2.2：沿解填入。
///  - 2.2.0/2.2.1 合并：无错叉→走魔法棒。
///  - 2.2.3：按被错叉占用的解集合个数 |O| 判定（非错叉格数）。
/// 复用 MagicWandSolver（走魔法棒逻辑）与 currentSolutions/lockedCatIndices。
/// </summary>
public static class TipSolver
{
    /// <summary>主入口：返回提示动作；无可给动作返回 null。</summary>
    public static TipAction Pick(int gridSize, int[] map, int paletteLength, List<bool[]> solutions, HashSet<int> locked, HashSet<int> crossed)
    {
        if (solutions == null || solutions.Count == 0) return null;
        if (locked == null || locked.Count == 0)
            return Branch1(gridSize, map, paletteLength, solutions, crossed);
        return Branch2(gridSize, map, paletteLength, solutions, locked, crossed);
    }

    // ===================== 分支1：棋盘无锁猫 =====================
    private static TipAction Branch1(int gridSize, int[] map, int paletteLength, List<bool[]> solutions, HashSet<int> crossed)
    {
        // 最小颜色组：可能位置数 = 该色未叉格数（无锁猫，无需考虑冲突）
        int bestColor = -1, bestP = int.MaxValue;
        for (int c = 0; c < paletteLength; c++)
        {
            int p = CountColorCells(gridSize, map, c, null, crossed, true /*excludeCrossed*/);
            if (p <= 0) continue;
            if (p < bestP) { bestP = p; bestColor = c; }
        }
        if (bestColor == -1) return null;

        // 1.1 最小区域1格：填入那只猫
        if (bestP == 1)
        {
            int g = FirstColorCell(gridSize, map, bestColor, crossed);
            if (g >= 0) return new TipAction(TipActionType.FillCat, g);
            return null;
        }

        // 1.2 >1格
        List<int> poss = CollectColorCells(gridSize, map, bestColor, crossed); // 该色未叉格
        if (poss.Count == 0) return null;

        if (AllSameRow(poss, gridSize) || AllSameCol(poss, gridSize))
        {
            // 1.2.1 单一行或列：排除该行/列中非该色未叉格
            List<int> targets = new List<int>();
            bool sameRow = AllSameRow(poss, gridSize);
            int line = sameRow ? poss[0] / gridSize : poss[0] % gridSize;
            for (int i = 0; i < gridSize * gridSize; i++)
            {
                if (crossed != null && crossed.Contains(i)) continue;
                if (map[i] == bestColor) continue;
                int r = i / gridSize, c = i % gridSize;
                if (sameRow ? (r == line) : (c == line)) targets.Add(i);
            }
            if (targets.Count == 0) return null;
            return new TipAction(TipActionType.SetCross, targets);
        }
        else
        {
            // 1.2.2 非单一行列：沿解填入（该色在并集解里的猫格，未锁，距中心最近）
            int? g = PickColorCatNearestCenter(gridSize, map, bestColor, solutions, null /*locked 空*/, crossed);
            if (g == null) return null;
            return new TipAction(TipActionType.FillCat, g.Value);
        }
    }

    // ===================== 分支2：已有锁猫 =====================
    private static TipAction Branch2(int gridSize, int[] map, int paletteLength, List<bool[]> solutions, HashSet<int> locked, HashSet<int> crossed)
    {
        // 辅助格 = 每只锁猫的同行/同列/3x3邻域 - locked自身
        HashSet<int> aux = new HashSet<int>();
        foreach (int idx in locked)
        {
            int r = idx / gridSize, c = idx % gridSize;
            for (int cc = 0; cc < gridSize; cc++) aux.Add(r * gridSize + cc); // 同行
            for (int rr = 0; rr < gridSize; rr++) aux.Add(rr * gridSize + c); // 同列
            for (int dr = -1; dr <= 1; dr++)
                for (int dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0) continue;
                    int nr = r + dr, nc = c + dc;
                    if (nr < 0 || nr >= gridSize || nc < 0 || nc >= gridSize) continue;
                    aux.Add(nr * gridSize + nc);
                }
        }
        aux.ExceptWith(locked); // 排除锁猫自身

        // 未叉辅助格
        List<int> unCrossedAux = new List<int>();
        foreach (int i in aux)
            if (crossed == null || !crossed.Contains(i)) unCrossedAux.Add(i);

        if (unCrossedAux.Count > 0)
        {
            // 2.1 无辅助cross：未叉→打叉，已叉不动
            return new TipAction(TipActionType.SetCross, unCrossedAux);
        }

        // 2.2 有辅助cross（全叉）
        bool[] union = UnionOfSolutions(solutions, gridSize * gridSize);
        // 错叉集 = crossed ∩ 解集并集
        List<int> wrongCross = new List<int>();
        if (crossed != null)
            foreach (int i in crossed)
                if (i >= 0 && i < union.Length && union[i]) wrongCross.Add(i);

        if (wrongCross.Count == 0)
        {
            // 2.2.0/2.2.1 无错叉→走魔法棒
            int? g = MagicWandSolver.Pick(gridSize, map, paletteLength, solutions, locked);
            if (g == null) return null;
            return new TipAction(TipActionType.FillCat, g.Value);
        }

        if (solutions.Count == 1)
        {
            // 2.2.2 单解：取消一个占用（错叉格按色可能位置数最小→中心）
            int? g = PickWrongCrossToRemove(gridSize, map, paletteLength, wrongCross, locked);
            if (g == null) return null;
            return new TipAction(TipActionType.RemoveCross, g.Value);
        }

        // 2.2.3 多解：按被错叉占用的解集合个数 |O| 判定
        List<bool[]> occupied = new List<bool[]>();
        foreach (var s in solutions)
            if (SolutionHasAny(s, wrongCross)) occupied.Add(s);

        if (occupied.Count == 1)
        {
            // |O|==1：走魔法棒但排除已占用解
            List<bool[]> usable = new List<bool[]>(solutions);
            usable.Remove(occupied[0]);
            int? g = MagicWandSolver.Pick(gridSize, map, paletteLength, usable, locked);
            if (g == null) return null;
            return new TipAction(TipActionType.FillCat, g.Value);
        }
        else
        {
            // |O|>=2：取消一个占用
            int? g = PickWrongCrossToRemove(gridSize, map, paletteLength, wrongCross, locked);
            if (g == null) return null;
            return new TipAction(TipActionType.RemoveCross, g.Value);
        }
    }

    // ===================== 辅助：颜色可能位置 / 选格 =====================

    /// <summary>颜色 c 的格子数（可选排除叉号、排除锁猫冲突）。用于分支1「最小区域」。</summary>
    private static int CountColorCells(int gridSize, int[] map, int c, HashSet<int> locked, HashSet<int> crossed, bool excludeCrossed)
    {
        int count = 0;
        for (int i = 0; i < gridSize * gridSize; i++)
        {
            if (map[i] != c) continue;
            if (excludeCrossed && crossed != null && crossed.Contains(i)) continue;
            if (locked != null && locked.Contains(i)) continue;
            count++;
        }
        return count;
    }

    private static int FirstColorCell(int gridSize, int[] map, int c, HashSet<int> crossed)
    {
        for (int i = 0; i < gridSize * gridSize; i++)
        {
            if (map[i] != c) continue;
            if (crossed != null && crossed.Contains(i)) continue;
            return i;
        }
        return -1;
    }

    private static List<int> CollectColorCells(int gridSize, int[] map, int c, HashSet<int> crossed)
    {
        var list = new List<int>();
        for (int i = 0; i < gridSize * gridSize; i++)
        {
            if (map[i] != c) continue;
            if (crossed != null && crossed.Contains(i)) continue;
            list.Add(i);
        }
        return list;
    }

    /// <summary>该色在并集解里的猫格（未锁），按距中心最近选一格（沿解填入用）。</summary>
    private static int? PickColorCatNearestCenter(int gridSize, int[] map, int c, List<bool[]> solutions, HashSet<int> locked, HashSet<int> crossed)
    {
        float cx = (gridSize - 1) * 0.5f, cy = (gridSize - 1) * 0.5f;
        int? best = null;
        float bestDist = float.MaxValue;
        HashSet<int> seen = new HashSet<int>();
        foreach (var s in solutions)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (!s[i]) continue;
                if (map[i] != c) continue;
                if (locked != null && locked.Contains(i)) continue;
                if (!seen.Add(i)) continue;
                int r = i / gridSize, col = i % gridSize;
                float dist = System.Math.Max(System.Math.Abs(r - cx), System.Math.Abs(col - cy)) * 100f
                             + (r - cx) * (r - cx) + (col - cy) * (col - cy);
                if (dist < bestDist) { bestDist = dist; best = i; }
            }
        }
        return best;
    }

    /// <summary>错叉格中按其色可能位置数（不读叉号、锁猫约束）最小→中心选一格。</summary>
    private static int? PickWrongCrossToRemove(int gridSize, int[] map, int paletteLength, List<int> wrongCross, HashSet<int> locked)
    {
        float cx = (gridSize - 1) * 0.5f, cy = (gridSize - 1) * 0.5f;
        int? best = null;
        int bestP = int.MaxValue;
        float bestDist = float.MaxValue;
        foreach (int i in wrongCross)
        {
            int color = map[i];
            int p = ColorPossibleCountNoCross(gridSize, map, color, locked);
            int r = i / gridSize, c = i % gridSize;
            float dist = System.Math.Max(System.Math.Abs(r - cx), System.Math.Abs(c - cy)) * 100f
                         + (r - cx) * (r - cx) + (c - cy) * (c - cy);
            if (p < bestP || (p == bestP && dist < bestDist))
            {
                bestP = p; bestDist = dist; best = i;
            }
        }
        return best;
    }

    /// <summary>颜色 c 在锁猫约束下可放猫的格子数（不读叉号）。与 MagicWandSolver.ColorPossibleCount 同语义。</summary>
    private static int ColorPossibleCountNoCross(int gridSize, int[] map, int color, HashSet<int> locked)
    {
        int count = 0;
        HashSet<int> usedRows = new HashSet<int>(), usedCols = new HashSet<int>();
        if (locked != null)
            foreach (int idx in locked) { usedRows.Add(idx / gridSize); usedCols.Add(idx % gridSize); }
        for (int i = 0; i < gridSize * gridSize; i++)
        {
            if (map[i] != color) continue;
            if (locked != null && locked.Contains(i)) continue;
            int r = i / gridSize, c = i % gridSize;
            if (usedRows.Contains(r)) continue;
            if (usedCols.Contains(c)) continue;
            if (AdjacentToAnyLocked(r, c, gridSize, locked)) continue;
            count++;
        }
        return count;
    }

    private static bool AdjacentToAnyLocked(int r, int c, int gridSize, HashSet<int> locked)
    {
        if (locked == null) return false;
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

    // ===================== 辅助：几何 / 解集 =====================
    private static bool AllSameRow(List<int> cells, int gridSize)
    {
        if (cells.Count == 0) return false;
        int r = cells[0] / gridSize;
        foreach (int i in cells) if (i / gridSize != r) return false;
        return true;
    }
    private static bool AllSameCol(List<int> cells, int gridSize)
    {
        if (cells.Count == 0) return false;
        int c = cells[0] % gridSize;
        foreach (int i in cells) if (i % gridSize != c) return false;
        return true;
    }

    private static bool[] UnionOfSolutions(List<bool[]> solutions, int len)
    {
        bool[] u = new bool[len];
        foreach (var s in solutions)
        {
            if (s == null) continue;
            for (int i = 0; i < s.Length && i < len; i++) if (s[i]) u[i] = true;
        }
        return u;
    }

    private static bool SolutionHasAny(bool[] s, List<int> idxs)
    {
        foreach (int i in idxs)
        {
            if (i >= 0 && i < s.Length && s[i]) return true;
        }
        return false;
    }
}
