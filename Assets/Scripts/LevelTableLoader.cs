using System.Collections.Generic;
using UnityEngine;

// 关卡表读取器：从 Resources/levels.csv 读取 CSV 文本，解析为 List<LevelData>。
// 完美适配：严格 3 列，第 3 列为包裹在双引号中的逗号分隔字符串（例如：3,5,"0,4,4,3..."）
public static class LevelTableLoader
{
    /// <summary>
    /// 一行 CSV 解析后的中间产物：既含回写 CSV 所需的原始子串（prefix/mapStr/givenStr/solutionStr），
    /// 也含解析后的强类型字段（gridSize/map/given/solutionData）。供运行时 BuildLevel 与
    /// Editor 烘焙器（LevelSolutionBaker）共享同一份解析逻辑，杜绝两处副本漂移。
    /// </summary>
    public struct ParsedLine
    {
        public string prefix;          // "level,gridSize" 前缀（用于回写）
        public string mapStr;          // map 列原始数字串（引号内）
        public string givenStr;        // given 列原始数字串（引号内，可空）
        public string solutionStr;     // solution 列原始数字串（引号内，可空=未烘焙）
        public int gridSize;
        public int[] map;              // 解析后的 map（长度 gridSize*gridSize）
        public int[] givenCats;        // 解析后的 given 一维索引（无则空数组）
        public bool[] solutionData;     // 解析后的 solution（null=未烘焙）
    }

    /// <summary>
    /// 解析单行 CSV 为中间产物。返回 false 表示格式非法（调用方应原样保留该行）。
    /// 这是运行时与 Editor 烘焙器的单一真相源：任何 CSV 格式变更只改这里，两端自动一致。
    /// </summary>
    public static bool TryParseLine(string line, int levelOrdinal, out ParsedLine result)
    {
        result = default;

        // 核心逻辑：利用双引号 `"` 把列剥离开。标准格式含多组引号字段：
        //    第 1 组 = map，第 2 组 = given（可选），第 3 组 = solution（可选）。前缀为 level,gridSize。
        var quoteIndices = new List<int>();
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') quoteIndices.Add(i);
        }

        string part1;               // 存储 level 和 gridSize 的前缀部分
        string mapString;           // 存储 map 内部的数字部分
        string givenString = "";    // 存储 given 内部的数字部分（默认空，表示无 given 小猫）
        string solutionString = "";// 存储 solution 内部的数字部分（默认空，表示未烘焙正解）

        if (quoteIndices.Count >= 2)
        {
            // 标准 CSV 格式：第 1 对引号为 map
            int mq0 = quoteIndices[0];
            int mq1 = quoteIndices[1];
            part1 = line.Substring(0, mq0).TrimEnd(',');
            mapString = line.Substring(mq0 + 1, mq1 - mq0 - 1);

            // 第 2 对引号为 given（可选，缺失则视为无 given）
            if (quoteIndices.Count >= 6)
            {
                // 3 对引号：map, given(带引号), solution —— given 列用引号包裹
                int gq0 = quoteIndices[2];
                int gq1 = quoteIndices[3];
                givenString = line.Substring(gq0 + 1, gq1 - gq0 - 1);

                int sq0 = quoteIndices[4];
                int sq1 = quoteIndices[5];
                solutionString = line.Substring(sq0 + 1, sq1 - sq0 - 1);
            }
            else if (quoteIndices.Count >= 4)
            {
                // 2 对引号：map, solution —— given 列无引号（裸值如 8，或空），
                // 位于 map 结束引号之后、solution 开始引号之前。第 2 对引号实为 solution。
                int solStart = quoteIndices[2];
                int solEnd = quoteIndices[3];
                givenString = line.Substring(mq1 + 1, solStart - mq1 - 1).Trim(',', ' ');
                solutionString = line.Substring(solStart + 1, solEnd - solStart - 1);
            }
            else
            {
                // given 列未用引号包裹（例如单个数字 2，或无逗号的多个数字）：
                // 取 map 引号对之后的剩余内容，去掉前导逗号与空白后作为 given 串。
                givenString = line.Substring(mq1 + 1).TrimStart(',').Trim();
            }
        }
        else
        {
            // 兼容备用：如果没有双引号，则尝试按前两个逗号切分
            string[] rawTokens = line.Split(',');
            if (rawTokens.Length < 3) return false;
            part1 = $"{rawTokens[0]},{rawTokens[1]}";
            mapString = string.Join(",", rawTokens, 2, rawTokens.Length - 2);
        }

        // 1. 解析前缀的 level 和 gridSize
        string[] tokens1 = part1.Split(',');
        if (tokens1.Length < 2) return false;

        if (!int.TryParse(tokens1[1].Trim(), out int gridSize)) return false;
        if (gridSize < 2) return false;

        // 2. 解析 map
        string[] mapTokens = mapString.Split(new char[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries);
        int expected = gridSize * gridSize;
        if (mapTokens.Length != expected) return false;

        int[] map = new int[expected];
        for (int i = 0; i < expected; i++)
        {
            if (!int.TryParse(mapTokens[i].Trim(), out map[i])) return false;
        }

        // 3. 解析 given（复用同源校验：每行至多一个 given）
        int[] givenCats = ParseGivenCats(givenString, expected, levelOrdinal);
        if (givenCats == null) return false; // 校验失败（错误已打印）

        // 4. 解析 solution（空串 → null=未烘焙；非空但非法 → null 且视失败）
        bool[] solutionData = ParseSolution(solutionString, expected, levelOrdinal);
        if (solutionData == null && !string.IsNullOrWhiteSpace(solutionString)) return false;

        result = new ParsedLine
        {
            prefix = part1,
            mapStr = mapString,
            givenStr = givenString,
            solutionStr = solutionString,
            gridSize = gridSize,
            map = map,
            givenCats = givenCats,
            solutionData = solutionData
        };
        return true;
    }

    // 默认从 Resources/levels 加载
    public static List<LevelData> Load(string resourceName = "levels")
    {
        TextAsset asset = Resources.Load<TextAsset>(resourceName);
        if (asset == null)
        {
            Debug.LogError($"[LevelTableLoader] 找不到关卡表：Resources/{resourceName}.csv");
            return new List<LevelData>();
        }
        return Parse(asset.text);
    }

    // 把 CSV 文本解析为关卡列表。
    public static List<LevelData> Parse(string text)
    {
        var levels = new List<LevelData>();
        if (string.IsNullOrEmpty(text))
        {
            Debug.LogWarning("[LevelTableLoader] 关卡表内容为空。");
            return levels;
        }

        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        bool headerSeen = false;
        int levelOrdinal = 0; 

        foreach (string raw in lines)
        {
            string line = raw.Trim();

            if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                continue;

            // 检查表头
            if (!headerSeen)
            {
                string cleanLine = line.Replace(" ", "").ToLower();
                if (!cleanLine.StartsWith("level,gridsize,map"))
                {
                    Debug.LogError($"[LevelTableLoader] 表头格式错误，期望 \"level,gridSize,map\"，实际为 \"{line}\"");
                    return levels;
                }
                headerSeen = true;
                continue;
            }

            LevelData data = BuildLevel(line, levelOrdinal);
            if (data != null)
            {
                levels.Add(data);
                levelOrdinal++; 
            }
        }

        return levels;
    }

    // 把一行 CSV 组装成一关。解析逻辑委托给 TryParseLine（与 Editor 烘焙器共享单一真相源），
    // 此处仅补充 level 列可读性校验 + 组装 LevelData。
    static LevelData BuildLevel(string line, int levelOrdinal)
    {
        if (!TryParseLine(line, levelOrdinal, out ParsedLine p))
        {
            Debug.LogError($"[LevelTableLoader] 第 {levelOrdinal + 1} 关解析失败，已跳过。当前行：{line}");
            return null;
        }

        // level 列可读性校验（仅报错友好；level 值不入 LevelData，levelIndex 用 levelOrdinal）
        string[] tokens1 = p.prefix.Split(',');
        if (!int.TryParse(tokens1[0].Trim(), out int _))
        {
            Debug.LogError($"[LevelTableLoader] 第 {levelOrdinal + 1} 关 level 列 \"{tokens1[0]}\" 无法解析，已跳过。");
            return null;
        }

        return new LevelData
        {
            levelIndex = levelOrdinal,
            gridSize = p.gridSize,
            mapData = p.map,
            givenCats = p.givenCats,
            solutionData = p.solutionData
        };
    }

    // 解析 solution 列字符串为 bool[] 正解数组，并做合法性校验。
    // totalCells = gridSize * gridSize。空串 → 返回 null（表示未烘焙，运行时回退解算）。
    // 非空但校验失败 → 返回 null 并打印错误（调用方据 solutionString 非空判定为失败而跳过该关）。
    static bool[] ParseSolution(string solutionString, int totalCells, int levelOrdinal)
    {
        if (string.IsNullOrWhiteSpace(solutionString))
        {
            return null; // 未烘焙：返回 null，调用方据此在运行时回退解算
        }

        string[] tokens = solutionString.Split(new char[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length != totalCells)
        {
            Debug.LogError($"[LevelTableLoader] 第 {levelOrdinal + 1} 关 solution 数据数量({tokens.Length})与 gridSize*gridSize({totalCells})不一致，已跳过该关。");
            return null;
        }

        var result = new bool[totalCells];
        for (int i = 0; i < totalCells; i++)
        {
            string t = tokens[i].Trim();
            if (t != "0" && t != "1")
            {
                Debug.LogError($"[LevelTableLoader] 第 {levelOrdinal + 1} 关 solution 第 {i + 1} 个元素 \"{tokens[i]}\" 不是合法的 0/1，已跳过该关。");
                return null;
            }
            result[i] = t == "1";
        }
        return result;
    }

    // 解析 given 列字符串为一维索引数组，并做合法性校验。
    // totalCells = gridSize * gridSize。返回 null 表示校验失败（已打印错误）。
    static int[] ParseGivenCats(string givenString, int totalCells, int levelOrdinal)
    {
        // 空字符串 → 无 given 小猫
        if (string.IsNullOrWhiteSpace(givenString))
        {
            return new int[0];
        }

        string[] tokens = givenString.Split(new char[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries);
        var result = new List<int>(tokens.Length);
        var rowSeen = new HashSet<int>(); // 用于校验“每行最多一个 given”

        for (int i = 0; i < tokens.Length; i++)
        {
            if (!int.TryParse(tokens[i].Trim(), out int idx))
            {
                Debug.LogError($"[LevelTableLoader] 第 {levelOrdinal + 1} 关 given 列第 {i + 1} 个元素 \"{tokens[i]}\" 不是合法整数，已跳过该关。");
                return null;
            }

            // 越界校验
            if (idx < 0 || idx >= totalCells)
            {
                int gridSize = (int)System.Math.Sqrt(totalCells);
                Debug.LogError($"[LevelTableLoader] 第 {levelOrdinal + 1} 关 given 索引 {idx} 越界（有效范围 0~{totalCells - 1}，棋盘 {gridSize}x{gridSize}），已跳过该关。");
                return null;
            }

            // 每行最多一个 given：这是解算器把 given 作为约束的硬性前提
            int row = idx / (int)System.Math.Sqrt(totalCells);
            if (!rowSeen.Add(row))
            {
                Debug.LogError($"[LevelTableLoader] 第 {levelOrdinal + 1} 关 given 列在第 {row + 1} 行出现多个小猫，解算器无法将其同时作为正解约束，已跳过该关。");
                return null;
            }

            result.Add(idx);
        }

        return result.ToArray();
    }
}