// 通用回溯解算器：单一真相源，供 Editor 烘焙工具与运行时回退共用，避免两份解算代码漂移。
//
// 规则（与原 GameManager 内联实现完全一致）：
//  - 棋盘 N x N，逐行放置，每行恰好一只猫；
//  - 每列至多一只猫（colUsed）；
//  - 每种颜色（color < paletteLength）至多一只猫（colorUsed）；
//  - 猫之间不得上下/左右/对角相邻（IsAnimalAdjacent 判 3x3 邻域，因逐行推进故实际只约束相邻行）；
//  - givenCats 锁定某行的列，使求出的解必定包含 given 位置。
// 返回 bool[]（长度 N*N，true=该格为正解猫），无解返回 null。
public static class MapSolver
{
    public static bool[] Solve(int gridSize, int[] map, int[] givenCats, int paletteLength)
    {
        bool[] result = new bool[gridSize * gridSize];
        bool[] colUsed = new bool[gridSize];
        bool[] colorUsed = new bool[paletteLength > 0 ? paletteLength : 20];

        // 预处理：把 given 索引转换为“每行锁定的列”。-1 表示该行未给定
        int[] givenColForRow = new int[gridSize];
        for (int i = 0; i < gridSize; i++) givenColForRow[i] = -1;
        if (givenCats != null)
        {
            foreach (int idx in givenCats)
            {
                int r = idx / gridSize;
                int c = idx % gridSize;
                givenColForRow[r] = c; // CSV 已校验每行最多一个 given，这里直接覆盖
            }
        }

        if (Backtrack(0, gridSize, map, result, colUsed, colorUsed, givenColForRow))
            return result;
        return null;
    }

    static bool Backtrack(int row, int gridSize, int[] map, bool[] result, bool[] colUsed, bool[] colorUsed, int[] givenColForRow)
    {
        if (row == gridSize) return true;

        // 该行若被 given 锁定，则只尝试给定的列；否则遍历所有列
        int fixedC = givenColForRow[row];
        for (int c = 0; c < gridSize; c++)
        {
            if (fixedC != -1 && c != fixedC) continue;

            int idx = row * gridSize + c;
            int color = map[idx];

            if (colUsed[c] || (color < colorUsed.Length && colorUsed[color])) continue;
            if (IsAnimalAdjacent(row, c, gridSize, result)) continue;

            result[idx] = true;
            colUsed[c] = true;
            if (color < colorUsed.Length) colorUsed[color] = true;

            if (Backtrack(row + 1, gridSize, map, result, colUsed, colorUsed, givenColForRow))
                return true;

            result[idx] = false;
            colUsed[c] = false;
            if (color < colorUsed.Length) colorUsed[color] = false;
        }
        return false;
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
