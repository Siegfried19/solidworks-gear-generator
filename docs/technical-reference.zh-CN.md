# 渐开线圆柱齿轮生成器 — 技术说明

> 给接手的人或 AI agent 看。含参数契约、算法、基准自检值、SolidWorks API 的坑、二次开发方法。
> 2026-09-03 · SolidWorks 2025 / Windows 11 · 状态：外齿轮全链路已实机跑通

---

## 1. 三个形态

| 形态 | 位置 | 用途 |
|---|---|---|
| **SolidWorks 插件**（主力） | `addin\build\` | 功能区【齿轮工具】选项卡 + 原生属性管理器面板，直接出实体 |
| **VBA 宏**（备份） | `macro/GearGen.bas`，源码 `macro/GearGen.bas` | 零安装，换台机器也能用 |
| **网页面板** | `web/gear.html` | 参数计算、实时齿形预览、DXF 导出 |

插件 GUID `{8B5B2107-56A9-4D5E-8DCC-613583B82797}`，注册项在
`HKLM\SOFTWARE\SolidWorks\Addins\{GUID}` 和 `HKCU\Software\SolidWorks\AddInsStartup\{GUID}`。
**安装目录不能移动**（regasm 记的是绝对路径）；要挪先跑 `uninstall.bat`。

---

## 2. 参数契约

| 键 | 符号 | 单位 | 默认 | 范围 |
|---|---|---|---|---|
| `Mn` | mn | mm | 2 | 0.1~100 |
| `Z` | z | — | 20 | 4~500 |
| `AlfN` | αn | ° | 20 | 10~35 |
| `Beta` | β | ° | 0 | −45~45 |
| `X` | x | — | 0 | −1.5~1.5 |
| `Bw` | b | mm | 20 | >0 |
| `Ha` | ha* | — | 1 | 0.5~1.5 |
| `Cc` | c* | — | 0.25 | 0~0.5 |
| `Rho` | ρ* | — | 0.38 | 0~0.6 |
| `Bore` | d0 | mm | 12 | ≥0（0=不做孔）|
| `Keyway` | — | bool | true | GB/T 1095 |
| `IsInternal` | — | bool | false | 外齿/内齿圈 |
| `Npts` | — | — | 24 | 8~80 |

宏与网页之间的逗号串：`mn,z,alfN,beta,x,bw,bore,type,keyway`，例 `2,20,20,0,0,20,12,EXT,1`。

**输出公式**

```
mt = mn/cosβ          αt = atan(tanαn/cosβ)      d = mt·z       db = d·cosαt
外齿  da = d + 2(ha*+x)mn      df = d − 2(ha*+c*−x)mn     sT = mt·π/2 + 2x·mn·tanαt
内齿  da = d − 2(ha*−x)mn      df = d + 2(ha*+c*+x)mn     sT = mt·π/2 − 2x·mn·tanαt
k = round(z'·αn/180 + 0.5)，z' = z·invαt/invαn
W = mn·cosαn·[π(k−0.5) + z·invαt] + 2x·mn·sinαn
zmin = 2(ha*−x)/sin²αt        xmin = ha* − z·sin²αt/2        zv = z/cos³β
```

---

## 3. 齿廓算法

**齿条刀具包络法**，不是圆弧近似。

半齿廓在「齿中心系」生成（齿中心 0°，齿廓点角为负），右半边由 y→−y 镜像。

**渐开线段**（解析，精确）：`θ(ρ) = sT/(2r) + inv(αt) − inv(αy)`，`αy = acos(rb/ρ)`

**过渡曲线段**（trochoid，包络）：齿条刀具在节线纯滚动，刀尖圆角扫出的包络。

```
a0 = π·mt/4 − x·mn·tanαt                       刀齿半厚(节线处)
h  = (ha* + c* − x)·mn                         刀顶高出节线 = r − rf
rc = ρ*·mn，上限 (a0 − h·tanαt)·cosαt/(1−sinαt)   刀顶须保留平顶
Cy = h − rc,   Cx = a0 − Cy·tanαt − rc/cosαt    圆角圆心
```

刀具平移 u 时齿轮转 φ=u/r。**啮合基本定律**：接触点法线过节点（世界系原点），
所以接触点 = 圆心沿「节点→圆心」方向外推 rc：`P = Cw + rc·Cw/|Cw|`，`Cw=(Cx+u, Cy)`。
再变换到齿轮系、旋到齿中心系（ζ = π/2 − π/z）。

u 扫描区间 `[−Cx, Cy/tanαt − Cx]`：起点是圆角与刀顶平面的切点（落在齿根圆上），
终点是圆角与刀具直线段的切点（**恰好落在渐开线上，两段自然相切**）。
齿根平底半角 = Cx/r，由刀顶平面展成，是精确圆弧。

**根切**：扫描中若 `ρ > rb` 且 `θ_trochoid < θ_involute(ρ)`，说明过渡曲线切进了渐开线区 ——
在交点截断，渐开线从该半径起画。所以根切会如实出现在模型上。
扫描密度（≥60 步）与输出密度（Npts）解耦，低精度设置下也不漏判。

**去重**：点列必须去掉连续重合点（容差 1e-5 mm）。手动加的齿根起点与 trochoid 的 u=−Cx
点数学上是同一个，不去重会让 SolidWorks 生成退化样条并静默丢弃，轮廓就开口子了。

**齿顶变尖**：`θ(ra) ≤ 0` 时求变尖半径 `inv(αy) = sT/(2r)+inv(αt)`，把 da 收到该值并警告。

**内齿圈**：渐开线部分公式相同；齿根过渡用与渐开线和齿根圆同时相切的圆弧近似
（圆心在渐开线法向偏置 rc 的曲线上，且 |C| = rf − rc，二分法求切点）。

---

## 4. SolidWorks 落地（踩过的坑全在这）

### 4.1 建模架构：齿坯 + 一个齿槽 + 圆周阵列

```
1. 前视基准面画齿顶圆 → FeatureExtrusion3 拉伸齿宽        （内齿：再切 ra 中心孔）
2. 前视基准面画一个齿槽（6 段）→ FeatureCut4 ThroughAll
3. 上视面 ∩ 右视面 → InsertAxis2 得到 Z 轴
4. FeatureCircularPattern5 阵列齿槽 z 次
5. 内孔 + 键槽 → FeatureCut4
```

齿槽 6 段：根弧 + 左样条 + 封口线 + 封口弧 + 封口线 + 右样条。
封口半径：外齿 `ra + max(0.5, mn)`（齿顶之外），内齿 `ra − max(0.5, mn)`（齿顶之内）。
**齿顶面由齿坯提供，不用画齿顶弧。**

> 这是 Fusion 360 官方 SpurGear、Onshape、FreeCAD、study-gears 一致的架构。
> 原来的做法是一次画 z×4=80 个实体然后赌它闭合 —— 失败时是一个黑盒 null，无从下手。
> 换成 6 个实体后每一步独立可判，问题一次定位。

### 4.2 API 硬规矩

| 坑 | 正确做法 |
|---|---|
| `FeatureExtrusion2` 已 Obsolete；`IPartDoc` 上还有个同名 16 参数返回 void 的方法 | 用 `IFeatureManager.FeatureExtrusion3`（23 参数） |
| 拉伸/切除前草图**必须**以 **mark 0** 选中；`InsertSketch(true)` 不保证留在选中状态 | `FeatureByPositionReverse(0)` 取刚建的草图 → `Select2(false, 0)`，检查返回值 |
| **`AddToDB` 必须为 true** | SolidWorks 默认吸附距离约 **1 mm**，而齿根圆弧只有 0.1 mm 量级，`false` 会把整个齿的端点吸成一坨 |
| `CreateSpline` 不受 `AddToDB` 控制 | 官方文档明写"总是直接入库"，样条坐标永远精确；`CreateArc` 才是变量 |
| `CreateArc` 半径由**圆心+起点**决定，终点只定张角 | 接缝点只算一次、复用同一个 double，别用两个式子分别推 |
| 基准面/特征名与界面语言相关 | 遍历特征找 `GetTypeName2()=="RefPlane"`，第 0/1/2 个就是前视/上视/右视 |
| 草图是否闭合无从得知 | `ISketch.GetSketchContourCount()` 自检，齿槽应为 **1** |
| `CreateArc` 的 direction 参数是 `short` | C# 里要显式 `(short)1` / `(short)-1` |

### 4.3 PropertyManagerPage 生命周期（三条铁律）

1. **参数必须在页面活着时快照。**
   实测：`OnClose` 时控件仍有效，到 `AfterClose` 已被复位成建页初值。
   在 `AfterClose` 里读 `nb.Value` 不抛异常、引用也不是 null，但拿到的是初值 ——
   这就是"参数输入完全不起作用"的根因。
   → 在 `OnClose` 快照，`AfterClose` 只用快照建模。另外把
   `OnNumberboxChanged(id, val)` 送来的 val 记进字典做交叉校验（最可信来源）。

2. **每次打开都要新建 PropertyManagerPage。**
   复用被 SolidWorks 释放的 COM 指针调 `Show2()` → 访问违例 → **SolidWorks 闪退**。

3. **所有回调包 try/catch。** 托管异常穿过 COM 边界进原生代码 = 崩进程。

**控件显示的坑：**
- `IPropertyManagerPageNumberbox` / `Combobox` **没有 Caption 属性**（反射确认）。
  `AddControl2` 传的 caption 被直接丢弃 → 参数名必须用独立 `swControlType_Label` 控件写。
- **Label 的版面矩形在建页那一刻按当时文本算死**，`Height`/`Left`/`Width` 之后都改不了。
  用 `""` 或 `" "` 建的 Label 是零宽空行，后面赋多长的文字都画不出来。
  → 需要动态刷新的"计算结果"行用**只读 Textbox**：编辑控件矩形由类型决定，改 `Text` 必定重画。
- 复选框有 Caption，正常显示，不用另配标签。

### 4.4 COM 注册

- 类上必须是 `[ClassInterface(ClassInterfaceType.AutoDispatch)]`。
  用 `ClassInterfaceType.None` 会让 `SetAddinCallbackInfo2` 抛 `InvalidCastException` ——
  SolidWorks 要通过 IDispatch 按名字回调 `ShowPage`/`EnablePage`。
- 三个 interop DLL 必须和插件放在一起（GAC 里没有），否则 regasm 都跑不起来。

---

## 5. 基准自检值

**mn=2, z=20, αn=20°, x=0, ha*=1, c*=0.25, ρ*=0.38**

| 量 | 值 |
|---|---|
| 分度圆 d | 40.0000 |
| 基圆 db | 37.5877 |
| 齿顶圆 da | 44.0000 |
| 齿根圆 df | 35.0000 |
| 跨齿数 k / 公法线 W | 3 / 15.3209 |
| 量棒距 M (dp=3.36) | 44.4498 |
| 分度圆齿厚 | 3.1416 (=π·mn/2) |
| 不根切最小齿数 | 17.097 |
| 齿根过渡圆角 rc | 0.7600 |
| 齿根平底全宽 | 0.2252 |
| 有效渐开线起始半径 | 18.8201 |

其他核对点：根切 z=10/12/14 报、z=17/20/40 不报；变尖 z=8 x=0.8 触发，da 由 23.200 收到 22.695；
斜齿 β=20° → mt=2.1284、αt=21.1728°、zv=24.103；内齿 z=40 → d=80、da=76、df=85；
键槽 d0=12 → b=4、t2=1.8。

**自检程序**（源码在安装目录 `src\`，改完代码务必跑一遍）：

- `Test.cs` — 几何量对标准值
- `Chain.cs` — 齿槽 6 段轮廓的闭合性与自交，9 个用例（含根切/变位/变尖/大齿数/斜齿/内齿）
- `Sets.cs` — 批量打印候选参数组的计算结果

编译并运行：`csc /target:exe /r:GearWorks.dll Chain.cs && Chain.exe`
期望输出全部 `闭合间隙=0.000000000000  自交=0`。

---

## 6. 已知限制

1. **斜齿只生成端面齿形的直齿体。** 要真螺旋：对拉伸体加【扭曲】特征，
   扭转角 = `b·tanβ/r`（程序会算好并提示）。
2. **内齿圈齿根过渡是切弧近似**（渐开线部分精确）。真实内齿过渡由插齿刀展成，
   形状取决于插齿刀齿数，常规制图用不到。**内齿路径尚未实机验证。**
3. 未做：齿轮副干涉校核、齿根弯曲/接触强度、精度等级与公差带、人字齿、锥齿轮、蜗轮蜗杆。
4. 变位系数不自动优化，由使用者给定。

**下一步可做**（有余力再说）：
- BOSL2 的「两张半径→极角查找表取 min」替代求交裁剪，一行解决所有根切分支判断
- `auto_profile_shift()`：`z_min = 2/sin²αt`，`x = (1 − z/z_min)/cosβ`，默认自动变位
- 每侧样条点数降到 12~20（Fusion 官方用 10，FreeCAD 用 20），点多了曲率会起波浪

---

## 7. 二次开发

| 文件 | 职责 |
|---|---|
| `GearMath.cs` | **纯几何，不依赖 SolidWorks**，可单独拿走用 |
| `GearBuilder.cs` | 齿坯/齿槽/阵列/内孔，落进 SolidWorks |
| `GearPage.cs` | PropertyManagerPage，实现 `IPropertyManagerPage2Handler9`（37 个成员） |
| `GearAddin.cs` | `ISwAddin`、工具栏/选项卡、COM 注册、图标生成、日志 |

**编译**（`csc.exe` 只支持 **C# 5** —— 不能用字符串插值 `$""`、`?.`、`nameof`、表达式体成员、自动属性初始化器）：

```
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:library /platform:x64 ^
  /langversion:5 /out:GearWorks.dll ^
  /r:SolidWorks.Interop.sldworks.dll /r:SolidWorks.Interop.swconst.dll ^
  /r:SolidWorks.Interop.swpublished.dll /r:System.Drawing.dll ^
  src\GearMath.cs src\GearBuilder.cs src\GearPage.cs src\GearAddin.cs
```

改完**不用重新注册**，重启 SolidWorks 即可。DLL 被 SolidWorks 锁着，必须先完全退出才能替换。

**诊断日志**：`GearAddin.Log(string)` → `%LOCALAPPDATA%\GearWorks\addin.log`。
记录插件加载每一步、面板控件创建结果、每次参数变更、OnClose/AfterClose 三路对照、
建模每个特征的成败。出问题先看它，**不要靠猜** —— 这个项目里靠猜错了三次，
每次都是日志一行就定位了。

**验证环境**：本机无 Node / Python（Python 是 Store 存根）。
JS 算法验证用 `cscript //E:JScript`（ES3，需给 `Math.cbrt`/`Math.hypot` 打补丁，
源文件转纯 ASCII 无 BOM，否则解析器会误判引号）。
C# 侧用 `csc.exe` 编译控制台程序跑断言。VBA 无法离线编译验证，只能人工审 + 结构配对检查。
