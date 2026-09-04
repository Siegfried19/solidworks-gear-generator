# VBA Macro

A self-contained SOLIDWORKS VBA macro using the same gear mathematics as the add-in. No
installation, no administrator rights — useful on a machine where you cannot register a COM
add-in.

*Prompts and messages are currently Chinese only.*

## Use it

1. SOLIDWORKS → **Tools → Macro → New**, save with any name (e.g. `Gear.swp`)
2. The VBA editor opens. **Select all the default code and delete it.**
3. Open `GearGen.bas` in a text editor, copy everything, paste it in
4. Click inside `Sub main()`, press **F5**

Afterwards: **Tools → Macro → Run** and pick your `.swp`.

> **Do not paste the `Attribute VB_Name = "GearGen"` line.** It is only valid when *importing*
> a `.bas` file; pasted into the code window it is a syntax error. Delete that first line before
> pasting, or use *File → Import File…* in the VBA editor instead of pasting.

### Encoding

This file is UTF-8. The VBA code window is ANSI, so on a Chinese Windows the safest route is:

- **Paste:** UTF-8 with BOM works — Notepad reads it correctly and the clipboard is Unicode.
- **Import** (*File → Import File…*): convert to the system ANSI code page (GBK on Chinese
  Windows) first, and re-add the `Attribute VB_Name = "GearGen"` line as line 1. VBA's importer
  reads the file bytes as ANSI and does not understand a BOM.

## Input

The macro asks in two steps:

| Step | Fields |
|---|---|
| 1 | module, tooth count, pressure angle, helix angle, profile shift |
| 2 | face width, bore diameter, keyway (1/0), type (`EXT`/`INT`) |

Paste the full nine-field comma string from the web panel into the **first** box to skip step 2:

```
2,20,20,0,0,20,12,EXT,1
```

Addendum, clearance and cutter-tip-radius coefficients are constants at the top of `Sub Defaults`.

## Differences from the add-in

The macro draws `z × 4` sketch entities in a single sketch and extrudes once, rather than the
add-in's blank + one tooth space + circular pattern. It works, but the feature tree is a single
extrude and there is no live parameter feedback.

## VBA pitfalls worth knowing

`mid`, `base`, `top`, `left`, `right` and similar are reserved words or property names — they
cannot be used as variable names. Single-line `Do While … : … : Loop` is best avoided.
