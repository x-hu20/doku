# 顶部 UI 预制体搭建规格

> 用途：`GameManager` 已移除运行时 `new GameObject / AddComponent` 拼装逻辑，顶部 UI 改为预制体在场景中摆放、Inspector 拖拽绑定。
> 本文档给出节点树、坐标、颜色、字体等全部参数（取自重构前场景 `GameManager` 上的实际配置值），按此搭建即可还原原视觉。

参考分辨率 720×1280，棋盘 650×650 居中于 (0,0)。所有顶部 UI 节点锚点均为 `(0.5, 0.5)`、轴心 `(0.5, 0.5)`，坐标相对 Canvas 中心。

---

## 〇、Canvas 分层（防局部刷新引发整树重绘）

棋盘格子（几百个）与高频变动的文字（LevelText/ProgressText/错误提示）原堆在同一 Canvas 下，文字一变全树重绘。
现划分为两个子 Canvas，利用 Canvas 边界隔离顶点重建污染：

```
Canvas（根，ScreenSpace Overlay，CanvasScaler 720x1280，GraphicRaycaster）
├── Background                         （静态背景，无需子 Canvas）
├── Canvas_Static_Grid                 ★ 子 Canvas：仅棋盘
│   ├── 组件：Canvas(Additional) + GraphicRaycaster + CanvasGroup(可选)
│   └── GridBoard                      （gridParent，650x650 居中）
│       └── (运行时 Instantiate 出的 Block 格子)
└── Canvas_Dynamic_HUD                 ★ 子 Canvas：顶部文字 + 按钮
    ├── 组件：Canvas(Additional) + GraphicRaycaster
    ├── TopUI                          （预制体，见下方“一”）
    │   ├── LevelText / ProgressText / RuleBanner...
    └── NextLevelButton                （场景已有，移入此子 Canvas）
```

**每个子 Canvas 必须各自挂 `GraphicRaycaster`**，否则该子树下的 UI（格子 / 按钮）会静默失去点击响应。
- 子 Canvas 的 `Canvas` 组件设为 **Additional**（勾选 Override Sorting 可调叠放次序；不勾则沿用父级顺序）。
- 根 Canvas 保留原有的 Canvas + CanvasScaler + GraphicRaycaster 不变。
- `GameManager.ValidateReferences()` 会在运行时校验：若 gridParent / nextLevelButton 所在 Canvas 缺 GraphicRaycaster，控制台报错指引补加。

> 这样：ProgressText 文字刷新只会重建 `Canvas_Dynamic_HUD` 的少量顶点；`Canvas_Static_Grid` 里几百个格子纹丝不动。

---

## 一、TopUI 预制体节点树

```
TopUI                  (RectTransform，根节点，置于 Canvas 下，anchoredPosition (0,0))
├── LevelText          (TextMeshProUGUI)
├── ProgressText       (TextMeshProUGUI)
└── RuleBanner         (RoundedImage)
    ├── RuleBlock0     (RoundedImage)
    │   └── RuleText0  (TextMeshProUGUI)
    ├── RuleBlock1     (RoundedImage)
    │   └── RuleText1  (TextMeshProUGUI)
    └── RuleBlock2     (RoundedImage)
        └── RuleText2  (TextMeshProUGUI)
```

### 1. LevelText
| 属性 | 值 |
|---|---|
| anchoredPosition | (0, 535) |
| sizeDelta | (400, 40) |
| font | TMP 默认字体（`TMP_Settings.defaultFontAsset`） |
| fontSize | 40 |
| fontStyle | Bold |
| alignment | Center |
| color | (0.2905661, 0.14637952, 0.15705997, 1) |
| raycastTarget | false |

### 2. ProgressText
| 属性 | 值 |
|---|---|
| anchoredPosition | (0, 465) |
| sizeDelta | (400, 36) |
| font | TMP 默认字体 |
| fontSize | 30 |
| fontStyle | Normal |
| alignment | Center |
| color | (0.29411766, 0.14901961, 0.16078432, 1) |
| raycastTarget | false |

### 3. RuleBanner（父级为 TopUI）
| 属性 | 值 |
|---|---|
| anchoredPosition | (0, 385) |
| sizeDelta | (650, 70) |
| RoundedImage.CornerRadius | 15 |
| color | (1, 0.98823535, 0.97647065, 1) |
| raycastTarget | false |

### 4. RuleBlock0 / 1 / 2（父级为 RuleBanner，子块均分）
- 布局参数：左右边距 `margin=10`，块间距 `gap=10`
- `blockWidth = (650 - 2*10 - 2*10) / 3 = 203.333...`
- `blockHeight = 50`
- 第 i 块中心 x = `-325 + 10 + blockWidth/2 + i*(blockWidth+10)`，即：
  - RuleBlock0: x ≈ -213.33
  - RuleBlock1: x = 0
  - RuleBlock2: x ≈ 213.33
- y = 0（相对 RuleBanner 中心）

| 属性 | 值 |
|---|---|
| anchoredPosition | (上表 x, 0) |
| sizeDelta | (203.333, 50) |
| RoundedImage.CornerRadius | 10 |
| color | (0.9764706, 0.9470624, 0.8509804, 1) |
| raycastTarget | false |

### 5. RuleText0 / 1 / 2（父级为对应 RuleBlock）
| 属性 | 值 |
|---|---|
| anchoredPosition | (0, 0) |
| sizeDelta | (195.333, 44)  // blockWidth-8, blockHeight-6 |
| font | TMP 默认字体 |
| enableAutoSizing | true |
| fontSizeMin | 10.8  // max(8, 18*0.6) |
| fontSizeMax | 18 |
| fontStyle | Normal |
| alignment | Center |
| color | (0.29411766, 0.14901961, 0.16078432, 1) |
| raycastTarget | false |

> 运行时 `GameManager.FillRuleTexts()` 会把 `Resources/rules.csv` 的 3 条规则文本写入 RuleText0/1/2 的 `.text`，预制体里文本可留空。

---

## 二、NextLevelButton（场景已有对象，烘焙样式）

按钮 GameObject 已存在于场景（原 `nextLevelButton` 引用）。重构前 `ConfigureNextButton()` 在运行时设置以下样式，现需在预制体/场景对象上直接配好：

### RoundedImage（按钮根节点上的 RoundedImage 组件）
| 属性 | 值 |
|---|---|
| CornerRadius | 0 |

### Button 颜色过渡（ColorBlock）
| 字段 | 值 |
|---|---|
| normalColor | (1, 0.61202824, 0.1415093, 1) |
| highlightedColor | (1, 0.61202824, 0.1415093, 1) |
| selectedColor | (1, 0.61202824, 0.1415093, 1) |
| pressedColor | (0.85, 0.520224, 0.120283, 1)  // normalColor × 0.85 |

### Shadow 组件（按钮根节点上）
| 属性 | 值 |
|---|---|
| effectColor | (0.4113207, 0.357837, 0.30655032, 0.4) |
| effectDistance | (0, -2) |

### 按钮文字（子节点 TextMeshProUGUI）
| 属性 | 值 |
|---|---|
| font | TMP 默认字体 |
| fontSize | 36 |
| color | (1, 1, 1, 1) |
| fontStyle | Bold |
| alignment | Center |

> 运行时通关后 `GameManager` 会把该文字设为下一关号（如 “Level 2”）或 “All Complete!”。

---

## 三、Inspector 绑定清单

在 `GameManager` 组件的 **“顶部UI引用”** 分组中拖拽绑定：

| GameManager 字段 | 拖入对象 |
|---|---|
| `levelText` | TopUI 下的 **LevelText** 节点 |
| `progressText` | TopUI 下的 **ProgressText** 节点 |
| `ruleTexts` (数组，3 项) | 依次拖入 **RuleText0、RuleText1、RuleText2** |
| `nextLevelButton` | 场景中的 **NextLevelButton** 对象 |
| `nextButtonText` | NextLevelButton 下的文字节点（**留空也可**，运行时会自动获取） |

另外 **“基础引用”** 分组保持原有绑定：`blockPrefab`、`gridParent`、`palette`。

---

## 四、迁移步骤

1. **拆子 Canvas（见“〇、Canvas 分层”）**：在根 Canvas 下新建两个空对象 `Canvas_Static_Grid`、`Canvas_Dynamic_HUD`，各挂 `Canvas`(Additional) + `GraphicRaycaster`。
2. 把场景里的 **GridBoard** 拖为 `Canvas_Static_Grid` 的子节点；把 **NextLevelButton** 拖为 `Canvas_Dynamic_HUD` 的子节点（坐标不变，子 Canvas 锚点居中即可）。
3. 在 Unity 中按“一、TopUI 预制体节点树”搭建节点并配置坐标/颜色/字体（可直接在场景 Canvas 下建好后拖成 Prefab）。
4. 把 TopUI 摆到 **`Canvas_Dynamic_HUD`** 下（与 NextLevelButton 同级），坐标已相对 Canvas 中心。
5. 按“二、NextLevelButton”烘焙按钮样式。
6. 按“三、Inspector 绑定清单”在 GameManager 上拖拽 5 个引用。
7. 运行。若控制台报 `[GameManager] xxx 未绑定` 或 `缺少 GraphicRaycaster`，对照提示修复。

完成后再无运行时 `new GameObject / AddComponent`，顶部 UI 由预制体静态摆放；且文字刷新被隔离在 `Canvas_Dynamic_HUD` 内，不再波及 `Canvas_Static_Grid` 的格子矩阵。
