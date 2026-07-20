using System;
using System.Collections.Generic;

// 关卡数据：运行时由 LevelTableLoader 从 Resources/levels.csv 读取填充（无需 Inspector 手填或 ScriptableObject 资产）。
[Serializable]
public class LevelData
{
    public int levelIndex;          // 关卡编号（按表中出现顺序自动赋值，从 0 开始）
    public int gridSize = 4;        // 棋盘大小 N x N
    public int[] mapData;           // 一维数组，大小为 gridSize * gridSize，元素为颜色ID（0, 1, 2...）
    public int[] givenCats;         // 关卡初始给出的小猫位置（一维索引 0 ~ N*N-1），无 given 时为空数组
    // 离线烘焙的全部正解（MapSolver.EnumerateAll 全集，多解支持）：每个 bool[] 长度 gridSize*gridSize，true=正解猫。
    // null/空 表示未烘焙，运行时回退 EnumerateAll 解算（不应出现在发布版本）。
    public List<bool[]> solutions;
}
