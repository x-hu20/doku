// 通用回溯解算器：单一真相源，供 Editor 烘焙工具（LevelSolutionBaker）与运行时未烘焙回退共用，避免两份解算代码漂移。
//
// 规则：
//  - 棋盘 N x N，逐行放置，每行恰好一只猫；
//  - 每列至多一只猫（colUsed）；
//  - 每种颜色（color < paletteLength）至多一只猫（colorUsed）；
//  - 猫之间不得上下/左右/对角相邻（IsAnimalAdjacent 判 3x3 邻域，因逐行推进故实际只约束相邻行）；
//  - givenCats 锁定某行的列，使求出的解必定包含 given 位置。
// EnumerateAll 收集全部合法解（多解支持）；无解返回空列表。
public static class MapSolver
{
    /// <summary>
    /// 枚举当前关卡的所有合法解（多解判定 / 魔法棒 / 双击沿解判定 / 通关校验共用）。
    /// 不命中即返回，而是收集全部解。cap 截断：解数超过 cap 时停止枚举（多解判定仍成立；阵营判定按已枚举子集，极端关可能不准，告警提示）。
    /// 返回 List&lt;bool[]&gt;（每个长度 N*N，true=正解猫）；无解返回空列表。
    /// </summary>
    public static System.Collections.Generic.List<bool[]> EnumerateAll(int gridSize, int[] map, int[] givenCats, int paletteLength, int cap = 128)
    {
        var results = new System.Collections.Generic.List<bool[]>();
        bool[] current = new bool[gridSize * gridSize];
        bool[] colUsed = new bool[gridSize];
        bool[] colorUsed = new bool[paletteLength > 0 ? paletteLength : 20];

        // 预处理：given → 每行锁定列（与 Solve 同源，保证所有解必含 given 位置）
        int[] givenColForRow = new int[gridSize];
        for (int i = 0; i < gridSize; i++) givenColForRow[i] = -1;
        if (givenCats != null)
        {
            foreach (int idx in givenCats)
            {
                int r = idx / gridSize;
                int c = idx % gridSize;
                givenColForRow[r] = c;
            }
        }

        EnumerateBacktrack(0, gridSize, map, current, colUsed, colorUsed, givenColForRow, results, cap);

        if (results.Count >= cap)
            UnityEngine.Debug.LogWarning($"[MapSolver] 第该关合法解数 ≥ {cap}，已截断枚举；阵营/多解判定按已枚举子集，极端关可能不准。");
        return results;
    }

    static void EnumerateBacktrack(int row, int gridSize, int[] map, bool[] current, bool[] colUsed, bool[] colorUsed, int[] givenColForRow, System.Collections.Generic.List<bool[]> results, int cap)
    {
        if (results.Count >= cap) return; // 截断传播
        if (row == gridSize)
        {
            results.Add((bool[])current.Clone());
            return;
        }

        int fixedC = givenColForRow[row];
        for (int c = 0; c < gridSize; c++)
        {
            if (fixedC != -1 && c != fixedC) continue;

            int idx = row * gridSize + c;
            int color = map[idx];

            if (colUsed[c] || (color < colorUsed.Length && colorUsed[color])) continue;
            if (IsAnimalAdjacent(row, c, gridSize, current)) continue;

            current[idx] = true;
            colUsed[c] = true;
            if (color < colorUsed.Length) colorUsed[color] = true;

            EnumerateBacktrack(row + 1, gridSize, map, current, colUsed, colorUsed, givenColForRow, results, cap);

            current[idx] = false;
            colUsed[c] = false;
            if (color < colorUsed.Length) colorUsed[color] = false;

            if (results.Count >= cap) return; // 截断传播
        }
    }

    static bool IsAnimalAdjacent(int r, int c, int gridSize, bool[] result)
    {
        for (int dr = -1; dr <= 1; dr++)
        {
            for (int dc = -1; dc <= 1; dc++)
            {
                int nr = r + dr;
                int nc = c + dc;

                if (nr >= 0 && nr < gridSize && nc >= 0 && nc < gridSize)
                {
                    if (result[nr * gridSize + nc]) return true;
                }
            }
        }
        return false;
    }
}
