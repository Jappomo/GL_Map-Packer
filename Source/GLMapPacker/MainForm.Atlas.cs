using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GLMapPacker;

// "Atlas" tab: drop texture sets, give each material a size, and they pack from the top-left corner
// downwards into one atlas per map type with a shared layout.
public partial class MainForm
{
	private static readonly int[] AtlasSizes = new int[5] { 512, 1024, 2048, 4096, 8192 };

	private static readonly int[] MaterialSizes = new int[8] { 64, 128, 256, 512, 1024, 2048, 4096, 8192 };

	private const int AtlasPreviewSide = 1024;

	private static readonly string[] AtlasImageExtensions = new string[8] { ".png", ".tga", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".gif" };

	private Panel pageAtlas;

	private TitledCard atSettingsCard;

	private TitledCard atMaterialsCard;

	private Label atCapSize;

	private ThemedComboBox atSizeCombo;

	private ThemedSlider atPadding;

	private Label atCapOrder;

	private ThemedComboBox atOrderCombo;

	private Label atCapMask;

	private ThemedComboBox atMaskCombo;

	private Label atCapNewSize;

	private ThemedComboBox atNewSizeCombo;

	private ThemedCheckBox atShrinkChk;

	private ThemedCheckBox atOutlineChk;

	private Label atCapExport;

	private ThemedComboBox atFormatCombo;

	private SegmentedToggle atBitToggle;

	private ThemedProgress atProgress;

	private FlatButton atExportBtn;

	private AtlasList atList;

	private FlatButton atAddBtn;

	private FlatButton atClearBtn;

	private Label atStatus;

	private ThemedComboBox atPreviewMapCombo;

	private PreviewBox atPreview;

	private Timer atPreviewTimer;

	private readonly List<AtlasCore.Entry> atEntries = new List<AtlasCore.Entry>();

	// Downscaled copies (longest side <= 512) of every loaded file, for the preview.
	private readonly Dictionary<string, FloatImage> atThumbs = new Dictionary<string, FloatImage>(StringComparer.OrdinalIgnoreCase);

	private readonly HashSet<string> at16Bit = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	// Each halving step applied to all sizes by "shrink to fit".
	private int atShrinkShift;

	private int atPreviewVersion;

	private bool atExporting;

	private List<AtlasCore.Map> atMaps = new List<AtlasCore.Map>();

	private int AtlasSize => AtlasSizes[Math.Max(0, atSizeCombo.SelectedIndex)];

	private int AtlasPaddingPx => (int)atPadding.Value;

	private void BuildAtlasPage()
	{
		pageAtlas = new Panel
		{
			Dock = DockStyle.Fill
		};
		atPreviewTimer = new Timer
		{
			Interval = 120
		};
		atPreviewTimer.Tick += delegate
		{
			atPreviewTimer.Stop();
			RunAtlasPreview();
		};
		// Settings and export.
		atSettingsCard = Card("Atlas");
		atCapSize = Caption("Atlas size");
		atSizeCombo = new ThemedComboBox();
		foreach (int s in AtlasSizes)
		{
			atSizeCombo.Items.Add(s + " × " + s);
		}
		atSizeCombo.SelectedIndex = 2;
		atSizeCombo.SelectedIndexChanged += delegate
		{
			RelayoutAtlas();
		};
		atPadding = new ThemedSlider("Padding between tiles", 0.0, 32.0, 8.0, 0, " px");
		atPadding.ValueChanged += delegate
		{
			RelayoutAtlas();
		};
		atCapOrder = Caption("Placement order");
		atOrderCombo = new ThemedComboBox();
		atOrderCombo.Items.AddRange(new object[2] { "Largest first (tightest)", "As added" });
		atOrderCombo.SelectedIndex = 0;
		atOrderCombo.SelectedIndexChanged += delegate
		{
			RelayoutAtlas();
		};
		atCapMask = Caption("Occlusion / roughness / metallic");
		atMaskCombo = new ThemedComboBox();
		atMaskCombo.Items.AddRange(new object[2] { "Packed ORM atlas", "Separate R, AO, M atlases" });
		atMaskCombo.SelectedIndex = 0;
		atMaskCombo.SelectedIndexChanged += delegate
		{
			RelayoutAtlas();
		};
		atCapNewSize = Caption("Size for new materials");
		atNewSizeCombo = new ThemedComboBox();
		foreach (int s in MaterialSizes)
		{
			atNewSizeCombo.Items.Add(s + " px");
		}
		atNewSizeCombo.SelectedIndex = 4;
		atShrinkChk = new ThemedCheckBox
		{
			Text = "Shrink all to fit when full",
			Checked = true
		};
		atShrinkChk.CheckedChanged += delegate
		{
			RelayoutAtlas();
		};
		atOutlineChk = new ThemedCheckBox
		{
			Text = "Show tile outlines",
			Checked = true
		};
		atOutlineChk.CheckedChanged += delegate
		{
			QueueAtlasPreview();
		};
		atCapExport = Caption("Export");
		atFormatCombo = new ThemedComboBox();
		atFormatCombo.Items.AddRange(new object[2] { "PNG", "TGA" });
		atFormatCombo.SelectedIndex = 0;
		atFormatCombo.SelectedIndexChanged += delegate
		{
			atBitToggle.Enabled = atFormatCombo.SelectedIndex == 0;
			atBitToggle.Invalidate();
		};
		atBitToggle = new SegmentedToggle
		{
			Options = new string[2] { "8-bit", "16-bit" }
		};
		atProgress = new ThemedProgress();
		atExportBtn = new FlatButton
		{
			Text = "Export atlas",
			Role = FlatButton.ButtonRole.Accent,
			Enabled = false
		};
		atExportBtn.Click += async delegate
		{
			await ExportAtlas();
		};
		atSettingsCard.Controls.AddRange(new Control[16] { atCapSize, atSizeCombo, atPadding, atCapOrder, atOrderCombo, atCapMask, atMaskCombo, atCapNewSize, atNewSizeCombo, atShrinkChk, atOutlineChk, atCapExport, atFormatCombo, atBitToggle, atProgress, atExportBtn });
		// Materials.
		atMaterialsCard = Card("Materials");
		atList = new AtlasList
		{
			AllowDrop = true
		};
		atList.DragEnter += AtlasDragEnter;
		atList.DragDrop += AtlasDragDrop;
		atList.EntriesChanged += delegate
		{
			RelayoutAtlas();
		};
		atList.SelectionChanged += delegate
		{
			QueueAtlasPreview();
		};
		atAddBtn = new FlatButton
		{
			Text = "Add textures",
			Role = FlatButton.ButtonRole.Secondary
		};
		atAddBtn.Click += delegate
		{
			using OpenFileDialog dialog = new OpenFileDialog();
			dialog.Multiselect = true;
			dialog.Filter = "Images|*.png;*.tga;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|All files|*.*";
			if (dialog.ShowDialog() == DialogResult.OK)
			{
				AddAtlasFiles(dialog.FileNames);
			}
		};
		atClearBtn = new FlatButton
		{
			Text = "Clear",
			Role = FlatButton.ButtonRole.Secondary
		};
		atClearBtn.Click += delegate
		{
			atEntries.Clear();
			atThumbs.Clear();
			at16Bit.Clear();
			RelayoutAtlas();
		};
		atStatus = new Label
		{
			Name = "capAtStatus",
			AutoSize = false,
			Font = Ui.Small,
			AutoEllipsis = true,
			TextAlign = ContentAlignment.MiddleLeft
		};
		atMaterialsCard.Controls.AddRange(new Control[4] { atList, atAddBtn, atClearBtn, atStatus });
		// Preview.
		atPreviewMapCombo = new ThemedComboBox();
		atPreviewMapCombo.SelectedIndexChanged += delegate
		{
			QueueAtlasPreview();
		};
		atPreview = new PreviewBox
		{
			EmptyText = "Drop texture sets to build an atlas",
			EmptyHint = "Each material keeps its maps lined up across every atlas",
			AcceptsFiles = true
		};
		atPreview.DragDrop += AtlasDragDrop;
		pageAtlas.AllowDrop = true;
		pageAtlas.DragEnter += AtlasDragEnter;
		pageAtlas.DragDrop += AtlasDragDrop;
		pageAtlas.Controls.Add(atSettingsCard);
		pageAtlas.Controls.Add(atMaterialsCard);
		pageAtlas.Controls.Add(atPreviewMapCombo);
		pageAtlas.Controls.Add(atPreview);
		pageAtlas.Resize += delegate
		{
			LayoutAtlasPage();
		};
		RelayoutAtlas();
	}

	private void LayoutAtlasPage()
	{
		if (pageAtlas == null)
		{
			return;
		}
		int width = pageAtlas.Width;
		int height = pageAtlas.Height;
		int side = SideWidth;
		int inner = side - Pad * 2;
		atSettingsCard.Location = new Point(0, 0);
		atSettingsCard.Size = new Size(side, height);
		int y = Stack(atCapSize, atSizeCombo, Pad, atSettingsCard.ContentTop, inner);
		atPadding.Location = new Point(Pad, y - Ui.S(4));
		atPadding.Width = inner;
		y = atPadding.Bottom + Ui.S(8);
		y = Stack(atCapOrder, atOrderCombo, Pad, y, inner);
		y = Stack(atCapMask, atMaskCombo, Pad, y, inner);
		y = Stack(atCapNewSize, atNewSizeCombo, Pad, y, inner);
		atShrinkChk.Location = new Point(Pad, y - Ui.S(4));
		atShrinkChk.Width = inner;
		atOutlineChk.Location = new Point(Pad, atShrinkChk.Bottom + Ui.S(2));
		atOutlineChk.Width = inner;
		int by = atSettingsCard.Height - Pad - atExportBtn.Height;
		atExportBtn.Location = new Point(Pad, by);
		atExportBtn.Width = inner;
		atProgress.Location = new Point(Pad, by - Ui.S(14));
		atProgress.Width = inner;
		int half = (inner - Ui.S(8)) / 2;
		atCapExport.Location = new Point(Pad, atProgress.Top - Ui.S(8) - atFormatCombo.Height - Ui.S(20));
		atFormatCombo.Location = new Point(Pad, atCapExport.Top + Ui.S(20));
		atFormatCombo.Width = half;
		atBitToggle.Location = new Point(Pad + half + Ui.S(8), atFormatCombo.Top + (atFormatCombo.Height - atBitToggle.Height) / 2);
		atBitToggle.Width = inner - half - Ui.S(8);
		int mx = side + Gap;
		int mw = Ui.S(360);
		atMaterialsCard.Location = new Point(mx, 0);
		atMaterialsCard.Size = new Size(mw, height);
		int min = mw - Pad * 2;
		int bby = height - Pad - atAddBtn.Height;
		int bw = (min - Ui.S(8)) / 2;
		atAddBtn.Location = new Point(Pad, bby);
		atAddBtn.Width = bw;
		atClearBtn.Location = new Point(Pad + bw + Ui.S(8), bby);
		atClearBtn.Width = min - bw - Ui.S(8);
		atStatus.Location = new Point(Pad, bby - Ui.S(28));
		atStatus.Size = new Size(min, Ui.S(22));
		atList.Location = new Point(Pad, atMaterialsCard.ContentTop);
		atList.Size = new Size(min, atStatus.Top - Ui.S(8) - atMaterialsCard.ContentTop);
		int px = mx + mw + Gap;
		int pw = Math.Max(Ui.S(120), width - px);
		atPreviewMapCombo.Location = new Point(px, 0);
		atPreviewMapCombo.Width = Math.Min(pw, Ui.S(200));
		atPreview.Location = new Point(px, atPreviewMapCombo.Bottom + Ui.S(10));
		atPreview.Size = new Size(pw, height - atPreview.Top);
	}

	private void AtlasDragEnter(object sender, DragEventArgs e)
	{
		e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
	}

	private void AtlasDragDrop(object sender, DragEventArgs e)
	{
		if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
		{
			// Dropped folders contribute the images inside them.
			List<string> paths = new List<string>();
			foreach (string f in files)
			{
				if (Directory.Exists(f))
				{
					paths.AddRange(Directory.GetFiles(f));
				}
				else
				{
					paths.Add(f);
				}
			}
			AddAtlasFiles(paths);
		}
	}

	private async void AddAtlasFiles(IEnumerable<string> paths)
	{
		List<string> images = paths.Where(p => AtlasImageExtensions.Contains(Path.GetExtension(p).ToLowerInvariant())).OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).ToList();
		if (images.Count == 0)
		{
			Notifier.Warning("No image files in what you dropped.");
			return;
		}
		int size = Math.Min(MaterialSizes[atNewSizeCombo.SelectedIndex], AtlasSize);
		int before = atEntries.Count;
		List<string> skipped = AtlasCore.AddFiles(atEntries, images, size);
		atStatus.Text = "Loading " + images.Count + " file(s)…";
		List<string> failed = new List<string>();
		foreach (string path in images)
		{
			if (atThumbs.ContainsKey(path) || skipped.Contains(Path.GetFileName(path)))
			{
				continue;
			}
			try
			{
				FloatImage full = await Task.Run(() => FloatImage.Load(path));
				Size ts = FloatImage.FitSize(full.Width, full.Height, Math.Min(512, Math.Max(full.Width, full.Height)));
				FloatImage thumb = await Task.Run(() => full.Resize(ts.Width, ts.Height));
				atThumbs[path] = thumb;
				if (full.SourceBitDepth >= 16)
				{
					at16Bit.Add(path);
				}
				AtlasCore.Entry entry = atEntries.Find(e => e.Files.ContainsValue(path));
				if (entry != null && entry.SourceWidth == 0)
				{
					entry.SourceWidth = full.Width;
					entry.SourceHeight = full.Height;
				}
			}
			catch (Exception ex)
			{
				failed.Add(Path.GetFileName(path) + " (" + ex.Message + ")");
				foreach (AtlasCore.Entry e in atEntries)
				{
					foreach (KeyValuePair<AtlasCore.Map, string> kv in e.Files.Where(kv => string.Equals(kv.Value, path, StringComparison.OrdinalIgnoreCase)).ToList())
					{
						e.Files.Remove(kv.Key);
					}
				}
			}
		}
		atEntries.RemoveAll(e => e.Files.Count == 0);
		// New materials never start larger than their source (no upscaling).
		for (int i = before; i < atEntries.Count; i++)
		{
			int longest = Math.Max(atEntries[i].SourceWidth, atEntries[i].SourceHeight);
			int pow = 32;
			while (pow * 2 <= longest)
			{
				pow *= 2;
			}
			if (longest > 0)
			{
				atEntries[i].Size = Math.Min(atEntries[i].Size, pow);
			}
		}
		atBitToggle.SelectedIndex = at16Bit.Count > 0 ? 1 : 0;
		RelayoutAtlas();
		int added = atEntries.Count - before;
		if (failed.Count > 0)
		{
			Notifier.Error("Couldn't load " + string.Join(", ", failed));
		}
		else if (skipped.Count > 0)
		{
			Notifier.Warning("Skipped " + skipped.Count + " file(s) that repeat a map a material already has: " + string.Join(", ", skipped));
		}
		else
		{
			Notifier.Success(images.Count + " file(s) → " + (added > 0 ? added + " new material(s)" : "existing materials") + ".");
		}
	}

	// Packs the materials (halving every size until they fit, if allowed) and refreshes the UI.
	private void RelayoutAtlas()
	{
		if (atList == null)
		{
			return;
		}
		int atlas = AtlasSize;
		foreach (AtlasCore.Entry e in atEntries)
		{
			e.Size = Math.Min(e.Size, atlas);
		}
		AtlasCore.Order order = atOrderCombo.SelectedIndex == 1 ? AtlasCore.Order.AsAdded : AtlasCore.Order.LargestFirst;
		atShrinkShift = 0;
		int missing = AtlasCore.Pack(atEntries, atlas, atlas, AtlasPaddingPx, order);
		while (missing > 0 && atShrinkChk.Checked && atShrinkShift < 6)
		{
			atShrinkShift++;
			missing = AtlasCore.Pack(atEntries, atlas, atlas, AtlasPaddingPx, order, atShrinkShift);
		}
		atList.MaxSize = atlas;
		atList.SetEntries(atEntries);
		AtlasCore.MaskOutput mask = atMaskCombo.SelectedIndex == 1 ? AtlasCore.MaskOutput.Separate : AtlasCore.MaskOutput.Packed;
		atMaps = AtlasCore.OutputMaps(atEntries, mask);
		string current = atPreviewMapCombo.SelectedItem as string;
		atPreviewMapCombo.Items.Clear();
		foreach (AtlasCore.Map m in atMaps)
		{
			atPreviewMapCombo.Items.Add(AtlasCore.Suffix(m));
		}
		int keep = current == null ? -1 : atPreviewMapCombo.Items.IndexOf(current);
		atPreviewMapCombo.SelectedIndex = keep >= 0 ? keep : (atMaps.Count > 0 ? 0 : -1);
		atPreviewMapCombo.Invalidate();
		if (atEntries.Count == 0)
		{
			atStatus.Text = "No materials yet";
			atStatus.ForeColor = Theme.Current.TextMuted;
		}
		else
		{
			string used = (AtlasCore.Coverage(atEntries, atlas, atlas) * 100.0).ToString("0") + "% of the atlas used";
			string text = atEntries.Count + " material" + (atEntries.Count == 1 ? "" : "s") + " • " + used;
			if (atShrinkShift > 0)
			{
				text += " • shrunk to 1/" + (1 << atShrinkShift) + " to fit";
			}
			if (missing > 0)
			{
				text = missing + " don't fit • " + used;
			}
			atStatus.Text = text;
			atStatus.ForeColor = missing > 0 ? Theme.Current.Danger : Theme.Current.TextMuted;
		}
		atExportBtn.Enabled = !atExporting && atEntries.Any(e => e.Placed) && atMaps.Count > 0;
		atExportBtn.Invalidate();
		QueueAtlasPreview();
	}

	private void QueueAtlasPreview()
	{
		atPreviewTimer.Stop();
		atPreviewTimer.Start();
	}

	private async void RunAtlasPreview()
	{
		int version = ++atPreviewVersion;
		if (atEntries.Count == 0 || atPreviewMapCombo.SelectedIndex < 0 || atPreviewMapCombo.SelectedIndex >= atMaps.Count)
		{
			Image old = atPreview.Image;
			atPreview.Image = null;
			old?.Dispose();
			return;
		}
		AtlasCore.Map map = atMaps[atPreviewMapCombo.SelectedIndex];
		int atlas = AtlasSize;
		int padding = AtlasPaddingPx;
		double scale = Math.Min(1.0, (double)AtlasPreviewSide / atlas);
		// Snapshot of the layout, so later edits don't race the render.
		List<AtlasCore.Entry> snapshot = atEntries.Select(e => CloneEntry(e)).ToList();
		Dictionary<string, FloatImage> thumbs = new Dictionary<string, FloatImage>(atThumbs, StringComparer.OrdinalIgnoreCase);
		try
		{
			Bitmap bmp = await Task.Run(() =>
			{
				AtlasCore.Canvas canvas = AtlasCore.Render(snapshot, map, atlas, atlas, padding, scale, p => thumbs.TryGetValue(p, out var t) ? t : null, null);
				return canvas.ToFloatImage().ToBitmap();
			});
			if (version != atPreviewVersion)
			{
				bmp.Dispose();
				return;
			}
			if (atOutlineChk.Checked)
			{
				DrawAtlasOutlines(bmp, scale);
			}
			Image old = atPreview.Image;
			atPreview.Image = bmp;
			atPreview.InfoText = atlas + "×" + atlas + " " + AtlasCore.Suffix(map);
			atPreview.PixelRatio = (float)(1.0 / scale);
			old?.Dispose();
		}
		catch (Exception ex)
		{
			Notifier.Error("Preview failed: " + ex.Message);
		}
	}

	private static AtlasCore.Entry CloneEntry(AtlasCore.Entry e)
	{
		AtlasCore.Entry c = new AtlasCore.Entry
		{
			Name = e.Name,
			Size = e.Size,
			SourceWidth = e.SourceWidth,
			SourceHeight = e.SourceHeight,
			Placed = e.Placed,
			X = e.X,
			Y = e.Y,
			W = e.W,
			H = e.H
		};
		foreach (KeyValuePair<AtlasCore.Map, string> kv in e.Files)
		{
			c.Files[kv.Key] = kv.Value;
		}
		return c;
	}

	// Coloured frame and name on every tile; the selected material gets a thicker frame.
	private void DrawAtlasOutlines(Bitmap bmp, double scale)
	{
		using Graphics g = Graphics.FromImage(bmp);
		g.SmoothingMode = SmoothingMode.AntiAlias;
		for (int i = 0; i < atEntries.Count; i++)
		{
			AtlasCore.Entry e = atEntries[i];
			if (!e.Placed)
			{
				continue;
			}
			Rectangle r = new Rectangle((int)Math.Round(e.X * scale), (int)Math.Round(e.Y * scale), Math.Max(1, (int)Math.Round(e.W * scale) - 1), Math.Max(1, (int)Math.Round(e.H * scale) - 1));
			bool sel = e == atList.Selected;
			Color c = AtlasList.SwatchColor(i);
			using (Pen pen = new Pen(c, sel ? 4f : 2f))
			{
				g.DrawRectangle(pen, r);
			}
			string label = e.Name + "  " + e.W + "×" + e.H;
			using Font font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
			Size ts = TextRenderer.MeasureText(label, font);
			if (ts.Width + 8 < r.Width && ts.Height + 6 < r.Height)
			{
				Rectangle chip = new Rectangle(r.X + 4, r.Y + 4, ts.Width + 8, ts.Height + 2);
				using (SolidBrush back = new SolidBrush(Color.FromArgb(210, 18, 20, 26)))
				{
					g.FillRectangle(back, chip);
				}
				TextRenderer.DrawText(g, label, font, chip, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
			}
		}
	}

	private async Task ExportAtlas()
	{
		if (atExporting || atMaps.Count == 0)
		{
			return;
		}
		bool png = atFormatCombo.SelectedIndex == 0;
		string ext = png ? ".png" : ".tga";
		string path;
		using (SaveFileDialog dialog = new SaveFileDialog())
		{
			dialog.Filter = png ? "PNG|*.png" : "Targa|*.tga";
			dialog.FileName = "T_Atlas_" + AtlasCore.Suffix(atMaps[0]) + ext;
			string first = atEntries.SelectMany(e => e.Files.Values).FirstOrDefault();
			if (first != null)
			{
				dialog.InitialDirectory = Path.GetDirectoryName(first);
			}
			if (dialog.ShowDialog() != DialogResult.OK)
			{
				return;
			}
			path = dialog.FileName;
		}
		// The chosen name minus any map suffix is the base name: T_Atlas_BaseColor.png -> T_Atlas.
		string dir = Path.GetDirectoryName(path);
		string baseName = Path.GetFileNameWithoutExtension(path);
		foreach (AtlasCore.Map m in (AtlasCore.Map[])Enum.GetValues(typeof(AtlasCore.Map)))
		{
			string suffix = "_" + AtlasCore.Suffix(m);
			if (baseName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
			{
				baseName = baseName.Substring(0, baseName.Length - suffix.Length);
				break;
			}
		}
		bool sixteen = png && atBitToggle.SelectedIndex == 1;
		int atlas = AtlasSize;
		int padding = AtlasPaddingPx;
		List<AtlasCore.Entry> snapshot = atEntries.Select(e => CloneEntry(e)).ToList();
		List<AtlasCore.Map> maps = new List<AtlasCore.Map>(atMaps);
		Progress<int> progress = new Progress<int>(delegate(int v)
		{
			atProgress.Value = v;
		});
		atExporting = true;
		atExportBtn.Enabled = false;
		try
		{
			await Task.Run(delegate
			{
				for (int i = 0; i < maps.Count; i++)
				{
					int from = i * 100 / maps.Count;
					int span = 100 / maps.Count;
					// Full-resolution sources, loaded once per map type and dropped afterwards.
					Dictionary<string, FloatImage> loaded = new Dictionary<string, FloatImage>(StringComparer.OrdinalIgnoreCase);
					FloatImage Load(string p)
					{
						if (!loaded.TryGetValue(p, out var img))
						{
							img = FloatImage.Load(p);
							loaded[p] = img;
						}
						return img;
					}
					IProgress<int> sub = new Progress<int>(v => ((IProgress<int>)progress).Report(from + v * span / 100));
					AtlasCore.Canvas canvas = AtlasCore.Render(snapshot, maps[i], atlas, atlas, padding, 1.0, Load, sub);
					string file = Path.Combine(dir, baseName + "_" + AtlasCore.Suffix(maps[i]) + ext);
					if (png)
					{
						AtlasCore.SavePng(canvas, file, sixteen);
					}
					else
					{
						AtlasCore.SaveTga(canvas, file);
					}
				}
				File.WriteAllText(Path.Combine(dir, baseName + "_layout.json"), AtlasCore.LayoutJson(snapshot, baseName, atlas, atlas, padding, maps, ext));
			});
			Notifier.Success("Exported " + maps.Count + " atlas map(s) and " + baseName + "_layout.json to " + dir);
		}
		catch (Exception ex)
		{
			Notifier.Error("Export failed: " + ex.Message);
		}
		finally
		{
			atExporting = false;
			atProgress.Value = 0;
			RelayoutAtlas();
		}
	}
}
