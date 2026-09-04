using System;
using System.Collections.Generic;
using GearWorks;

class Test
{
    static double ThickAt(List<double[]> half, double rad)
    {
        // half 是半齿廓（负角侧），整齿厚度 = 2*|角度|*r
        for (int i = 0; i < half.Count - 1; i++)
        {
            double r1 = Math.Sqrt(half[i][0] * half[i][0] + half[i][1] * half[i][1]);
            double r2 = Math.Sqrt(half[i + 1][0] * half[i + 1][0] + half[i + 1][1] * half[i + 1][1]);
            if ((r1 - rad) * (r2 - rad) <= 0 && r1 != r2)
            {
                double t = (rad - r1) / (r2 - r1);
                double x = half[i][0] + (half[i + 1][0] - half[i][0]) * t;
                double y = half[i][1] + (half[i + 1][1] - half[i][1]) * t;
                return 2 * Math.Abs(Math.Atan2(y, x)) * rad;
            }
        }
        return -1;
    }

    static void Main()
    {
        GearParams p = new GearParams();
        p.Mn = 2; p.Z = 20; p.AlfN = 20; p.Beta = 0; p.X = 0; p.Npts = 40;
        GearGeom g = GearMath.Calc(p);
        GearMath.Half h = GearMath.Flank(p, g);
        Console.WriteLine("=== A. m2 z20 a20 x0 ===");
        Console.WriteLine("d ={0:0.0000}  expect 40", g.D);
        Console.WriteLine("db={0:0.0000}  expect 37.5877", g.Db);
        Console.WriteLine("da={0:0.0000}  expect 44", g.Da);
        Console.WriteLine("df={0:0.0000}  expect 35", g.Df);
        Console.WriteLine("k ={0}  W={1:0.0000}  expect k=3 W=15.3209", g.K, g.W);
        Console.WriteLine("M ={0:0.0000}  expect 44.4498", g.M);
        Console.WriteLine("zMin={0:0.000} expect 17.097   sa={1:0.0000}", g.ZMin, g.Sa);
        Console.WriteLine("s(pitch)={0:0.0000}  expect 3.1416", ThickAt(h.Pts, 20));
        Console.WriteLine("rc={0:0.0000} expect 0.76   land(half)={1:0.00000}  rForm={2:0.000}",
            h.Rc, h.LandA, h.RForm);
        Console.WriteLine("pts={0}  first r={1:0.0000} (rf={2:0.0000})  last r={3:0.0000} (ra={4:0.0000})",
            h.Pts.Count,
            Math.Sqrt(h.Pts[0][0] * h.Pts[0][0] + h.Pts[0][1] * h.Pts[0][1]), g.Rf,
            Math.Sqrt(h.Pts[h.Pts.Count - 1][0] * h.Pts[h.Pts.Count - 1][0]
                    + h.Pts[h.Pts.Count - 1][1] * h.Pts[h.Pts.Count - 1][1]), g.RaUse);
        Console.WriteLine("thA={0:0.00000}  half-tooth at tip check: {1:0.0000} vs sa/2/ra={2:0.0000}",
            h.ThA, h.ThA, g.Sa / 2 / g.RaUse);

        Console.WriteLine();
        Console.WriteLine("=== B. undercut ===");
        int[] zs = { 10, 12, 14, 17, 20, 40 };
        foreach (int z in zs)
        {
            GearParams q = new GearParams(); q.Z = z; q.Npts = 40;
            GearGeom gg = GearMath.Calc(q);
            GearMath.Half hh = GearMath.Flank(q, gg);
            Console.WriteLine("z={0,-3} zMin={1,6:0.00} undercut={2,-6} rForm={3:0.000} rb={4:0.000} s(pitch)={5:0.0000}",
                z, gg.ZMin, hh.Undercut, hh.RForm, gg.Rb, ThickAt(hh.Pts, gg.R));
        }

        Console.WriteLine();
        Console.WriteLine("=== C. pointed z8 x0.8 ===");
        GearParams p4 = new GearParams(); p4.Z = 8; p4.X = 0.8; p4.Npts = 40;
        GearGeom g4 = GearMath.Calc(p4);
        Console.WriteLine("pointed={0} da={1:0.000} sa={2:0.0000}", g4.Pointed, g4.Da, g4.Sa);

        Console.WriteLine();
        Console.WriteLine("=== D. helical beta20 ===");
        GearParams p5 = new GearParams(); p5.Beta = 20; p5.Npts = 40;
        GearGeom g5 = GearMath.Calc(p5);
        Console.WriteLine("mt={0:0.0000} expect 2.1284  at={1:0.0000} expect 21.1728  d={2:0.0000} expect 42.5671  zv={3:0.000}",
            g5.Mt, g5.AlfT / GearMath.D2R, g5.D, g5.Zv);

        Console.WriteLine();
        Console.WriteLine("=== E. internal z40 ===");
        GearParams p6 = new GearParams(); p6.Z = 40; p6.IsInternal = true; p6.Npts = 40;
        GearGeom g6 = GearMath.Calc(p6);
        GearMath.Half h6 = GearMath.Flank(p6, g6);
        Console.WriteLine("d={0:0.000} expect 80  da={1:0.000} expect 76  df={2:0.000} expect 85",
            g6.D, g6.Da, g6.Df);
        Console.WriteLine("s(pitch)={0:0.0000} expect 3.1416  land={1:0.00000}  first r={2:0.000} (rf={3:0.000})",
            ThickAt(h6.Pts, 40), h6.LandA,
            Math.Sqrt(h6.Pts[0][0] * h6.Pts[0][0] + h6.Pts[0][1] * h6.Pts[0][1]), g6.Rf);

        Console.WriteLine();
        Console.WriteLine("=== F. keyway / bore ===");
        double[] ky = GearMath.Keyway(12);
        Console.WriteLine("d12 -> b={0} t2={1} (expect 4 / 1.8)", ky[0], ky[1]);
        List<double[]> bp = GearMath.BorePts(6, ky, 60);
        Console.WriteLine("bore pts={0}  top y={1:0.000} expect 7.8", bp.Count, bp[bp.Count - 1][1]);

        Console.WriteLine();
        Console.WriteLine("=== G. 闭合性（相邻齿接缝）===");
        // 齿根圆弧终点 应等于 半齿廓起点
        double sp = -Math.PI / p.Z;
        double[] arcEnd = GearMath.Pol(g.Rf, sp + h.LandA);
        double dx = arcEnd[0] - h.Pts[0][0], dy = arcEnd[1] - h.Pts[0][1];
        Console.WriteLine("齿根弧端点 vs 齿廓起点 间隙 = {0:0.000000000} mm", Math.Sqrt(dx * dx + dy * dy));
        // 齿顶弧起点 应等于 半齿廓终点
        double[] tipS = GearMath.Pol(g.RaUse, -h.ThA);
        double[] last = h.Pts[h.Pts.Count - 1];
        Console.WriteLine("齿顶弧起点 vs 齿廓终点 间隙 = {0:0.000000000} mm",
            Math.Sqrt(Math.Pow(tipS[0] - last[0], 2) + Math.Pow(tipS[1] - last[1], 2)));
        // 下一齿的齿根弧起点 vs 本齿镜像终点
        double[] nextArcStart = GearMath.Pol(g.Rf, (2 * Math.PI / p.Z) - Math.PI / p.Z - h.LandA);
        double[] mirrorEnd = new double[] { h.Pts[0][0], -h.Pts[0][1] };
        Console.WriteLine("下一齿根弧起点 vs 本齿镜像起点 间隙 = {0:0.000000000} mm",
            Math.Sqrt(Math.Pow(nextArcStart[0] - mirrorEnd[0], 2) + Math.Pow(nextArcStart[1] - mirrorEnd[1], 2)));
    }
}
