using System;
using System.Collections.Generic;
using GearWorks;

// 核对 DrawOneToothSpace 的齿槽轮廓：闭合性 + 自交，并精确报出自交位置。
class Chain
{
    static double[] Rot(double x, double y, double a)
    {
        double c = Math.Cos(a), s = Math.Sin(a);
        return new double[] { x * c - y * s, x * s + y * c };
    }
    static double Dst(double[] a, double[] b)
    {
        double dx = a[0] - b[0], dy = a[1] - b[1];
        return Math.Sqrt(dx * dx + dy * dy);
    }
    static double Cr(double[] a, double[] b, double[] c)
    {
        return (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]);
    }
    static bool Cross(double[] p1, double[] p2, double[] p3, double[] p4)
    {
        double d1 = Cr(p3, p4, p1), d2 = Cr(p3, p4, p2), d3 = Cr(p1, p2, p3), d4 = Cr(p1, p2, p4);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }
    static string Pol(double[] p)
    {
        double r = Math.Sqrt(p[0] * p[0] + p[1] * p[1]);
        double a = Math.Atan2(p[1], p[0]) * 180 / Math.PI;
        return "r=" + r.ToString("0.000") + " θ=" + a.ToString("0.000") + "°";
    }

    static bool Run(GearParams p, string title)
    {
        GearGeom g = GearMath.Calc(p);
        GearMath.Half h = GearMath.Flank(p, g);
        int n = h.Pts.Count;
        double dth = 2 * Math.PI / p.Z;
        double spc = -Math.PI / p.Z;
        double rClose = p.IsInternal ? g.RaUse - Math.Max(0.5, p.Mn) : g.RaUse + Math.Max(0.5, p.Mn);
        double aL = -h.ThA, aR = -dth + h.ThA;

        List<double[]> poly = new List<double[]>();
        List<string> tag = new List<string>();
        Action<double[], string> add = delegate (double[] q, string t)
        {
            // 闭环处首尾是同一个点，不能重复计入，否则会把相邻两段判成相交
            if (poly.Count > 2 && Dst(poly[0], q) < 1e-9) return;
            if (poly.Count == 0 || Dst(poly[poly.Count - 1], q) > 1e-12) { poly.Add(q); tag.Add(t); }
        };

        if (h.LandA > 1e-9)
            for (int i = 0; i <= 16; i++)
                add(GearMath.Pol(g.Rf, (spc - h.LandA) + 2 * h.LandA * i / 16.0), "根弧");
        for (int i = 0; i < n; i++) add(new double[] { h.Pts[i][0], h.Pts[i][1] }, "左样条");
        add(GearMath.Pol(rClose, aL), "封口线1");
        for (int i = 1; i <= 24; i++) add(GearMath.Pol(rClose, aL + (aR - aL) * i / 24.0), "封口弧");
        add(GearMath.Pol(g.RaUse, aR), "封口线2");
        for (int i = 0; i < n; i++)
        {
            double[] s0 = h.Pts[n - 1 - i];
            add(Rot(s0[0], -s0[1], -dth), "右样条");
        }

        double worstGap = Dst(poly[poly.Count - 1], poly[0]);
        int hits = 0;
        string detail = "";
        for (int i = 0; i < poly.Count; i++)
        {
            double[] a1 = poly[i], a2 = poly[(i + 1) % poly.Count];
            for (int j = i + 2; j < poly.Count; j++)
            {
                if (i == 0 && j == poly.Count - 1) continue;
                double[] b1 = poly[j], b2 = poly[(j + 1) % poly.Count];
                if (Cross(a1, a2, b1, b2))
                {
                    hits++;
                    if (hits <= 3)
                        detail += "\n    ✗ [" + tag[i] + " #" + i + " " + Pol(a1) + "]  ×  ["
                                + tag[j] + " #" + j + " " + Pol(b1) + "]";
                }
            }
        }

        bool ok = hits == 0;
        Console.WriteLine(string.Format("{0,-26} 点={1,3}  闭合间隙={2:0.000000000000}  自交={3}{4}",
            title, poly.Count, worstGap, hits, detail));
        return ok;
    }

    static void Main()
    {
        int bad = 0;
        GearParams a = new GearParams(); a.Mn = 2; a.Z = 20; a.Npts = 24;
        if (!Run(a, "外齿 m2 z20 x0")) bad++;
        GearParams b = new GearParams(); b.Mn = 2; b.Z = 12; b.Npts = 24;
        if (!Run(b, "外齿 m2 z12 根切")) bad++;
        GearParams c = new GearParams(); c.Mn = 2; c.Z = 12; c.X = 0.5; c.Npts = 24;
        if (!Run(c, "外齿 m2 z12 x0.5")) bad++;
        GearParams e = new GearParams(); e.Mn = 2; e.Z = 8; e.X = 0.8; e.Npts = 24;
        if (!Run(e, "外齿 m2 z8 变尖")) bad++;
        GearParams f = new GearParams(); f.Mn = 2; f.Z = 60; f.Npts = 24;
        if (!Run(f, "外齿 m2 z60")) bad++;
        GearParams m = new GearParams(); m.Mn = 3; m.Z = 18; m.Beta = 15; m.X = 0.2; m.Npts = 24;
        if (!Run(m, "斜齿 m3 z18 β15 x0.2")) bad++;

        Console.WriteLine();
        GearParams d1 = new GearParams(); d1.Mn = 2; d1.Z = 40; d1.IsInternal = true; d1.Npts = 24;
        if (!Run(d1, "内齿 m2 z40")) bad++;
        GearParams d2 = new GearParams(); d2.Mn = 2; d2.Z = 60; d2.IsInternal = true; d2.Npts = 24;
        if (!Run(d2, "内齿 m2 z60")) bad++;
        GearParams d3 = new GearParams(); d3.Mn = 1.5; d3.Z = 80; d3.IsInternal = true; d3.Npts = 24;
        if (!Run(d3, "内齿 m1.5 z80")) bad++;

        Console.WriteLine();
        Console.WriteLine(bad == 0 ? "全部通过" : (bad + " 个用例有问题"));
    }
}
