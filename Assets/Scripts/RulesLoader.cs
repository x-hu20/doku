using System.Collections.Generic;

// 规则提示文本：直接在代码中配置，不再从 Resources/rules.csv 读取。
// 共 3 条，顺序对应顶部 RuleBanner 内 RuleBlock0 / RuleBlock1 / RuleBlock2 的文本。
public static class RulesLoader
{
    // 在此修改规则文本（改完无需重启 Unity 之外的任何配置）
    public static readonly string[] Rules =
    {
        "1 Cat per color",
        "1 Cat per column and row",
        "Cat cannot touch"
    };

    // 返回规则列表，供 GameManager.FillRuleTexts 填充到顶部 UI。
    public static List<string> Load()
    {
        return new List<string>(Rules);
    }
}
