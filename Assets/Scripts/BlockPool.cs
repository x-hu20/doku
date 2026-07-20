using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 棋盘格子对象池：切换关卡时不销毁格子，回收入池复用，避免高频 Instantiate/Destroy 造成内存碎片与耗时。
///
/// 池无上限，按历史最大棋盘规模驻留（10×10 关后池里稳定 100 个 GameObject 常驻内存，代价 <1MB）。
/// 首次遇到大棋盘时一次性 Instantiate N 个；之后切回小棋盘再切大棋盘不再 Instantiate。
/// BlockController.Setup 显式复位所有持久状态，故池复用安全（无串色/串状态）。
/// </summary>
public class BlockPool
{
    private readonly Queue<BlockController> pool = new Queue<BlockController>();
    private readonly GameObject prefab;
    private readonly Transform parent;

    public BlockPool(GameObject prefab, Transform parent)
    {
        this.prefab = prefab;
        this.parent = parent;
    }

    /// <summary>池内可用格子数。</summary>
    public int Count => pool.Count;

    /// <summary>取一个格子：池非空则复用（SetParent 回棋盘 + SetActive），池空才 Instantiate 新格子。</summary>
    public BlockController Acquire()
    {
        BlockController bc;
        if (pool.Count > 0)
        {
            bc = pool.Dequeue();
            bc.transform.SetParent(parent, false);
            bc.gameObject.SetActive(true);
        }
        else
        {
            GameObject go = Object.Instantiate(prefab, parent);
            go.transform.localScale = Vector3.one;
            bc = go.GetComponent<BlockController>();
        }
        return bc;
    }

    /// <summary>回收格子入池：失活 + 脱离棋盘布局（SetParent(null) 避免 GridLayoutGroup 仍参与排布）。</summary>
    public void Release(BlockController bc)
    {
        if (bc == null) return;
        bc.gameObject.SetActive(false);
        bc.transform.SetParent(null, false);
        pool.Enqueue(bc);
    }
}
