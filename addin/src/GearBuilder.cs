using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace GearWorks
{
    /// <summary>
    /// 把齿廓落进 SolidWorks。
    /// 架构：齿坯(一个圆) -> 切掉一个齿槽(6 段实体) -> 圆周阵列 z 次 -> 内孔键槽。
    /// 这是 Fusion 360 官方 SpurGear、Onshape、FreeCAD、study-gears 一致采用的做法：
    /// 草图实体从 z*4 降到 6，每个特征独立可判成败，失败时能定位到具体哪一步。
    /// </summary>
    public static class GearBuilder
    {
        const double MM = 0.001;   // mm -> m

        public static string Build(ISldWorks swApp, GearParams p)
        {
            GearGeom g = GearMath.Calc(p);
            GearMath.Half h = GearMath.Flank(p, g);

            GearAddin.Log("Build: mn=" + p.Mn + " z=" + p.Z + " x=" + p.X + " bw=" + p.Bw
                + " bore=" + p.Bore + " key=" + p.Keyway + " int=" + p.IsInternal);
            GearAddin.Log("  d=" + F(g.D) + " da=" + F(g.Da) + " df=" + F(g.Df) + " rb=" + F(g.Rb)
                + "  半齿廓点数=" + h.Pts.Count + " landA=" + h.LandA.ToString("0.000000")
                + " thA=" + h.ThA.ToString("0.000000"));

            ModelDoc2 model = (ModelDoc2)swApp.ActiveDoc;
            if (model == null)
            {
                string tpl = swApp.GetUserPreferenceStringValue(
                    (int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
                model = (ModelDoc2)swApp.NewDocument(tpl, 0, 0, 0);
            }
            if (model == null) return "无法建立零件文档。";
            if (model.GetType() != (int)swDocumentTypes_e.swDocPART) return "请在零件文档中运行。";

            SketchManager sk = model.SketchManager;
            bool oldAdd = sk.AddToDB, oldDisp = sk.DisplayWhenAdded;
            // AddToDB 必须为 true：SolidWorks 默认吸附距离约 1mm，而齿根圆弧只有 0.1mm 量级，
            // 关掉它会把整个齿的端点吸成一坨。样条 API 本身不受此开关影响，永远精确入库。
            sk.AddToDB = true;
            sk.DisplayWhenAdded = false;

            try
            {
                // ---------- 1) 齿坯 ----------
                double rBlank = p.IsInternal ? (p.OdInt > 0 ? p.OdInt / 2 : g.Rf + 2 * p.Mn) : g.RaUse;
                string err = NewSketchOnPlane(model, sk, 0);
                if (err != null) return err;
                sk.CreateCircle(0, 0, 0, rBlank * MM, 0, 0);
                Feature blankSk = EndSketch(model, sk, "齿坯");
                if (blankSk == null) return "齿坯草图创建失败。";
                Feature blank = Extrude(model, blankSk, p.Bw);
                GearAddin.Log("  齿坯拉伸 -> " + (blank == null ? "失败" : "OK"));
                if (blank == null) return "齿坯拉伸失败。";

                // 内齿圈：先掏出齿顶圆的中心孔，剩下的环内表面就是齿顶
                if (p.IsInternal)
                {
                    err = NewSketchOnPlane(model, sk, 0);
                    if (err != null) return err;
                    sk.CreateCircle(0, 0, 0, g.RaUse * MM, 0, 0);
                    Feature holeSk = EndSketch(model, sk, "内齿中心孔");
                    if (holeSk == null) return "内齿中心孔草图失败。";
                    Feature hole = Cut(model, holeSk);
                    GearAddin.Log("  内齿中心孔 -> " + (hole == null ? "失败" : "OK"));
                    if (hole == null) return "内齿中心孔切除失败。";
                }

                // ---------- 2) 一个齿槽 ----------
                err = NewSketchOnPlane(model, sk, 0);
                if (err != null) return err;
                int nEnt = DrawOneToothSpace(sk, p, g, h);
                Feature cutSk = EndSketch(model, sk, "齿槽");
                if (cutSk == null) return "齿槽草图创建失败。";
                GearAddin.Log("  齿槽实体数=" + nEnt);
                Feature toothCut = Cut(model, cutSk);
                GearAddin.Log("  齿槽切除 -> " + (toothCut == null ? "失败" : "OK"));
                if (toothCut == null) return "齿槽切除失败（草图轮廓未闭合或自交）。";
                try { toothCut.Name = "齿槽"; }
                catch { }

                // ---------- 3) 圆周阵列 ----------
                if (p.Z > 1)
                {
                    Feature axis = MakeAxis(model);
                    GearAddin.Log("  基准轴 -> " + (axis == null ? "失败" : axis.Name));
                    if (axis == null) return "建立旋转轴失败。";

                    model.ClearSelection2(true);
                    axis.Select2(false, 1);        // mark 1 = 方向轴
                    toothCut.Select2(true, 4);     // mark 4 = 被阵列的特征
                    Feature pat = model.FeatureManager.FeatureCircularPattern5(
                        p.Z, 2 * Math.PI, false, "NULL",
                        true,   // GeometryPattern：纯几何复制，最快最稳
                        true,   // EqualSpacing
                        false, false, false, false, 0, 0, "NULL", false);
                    GearAddin.Log("  圆周阵列 x" + p.Z + " -> " + (pat == null ? "失败" : "OK"));
                    if (pat == null) return "圆周阵列失败。";
                    try { pat.Name = "齿_z" + p.Z + "_m" + p.Mn; }
                    catch { }
                }

                // ---------- 4) 内孔 + 键槽 ----------
                if (p.Bore > 0 && !p.IsInternal && p.Bore / 2 < g.Rf * 0.98)
                {
                    err = NewSketchOnPlane(model, sk, 0);
                    if (err != null) return err;
                    DrawBore(sk, p);
                    Feature boreSk = EndSketch(model, sk, "内孔");
                    if (boreSk != null)
                    {
                        Feature bore = Cut(model, boreSk);
                        GearAddin.Log("  内孔切除 -> " + (bore == null ? "失败" : "OK"));
                    }
                }

                WriteProps(model, p, g);
                model.ViewZoomtofit2();
                model.ClearSelection2(true);
                GearAddin.Log("  ===== 生成完成 =====");
                return null;
            }
            finally
            {
                sk.AddToDB = oldAdd;
                sk.DisplayWhenAdded = oldDisp;
                try { model.GraphicsRedraw2(); }
                catch { }
            }
        }

        // ================= 草图辅助 =================

        /// <summary>选基准面并进入新草图。planeIdx: 0=前视 1=上视 2=右视（按特征顺序，与界面语言无关）</summary>
        static string NewSketchOnPlane(ModelDoc2 model, SketchManager sk, int planeIdx)
        {
            model.ClearSelection2(true);
            Feature pl = GetPlane(model, planeIdx);
            if (pl == null) return "找不到基准面 #" + planeIdx + "。";
            pl.Select2(false, 0);
            sk.InsertSketch(true);
            return null;
        }

        /// <summary>退出草图，自检闭合轮廓数，返回该草图特征（已选中，mark 0）</summary>
        static Feature EndSketch(ModelDoc2 model, SketchManager sk, string tag)
        {
            Sketch sko = (Sketch)sk.ActiveSketch;
            if (sko != null)
            {
                try
                {
                    int nc = sko.GetSketchContourCount();
                    object[] segs = (object[])sko.GetSketchSegments();
                    GearAddin.Log("  [" + tag + "] 实体=" + (segs == null ? 0 : segs.Length)
                        + "  闭合轮廓数=" + nc);
                }
                catch (Exception ex) { GearAddin.Log("  [" + tag + "] 自检失败: " + ex.Message); }
            }
            sk.InsertSketch(true);
            Feature f = (Feature)model.FeatureByPositionReverse(0);
            if (f == null) return null;
            model.ClearSelection2(true);
            // FeatureExtrusion3/FeatureCut4 要求 profile 草图以 mark 0 选中
            bool ok = f.Select2(false, 0);
            if (!ok) { GearAddin.Log("  [" + tag + "] 选中草图失败"); return null; }
            return f;
        }

        static Feature GetPlane(ModelDoc2 model, int idx)
        {
            int n = 0;
            Feature f = (Feature)model.FirstFeature();
            while (f != null)
            {
                if (f.GetTypeName2() == "RefPlane")
                {
                    if (n == idx) return f;
                    n++;
                }
                f = (Feature)f.GetNextFeature();
            }
            return null;
        }

        /// <summary>上视面 ∩ 右视面 = Z 轴，即齿轮回转轴</summary>
        static Feature MakeAxis(ModelDoc2 model)
        {
            model.ClearSelection2(true);
            Feature t = GetPlane(model, 1), r = GetPlane(model, 2);
            if (t == null || r == null) return null;
            t.Select2(false, 0);
            r.Select2(true, 0);
            bool ok = model.InsertAxis2(true);
            if (!ok) return null;
            return (Feature)model.FeatureByPositionReverse(0);
        }

        // ================= 特征 =================

        static Feature Extrude(ModelDoc2 model, Feature profile, double depthMm)
        {
            try
            {
                return model.FeatureManager.FeatureExtrusion3(
                    true, false, false,
                    (int)swEndConditions_e.swEndCondBlind, 0,
                    depthMm * MM, 0,
                    false, false, false, false,
                    0, 0,
                    false, false, false, false,
                    true,   // Merge
                    true,   // UseFeatScope
                    true,   // UseAutoSelect
                    (int)swStartConditions_e.swStartSketchPlane, 0, false);
            }
            catch (Exception ex)
            {
                GearAddin.Log("  拉伸抛异常: " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        static Feature Cut(ModelDoc2 model, Feature profile)
        {
            try
            {
                return model.FeatureManager.FeatureCut4(
                    true, false, true,
                    (int)swEndConditions_e.swEndCondThroughAll,
                    (int)swEndConditions_e.swEndCondThroughAll,
                    0.01, 0.01,
                    false, false, false, false,
                    0, 0,
                    false, false, false, false,
                    false,  // NormalCut
                    true,   // UseFeatScope
                    true,   // UseAutoSelect
                    false, true, false,
                    (int)swStartConditions_e.swStartSketchPlane, 0, false, false);
            }
            catch (Exception ex)
            {
                GearAddin.Log("  切除抛异常: " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        // ================= 齿槽轮廓 =================

        /// <summary>
        /// 画一个齿槽的封闭轮廓。齿槽中心在 -PI/z，左边界是齿(中心 0)的左齿廓，
        /// 右边界是齿(中心 -2PI/z)的右齿廓，外(内)侧用一段圆弧封口到坯体之外。
        /// 6 段实体：根弧 + 左样条 + 封口线 + 封口弧 + 封口线 + 右样条。
        /// </summary>
        static int DrawOneToothSpace(SketchManager sk, GearParams p, GearGeom g, GearMath.Half h)
        {
            int n = h.Pts.Count, cnt = 0;
            double dth = 2 * Math.PI / p.Z;
            double spc = -Math.PI / p.Z;          // 齿槽中心角
            // 封口半径：外齿取齿顶之外，内齿取齿顶之内
            double rClose = p.IsInternal ? g.RaUse - Math.Max(0.5, p.Mn) : g.RaUse + Math.Max(0.5, p.Mn);

            double aL = -h.ThA;                   // 左齿(中心0)齿顶侧边角
            double aR = -dth + h.ThA;             // 右齿(中心-dth)齿顶侧边角

            double[] A = GearMath.Pol(g.Rf, spc - h.LandA);
            double[] B = GearMath.Pol(g.Rf, spc + h.LandA);

            // 1) 齿根圆弧 A -> B（逆时针）
            if (h.LandA > 1e-9)
            {
                if (Arc(sk, 0, 0, A, B, 1)) cnt++;
                else GearAddin.Log("  根弧创建失败");
            }

            // 2) 左齿廓样条 B -> C（齿根 -> 齿顶）
            if (Spline(sk, h.Pts, 0, false)) cnt++;
            else GearAddin.Log("  左样条创建失败");

            double[] C = GearMath.Pol(g.RaUse, aL);
            double[] Co = GearMath.Pol(rClose, aL);
            double[] Do = GearMath.Pol(rClose, aR);
            double[] Dd = GearMath.Pol(g.RaUse, aR);

            // 3) C -> Co 封口线
            if (Line(sk, C, Co)) cnt++; else GearAddin.Log("  封口线1 失败");
            // 4) Co -> Do 封口弧（角度递减，顺时针）
            if (Arc(sk, 0, 0, Co, Do, -1)) cnt++; else GearAddin.Log("  封口弧 失败");
            // 5) Do -> Dd 封口线
            if (Line(sk, Do, Dd)) cnt++; else GearAddin.Log("  封口线2 失败");
            // 6) 右齿廓样条 Dd -> A（齿顶 -> 齿根，镜像并旋到 -dth）
            if (Spline(sk, h.Pts, -dth, true)) cnt++;
            else GearAddin.Log("  右样条创建失败");

            return cnt;
        }

        static bool Arc(SketchManager sk, double cx, double cy, double[] a, double[] b, int dir)
        {
            object o = sk.CreateArc(cx * MM, cy * MM, 0,
                a[0] * MM, a[1] * MM, 0, b[0] * MM, b[1] * MM, 0, (short)dir);
            return o != null;
        }

        static bool Line(SketchManager sk, double[] a, double[] b)
        {
            object o = sk.CreateLine(a[0] * MM, a[1] * MM, 0, b[0] * MM, b[1] * MM, 0);
            return o != null;
        }

        /// <summary>画半齿廓样条。mirror=true 时 y 取反并倒序（即齿顶->齿根）</summary>
        static bool Spline(SketchManager sk, List<double[]> pts, double rot, bool mirror)
        {
            int n = pts.Count;
            double[] pd = new double[3 * n];
            for (int i = 0; i < n; i++)
            {
                double[] s = mirror ? pts[n - 1 - i] : pts[i];
                double y = mirror ? -s[1] : s[1];
                double c = Math.Cos(rot), sn = Math.Sin(rot);
                pd[3 * i] = (s[0] * c - y * sn) * MM;
                pd[3 * i + 1] = (s[0] * sn + y * c) * MM;
                pd[3 * i + 2] = 0;
            }
            object o = sk.CreateSpline(pd);
            return o != null;
        }

        // ================= 内孔 =================

        static void DrawBore(SketchManager sk, GearParams p)
        {
            double r0 = p.Bore / 2.0;
            double[] ky = p.Keyway ? GearMath.Keyway(p.Bore) : null;
            if (ky == null)
            {
                sk.CreateCircle(0, 0, 0, r0 * MM, 0, 0);
                return;
            }
            double hb = ky[0] / 2.0, top = r0 + ky[1];
            double ys = Math.Sqrt(Math.Max(0, r0 * r0 - hb * hb));
            sk.CreateArc(0, 0, 0, hb * MM, ys * MM, 0, -hb * MM, ys * MM, 0, (short)-1);
            sk.CreateLine(-hb * MM, ys * MM, 0, -hb * MM, top * MM, 0);
            sk.CreateLine(-hb * MM, top * MM, 0, hb * MM, top * MM, 0);
            sk.CreateLine(hb * MM, top * MM, 0, hb * MM, ys * MM, 0);
        }

        // ================= 自定义属性 =================

        static void WriteProps(ModelDoc2 model, GearParams p, GearGeom g)
        {
            try
            {
                CustomPropertyManager cpm = model.Extension.get_CustomPropertyManager("");
                int rep = (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue;
                int txt = (int)swCustomInfoType_e.swCustomInfoText;
                cpm.Add3("模数mn", txt, F(p.Mn), rep);
                cpm.Add3("齿数z", txt, p.Z.ToString(), rep);
                cpm.Add3("压力角", txt, F(p.AlfN), rep);
                cpm.Add3("螺旋角", txt, F(p.Beta), rep);
                cpm.Add3("变位系数x", txt, F(p.X), rep);
                cpm.Add3("齿宽b", txt, F(p.Bw), rep);
                cpm.Add3("分度圆直径d", txt, F(g.D), rep);
                cpm.Add3("齿顶圆直径da", txt, F(g.Da), rep);
                cpm.Add3("齿根圆直径df", txt, F(g.Df), rep);
                cpm.Add3("基圆直径db", txt, F(g.Db), rep);
                cpm.Add3("跨齿数k", txt, g.K.ToString(), rep);
                cpm.Add3("公法线W", txt, F(g.W), rep);
                cpm.Add3("量棒距M", txt, F(g.M), rep);
            }
            catch { }
        }

        static string F(double v) { return v.ToString("0.####"); }
    }
}
