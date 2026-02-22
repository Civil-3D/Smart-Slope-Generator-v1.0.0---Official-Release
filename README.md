# Smart Slope Generator (AutoCAD / Civil 3D) ⛰️

A highly dynamic, UI-driven plugin for AutoCAD and Civil 3D designed to generate professional slope patterns, hatches, and Tapered lines (triangles) with ease. Created to streamline workflows for Civil Engineers and infrastructure designers, especially for large-scale master plans and road design projects.

<img width="638" height="415" alt="Screenshot 2026-02-22 144637" src="https://github.com/user-attachments/assets/791bc73b-281d-4313-ab7e-4148afce4e5c" />

---

## ✨ Key Features

* **Dynamic Visual JIG:** Real-time preview of slope lines as you move your mouse, ensuring perfect placement and visual feedback before final execution.
* **Elastic & Proportional Distribution:** A smart algorithm that distributes lines proportionally along the selected path, effectively preventing crossing or overlapping on sharp internal corners or steep terrain.
* **Multiple Professional Styles:**
    * **Fixed Length:** Standard uniform patterns for general use.
    * **Long / 1 Short:** Classic slope representation.
    * **Long / 2 Shorts:** European standard style for detailed engineering drawings.
* **Advanced Ending Types:** Professional markers for short lines, including **Circles** and **T-Shape Ticks**.
* **Tapered Triangles:** A specialized option to draw long slope lines as solid **Civil 3D style triangles**, perfect for clean and modern master plans.
* **Auto Cut/Fill Detection:** Automatically senses elevation (Z-axis) differences between the Crest and Toe lines to orient the pattern correctly downwards.
* **Settings Persistence:** The plugin remembers your last-used layers, colors, styles, and ratios, making your next session faster and more consistent.
* **Wipeout Background:** Automatically generates a background hatch mask to maintain drawing clarity even over complex underlying topography.

---

## 🚀 Installation

### Option 1: MSI Installer (Recommended)
1. Download the latest **`SmartSlopeInstaller.msi`** from the [Releases](https://github.com/Civil-3D/Smart-Slope-Generator-v1.0.0---Official-Release/releases) section.
2. Run the installer on your machine. It will automatically place the plugin in the correct Autodesk ApplicationPlugins folder.
3. Restart AutoCAD or Civil 3D. The plugin will be loaded automatically!

### Option 2: Manual Load (Portable)
1. Download the **`Draw_Slopes.dll`** file from the latest release.
2. In AutoCAD / Civil 3D, type the **`NETLOAD`** command.
3. Browse and select the downloaded `.dll` file.

---

## 🛠️ Usage

1. **Launch:** Type the command **`DRAWSLOPE`** in the command line.
2. **Configure:** A modern UI window will pop up. Select your preferred settings (Step, Ratio, Style, Layer, Colors).
3. **Action:** Click the **Draw Slope** button.
4. **Selection:** * Select the **Top Line (Crest)**.
    * Select the **Bottom Line (Toe)**.
5. **Finalize:** Use the dynamic JIG to either select a specific segment or draw along the entire length of the selected curves.

---

## 📝 Planned Updates
* **Auto-Coloring:** Automated color separation based on calculated Cut and Fill values.
* **Slope Labeling:** Automatic text labeling for gradients (e.g., 1:1.5 or 2%).

* ---

## 💡 Feedback & Feature Requests
Have an idea for a new feature or found a bug? 
Feel free to open an **[Issue](https://github.com/Civil-3D/Smart-Slope-Generator-v1.0.0---Official-Release/issues)** or reach out directly. 
I am open to community feedback to make this tool even better for infrastructure designers!

---

**Author:** [Beka Tchigladze]([https://www.linkedin.com/in/beka-tchigladze-038901146/](https://www.linkedin.com/feed/))  
**Organization:** [Green Road Group](https://www.greenroadgroup.com.ge](https://www.greenroadgroup.com.ge/))  
**Educational Resource:** [GeoCourse.ge](https://www.geocourse.ge/courses/autodesk-civil-3d/)

*Supporting infrastructure design workflows with automation and precision.*
