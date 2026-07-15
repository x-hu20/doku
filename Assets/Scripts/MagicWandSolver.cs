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
/// 公共原语（阵营判定/颜色可能位置/距中心）复用 <see cref="SolverUtil"/>。
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
        List<bool[]> faction = new List<bool[]>();
        foreach (var s in solutions)
            if (SolverUtil.LockedSubsetOf(s, locked)) faction.Add(s);
        if (faction.Count == 0) faction = solutions; // 兜底：理论不会发生（given⊆所有解）

        // 候选集合：A 中各解的未锁猫格并集，选 P(g) 最小；并列取距中心最近
        int? bestIdx = null;
        int bestP = int.MaxValue;
        float bestDist = float.MaxValue;
        HashSet<int> seen = new HashSet<int>();

        foreach (var s in faction)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (!s[i]) continue;
                if (locked != null && locked.Contains(i)) continue;
                if (!seen.Add(i)) continue; // 并集去重

                int p = SolverUtil.ColorPossibleCount(gridSize, map, map[i], locked);
                if (p < bestP)
                {
                    bestP = p;
                    bestDist = SolverUtil.CenterDistance(i, gridSize);
                    bestIdx = i;
                }
                else if (p == bestP)
                {
                    float dist = SolverUtil.CenterDistance(i, gridSize);
                    if (dist < bestDist) { bestDist = dist; bestIdx = i; }
                }
            }
        }
        return bestIdx;
    }
}
