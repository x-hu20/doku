using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 新手引导关（level 0）控制器：按脚本化步骤引导玩家完成双击/排除/双击/最后一只的操作。
///
/// 设计：
/// - 复用标准棋盘反馈（双击锁猫/打叉动画/音效一致），但点击受限于「当前允许格」。
/// - 引导目标格用聚光灯遮罩呈现：全屏暗色遮罩在目标格位置开洞，仅露出该格，其余区域压暗。
///   遮罩挂在 TutorialUI 所在 Canvas（Dynamic_HUD，sortingOrder 高于棋盘）下作首子节点，
///   故盖在棋盘之上、文案/手指之下；洞内透明 → 露出下方棋盘格与手指。
/// - 通关不走 CheckRules（GameManager.isTutorial 时跳过），由本控制器检测最后一只锁触发，显示 start game 按钮。
/// - 提示道具引导关免费但逻辑一致（ItemInventory.GateTip(isTutorial) 不扣数量）。
/// 步骤锁定猫路径：格8 → 格7 → 格1 → 格14（构成合法解）。
/// </summary>
public class TutorialController : MonoBehaviour
{
    [Header("引导 UI 引用（Inspector 拖拽）")]
    [Tooltip("棋盘上方文案 TMP_Text（支持 \\n 换行）")]
    [SerializeField] private TMP_Text topText;
    [Tooltip("棋盘上方文案背景色块 Image（固定大小，随文案同步显隐，出现时震荡1次）")]
    [SerializeField] private Image topBlock;
    [Tooltip("棋盘下方文案 TMP_Text（Step4 阶段隐藏，由 hintButton 取代）")]
    [SerializeField] private TMP_Text bottomText;
    [Tooltip("棋盘下方文案背景色块 Image（固定大小，随文案同步显隐，出现时震荡1次）")]
    [SerializeField] private Image bottomBlock;
    [Tooltip("手指 Image（Sprite=click_image.png），定位到目标格上方，循环双击缩放动画")]
    [SerializeField] private Image finger;
    [Tooltip("Step4 阶段棋盘下方「A quick hint」按钮，绑定提示道具（引导关免费）")]
    [SerializeField] private Button hintButton;
    [Tooltip("引导关通关后棋盘下方「start game」按钮，点击进第一关")]
    [SerializeField] private Button startGameButton;
    [Tooltip("Step1 反应文案后的「Got it!」继续按钮，点击进入 Step2")]
    [SerializeField] private Button gotItButton;

    [Header("聚光灯遮罩")]
    [Tooltip("全屏遮罩压暗色（RGBA）。覆盖棋盘其余区域，仅在目标格开洞露出该格")]
    [SerializeField] private Color spotlightColor = new Color(0.02f, 0.02f, 0.03f, 0.85f);
    [Tooltip("开洞比目标格四周外扩的像素边距，使整格（含圆角）完整露出")]
    [SerializeField] private float spotlightPadding = 6f;

    [Header("出现/消失节奏")]
    [Tooltip("文案/色块/遮罩/手指 渐入渐出时长（秒）：引导元素缓慢淡入淡出，避免瞬切突兀")]
    [SerializeField] private float appearFadeDur = 0.3f;

    private GameManager gameManager;
    private List<BlockController> Blocks => gameManager != null ? gameManager.AllBlocks : null;

    private enum Step
    {
        Step1_WaitDoubleTap8,
        Step1_TapContinue,      // Step1 锁猫后展示反应文案 + 「Tap anywhere to continue」，等任意点击再进 Step2
        Step2_ExcludeColor2,      // 等 0,4,9,10,11,12 全叉
        Step2_WaitDoubleTap7,     // 等 格7 锁
        Step3_ExcludeColor1,      // 等 2,3,6 全叉
        Step3_WaitDoubleTap1,     // 等 格1 锁
        Step4_WaitLastCat,        // 等 格14 锁
        Done
    }
    private Step step;

    // 当前允许点击的格索引集（聚光灯露出的格），由步骤设置，GameManager.HandlePointerDown 查询
    private HashSet<int> allowedCells = new HashSet<int>();
    // 排除格集（allowedCells 的子集）：这些格的触碰一律视为单击翻叉，不走双击锁猫/判错逻辑。
    // 仅排除阶段（Step2/Step3 的多格）置入；双击目标格（8/7/1/14）不在其列。
    private HashSet<int> excludeOnlyCells = new HashSet<int>();

    // 聚光灯遮罩运行时节点（代码创建，无需场景绑定）：根节点持若干暗色条带子节点，水平切片拼出多洞
    private RectTransform spotlightRoot;
    private CanvasGroup spotlightCG; // 遮罩整体淡入用（不触碰子条带 raycast/scale）
    private Canvas fingerCanvas; // 手指独立 Canvas（overrideSorting）使其渲染在最上层
    private readonly List<RectTransform> stripPool = new List<RectTransform>(); // 条带复用池
    private readonly List<int> spotlightCells = new List<int>(); // 当前开洞目标格集（已含桥接，保证连通）
    // UpdateSpotlightHole 每帧复用缓冲（零 GC）：洞矩形 / y 边界 / 区间并集 / 补集 / 世界坐标四角
    private readonly List<(float x0, float y0, float x1, float y1)> _holes = new List<(float, float, float, float)>();
    private readonly List<float> _ys = new List<float>();
    private readonly List<(float lo, float hi)> _iv = new List<(float lo, float hi)>();
    private readonly List<(float lo, float hi)> _complement = new List<(float lo, float hi)>();
    private readonly Vector3[] _worldCorners = new Vector3[4];

    // 步骤过渡：完成一步后停顿一拍再出现下一步文案/遮罩，避免文案瞬切
    private bool transitioning;
    private Coroutine transitionRoutine;
    // Step1_TapContinue 态：点击任意位置后才渐入下一步，pendingTapContinue 持待执行的 enter 回调
    private System.Action pendingTapContinue;

    /// <summary>当前允许点击的格子集合（引导关点击拦截用）。</summary>
    public bool IsCellAllowed(BlockController block)
    {
        if (block == null) return false;
        int idx = block.row * gameManager.GridSize + block.col;
        return allowedCells.Contains(idx);
    }

    /// <summary>该格是否为「排除格」：触碰一律视为单击翻叉，不参与双击锁猫/判错。
    /// GameManager.HandlePointerDown 双击分支据此跳过，避免排除格误触双击→点错扣血。</summary>
    public bool IsExcludeOnly(BlockController block)
    {
        if (block == null || gameManager == null) return false;
        int idx = block.row * gameManager.GridSize + block.col;
        return excludeOnlyCells.Contains(idx);
    }

    /// <summary>启动引导流程（GameManager.SafeLoadLevelRoutine 末尾调用）。</summary>
    public void StartTutorial(GameManager gm)
    {
        gameManager = gm;
        StopTransition(); // 防御：中断可能残留的上一次过渡协程
        step = Step.Step1_WaitDoubleTap8;
        if (startGameButton != null)
        {
            startGameButton.gameObject.SetActive(false);
            startGameButton.onClick.RemoveAllListeners();
            startGameButton.onClick.AddListener(OnStartGame);
        }
        if (hintButton != null) hintButton.gameObject.SetActive(false);
        HideGotItButton(); // 防御：重置可能残留的脉冲缩放与监听
        ShowFinger(false);
        EnterStep1();
    }

    /// <summary>离开引导关清理（切关时 GameManager 调用）。</summary>
    public void EndTutorial()
    {
        StopTransition();
        ClearSpotlight();
        ShowFinger(false);
        TweenRunner.FadeOut(topText, appearFadeDur);
        TweenRunner.FadeOut(topBlock, appearFadeDur);
        TweenRunner.FadeOut(bottomText, appearFadeDur);
        TweenRunner.FadeOut(bottomBlock, appearFadeDur);
        if (hintButton != null) hintButton.gameObject.SetActive(false);
        HideGotItButton();
        if (startGameButton != null)
        {
            TweenRunner.Stop(startGameButton.transform);
            startGameButton.transform.localScale = Vector3.one;
            startGameButton.gameObject.SetActive(false);
        }
        allowedCells.Clear();
        excludeOnlyCells.Clear();
    }

    private void Update()
    {
        if (gameManager == null || step == Step.Done || transitioning) return;
        // 棋盘布局可能在格子生成当帧尚未结算，每帧按目标格当前世界坐标刷新开洞，规避布局时序
        if (spotlightCells.Count > 0) UpdateSpotlightHole();
        var blocks = Blocks;
        if (blocks == null) return;

        switch (step)
        {
            case Step.Step1_WaitDoubleTap8:
                if (IsLocked(8)) BeginTransition(I18n.Get("tutorial.step1.reaction"), EnterStep2, waitForTap: true);
                break;
            case Step.Step1_TapContinue:
                // 由「Got it!」按钮 onClick 驱动 OnTapContinue，此处无需轮询输入
                break;
            case Step.Step2_ExcludeColor2:
                if (AllCrossed(0, 4, 9, 10, 11, 12)) BeginTransition(null, EnterStep2_DoubleTap7);
                break;
            case Step.Step2_WaitDoubleTap7:
                if (IsLocked(7)) BeginTransition(I18n.Get("tutorial.reaction.perfect"), EnterStep3, delay: 0.5f);
                break;
            case Step.Step3_ExcludeColor1:
                if (AllCrossed(2, 3, 6)) BeginTransition(null, EnterStep3_DoubleTap1);
                break;
            case Step.Step3_WaitDoubleTap1:
                if (IsLocked(1)) BeginTransition(I18n.Get("tutorial.reaction.amazing"), EnterStep4, delay: 0.5f);
                break;
            case Step.Step4_WaitLastCat:
                if (IsLocked(14)) OnTutorialComplete();
                break;
        }
    }

    // ===================== 步骤进入 =====================
    private void EnterStep1()
    {
        SetTop(I18n.Get("tutorial.step1.top"));
        if (bottomText != null) bottomText.gameObject.SetActive(false); // Step1 无下方文案
        HideBottomBlock();
        SetSpotlight(8);
        PositionFinger(8);
        ShowFinger(true);
        SetAllowed(8);
    }

    private void EnterStep2()
    {
        // 色2 排除阶段：0,4,9,10,11,12 可点（打叉）= 放猫8 的行+列去8，本身三段不连通
        // 遮罩露该排除格集 + 最少桥接（补格8 → 十字），整片连通聚焦
        SetTop(I18n.Get("tutorial.step2.top"));
        SetBottom(I18n.Get("tutorial.step2.bottom"));
        ShowFinger(false);
        SetSpotlightRegion(new HashSet<int> { 0, 4, 9, 10, 11, 12 });
        SetAllowedExcludeOnly(0, 4, 9, 10, 11, 12);
        step = Step.Step2_ExcludeColor2;
    }

    private void EnterStep3()
    {
        // 色1 排除阶段：2,3,6 可点，本身已连通（L 形），无需补桥
        SetTop(I18n.Get("tutorial.step3.top"));
        SetBottom(I18n.Get("tutorial.step3.bottom"));
        ShowFinger(false);
        SetSpotlightRegion(new HashSet<int> { 2, 3, 6 });
        SetAllowedExcludeOnly(2, 3, 6);
        step = Step.Step3_ExcludeColor1;
    }

    private void EnterStep4()
    {
        SetTop(I18n.Get("tutorial.step4.top"));
        ShowFinger(false);
        ClearSpotlight();
        // Step4 下方是 hint 按钮非文案：隐藏 bottomText 与 bottomBlock
        if (bottomText != null) bottomText.gameObject.SetActive(false);
        HideBottomBlock();
        if (hintButton != null)
        {
            hintButton.gameObject.SetActive(true);
            // 按钮文案走本地化表（预制体静态文案由运行时表驱动）
            var hintLbl = hintButton.GetComponentInChildren<TMP_Text>();
            if (hintLbl != null) hintLbl.text = I18n.Get("tutorial.hint_button");
            hintButton.onClick.RemoveAllListeners();
            hintButton.onClick.AddListener(() => gameManager.UseTip());
        }
        SetAllowedAll(); // 最后一只猫阶段：放开全盘可点，非正解双击不判错
        step = Step.Step4_WaitLastCat;
    }

    // 排除阶段完成后进入双击引导子步（由过渡协程在停顿后调用）
    private void EnterStep2_DoubleTap7()
    {
        SetTop(I18n.Get("tutorial.doubletap.top"));
        // 遮罩露出目标格7 + 已排除格10、11（连通，无需桥接）；10/11 仅展示不可点（SetAllowed 只放7）
        SetSpotlightRegion(new HashSet<int> { 7, 10, 11 });
        PositionFinger(7);
        ShowFinger(true);
        SetBottom(I18n.Get("tutorial.doubletap.bottom"));
        SetAllowed(7);
        step = Step.Step2_WaitDoubleTap7;
    }

    private void EnterStep3_DoubleTap1()
    {
        SetTop(I18n.Get("tutorial.doubletap.top"));
        // 遮罩露出目标格1 + 已排除格2、3、6（连通，无需桥接）；2/3/6 仅展示不可点（SetAllowed 只放1）
        SetSpotlightRegion(new HashSet<int> { 1, 2, 3, 6 });
        PositionFinger(1);
        ShowFinger(true);
        SetBottom(I18n.Get("tutorial.doubletap.bottom"));
        SetAllowed(1);
        step = Step.Step3_WaitDoubleTap1;
    }

    // ===================== 步骤过渡（步骤间停顿一拍）=====================
    // 完成一步后渐出当前元素（文案/手指/遮罩），等渐出时长得让淡出播完，再渐入下一步元素。
    // 反应文案（若有）作为上方文案的过渡内容淡入替换旧文案。
    // waitForTap=true：渐出后不立即进下一步，改在 bottomText 展示「Tap anywhere to continue」，
    // 切到 Step1_TapContinue 态等任意点击（OnTapContinue）后再渐出反应文案、渐入下一步。
    // delay>0：渐出后让反应文案多停留 delay 秒再进下一步（给玩家看清反应文案）。
    private void BeginTransition(string reaction, System.Action enter, bool waitForTap = false, float delay = 0f)
    {
        if (transitionRoutine != null) StopCoroutine(transitionRoutine);
        transitionRoutine = StartCoroutine(TransitionTo(reaction, enter, waitForTap, delay));
    }

    private IEnumerator TransitionTo(string reaction, System.Action enter, bool waitForTap, float delay)
    {
        transitioning = true;
        // 渐出当前元素：上方文案（无反应时）/下方文案/手指/遮罩
        if (string.IsNullOrEmpty(reaction))
        {
            TweenRunner.FadeOut(topText, appearFadeDur);
            TweenRunner.FadeOut(topBlock, appearFadeDur);
        }
        else SetTop(reaction); // 反应文案淡入替换上方文案
        TweenRunner.FadeOut(bottomText, appearFadeDur);
        TweenRunner.FadeOut(bottomBlock, appearFadeDur);
        ShowFinger(false);
        ClearSpotlight();
        yield return new WaitForSeconds(appearFadeDur); // 等渐出播完
        if (delay > 0f) yield return new WaitForSeconds(delay); // 反应文案多停留 delay 秒

        if (waitForTap)
        {
            // 展示「Got it!」按钮并进入等待态；step 切到 Step1_TapContinue 占位，
            // 避免仍停在 Step1_WaitDoubleTap8 因格8已锁被 Update 重复触发
            ShowGotItButton();
            pendingTapContinue = enter;
            step = Step.Step1_TapContinue;
            transitionRoutine = null;
            transitioning = false;
            yield break;
        }

        // 反应文案先渐出再进下一步，避免 enter.Invoke() 的 SetTop 硬切（alpha 置0）突兀。
        // 无反应时 topText 已在开头渐出，此处跳过。等待 tap 的分支在上方已 yield break。
        if (!string.IsNullOrEmpty(reaction))
        {
            TweenRunner.FadeOut(topText, appearFadeDur);
            TweenRunner.FadeOut(topBlock, appearFadeDur);
            yield return new WaitForSeconds(appearFadeDur);
        }

        transitionRoutine = null;
        enter.Invoke(); // 渐入下一步元素
        transitioning = false;
    }

    /// <summary>Step1_TapContinue 态点击「Got it!」按钮：渐出反应文案/按钮，再渐入下一步。</summary>
    private void OnTapContinue()
    {
        var enter = pendingTapContinue;
        pendingTapContinue = null;
        if (enter == null) return;
        FeedbackManager.Instance?.Tap();
        HideGotItButton();
        if (transitionRoutine != null) StopCoroutine(transitionRoutine);
        transitionRoutine = StartCoroutine(FinishTapContinue(enter));
    }

    private IEnumerator FinishTapContinue(System.Action enter)
    {
        transitioning = true;
        // 渐出反应文案，等淡出播完再进下一步（「Got it!」按钮已在 OnTapContinue 隐藏）
        TweenRunner.FadeOut(topText, appearFadeDur);
        TweenRunner.FadeOut(topBlock, appearFadeDur);
        TweenRunner.FadeOut(bottomText, appearFadeDur);
        TweenRunner.FadeOut(bottomBlock, appearFadeDur);
        yield return new WaitForSeconds(appearFadeDur);
        transitionRoutine = null;
        enter.Invoke(); // 渐入下一步元素
        transitioning = false;
    }

    /// <summary>中断进行中的过渡（切关/通关/离关时调用），避免协程残留改写已清理的状态。</summary>
    private void StopTransition()
    {
        if (transitionRoutine != null) { StopCoroutine(transitionRoutine); transitionRoutine = null; }
        pendingTapContinue = null; // 清理等待点击回调，避免切回时残留触发
        transitioning = false;
    }

    // ===================== 通关 =====================
    private void OnTutorialComplete()
    {
        StopTransition();
        step = Step.Done;
        ShowFinger(false);
        ClearSpotlight();
        TweenRunner.FadeOut(topText, appearFadeDur);
        TweenRunner.FadeOut(topBlock, appearFadeDur);
        TweenRunner.FadeOut(bottomText, appearFadeDur);
        TweenRunner.FadeOut(bottomBlock, appearFadeDur);
        if (hintButton != null) hintButton.gameObject.SetActive(false);
        HideGotItButton();
        allowedCells.Clear();
        excludeOnlyCells.Clear();
        if (startGameButton != null)
        {
            startGameButton.gameObject.SetActive(true);
            // 按钮文案走本地化表（预制体静态文案由运行时表驱动）
            var startLbl = startGameButton.GetComponentInChildren<TMP_Text>();
            if (startLbl != null) startLbl.text = I18n.Get("tutorial.start_button");
            // 与结算页 NextButton 同款动效：持续脉冲引导点击
            TweenRunner.PulseLoop(startGameButton.transform, peak: Tuning.ButtonPulsePeak, halfMs: Tuning.ButtonPulseHalfMs);
        }
    }

    private void OnStartGame()
    {
        FeedbackManager.Instance?.Tap();
        if (startGameButton != null)
        {
            TweenRunner.Stop(startGameButton.transform); // 停脉冲
            startGameButton.transform.localScale = Vector3.one;
        }
        gameManager.LoadLevel(1); // 进第一关
        // 标记教程完成（随 LoadLevel(1) 的 Persist 一同写档）：之后启动直接恢复上次关卡，不再播教程
        SaveSystem.Data.tutorialSeen = true;
    }

    // ===================== 辅助 =====================
    private bool IsLocked(int idx) { var b = Blocks; return idx >= 0 && idx < b.Count && b[idx] != null && b[idx].hasCat; }

    /// <summary>显示「Got it!」继续按钮：设文案（走本地化表）+ 绑定 onClick + 持续脉冲引导点击。</summary>
    private void ShowGotItButton()
    {
        if (gotItButton == null) return;
        gotItButton.gameObject.SetActive(true);
        var lbl = gotItButton.GetComponentInChildren<TMP_Text>();
        if (lbl != null) lbl.text = I18n.Get("tutorial.got_it");
        gotItButton.onClick.RemoveAllListeners();
        gotItButton.onClick.AddListener(OnTapContinue);
        // 与 start game 按钮同款脉冲，引导点击
        TweenRunner.PulseLoop(gotItButton.transform, peak: Tuning.ButtonPulsePeak, halfMs: Tuning.ButtonPulseHalfMs);
    }

    /// <summary>隐藏「Got it!」按钮：停脉冲 + 复位缩放 + 失活，避免残留监听/动效。</summary>
    private void HideGotItButton()
    {
        if (gotItButton == null) return;
        TweenRunner.Stop(gotItButton.transform);
        gotItButton.transform.localScale = Vector3.one;
        gotItButton.onClick.RemoveAllListeners();
        gotItButton.gameObject.SetActive(false);
    }
    private bool AllCrossed(params int[] idxs)
    {
        var b = Blocks;
        foreach (int i in idxs)
        {
            if (i < 0 || i >= b.Count || b[i] == null || !b[i].hasCross) return false;
        }
        return true;
    }
    private void SetAllowed(params int[] idxs)
    {
        allowedCells.Clear();
        excludeOnlyCells.Clear(); // 双击目标格：不标记排除格
        foreach (int i in idxs) allowedCells.Add(i);
    }

    /// <summary>排除阶段专用：设允许格集合并全部标记为排除格（触碰=单击翻叉，不走双击）。</summary>
    private void SetAllowedExcludeOnly(params int[] idxs)
    {
        allowedCells.Clear();
        excludeOnlyCells.Clear();
        foreach (int i in idxs) { allowedCells.Add(i); excludeOnlyCells.Add(i); }
    }

    /// <summary>最后一只猫猎杀阶段（Step4）：放开全盘可点——玩家可单击排除任意非正解格，
    /// 双击正解格(14)锁猫通关；非正解格双击不判错（由 GameManager 识别为两次单击）。</summary>
    private void SetAllowedAll()
    {
        allowedCells.Clear();
        excludeOnlyCells.Clear();
        if (gameManager == null) return;
        int total = gameManager.GridSize * gameManager.GridSize;
        for (int i = 0; i < total; i++) allowedCells.Add(i);
    }
    private void SetTop(string s)
    {
        if (topText != null) { topText.gameObject.SetActive(true); topText.text = s; TweenRunner.FadeIn(topText, appearFadeDur); }
        ShowBlock(topBlock);
    }
    private void SetBottom(string s)
    {
        if (bottomText != null) { bottomText.gameObject.SetActive(true); bottomText.text = s; TweenRunner.FadeIn(bottomText, appearFadeDur); }
        ShowBlock(bottomBlock);
    }
    /// <summary>显示文案背景色块并渐入（固定大小，纯装饰）。</summary>
    private void ShowBlock(Image block)
    {
        if (block == null) return;
        block.gameObject.SetActive(true);
        TweenRunner.FadeIn(block, appearFadeDur);
    }
    private void HideTopBlock() { if (topBlock != null) topBlock.gameObject.SetActive(false); }
    private void HideBottomBlock() { if (bottomBlock != null) bottomBlock.gameObject.SetActive(false); }
    private void ShowFinger(bool on)
    {
        if (finger == null) return;
        if (on) { finger.gameObject.SetActive(true); TweenRunner.FadeIn(finger, appearFadeDur, false); }
        else { TweenRunner.FadeOut(finger, appearFadeDur, false); TweenRunner.Stop(finger.transform); }
    }

    /// <summary>手指定位到目标格正中心（挂格子下、anchoredPosition=zero，依赖手指 pivot=中心、格子 pivot=中心）。
    /// 层级：手指带独立 Canvas（overrideSorting, sortingOrder=1000），即便作为格子子节点也渲染在遮罩/文案之上。</summary>
    private void PositionFinger(int idx)
    {
        var b = Blocks;
        if (finger == null || idx < 0 || idx >= b.Count || b[idx] == null) return;
        finger.transform.SetParent(b[idx].transform, false);
        finger.transform.localScale = Vector3.one;
        finger.rectTransform.anchoredPosition = Vector2.zero; // 居中（格子 pivot 中心 + 手指 pivot 中心）
        EnsureFingerCanvas();
        TweenRunner.FingerDoubleTapLoop(finger.transform);
    }

    /// <summary>给手指挂独立 Canvas 并 overrideSorting，使其渲染在最上层（高于遮罩/文案所在 Canvas）。
    /// 先 GetComponent 复用已存在的 Canvas（避免重复添加导致 AddComponent 返回 null）。</summary>
    private void EnsureFingerCanvas()
    {
        if (finger == null) return;
        if (fingerCanvas == null) fingerCanvas = finger.GetComponent<Canvas>();
        if (fingerCanvas == null) fingerCanvas = finger.gameObject.AddComponent<Canvas>();
        if (fingerCanvas == null) return; // 仍为 null 则放弃，避免崩溃
        fingerCanvas.overrideSorting = true;
        fingerCanvas.sortingOrder = 1000;
    }

    // ===================== 聚光灯遮罩 =====================
    // 全屏暗色遮罩 + 在目标格位置开矩形洞：仅露出目标格，其余区域压暗。
    // 遮罩根挂在 TutorialUI 的父节点（全屏 Dynamic_HUD Canvas，sortingOrder 高于棋盘）下作首子节点：
    // 盖在棋盘之上；同 Canvas 内首子节点 → 文案/手指（后渲染）盖住遮罩保持可见。
    // 洞内无 Image → 透明，露出下方棋盘格与手指。条带 RaycastTarget=false，不拦截输入（输入限制由 IsCellAllowed 负责）。
    //
    // 多洞实现：把全屏按"所有洞的 y 边界"切成水平条带，每条带内洞的 x 区间为"露出"，其补集为"遮罩"条带。
    // 这样任意连通区（十字/L形等）都能用一组不重叠的暗色矩形拼出开洞。

    /// <summary>开启聚光灯：单格开洞（双击引导步骤用）。</summary>
    private void SetSpotlight(int idx)
    {
        EnsureSpotlightRoot();
        spotlightCells.Clear();
        spotlightCells.Add(idx);
        UpdateSpotlightHole();
        ShowSpotlightRoot();
    }

    /// <summary>开启聚光灯：在 seeds 格集上补最少桥接格使其连通，整片连通区开洞（排除阶段用）。
    /// 例：Step2 seeds={0,4,9,10,11,12}（本身三段不连通）补格8 → 十字 {0,4,8,9,10,11,12}；
    /// Step3 seeds={2,3,6}（本身已连通 L 形）不补。</summary>
    private void SetSpotlightRegion(HashSet<int> seeds)
    {
        EnsureSpotlightRoot();
        spotlightCells.Clear();
        foreach (int c in BuildConnectedRegion(seeds)) spotlightCells.Add(c);
        UpdateSpotlightHole();
        ShowSpotlightRoot();
    }

    /// <summary>激活遮罩根并渐入（CanvasGroup alpha 0→1），避免遮罩瞬切。</summary>
    private void ShowSpotlightRoot()
    {
        if (spotlightRoot == null) return;
        spotlightRoot.gameObject.SetActive(true);
        TweenRunner.FadeIn(spotlightCG, appearFadeDur);
    }

    /// <summary>关闭聚光灯（切步骤/离关/通关时调用）。</summary>
    private void ClearSpotlight()
    {
        spotlightCells.Clear();
        if (spotlightCG != null) TweenRunner.FadeOut(spotlightCG, appearFadeDur); // 渐出遮罩
    }

    /// <summary>惰性创建遮罩根节点（条带按需创建复用，不预建固定数量）。</summary>
    private void EnsureSpotlightRoot()
    {
        if (spotlightRoot != null) return;
        Transform parent = transform.parent; // 全屏 Dynamic_HUD Canvas RT
        if (parent == null) return;

        var root = new GameObject("TutorialSpotlight", typeof(RectTransform));
        spotlightRoot = (RectTransform)root.transform;
        spotlightRoot.SetParent(parent, false);
        spotlightRoot.SetAsFirstSibling(); // 首子节点：渲染在同 Canvas 最底层，文案/手指在其后盖住
        // 全屏铺满父节点；pivot 左下角使局部坐标原点 = 左下，便于条带按局部坐标定位
        spotlightRoot.anchorMin = Vector2.zero;
        spotlightRoot.anchorMax = Vector2.one;
        spotlightRoot.pivot = new Vector2(0f, 0f);
        spotlightRoot.anchoredPosition = Vector2.zero;
        spotlightRoot.sizeDelta = Vector2.zero;
        // CanvasGroup 仅用于整体淡入；关闭交互/射线拦截，输入仍由棋盘格 + IsCellAllowed 处理
        spotlightCG = root.AddComponent<CanvasGroup>();
        spotlightCG.interactable = false;
        spotlightCG.blocksRaycasts = false;
        root.SetActive(false);
    }

    /// <summary>取/建一条条带（复用池），不足时新建。返回的条带 pivot 左下、anchor 左下、暗色无 raycast。</summary>
    private RectTransform AcquireStrip()
    {
        int n = stripPool.Count;
        for (int i = 0; i < n; i++)
        {
            if (!stripPool[i].gameObject.activeSelf) return stripPool[i];
        }
        var strip = new GameObject($"SpotlightStrip_{n}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        strip.transform.SetParent(spotlightRoot, false);
        var rt = (RectTransform)strip.transform;
        rt.pivot = new Vector2(0f, 0f);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        var img = strip.GetComponent<Image>();
        img.color = spotlightColor;
        img.raycastTarget = false;
        stripPool.Add(rt);
        return rt;
    }

    /// <summary>按 spotlightCells 当前世界坐标刷新条带，拼出多洞（每帧调以规避布局时序）。
    /// 全程复用实例缓冲（_holes/_ys/_iv/_complement/_worldCorners），每帧零 GC。</summary>
    private void UpdateSpotlightHole()
    {
        if (spotlightRoot == null) return;
        var b = Blocks;
        if (b == null || spotlightCells.Count == 0) { HideAllStrips(); return; }

        float W = spotlightRoot.rect.width;
        float H = spotlightRoot.rect.height;
        if (W < 1f || H < 1f) return; // 父布局尚未结算

        // 1) 收集所有洞矩形（局部坐标，pivot 左下 → 0..W,0..H），并钳制到遮罩范围
        float pad = spotlightPadding;
        _holes.Clear();
        foreach (int idx in spotlightCells)
        {
            if (idx < 0 || idx >= b.Count || b[idx] == null) continue;
            ((RectTransform)b[idx].transform).GetWorldCorners(_worldCorners); // [0]=BL [1]=TL [2]=TR [3]=BR
            Vector2 bl = spotlightRoot.InverseTransformPoint(_worldCorners[0]);
            Vector2 tr = spotlightRoot.InverseTransformPoint(_worldCorners[2]);
            float x0 = Mathf.Clamp(bl.x - pad, 0f, W);
            float y0 = Mathf.Clamp(bl.y - pad, 0f, H);
            float x1 = Mathf.Clamp(tr.x + pad, 0f, W);
            float y1 = Mathf.Clamp(tr.y + pad, 0f, H);
            if (x1 > x0 && y1 > y0) _holes.Add((x0, y0, x1, y1));
        }
        if (_holes.Count == 0) { HideAllStrips(); return; }

        // 2) 收集所有 y 边界（含 0、H 与每个洞的 y0/y1），去重升序
        _ys.Clear();
        _ys.Add(0f); _ys.Add(H);
        foreach (var h in _holes) { _ys.Add(h.y0); _ys.Add(h.y1); }
        _ys.Sort((a, c) => a.CompareTo(c));
        // 原地去重
        int uniq = 1;
        for (int i = 1; i < _ys.Count; i++) { if (_ys[i] - _ys[uniq - 1] > 0.001f) _ys[uniq++] = _ys[i]; }
        _ys.RemoveRange(uniq, _ys.Count - uniq);

        // 3) 逐水平带：取带中点 midY，求覆盖该带的洞 x 区间并集，其补集（在 [0,W] 内）即遮罩条带
        // 先清空所有条带：AcquireStrip 找"首个未激活"复用，若不先清空，上一帧残留的激活条带会让
        // 新分配落到池尾，被尾部停用循环误关，残留旧洞（如 Step1 居中洞在 Step2 仍可见）。
        HideAllStrips();
        int stripIdx = 0;
        int holeCount = _holes.Count;
        for (int i = 0; i < _ys.Count - 1; i++)
        {
            float y0 = _ys[i], y1 = _ys[i + 1];
            if (y1 - y0 < 0.01f) continue;
            float midY = (y0 + y1) * 0.5f;
            _iv.Clear();
            for (int h = 0; h < holeCount; h++)
            {
                var hh = _holes[h];
                if (midY >= hh.y0 && midY <= hh.y1) _iv.Add((hh.x0, hh.x1));
            }
            ComplementIntervals(_iv, 0f, W, _complement);
            for (int s = 0; s < _complement.Count; s++)
            {
                var seg = _complement[s];
                var rt = AcquireStrip();
                rt.anchoredPosition = new Vector2(seg.lo, y0);
                rt.sizeDelta = new Vector2(seg.hi - seg.lo, y1 - y0);
                rt.gameObject.SetActive(true);
                stripIdx++;
            }
        }
        // 4) 关闭多余的条带（本帧未用到）
        for (int i = stripIdx; i < stripPool.Count; i++)
            if (stripPool[i].gameObject.activeSelf) stripPool[i].gameObject.SetActive(false);
    }

    private void HideAllStrips()
    {
        foreach (var rt in stripPool) if (rt != null && rt.gameObject.activeSelf) rt.gameObject.SetActive(false);
    }

    /// <summary>区间并集在 [lo,hi] 内的补集（即需要遮罩的空白段），按 lo 升序写入 res（复用缓冲，零 GC）。</summary>
    private static void ComplementIntervals(List<(float lo, float hi)> iv, float lo, float hi, List<(float lo, float hi)> res)
    {
        res.Clear();
        if (iv.Count == 0) { res.Add((lo, hi)); return; }
        // 按起点排序（iv 为复用缓冲，排序副作用不影响调用方——调用方每带 Clear 重建）
        iv.Sort((a, b) => a.lo.CompareTo(b.lo));
        float cur = lo;
        foreach (var seg in iv)
        {
            float s = Mathf.Max(seg.lo, lo);
            float e = Mathf.Min(seg.hi, hi);
            if (s > cur) res.Add((cur, s));
            if (e > cur) cur = e;
        }
        if (cur < hi) res.Add((cur, hi));
    }

    // ===================== 连通区桥接（最小补格使种子集连通）=====================
    // 排除阶段 seeds（允许排除格）可能本身不连通（如 Step2 的行+列去猫 = 三段）。
    // 暴力枚举补 k 个非种子格（k=1,2,...），首个使整体连通的即最小桥集。4×4 盘非种子格≤10，
    // 枚举量极小，运行时一次性求解无压力。Step2 得 {8}（十字中点连三段），Step3 得 ∅（本身连通）。

    private HashSet<int> BuildConnectedRegion(HashSet<int> seeds)
    {
        var result = new HashSet<int>();
        if (seeds == null || seeds.Count == 0 || gameManager == null) return result;
        int gs = gameManager.GridSize;
        int total = gs * gs;
        result.UnionWith(seeds);
        if (IsConnected(result, gs)) return result;

        // 候选桥接格 = 全盘非种子格
        var cand = new List<int>();
        for (int i = 0; i < total; i++) if (!seeds.Contains(i)) cand.Add(i);

        var combo = new int[cand.Count];
        for (int k = 1; k <= cand.Count; k++)
        {
            if (TryCombine(seeds, cand, combo, 0, 0, k, gs, ref result)) return result;
        }
        return result; // 兜底：无法连通则返回原种子集
    }

    /// <summary>从 cand 中选 k 个（组合枚举），首个使 seeds∪选集 连通的即返回 true 并写入 result。</summary>
    private bool TryCombine(HashSet<int> seeds, List<int> cand, int[] combo, int idx, int start, int k, int gs, ref HashSet<int> result)
    {
        if (idx == k)
        {
            var test = new HashSet<int>(seeds);
            for (int i = 0; i < k; i++) test.Add(combo[i]);
            if (IsConnected(test, gs)) { result = test; return true; }
            return false;
        }
        for (int i = start; i < cand.Count; i++)
        {
            combo[idx] = cand[i];
            if (TryCombine(seeds, cand, combo, idx + 1, i + 1, k, gs, ref result)) return true;
        }
        return false;
    }

    /// <summary>集合在 4 邻接下是否单一连通分量。</summary>
    private static bool IsConnected(HashSet<int> set, int gs)
    {
        if (set.Count <= 1) return true;
        int start = -1;
        foreach (int v in set) { start = v; break; }
        var visited = new HashSet<int> { start };
        var q = new Queue<int>();
        q.Enqueue(start);
        while (q.Count > 0)
        {
            int cur = q.Dequeue();
            foreach (int nb in Neighbors(cur, gs))
                if (set.Contains(nb) && !visited.Contains(nb)) { visited.Add(nb); q.Enqueue(nb); }
        }
        return visited.Count == set.Count;
    }

    /// <summary>4 邻接索引列表（上下左右，越界裁掉）。</summary>
    private static List<int> Neighbors(int idx, int gs)
    {
        var list = new List<int>(4);
        int r = idx / gs, c = idx % gs;
        if (r > 0) list.Add(idx - gs);
        if (r < gs - 1) list.Add(idx + gs);
        if (c > 0) list.Add(idx - 1);
        if (c < gs - 1) list.Add(idx + 1);
        return list;
    }
}
