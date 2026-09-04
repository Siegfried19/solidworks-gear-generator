using System;
using System.Collections.Generic;

namespace GearWorks
{
    /// <summary>齿轮输入参数（长度单位 mm，角度单位 度）</summary>
    public class GearParams
    {
        public double Mn = 2;          // 法向模数
        public int Z = 20;             // 齿数
        public double AlfN = 20;       // 法向压力角
        public double Beta = 0;        // 螺旋角
        public double X = 0;           // 变位系数
        public double Bw = 20;         // 齿宽
        public double Ha = 1;          // 齿顶高系数
        public double Cc = 0.25;       // 顶隙系数
        public double Rho = 0.38;      // 刀尖圆角系数
        public double Bore = 12;       // 内孔直径，0=不做孔
        public bool IsInternal = false;// 内齿圈
        public double OdInt = 0;       // 内齿圈外径，0=自动
        public int Npts = 24;          // 每侧齿廓型值点数

        public GearParams Clone()
        {
            return (GearParams)MemberwiseClone();
        }
    }

    /// <summary>计算结果</summary>
    public class GearGeom
    {
        public double Mt, AlfT, BetaR, R, Rb, Ra, RaUse, Rf, ST, Psi;
        public double ZMin, XMin, Sa, W, M, Dp, Zv, Pb, Sc, Hc;
        public int K;
        public bool Pointed;
        public double D { get { return 2 * R; } }
        public double Da { get { return 2 * Ra; } }
        public double Df { get { return 2 * Rf; } }
        public double Db { get { return 2 * Rb; } }
    }

    public static class GearMath
    {
        public const double D2R = Math.PI / 180.0;

        public static double Inv(double a) { return Math.Tan(a) - a; }

        public static double InvInv(double v)
        {
            if (v <= 0) return 0;
            double a = Math.Pow(3.0 * v, 1.0 / 3.0);
            for (int i = 0; i < 80; i++)
            {
                double t = Math.Tan(a), d = t * t;
                if (d < 1e-12) break;
                double s = (t - a - v) / d;
                a -= s;
                if (Math.Abs(s) < 1e-15) break;
            }
            return a;
        }

        public static GearGeom Calc(GearParams p)
        {
            GearGeom g = new GearGeom();
            double b = p.Beta * D2R, an = p.AlfN * D2R;
            g.BetaR = b;
            g.Mt = p.Mn / Math.Cos(b);
            g.AlfT = Math.Atan(Math.Tan(an) / Math.Cos(b));
            g.R = g.Mt * p.Z / 2.0;
            g.Rb = g.R * Math.Cos(g.AlfT);
            g.Pb = Math.PI * g.Mt * Math.Cos(g.AlfT);
            g.Zv = p.Z / Math.Pow(Math.Cos(b), 3);

            if (p.IsInternal)
            {
                g.ST = g.Mt * Math.PI / 2.0 - 2.0 * p.X * p.Mn * Math.Tan(g.AlfT);
                g.Ra = g.R - (p.Ha - p.X) * p.Mn;
                g.Rf = g.R + (p.Ha + p.Cc + p.X) * p.Mn;
            }
            else
            {
                g.ST = g.Mt * Math.PI / 2.0 + 2.0 * p.X * p.Mn * Math.Tan(g.AlfT);
                g.Ra = g.R + (p.Ha + p.X) * p.Mn;
                g.Rf = g.R - (p.Ha + p.Cc - p.X) * p.Mn;
            }
            g.Psi = g.ST / (2.0 * g.R);

            double rPoint = g.Rb / Math.Cos(InvInv(g.Psi + Inv(g.AlfT)));
            g.Pointed = !p.IsInternal && (g.Ra >= rPoint - 1e-9);
            g.RaUse = g.Pointed ? rPoint * 0.999995 : g.Ra;
            if (g.Pointed) g.Ra = g.RaUse;
            g.Sa = 2.0 * g.RaUse * Math.Max(0, HalfAng(g, g.RaUse));

            double sa2 = Math.Sin(g.AlfT);
            g.ZMin = 2.0 * (p.Ha - p.X) / (sa2 * sa2);
            g.XMin = p.Ha - p.Z * sa2 * sa2 / 2.0;

            double hf = g.ST / g.D;
            g.Sc = g.D * Math.Sin(hf);
            g.Hc = Math.Abs(g.RaUse - g.R * Math.Cos(hf));

            double zp = p.Z * Inv(g.AlfT) / Inv(an);
            g.K = Math.Max(2, (int)Math.Round(zp * p.AlfN / 180.0 + 0.5, MidpointRounding.AwayFromZero));
            g.W = p.Mn * Math.Cos(an) * (Math.PI * (g.K - 0.5) + p.Z * Inv(g.AlfT))
                + 2.0 * p.X * p.Mn * Math.Sin(an);

            g.Dp = 1.68 * p.Mn;
            double q = g.Dp / (p.Mn * p.Z * Math.Cos(g.AlfT)), sh = 2.0 * p.X * Math.Tan(an) / p.Z;
            double iM = p.IsInternal
                ? Inv(g.AlfT) - q + Math.PI / (2.0 * p.Z) + sh
                : Inv(g.AlfT) + q - Math.PI / (2.0 * p.Z) + sh;
            if (iM > 1e-9)
            {
                double RM = g.Rb / Math.Cos(InvInv(iM));
                double baseM = (p.Z % 2 != 0) ? 2 * RM * Math.Cos(Math.PI / (2.0 * p.Z)) : 2 * RM;
                g.M = baseM + (p.IsInternal ? -g.Dp : g.Dp);
            }
            else g.M = double.NaN;

            return g;
        }

        /// <summary>半齿角（半径 th 处，从齿中心量）</summary>
        public static double HalfAng(GearGeom g, double th)
        {
            if (th < g.Rb) th = g.Rb;
            double q = g.Rb / th;
            if (q > 1) q = 1;
            return g.Psi + Inv(g.AlfT) - Inv(Math.Acos(q));
        }

        /// <summary>半齿廓结果</summary>
        public class Half
        {
            public List<double[]> Pts = new List<double[]>();  // 齿根圆起点 -> 过渡曲线 -> 渐开线 -> 齿顶圆
            public double LandA;     // 齿根平底半角(rad)
            public double Rc;        // 实际刀尖/过渡圆角半径
            public double RForm;     // 有效渐开线起始半径
            public bool Undercut;
            public double ThA;       // 齿顶半角
        }

        /// <summary>外齿轮：齿条刀具包络（渐开线 + 真实过渡曲线）</summary>
        public static Half FlankExternal(GearParams p, GearGeom g)
        {
            Half h = new Half();
            double at = g.AlfT, r = g.R, rb = g.Rb, m = p.Mn;
            double a0 = Math.PI * g.Mt / 4.0 - p.X * m * Math.Tan(at);
            double hh = (p.Ha + p.Cc - p.X) * m;
            double rLim = (a0 - hh * Math.Tan(at)) * Math.Cos(at) / (1.0 - Math.Sin(at));
            if (rLim < 0) rLim = 0;
            double rc = Math.Max(0, Math.Min(p.Rho * m, rLim));
            double cy = hh - rc;
            double cx = a0 - cy * Math.Tan(at) - rc / Math.Cos(at);
            if (cx < 0) cx = 0;
            h.Rc = rc;
            h.LandA = cx / r;
            double zeta = Math.PI / 2.0 - Math.PI / p.Z;

            h.Pts.Add(Pol(g.Rf, -Math.PI / p.Z + h.LandA));

            double uEnd = cy / Math.Tan(at) - cx;
            int nOut = Math.Max(8, p.Npts);
            int nt = Math.Max(60, nOut);
            int keep = Math.Max(1, (int)Math.Round((double)nt / nOut));
            double rJ = g.Rf;

            for (int j = 0; j <= nt; j++)
            {
                double u = -cx + (uEnd + cx) * j / nt;
                double[] pt = Troch(u, cx, cy, rc, zeta, r);
                double rr = Math.Sqrt(pt[0] * pt[0] + pt[1] * pt[1]);
                if (rr >= g.RaUse) { rJ = rr; break; }
                if (rr > rb)
                {
                    double th = -Math.Atan2(pt[1], pt[0]);
                    if (th < HalfAng(g, rr) - 1e-9)
                    { h.Pts.Add(pt); rJ = rr; h.Undercut = true; break; }
                }
                if (j % keep == 0 || j == nt) h.Pts.Add(pt);
                rJ = rr;
            }
            if (rJ < rb) rJ = rb;
            if (rJ > g.RaUse) rJ = g.RaUse;
            h.RForm = rJ;

            double aS = Math.Acos(Math.Min(1, rb / rJ));
            double aE = Math.Acos(Math.Min(1, rb / g.RaUse));
            if (aE > aS + 1e-6)
            {
                for (int i = 1; i <= p.Npts; i++)
                {
                    double ay = aS + (aE - aS) * i / p.Npts;
                    h.Pts.Add(Pol(rb / Math.Cos(ay), -(g.Psi + Inv(at) - Inv(ay))));
                }
            }
            h.ThA = Math.Max(0, HalfAng(g, g.RaUse));
            Dedupe(h.Pts);
            // 末点精确落在齿顶圆上，保证与齿顶圆弧无缝衔接
            h.Pts[h.Pts.Count - 1] = Pol(g.RaUse, -h.ThA);
            return h;
        }

        /// <summary>
        /// 去掉连续重合点，保留靠前的那个（首点必须严格等于齿根圆弧端点）。
        /// SolidWorks 草图容差约 1e-8 m (=1e-5 mm)，比这更近的两点会让 CreateSpline
        /// 生成退化曲线并被静默丢弃，从而在轮廓上开口子导致拉伸失败。
        /// </summary>
        static void Dedupe(List<double[]> pts)
        {
            const double TOL = 1e-5;   // mm
            for (int i = pts.Count - 1; i > 0; i--)
            {
                double dx = pts[i][0] - pts[i - 1][0];
                double dy = pts[i][1] - pts[i - 1][1];
                if (Math.Sqrt(dx * dx + dy * dy) < TOL) pts.RemoveAt(i);
            }
            if (pts.Count < 3) throw new Exception("齿廓点数不足 (" + pts.Count + ")，参数可能不合理。");
        }

        /// <summary>内齿圈：渐开线 + 齿根相切过渡圆弧</summary>
        public static Half FlankInternal(GearParams p, GearGeom g)
        {
            Half h = new Half();
            double at = g.AlfT, rb = g.Rb;
            double rc = Math.Min(p.Rho * p.Mn, (g.Rf - g.RaUse) * 0.35);
            h.Rc = rc;

            double lo = Math.Acos(Math.Min(1, rb / Math.Max(rb * 1.0001, g.RaUse)));
            double hi = Math.Acos(Math.Min(1, rb / g.Rf));
            double aJ = hi;
            for (int i = 0; i < 60; i++)
            {
                double mid = (lo + hi) / 2.0;
                double[] c = FilCen(g, mid, rc);
                if (Math.Sqrt(c[0] * c[0] + c[1] * c[1]) > (g.Rf - rc)) hi = mid; else lo = mid;
                aJ = mid;
            }
            double[] cen = FilCen(g, aJ, rc);
            double tc = Math.Atan2(cen[1], cen[0]);
            h.LandA = Math.Max(0, Math.PI / p.Z + tc);
            h.RForm = g.Rb / Math.Cos(aJ);

            h.Pts.Add(Pol(g.Rf, tc));

            double[] A = FlPt(g, aJ);
            // 圆心在齿根圆内侧(|cen| = rf - rc)，与齿根圆的切点在圆心的【外侧】方向上，
            // 即 cen + rc·cen/|cen|。原来写成 atan2(-cen) 是朝原点，差 180°，
            // 导致圆弧从错误的一端起扫，齿根出现倒钩。
            double a0 = Math.Atan2(cen[1], cen[0]);
            double a1 = Math.Atan2(A[1] - cen[1], A[0] - cen[0]);
            double da = a1 - a0;
            while (da > Math.PI) da -= 2 * Math.PI;
            while (da < -Math.PI) da += 2 * Math.PI;
            int nf = Math.Max(4, p.Npts / 4);
            for (int i = 1; i <= nf; i++)
            {
                double a = a0 + da * i / nf;
                h.Pts.Add(new double[] { cen[0] + rc * Math.Cos(a), cen[1] + rc * Math.Sin(a) });
            }
            double aT = Math.Acos(Math.Min(1, rb / Math.Max(rb * 1.0001, g.RaUse)));
            for (int i = 1; i <= p.Npts; i++)
                h.Pts.Add(FlPt(g, aJ + (aT - aJ) * i / p.Npts));

            h.ThA = Math.Max(0, HalfAng(g, g.RaUse));
            Dedupe(h.Pts);
            h.Pts[h.Pts.Count - 1] = Pol(g.RaUse, -h.ThA);
            return h;
        }

        public static Half Flank(GearParams p, GearGeom g)
        {
            return p.IsInternal ? FlankInternal(p, g) : FlankExternal(p, g);
        }

        // ---- 内部工具 ----
        static double[] FlPt(GearGeom g, double ay)
        {
            return Pol(g.Rb / Math.Cos(ay), -(g.Psi + Inv(g.AlfT) - Inv(ay)));
        }

        static double[] FilCen(GearGeom g, double ay, double rc)
        {
            double[] A = FlPt(g, ay);
            double[] B = FlPt(g, Math.Max(1e-4, ay - 1e-4));
            double tx = A[0] - B[0], ty = A[1] - B[1];
            double L = Math.Sqrt(tx * tx + ty * ty);
            if (L < 1e-9) L = 1e-9;
            tx /= L; ty /= L;
            double[] c1 = { A[0] + ty * rc, A[1] - tx * rc };
            double[] c2 = { A[0] - ty * rc, A[1] + tx * rc };
            double aA = Math.Atan2(A[1], A[0]);
            return (Math.Atan2(c1[1], c1[0]) < aA) ? c1 : c2;
        }

        /// <summary>刀尖圆角在展成运动中的包络点（齿中心坐标系）</summary>
        static double[] Troch(double u, double cx, double cy, double rc, double zeta, double r)
        {
            double wx = cx + u, wy = cy;
            double L = Math.Sqrt(wx * wx + wy * wy);
            if (L < 1e-9) L = 1e-9;
            double px = wx + rc * wx / L, py = wy + rc * wy / L;
            double f = u / r;
            double ax = px * Math.Cos(f) + (py - r) * Math.Sin(f);
            double ay = -px * Math.Sin(f) + (py - r) * Math.Cos(f);
            return new double[] {
                ax * Math.Cos(zeta) - ay * Math.Sin(zeta),
                ax * Math.Sin(zeta) + ay * Math.Cos(zeta) };
        }

        public static double[] Pol(double r, double t)
        {
            return new double[] { r * Math.Cos(t), r * Math.Sin(t) };
        }

        /// <summary>内孔轮廓点（光孔）</summary>
        public static List<double[]> BorePts(double r0, double[] ky, int n)
        {
            List<double[]> pts = new List<double[]>();
            if (ky == null)
            {
                for (int i = 0; i < n; i++)
                {
                    double a = 2 * Math.PI * i / n;
                    pts.Add(new double[] { r0 * Math.Cos(a), r0 * Math.Sin(a) });
                }
                return pts;
            }
            double hb = ky[0] / 2.0, top = r0 + ky[1];
            double ys = Math.Sqrt(Math.Max(0, r0 * r0 - hb * hb));
            double a1 = Math.Atan2(ys, hb), a2 = Math.Atan2(ys, -hb);
            double sweep = a1 - a2 + 2 * Math.PI;
            pts.Add(new double[] { hb, ys });
            for (int j = 1; j < n; j++)
            {
                double a = a1 - sweep * j / n;
                pts.Add(new double[] { r0 * Math.Cos(a), r0 * Math.Sin(a) });
            }
            pts.Add(new double[] { -hb, ys });
            pts.Add(new double[] { -hb, top });
            pts.Add(new double[] { hb, top });
            return pts;
        }

        /// <summary>校核提示文字</summary>
        public static string Warnings(GearParams p, GearGeom g, Half h)
        {
            string s = "";
            if (!p.IsInternal)
            {
                if (p.Z < g.ZMin - 1e-9)
                    s += string.Format("· 根切：齿数 {0} < 不根切最小齿数 {1:0.00}，齿根已切去一段渐开线。取 x ≥ {2:0.000} 可避免。\r\n",
                        p.Z, g.ZMin, g.XMin);
                if (g.Pointed)
                    s += string.Format("· 齿顶变尖，齿顶圆已收到 da = {0:0.0000} mm。\r\n", g.Da);
                else if (g.Sa < 0.25 * p.Mn)
                    s += string.Format("· 齿顶厚 {0:0.000} mm < 0.25mn，建议减小变位。\r\n", g.Sa);
                if (h != null && h.Rc < p.Rho * p.Mn - 1e-6)
                    s += string.Format("· 刀尖圆角已由 {0:0.000} 限制到 {1:0.000} mm（刀顶需保留平顶）。\r\n",
                        p.Rho * p.Mn, h.Rc);
                if (p.Bore > 0 && p.Bore / 2 > g.Rf * 0.7)
                    s += string.Format("· 内孔偏大，孔壁到齿根仅 {0:0.000} mm。\r\n", g.Rf - p.Bore / 2);
            }
            else
                s += "· 内齿圈齿根过渡按相切圆弧生成（渐开线齿廓部分精确）。\r\n";
            if (Math.Abs(p.Beta) > 1e-9)
                s += string.Format("· 斜齿：生成的是端面齿形直齿体。加【扭曲】特征，扭转角 = {0:0.000}°。\r\n",
                    p.Bw * Math.Tan(g.BetaR) / g.R / D2R);
            return s;
        }
    }
}
