using System;
using GearWorks;

class Sets
{
    static void Show(string name, GearParams p)
    {
        GearGeom g = GearMath.Calc(p);
        GearMath.Half h = GearMath.Flank(p, g);
        Console.WriteLine("--- " + name + " ---");
        Console.WriteLine(string.Format("  mn={0} z={1} a={2} b(螺旋)={3} x={4} 齿宽={5} 内孔={6}",
            p.Mn, p.Z, p.AlfN, p.Beta, p.X, p.Bw, p.Bore));
        Console.WriteLine(string.Format("  分度圆 d={0:0.0000}   齿顶圆 da={1:0.0000}   齿根圆 df={2:0.0000}   基圆 db={3:0.0000}",
            g.D, g.Da, g.Df, g.Db));
        Console.WriteLine(string.Format("  跨齿数 k={0}   公法线 W={1:0.0000}   量棒 dp={2:0.000} 量棒距 M={3:0.0000}",
            g.K, g.W, g.Dp, g.M));
        Console.WriteLine(string.Format("  齿顶厚 sa={0:0.0000}   不根切最小齿数={1:0.00}   根切={2}",
            g.Sa, g.ZMin, p.Z < g.ZMin ? "是" : "否"));
        Console.WriteLine(string.Format("  半齿廓点数={0}  齿根过渡圆角={1:0.0000}", h.Pts.Count, h.Rc));
        Console.WriteLine();
    }

    static void Main()
    {
        GearParams a = new GearParams();
        a.Mn = 1.5; a.Z = 35; a.AlfN = 20; a.Beta = 0; a.X = 0;
        a.Bw = 15; a.Bore = 15; a.Npts = 24;
        Show("A  细齿", a);

        GearParams b = new GearParams();
        b.Mn = 2; b.Z = 12; b.X = 0; b.Bw = 20; b.Bore = 8; b.Npts = 24;
        Show("B  根切演示 (x=0)", b);

        GearParams c = new GearParams();
        c.Mn = 2; c.Z = 12; c.X = 0.5; c.Bw = 20; c.Bore = 8; c.Npts = 24;
        Show("C  变位修正 (x=0.5) —— 与 B 对比", c);

        GearParams d = new GearParams();
        d.Mn = 2; d.Z = 40; d.X = 0; d.Bw = 20; d.Bore = 20; d.Npts = 24;
        Show("D  与默认 z=20 配对的大齿轮", d);

        GearParams e = new GearParams();
        e.Mn = 3; e.Z = 18; e.AlfN = 20; e.Beta = 15; e.X = 0.2;
        e.Bw = 30; e.Bore = 25; e.Npts = 24;
        Show("E  斜齿 + 变位 (重载)", e);
    }
}
