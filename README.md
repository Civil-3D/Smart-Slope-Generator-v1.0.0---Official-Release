# Smart Slope Generator (AutoCAD / Civil 3D) ⛰️

A highly dynamic, UI-driven plugin for AutoCAD and Civil 3D to easily generate professional slope patterns, hatches, and Tapered lines (triangles) between Crest and Toe lines.

## ✨ Features

* **Dynamic Visual JIG:** See the slope lines adjust in real-time as you move your mouse.
* **Elastic Distribution:** Lines are distributed proportionally, eliminating crossing lines on sharp internal corners.
* **Multiple Styles:**
    * Fixed Length
    * Long / 1 Short
    * Long / 2 Shorts (European Style)
* **Advanced Ending Types:** Add Circles or T-Shape Ticks to short lines.
* **Civil 3D Style Triangles:** Option to draw long slope lines as solid "Tapered" triangles.
* **Auto Cut/Fill Orientation:** Automatically detects elevation (Z-axis) differences to orient the slope downwards.
* **Wipeout Background:** Automatically generates a background hatch mask to hide underlying topography.

## 🚀 Installation

1. Download the `.dll` file from the latest release.
2. Open AutoCAD / Civil 3D.
3. Type the `NETLOAD` command.
4. Select the downloaded `Draw_Slopes.dll` file.

## 🛠️ Usage

1. Type the command **`DRAWSLOPE`** in the command line.
2. A modern UI window will pop up where you can configure all settings (Step, Ratio, Styles, Layer, Colors).
3. Click **Draw Slope**.
4. Select the **Top Line (Crest)**.
5. Select the **Bottom Line (Toe)**.
6. Follow the on-screen prompts to either draw along the entire length or select a specific segment using the dynamic JIG.

## 📝 Planned Updates
* Auto Cut/Fill color separation.
* Slope slope text labeling (e.g., 1:1.5).

---
*Created for Civil Engineers and Draftsmen to speed up infrastructure design workflows.*
