# SOLIDWORKS Gear Generator

A SOLIDWORKS add-in that generates **true involute cylindrical gears** — including the real
trochoidal root fillet swept by a rack cutter, with undercut reproduced faithfully rather than
hidden behind a cosmetic radius.

*[中文说明](README.zh-CN.md)*

---

## Why another gear tool

Most gear generators available to SOLIDWORKS users approximate the tooth profile. This one doesn't:

| Tool | Flank profile | Root fillet | Undercut |
|---|---|---|---|
| SOLIDWORKS Toolbox | **Constant-radius arc** — Dassault [states plainly](https://help.solidworks.com/2026/english/SolidWorks/toolbox/c_Gears.htm) these "are not true involute gears that you can use for manufacturing" | none | none |
| Fusion 360 official SpurGear script | Involute, 10 points | **Straight line** to root circle | none (geometry is wrong for z ≥ 42) |
| Onshape Spur Gear | Involute | Solver-fitted inscribed circle | none |
| OpenSCAD libraries | Polyline approximation | mostly none (BOSL2 excepted) | BOSL2 only |
| Online DXF generators | Polyline approximation | none | none |
| **This project** | **Analytic involute** | **True rack-cutter envelope (trochoid)** | **Modelled as it actually occurs** |

If you are cutting gears by wire EDM, 3D printing them, running motion studies, or analysing
backlash and contact, the difference matters.

---

## What you get

**A native add-in.** Its own ribbon tab and a real PropertyManager panel — live results update as
you type, and undercut / pointed-tooth / thin-tip-land warnings appear immediately. One click
produces a solid with a clean, editable feature tree.

```
Gear blank (extrude)  →  one tooth space (cut)  →  circular pattern ×z  →  bore + keyway (cut)
```

Gear data (module, tooth count, profile shift, span measurement, pin measurement …) is written to
the model's custom properties, so drawings can reference it directly.

**Two companions:**

- `macro/` — a self-contained VBA macro. Paste into the VBA editor, press F5. Zero install; useful
  on a machine where you cannot register an add-in.
- `web/gear.html` — a browser panel: live profile preview, all geometry, span and pin measurements,
  DXF export (R12). Works offline, no dependencies.

---

## Features

- Spur and helical (helical produces the transverse profile; the required twist angle is reported)
- External gears and internal ring gears
- Profile shift, with undercut and pointed-tooth detection and automatic tip-diameter clamping
- Configurable addendum / clearance / cutter tip radius coefficients
- Bore with optional hub keyway per **GB/T 1095**
- Span measurement `W` over `k` teeth, pin/ball measurement `M`, chordal thickness
- DXF R12 export (web panel)

---

## Requirements

- SOLIDWORKS 2016 or later (developed and verified against **2025**, API revision 33.4.0)
- .NET Framework 4.x — the `csc.exe` that ships with Windows is enough; **no Visual Studio required**
- Administrator rights once, to register the COM add-in

---

## Install

### Option A — prebuilt release (no compiler needed)

Download the ZIP from the [Releases](../../releases) page, unpack it somewhere permanent, then
right-click `install.bat` → **Run as administrator**. Restart SOLIDWORKS.

> The install path is written into the registry, so **do not move the folder afterwards**.
> To relocate: run `uninstall.bat`, move it, run `install.bat` again.

### Option B — build from source

The SOLIDWORKS interop assemblies are **not** committed to this repository. Copy them from your
own installation — they live in:

```
<SOLIDWORKS install>\api\redist\
    SolidWorks.Interop.sldworks.dll
    SolidWorks.Interop.swconst.dll
    SolidWorks.Interop.swpublished.dll
```

Then:

```bat
cd addin
build.cmd                     REM output goes to addin\build
build.cmd D:\somewhere\else   REM or pick your own output directory
```

`build.cmd` locates SOLIDWORKS through the registry, copies the three interop DLLs next to the
output, and compiles `GearWorks.dll`. Then run `install.bat` from the output folder **as
administrator** and restart SOLIDWORKS.

A **Gear Tools** tab appears in the CommandManager when a part document is open. If it does not,
enable the add-in under *Tools → Add-Ins* (tick both columns).

To remove: `uninstall.bat` as administrator, then delete the folder.

### Packaging a release

```bat
cd addin
make-release.cmd                              REM addin\release, v1.0.0
make-release.cmd D:\out v1.1.0                REM or specify both
```

Produces `GearWorks-<version>-solidworks.zip` containing the add-in, the interop assemblies,
the install scripts and a quick-start note — ready to attach to a GitHub Release.

---

## Verification

Reference values for **mn = 2, z = 20, αn = 20°, x = 0, ha\* = 1, c\* = 0.25, ρ\* = 0.38**:

| Quantity | Value |
|---|---|
| Reference diameter `d` | 40.0000 |
| Base diameter `db` | 37.5877 |
| Tip diameter `da` | 44.0000 |
| Root diameter `df` | 35.0000 |
| Span `W` over `k` = 3 teeth | 15.3209 |
| Pin measurement `M` (dp = 3.36) | 44.4498 |
| Circular thickness at `d` | 3.1416 (= π·mn/2) |
| Minimum teeth without undercut | 17.097 |

All match standard gear handbook tables.

Self-check programs live in `addin/tests/`. **Run them after changing anything:**

```bat
csc /target:exe /r:GearWorks.dll ..\tests\Chain.cs && Chain.exe
```

`Chain.cs` walks the tooth-space profile across nine cases (standard, undercut, profile-shifted,
pointed, high tooth count, helical, and three internal gears) and verifies the six segments close
exactly and never self-intersect. Expected output: `gap=0.000000000000  self-intersections=0`
on every line. `Test.cs` checks geometry against the reference table above; `Sets.cs` prints
computed values for candidate parameter sets.

---

## How the profile is generated

The tooth flank has two mathematically distinct parts, joined where they are naturally tangent.

**Involute portion** — analytic, exact:

```
θ(ρ) = sT/(2r) + inv(αt) − inv(αy),    αy = acos(rb/ρ),    inv(a) = tan a − a
```

**Root fillet** — the envelope swept by the rounded tip of a rack cutter rolling on the pitch line.
With the cutter translated by `u`, the gear turns by `φ = u/r`. By the fundamental law of gearing
the contact normal passes through the pitch point, so the contact point on the tip round is simply
the fillet centre pushed outward by the fillet radius along the line from the pitch point:

```
Cw = (Cx + u, Cy)              fillet centre in the rack frame
P  = Cw + rc · Cw/|Cw|         contact point
```

Sweeping `u` from `−Cx` (fillet tangent to the cutter tip land, landing on the root circle) to
`Cy/tan αt − Cx` (fillet tangent to the cutter flank, landing exactly on the involute) traces the
whole fillet.

**Undercut** falls out of this for free: while sweeping, if a trochoid point at radius `ρ > rb`
lies inside the involute (`θ_trochoid < θ_involute(ρ)`), the cutter has removed involute flank
there. The profile is truncated at that crossing and the involute resumes from that radius. No
special case, no heuristic — undercut appears because it physically happens.

Full derivation, plus every SOLIDWORKS API pitfall encountered while building this:
**[docs/technical-reference.md](docs/technical-reference.md)**

---

## Known limitations

1. **Helical gears produce the transverse profile as a spur solid.** For a true helical flank, add
   a Flex/twist feature to the extrusion — the panel computes the required twist angle
   (`b·tanβ/r`) and tells you.
2. **Internal ring gear root fillets are a tangent-arc approximation** (the involute flank itself
   is exact). A real internal fillet is generated by a shaper cutter and depends on the cutter's
   tooth count; this is rarely needed for drawings. The internal path is geometrically verified
   offline but has not been exercised in SOLIDWORKS.
3. No interference checking, no bending/contact strength calculation, no accuracy grades or
   tolerance bands, no double-helical, bevel or worm gears.
4. Profile shift is not auto-optimised — you supply it.

---

## Interface language

The add-in panel and macro prompts are currently **Chinese only**. Documentation is bilingual.
An English UI is a natural next step; contributions welcome.

---

## License

MIT — see [LICENSE](LICENSE).

The SOLIDWORKS interop assemblies are **not** included and are not covered by this license; they
are Dassault Systèmes redistributables that come with your SOLIDWORKS installation.
