using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 关卡正解离线烘焙器：在配置表导出阶段把每关的全部正解预先解出，写入 levels.csv 的 solution 列。
/// 运行时 LevelData 直接读取该全集（List<bool[]>），多解判定/沿解校验零解算开销，避免主线程回溯卡顿。
/// 烘焙用 MapSolver.EnumerateAll（与运行时同算法、同 cap、同顺序），故烘焙全解集与运行时枚举行为完全等价。
///
/// 多解存储：solution 列内多个解以 ';' 分隔，每个解是逗号分隔的 0/1 串（引号包裹）。
/// 单解关无 ';'（向后兼容）。cap=128 截断与运行时 EnumerateAll 一致。
///
/// 用法：菜单 Tools/Meowdoku/烘焙关卡正解 (Bake Solutions)。
/// paletteLength 自动推断：遍历全部关卡 map 取最大颜色 ID +1，无需人肉与 GameManager.palette 保持一致。
/// 解析逻辑复用 LevelTableLoader.TryParseLine（单一真相源），格式变更两端自动一致。
/// 写入前自动备份为 levels.csv.bak。
/// </summary>
public class LevelSolutionBaker : EditorWindow
{
    private const string CSV_RELATIVE_PATH = "Assets/Resources/levels.csv";

    [MenuItem("Tools/Meowdoku/烘焙关卡正解 (Bake Solutions)")]
    public static void Open()
    {
        var window = GetWindow<LevelSolutionBaker>(true, "关卡正解烘焙器", true);
        window.minSize = new Vector2(420, 140);
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("关卡正解离线烘焙", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "读取 Assets/Resources/levels.csv，逐关跑 EnumerateAll 解出全部正解，写入第 5 列 solution。\n" +
            "多解以 ';' 分隔（单解无 ';'）。运行时直接读全集，零解算开销且保留多解。\n" +
            "palette 从全部关卡 map 的最大颜色 ID 推断。写入前备份为 levels.csv.bak。",
            MessageType.Info);

        if (GUILayout.Button("开始烘焙", GUILayout.Height(32)))
        {
            Bake();
        }
    }

    void Bake()
    {
        string absPath = Path.GetFullPath(CSV_RELATIVE_PATH);
        if (!File.Exists(absPath))
        {
            EditorUtility.DisplayDialog("找不到关卡表", $"未找到 {CSV_RELATIVE_PATH}", "确定");
            return;
        }

        string text = File.ReadAllText(absPath);
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        // 备份
        string bakPath = absPath + ".bak";
        File.WriteAllText(bakPath, text, new UTF8Encoding(false));

        // 阶段一：扫描全部关卡行，推断 paletteLength（取全部 map 中最大颜色 ID +1）。
        // 无需人肉与 GameManager.palette 保持一致——颜色约束覆盖范围由数据本身决定。
        int paletteLength = 1;
        for (int li = 0; li < lines.Length; li++)
        {
            string line = lines[li].Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
            string clean = line.Replace(" ", "").ToLower();
            if (clean.StartsWith("level,gridsize,map")) continue; // 跳过表头

            if (LevelTableLoader.TryParseLine(line, li, out LevelTableLoader.ParsedLine p))
            {
                for (int i = 0; i < p.map.Length; i++)
                    if (p.map[i] + 1 > paletteLength) paletteLength = p.map[i] + 1;
            }
        }

        // 阶段二：逐关解算并写回。解析复用 LevelTableLoader.TryParseLine（单一真相源）。
        var sb = new StringBuilder();
        bool headerSeen = false;
        int baked = 0;
        int failed = 0;

        for (int li = 0; li < lines.Length; li++)
        {
            string raw = lines[li];
            string line = raw.Trim();

            if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
            {
                if (li > 0) sb.Append('\n');
                sb.Append(raw);
                continue;
            }

            if (!headerSeen)
            {
                string clean = line.Replace(" ", "").ToLower();
                if (clean.StartsWith("level,gridsize,map"))
                {
                    // 规范化表头为含 solution 列
                    sb.Append("level,gridSize,map,given,solution");
                    headerSeen = true;
                    continue;
                }
            }

            // 解析该行：复用 LevelTableLoader 的单一真相源
            if (!LevelTableLoader.TryParseLine(line, li, out LevelTableLoader.ParsedLine p))
            {
                Debug.LogWarning($"[LevelSolutionBaker] 第 {li + 1} 行解析失败，原样保留。");
                sb.Append('\n').Append(raw);
                continue;
            }

            // 烘焙全解集（EnumerateAll，与运行时同算法同 cap）；多解关写入全部解，单解关写入 1 解
            List<bool[]> solutions = MapSolver.EnumerateAll(p.gridSize, p.map, p.givenCats, paletteLength);
            string solStr = (solutions != null && solutions.Count > 0) ? SolutionsToCsv(solutions) : "";

            if (solutions == null || solutions.Count == 0)
            {
                failed++;
                Debug.LogError($"[LevelSolutionBaker] 关卡 level={p.prefix.Split(',')[0]} (gridSize={p.gridSize}) 无解！solution 列留空。");
            }
            else
            {
                baked++;
                if (solutions.Count >= 128)
                    Debug.LogWarning($"[LevelSolutionBaker] 关卡 level={p.prefix.Split(',')[0]} 合法解数 ≥128 已截断（与运行时 EnumerateAll cap 一致）。");
            }

            sb.Append('\n')
              .Append(p.prefix).Append(',')
              .Append('"').Append(p.mapStr).Append("\",")
              .Append('"').Append(p.givenStr).Append("\",")
              .Append('"').Append(solStr).Append('"');
        }

        File.WriteAllText(absPath, sb.ToString(), new UTF8Encoding(false));
        AssetDatabase.ImportAsset(CSV_RELATIVE_PATH, ImportAssetOptions.ForceUpdate);

        EditorUtility.DisplayDialog("烘焙完成",
            $"成功烘焙 {baked} 关，无解 {failed} 关。\npaletteLength 自动推断 = {paletteLength}\n备份：levels.csv.bak", "确定");
        Debug.Log($"[LevelSolutionBaker] 烘焙完成：成功 {baked}，无解 {failed}，paletteLength={paletteLength}。已写回 {CSV_RELATIVE_PATH}，备份 levels.csv.bak");
    }

    /// <summary>全解集转 CSV：多解以 ';' 分隔，每个解逗号分隔 0/1（整体由调用方引号包裹）。</summary>
    static string SolutionsToCsv(List<bool[]> solutions)
    {
        if (solutions == null || solutions.Count == 0) return "";
        var sb = new StringBuilder();
        for (int s = 0; s < solutions.Count; s++)
        {
            if (s > 0) sb.Append(';');
            bool[] sol = solutions[s];
            for (int i = 0; i < sol.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(sol[i] ? '1' : '0');
            }
        }
        return sb.ToString();
    }
}
