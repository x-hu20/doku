using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 本地化（多语言）访问器：从 Resources/i18n.csv 读取文案表，按 key 取当前语言列的值。
///
/// 设计：
/// - 与 LevelTableLoader 同模式：CSV 放 Resources/，TextAsset 读取，解析为字典。
/// - 表头第一列为 key，其余列为语言列（en / zh / ...）。当前语言由 <see cref="CurrentLanguage"/> 常量决定，
///   未来加语言只需在 CSV 增列 + 改此常量，调用点零改动。
/// - 当前固定英文（CurrentLanguage="en"），不做运行时切换；预留结构便于后续接入语言设置。
/// - 缺键返回 "[key]"：缺失在运行时一眼可见，便于排查，不静默崩。
/// - 换行用字面 \n 写在 CSV 里（转义为真实换行），保证一行一记录，规避字段内真实换行的多行解析歧义。
/// </summary>
public static class I18n
{
    /// <summary>当前语言列名（对应 CSV 表头列）。固定英文；未来切换语言改此处 + 加列即可。</summary>
    public const string CurrentLanguage = "en";

    private static Dictionary<string, string> table;
    private static bool loaded;

    /// <summary>加载文案表（幂等）。GameManager.Start 最先调用；Get 也有懒加载兜底。</summary>
    public static void Init()
    {
        if (loaded) return;
        loaded = true;
        table = LoadTable();
    }

    /// <summary>按 key 取当前语言文案。缺键返回 "[key]"，表未初始化则先懒加载。</summary>
    public static string Get(string key)
    {
        if (!loaded) Init();
        if (table != null && table.TryGetValue(key, out string v)) return v;
        return "[" + key + "]";
    }

    /// <summary>带参文案：string.Format(Get(key), args)。用于 "Level {0}" / "Progress: {0} / {1}" 等。</summary>
    public static string Getf(string key, params object[] args)
    {
        try { return string.Format(Get(key), args); }
        catch (System.FormatException)
        {
            // 占位符与参数不匹配等异常：退回原文，避免格式错误直接崩
            return Get(key);
        }
    }

    // ===================== 表加载与 CSV 解析 =====================

    private static Dictionary<string, string> LoadTable()
    {
        var dict = new Dictionary<string, string>();
        var asset = Resources.Load<TextAsset>("i18n");
        if (asset == null)
        {
            Debug.LogError("[I18n] 找不到文案表：Resources/i18n.csv");
            return dict;
        }

        string text = asset.text.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = text.Split('\n');
        if (lines.Length == 0) return dict;

        // 解析表头，定位 key 列与当前语言列
        var header = ParseLine(lines[0]);
        if (header.Count == 0)
        {
            Debug.LogError("[I18n] 表头为空。");
            return dict;
        }
        int langCol = -1;
        for (int i = 0; i < header.Count; i++)
        {
            if (header[i] == CurrentLanguage) { langCol = i; break; }
        }
        if (langCol < 0)
        {
            // 当前语言列缺失：退回第二列（首个语言列）兜底，避免全表不可用
            langCol = header.Count > 1 ? 1 : 0;
            Debug.LogWarning($"[I18n] 表头未找到语言列 \"{CurrentLanguage}\"，退回列 \"{header[langCol]}\"。");
        }

        for (int li = 1; li < lines.Length; li++)
        {
            string raw = lines[li];
            if (string.IsNullOrEmpty(raw)) continue;
            var fields = ParseLine(raw);
            if (fields.Count == 0) continue;
            string key = fields[0];
            if (string.IsNullOrEmpty(key) || key.StartsWith("#")) continue;
            string value = langCol < fields.Count ? fields[langCol] : "";
            dict[key] = Unescape(value);
        }
        return dict;
    }

    /// <summary>解析单行 CSV 为字段列表：支持双引号包裹、字段内逗号、"" 转义引号。
    /// 不支持字段内真实换行（换行统一用 \n 转义写在单行内），故按物理行解析即可。</summary>
    private static List<string> ParseLine(string line)
    {
        var fields = new List<string>();
        if (line == null) return fields;
        var sb = new StringBuilder();
        bool inQuotes = false;
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    // 连续两个引号 = 转义的字面引号
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i += 2; continue; }
                    inQuotes = false; i++; continue;
                }
                sb.Append(c); i++;
            }
            else
            {
                if (c == '"') { inQuotes = true; i++; continue; }
                if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); i++; continue; }
                sb.Append(c); i++;
            }
        }
        fields.Add(sb.ToString());
        return fields;
    }

    /// <summary>反转义：\n→换行 \t→制表 \\→反斜杠。保持 CSV 一行一记录的同时支持多行文案。</summary>
    private static string Unescape(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                char n = s[i + 1];
                if (n == 'n') { sb.Append('\n'); i++; continue; }
                if (n == 't') { sb.Append('\t'); i++; continue; }
                if (n == '\\') { sb.Append('\\'); i++; continue; }
            }
            sb.Append(s[i]);
        }
        return sb.ToString();
    }
}
