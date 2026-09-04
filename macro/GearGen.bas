Attribute VB_Name = "GearGen"
'=====================================================================
' 渐开线圆柱齿轮生成器  for SolidWorks  (VBA 宏)
'---------------------------------------------------------------------
' 齿条刀具包络法生成真实齿廓：渐开线段 + 刀尖圆角扫出的过渡曲线
' 支持 外齿轮 / 内齿圈、变位、内孔、GB/T 1095 平键槽
' 每个齿 = 齿根圆弧 + 左齿廓样条 + 齿顶圆弧 + 右齿廓样条，一次拉伸成型
'---------------------------------------------------------------------
' 用法：工具-宏-新建 → 保存 → 在 VBA 编辑器里粘贴本文件全部内容 → F5
'=====================================================================
Option Explicit

Const PI As Double = 3.14159265358979

' ============ 参数（改这里，或运行时在弹框里改） ============
Dim mn      As Double   ' 法向模数
Dim zz      As Long     ' 齿数
Dim alfN    As Double   ' 法向压力角 (度)
Dim beta    As Double   ' 螺旋角 (度)，直齿填 0
Dim xs      As Double   ' 变位系数
Dim bw      As Double   ' 齿宽 (mm)
Dim haC     As Double   ' 齿顶高系数
Dim ccC     As Double   ' 顶隙系数
Dim rhoC    As Double   ' 刀尖圆角系数
Dim bore    As Double   ' 内孔直径 (mm)，0 = 不做孔
Dim useKey  As Boolean  ' 是否加平键槽 (GB/T 1095)
Dim gType   As String   ' "EXT" 外齿轮  /  "INT" 内齿圈
Dim odInt   As Double   ' 内齿圈外径 (mm)，0 = 自动取 df + 4*mn
Dim nPts    As Long     ' 每侧齿廓样条点数

Const ASK As Boolean = True     ' True = 运行时弹框确认参数

' ---- 计算量 ----
Dim mt As Double, alfT As Double, rr As Double, rb As Double
Dim ra As Double, rf As Double, sT As Double, psi As Double
Dim landA As Double, thA As Double, rcut As Double
Dim isInt As Boolean, warnTxt As String

Dim swApp As Object

'=====================================================================
Sub main()
    Dim swModel As Object, swSk As Object, swFeat As Object
    Dim i As Long

    Set swApp = Application.SldWorks
    Call Defaults
    If ASK Then If Not AskParams() Then Exit Sub
    If Not Geometry() Then Exit Sub

    Set swModel = swApp.ActiveDoc
    If swModel Is Nothing Then
        Set swModel = swApp.NewDocument(swApp.GetUserPreferenceStringValue(8), 0, 0, 0)
    End If
    If swModel Is Nothing Then MsgBox "无法建立零件文档。": Exit Sub
    If swModel.GetType <> 1 Then MsgBox "请在【零件】文档中运行本宏。": Exit Sub

    swModel.ClearSelection2 True
    If Not SelFront(swModel) Then MsgBox "找不到前视基准面。": Exit Sub

    ' ---------- 草图 1：齿廓 ----------
    Set swSk = swModel.SketchManager
    swSk.InsertSketch True
    swSk.AddToDB = True
    swSk.DisplayWhenAdded = False
    If isInt Then
        Dim od As Double
        od = odInt
        If od <= 0 Then od = 2 * rf + 4 * mn
        swSk.CreateCircleByRadius 0, 0, 0, od / 2000#
    End If
    Call DrawTeeth(swSk)
    swSk.AddToDB = False
    swSk.DisplayWhenAdded = True
    swSk.InsertSketch True

    ' ---------- 拉伸 ----------
    Set swFeat = swModel.FeatureManager.FeatureExtrusion2( _
        True, False, False, 0, 0, bw / 1000#, 0, False, False, False, False, _
        0, 0, False, False, False, False, True, True, True, 0, 0, False)
    If swFeat Is Nothing Then MsgBox "拉伸失败，请检查参数。": Exit Sub
    swFeat.Name = "齿轮_z" & zz & "_m" & mn

    ' ---------- 草图 2：内孔 + 键槽 ----------
    If bore > 0 And Not isInt Then
        swModel.ClearSelection2 True
        Call SelFront(swModel)
        swSk.InsertSketch True
        swSk.AddToDB = True
        Call DrawBore(swSk)
        swSk.AddToDB = False
        swSk.InsertSketch True
        swModel.FeatureManager.FeatureCut2 True, False, True, 1, 1, 0.01, 0.01, _
            False, False, False, False, 0, 0, False, False, False, False, _
            True, True, True, False, False, False
    End If

    swModel.ViewZoomtofit2
    swModel.ClearSelection2 True
    Call WriteProps(swModel)
    MsgBox Report(), vbInformation, "齿轮生成完毕"
End Sub

'=====================================================================
' 默认参数
Sub Defaults()
    mn = 2:    zz = 20:   alfN = 20: beta = 0:  xs = 0
    bw = 20:   haC = 1:   ccC = 0.25: rhoC = 0.38
    bore = 12: useKey = True: gType = "EXT": odInt = 0: nPts = 20
End Sub

' 参数输入面板（两步 + 结果确认）
Function AskParams() As Boolean
    Dim s As String, p() As String
    AskParams = False
    On Error GoTo bad

    ' 第 1 步：齿形参数。支持直接粘贴网页面板复制的整串参数
    s = mn & "," & zz & "," & alfN & "," & beta & "," & xs
    s = InputBox( _
        "【1/2】齿形参数     模数, 齿数, 压力角, 螺旋角, 变位系数" & vbCrLf & vbCrLf & _
        "· 直齿螺旋角填 0" & vbCrLf & _
        "· 可直接粘贴网页面板复制的整串参数（8 项），将跳过第 2 步" & vbCrLf & _
        "· 齿顶高/顶隙/刀尖圆角系数在代码 Defaults 中修改", _
        "齿轮参数  1/2  —  齿形", s)
    If Len(Trim(s)) = 0 Then Exit Function
    p = Split(s, ",")
    mn = CDbl(Trim(p(0))): zz = CLng(Trim(p(1))): alfN = CDbl(Trim(p(2)))
    beta = CDbl(Trim(p(3))): xs = CDbl(Trim(p(4)))

    If UBound(p) >= 7 Then                  ' 整串参数，一步到位
        bw = CDbl(Trim(p(5))): bore = CDbl(Trim(p(6))): gType = UCase(Trim(p(7)))
        If UBound(p) >= 8 Then useKey = (Trim(p(8)) = "1")
    Else
        ' 第 2 步：结构参数
        s = bw & "," & bore & "," & IIf(useKey, 1, 0) & "," & gType
        s = InputBox( _
            "【2/2】结构参数     齿宽, 内孔直径, 键槽, 类型" & vbCrLf & vbCrLf & _
            "· 内孔直径填 0 = 不做孔" & vbCrLf & _
            "· 键槽：1 = 按 GB/T 1095 加平键槽，0 = 光孔" & vbCrLf & _
            "· 类型：EXT = 外齿轮，INT = 内齿圈", _
            "齿轮参数  2/2  —  结构", s)
        If Len(Trim(s)) = 0 Then Exit Function
        p = Split(s, ",")
        bw = CDbl(Trim(p(0))): bore = CDbl(Trim(p(1)))
        useKey = (Trim(p(2)) = "1")
        If UBound(p) >= 3 Then gType = UCase(Trim(p(3)))
    End If

    ' 计算并预览确认
    If Not Geometry() Then Exit Function
    If MsgBox(Report() & vbCrLf & "确定要生成吗？", vbOKCancel + vbInformation, _
        "确认参数") <> vbOK Then Exit Function
    AskParams = True
    Exit Function
bad:
    MsgBox "参数格式不对（请用英文逗号分隔），请重新运行。", vbExclamation
End Function

'=====================================================================
' 几何计算
Function Geometry() As Boolean
    Geometry = False: warnTxt = ""
    If mn <= 0 Or zz < 4 Or bw <= 0 Then MsgBox "模数/齿数/齿宽 数值不合理。": Exit Function
    isInt = (gType = "INT")
    Dim b As Double, an As Double
    b = beta * PI / 180#: an = alfN * PI / 180#
    mt = mn / Cos(b)
    alfT = Atn(Tan(an) / Cos(b))
    rr = mt * zz / 2#
    rb = rr * Cos(alfT)
    If isInt Then
        sT = mt * PI / 2# - 2# * xs * mn * Tan(alfT)
        ra = rr - (haC - xs) * mn
        rf = rr + (haC + ccC + xs) * mn
    Else
        sT = mt * PI / 2# + 2# * xs * mn * Tan(alfT)
        ra = rr + (haC + xs) * mn
        rf = rr - (haC + ccC - xs) * mn
    End If
    psi = sT / (2# * rr)

    ' 齿顶变尖检查
    Dim rPt As Double
    rPt = rb / Cos(InvInv(psi + Involute(alfT)))
    If Not isInt And ra >= rPt Then
        ra = rPt * 0.999995
        warnTxt = warnTxt & "· 齿顶变尖，齿顶圆已收到 da=" & Rnd4(2 * ra) & " mm" & vbCrLf
    End If
    thA = HalfAng(ra)
    If thA < 0 Then thA = 0

    ' 根切提示
    Dim zMin As Double
    zMin = 2# * (haC - xs) / (Sin(alfT) ^ 2)
    If Not isInt And zz < zMin Then
        warnTxt = warnTxt & "· 已根切（不根切需 z>=" & Rnd4(zMin) & " 或 x>=" _
            & Rnd4(haC - zz * Sin(alfT) ^ 2 / 2#) & "），齿根过渡曲线已如实生成。" & vbCrLf
    End If
    If beta <> 0 Then
        warnTxt = warnTxt & "· 斜齿：本宏生成的是【端面齿形】直齿体。如需螺旋，" & vbCrLf & _
            "  对拉伸体加【扭曲】特征，扭转角 = " & Rnd4(bw * Tan(b) / rr * 180# / PI) & " 度。" & vbCrLf
    End If
    Geometry = True
End Function

' 半齿角（半径 th 处，从齿中心量）
Function HalfAng(ByVal th As Double) As Double
    Dim q As Double
    If th < rb Then th = rb
    q = rb / th: If q > 1 Then q = 1
    If isInt Then
        HalfAng = psi + Involute(ACos(q)) - Involute(alfT)   ' 内齿：渐开线凹侧，根粗顶细
    Else
        HalfAng = psi + Involute(alfT) - Involute(ACos(q))
    End If
End Function

'=====================================================================
' 画齿（每齿 4 段：齿根弧 + 左样条 + 齿顶弧 + 右样条）
Sub DrawTeeth(swSk As Object)
    Dim fx() As Double, fy() As Double, n As Long
    Call Flank(fx, fy, n)                       ' 半齿廓（含过渡曲线，相对齿中心，角为负）
    Dim k As Long, i As Long, bas As Double, sp As Double, dth As Double
    Dim pd() As Double, seg As Object
    dth = 2# * PI / zz

    For k = 0 To zz - 1
        bas = k * dth
        sp = bas - PI / zz                     ' 齿槽中心

        ' 1) 齿根圆弧（跨齿槽中心）
        If landA > 0.0000001 Then
            swSk.CreateArc 0, 0, 0, _
                Mx(rf, sp - landA), My(rf, sp - landA), 0, _
                Mx(rf, sp + landA), My(rf, sp + landA), 0, 1
        End If

        ' 2) 左齿廓样条（齿根 -> 齿顶）
        ReDim pd(0 To 3 * n - 1)
        For i = 0 To n - 1
            pd(3 * i) = Rx(fx(i), fy(i), bas)
            pd(3 * i + 1) = Ry(fx(i), fy(i), bas)
            pd(3 * i + 2) = 0
        Next i
        Set seg = swSk.CreateSpline((pd))

        ' 3) 齿顶圆弧
        swSk.CreateArc 0, 0, 0, _
            Mx(ra, bas - thA), My(ra, bas - thA), 0, _
            Mx(ra, bas + thA), My(ra, bas + thA), 0, 1

        ' 4) 右齿廓样条（齿顶 -> 齿根，镜像）
        ReDim pd(0 To 3 * n - 1)
        For i = 0 To n - 1
            pd(3 * i) = Rx(fx(n - 1 - i), -fy(n - 1 - i), bas)
            pd(3 * i + 1) = Ry(fx(n - 1 - i), -fy(n - 1 - i), bas)
            pd(3 * i + 2) = 0
        Next i
        Set seg = swSk.CreateSpline((pd))
    Next k
End Sub

'---------------------------------------------------------------------
' 半齿廓点列：齿根圆上起点 -> 过渡曲线 -> 渐开线 -> 齿顶圆
Sub Flank(fx() As Double, fy() As Double, n As Long)
    Dim i As Long, cnt As Long
    Dim px(0 To 400) As Double, py(0 To 400) As Double
    cnt = 0

    If isInt Then
        ' 内齿圈：齿根圆角用切弧近似 + 渐开线
        Dim rcI As Double, aJ As Double, lo As Double, hi As Double, md As Double
        Dim cx As Double, cy As Double, a0 As Double, a1 As Double, da As Double, j As Long
        rcI = rhoC * mn
        If rcI > (rf - ra) * 0.35 Then rcI = (rf - ra) * 0.35
        lo = ACos(Min1(rb / MaxD(rb * 1.0001, ra))): hi = ACos(Min1(rb / rf))
        aJ = hi
        For i = 1 To 50
            md = (lo + hi) / 2#
            Call FilCen(md, rcI, cx, cy)
            If Sqr(cx * cx + cy * cy) > (rf - rcI) Then hi = md Else lo = md
            aJ = md
        Next i
        Call FilCen(aJ, rcI, cx, cy)
        Dim tc As Double
        tc = ATan2(cy, cx)
        landA = PI / zz + tc
        If landA < 0 Then landA = 0
        px(cnt) = rf * Cos(tc): py(cnt) = rf * Sin(tc): cnt = cnt + 1
        ' 切点在圆心外侧方向，写成 ATan2(-cy,-cx) 会差 180°，齿根出倒钩
        a0 = ATan2(cy, cx): a1 = ATan2(FlY(aJ) - cy, FlX(aJ) - cx)
        da = a1 - a0
        Do While da > PI
            da = da - 2 * PI
        Loop
        Do While da < -PI
            da = da + 2 * PI
        Loop
        For j = 1 To 6
            px(cnt) = cx + rcI * Cos(a0 + da * j / 6#)
            py(cnt) = cy + rcI * Sin(a0 + da * j / 6#): cnt = cnt + 1
        Next j
        Dim aT As Double
        aT = ACos(Min1(rb / MaxD(rb * 1.0001, ra)))
        For j = 1 To nPts
            px(cnt) = FlX(aJ + (aT - aJ) * j / nPts)
            py(cnt) = FlY(aJ + (aT - aJ) * j / nPts): cnt = cnt + 1
        Next j
    Else
        ' 外齿轮：齿条刀具包络
        Dim a0c As Double, hh As Double, rLim As Double, Cyy As Double, Cxx As Double
        Dim uEnd As Double, u As Double, gx As Double, gy As Double, rad As Double
        Dim zeta As Double, nt As Long, rJ As Double
        a0c = PI * mt / 4# - xs * mn * Tan(alfT)
        hh = (haC + ccC - xs) * mn
        rLim = (a0c - hh * Tan(alfT)) * Cos(alfT) / (1# - Sin(alfT))
        rcut = rhoC * mn
        If rLim < 0 Then rLim = 0
        If rcut > rLim Then rcut = rLim
        Cyy = hh - rcut
        Cxx = a0c - Cyy * Tan(alfT) - rcut / Cos(alfT)
        If Cxx < 0 Then Cxx = 0
        landA = Cxx / rr
        zeta = PI / 2# - PI / zz
        uEnd = Cyy / Tan(alfT) - Cxx
        nt = nPts
        If nt < 28 Then nt = 28
        rJ = rf
        px(cnt) = rf * Cos(-PI / zz + landA): py(cnt) = rf * Sin(-PI / zz + landA): cnt = cnt + 1
        For i = 1 To nt
            u = -Cxx + (uEnd + Cxx) * i / nt
            Call Troch(u, Cxx, Cyy, rcut, zeta, gx, gy)
            rad = Sqr(gx * gx + gy * gy)
            If rad >= ra Then rJ = rad: Exit For
            If rad > rb Then
                If -ATan2(gy, gx) < HalfAng(rad) - 0.000000001 Then
                    px(cnt) = gx: py(cnt) = gy: cnt = cnt + 1: rJ = rad: Exit For
                End If
            End If
            px(cnt) = gx: py(cnt) = gy: cnt = cnt + 1: rJ = rad
        Next i
        If rJ < rb Then rJ = rb
        If rJ > ra Then rJ = ra
        Dim aS As Double, aE As Double, ay As Double
        aS = ACos(Min1(rb / rJ)): aE = ACos(Min1(rb / ra))
        If aE > aS + 0.000001 Then
            For i = 1 To nPts
                ay = aS + (aE - aS) * i / nPts
                px(cnt) = (rb / Cos(ay)) * Cos(-(psi + Involute(alfT) - Involute(ay)))
                py(cnt) = (rb / Cos(ay)) * Sin(-(psi + Involute(alfT) - Involute(ay))): cnt = cnt + 1
            Next i
        Else
            px(cnt) = ra * Cos(-thA): py(cnt) = ra * Sin(-thA): cnt = cnt + 1
        End If
    End If

    ' 末点精确落在齿顶圆上，保证与齿顶圆弧无缝
    px(cnt - 1) = ra * Cos(-thA): py(cnt - 1) = ra * Sin(-thA)

    n = cnt
    ReDim fx(0 To n - 1): ReDim fy(0 To n - 1)
    For i = 0 To n - 1
        fx(i) = px(i): fy(i) = py(i)
    Next i
End Sub

' 刀尖圆角包络点（齿中心坐标系）
Sub Troch(u As Double, Cxx As Double, Cyy As Double, rc As Double, zeta As Double, _
          gx As Double, gy As Double)
    Dim wx As Double, wy As Double, L As Double, pxx As Double, pyy As Double
    Dim f As Double, ax As Double, ay As Double
    wx = Cxx + u: wy = Cyy: L = Sqr(wx * wx + wy * wy)
    If L < 0.000000001 Then L = 0.000000001
    pxx = wx + rc * wx / L: pyy = wy + rc * wy / L
    f = u / rr
    ax = pxx * Cos(f) + (pyy - rr) * Sin(f)
    ay = -pxx * Sin(f) + (pyy - rr) * Cos(f)
    gx = ax * Cos(zeta) - ay * Sin(zeta)
    gy = ax * Sin(zeta) + ay * Cos(zeta)
End Sub

' 内齿：渐开线点 + 圆角中心
Function FlX(ay As Double) As Double
    FlX = (rb / Cos(ay)) * Cos(-HalfAng(rb / Cos(ay)))
End Function
Function FlY(ay As Double) As Double
    FlY = (rb / Cos(ay)) * Sin(-HalfAng(rb / Cos(ay)))
End Function
Sub FilCen(ay As Double, rc As Double, cx As Double, cy As Double)
    Dim ax As Double, ayy As Double, bx As Double, byy As Double
    Dim tx As Double, ty As Double, L As Double, aA As Double
    Dim c1x As Double, c1y As Double, c2x As Double, c2y As Double
    ax = FlX(ay): ayy = FlY(ay)
    bx = FlX(MaxD(0.0001, ay - 0.0001)): byy = FlY(MaxD(0.0001, ay - 0.0001))
    tx = ax - bx: ty = ayy - byy: L = Sqr(tx * tx + ty * ty)
    If L < 0.000000001 Then L = 0.000000001
    tx = tx / L: ty = ty / L
    c1x = ax + ty * rc: c1y = ayy - tx * rc
    c2x = ax - ty * rc: c2y = ayy + tx * rc
    aA = ATan2(ayy, ax)
    If ATan2(c1y, c1x) < aA Then
        cx = c1x: cy = c1y
    Else
        cx = c2x: cy = c2y
    End If
End Sub

'=====================================================================
' 内孔 + 键槽
Sub DrawBore(swSk As Object)
    Dim r0 As Double, kb As Double, kt As Double, hb As Double, ys As Double, tp As Double
    r0 = bore / 2#
    If Not useKey Then
        swSk.CreateCircleByRadius 0, 0, 0, r0 / 1000#
        Exit Sub
    End If
    Call KeySize(bore, kb, kt)
    If kb <= 0 Then
        swSk.CreateCircleByRadius 0, 0, 0, r0 / 1000#
        Exit Sub
    End If
    hb = kb / 2#
    ys = Sqr(MaxD(0, r0 * r0 - hb * hb))
    tp = r0 + kt
    swSk.CreateArc 0, 0, 0, hb / 1000#, ys / 1000#, 0, -hb / 1000#, ys / 1000#, 0, -1
    swSk.CreateLine -hb / 1000#, ys / 1000#, 0, -hb / 1000#, tp / 1000#, 0
    swSk.CreateLine -hb / 1000#, tp / 1000#, 0, hb / 1000#, tp / 1000#, 0
    swSk.CreateLine hb / 1000#, tp / 1000#, 0, hb / 1000#, ys / 1000#, 0
End Sub

' GB/T 1095 轮毂键槽 b 与 t2
Sub KeySize(d As Double, kb As Double, kt As Double)
    Dim lim, bb, tt, i As Long
    lim = Array(8, 10, 12, 17, 22, 30, 38, 44, 50, 58, 65, 75, 85, 95, 110, 130)
    bb = Array(2, 3, 4, 5, 6, 8, 10, 12, 14, 16, 18, 20, 22, 25, 28, 32)
    tt = Array(1#, 1.4, 1.8, 2.3, 2.8, 3.3, 3.3, 3.3, 3.8, 4.3, 4.4, 4.9, 5.4, 5.4, 6.4, 7.4)
    kb = 0: kt = 0
    For i = 0 To 15
        If d <= lim(i) Then kb = bb(i): kt = tt(i): Exit For
    Next i
End Sub

'=====================================================================
' 参数写入自定义属性
Sub WriteProps(swModel As Object)
    Dim cpm As Object
    On Error Resume Next
    Set cpm = swModel.Extension.CustomPropertyManager("")
    cpm.Add3 "模数mn", 30, CStr(mn), 1
    cpm.Add3 "齿数z", 30, CStr(zz), 1
    cpm.Add3 "压力角", 30, CStr(alfN), 1
    cpm.Add3 "螺旋角", 30, CStr(beta), 1
    cpm.Add3 "变位系数x", 30, CStr(xs), 1
    cpm.Add3 "分度圆直径d", 30, Rnd4(2 * rr), 1
    cpm.Add3 "齿顶圆直径da", 30, Rnd4(2 * ra), 1
    cpm.Add3 "齿根圆直径df", 30, Rnd4(2 * rf), 1
    cpm.Add3 "公法线W", 30, Rnd4(Wk()), 1
    cpm.Add3 "跨齿数k", 30, CStr(KSpan()), 1
End Sub

Function KSpan() As Long
    Dim an As Double, zp As Double
    an = alfN * PI / 180#
    zp = zz * Involute(alfT) / Involute(an)
    KSpan = Int(zp * alfN / 180# + 0.5 + 0.5)
    If KSpan < 2 Then KSpan = 2
End Function
Function Wk() As Double
    Dim an As Double
    an = alfN * PI / 180#
    Wk = mn * Cos(an) * (PI * (KSpan() - 0.5) + zz * Involute(alfT)) + 2 * xs * mn * Sin(an)
End Function

Function Report() As String
    Report = "模数 mn = " & mn & "    齿数 z = " & zz & "    变位 x = " & xs & vbCrLf & _
        "分度圆 d  = " & Rnd4(2 * rr) & " mm" & vbCrLf & _
        "齿顶圆 da = " & Rnd4(2 * ra) & " mm" & vbCrLf & _
        "齿根圆 df = " & Rnd4(2 * rf) & " mm" & vbCrLf & _
        "基圆   db = " & Rnd4(2 * rb) & " mm" & vbCrLf & _
        "公法线 W(k=" & KSpan() & ") = " & Rnd4(Wk()) & " mm" & vbCrLf
    If Len(warnTxt) > 0 Then Report = Report & vbCrLf & "提示：" & vbCrLf & warnTxt
End Function

'=====================================================================
' 工具函数
Function Involute(a As Double) As Double
    Involute = Tan(a) - a
End Function
Function InvInv(v As Double) As Double
    Dim a As Double, i As Long, d As Double, s As Double
    If v <= 0 Then InvInv = 0: Exit Function
    a = (3# * v) ^ (1# / 3#)
    For i = 1 To 60
        d = Tan(a) ^ 2
        If d < 0.000000000001 Then Exit For
        s = (Tan(a) - a - v) / d
        a = a - s
        If Abs(s) < 0.000000000001 Then Exit For
    Next i
    InvInv = a
End Function
Function ACos(x As Double) As Double
    If x >= 1 Then ACos = 0: Exit Function
    If x <= -1 Then ACos = PI: Exit Function
    ACos = Atn(-x / Sqr(1 - x * x)) + PI / 2#
End Function
Function ATan2(y As Double, x As Double) As Double
    If x > 0 Then
        ATan2 = Atn(y / x)
    ElseIf x < 0 Then
        If y >= 0 Then ATan2 = Atn(y / x) + PI Else ATan2 = Atn(y / x) - PI
    Else
        If y > 0 Then ATan2 = PI / 2# ElseIf y < 0 Then ATan2 = -PI / 2# Else ATan2 = 0
    End If
End Function
Function Min1(x As Double) As Double
    If x > 1 Then Min1 = 1 Else Min1 = x
End Function
Function MaxD(a As Double, b As Double) As Double
    If a > b Then MaxD = a Else MaxD = b
End Function
Function Rnd4(v As Double) As String
    Rnd4 = Format(v, "0.####")
End Function
' 极坐标 -> 米
Function Mx(r As Double, t As Double) As Double
    Mx = r * Cos(t) / 1000#
End Function
Function My(r As Double, t As Double) As Double
    My = r * Sin(t) / 1000#
End Function
' 齿中心系点绕原点转 bas 角，输出米
Function Rx(x As Double, y As Double, bas As Double) As Double
    Rx = (x * Cos(bas) - y * Sin(bas)) / 1000#
End Function
Function Ry(x As Double, y As Double, bas As Double) As Double
    Ry = (x * Sin(bas) + y * Cos(bas)) / 1000#
End Function

'=====================================================================
' 选中前视基准面（与界面语言无关：取第一个基准面特征）
Function SelFront(swModel As Object) As Boolean
    Dim f As Object
    Set f = swModel.FirstFeature
    Do While Not f Is Nothing
        If f.GetTypeName2 = "RefPlane" Then
            f.Select2 False, 0
            SelFront = True
            Exit Function
        End If
        Set f = f.GetNextFeature
    Loop
    SelFront = False
End Function
