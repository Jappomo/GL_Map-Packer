# GL Map Packer 2.1

**Download:** `GLMapPacker-2.1.zip` (app, Maya `Injector.py`, quick start).
Requires Windows 10 or 11 with .NET Framework 4.8.

## New

- **Validator tab**: checks a PBR texture set (albedo 30–240 sRGB, metal reflectance 60/70–100%, roughness range) and marks failing texels with colour-coded, optionally animated warnings, with an Original / Validated split. Fixes: albedo darks / brights / metal reflectance (hue-preserving, smooth), per-channel AO / roughness / metallic remap (min, max, mid-grey, contrast), normal intensity, and an **Auto-correct** button. Saves `_fixed` maps and re-checks them at full resolution.
- **Atlas tab**: drop texture sets into the **Layer Stack**, give each layer a size and drag layers to set the order. Layer 1 is placed first; layers pack top-left first, moving down when a row is full, into one atlas per map type (Albedo, Normal, ORM or separate R/AO/M, Emissive, Opacity, Height) with a shared layout, padding that bleeds edge colours for mipmaps, shrink-to-fit, and a `_layout.json` with each material's UV scale and offset.
- **Convert tab** with three map conversions and a live before/after preview:
  - **Normal → Height**: whole-image FFT Poisson integration, with height intensity, mid-point baseline, contrast and surface blur; OpenGL or DirectX input.
  - **Height → Normal**: Sobel gradient, with strength, smoothing and invert; OpenGL or DirectX output.
  - **Height → Ambient Occlusion**: horizon-based AO (HBAO), with radius, depth, intensity, contrast and quality.
  - Normal → Height **integration modes**: *Tileable* for tiling textures; *UV islands* (with a UV mask or ID map), which solves each island on its own and keeps broad shapes; and *Mesh-aware*, which reads the low-poly OBJ and stitches UV seams so heights meet across islands, even rotated ones. Plus *Flatten broad* and *Detail boost* sliders to balance broad shapes against fine detail.
  - Float32 processing and **true 16-bit PNG** input and output. The 8-bit / 16-bit toggle follows the loaded file.
  - Output resolution: source size, or 512 to 4096 px.
- **New interface**: card layout, segmented tabs, custom dropdowns and text fields, one accent color in light and dark themes, toasts and a status bar.
- **Slider reset icon**: a changed slider shows a ↺ button that returns it to its default.
- **Better previews**: wheel zoom, pan, Fit, R/G/B/A channel view, size and zoom readout, a draggable before/after divider, and drag-and-drop drop zones.
- **High-DPI support**, a dark title bar on Windows 10 and 11, and a new app icon.

## Fixes

- **TGA files can now be opened.** They appeared in every open dialog, but Windows' image library can't read TGA, so loading failed. GL Map Packer now has its own TGA reader (uncompressed and RLE; greyscale, colour and palette; 15 to 32-bit).
- **Height → AO is closer to baked AO.** A new *Detailed* sampling mode (the default; *Classic* keeps the old look) samples more densely near each pixel. Against a reference AO bake it scores 0.84 vs 0.81 correlation, at the same speed. The default radius is now 32 px.
- Dropping an image directly onto a Pack slot's preview now loads it.

## Notes

- Pack, Unpack and Utility still work at 8 bits per channel.
- AO runs on the CPU, in about 1 to 2 s at 1024 px and roughly 10 to 20 s for a 4K save.
