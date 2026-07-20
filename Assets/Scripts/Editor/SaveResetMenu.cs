using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 存档重置编辑器菜单：测试用。删除 persistentDataPath 下的存档文件，下次播放 = 全新玩家（重播教程 + 初始道具）。
/// 菜单：Tools/Meowdoku/重置存档（重播教程）。
/// </summary>
public static class SaveResetMenu
{
    [MenuItem("Tools/Meowdoku/重置存档（重播教程）")]
    public static void ResetSave()
    {
        string path = Path.Combine(Application.persistentDataPath, "meowdoku_save.json");
        string tmp = path + ".tmp";
        bool hadSave = File.Exists(path);

        if (File.Exists(path)) File.Delete(path);
        if (File.Exists(tmp)) File.Delete(tmp);

        // 内存存档也清（若运行时已 Load 过，避免下次 Save 把旧值写回）
        SaveSystem.Clear();

        Debug.Log($"[SaveReset] 存档已重置（{path}）。下次播放从头开始。");
        EditorUtility.DisplayDialog("存档已重置",
            hadSave ? "已删除存档，下次播放重播教程 + 初始道具。" : "本就无存档（已是全新状态）。",
            "确定");
    }
}
