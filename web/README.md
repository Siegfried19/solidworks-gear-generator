# Web Panel

`gear.html` — a single self-contained file. Open it in any browser. No server, no network, no
dependencies.

*Interface is currently Chinese only.*

## What it does

- **Live profile preview** — scroll to zoom, drag to pan. The drawing follows engineering
  conventions: reference circle as a chain line, base and root circles dashed, centre lines
  through the origin.
- **All geometry** — transverse module and pressure angle, reference / base / tip / root
  diameters, whole depth, normal circular thickness, tip land thickness, base pitch, virtual
  tooth count.
- **Inspection dimensions** — span measurement `W` with the span count `k`, pin/ball measurement
  `M` for a chosen pin diameter, chordal thickness and chordal height.
- **Checks** — undercut (with the minimum profile shift that avoids it), pointed teeth, thin tip
  land, hub wall thickness, and contact ratio when a mating gear is shown.
- **Mating gear** — tick "show mating gear" to draw the pair, with working pressure angle,
  centre distance and transverse contact ratio.
- **Export** — DXF (R12, closed polylines, layers `GEAR` / `CONSTRUCTION` / `BORE`) and SVG.

## Feeding the macro

**Copy SolidWorks parameters** puts the nine-field comma string on the clipboard:

```
2,20,20,0,0,20,12,EXT,1
```

Paste that into the first prompt of the VBA macro to skip its second prompt.

## Note on the DXF path

Importing a gear as DXF into SOLIDWORKS is supported but not recommended for modelling: the
profile arrives as polylines rather than splines, and SOLIDWORKS turns off automatic sketch
solving when an import contains many entities. Use the add-in when you want a solid; use the DXF
for wire EDM, laser and waterjet toolpaths, or for drawings.
