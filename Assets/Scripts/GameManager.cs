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
    [SerializeField] private bool autoScaleSpacing = true;
    [SerializeField] private Vector2 baseSpacing = new Vector2(20f, 20f);

    [Header("基础引用")]
    [SerializeField] private GameObject blockPrefab;
    [SerializeField] private Transform gridParent;
    [SerializeField] private Color[] palette;
    [Tooltip("全屏装饰背景（不接收点击，仅关闭其 RaycastTarget 减少输入遍历）。拖入场景根 Canvas 下的 Background 节点")]
    [SerializeField] private Image background;

    [Header("顶部UI引用（预制体摆放在场景后，Inspector 拖拽绑定）")]
    [Tooltip("最顶部 “Level xx” 文本节点")]
    [SerializeField] private TMP_Text levelText;
    [Tooltip("下方 “Progress: n / m” 文本节点")]
    [SerializeField] private TMP_Text progressText;
    [Tooltip("RuleBanner 内三块规则文本节点，按顺序绑定（仅运行时填充 .text，不创建节点）")]
    [SerializeField] private TMP_Text[] ruleTexts;
    // 规则文案 i18n 键，按顺序对应 ruleTexts[0/1/2]
    private static readonly string[] RuleKeys = { "rule.color", "rule.rowcol", "rule.no_touch" };
    [Tooltip("规则横幅容器（含背景），引导关隐藏。留空则仅隐藏 ruleTexts 文本")]
    [SerializeField] private Transform ruleBanner;

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
    [Tooltip("通关结算弹窗（LevelCompletePopup 预制体摆放在场景中，默认隐藏；下一关按钮位于其面板内）")]
    [SerializeField] private LevelCompletePopup completePopup;
    [Tooltip("魔法棒道具按钮（棋盘下方，关卡过程中常驻展示），点击自动填入一个正解猫格")]
    [SerializeField] private Button magicWandButton;
    [Tooltip("提示道具按钮（棋盘下方，与魔法棒并列），按优先级给填入/打叉/取消叉提示")]
    [SerializeField] private Button tipButton;
    [Tooltip("新手引导关控制器（挂场景中，level 0 时启用）")]
    [SerializeField] private TutorialController tutorialController;
    [Tooltip("欢迎页控制器（启动时显示 slogan+加载进度条+游客ID，完成后进关卡）")]
    [SerializeField] private WelcomeController welcomeController;

    [Header("道具数量与红点（Inspector 拖拽）")]
    [Tooltip("每关初始道具数量（魔法棒与提示各自独立计数）")]
    [SerializeField] private int initialItemCount = 5;
    [Tooltip("魔法棒按钮右上角红点：Image（圆形背景）+ 子 TMP_Text 显示数量。数量 0 时整体隐藏")]
    [SerializeField] private Image magicWandBadge;
    [SerializeField] private TMP_Text magicWandBadgeText;
    [Tooltip("提示道具按钮右上角红点")]
    [SerializeField] private Image tipBadge;
    [SerializeField] private TMP_Text tipBadgeText;

    [Header("阶段宝箱（每10关一轮）")]
    [Tooltip("每10关一个宝箱，宝箱内魔法棒数量（可配置为0即空箱，仍触发开箱动画）")]
    [SerializeField] private int chestRewardMagic = 1;
    [Tooltip("宝箱内提示道具数量（可配置为0）")]
    [SerializeField] private int chestRewardTip = 1;
    // 道具数量/红点/宝箱/广告门控集中托管（纯逻辑容器，Start 时注入初始数量与红点 UI 引用）
    private ItemInventory inventory;

    private List<BlockController> allBlocks = new List<BlockController>();
    // 复用缓冲（零 GC）：通关起舞猫集合 / 提示道具叉号集
    private readonly List<Transform> _dancingCats = new List<Transform>();
    private readonly HashSet<int> _crossed = new HashSet<int>();
    // 本关解集/锁猫/地图状态集中托管（纯逻辑容器 LevelState）
    private readonly LevelState levelState = new LevelState();
    internal bool isTutorial; // 新手引导关（level 0）：隐藏常规 UI，点击受 TutorialController 限制，通关由引导检测触发

    /// <summary>棋盘所有格子（引导关 TutorialController 查询/操作用）。</summary>
    internal List<BlockController> AllBlocks => allBlocks;
    /// <summary>当前关卡尺寸 N（引导关格索引换算用）。</summary>
    internal int GridSize => gridSize;
    // 对象池：切换关卡时不销毁格子，回收入池复用，避免高频 Instantiate/Destroy 造成内存碎片与耗时
    private BlockPool blockPool;

    [Header("交互配置")]
    [Tooltip("双击判定的时间阈值（秒），两次点击间隔小于此值视为双击")]
    [SerializeField] private float doubleClickThreshold = 0.3f;
    [Tooltip("双击错误格子时状态栏错误提示的持续时长（秒）")]
    [SerializeField] private float errorHintDuration = 1.5f;
    [Tooltip("血量归零后，延迟多久弹出失败弹窗（秒）。应大于错误抖动时长(0.4s)，让最后一次犯错的单格错误反馈（抖动/红框/音效/HUD文案）先播完再弹窗")]
    [SerializeField] private float failPopupDelay = 0.5f;
    [Tooltip("通关后，延迟多久弹出结算弹窗（秒）。应大于集体起舞时长 WaveJump≈0.48s，让猫咪起舞动画先播完再弹窗，避免弹窗瞬间盖住")]
    [SerializeField] private float completePopupDelay = 0.5f;

    // ====== 单击/双击/滑动 协调状态 ======
    private bool isPointerDown;            // 当前是否处于按下（可能滑动）状态
    private bool hasSlid;                  // 本次按下是否已滑到其他格子（滑动收尾时据此抑制 click）
    private bool swipeTargetState;         // 本次基准状态：起始格翻转后的目标 hasCross，单击应用 / 滑动覆盖
    private BlockController lastSlidBlock; // 防抖缓存：最近处理过的滑动格，同格只处理一次
    private BlockController pendingToggleBlock; // 按下后待应用的 cross 翻转格（延迟判定期内尚未显示，双击时取消以杜绝闪烁）
    private Coroutine pendingToggleRoutine;     // 延迟应用 cross 翻转的协程
    // WaitForSeconds 缓存：单击延迟判定每次点击都 yield，复用避免高频点击 GC（阈值变更时重建）
    private WaitForSeconds _doubleClickWait;
    private float _cachedDoubleClickThreshold = -1f;
    private Coroutine errorHintRoutine;    // 当前错误提示协程（用于重启时先停掉）
    private Coroutine failPopupRoutine;    // 延迟弹出失败弹窗的协程（待单格错误反馈播完再弹，用于重启时先停掉）
    private Coroutine completePopupRoutine; // 延迟弹出结算弹窗的协程（待集体起舞播完再弹，用于切关时先停掉）
    private bool isLevelCompleted;    // 本关已通关：起舞/按钮弹出期间锁定棋盘输入，避免制造新错误格与"已通关"冲突
    private bool isLevelFailed;       // 本关已失败：血量归零后锁定棋盘输入，与 isLevelCompleted 互斥（PRD §7.5）

    // 棋盘布局组件缓存（Start 一次取，避免每关 GetComponent）
    private GridLayoutGroup gridLayout;
    private RectTransform boardRect;

    void Start()
    {
        // 本地化文案表优先加载：后续 levelText/弹窗等所有 UI 文案取值依赖此表
        I18n.Init();
        // 缓存棋盘布局组件：切关时频繁读取 rect/cellSize，避免每关 GetComponent
        if (gridParent != null)
        {
            gridLayout = gridParent.GetComponent<GridLayoutGroup>();
            boardRect = gridParent.GetComponent<RectTransform>();
        }
        // 格子对象池（prefab + 棋盘父节点注入）
        blockPool = new BlockPool(blockPrefab, gridParent);
        // 从 CSV 加载全部关卡数据（仅 Start 一次，切关不重读）
        loadedLevels = LevelTableLoader.Load("levels");

        // 顶部 UI 由预制体在场景中摆放，此处仅填充动态规则文本并校验引用，不运行时拼装节点
        FillRuleTexts();
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
            if (levelText != null) levelText.text = I18n.Get("game.no_levels");
            return;
        }

        // 绑定魔法棒道具按钮：点击触发自动填入正解猫
        if (magicWandButton != null)
        {
            magicWandButton.onClick.RemoveAllListeners();
            magicWandButton.onClick.AddListener(UseMagicWand);
        }
        // 绑定提示道具按钮：按优先级给填入/打叉/取消叉提示
        if (tipButton != null)
        {
            tipButton.onClick.RemoveAllListeners();
            tipButton.onClick.AddListener(UseTip);
        }

        // 道具数量/红点/宝箱/广告门控容器：从存档恢复（首次玩家用 initialItemCount 种子，教程完成前视为首次）
        SaveSystem.Load();
        SaveSystem.EnsurePlayerId(); // 首次生成游客ID（GUID）并写档，欢迎页展示用
        bool fresh = !SaveSystem.Data.tutorialSeen;
        int mwCount = fresh ? initialItemCount : SaveSystem.Data.magicWandCount;
        int tipCnt = fresh ? initialItemCount : SaveSystem.Data.tipCount;
        int lsc = fresh ? 0 : SaveSystem.Data.levelsSinceChest;
        inventory = new ItemInventory(mwCount, tipCnt, lsc, chestRewardMagic, chestRewardTip,
            magicWandBadge, magicWandBadgeText, tipBadge, tipBadgeText);
        inventory.OnChanged += Persist; // 道具扣减/广告+1/宝箱进度变更触发写档
        currentLevelIndex = fresh ? 0 : Mathf.Clamp(SaveSystem.Data.currentLevel, 0, loadedLevels.Count - 1);

        // 欢迎页（slogan+加载进度条+游客ID）覆盖显示，完成后进起始关卡（首次教程 / 老玩家上次关）
        if (welcomeController != null)
        {
            welcomeController.MarkReady(); // 同步初始化已完成（未来 SDK 异步加载时改在此前置位）
            welcomeController.Show(OnWelcomeComplete);
        }
        else LoadLevel(currentLevelIndex); // 无欢迎页兜底直进
    }

    /// <summary>欢迎页播放完毕回调：先启动关卡加载（协程），再渐出欢迎页——棋盘在 welcome 之下生成，
    /// 渐出期间 sortingOrder 仍为 2000 盖住棋盘生成过程，避免白屏。</summary>
    private void OnWelcomeComplete()
    {
        LoadLevel(currentLevelIndex);
        if (welcomeController != null) welcomeController.Hide();
    }

    /// <summary>把 live 状态写回存档并保存。resumeLevel 为"下次启动应恢复的关卡"：
    /// 通常= currentLevelIndex（切关/道具变更）；通关时= 下一关（min(currentLevelIndex+1, Count-1)），避免重启重玩刚通关的关。
    /// tutorialSeen 由 TutorialController 写入。</summary>
    private void Persist() => Persist(currentLevelIndex);

    private void Persist(int resumeLevel)
    {
        SaveSystem.Data.currentLevel = resumeLevel;
        if (resumeLevel > SaveSystem.Data.highestUnlocked) SaveSystem.Data.highestUnlocked = resumeLevel;
        SaveSystem.Data.magicWandCount = inventory.MagicWandCount;
        SaveSystem.Data.tipCount = inventory.TipCount;
        SaveSystem.Data.levelsSinceChest = inventory.LevelsSinceChest;
        SaveSystem.Save();
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

    // ================= 顶部 UI 引用初始化 =================

    /// <summary>
    /// 把规则文案填充到预制体上的 ruleTexts 节点（按顺序对应 RuleBlock0/1/2）。
    /// 文案走 I18n 本地化表，仅设置 .text，不创建任何节点——节点结构已在预制体中摆好。
    /// </summary>
    private void FillRuleTexts()
    {
        if (ruleTexts == null) return;
        for (int i = 0; i < ruleTexts.Length; i++)
        {
            if (ruleTexts[i] == null) continue;
            ruleTexts[i].text = i < RuleKeys.Length ? I18n.Get(RuleKeys[i]) : "";
        }
    }

    /// <summary>
    /// 校验必需的顶部 UI 引用是否已绑定，缺失则报错便于排查。
    /// 同时检查 gridParent / 各弹窗所在子 Canvas 是否带 GraphicRaycaster——
    /// 拆分子 Canvas 后若漏加 GraphicRaycaster，格子与按钮会静默失去点击响应。
    /// </summary>
    private void ValidateReferences()
    {
        if (gridParent == null) Debug.LogError("[GameManager] gridParent 未绑定！");
        if (blockPrefab == null) Debug.LogError("[GameManager] blockPrefab 未绑定！");
        if (levelText == null) Debug.LogError("[GameManager] levelText 未绑定（请按 TopUI_PrefabSpec.md 搭建预制体并拖拽）！");
        if (progressText == null) Debug.LogError("[GameManager] progressText 未绑定（请按 TopUI_PrefabSpec.md 搭建预制体并拖拽）！");

        WarnIfNoRaycaster(gridParent, "gridParent");

        // 反馈音频素材缺失仅告警（不阻塞），触觉与交互逻辑仍可工作
        if (excludeClip == null) Debug.LogWarning("[GameManager] excludeClip 未绑定，滑动/单击将无声！");
        if (successClip == null) Debug.LogWarning("[GameManager] successClip 未绑定，解锁成功将无声！");
        if (errorClip == null) Debug.LogWarning("[GameManager] errorClip 未绑定，点错将无声！");
        if (finishClip == null) Debug.LogWarning("[GameManager] finishClip 未绑定，通关将无声！");
        if (failClip == null) Debug.LogWarning("[GameManager] failClip 未绑定，关卡失败将无声！");

        // 生命值与弹窗引用校验
        if (livesController == null) Debug.LogError("[GameManager] livesController 未绑定（请拖入挂有 LivesController 的场景对象）！");
        if (failPopup == null) Debug.LogError("[GameManager] failPopup 未绑定（请拖入 ModalPopup 预制体实例）！");
        if (completePopup == null) Debug.LogError("[GameManager] completePopup 未绑定（请拖入 LevelCompletePopup 预制体实例）！");
        if (magicWandButton == null) Debug.LogError("[GameManager] magicWandButton 未绑定（请拖入棋盘下方的魔法棒道具按钮）！");
        if (tipButton == null) Debug.LogError("[GameManager] tipButton 未绑定（请拖入棋盘下方的提示道具按钮）！");
        if (magicWandBadge == null) Debug.LogWarning("[GameManager] magicWandBadge 未绑定，魔法棒数量红点不显示！");
        if (tipBadge == null) Debug.LogWarning("[GameManager] tipBadge 未绑定，提示道具数量红点不显示！");
        WarnIfNoRaycaster(failPopup != null ? failPopup.transform : null, "failPopup");
        WarnIfNoRaycaster(completePopup != null ? completePopup.transform : null, "completePopup");
        WarnIfNoRaycaster(magicWandButton != null ? magicWandButton.transform : null, "magicWandButton");
        WarnIfNoRaycaster(tipButton != null ? tipButton.transform : null, "tipButton");
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
        // 下一关按钮文字由结算弹窗（LevelCompletePopup）内部管理，此处不处理

        // 魔法棒/提示道具按钮：按钮自身 Graphic（RoundedImage 圆形背景，Button.targetGraphic）保留 raycastTarget
        // 以接收点击；其子节点（图标 Image / 文字）不接收点击，关闭 raycastTarget 减少输入遍历开销
        DisableButtonChildRaycast(magicWandButton);
        DisableButtonChildRaycast(tipButton);

        // 道具红点文字（纯展示）关闭 raycastTarget，减少输入遍历
        if (magicWandBadgeText != null) magicWandBadgeText.raycastTarget = false;
        if (tipBadgeText != null) tipBadgeText.raycastTarget = false;

        // 全屏装饰背景：不接收点击，关闭其 RaycastTarget，让根 Canvas 的 raycaster 在输入事件中
        // 无可遍历 Graphic（兜底 raycaster 组件保留，零开销不破坏链路）。由 Inspector 绑定，不靠名字查找。
        if (background != null) background.raycastTarget = false;
    }

    /// <summary>切换常规关卡 UI 显隐：引导关(level 0)隐藏 levelText/progressText/RuleBanner/血点/道具，仅留棋盘。
    /// 血点通过 livesController.lifeIcons 逐个隐藏（LivesController 节点与血点容器分离）。</summary>
    internal void HideLevelUI(bool hide)
    {
        if (levelText != null) levelText.gameObject.SetActive(!hide);
        if (progressText != null) progressText.gameObject.SetActive(!hide);
        if (ruleBanner != null) ruleBanner.gameObject.SetActive(!hide);
        if (livesController != null && livesController.lifeIcons != null)
            foreach (var icon in livesController.lifeIcons)
                if (icon != null) icon.gameObject.SetActive(!hide);
        if (magicWandButton != null) magicWandButton.gameObject.SetActive(!hide);
        if (tipButton != null) tipButton.gameObject.SetActive(!hide);
    }

    /// <summary>关闭按钮子节点 Image 的 raycastTarget，保留按钮自身 targetGraphic 接收点击。</summary>
    private void DisableButtonChildRaycast(Button btn)
    {
        if (btn == null) return;
        var target = btn.targetGraphic;
        foreach (var img in btn.GetComponentsInChildren<Image>())
        {
            if (img == target) continue; // 按钮自身 Graphic 保留
            img.raycastTarget = false;
        }
    }

    /// <summary>外部公开的关卡加载入口。</summary>
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
            if (levelText != null) levelText.text = I18n.Get("game.all_completed");
            if (progressText != null) progressText.text = "";
            if (completePopup != null) completePopup.Hide();
            yield break; // 结束协程
        }

        // 回写当前关卡索引：LoadLevel(index) 必须同步 currentLevelIndex，否则
        // 教程→第1关（OnStartGame 调 LoadLevel(1)）后 currentLevelIndex 仍为 0，
        // 第1关失败点 restart 会 LoadLevel(0) 误回教程。越界路径上方已 yield break，不会污染。
        currentLevelIndex = index;

        // 回收当前格子入对象池（不销毁），交互瞬间失效，防止异步期间玩家连点
        foreach (BlockController bc in allBlocks)
        {
            if (bc == null) continue;
            blockPool.Release(bc); // 失活 + 脱离棋盘布局 + 入池
        }
        allBlocks.Clear();
        levelState.Reset(); // 重置解集/锁猫/地图状态，等待下方 Init 重新填充
        isLevelCompleted = false; // 新关卡开始，解锁棋盘输入
        isLevelFailed = false; // 复位失败锁，允许新关卡正常交互（PRD §7.5）
        // 引导关判定：level 0 隐藏常规 UI，启动引导流程；非引导关恢复 UI
        isTutorial = (index == 0);
        HideLevelUI(isTutorial);
        if (!isTutorial && tutorialController != null) tutorialController.EndTutorial(); // 离开引导关清理
        // 道具数量跨关继承（不每关重置）；按钮恢复可点击（通关时被锁），刷新红点显示当前数量
        if (magicWandButton != null) magicWandButton.interactable = true;
        if (tipButton != null) tipButton.interactable = true;
        inventory.RefreshBadges();
        // 重开/切关时复位血量并隐藏弹窗（restart 也走本路径，全新棋盘不保留失败前状态，PRD F16）
        if (livesController != null) livesController.ResetLives();
        if (failPopupRoutine != null) { StopCoroutine(failPopupRoutine); failPopupRoutine = null; } // 停掉待弹的延迟协程，避免重开后又弹出过时弹窗
        if (failPopup != null) failPopup.Hide();
        if (completePopupRoutine != null) { StopCoroutine(completePopupRoutine); completePopupRoutine = null; } // 停掉待弹的延迟协程，避免切关后又弹出过时结算弹窗
        if (completePopup != null) completePopup.Hide();

        // 顺延一帧：让回收的 SetActive(false) 在本帧渲染前生效，再生成新关卡格子。
        // 用 yield return null（下一帧 Update 后）而非 WaitForEndOfFrame（帧末渲染后），
        // 后者触发时机极晚会打断 Batching 渲染引发 Spike。
        yield return null;

        // 获取当前关卡的数据（数据源自刚刚解析出的 CSV 数据）
        LevelData currentLevel = loadedLevels[index];
        gridSize = currentLevel.gridSize; // 动态把关卡尺寸传给游戏

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

        // UI自适应（gridLayout/boardRect 已在 Start 缓存）
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

        // 本关全部合法解（多解支持）：双击沿解判定 / 通关校验 / 魔法棒选格共用。
        // 优先读 CSV 烘焙的全解集（EnumerateAll 离线烘焙，运行时零解算）；未烘焙才回退运行时解算并告警。
        // 烘焙用与运行时相同的 EnumerateAll（同算法同 cap 同顺序），故全解集等价，多解行为不变。
        int paletteLen = palette != null ? palette.Length : 0;
        List<bool[]> solutions = (currentLevel.solutions != null && currentLevel.solutions.Count > 0)
            ? currentLevel.solutions
            : MapSolver.EnumerateAll(gridSize, currentMap, currentLevel.givenCats, paletteLen);
        if (solutions == null || solutions.Count == 0)
        {
            Debug.LogError($"第 {index + 1} 关(CSV行)地图无解！");
        }
        else if (currentLevel.solutions == null || currentLevel.solutions.Count == 0)
        {
            Debug.LogWarning($"第 {index + 1} 关未烘焙 solution，运行时回退 EnumerateAll。请用 Tools/Meowdoku/烘焙关卡正解 后发布。");
        }
        // 初始化关卡状态：托管 map/solutions/locked，并预计算解集并集（避免每格扫描）
        levelState.Init(gridSize, currentMap, solutions, totalCells);

        // 生成格子：优先从对象池复用，仅池空时才 Instantiate 新格子
        for (int i = 0; i < totalCells; i++)
        {
            int r = i / gridSize;
            int c = i % gridSize;

            BlockController bc = blockPool.Acquire();

            Color cellColor = (currentMap[i] < palette.Length) ? palette[currentMap[i]] : Color.white;
            bc.Setup(r, c, currentMap[i], cellColor, this); // Setup 内部重置 hasCat/hasCross/isGiven/isCorrect

            // isCorrect = 该格属于至少一个合法解（解集并集）。仅供调试/未来视觉提示；
            // 双击判定用 CanLockAlongSolution（沿解可锁），不读 isCorrect。
            bc.isCorrect = levelState.IsInUnion(i);
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
                levelState.LockCat(idx); // given 已锁，计入已锁数与阵营/沿解校验基准
            }
        }

        UpdateStatusText();

        // 引导关格子生成完毕，启动引导流程
        if (isTutorial && tutorialController != null)
            tutorialController.StartTutorial(this);

        // 关卡载入完成：持久化 currentLevel/highestUnlocked（启动恢复与关卡选择用）
        Persist();
    }

    // ================= 单击/双击/滑动 交互协调 =================
    // 延迟判定模型：按下时计算目标 cross 状态但暂不显示，等双击阈值过后无第二次按下才应用（单击出叉）。
    // 双击锁猫时取消未显示的翻转 → 无闪烁。滑动开始时立即应用起始格翻转，随后刷墙式覆盖。

    public void HandlePointerDown(BlockController block)
    {
        if (block == null) return;
        // 本关已通关/已失败：终态期间锁定棋盘输入，不处理任何状态变更，
        // 仅给一致的轻触反馈（与锁定格点击一致），避免终态后制造新错误格与状态冲突（PRD §7.5）
        if (RejectIf(isLevelCompleted || isLevelFailed)) return;
        // 引导关：仅当前允许格支持点击，其他格完全无响应（不触发震动/状态变更）
        if (IsTutorialBlocked(block)) return;
        // 引导关排除格：触碰一律视为单击翻叉，绝不走双击锁猫/判错（避免排除格误双击→点错扣血）
        bool treatAsExcludeOnly = IsExcludeOnlyBlock(block);
        // 已锁定猫的格子（given 或双击锁定）/ 错误锁定格：不改变状态，仅给一致的轻触反馈，
        // 保证玩家在棋盘上任何一次点击都收到统一的 Selection 触觉
        if (RejectIf(block.hasCat || block.isErrorLocked)) return;

        isPointerDown = true;
        hasSlid = false;
        lastSlidBlock = block;

        // 引导关排除格（遮罩露出的排除格）：幂等加叉 + 立即应用 + 只接收第一次点击（已叉则无反应）。
        // 不走延迟/双击，杜绝阈值内二次点击的歧义。滑动复用 swipeTargetState（起始格也置叉）。
        if (treatAsExcludeOnly)
        {
            swipeTargetState = true;
            if (block.hasCross) return; // 已叉：二次点击无反应
            CommitPendingToggle(); // 提交他格未决翻转
            block.hasCross = true;
            block.ApplyVisualState();
            block.PlayCrossPunch();
            FeedbackManager.Instance?.Exclude();
            pendingToggleBlock = null; // 排除格不留待决翻转
            return;
        }

        // 双击判定（锁猫）：该格仍有未应用的待翻转（阈值内第二次按下）→ 触发双击。
        // 引导关非正解格的双击不判错（最后一只猫猎杀阶段），识别为两次单击：cross、取消cross。
        if (pendingToggleBlock == block && pendingToggleRoutine != null)
        {
            if (!isTutorial || CanLockAlongSolution(block))
            {
                CancelPendingToggle(); // 取消未显示的 cross 翻转，杜绝双击锁猫时的闪烁
                ExecuteDoubleClick(block);
                return;
            }
            // 引导关 + 非正解：不升级双击，落入下方单击路径（提交第一次 cross，再排取消 cross）
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
        if (block.hasCat || block.isErrorLocked) return; // 已锁猫格/错误锁定格跳过：错误锁定格的红叉不可被滑动覆盖
        if (block == lastSlidBlock) return; // 去重防抖：手指在本格内摩擦不重复处理
        // 引导关：滑动仅限允许格，非允许格跳过（不刷叉）
        if (IsTutorialBlocked(block)) return;

        hasSlid = true; // 标记本次为滑动，松开时的 click 将被抑制

        // 滑动起始：立即提交起始格的待翻转（起始格状态立即同步），不再等延迟
        CommitPendingToggle();

        // 刷墙式覆盖：强制覆盖为基准状态，杜绝滑过区域叉号交替闪烁。
        // 快速滑动可能跨过中间格未触发其 OnPointerEnter，按 Bresenham 直线补全 lastSlidBlock→block 之间所有格。
        PaintSwipeLine(lastSlidBlock, block);
        lastSlidBlock = block;
    }

    /// <summary>沿 from→to 的 Bresenham 直线逐格刷 cross=swipeTargetState（补全快速滑动跳过的中间格）。
    /// GridLayoutGroup 等宽格，网格空间直线 ≈ 屏幕滑动路径。每格由 PaintSwipeCell 守卫（猫/错误锁/教程非允许/已目标态跳过）。</summary>
    private void PaintSwipeLine(BlockController from, BlockController to)
    {
        if (to == null) return;
        if (from == null) { PaintSwipeCell(to); return; }
        int r = from.row, c = from.col;
        int r1 = to.row, c1 = to.col;
        int dr = Mathf.Abs(r1 - r), dc = Mathf.Abs(c1 - c);
        int sr = r < r1 ? 1 : -1, sc = c < c1 ? 1 : -1;
        // 标准 Bresenham（c=x 轴, r=y 轴）：err = dx - dy
        int err = dc - dr;
        int guard = 0; // 防御：步数上限 = 棋盘格数+余量，避免异常输入死循环
        while (guard++ < gridSize * gridSize + 4)
        {
            int idx = r * gridSize + c;
            if (idx >= 0 && idx < allBlocks.Count) PaintSwipeCell(allBlocks[idx]);
            if (r == r1 && c == c1) break;
            int e2 = 2 * err;
            if (e2 > -dr) { err -= dr; c += sc; }
            if (e2 < dc) { err += dc; r += sr; }
        }
    }

    /// <summary>单格刷 cross=swipeTargetState：跳过猫格/错误锁定格/教程非允许格/已在目标态；仅翻转时播出拳+音频。</summary>
    private void PaintSwipeCell(BlockController block)
    {
        if (block == null || block.hasCat || block.isErrorLocked) return;
        if (IsTutorialBlocked(block)) return;
        bool changed = block.hasCross != swipeTargetState;
        block.hasCross = swipeTargetState;
        block.ApplyVisualState();
        if (changed) // 仅叉号真正翻转时播出拳动画 + 音频反馈，避免无效"嗒"
        {
            block.PlayCrossPunch();
            FeedbackManager.Instance?.Exclude();
        }
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

    /// <summary>取双击阈值对应的 WaitForSeconds（缓存复用，阈值变更时重建）。单击延迟判定每次点击都用，避免高频 new。</summary>
    private WaitForSeconds GetDoubleClickWait()
    {
        if (_doubleClickWait == null || _cachedDoubleClickThreshold != doubleClickThreshold)
        {
            _cachedDoubleClickThreshold = doubleClickThreshold;
            _doubleClickWait = new WaitForSeconds(doubleClickThreshold);
        }
        return _doubleClickWait;
    }

    // 延迟应用 cross 翻转：阈值内未被双击取消则确认为单击，显示 cross 并配音
    private IEnumerator ApplyPendingToggle(BlockController block, bool target)
    {
        yield return GetDoubleClickWait(); // 复用缓存 WaitForSeconds，避免每次点击 new
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

    /// <summary>锁定一只正解猫：显示猫 + 破壳弹跳 + 清除排除标记 + 记账已锁 + 成功反馈 + 规则校验。
    /// 双击正解 / 魔法棒 / 提示填入共用。</summary>
    private void LockCat(BlockController bc, int idx)
    {
        bc.hasCat = true;
        bc.hasCross = false; // 清除可能存在的排除标记
        bc.ApplyVisualState();
        bc.PlayCatPop(); // 破壳弹跳 0->1.25->1.0
        levelState.LockCat(idx); // 计入已锁数与阵营/沿解校验基准
        FeedbackManager.Instance?.Success(); // 成功音效 + 触觉，与猫咪显现同帧
        CheckRules();
    }

    /// <summary>条件为真时给轻触反馈并返回 true（调用方据此 return）。</summary>
    private bool RejectIf(bool condition)
    {
        if (condition) FeedbackManager.Instance?.Tap();
        return condition;
    }

    /// <summary>引导关且该格不在允许集 → 拦截（不响应）。</summary>
    private bool IsTutorialBlocked(BlockController block)
        => isTutorial && tutorialController != null && !tutorialController.IsCellAllowed(block);

    /// <summary>引导关该格是否为"排除格"（触碰=单击翻叉，不走双击）。</summary>
    private bool IsExcludeOnlyBlock(BlockController block)
        => isTutorial && tutorialController != null && tutorialController.IsExcludeOnly(block);

    /// <summary>按一维索引锁定一只正解猫（idx 范围/已锁猫校验+LockCat 统一路径）。
    /// 校验失败给轻触反馈并返回 false；成功锁定返回 true。魔法棒/提示填入共用。</summary>
    private bool TryLockCatByIdx(int idx)
    {
        if (RejectIf(idx < 0 || idx >= allBlocks.Count)) return false;
        BlockController bc = allBlocks[idx];
        if (RejectIf(bc == null || bc.hasCat)) return false;
        LockCat(bc, idx);
        return true;
    }

    /// <summary>对 indices 批量设置 cross=target（跳过已锁猫格/已在目标态），返回是否发生改变。
    /// target=true 出叉→PlayCrossPunch；target=false 取消叉→PlayAttentionShake。反馈由调用方决定。</summary>
    private bool ApplyCrossToBlocks(List<int> indices, bool target)
    {
        bool any = false;
        if (indices == null) return false;
        for (int i = 0; i < indices.Count; i++)
        {
            int idx = indices[i];
            if (idx < 0 || idx >= allBlocks.Count) continue;
            BlockController bc = allBlocks[idx];
            if (bc == null || bc.hasCat || bc.hasCross == target) continue; // 已锁猫或已在目标态跳过
            bc.hasCross = target;
            bc.ApplyVisualState();
            if (target) bc.PlayCrossPunch();
            else bc.PlayAttentionShake();
            any = true;
        }
        return any;
    }

    // 双击：已锁定→忽略；沿解可锁→锁定小猫；否则错误提示
    private void ExecuteDoubleClick(BlockController block)
    {
        if (block == null || block.hasCat) return;

        if (CanLockAlongSolution(block))
        {
            LockCat(block, block.row * gridSize + block.col);
        }
        else
        {
            // 双击非小猫格子：错误反馈
            // 打上叉号并保留至关卡结束（该格已被锁定，不会被后续操作取消）
            // 仅 ApplyVisualState 静默显示叉号——不调 PlayCrossPunch，避免出拳动画与抖动叠加
            block.hasCross = true;
            block.ApplyVisualState();
            block.LockAsError();        // 本关锁定，不再响应点击（关卡不消失、不可点）
            block.PlayErrorFeedback();  // 红框出现 + 抖动，结束后红框消失、叉号染红
            ShowErrorHint();            // HUD 强切 "Not Cat!!!" 并放大回弹
            FeedbackManager.Instance?.Error(); // 错误音效 + 触觉
            // 扣血放最后：最后一次犯错的完整单格错误反馈已播完，再扣血/触发失败（PRD F7）。
            // LoseLife 内部归零时经 OnLivesZero 事件触发 HandleLevelFailed，本处不直接判失败。
            if (livesController != null) livesController.LoseLife();
        }
    }

    /// <summary>该格是否可锁定：存在某个合法解同时包含该格与当前所有已锁猫（沿解可锁）。
    /// 单解关等价于 isCorrect；多解关玩家只能沿某解推进，走错（与已锁猫不在同一解）→ false 落错误分支。
    /// 保证玩家锁的猫始终是某解子集，N 只即构成某解通关。</summary>
    private bool CanLockAlongSolution(BlockController block)
    {
        int idx = block.row * gridSize + block.col;
        return levelState.CanLockAlongSolution(idx);
    }

    /// <summary>已锁猫是否恰好构成某个合法解（多解关走出任一解即通关）。</summary>
    private bool IsAnySolutionSatisfied()
    {
        return levelState.IsSatisfied();
    }


    // 错误提示：ProgressText 临时显示并放大回弹，随后恢复进度文本
    private void ShowErrorHint()
    {
        if (progressText != null)
        {
            progressText.text = I18n.Get("game.not_cat");
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

    // ================= 魔法棒道具（自动填入一个正解猫格）=================

    /// <summary>魔法棒按钮点击：数量 0 调激励广告（奖励 +1 不立即执行）；>0 按 MagicWandSolver 选格锁猫并 -1。
    /// 终态（通关/失败）锁定不处理；无候选格给轻触反馈。</summary>
    public void UseMagicWand()
    {
        if (RejectIf(isLevelCompleted || isLevelFailed)) return;
        if (RejectIf(levelState.Solutions == null || levelState.Solutions.Count == 0)) return;

        // 数量 0：调激励广告（+1 刷新红点，不立即执行）；数量>0 才继续选格（扣减在锁猫成功后）
        if (!inventory.GateMagicWand()) return;

        int? picked = MagicWandSolver.Pick(gridSize, levelState.Map, palette != null ? palette.Length : 0, levelState.Solutions, levelState.LockedIndices);
        if (RejectIf(picked == null)) return;

        // 锁定小猫（统一路径）+ 扣减魔法棒（棋盘发生变化后扣减并刷新红点）
        if (!TryLockCatByIdx(picked.Value)) return;
        inventory.SpendMagicWand();
    }

    // ================= 提示道具（按优先级给填入/打叉/取消叉）=================

    /// <summary>提示道具按钮点击：收集叉号集 → TipSolver.Pick 选动作 → 执行填入/打叉/取消叉。
    /// 终态（通关/失败）锁定不处理；无动作给轻触反馈。叉号集每次调用前从 allBlocks 收集，无需维护。</summary>
    public void UseTip()
    {
        if (RejectIf(isLevelCompleted || isLevelFailed)) return;
        if (RejectIf(levelState.Solutions == null || levelState.Solutions.Count == 0)) return;

        // 数量 0：调激励广告（+1 刷新红点，不立即执行）；引导关免费；数量>0 才继续选格（扣减在动作成功后）
        if (!inventory.GateTip(isTutorial)) return;

        // 收集当前叉号格集（复用缓冲）
        _crossed.Clear();
        for (int i = 0; i < allBlocks.Count; i++)
        {
            BlockController b = allBlocks[i];
            if (b != null && b.hasCross) _crossed.Add(i);
        }

        TipAction action = TipSolver.Pick(gridSize, levelState.Map, palette != null ? palette.Length : 0, levelState.Solutions, levelState.LockedIndices, _crossed);
        if (RejectIf(action == null || action.indices == null || action.indices.Count == 0)) return;

        // 有动作可执行：扣减并刷新红点（棋盘即将发生变化）。引导关免费不扣
        if (!isTutorial) inventory.SpendTip();

        switch (action.type)
        {
            case TipActionType.FillCat:
                // 锁定正解猫（统一路径，同双击/魔法棒）
                TryLockCatByIdx(action.indices[0]);
                break;
            case TipActionType.SetCross:
                // 给目标格打叉（跳过已锁猫格与已叉格）；有改变才出打叉音 + 触觉
                if (ApplyCrossToBlocks(action.indices, true)) FeedbackManager.Instance?.Exclude();
                break;
            case TipActionType.RemoveCross:
                // 取消错叉（仅移除叉号，不改锁猫状态），抖动两下引导玩家双击该正解格
                ApplyCrossToBlocks(action.indices, false);
                FeedbackManager.Instance?.Tap();
                break;
        }
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

        // 失败音效 + 触觉：延迟到当前格子 errorClip 播完再播 failClip，避免与双击点错的 Error 叠加（PRD F19/§6）
        FeedbackManager.Instance?.FailAfterErrorClip();

        // 延迟显示失败弹窗：等最后一次犯错的单格错误反馈（抖动 0.4s / 红框 / 音效 / HUD"Not Cat!!!"）播完再弹，
        // 避免弹窗瞬间盖住反馈。延迟期间 isLevelFailed 已锁棋盘，玩家完整看到最后一次犯错的反馈。
        if (failPopup != null)
        {
            var config = new ModalConfig(I18n.Get("modal.level_failed.title"), new List<ModalButtonDef>
            {
                new ModalButtonDef(I18n.Get("modal.level_failed.revive"), OnReviveRequested),
                new ModalButtonDef(I18n.Get("modal.level_failed.restart"), OnRestartRequested),
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

    /// <summary>延迟弹出通关结算弹窗：待集体起舞（completePopupDelay，默认 0.5s）播完再 Show。
    /// 文案随是否末关切换；末关下一关按钮不可点
    /// （待后续「全通关」需求再定）。切关时由 SafeLoadLevelRoutine 停掉本协程避免弹过时弹窗。</summary>
    private IEnumerator ShowCompletePopupAfter(float delay, bool isLastLevel)
    {
        yield return new WaitForSeconds(delay);
        completePopupRoutine = null;
        if (completePopup == null) yield break;
        string title = isLastLevel ? I18n.Get("game.all_completed_title") : I18n.Getf("game.level_completed_title", currentLevelIndex);
        string nextLabel = isLastLevel ? I18n.Get("game.all_complete_button") : I18n.Getf("game.level", currentLevelIndex + 1);
        completePopup.Show(title, nextLabel, !isLastLevel, LoadNextLevel,
            inventory.LevelsSinceChest, inventory.ChestReadyThisShow, inventory.ChestRewardMagic, inventory.ChestRewardTip,
            onRewardShown: () => inventory.GrantChestReward(), // 宝箱打开（遮罩弹出）时发放道具，红点刷新
            onRewardClosed: () => inventory.OnRewardClosed()); // 退出奖励遮罩后归0，下次结算页起从0格
        inventory.ConsumeChestReady(); // 本结算页开箱展示消费后清除，下关重新判定
    }

    // ================= 规则校验与状态刷新 =================
    // 双击锁猫后 levelState.LockedCount 与 ApplyVisualState 均已同步完成，无需等帧，直接同步判定胜利与刷新状态。
    void CheckRules()
    {
        // 引导关不走标准通关（不弹 CompletePopup），由 TutorialController 检测最后一只锁触发
        if (isTutorial) return;
        // 已锁定的小猫数（given + 双击锁定；错误格无法锁猫，故达标即所有正解猫被点出）
        int correctCount = levelState.LockedCount;

        // 胜利条件：已锁猫恰好构成某个合法解（多解关走出任一解即通关）。
        // 因双击/魔法棒只锁沿解猫，correctCount==gridSize 时 IsAnySolutionSatisfied 必真，加它作防 bug 保险。
        if (correctCount == gridSize && IsAnySolutionSatisfied())
        {
            isLevelCompleted = true; // 锁定棋盘输入：起舞/按钮弹出期间不处理状态变更
            // 通关起舞期间道具按钮保持正常颜色（不置灰），点击由 UseMagicWand/UseTip 的
            // isLevelCompleted 拦截（轻触反馈+不执行）阻断，进入下一关时自然恢复。
            // 阶段宝箱：本关通关计入进度，满10关标记开箱展示。
            // 奖励发放延迟到宝箱打开（奖励遮罩弹出）时，进度归0延迟到遮罩退出后。
            inventory.OnLevelCompleted();
            // 通关 checkpoint：恢复关卡写为下一关（末关维持本关），避免重启重玩刚通关的关
            Persist(Mathf.Min(currentLevelIndex + 1, loadedLevels.Count - 1));
            bool isLastLevel = currentLevelIndex >= loadedLevels.Count - 1;
            if (progressText != null)
                progressText.text = isLastLevel ? I18n.Get("game.all_completed") : I18n.Getf("game.level_completed", currentLevelIndex);

            // 通关音效 + 触觉：延迟到当前格子 successClip 播完再播 finishClip，避免与双击/魔法棒锁猫的 Success 叠加
            FeedbackManager.Instance?.FinishAfterSuccessClip();

            // 猫咪集体起舞：遍历全盘所有已点出的猫咪，同步波浪往复跳跃（LocalY 0->30->0）
            _dancingCats.Clear();
            foreach (BlockController b in allBlocks)
            {
                if (b == null || !b.hasCat || b.cat == null) continue;
                _dancingCats.Add(b.cat.transform);
            }
            TweenRunner.WaveJump(_dancingCats);

            // 通关结算弹窗：待集体起舞（WaveJump≈0.48s）播完再弹，避免弹窗瞬间盖住起舞动画。
            // 下一关按钮现已位于结算弹窗面板内，由弹窗 Show 时填充文案与回调。
            if (completePopup != null)
            {
                if (completePopupRoutine != null) StopCoroutine(completePopupRoutine);
                completePopupRoutine = StartCoroutine(ShowCompletePopupAfter(completePopupDelay, isLastLevel));
            }
            return;
        }

        // 如果没赢，刷新普通状态文本
        UpdateStatusText(correctCount);
    }

    void UpdateStatusText()
    {
        UpdateStatusText(levelState.LockedCount);
    }

    void UpdateStatusText(int correctCount)
    {
        if (levelText != null)
            levelText.text = I18n.Getf("game.level", currentLevelIndex);
        if (progressText != null)
            progressText.text = I18n.Getf("game.progress", correctCount, gridSize);
    }
}
