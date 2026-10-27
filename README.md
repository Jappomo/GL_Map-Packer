<p align="center">
  <img src="docs/images/icon.png" width="72" alt="GL Map Packer icon">
</p>

<h1 align="center">GL Map Packer</h1>

<p align="center">
  A small Windows toolkit for game texture work: pack and unpack channels, convert normal, height and AO maps,
  flip normals, batch-rename files and send materials to Maya.
</p>

<p align="center">
  <img src="docs/images/convert-height-to-normal.png" alt="GL Map Packer, Convert tab" width="900">
</p>

## Features

| Tab | What it does |
|---|---|
| **Pack** | Combine up to four grayscale maps into the R, G, B and A channels of one texture (ORM, mask maps and so on). |
| **Unpack** | Split a packed texture back into four grayscale channel images. |
| **Convert** | Normal → Height (tileable, UV-island or mesh-aware with an OBJ, so heights meet across UV seams), Height → Normal (Sobel) and Height → Ambient Occlusion (HBAO), with a live preview and 16-bit PNG output. |
| **Atlas** | Drop texture sets (Albedo, Normal, ORM or separate R/AO/M, Emissive…) into a layer stack, give each a size and drag to order them; they pack from the top-left corner into one atlas per map type, with a UV layout file. |
| **Utility** | Flip the green channel (OpenGL ↔ DirectX normals), flip vertically or horizontally, or invert RGB. |
| **Renamer** | Batch-rename a folder with search/replace, prefix, suffix and numbering, with a live preview before anything changes. |
| **Injector** | Pick a material's texture maps and export a preset that `Injector.py` builds into a shading network in Maya. |

Also:

- **Live, comparable previews.** Wheel to zoom, drag to pan, view single channels (R/G/B/A), and drag the divider for a before/after comparison.
- **Float32 processing and true 16-bit PNG** reading and writing on the Convert tab, so height maps stay free of banding.
- **Light and dark themes**, high-DPI aware, with a dark title bar on Windows 10 and 11.
- **Drag and drop everywhere:** drop files onto any preview or slot.
- **One portable `.exe`** with no installer and no dependencies.

<p align="center">
  <img src="docs/images/light-theme.png" alt="Light theme" width="700">
</p>

## Getting started

**Requirements:** Windows 10 or 11 with .NET Framework 4.8. It's already installed on Windows 10 (version 1903 and later) and on Windows 11.

### Download

**[⬇ GLMapPacker-2.1.zip](releases/GLMapPacker-2.1.zip?raw=true)** (about 100 KB) contains:

- `GLMapPacker.exe`: the app
- `Injector.py`: the Maya importer for the Injector tab
- `README.txt`: quick start

Unzip it anywhere and double-click `GLMapPacker.exe`. There's nothing to install. If Windows SmartScreen warns about an unknown publisher, choose **More info → Run anyway**: the exe isn't code-signed.

New to the tool? The **[tutorial](docs/TUTORIAL.md)** walks through every tab with screenshots.

## Building from source

The C# source is in [`Source/`](Source). It's a Windows Forms app targeting .NET Framework 4.8.

1. Install the [.NET SDK](https://dotnet.microsoft.com/download) (version 6 or newer).
2. Run `Source/build.bat`, or from a terminal:

   ```bat
   cd Source
   dotnet build GLMapPacker.csproj -c Release
   ```

3. The program is written to `Source/bin/Release/GLMapPacker.exe`.

### Project layout

```
GLMapPacker.exe          ready-to-run build
Injector.py              Maya importer for presets from the Injector tab
Source/
  GLMapPacker.csproj     project (net48), app.manifest, app.ico, build.bat
  GLMapPacker/
    MainForm.cs          window, header and the Pack/Unpack/Utility/Renamer/Injector tabs
    MainForm.Convert.cs  the Convert tab
    ConvertCore.cs       Normal→Height (FFT / island / mesh), Height→Normal (Sobel), Height→AO (HBAO)
    HeightSolver.cs      coarse-to-fine least-squares height solve for islands and UV seams
    UvMesh.cs            OBJ reader, UV islands, seams and texel rasterising
    FloatImage.cs        Float32 image, resampling
    PngIO.cs             8/16-bit PNG reader and writer
    ImageCore.cs         8-bit pack/split/flip and TGA/TIFF/BMP/JPEG saving
    RenameEngine.cs      rename planning and safe two-step renaming
    MaterialPreset.cs    preset JSON for the Maya bridge
    Ui.cs, Theme.cs      DPI scaling, fonts, palette, title bar, notifications
    *.cs                 themed controls (buttons, sliders, previews, toasts...)
docs/
  TUTORIAL.md            step-by-step guide
  images/                screenshots
```

## Notes

- **Bit depth.** Convert keeps 16-bit precision end to end. Pack, Unpack and Utility work at 8 bits per channel, and a 16-bit input on those tabs is reduced to 8-bit.
- **Mesh-aware Normal → Height** reads OBJ files and needs non-overlapping UVs. It takes about 6 s at 2K and 20 to 30 s at 4K.
- **AO speed.** Height → AO runs on the CPU across all cores, in about 1 to 2 s at 1024 px. A 4K save takes roughly 10 to 20 s, depending on the processor.
- **Screenshots.** The images in this README were captured from a Linux preview build, so the fonts differ slightly from Windows (Segoe UI).
