using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// 存档系统：JSON 文件（Application.persistentDataPath）持久化玩家进度/道具/宝箱周期/教程标志。
/// 纯逻辑+文件 IO，不依赖 MonoBehaviour。GameManager 启动 Load，关键状态变更时 Save。
///
/// 设置（音频/触觉开关）仍由 FeedbackManager 用 PlayerPrefs 管理，不在此处。
/// JsonUtility 无版本字段：新增字段时旧存档缺该字段取默认值（可接受；未来可加 version 字段做迁移）。
/// </summary>
[Serializable]
public class SaveData
{
    public int currentLevel = 0;        // 上次所在关卡（启动恢复用）
    public int highestUnlocked = 0;    // 已解锁最高关（关卡选择/继续用，预留）
    public int magicWandCount = 0;     // 魔法棒余量
    public int tipCount = 0;           // 提示道具余量
    public int levelsSinceChest = 0;   // 自上次开箱后通关数（宝箱周期）
    public bool tutorialSeen = false;  // 是否已完成 level 0 教程
    public bool livesHintSeen = false; // 是否已展示过"首次犯错生命值提示"（全程仅一次，写档不再显示）
    public string playerId = "";       // 本地游客ID（GUID），首次启动生成，终身不变（除非清档）
    public string accountId = "";      // 预留：未来 SDK 登录的账号ID，游客态留空
    /// <summary>上次退出后台时的在玩棋盘状态（恢复用）。levelIndex=-1 表示无在玩状态，下次按全新关卡开始。</summary>
    public BoardState inProgress = new BoardState();
}

/// <summary>在玩棋盘的持久快照：记录已锁猫 / 普通叉号 / 错误锁定格索引与剩余血量，供退出后台后恢复。
/// 仅保存非引导、非终态（未通关未失败）的进行中关卡；levelIndex 与当前关一致才恢复。</summary>
[Serializable]
public class BoardState
{
    public int levelIndex = -1;      // 该快照所属关卡索引；-1=无在玩状态
    public int lives = 3;            // 退出时剩余血量（恢复血量用）
    public List<int> lockedCats = new List<int>();   // hasCat 格索引（含 given，恢复时 LockCat 幂等）
    public List<int> crossed = new List<int>();       // 普通叉号格索引（非错误锁定）
    public List<int> errorLocked = new List<int>();  // 错误锁定格索引（hasCross 且 isErrorLocked）

    /// <summary>确保三个列表非空（JsonUtility 反序列化时缺失字段会留 null，载入后兜底重建）。</summary>
    public void EnsureLists()
    {
        if (lockedCats == null) lockedCats = new List<int>();
        if (crossed == null) crossed = new List<int>();
        if (errorLocked == null) errorLocked = new List<int>();
    }

    /// <summary>清空在玩状态：标记 levelIndex=-1（无快照），并清空三个索引列表。</summary>
    public void Clear()
    {
        levelIndex = -1;
        EnsureLists();
        lockedCats.Clear();
        crossed.Clear();
        errorLocked.Clear();
    }
}

public static class SaveSystem
{
    private static readonly string SavePath = Path.Combine(Application.persistentDataPath, "meowdoku_save.json");

    /// <summary>当前内存存档。Load 前为默认空存档。</summary>
    public static SaveData Data { get; private set; } = new SaveData();

    /// <summary>Load 时记录文件是否存在（首次玩家判定备用，实际以 tutorialSeen 判 fresh 更稳）。</summary>
    public static bool HasSave { get; private set; }

    /// <summary>读档：文件存在则反序列化，缺失/异常用默认空存档。</summary>
    public static void Load()
    {
        try
        {
            HasSave = File.Exists(SavePath);
            if (HasSave)
            {
                string json = File.ReadAllText(SavePath);
                SaveData d = JsonUtility.FromJson<SaveData>(json);
                if (d != null) Data = d;
                if (Data.inProgress == null) Data.inProgress = new BoardState();
                Data.inProgress.EnsureLists();
            }
        }
        catch (Exception e)
        {
            HasSave = false;
            Debug.LogWarning($"[SaveSystem] 存档读取失败，使用默认存档：{e.Message}");
            Data = new SaveData();
        }
    }

    /// <summary>写档：先写 .tmp 成功后原子替换正式文件，避免写档中途崩溃损坏旧存档。异常仅 LogWarning，不崩。</summary>
    public static void Save()
    {
        string tmp = SavePath + ".tmp";
        try
        {
            string dir = Path.GetDirectoryName(SavePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(tmp, JsonUtility.ToJson(Data, true));
            // 原子替换：仅当 tmp 写成功才替换正式文件，崩溃落在 tmp 写入期间不损旧存档。
            // File.Replace 原子替换（.NET 2.0+，要求目标存在）；首次无正式文件时用 File.Move。
            if (File.Exists(SavePath)) File.Replace(tmp, SavePath, null);
            else File.Move(tmp, SavePath);
        }
        catch (Exception e)
        {
            // 清理残留 tmp，避免下次替换误用半写文件
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            Debug.LogWarning($"[SaveSystem] 存档写入失败：{e.Message}");
        }
    }

    /// <summary>清档：内存 Data 归零 + 删除存档文件（含 tmp）。下次启动 = 全新玩家（重播教程+初始道具）。测试/重置进度用。</summary>
    public static void Clear()
    {
        Data = new SaveData();
        try
        {
            if (File.Exists(SavePath)) File.Delete(SavePath);
            string tmp = SavePath + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);
        }
        catch (Exception e) { Debug.LogWarning($"[SaveSystem] 存档删除失败：{e.Message}"); }
    }

    /// <summary>确保玩家拥有本地游客ID：缺失则生成 GUID 并写档。GameManager 启动 Load 后调用。
    /// 纯本地身份，跨设备/清档不保留；accountId 留空待未来 SDK 登录填充。</summary>
    public static void EnsurePlayerId()
    {
        if (string.IsNullOrEmpty(Data.playerId))
        {
            Data.playerId = System.Guid.NewGuid().ToString();
            Save();
        }
    }
}
