using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class GameManager : MonoBehaviour
{
    private List<LevelData> loadedLevels = new List<LevelData>(); // 运行时从 CSV 读取填充的关卡列表
    private int currentLevelIndex = 0; // 当前关卡索引（从0开始计数，0代表第1关）
    private int gridSize = 4;          // 运行时由 CSV 数据动态决定

    [Header("UI格子动态缩放配置")]
    public bool autoScaleSpacing = true;
    public Vector2 baseSpacing = new Vector2(20f, 20f);

    [Header("基础引用")]
    public GameObject blockPrefab;
    public Transform gridParent;
    public Color[] palette;
    [Tooltip("全屏装饰背景（不接收点击，仅关闭其 RaycastTarget 减少输入遍历）。拖入场景根 Canvas 下的 Background 节点")]
    [SerializeField] private Image background;

    [Header("顶部UI引用（预制体摆放在场景后，Inspector 拖拽绑定）")]
    [Tooltip("最顶部 “Level xx” 文本节点")]
    [SerializeField] private TMP_Text levelText;
    [Tooltip("下方 “Progress: n / m” 文本节点")]
    [SerializeField] private TMP_Text progressText;
    [Tooltip("RuleBanner 内三块规则文本节点，按顺序绑定（仅运行时填充 .text，不创建节点）")]
    [SerializeField] private TMP_Text[] ruleTexts;
    [Tooltip("NextLevelButton 按钮")]
    [SerializeField] private Button nextLevelButton;
    [Tooltip("NextLevelButton 上的文字节点（留空则在 Start 时从按钮自动获取）")]
    [SerializeField] private TMP_Text nextButtonText;

    [Header("反馈音频素材（拖入 Assets/Audio 下对应 mp3；启动时注入 FeedbackManager 实现预加载）")]
    [Tooltip("单击/滑动排除音效")]
    [SerializeField] private AudioClip excludeClip;
    [Tooltip("双击解锁成功音效")]
    [SerializeField] private AudioClip successClip;
    [Tooltip("双击解锁失败/点错音效")]
    [SerializeField] private AudioClip errorClip;
    [Tooltip("通关音效")]
    [SerializeField] private AudioClip finishClip;
    [Tooltip("关卡失败音效（血量归零），对称 finishClip，经 FeedbackManager.Fail() 播放")]
    [SerializeField] private AudioClip failClip;

    [Header("生命值与失败弹窗（Inspector 拖拽绑定）")]
    [Tooltip("关卡生命值控制器（挂载在场景中，持有 3 个血点 Image）")]
    [SerializeField] private LivesController livesController;
    [Tooltip("失败弹窗（ModalPopup 预制体摆放在场景中，默认隐藏）")]
    [SerializeField] private ModalPopup failPopup;

    private List<BlockController> allBlocks = new List<BlockController>();
    private int currentCatCount; // 当前已锁定小猫数（given + 双击锁定），替代 FindAll 实时遍历，O(1) 读取
    // 对象池：切换关卡时不销毁格子，回收入池复用，避免高频 Instantiate/Destroy 造成内存碎片与耗时
    private readonly Queue<BlockController> blockPool = new Queue<BlockController>();

    [Header("交互配置")]
    [Tooltip("双击判定的时间阈值（秒），两次点击间隔小于此值视为双击")]
    public float doubleClickThreshold = 0.3f;
    [Tooltip("双击错误格子时状态栏错误提示的持续时长（秒）")]
    public float errorHintDuration = 1.5f;
    [Tooltip("血量归零后，延迟多久弹出失败弹窗（秒）。应大于错误抖动时长(0.4s)，让最后一次犯错的单格错误反馈（抖动/红框/音效/HUD文案）先播完再弹窗")]
    public float failPopupDelay = 0.5f;

    // ====== 单击/双击/滑动 协调状态 ======
    private bool isPointerDown;            // 当前是否处于按下（可能滑动）状态
    private bool hasSlid;                  // 本次按下是否已滑到其他格子（滑动收尾时据此抑制 click）
    private bool swipeTargetState;         // 本次基准状态：起始格翻转后的目标 hasCross，单击应用 / 滑动覆盖
    private BlockController lastSlidBlock; // 防抖缓存：最近处理过的滑动格，同格只处理一次
    private BlockController pendingToggleBlock; // 按下后待应用的 cross 翻转格（延迟判定期内尚未显示，双击时取消以杜绝闪烁）
    private Coroutine pendingToggleRoutine;     // 延迟应用 cross 翻转的协程
    private Coroutine errorHintRoutine;    // 当前错误提示协程（用于重启时先停掉）
    private Coroutine failPopupRoutine;    // 延迟弹出失败弹窗的协程（待单格错误反馈播完再弹，用于重启时先停掉）
    private bool isLevelCompleted;    // 本关已通关：起舞/按钮弹出期间锁定棋盘输入，避免制造新错误格与"已通关"冲突
    private bool isLevelFailed;       // 本关已失败：血量归零后锁定棋盘输入，与 isLevelCompleted 互斥（PRD §7.5）

    void Start()
    {
        // 顶部 UI 已改为预制体在场景中摆放，此处仅填充动态规则文本并校验引用，不再运行时拼装节点
        loadedLevels = LevelTableLoader.Load("levels");

        FillRuleTexts();
        EnsureNextButtonText();
        ValidateReferences();
        DisableUnneededRaycastTargets();

        // 注入音频素材到统一反馈中心（被本 MonoBehaviour 引用即随场景预加载，杜绝高频操作时的动态加载滞后）
        if (FeedbackManager.Instance != null)
            FeedbackManager.Instance.Init(excludeClip, successClip, errorClip, finishClip, failClip);

        // 订阅血量归零事件：LivesController 不反向依赖 GameManager，经事件解耦触发失败流程（PRD §7.1）
        if (livesController != null)
            livesController.OnLivesZero += HandleLevelFailed;

        if (loadedLevels == null || loadedLevels.Count == 0)
        {
            Debug.LogError("[GameManager] 无法从 CSV 中读取到任何有效的关卡数据，请检查 Resources 文件夹下的 levels.csv！");
            if (levelText != null) levelText.text = "No levels!";
            return;
        }

        // 绑定下一关按钮的点击事件
        if (nextLevelButton != null)
        {
            nextLevelButton.gameObject.SetActive(false); // 初始时隐藏下一关按钮
            nextLevelButton.onClick.RemoveAllListeners(); // 清理残留监听
            nextLevelButton.onClick.AddListener(LoadNextLevel);
        }

        // 默认加载 CSV 的第一关
        LoadLevel(currentLevelIndex);
    }

    // ================= 顶部 UI 引用初始化 =================

    /// <summary>
    /// 把 RulesLoader 中配置的规则文本填充到预制体上的 ruleTexts 节点。
    /// 仅设置 .text，不创建任何节点——节点结构已在预制体中摆好。
    /// </summary>
    private void FillRuleTexts()
    {
        if (ruleTexts == null) return;
        List<string> rules = RulesLoader.Load();
        for (int i = 0; i < ruleTexts.Length; i++)
        {
            if (ruleTexts[i] == null) continue;
            ruleTexts[i].text = i < rules.Count ? rules[i] : "";
        }
    }

    /// <summary>
    /// nextButtonText 未在 Inspector 绑定时，从 NextLevelButton 子节点兜底获取。
    /// </summary>
    private void EnsureNextButtonText()
    {
        if (nextButtonText == null && nextLevelButton != null)
            nextButtonText = nextLevelButton.GetComponentInChildren<TMP_Text>();
    }

    /// <summary>
    /// 校验必需的顶部 UI 引用是否已绑定，缺失则报错便于排查。
    /// 同时检查 gridParent / nextLevelButton 所在子 Canvas 是否带 GraphicRaycaster——
    /// 拆分子 Canvas 后若漏加 GraphicRaycaster，格子与按钮会静默失去点击响应。
    /// </summary>
    private void ValidateReferences()
    {
        if (gridParent == null) Debug.LogError("[GameManager] gridParent 未绑定！");
        if (blockPrefab == null) Debug.LogError("[GameManager] blockPrefab 未绑定！");
        if (levelText == null) Debug.LogError("[GameManager] levelText 未绑定（请按 TopUI_PrefabSpec.md 搭建预制体并拖拽）！");
        if (progressText == null) Debug.LogError("[GameManager] progressText 未绑定（请按 TopUI_PrefabSpec.md 搭建预制体并拖拽）！");
        if (nextLevelButton == null) Debug.LogError("[GameManager] nextLevelButton 未绑定！");

        WarnIfNoRaycaster(gridParent, "gridParent");
        WarnIfNoRaycaster(nextLevelButton != null ? nextLevelButton.transform : null, "nextLevelButton");

        // 反馈音频素材缺失仅告警（不阻塞），触觉与交互逻辑仍可工作
        if (excludeClip == null) Debug.LogWarning("[GameManager] excludeClip 未绑定，滑动/单击将无声！");
        if (successClip == null) Debug.LogWarning("[GameManager] successClip 未绑定，解锁成功将无声！");
        if (errorClip == null) Debug.LogWarning("[GameManager] errorClip 未绑定，点错将无声！");
        if (finishClip == null) Debug.LogWarning("[GameManager] finishClip 未绑定，通关将无声！");
        if (failClip == null) Debug.LogWarning("[GameManager] failClip 未绑定，关卡失败将无声！");

        // 生命值与失败弹窗引用校验
        if (livesController == null) Debug.LogError("[GameManager] livesController 未绑定（请拖入挂有 LivesController 的场景对象）！");
        if (failPopup == null) Debug.LogError("[GameManager] failPopup 未绑定（请拖入 ModalPopup 预制体实例）！");
        WarnIfNoRaycaster(failPopup != null ? failPopup.transform : null, "failPopup");
    }

    // 子 Canvas 必须各自带 GraphicRaycaster 才能接收点击；缺失则报错指引补加。
    private void WarnIfNoRaycaster(Transform target, string label)
    {
        if (target == null) return;
        Canvas c = target.GetComponentInParent<Canvas>();
        if (c != null && c.GetComponent<GraphicRaycaster>() == null)
            Debug.LogError($"[GameManager] {label} 所在 Canvas \"{c.name}\" 缺少 GraphicRaycaster，该处 UI 将无法响应点击！请为该子 Canvas 添加 GraphicRaycaster 组件。");
    }

    /// <summary>
    /// 关闭静态文本/装饰背景的 RaycastTarget：这些节点不接收点击，却参与 GraphicRaycaster 的
    /// 每帧 raycast 遍历与排序，是高频滑动输入事件的无谓开销。改代码而非改预制体，可见可追溯、
    /// 不依赖场景文件序列化值（LevelText/ProgressText/RuleText/按钮子Text/Background 全部代码统一关闭）。
    /// 保留各 Canvas 的 GraphicRaycaster 组件作为兜底链路，仅去掉其下无交互 Graphic 的 RaycastTarget。
    /// </summary>
    private void DisableUnneededRaycastTargets()
    {
        if (levelText != null) levelText.raycastTarget = false;
        if (progressText != null) progressText.raycastTarget = false;
        if (ruleTexts != null)
        {
            for (int i = 0; i < ruleTexts.Length; i++)
                if (ruleTexts[i] != null) ruleTexts[i].raycastTarget = false;
        }
        // 按钮自身的 Graphic 必须保留 raycastTarget 以接收点击；仅关其子文字
        if (nextButtonText != null) nextButtonText.raycastTarget = false;

        // 全屏装饰背景：不接收点击，关闭其 RaycastTarget，让根 Canvas 的 raycaster 在输入事件中
        // 无可遍历 Graphic（兜底 raycaster 组件保留，零开销不破坏链路）。由 Inspector 绑定，不靠名字查找。
        if (background != null) background.raycastTarget = false;
    }

    /// <summary>
    /// 从某节点沿 parent 链回溯到最顶层 Canvas（子 Canvas 也算 Canvas，会被跳过）。
    /// <summary>
    /// 外部公开的关卡加载入口
    /// </summary>
    public void LoadLevel(int index)
    {
        // 开启协程，安全进行 UI 销毁与重建，彻底杜绝 OnGUIDepth 报错
        StartCoroutine(SafeLoadLevelRoutine(index));
    }

    /// <summary>
    /// 核心异步安全加载逻辑
    /// </summary>
    private IEnumerator SafeLoadLevelRoutine(int index)
    {
        // 如果超出了最大关卡，说明全通关了
        if (index >= loadedLevels.Count)
        {
            if (levelText != null) levelText.text = "All levels completed!";
            if (progressText != null) progressText.text = "";
            if (nextLevelButton != null) nextLevelButton.gameObject.SetActive(false);
            yield break; // 结束协程
        }

        // 回收当前格子入对象池（不销毁），交互瞬间失效，防止异步期间玩家连点
        foreach (BlockController bc in allBlocks)
        {
            if (bc == null) continue;
            bc.gameObject.SetActive(false);
            bc.transform.SetParent(null, false); // 脱离棋盘布局，避免 GridLayoutGroup 仍参与排布
            blockPool.Enqueue(bc);
        }
        allBlocks.Clear();
        currentCatCount = 0; // 重置计数器，等待 given 猫与玩家双击重新累加
        isLevelCompleted = false; // 新关卡开始，解锁棋盘输入
        isLevelFailed = false; // 复位失败锁，允许新关卡正常交互（PRD §7.5）
        // 重开/切关时复位血量并隐藏失败弹窗（restart 也走本路径，全新棋盘不保留失败前状态，PRD F16）
        if (livesController != null) livesController.ResetLives();
        if (failPopupRoutine != null) { StopCoroutine(failPopupRoutine); failPopupRoutine = null; } // 停掉待弹的延迟协程，避免重开后又弹出过时弹窗
        if (failPopup != null) failPopup.Hide();

        // 顺延一帧：让回收的 SetActive(false) 在本帧渲染前生效，再生成新关卡格子。
        // 用 yield return null（下一帧 Update 后）而非 WaitForEndOfFrame（帧末渲染后），
        // 后者触发时机极晚会打断 Batching 渲染引发 Spike；对象池后已无 OnGUIDepth 报错根源。
        yield return null;

        // 获取当前关卡的数据（数据源自刚刚解析出的 CSV 数据）
        LevelData currentLevel = loadedLevels[index];
        gridSize = currentLevel.gridSize; // 动态把关卡尺寸传给游戏

        // 隐藏下一关按钮，等赢了再显示
        if (nextLevelButton != null) nextLevelButton.gameObject.SetActive(false);

        // 转化地图数据
        int totalCells = gridSize * gridSize;
        int[] currentMap = new int[totalCells];
        for (int i = 0; i < totalCells; i++)
        {
            if (i < currentLevel.mapData.Length)
                currentMap[i] = currentLevel.mapData[i];
            else
                currentMap[i] = 0;
        }

        // UI自适应
        GridLayoutGroup gridLayout = gridParent.GetComponent<GridLayoutGroup>();
        RectTransform boardRect = gridParent.GetComponent<RectTransform>();

        if (gridLayout != null && boardRect != null)
        {
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = gridSize;

            float totalW = boardRect.rect.width;
            float totalH = boardRect.rect.height;
            float paddingX = gridLayout.padding.left + gridLayout.padding.right;
            float paddingY = gridLayout.padding.top + gridLayout.padding.bottom;

            float currentSpaceX = baseSpacing.x;
            float currentSpaceY = baseSpacing.y;

            if (autoScaleSpacing)
            {
                float scaleFactor = 4f / gridSize;
                currentSpaceX = Mathf.Max(2f, baseSpacing.x * scaleFactor);
                currentSpaceY = Mathf.Max(2f, baseSpacing.y * scaleFactor);
            }
            gridLayout.spacing = new Vector2(currentSpaceX, currentSpaceY);

            float usableW = totalW - paddingX - (currentSpaceX * (gridSize - 1));
            float usableH = totalH - paddingY - (currentSpaceY * (gridSize - 1));

            gridLayout.cellSize = new Vector2(usableW / gridSize, usableH / gridSize);
        }

        // 正解：优先读取离线烘焙的 solutionData（O(1)）；缺失则回退运行时解算并告警
        bool[] solution = currentLevel.solutionData;
        if (solution == null)
        {
            Debug.LogWarning($"第 {index + 1} 关未烘焙 solution，运行时回退解算（建议用 Tools/Meowdoku/烘焙关卡正解 预先烘焙）。");
            solution = MapSolver.Solve(gridSize, currentMap, currentLevel.givenCats, palette != null ? palette.Length : 0);
        }
        if (solution == null)
        {
            Debug.LogError($"第 {index + 1} 关(CSV行)地图无解！");
        }

        // 生成格子：优先从对象池复用，仅池空时才 Instantiate 新格子
        for (int i = 0; i < totalCells; i++)
        {
            int r = i / gridSize;
            int c = i % gridSize;

            BlockController bc;
            if (blockPool.Count > 0)
            {
                bc = blockPool.Dequeue();
                bc.transform.SetParent(gridParent, false);
                bc.gameObject.SetActive(true);
            }
            else
            {
                GameObject go = Instantiate(blockPrefab, gridParent);
                go.transform.localScale = Vector3.one;
                bc = go.GetComponent<BlockController>();
            }

            Color cellColor = (currentMap[i] < palette.Length) ? palette[currentMap[i]] : Color.white;
            bc.Setup(r, c, currentMap[i], cellColor, this); // Setup 内部重置 hasCat/hasCross/isGiven/isCorrect

            bc.isCorrect = solution != null && solution[i];
            allBlocks.Add(bc);
        }

        // 放置关卡初始给出的 given 小猫（锁定不可取消）
        // 注意：必须在 bc.Setup 之后执行，因为 Setup 末尾会按 hasCat 重置猫的显示状态
        // 解算器已把 given 作为约束，故 given 格的 isCorrect 必为 true，显示猫与正解自洽
        if (currentLevel.givenCats != null)
        {
            foreach (int idx in currentLevel.givenCats)
            {
                if (idx < 0 || idx >= allBlocks.Count) continue;
                BlockController gb = allBlocks[idx];
                gb.hasCat = true;
                gb.isGiven = true;
                gb.ApplyVisualState(); // given 猫载入时无声显示，不播破壳动画
                currentCatCount++; // given 猫计入已锁定数
            }
        }

        UpdateStatusText();
    }

    // ================= 单击/双击/滑动 交互协调 =================
    // 延迟判定模型：按下时计算目标 cross 状态但暂不显示，等双击阈值过后无第二次按下才应用（单击出叉）。
    // 双击锁猫时取消未显示的翻转 → 无闪烁。滑动开始时立即应用起始格翻转（滑动起始格同步 2.1），随后刷墙式覆盖。

    public void HandlePointerDown(BlockController block)
    {
        if (block == null) return;
        // 本关已通关/已失败：终态期间锁定棋盘输入，不处理任何状态变更，
        // 仅给一致的轻触反馈（与锁定格点击一致），避免终态后制造新错误格与状态冲突（PRD §7.5）
        if (isLevelCompleted || isLevelFailed)
        {
            FeedbackManager.Instance?.Tap();
            return;
        }
        // 已锁定猫的格子（given 或双击锁定）/ 错误锁定格：不改变状态，仅给一致的轻触反馈，
        // 保证玩家在棋盘上任何一次点击都收到统一的 Selection 触觉
        if (block.hasCat || block.isErrorLocked)
        {
            FeedbackManager.Instance?.Tap();
            return;
        }

        isPointerDown = true;
        hasSlid = false;
        lastSlidBlock = block;

        // 双击判定（锁猫）：该格仍有未应用的待翻转（即上一次按下在阈值内）→ 第二次按下，触发双击
        if (pendingToggleBlock == block && pendingToggleRoutine != null)
        {
            CancelPendingToggle(); // 取消未显示的 cross 翻转，杜绝双击锁猫时的闪烁
            ExecuteDoubleClick(block);
            return;
        }

        // 单击：计算目标状态，延迟应用（阈值内若再来一次同格按下则升级为双击）
        // 上一格若有未决的待翻转（必为不同格——同格双击已在上面处理），先提交它；
        // 否则连续单击多个不同格时前面格子的翻转会被丢弃，只有最后格翻转
        CommitPendingToggle();
        swipeTargetState = !block.hasCross;
        pendingToggleBlock = block;
        pendingToggleRoutine = StartCoroutine(ApplyPendingToggle(block, swipeTargetState));

        // 触控响应第一帧：仅触觉（轻刻度）立即发射，保证每次点击反馈一致。
        // excludeClip 音频不在此刻播——延迟到"确认单击（阈值后）或滑动提交叉号"时才与叉号同步播放；
        // 否则双击的第一次按下会先响"嗒"且收不回，违背"双击只播 success/error"的需求。
        FeedbackManager.Instance?.Tap();
    }

    public void HandlePointerUp(BlockController block)
    {
        isPointerDown = false;
    }

    // 按下后拖动划过他格：刷墙式覆盖（已锁定猫格跳过，同格防抖跳过）
    public void HandlePointerEnter(BlockController block)
    {
        if (!isPointerDown || block == null) return;
        if (block.hasCat) return;
        if (block == lastSlidBlock) return; // 去重防抖：手指在本格内摩擦不重复处理

        hasSlid = true; // 标记本次为滑动，松开时的 click 将被抑制

        // 滑动起始：立即提交起始格的待翻转（滑动起始格状态同步 2.1），不再等延迟
        CommitPendingToggle();

        // 刷墙式覆盖：强制覆盖为基准状态，杜绝滑过区域叉号交替闪烁
        bool changed = block.hasCross != swipeTargetState; // 仅状态成功改变时才反馈，避免无效"嗒"
        block.hasCross = swipeTargetState;
        block.ApplyVisualState();
        if (changed) // 仅叉号真正翻转时播出拳动画 + 音频反馈
        {
            block.PlayCrossPunch();
            FeedbackManager.Instance?.Exclude();
        }
        lastSlidBlock = block;
    }

    public void HandlePointerClick(BlockController block)
    {
        // 滑动收尾：松开触发的 click 不再作为有效点击（翻转已由延迟协程或滑动处理）
        if (hasSlid)
        {
            hasSlid = false;
            return;
        }
        // 纯单击：cross 翻转由 pendingToggleRoutine 在阈值后应用；双击由 HandlePointerDown 检测。此处无需动作。
    }

    // 延迟应用 cross 翻转：阈值内未被双击取消则确认为单击，显示 cross 并配音
    private IEnumerator ApplyPendingToggle(BlockController block, bool target)
    {
        yield return new WaitForSeconds(doubleClickThreshold);
        pendingToggleRoutine = null;
        if (pendingToggleBlock != block) yield break; // 已被后续操作取代

        block.hasCross = target;
        block.ApplyVisualState();
        if (target) block.PlayCrossPunch(); // 确认出叉时播出拳动画
        pendingToggleBlock = null;
        // 叉号此刻才显示，excludeClip 同步播放（触觉已在按下第一帧给过，此处只补音频）
        FeedbackManager.Instance?.ExcludeAudioOnly();
    }

    private void CancelPendingToggle()
    {
        if (pendingToggleRoutine != null)
        {
            StopCoroutine(pendingToggleRoutine);
            pendingToggleRoutine = null;
        }
        pendingToggleBlock = null;
    }

    // 提交待决翻转：立即兑现未显示的 cross 翻转（转向他格或滑动起始时，兑现前一次单击）。
    // 与 CancelPendingToggle 的区别：前者应用翻转（出叉），后者丢弃（双击锁猫时不显示 cross）。
    private void CommitPendingToggle()
    {
        if (pendingToggleRoutine == null) return; // 无待决翻转（从未挂起或已自然到期应用）
        BlockController pb = pendingToggleBlock;
        StopCoroutine(pendingToggleRoutine);
        pendingToggleRoutine = null;
        pendingToggleBlock = null;
        pb.hasCross = !pb.hasCross; // 待翻转尚未应用，hasCross 仍为翻转前值，取反即目标状态
        pb.ApplyVisualState();
        if (pb.hasCross) pb.PlayCrossPunch(); // 兑现出叉时播出拳动画
        // 立即兑现叉号，excludeClip 同步播放（触觉已在按下第一帧给过，此处只补音频）
        FeedbackManager.Instance?.ExcludeAudioOnly();
    }

    // 双击：已锁定→忽略；正解格→锁定小猫；错误格→错误提示
    private void ExecuteDoubleClick(BlockController block)
    {
        if (block == null || block.hasCat) return;

        if (block.isCorrect)
        {
            // 锁定小猫：显示猫并播破壳弹跳 0->1.25->1.0，清除可能存在的排除标记
            block.hasCat = true;
            block.hasCross = false;
            block.ApplyVisualState();
            block.PlayCatPop();
            currentCatCount++; // 新锁定一只正解猫
            FeedbackManager.Instance?.Success(); // 成功音效 + 触觉，与猫咪显现同帧
            CheckRules();
        }
        else
        {
            // 双击非小猫格子：错误反馈
            // 打上叉号并保留至关卡结束（该格已被锁定，不会被后续操作取消）
            // 仅 ApplyVisualState 静默显示叉号——不调 PlayCrossPunch，避免出拳动画与抖动叠加
            block.hasCross = true;
            block.ApplyVisualState();
            block.PlayErrorShake();  // 横向正弦衰减抖动
            block.LockAsError();     // 边框染红 + 本关锁定，不再响应点击
            ShowErrorHint();         // HUD 强切 "Not Cat!!!" 并放大回弹
            FeedbackManager.Instance?.Error(); // 错误音效 + 触觉
            // 扣血放最后：最后一次犯错的完整单格错误反馈已播完，再扣血/触发失败（PRD F7）。
            // LoseLife 内部归零时经 OnLivesZero 事件触发 HandleLevelFailed，本处不直接判失败。
            if (livesController != null) livesController.LoseLife();
        }
    }

    // 错误提示：ProgressText 临时显示并放大回弹，随后恢复进度文本
    private void ShowErrorHint()
    {
        if (progressText != null)
        {
            progressText.text = "Not Cat!!!";
            TweenRunner.TextPunch(progressText.transform); // 顶部 HUD 文本放大回弹
            if (errorHintRoutine != null) StopCoroutine(errorHintRoutine);
            errorHintRoutine = StartCoroutine(RestoreStatusAfter(errorHintDuration));
        }
    }

    private IEnumerator RestoreStatusAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        UpdateStatusText();
        errorHintRoutine = null;
    }

    public void LoadNextLevel()
    {
        currentLevelIndex++;
        LoadLevel(currentLevelIndex);
    }

    // ================= 关卡失败与复活流程（PRD §3.3 / §3.5 / §4.2-4.4）=================

    /// <summary>
    /// 血量归零触发（由 LivesController.OnLivesZero 事件调用）：
    /// 锁棋盘 → 取消进行中协程 → 失败音效 → 显示失败弹窗。
    /// 与 CheckRules 胜利分支对称，二者经 isLevelCompleted/isLevelFailed 互斥（PRD §7.5）。
    /// </summary>
    private void HandleLevelFailed()
    {
        isLevelFailed = true; // 锁棋盘输入（HandlePointerDown 已拦截）

        // 取消进行中的交互协程，避免失败时残留补间/提示与弹窗打架（PRD F10）
        CancelPendingToggle();
        if (errorHintRoutine != null)
        {
            StopCoroutine(errorHintRoutine);
            errorHintRoutine = null;
        }

        // 失败音效 + 触觉立即播（与单格 Error 叠加，PRD F19/§6）
        FeedbackManager.Instance?.Fail();

        // 延迟显示失败弹窗：等最后一次犯错的单格错误反馈（抖动 0.4s / 红框 / 音效 / HUD"Not Cat!!!"）播完再弹，
        // 避免弹窗瞬间盖住反馈。延迟期间 isLevelFailed 已锁棋盘，玩家完整看到最后一次犯错的反馈。
        if (failPopup != null)
        {
            var config = new ModalConfig("Level Failed", new List<ModalButtonDef>
            {
                new ModalButtonDef("3 more lives", OnReviveRequested),
                new ModalButtonDef("restart", OnRestartRequested),
            });
            if (failPopupRoutine != null) StopCoroutine(failPopupRoutine);
            failPopupRoutine = StartCoroutine(ShowFailPopupAfter(failPopupDelay, config));
        }
    }

    /// <summary>延迟弹出失败弹窗：待单格错误反馈播完（failPopupDelay，默认 0.5s）再 Show。</summary>
    private IEnumerator ShowFailPopupAfter(float delay, ModalConfig config)
    {
        yield return new WaitForSeconds(delay);
        failPopupRoutine = null;
        if (failPopup != null) failPopup.Show(config);
    }

    /// <summary>restart 按钮：重开本关，全新棋盘不保留失败前状态（PRD F16）。
    /// LoadLevel 内部会 ResetLives、复位 isLevelFailed、隐藏弹窗。</summary>
    private void OnRestartRequested()
    {
        LoadLevel(currentLevelIndex);
    }

    /// <summary>3 more lives 按钮：走广告接口，发放奖励后补血+保留棋盘继续（PRD F17）。
    /// 本期 NullAdProvider 直接调 onRewarded；后续接真实 SDK 调用方零改动。
    /// 失败/取消则弹窗保持打开，玩家可再次选择。</summary>
    private void OnReviveRequested()
    {
        AdManager.Instance?.ShowRewardedAd(
            onRewarded: () =>
            {
                if (failPopup != null) failPopup.Hide();
                isLevelFailed = false; // 解锁棋盘输入
                if (livesController != null) livesController.RefillLives(); // 补血满，复位归零节流
                // 保留棋盘所有状态（猫 / cross / 错误锁定格），玩家从当前进度继续（PRD F17/F18）
            },
            onFailedOrClosed: () =>
            {
                // 广告未发放奖励：弹窗保持打开，玩家可再次选择 restart 或 3 more lives
            });
    }

    // 失败弹窗显示时屏蔽 Android 物理返回键 / Escape：玩家只能点 restart / 3 more lives，
    // 避免"血量 0、棋盘锁定、弹窗消失"的死局（PRD F15.1）
    private void Update()
    {
        if (isLevelFailed && Input.GetKeyDown(KeyCode.Escape))
        {
            // 吞掉，不做任何操作
        }
    }

    // 通关后延时 0.5s 给视觉缓冲，再让下一关按钮弹性亮相；亮相后才恢复可点击
    private IEnumerator RevealNextButtonAfter(float delay, bool interactableAfter)
    {
        yield return new WaitForSeconds(delay);
        if (nextLevelButton == null) yield break;
        nextLevelButton.interactable = interactableAfter;
        TweenRunner.ElasticButtonPop(nextLevelButton.transform); // 0.0->1.25->0.90->1.0
    }

    // ================= 规则校验与状态刷新 =================
    // 双击锁猫后 currentCatCount 与 ApplyVisualState 均已同步完成，无需等帧；
    // 直接同步判定胜利与刷新状态，剥离原先的延迟协程依赖（消除 WaitForEndOfFrame 引发的 Batching 中断）。
    void CheckRules()
    {
        // 已锁定的小猫数（given + 双击锁定；错误格无法锁猫，故达标即所有正解猫被点出）
        int correctCount = currentCatCount;

        // 胜利条件：所有正解小猫被双击点出（hasCat 数 == gridSize）；排除标记不影响通关
        if (correctCount == gridSize)
        {
            isLevelCompleted = true; // 锁定棋盘输入：起舞/按钮弹出期间不再处理状态变更
            bool isLastLevel = currentLevelIndex >= loadedLevels.Count - 1;
            if (progressText != null)
                progressText.text = isLastLevel ? "All levels completed!" : $"Level {currentLevelIndex + 1} completed!";

            // 通关音效 + 触觉
            FeedbackManager.Instance?.Finish();

            // 猫咪集体起舞：遍历全盘所有已点出的猫咪，同步波浪往复跳跃（LocalY 0->30->0）
            var dancingCats = new List<Transform>();
            foreach (BlockController b in allBlocks)
            {
                if (b == null || !b.hasCat || b.cat == null) continue;
                dancingCats.Add(b.cat.transform);
            }
            TweenRunner.WaveJump(dancingCats);

            // 下一关按钮：先隐藏（缩放 0），延时 0.5s 视觉缓冲后弹性亮相
            if (nextLevelButton != null)
            {
                if (nextButtonText != null)
                    nextButtonText.text = isLastLevel ? "All Complete!" : $"Level {currentLevelIndex + 2}";
                nextLevelButton.interactable = false; // 弹出动画期间禁用，防误触
                nextLevelButton.gameObject.SetActive(true);
                nextLevelButton.transform.localScale = Vector3.zero; // 瞬间强行设为 0
                StartCoroutine(RevealNextButtonAfter(0.5f, !isLastLevel));
            }
            return;
        }

        // 如果没赢，刷新普通状态文本
        UpdateStatusText(correctCount);
    }

    void UpdateStatusText()
    {
        UpdateStatusText(currentCatCount);
    }

    void UpdateStatusText(int correctCount)
    {
        if (levelText != null)
            levelText.text = $"Level {currentLevelIndex + 1}";
        if (progressText != null)
            progressText.text = $"Progress: {correctCount} / {gridSize}";
    }
}
