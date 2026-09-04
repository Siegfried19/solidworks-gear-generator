using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;

namespace GearWorks
{
    /// <summary>
    /// SolidWorks 原生参数面板（PropertyManagerPage）。
    ///
    /// 五条铁律，改这个文件之前必须先读完：
    ///
    ///  1) 页面关闭后 SolidWorks 会把控件复位/销毁。AfterClose 里读 nb.Value 不抛异常、
    ///     引用也不是 null，但拿到的是建页时 nb.Value=val 写进去的初值。
    ///     → 参数必须在页面活着时留存（cur 字段），AfterClose 只用留存值建模。
    ///
    ///  2) 最可信的取值来源不是"读控件"，而是回调参数本身：
    ///     OnNumberboxChanged(int id, double val) 里的 val 是 SolidWorks 直接送过来的。
    ///     → cbNum 字典记下每个 id 最后一次回调值；读控件的结果与它冲突时以回调值为准。
    ///
    ///  3) 数字框 / 组合框根本没有 Caption 属性（反射确认：IPropertyManagerPageNumberbox
    ///     只有 Value/Style/Height/Text/ItemText/CurrentSelection/DisplayedUnit）。
    ///     AddControl2 的 caption 参数对它们是废纸。
    ///     → 参数名必须用独立的 swControlType_Label 控件写，并显式 Left=0 / Width=100。
    ///
    ///  4) Label 的版面尺寸在建页那一刻按当时的文本算死，之后只能改文字、改不了尺寸
    ///     （Height / Left / Width 都是"只能在页面显示前设置"）。用 "" 或 " " 建立的 Label
    ///     等于一条零宽的空行，后面赋多长的 Caption 都画不出来。
    ///     → 需要动态刷新的"计算结果"行改用只读 Textbox：编辑控件的矩形由控件类型决定，
    ///       与文字内容无关，运行期改 Text 一定重画。
    ///
    ///  5) 所有回调都要包 try/catch。托管异常穿过 COM 边界进原生代码 = SolidWorks 闪退。
    /// </summary>
    [ComVisible(true)]
    public class GearPage : PropertyManagerPage2Handler9
    {
        // ================= 控件 ID（全页唯一）=================
        const int GRP_MAIN = 1, GRP_STRUCT = 2, GRP_TOOL = 3, GRP_OUT = 4;
        const int ID_MN = 10, ID_Z = 11, ID_AN = 12, ID_BETA = 13, ID_X = 14;
        const int ID_BW = 20, ID_BORE = 21, ID_TYPE = 23;
        const int ID_HA = 30, ID_CC = 31, ID_RHO = 32, ID_NP = 33;
        const int ID_OUT = 60;          // 结果行 60..67
        const int ID_CAP = 100;         // 参数名标签 100 起自增

        const int NOUT = 8;             // 结果行数（7 行数值 + 1 行提示）
        const short LAB_H = 10;         // 标签高度，对话框单位（默认 8）
        const short FULL_L = 0;         // 组框左边缘
        const short FULL_W = 100;       // 组框宽度就是 100 个对话框单位

        // ================= 状态 =================
        ISldWorks swApp;
        IPropertyManagerPage2 page;
        IPropertyManagerPageNumberbox nMn, nZ, nAn, nBeta, nX, nBw, nBore, nHa, nCc, nRho, nNp;
        IPropertyManagerPageCombobox cType;

        // 计算结果回显：只读文本框。若要换回 Label，见文件末尾 LabOut() 的说明。
        IPropertyManagerPageTextbox[] outBox = new IPropertyManagerPageTextbox[NOUT];

        int capId = ID_CAP;
        bool accepted;
        int closeReason = -1;

        /// <summary>当前参数。页面活着时每次变动都刷新它；AfterClose 只用它建模。</summary>
        GearParams cur;

        /// <summary>上次确定时用过的参数，下次打开面板恢复，省得重填</summary>
        static GearParams remembered;

        /// <summary>建页时写进各数字框的初值 id -> val，用来识别"控件已被复位"</summary>
        Dictionary<int, double> initNum = new Dictionary<int, double>();

        /// <summary>SolidWorks 回调直接送来的值 id -> val（不经过读控件，最可信）</summary>
        Dictionary<int, double> cbNum = new Dictionary<int, double>();

        int initType, cbType;
        bool cbTypeHas;

        public GearPage(ISldWorks app)
        {
            swApp = app;
            cur = remembered != null ? remembered.Clone() : new GearParams();
            Create();
        }

        public void Show()
        {
            accepted = false;
            closeReason = -1;
            if (page == null)
            {
                GearAddin.Log("!!! Show 被调用但 page==null，面板没建起来");
                swApp.SendMsgToUser2("参数面板创建失败，详见日志 " + GearAddin.LogPath,
                    (int)swMessageBoxIcon_e.swMbWarning, (int)swMessageBoxBtn_e.swMbOk);
                return;
            }
            GearAddin.Log("Show2(0) 调用");
            page.Show2(0);
        }

        // ================= 建面板 =================

        void Create()
        {
            GearAddin.Log("======== 建页开始 ========");
            int err = -1;
            int opts = (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_OkayButton
                     | (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_CancelButton
                     | (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_LockedPage;
            page = (IPropertyManagerPage2)swApp.CreatePropertyManagerPage("渐开线齿轮", opts, this, ref err);
            GearAddin.Log("CreatePropertyManagerPage -> page==null? " + (page == null)
                + "  err=" + err + " (0=Okay, 1=UnsupportedHandler, -1=CreationFailure, -2=NoDocument)");
            if (page == null) return;

            GearParams d = cur;

            // ---- 组 1：齿轮基本参数 ----
            IPropertyManagerPageGroup g1 = Grp(GRP_MAIN, "齿轮基本参数");
            nMn = Num(g1, ID_MN, "法向模数  mn  (mm)",
                "齿的大小。分度圆直径 d = mn × z。配对齿轮的模数必须相同。",
                false, 0.1, 100, 0.25, d.Mn);
            nZ = Num(g1, ID_Z, "齿数  z  (个)",
                "整数。齿数少于 17 且不变位会根切。",
                true, 4, 500, 1, d.Z);
            nAn = Num(g1, ID_AN, "法向压力角  αn  (度)",
                "国标标准值 20 度。改这个值就配不上标准齿条刀具和标准配对齿轮。",
                false, 10, 35, 0.5, d.AlfN);
            nBeta = Num(g1, ID_BETA, "螺旋角  β  (度)  —— 直齿填 0",
                "斜齿只生成端面齿形，拉伸后需要自己再加【扭曲】特征。",
                false, -45, 45, 1, d.Beta);
            nX = Num(g1, ID_X, "径向变位系数  x  (无量纲)",
                "正变位可防根切、凑中心距、增强齿根。不确定就填 0。",
                false, -1.5, 1.5, 0.05, d.X);

            // ---- 组 2：结构尺寸 ----
            IPropertyManagerPageGroup g2 = Grp(GRP_STRUCT, "结构尺寸");
            nBw = Num(g2, ID_BW, "齿宽  b  (mm)",
                "齿坯的拉伸厚度。", false, 0.1, 2000, 1, d.Bw);
            nBore = Num(g2, ID_BORE, "内孔直径  d0  (mm)  —— 填 0 则不做孔",
                "轴孔直径。", false, 0, 2000, 1, d.Bore);


            // 组合框没有 Caption 属性，标题必须另起一行 Label
            Cap(g2, "齿轮类型", "外齿轮或内齿圈");
            cType = g2.AddControl2(ID_TYPE,
                (short)swPropertyManagerPageControlType_e.swControlType_Combobox, "",
                (short)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent,
                CtlOpt(true), "外齿轮或内齿圈") as IPropertyManagerPageCombobox;
            if (cType == null) GearAddin.Log("  !!! 类型组合框建立失败");
            else
            {
                cType.Height = 40;
                cType.AddItems(new string[] { "外齿轮", "内齿圈" });
                initType = d.IsInternal ? 1 : 0;
                cType.CurrentSelection = (short)initType;
                GearAddin.Log("  组合框 " + ID_TYPE + " 写入 " + initType + " 回读 " + cType.CurrentSelection);
            }

            // ---- 组 3：刀具与精度 ----
            IPropertyManagerPageGroup g3 = Grp(GRP_TOOL, "刀具与精度");
            nHa = Num(g3, ID_HA, "齿顶高系数  ha*  (无量纲，标准 1.0)",
                "标准齿制 1.0，短齿制 0.8。", false, 0.5, 1.5, 0.05, d.Ha);
            nCc = Num(g3, ID_CC, "顶隙系数  c*  (无量纲，标准 0.25)",
                "决定齿根圆位置：df = d - 2(ha*+c*-x)mn。", false, 0, 0.5, 0.05, d.Cc);
            nRho = Num(g3, ID_RHO, "刀尖圆角系数  ρ*  (无量纲，标准滚刀 0.38)",
                "决定齿根过渡圆角大小，直接影响齿根弯曲强度。", false, 0, 0.6, 0.02, d.Rho);
            nNp = Num(g3, ID_NP, "每侧齿廓型值点数  n  (个，建议 24)",
                "样条拟合精度。12~24 足够，太大反而容易让草图求解变慢。",
                true, 8, 80, 4, d.Npts);

            // ---- 组 4：计算结果（只读回显）----
            IPropertyManagerPageGroup g4 = Grp(GRP_OUT, "计算结果（只读，随输入实时更新）");
            for (int i = 0; i < NOUT; i++) outBox[i] = TxtOut(g4, ID_OUT + i, i > 0);

            GearAddin.Log("======== 建页结束，控件汇总 ========");
            GearAddin.Log("  数字框 null? mn=" + (nMn == null) + " z=" + (nZ == null) + " an=" + (nAn == null)
                + " beta=" + (nBeta == null) + " x=" + (nX == null) + " bw=" + (nBw == null)
                + " bore=" + (nBore == null) + " ha=" + (nHa == null) + " cc=" + (nCc == null)
                + " rho=" + (nRho == null) + " np=" + (nNp == null));
            string s = "";
            for (int i = 0; i < NOUT; i++) s += (outBox[i] == null ? "null " : "ok ");
            GearAddin.Log("  组合框 null? " + (cType == null)
                + "   结果框 60..67: " + s);

            // 消息区只在这里设一次。绝不能在控件回调里调它 —— 见 Refresh 中的注释。
            try
            {
                page.SetMessage3(
                    "填参数，下方【计算结果】随输入实时更新。\r\n" +
                    "根切、齿顶变尖、齿顶过薄会在最后一行提示。确认后点 ✓ 生成实体。",
                    (int)swPropertyManagerPageMessageVisibility.swImportantMessageBox,
                    (int)swPropertyManagerPageMessageExpanded.swMessageBoxExpand,
                    "渐开线齿轮");
            }
            catch (Exception ex) { GearAddin.Log("  SetMessage3(建页一次) 异常: " + ex.Message); }

            Refresh("建页首刷");
        }

        static int CtlOpt(bool tight)
        {
            int o = (int)swAddControlOptions_e.swControlOptions_Visible
                  | (int)swAddControlOptions_e.swControlOptions_Enabled;
            // SmallGapAbove：和上一行贴紧，让"标签 + 控件"看起来是一组
            if (tight) o |= (int)swAddControlOptions_e.swControlOptions_SmallGapAbove;
            return o;
        }

        IPropertyManagerPageGroup Grp(int id, string caption)
        {
            IPropertyManagerPageGroup g = page.AddGroupBox(id, caption,
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible
              | (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded) as IPropertyManagerPageGroup;
            GearAddin.Log("  分组 " + id + " 「" + caption + "」 -> " + (g == null ? "失败" : "ok"));
            return g;
        }

        /// <summary>
        /// 参数名标签。数字框和组合框没有 Caption 属性，参数名只能靠这种独立 Label 显示。
        /// 必须显式定 Left/Width/Height：这些属性只在页面显示前有效，Show2 之后再设无效。
        /// </summary>
        void Cap(IPropertyManagerPageGroup g, string text, string tip)
        {
            int id = capId++;
            IPropertyManagerPageLabel lb = g.AddControl2(id,
                (short)swPropertyManagerPageControlType_e.swControlType_Label, text,
                (short)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge,
                CtlOpt(false), tip) as IPropertyManagerPageLabel;
            if (lb == null) { GearAddin.Log("  !!! 参数名标签建立失败: " + text); return; }
            try
            {
                lb.Style = (int)swPropMgrPageLabelStyle_e.swPropMgrPageLabelStyle_LeftText;
                lb.Height = LAB_H;
            }
            catch (Exception ex) { GearAddin.Log("  标签 " + id + " 样式设置失败: " + ex.Message); }
            Layout(lb as IPropertyManagerPageControl, id, "标签", FULL_L, FULL_W, text);
        }

        /// <summary>
        /// 打印控件的原始版面参数（这是校准 Left/Width 的唯一依据：PMP 用的是对话框单位，
        /// 组框左边缘 = 0，右边缘 = 100，不是像素），然后按需要覆盖。
        /// </summary>
        void Layout(IPropertyManagerPageControl c, int id, string what, short left, short width, string note)
        {
            if (c == null)
            {
                GearAddin.Log("  " + what + " " + id + " 取不到 IPropertyManagerPageControl（QueryInterface 失败）");
                return;
            }
            try
            {
                GearAddin.Log("  " + what + " " + id + " 默认版面 Left=" + c.Left + " Top=" + c.Top
                    + " Width=" + c.Width + "  「" + note + "」");
                if (width > 0)
                {
                    c.Left = left;
                    c.Width = width;
                    c.OptionsForResize =
                          (int)swPropMgrPageControlOnResizeOptions_e.swControlOptionsOnResize_LockLeft
                        | (int)swPropMgrPageControlOnResizeOptions_e.swControlOptionsOnResize_LockRight;
                    GearAddin.Log("  " + what + " " + id + " 已设 Left=" + c.Left + " Width=" + c.Width);
                }
            }
            catch (Exception ex) { GearAddin.Log("  " + what + " " + id + " 布局失败: " + ex.Message); }
        }

        /// <summary>参数行 = 参数名标签（顶格）+ 数字框（缩进一级、紧贴上一行）</summary>
        IPropertyManagerPageNumberbox Num(IPropertyManagerPageGroup g, int id, string name,
            string tip, bool integer, double lo, double hi, double inc, double val)
        {
            Cap(g, name, tip);
            IPropertyManagerPageNumberbox nb = g.AddControl2(id,
                (short)swPropertyManagerPageControlType_e.swControlType_Numberbox,
                name,   // 这个 caption 数字框不会画出来，留着只为在 API 里能认出是谁
                (short)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent,
                CtlOpt(true), tip) as IPropertyManagerPageNumberbox;
            if (nb == null) { GearAddin.Log("  !!! 数字框 " + id + " 建立失败: " + name); return null; }
            try
            {
                // 单位类型只能在页面显示前定死。这里一律用无单位类型：数值就是 mm / 度，
                // 不经过 SolidWorks 的系统单位换算，杜绝 1000 倍错。单位写在标签文字里。
                int ut = integer
                    ? (int)swNumberboxUnitType_e.swNumberBox_UnitlessInteger
                    : (int)swNumberboxUnitType_e.swNumberBox_UnitlessDouble;
                nb.SetRange2(ut, lo, hi, true, inc, inc, inc / 2.0);
                nb.Value = val;
                initNum[id] = val;
                GearAddin.Log("  数字框 " + id + "(" + NameOf(id) + ") 写入 " + val
                    + " 回读 " + nb.Value + "  范围[" + lo + "," + hi + "] 步进 " + inc);
                if (Math.Abs(nb.Value - val) > 1e-9)
                    GearAddin.Log("  !! 数字框 " + id + " 回读值与写入值不符，SetRange2 把它夹住了");
            }
            catch (Exception ex) { GearAddin.Log("  !!! 数字框 " + id + " 初始化失败: " + ex.Message); }
            // 数字框保持 SolidWorks 的默认版面（缩进 + 撑到组框右边缘），只记录不覆盖
            Layout(nb as IPropertyManagerPageControl, id, "数字框", 0, 0, name);
            return nb;
        }

        /// <summary>
        /// 计算结果行 = 只读文本框。
        /// 为什么不用 Label：Label 的宽高在建页那一刻按当时的文本定死，用空串建的 Label
        /// 就是一条零宽空行，之后赋 Caption 一定看不见。Textbox 是编辑控件，矩形由控件类型
        /// 决定、与内容无关，运行期改 Text 必定重画，而且用户能选中数值复制走。
        /// </summary>
        IPropertyManagerPageTextbox TxtOut(IPropertyManagerPageGroup g, int id, bool tight)
        {
            IPropertyManagerPageTextbox tb = g.AddControl2(id,
                (short)swPropertyManagerPageControlType_e.swControlType_Textbox, "",
                (short)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge,
                CtlOpt(tight), "计算结果，只读，可选中复制") as IPropertyManagerPageTextbox;
            if (tb == null) { GearAddin.Log("  !!! 结果文本框 " + id + " 建立失败"); return null; }
            try
            {
                tb.Style = (int)swPropMgrPageTextBoxStyle_e.swPropMgrPageTextBoxStyle_ReadOnly;
                tb.Text = "";
            }
            catch (Exception ex) { GearAddin.Log("  结果文本框 " + id + " 样式设置失败: " + ex.Message); }
            Layout(tb as IPropertyManagerPageControl, id, "结果框", FULL_L, FULL_W, "结果行");
            return tb;
        }

        void SetOut(int i, string s)
        {
            if (i < 0 || i >= outBox.Length || outBox[i] == null) return;
            try { outBox[i].Text = s; }
            catch (Exception ex) { GearAddin.Log("  写结果行 " + i + " 失败: " + ex.Message); }
        }

        // ================= 取值 =================

        /// <summary>
        /// 取一个数字框的值。规则（这是整个修复的核心）：
        ///   · 正常情况用控件读回的值；
        ///   · 但如果读回的恰好等于建页初值、而回调曾送来别的值，
        ///     说明控件已被 SolidWorks 复位 —— 这时相信回调值。
        /// 两种世界都不会出错：控件还活着时两者相等；控件被复位时回调值救场。
        /// </summary>
        double GetNum(IPropertyManagerPageNumberbox nb, int id, double fallback)
        {
            if (nb == null) return fallback;
            double live;
            try { live = nb.Value; }
            catch (Exception ex)
            {
                GearAddin.Log("  读数字框 " + id + "(" + NameOf(id) + ") 异常: " + ex.Message
                    + "，改用留存值 " + fallback);
                return fallback;
            }
            double cb, init;
            if (cbNum.TryGetValue(id, out cb) && initNum.TryGetValue(id, out init)
                && Math.Abs(live - init) < 1e-9 && Math.Abs(cb - init) > 1e-9)
            {
                GearAddin.Log("  !! 数字框 " + id + "(" + NameOf(id) + ") 读回初值 " + init
                    + "，判定为已被复位，改用回调值 " + cb);
                return cb;
            }
            return live;
        }

        /// <summary>读控件 + 与回调值合并，得到当前应该使用的参数</summary>
        GearParams ReadMerged()
        {
            GearParams p = cur.Clone();          // 读不到的字段保持上一次的值，绝不退回默认值
            p.Mn = GetNum(nMn, ID_MN, p.Mn);
            p.Z = (int)Math.Round(GetNum(nZ, ID_Z, p.Z));
            p.AlfN = GetNum(nAn, ID_AN, p.AlfN);
            p.Beta = GetNum(nBeta, ID_BETA, p.Beta);
            p.X = GetNum(nX, ID_X, p.X);
            p.Bw = GetNum(nBw, ID_BW, p.Bw);
            p.Bore = GetNum(nBore, ID_BORE, p.Bore);
            p.Ha = GetNum(nHa, ID_HA, p.Ha);
            p.Cc = GetNum(nCc, ID_CC, p.Cc);
            p.Rho = GetNum(nRho, ID_RHO, p.Rho);
            p.Npts = (int)Math.Round(GetNum(nNp, ID_NP, p.Npts));

            if (cType != null)
            {
                try
                {
                    int live = cType.CurrentSelection;
                    if (cbTypeHas && live == initType && cbType != initType)
                    {
                        GearAddin.Log("  !! 组合框读回初值 " + initType + "，判定为已被复位，改用回调值 " + cbType);
                        live = cbType;
                    }
                    p.IsInternal = (live == 1);
                }
                catch (Exception ex) { GearAddin.Log("  读组合框异常: " + ex.Message); }
            }

            if (p.Z < 4) p.Z = 4;
            if (p.Npts < 8) p.Npts = 8;
            if (p.Mn <= 0) p.Mn = 0.1;
            return p;
        }

        /// <summary>纯读控件、不做任何合并 —— 只写进日志做诊断，返回值不参与建模</summary>
        GearParams RawRead()
        {
            GearParams p = new GearParams();
            if (nMn != null) p.Mn = nMn.Value;
            if (nZ != null) p.Z = (int)Math.Round(nZ.Value);
            if (nAn != null) p.AlfN = nAn.Value;
            if (nBeta != null) p.Beta = nBeta.Value;
            if (nX != null) p.X = nX.Value;
            if (nBw != null) p.Bw = nBw.Value;
            if (nBore != null) p.Bore = nBore.Value;
            if (nHa != null) p.Ha = nHa.Value;
            if (nCc != null) p.Cc = nCc.Value;
            if (nRho != null) p.Rho = nRho.Value;
            if (nNp != null) p.Npts = (int)Math.Round(nNp.Value);
            if (cType != null) p.IsInternal = (cType.CurrentSelection == 1);
            return p;
        }

        // ================= 刷新回显 =================

        /// <summary>
        /// 只留存参数，不碰任何控件。失焦、滑块结束这类"补一刀"的回调走这里 ——
        /// 它们只是为了别漏掉用户输入，没必要再刷一遍界面。
        /// 在控件回调里反复重排面板，正是崩溃的来源。
        /// </summary>
        void Snap(string who)
        {
            try
            {
                cur = ReadMerged();
                GearAddin.Log("Snap[" + who + "] 留存: " + Dump(cur));
            }
            catch (Exception ex) { GearAddin.Log("!!! Snap[" + who + "] 异常: " + ex.Message); }
        }

        /// <summary>防重入：刷新时写控件可能再次触发回调，嵌套进来会踩死 SolidWorks</summary>
        bool refreshing;

        void Refresh(string who)
        {
            if (refreshing) { GearAddin.Log("Refresh[" + who + "] 跳过(重入)"); return; }
            refreshing = true;
            GearParams p = null;
            try
            {
                p = ReadMerged();
                cur = p;                                   // ← 页面活着时不断留存
                GearAddin.Log("Refresh[" + who + "] 采用参数: " + Dump(p));

                GearGeom g = GearMath.Calc(p);
                SetOut(0, "分度圆   d  = " + N(g.D) + " mm");
                SetOut(1, "基圆     db = " + N(g.Db) + " mm");
                SetOut(2, "齿顶圆   da = " + N(g.Da) + " mm");
                SetOut(3, "齿根圆   df = " + N(g.Df) + " mm");
                SetOut(4, "公法线   W  = " + N(g.W) + " mm   (跨 " + g.K + " 齿)");
                SetOut(5, "量棒距   M  = " + N(g.M) + " mm   (量棒 φ" + N(g.Dp) + ")");
                SetOut(6, "齿顶厚   sa = " + N(g.Sa) + " mm   全齿高 h = " + N(Math.Abs(g.Ra - g.Rf)) + " mm");
                string w = Warn(p, g);
                SetOut(7, w);
                // 这里【不能】调 SetMessage3：它带 MessageBoxExpand 会让 SolidWorks 重排整个属性页，
                // 在控件变更回调内部触发重排 = 重入崩溃。消息区只在建页时设一次。
                GearAddin.Log("Refresh[" + who + "] 界面已更新");
            }
            catch (Exception ex)
            {
                // 绝不静默吞异常：吞了就分不清是"算不出来"还是"画不出来"
                GearAddin.Log("!!! Refresh[" + who + "] 异常: " + ex.GetType().Name + ": " + ex.Message);
                GearAddin.Log("    " + ex.StackTrace);
                try { SetOut(7, "参数超出可计算范围：" + ex.Message); }
                catch { }
            }
            finally { refreshing = false; }
        }

        /// <summary>
        /// 保底通道：把"读到的参数"和结果同时打进 PMP 的消息区。
        /// 消息区是 SolidWorks 自己维护的、可靠重绘的区域，即使上面那些控件全都不刷新，
        /// 用户也能在这里一眼确认自己填的值确实被读到了。
        /// </summary>
        /// <summary>【已停用】不要在任何控件回调里调用它。SetMessage3 会让 SolidWorks
        /// 重排整个属性页，在回调内部触发重排会崩进程（2026-09-04 实测）。</summary>
        void Echo_DoNotCallFromCallbacks(GearParams p, GearGeom g, string warn)
        {
            if (page == null || p == null) return;
            try
            {
                string s = "读到的参数：模数 mn=" + N(p.Mn) + "   齿数 z=" + p.Z
                    + "   压力角 αn=" + N(p.AlfN) + "°   螺旋角 β=" + N(p.Beta) + "°"
                    + "   变位 x=" + N(p.X) + "\r\n"
                    + "齿宽 b=" + N(p.Bw) + "   内孔 d0=" + N(p.Bore)
    
                    + "   类型=" + (p.IsInternal ? "内齿圈" : "外齿轮") + "\r\n";
                if (g != null)
                    s += "分度圆 d=" + N(g.D) + "   齿顶圆 da=" + N(g.Da)
                       + "   齿根圆 df=" + N(g.Df) + "   公法线 W=" + N(g.W) + "\r\n";
                s += warn;
                bool ok = page.SetMessage3(s,
                    (int)swPropertyManagerPageMessageVisibility.swImportantMessageBox,
                    (int)swPropertyManagerPageMessageExpanded.swMessageBoxExpand,
                    "确认一下这些是不是你填的值");
                if (!ok) GearAddin.Log("  SetMessage3 返回 false");
            }
            catch (Exception ex) { GearAddin.Log("  SetMessage3 异常: " + ex.Message); }
        }

        static string Warn(GearParams p, GearGeom g)
        {
            if (!p.IsInternal)
            {
                if (p.Z < g.ZMin - 1e-9)
                    return "⚠ 根切：齿数 " + p.Z + " < 不根切最小齿数 "
                        + g.ZMin.ToString("0.0") + "，建议变位 x ≥ " + g.XMin.ToString("0.000");
                if (g.Pointed) return "⚠ 齿顶变尖，齿顶圆已自动收到 " + N(g.Da);
                if (g.Sa < 0.25 * p.Mn) return "⚠ 齿顶厚仅 " + N(g.Sa) + " mm，偏薄";
            }
            if (Math.Abs(p.Beta) > 1e-9)
                return "斜齿：生成端面齿形，拉伸后加【扭曲】特征，扭转角 "
                    + (p.Bw * Math.Tan(g.BetaR) / g.R / GearMath.D2R).ToString("0.000") + "°";
            return "参数正常";
        }

        static string N(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "—";
            return v.ToString("0.0000");
        }

        static string Dump(GearParams p)
        {
            return "mn=" + p.Mn + " z=" + p.Z + " an=" + p.AlfN + " beta=" + p.Beta
                + " x=" + p.X + " bw=" + p.Bw + " bore=" + p.Bore + " ha=" + p.Ha
                + " cc=" + p.Cc + " rho=" + p.Rho + " npts=" + p.Npts
                + " 内齿=" + p.IsInternal;
        }

        string DumpCb()
        {
            if (cbNum.Count == 0 && !cbTypeHas) return "(本次一个回调都没收到)";
            string s = "";
            foreach (KeyValuePair<int, double> kv in cbNum)
                s += NameOf(kv.Key) + "=" + kv.Value + " ";
            if (cbTypeHas) s += "type=" + cbType + " ";
            return s;
        }

        static string NameOf(int id)
        {
            switch (id)
            {
                case ID_MN: return "mn";
                case ID_Z: return "z";
                case ID_AN: return "an";
                case ID_BETA: return "beta";
                case ID_X: return "x";
                case ID_BW: return "bw";
                case ID_BORE: return "bore";
                case ID_HA: return "ha";
                case ID_CC: return "cc";
                case ID_RHO: return "rho";
                case ID_NP: return "npts";
                case ID_TYPE: return "type";
                default: return "id" + id;
            }
        }

        // ================= 回调（全部包 try/catch，异常绝不能穿回 SolidWorks）=================

        public void OnNumberboxChanged(int id, double val)
        {
            try
            {
                cbNum[id] = val;        // ← 最可信的来源：值由 SolidWorks 直接送过来
                GearAddin.Log("OnNumberboxChanged " + NameOf(id) + "(id=" + id + ") = " + val);
                Refresh("数字框变更");
            }
            catch (Exception ex) { GearAddin.Log("!!! OnNumberboxChanged 异常: " + ex.Message); }
        }

        public void OnNumberBoxTrackingCompleted(int id, double val)
        {
            try
            {
                cbNum[id] = val;
                GearAddin.Log("OnNumberBoxTrackingCompleted " + NameOf(id) + "(id=" + id + ") = " + val);
                Snap("滑块结束");   // 只留存，界面已由 OnNumberboxChanged 刷过
            }
            catch (Exception ex) { GearAddin.Log("!!! OnNumberBoxTrackingCompleted 异常: " + ex.Message); }
        }

        public void OnCheckboxCheck(int id, bool val)
        {
            try
            {
                GearAddin.Log("OnCheckboxCheck id=" + id + " = " + val);
                Refresh("复选框变更");
            }
            catch (Exception ex) { GearAddin.Log("!!! OnCheckboxCheck 异常: " + ex.Message); }
        }

        public void OnComboboxSelectionChanged(int id, int item)
        {
            try
            {
                if (id == ID_TYPE) { cbType = item; cbTypeHas = true; }
                GearAddin.Log("OnComboboxSelectionChanged id=" + id + " = " + item);
                Refresh("组合框变更");
            }
            catch (Exception ex) { GearAddin.Log("!!! OnComboboxSelectionChanged 异常: " + ex.Message); }
        }

        public void AfterActivation()
        {
            try { GearAddin.Log("AfterActivation"); Refresh("页面激活"); }
            catch (Exception ex) { GearAddin.Log("!!! AfterActivation 异常: " + ex.Message); }
        }

        /// <summary>
        /// 失焦补一刀：用户在数字框里打完字直接点绿勾时，万一 change 通知没发出来，
        /// 这里还能在页面活着的时候把值捞回来。
        /// </summary>
        public void OnLostFocus(int id)
        {
            try { Snap("失焦 id=" + id); }   // 只留存，不重排界面
            catch (Exception ex) { GearAddin.Log("!!! OnLostFocus 异常: " + ex.Message); }
        }

        public void OnClose(int reason)
        {
            try
            {
                closeReason = reason;
                // 只把"确定"和"应用"当成要建模；Cancel/Escape/Closed/ParentClosed 都不建
                accepted = (reason == (int)swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Okay)
                        || (reason == (int)swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Apply);

                string raw = "读控件失败";
                try { raw = Dump(RawRead()); }
                catch (Exception ex) { raw = "读控件异常 " + ex.Message; }

                GearParams merged = ReadMerged();

                GearAddin.Log("======== OnClose reason=" + reason
                    + " (1=确定 2=取消 4=Closed 5=Esc 6=应用)  accepted=" + accepted + " ========");
                GearAddin.Log("    [A] 回调累计值  : " + DumpCb());
                GearAddin.Log("    [B] 此刻读控件  : " + raw);
                GearAddin.Log("    [C] 合并后采用  : " + Dump(merged));
                GearAddin.Log("    判读：[B] 与 [A] 一致 => OnClose 期间控件仍有效；"
                    + "[B] 退回建页初值 => 控件已复位，靠 [A] 救回来了");

                cur = merged;
                if (accepted) remembered = cur.Clone();
            }
            catch (Exception ex) { GearAddin.Log("!!! OnClose 异常: " + ex.Message); }
        }

        public void AfterClose()
        {
            try
            {
                GearAddin.Log("======== AfterClose reason=" + closeReason + " accepted=" + accepted + " ========");
                // 纯诊断：此刻再读一次控件，只写日志、绝不使用。
                // 一次运行就能确认 SolidWorks 到底什么时候把控件复位的。确认完可以删掉这三行。
                try { GearAddin.Log("    [诊断] AfterClose 读控件 : " + Dump(RawRead())); }
                catch (Exception ex) { GearAddin.Log("    [诊断] AfterClose 读控件异常: " + ex.Message); }

                if (!accepted) { Release(); return; }
                accepted = false;

                GearParams p = cur.Clone();      // ← 只用留存值，绝不再读控件
                GearAddin.Log("AfterClose 建模采用: " + Dump(p));

                string err = null;
                try { err = GearBuilder.Build(swApp, p); }
                catch (Exception ex)
                {
                    err = "生成失败：" + ex.Message;
                    GearAddin.Log("!!! Build 异常: " + ex.GetType().Name + ": " + ex.Message);
                    GearAddin.Log("    " + ex.StackTrace);
                }

                if (err != null)
                    swApp.SendMsgToUser2(err, (int)swMessageBoxIcon_e.swMbWarning,
                        (int)swMessageBoxBtn_e.swMbOk);
                else
                {
                    GearGeom g = GearMath.Calc(p);
                    string s = "齿轮已生成，用的是这组参数：\r\n\r\n"
                        + "法向模数 mn = " + N(p.Mn) + " mm\r\n"
                        + "齿数     z  = " + p.Z + "\r\n"
                        + "压力角   αn = " + N(p.AlfN) + "°\r\n"
                        + "变位系数 x  = " + N(p.X) + "\r\n"
                        + "齿宽     b  = " + N(p.Bw) + " mm\r\n"
                        + "内孔     d0 = " + N(p.Bore) + " mm\r\n"
                        + "------------------------------\r\n"
                        + "分度圆 d  = " + N(g.D) + " mm\r\n"
                        + "齿顶圆 da = " + N(g.Da) + " mm\r\n"
                        + "齿根圆 df = " + N(g.Df) + " mm\r\n"
                        + "公法线 W(跨 " + g.K + " 齿) = " + N(g.W) + " mm\r\n"
                        + "量棒距 M(量棒 φ" + N(g.Dp) + ") = " + N(g.M) + " mm\r\n";
                    try
                    {
                        GearMath.Half h = GearMath.Flank(p, g);
                        string wtxt = GearMath.Warnings(p, g, h);
                        if (wtxt != "") s += "\r\n提示：\r\n" + wtxt;
                    }
                    catch (Exception ex) { GearAddin.Log("  校核提示生成失败: " + ex.Message); }
                    swApp.SendMsgToUser2(s, (int)swMessageBoxIcon_e.swMbInformation,
                        (int)swMessageBoxBtn_e.swMbOk);
                }
                Release();
            }
            catch (Exception ex) { GearAddin.Log("!!! AfterClose 异常: " + ex.Message); }
        }

        /// <summary>官方文档要求：AfterClose 里放掉引用，避免 GC 相关问题</summary>
        void Release()
        {
            nMn = null; nZ = null; nAn = null; nBeta = null; nX = null;
            nBw = null; nBore = null; nHa = null; nCc = null; nRho = null; nNp = null;
            cType = null;
            for (int i = 0; i < outBox.Length; i++) outBox[i] = null;
            page = null;
        }

        // ---- 其余接口成员：空实现 ----
        public void OnTextboxChanged(int id, string text) { }
        public void OnComboboxEditChanged(int id, string text) { }
        public void OnListboxSelectionChanged(int id, int item) { }
        public void OnGroupExpand(int id, bool expanded) { }
        public void OnGroupCheck(int id, bool val) { }
        public void OnOptionCheck(int id) { }
        public void OnButtonPress(int id) { }
        public bool OnHelp() { return true; }
        public bool OnPreviousPage() { return true; }
        public bool OnNextPage() { return true; }
        public bool OnPreview() { return true; }
        public void OnWhatsNew() { }
        public void OnUndo() { }
        public void OnRedo() { }
        public bool OnTabClicked(int id) { return true; }
        public void OnSelectionboxFocusChanged(int id) { }
        public void OnSelectionboxListChanged(int id, int count) { }
        public void OnSelectionboxCalloutCreated(int id) { }
        public void OnSelectionboxCalloutDestroyed(int id) { }
        public bool OnSubmitSelection(int id, object sel, int selType, ref string tag) { return true; }
        public int OnActiveXControlCreated(int id, bool status) { return 0; }
        public void OnSliderPositionChanged(int id, double val) { }
        public void OnSliderTrackingCompleted(int id, double val) { }
        public bool OnKeystroke(int wparam, int msg, int lparam, int id) { return false; }
        public void OnPopupMenuItem(int id) { }
        public void OnPopupMenuItemUpdate(int id, ref int retval) { }
        public void OnGainedFocus(int id) { }
        public int OnWindowFromHandleControlCreated(int id, bool status) { return 0; }
        public void OnListboxRMBUp(int id, int posX, int posY) { }

        // ================================================================
        // 备用：如果只读文本框不合用（比如觉得边框难看），把 Create() 里
        //   for (int i = 0; i < NOUT; i++) outBox[i] = TxtOut(g4, ID_OUT + i, i > 0);
        // 换成 LabOut，并把 outBox 的类型改成 IPropertyManagerPageLabel[]、
        // SetOut 里的 .Text 改成 .Caption 即可。
        // 关键是占位串 PAD：Label 的宽高按建页那一刻的文本算死，占位串必须不短于
        // 运行期可能出现的最长文本，否则后面赋的字符串会被裁进零宽区域，看起来就是"死的"。
        //
        // const string PAD = "量棒距   M  = 0000.0000 mm   (量棒 φ00.0000)  占位撑宽占位撑宽";
        //
        // IPropertyManagerPageLabel LabOut(IPropertyManagerPageGroup g, int id, bool tight)
        // {
        //     IPropertyManagerPageLabel lb = g.AddControl2(id,
        //         (short)swPropertyManagerPageControlType_e.swControlType_Label, PAD,
        //         (short)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge,
        //         CtlOpt(tight), "") as IPropertyManagerPageLabel;
        //     if (lb == null) { GearAddin.Log("  !!! 结果标签 " + id + " 建立失败"); return null; }
        //     lb.Style = (int)swPropMgrPageLabelStyle_e.swPropMgrPageLabelStyle_LeftText;
        //     lb.Height = LAB_H;                       // 只能在 Show2 之前设
        //     Layout(lb as IPropertyManagerPageControl, id, "结果标签", FULL_L, FULL_W, "结果行");
        //     return lb;
        // }
        // ================================================================
    }
}
