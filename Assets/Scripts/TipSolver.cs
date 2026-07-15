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
///  - 叉号 = 排除标记；错叉 = 叉在阵营解（含 locked）的正解格上（该取消）；互斥解正解格被叉是合理排除，不取消。
///  - 分支1触发：lockedCatIndices 为空（棋盘完全无锁猫）。
///  - 分支2顺序：先排除已锁猫行列+周围（辅助格打叉）→ 全叉后判错叉/填猫。
///  - candidates = 阵营 F（含 locked）中未被 cross 占用的解；空（误解）→ 取消阵营正解格错叉。
/// 复用 <see cref="SolverUtil"/>（阵营判定/颜色可能位置/距中心/解集并集）与 <see cref="MagicWandSolver"/>（走魔法棒逻辑）。
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
            int p = CountColorUnCrossed(gridSize, map, c, crossed);
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
        List<int> poss = CollectColorCells(gridSize, map, bestColor, crossed);
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
            int? g = PickColorCatNearestCenter(gridSize, map, bestColor, solutions, crossed);
            if (g == null) return null;
            return new TipAction(TipActionType.FillCat, g.Value);
        }
    }

    // ===================== 分支2：已有锁猫 =====================
    private static TipAction Branch2(int gridSize, int[] map, int paletteLength, List<bool[]> solutions, HashSet<int> locked, HashSet<int> crossed)
    {
        List<int> crossedList = crossed != null ? new List<int>(crossed) : new List<int>();

        // F = 含所有已锁猫的解（阵营）。用于辅助格排除阵营正解、错叉判定。
        List<bool[]> factionLocked = new List<bool[]>();
        foreach (var s in solutions)
            if (SolverUtil.LockedSubsetOf(s, locked)) factionLocked.Add(s);
        if (factionLocked.Count == 0) factionLocked = solutions; // 兜底（理论不会，locked 必属某解）
        bool[] unionF = SolverUtil.UnionOfSolutions(factionLocked, gridSize * gridSize);

        // 1. 优先排除已锁猫的行列+周围：辅助格 = 锁猫同行/同列/3x3邻域 - locked - 阵营正解格
        //    阵营正解格（unionF）不该被叉；其余周围格必非阵营正解，可安全排除。未叉→打叉。
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

        List<int> unCrossedAux = new List<int>();
        foreach (int i in aux)
        {
            if (i >= 0 && i < unionF.Length && unionF[i]) continue; // 阵营正解格跳过
            if (crossed != null && crossed.Contains(i)) continue;
            unCrossedAux.Add(i);
        }
        if (unCrossedAux.Count > 0)
            return new TipAction(TipActionType.SetCross, unCrossedAux); // 未叉→打叉，已叉不动（非 toggle）

        // 2. 辅助格全叉后，判错叉/填猫
        // candidates = 阵营 F 中未被 cross 占用（无正解格被叉）的解
        List<bool[]> candidates = new List<bool[]>();
        foreach (var s in factionLocked)
            if (!SolverUtil.SolutionHasAny(s, crossedList)) candidates.Add(s);

        if (candidates.Count == 0)
        {
            // 阵营解被占用，但可能有未叉未锁的阵营正解格（如刚取消 cross 的格）→优先填入引导锁猫，
            // 这样取消一个错叉后下一步即填该格，而非连续取消多个错叉。
            int? fill = PickUnCrossedFactionCat(gridSize, map, unionF, crossed, locked);
            if (fill != null) return new TipAction(TipActionType.FillCat, fill.Value);

            // 无未叉阵营正解格 → 取消阵营正解格上的 cross（错叉）。
            // 互斥解（不含 locked）正解格被叉是合理排除，不算错叉，不取消。
            List<int> wrongCross = new List<int>();
            if (crossed != null)
                foreach (int i in crossed)
                    if (i >= 0 && i < unionF.Length && unionF[i]) wrongCross.Add(i);
            if (wrongCross.Count == 0) return null;
            int? g = PickWrongCrossToRemove(gridSize, map, wrongCross, locked);
            if (g == null) return null;
            return new TipAction(TipActionType.RemoveCross, g.Value);
        }

        // candidates 非空 → 走魔法棒限定 candidates 填猫
        int? picked = MagicWandSolver.Pick(gridSize, map, paletteLength, candidates, locked);
        if (picked == null) return null;
        return new TipAction(TipActionType.FillCat, picked.Value);
    }

    // ===================== 分支1专用：颜色格计数（排除叉号）=====================
    /// <summary>颜色 c 的未叉格数（无锁猫，用于分支1「最小区域」）。</summary>
    private static int CountColorUnCrossed(int gridSize, int[] map, int c, HashSet<int> crossed)
    {
        int count = 0;
        for (int i = 0; i < gridSize * gridSize; i++)
        {
            if (map[i] != c) continue;
            if (crossed != null && crossed.Contains(i)) continue;
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
    private static int? PickColorCatNearestCenter(int gridSize, int[] map, int c, List<bool[]> solutions, HashSet<int> crossed)
    {
        bool[] union = SolverUtil.UnionOfSolutions(solutions, gridSize * gridSize);
        int? best = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < union.Length; i++)
        {
            if (!union[i] || map[i] != c) continue;
            if (crossed != null && crossed.Contains(i)) continue;
            float dist = SolverUtil.CenterDistance(i, gridSize);
            if (dist < bestDist) { bestDist = dist; best = i; }
        }
        return best;
    }

    /// <summary>未叉未锁的阵营正解格中，按其色可能位置数最小→中心选一格（candidates 空时优先填入引导锁猫）。</summary>
    private static int? PickUnCrossedFactionCat(int gridSize, int[] map, bool[] unionF, HashSet<int> crossed, HashSet<int> locked)
    {
        int? best = null;
        int bestP = int.MaxValue;
        float bestDist = float.MaxValue;
        for (int i = 0; i < unionF.Length; i++)
        {
            if (!unionF[i]) continue;
            if (locked != null && locked.Contains(i)) continue;
            if (crossed != null && crossed.Contains(i)) continue;
            int p = SolverUtil.ColorPossibleCount(gridSize, map, map[i], locked);
            if (p < bestP)
            {
                bestP = p;
                bestDist = SolverUtil.CenterDistance(i, gridSize);
                best = i;
            }
            else if (p == bestP)
            {
                float dist = SolverUtil.CenterDistance(i, gridSize);
                if (dist < bestDist) { bestDist = dist; best = i; }
            }
        }
        return best;
    }

    /// <summary>错叉格中按其色可能位置数（不读叉号、锁猫约束）最小→中心选一格。</summary>
    private static int? PickWrongCrossToRemove(int gridSize, int[] map, List<int> wrongCross, HashSet<int> locked)
    {
        int? best = null;
        int bestP = int.MaxValue;
        float bestDist = float.MaxValue;
        foreach (int i in wrongCross)
        {
            int p = SolverUtil.ColorPossibleCount(gridSize, map, map[i], locked);
            if (p < bestP)
            {
                bestP = p;
                bestDist = SolverUtil.CenterDistance(i, gridSize);
                best = i;
            }
            else if (p == bestP)
            {
                float dist = SolverUtil.CenterDistance(i, gridSize);
                if (dist < bestDist) { bestDist = dist; best = i; }
            }
        }
        return best;
    }

    // ===================== 几何 =====================
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
}
