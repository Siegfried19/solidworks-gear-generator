using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;

namespace GearWorks
{
    [ComVisible(true)]
    [Guid("8B5B2107-56A9-4D5E-8DCC-613583B82797")]
    // 必须是 AutoDispatch：SolidWorks 通过 IDispatch 按名字回调 ShowPage/EnablePage，
    // 用 ClassInterfaceType.None 会让 SetAddinCallbackInfo2 抛 InvalidCastException
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    [ProgId("GearWorks.GearAddin")]
    public class GearAddin : ISwAddin
    {
        public const string ADDIN_GUID = "{8B5B2107-56A9-4D5E-8DCC-613583B82797}";
        const string TITLE = "齿轮工具";
        const string DESC = "渐开线圆柱齿轮生成器";
        const int GRP_ID = 8801;

        ISldWorks swApp;
        int cookie;
        ICommandManager cmdMgr;
        GearPage page;

        // ======== 诊断日志 ========
        // %LOCALAPPDATA%\GearWorks\addin.log —— 出问题先看它，不要靠猜。
        public static readonly string LogPath = Path.Combine(
            Path.Combine(System.Environment.GetFolderPath(
                System.Environment.SpecialFolder.LocalApplicationData), "GearWorks"),
            "addin.log");

        static bool logDirReady;

        public static void Log(string msg)
        {
            try
            {
                if (!logDirReady)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                    logDirReady = true;
                }
                File.AppendAllText(LogPath,
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + "\r\n");
            }
            catch { }
        }

        static GearAddin()
        {
            Log("=== 静态构造：程序集已载入 CLR ===");
        }

        public GearAddin()
        {
            Log("实例构造：COM 已创建对象");
        }

        // ======== COM 注册（regasm 调用）========
        [ComRegisterFunction]
        public static void RegisterFunction(Type t)
        {
            try
            {
                RegistryKey k = Registry.LocalMachine.CreateSubKey(
                    @"SOFTWARE\SolidWorks\Addins\" + ADDIN_GUID);
                k.SetValue(null, 1, RegistryValueKind.DWord);   // 1 = SolidWorks 启动时加载
                k.SetValue("Description", DESC);
                k.SetValue("Title", TITLE);
                k.Close();
                RegistryKey k2 = Registry.CurrentUser.CreateSubKey(
                    @"Software\SolidWorks\AddInsStartup\" + ADDIN_GUID);
                k2.SetValue(null, 1, RegistryValueKind.DWord);
                k2.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine("注册失败: " + ex.Message);
            }
        }

        [ComUnregisterFunction]
        public static void UnregisterFunction(Type t)
        {
            try
            {
                Registry.LocalMachine.DeleteSubKeyTree(
                    @"SOFTWARE\SolidWorks\Addins\" + ADDIN_GUID, false);
                Registry.CurrentUser.DeleteSubKeyTree(
                    @"Software\SolidWorks\AddInsStartup\" + ADDIN_GUID, false);
            }
            catch { }
        }

        // ======== ISwAddin ========
        public bool ConnectToSW(object ThisSW, int Cookie)
        {
            Log("ConnectToSW 进入, cookie=" + Cookie);
            try
            {
                swApp = (ISldWorks)ThisSW;
                cookie = Cookie;
                Log("  SolidWorks 版本: " + swApp.RevisionNumber());
                swApp.SetAddinCallbackInfo2(0, this, cookie);
                Log("  SetAddinCallbackInfo2 ok");
                cmdMgr = swApp.GetCommandManager(cookie);
                Log("  GetCommandManager ok, null=" + (cmdMgr == null));
                AddCommands();
                Log("ConnectToSW 完成，返回 true");
                return true;
            }
            catch (Exception ex)
            {
                Log("!!! ConnectToSW 异常: " + ex.GetType().Name + ": " + ex.Message);
                Log("    " + ex.StackTrace);
                return true;   // 仍返回 true，避免 SolidWorks 直接取消勾选
            }
        }

        public bool DisconnectFromSW()
        {
            try { cmdMgr.RemoveCommandGroup2(GRP_ID, true); }
            catch { }
            page = null;
            cmdMgr = null;
            swApp = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            return true;
        }

        // ======== 工具栏 / 选项卡 ========
        void AddCommands()
        {
            int errs = 0;
            ICommandGroup cg = cmdMgr.CreateCommandGroup2(GRP_ID, TITLE, DESC, "生成渐开线齿轮", -1, true, ref errs);
            Log("  CreateCommandGroup2 -> null=" + (cg == null) + " errs=" + errs);
            if (cg == null) return;

            string[] icons = MakeIcons();
            Log("  MakeIcons -> " + (icons == null ? "null" : icons.Length + " 个, 首个=" + icons[0]));
            if (icons != null)
            {
                cg.IconList = icons;
                cg.MainIconList = icons;
            }

            int itemType = (int)swCommandItemType_e.swMenuItem | (int)swCommandItemType_e.swToolbarItem;
            int idx = cg.AddCommandItem2("生成齿轮", -1, "打开齿轮参数面板", "生成齿轮", 0,
                "ShowPage", "EnablePage", 0, itemType);
            cg.HasToolbar = true;
            cg.HasMenu = true;
            bool act = cg.Activate();
            Log("  AddCommandItem2 idx=" + idx + "  Activate=" + act);

            // 功能区选项卡
            try
            {
                int docType = (int)swDocumentTypes_e.swDocPART;
                CommandTab tab = cmdMgr.GetCommandTab(docType, TITLE);
                Log("  GetCommandTab 已存在? " + (tab != null));
                if (tab == null)
                {
                    tab = cmdMgr.AddCommandTab(docType, TITLE);
                    Log("  AddCommandTab -> null=" + (tab == null));
                    if (tab != null)
                    {
                        CommandTabBox box = (CommandTabBox)tab.AddCommandTabBox();
                        int[] ids = new int[1];
                        int[] styles = new int[1];
                        ids[0] = cg.get_CommandID(idx);
                        styles[0] = (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow;
                        bool ok = box.AddCommands(ids, styles);
                        Log("  选项卡按钮 cmdID=" + ids[0] + " AddCommands=" + ok);
                    }
                }
            }
            catch (Exception ex) { Log("  !!! 选项卡异常: " + ex.Message); }
        }

        // ======== 按钮回调 ========
        public void ShowPage()
        {
            try
            {
                // 每次都新建：SolidWorks 关闭页面时会释放底层 COM 对象，
                // 复用已释放的指针调 Show2() 会让 SolidWorks 直接闪退。
                page = new GearPage(swApp);
                page.Show();
            }
            catch (Exception ex)
            {
                swApp.SendMsgToUser2("打开面板失败：" + ex.Message,
                    (int)swMessageBoxIcon_e.swMbWarning, (int)swMessageBoxBtn_e.swMbOk);
            }
        }

        public int EnablePage()
        {
            ModelDoc2 m = (ModelDoc2)swApp.ActiveDoc;
            if (m == null) return 1;                                   // 无文档时允许（会自动新建零件）
            return m.GetType() == (int)swDocumentTypes_e.swDocPART ? 1 : 0;
        }

        // ======== 图标（用齿轮算法画出来）========
        static string[] MakeIcons()
        {
            try
            {
                string dir = Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "GearWorks");
                Directory.CreateDirectory(dir);
                int[] sizes = { 20, 32, 40, 64, 96, 128 };
                string[] files = new string[sizes.Length];
                for (int i = 0; i < sizes.Length; i++)
                {
                    string f = Path.Combine(dir, "gear" + sizes[i] + ".png");
                    files[i] = f;
                    if (!File.Exists(f)) DrawIcon(f, sizes[i]);
                }
                return files;
            }
            catch { return null; }
        }

        static void DrawIcon(string file, int s)
        {
            GearParams p = new GearParams();
            p.Mn = 1; p.Z = 9; p.AlfN = 20; p.X = 0.4; p.Npts = 10; p.Bore = 0;
            GearGeom g = GearMath.Calc(p);
            GearMath.Half h = GearMath.Flank(p, g);

            List<PointF> poly = new List<PointF>();
            double scale = (s * 0.46) / g.Ra;
            double dth = 2 * Math.PI / p.Z;
            for (int k = 0; k < p.Z; k++)
            {
                double bas = k * dth;
                for (int i = 0; i < h.Pts.Count; i++) poly.Add(Map(h.Pts[i], bas, scale, s, false));
                for (int i = h.Pts.Count - 1; i >= 0; i--) poly.Add(Map(h.Pts[i], bas, scale, s, true));
            }

            using (Bitmap bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb))
            using (Graphics gr = Graphics.FromImage(bmp))
            {
                gr.SmoothingMode = SmoothingMode.AntiAlias;
                gr.Clear(Color.Transparent);
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddPolygon(poly.ToArray());
                    path.AddEllipse(s * 0.5f - s * 0.13f, s * 0.5f - s * 0.13f, s * 0.26f, s * 0.26f);
                    path.FillMode = FillMode.Alternate;
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(255, 42, 96, 110)))
                        gr.FillPath(b, path);
                    using (Pen pen = new Pen(Color.FromArgb(255, 22, 58, 68), Math.Max(1f, s / 40f)))
                        gr.DrawPath(pen, path);
                }
                bmp.Save(file, ImageFormat.Png);
            }
        }

        static PointF Map(double[] pt, double bas, double scale, int s, bool mirror)
        {
            double y = mirror ? -pt[1] : pt[1];
            double c = Math.Cos(bas), si = Math.Sin(bas);
            double x2 = pt[0] * c - y * si, y2 = pt[0] * si + y * c;
            return new PointF((float)(s / 2.0 + x2 * scale), (float)(s / 2.0 - y2 * scale));
        }
    }
}
