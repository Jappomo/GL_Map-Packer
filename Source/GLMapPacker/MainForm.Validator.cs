using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GLMapPacker;

// "Validator" tab: load a PBR texture set, see which texels break physically based ranges, fix them
// (by hand or with Auto-correct) and save the fixed maps. Works on the textures directly (no 3D view).
public partial class MainForm
{
	private const int PbrPreviewSide = 1024;

	private static readonly PbrCore.Slot[] PbrSlots = new PbrCore.Slot[6]
	{
		PbrCore.Slot.Albedo,
		PbrCore.Slot.Normal,
		PbrCore.Slot.ORM,
		PbrCore.Slot.Roughness,
		PbrCore.Slot.Metallic,
		PbrCore.Slot.AO
	};

	private Panel pageValidator;

	private TitledCard pvMapsCard;

	private TitledCard pvAdjustCard;

	private readonly Dictionary<PbrCore.Slot, FileDropBox> pvBoxes = new Dictionary<PbrCore.Slot, FileDropBox>();

	private IssueLegend pvLegend;

	private FlatButton pvAutoBtn;

	private FlatButton pvResetBtn;

	private ThemedComboBox pvViewCombo;

	private PreviewBox pvPreview;

	private SegmentedToggle pvSection;

	private Panel pvChecksPanel;

	private Panel pvAlbedoPanel;

	private Panel pvOrmPanel;

	private Panel pvNormalPanel;

	private ThemedCheckBox pvCheckAlbedo;

	private ThemedSlider pvAlbedoMin;

	private ThemedSlider pvAlbedoMax;

	private ThemedCheckBox pvCheckMetal;

	private SegmentedToggle pvMetalThreshold;

	private ThemedCheckBox pvCheckRough;

	private ThemedSlider pvRoughMin;

	private ThemedSlider pvRoughMax;

	private ThemedCheckBox pvShowWarnings;

	private ThemedCheckBox pvAnimate;

	private ThemedSlider pvFixDarks;

	private ThemedSlider pvFixBrights;

	private ThemedSlider pvFixMetal;

	private SegmentedToggle pvChannel;

	private ThemedSlider pvChMin;

	private ThemedSlider pvChMax;

	private ThemedSlider pvChMid;

	private ThemedSlider pvChContrast;

	private ThemedSlider pvNormalIntensity;

	private Label pvCapSave;

	private SegmentedToggle pvBitToggle;

	private FlatButton pvSaveBtn;

	private ThemedProgress pvProgress;

	private Timer pvUpdateTimer;

	private Timer pvAnimTimer;

	private readonly Dictionary<PbrCore.Slot, FloatImage> pvMaps = new Dictionary<PbrCore.Slot, FloatImage>();

	private readonly Dictionary<PbrCore.Slot, string> pvPaths = new Dictionary<PbrCore.Slot, string>();

	private PbrCore.Settings pvSettings = new PbrCore.Settings();

	// The maps at preview size, unfixed.
	private PbrCore.Working pvOriginal;

	// Validated view: the fixed map's pixels (BGRA) and each texel's issues, for the animated overlay.
	private byte[] pvViewPixels;

	private PbrCore.Issue[] pvFlags;

	private int pvViewW;

	private int pvViewH;

	private int pvPhase;

	private int pvVersion;

	private int pvBuildVersion;

	private bool pvSyncing;

	private bool pvSaving;

	private void BuildValidatorPage()
	{
		pageValidator = new Panel
		{
			Dock = DockStyle.Fill
		};
		pvUpdateTimer = new Timer
		{
			Interval = 90
		};
		pvUpdateTimer.Tick += delegate
		{
			pvUpdateTimer.Stop();
			RunValidation();
		};
		pvAnimTimer = new Timer
		{
			Interval = 70
		};
		pvAnimTimer.Tick += delegate
		{
			pvPhase = (pvPhase + 1) % 16;
			ComposeValidated();
		};
		// Maps and results.
		pvMapsCard = Card("Texture set");
		foreach (PbrCore.Slot slot in PbrSlots)
		{
			FileDropBox box = new FileDropBox
			{
				Caption = SlotLabel(slot),
				Prompt = "drop or click",
				Filter = "Images|*.png;*.tga;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|All files|*.*"
			};
			PbrCore.Slot s = slot;
			box.FileChanged += delegate
			{
				LoadPbrMap(s, box.FilePath);
			};
			pvBoxes[slot] = box;
			pvMapsCard.Controls.Add(box);
		}
		pvLegend = new IssueLegend();
		pvAutoBtn = new FlatButton
		{
			Text = "Auto-correct",
			Role = FlatButton.ButtonRole.Accent,
			Enabled = false
		};
		pvAutoBtn.Click += delegate
		{
			AutoCorrectPbr();
		};
		pvResetBtn = new FlatButton
		{
			Text = "Reset fixes",
			Role = FlatButton.ButtonRole.Secondary
		};
		pvResetBtn.Click += delegate
		{
			pvSettings.ResetFixes();
			SyncPbrControls();
			QueueValidation();
		};
		pvMapsCard.Controls.Add(pvLegend);
		pvMapsCard.Controls.Add(pvAutoBtn);
		pvMapsCard.Controls.Add(pvResetBtn);
		pvMapsCard.AllowDrop = true;
		pvMapsCard.DragEnter += AtlasDragEnter;
		pvMapsCard.DragDrop += PbrDragDrop;
		// Texture view.
		pvViewCombo = new ThemedComboBox();
		pvViewCombo.SelectedIndexChanged += delegate
		{
			QueueValidation();
		};
		pvPreview = new PreviewBox
		{
			EmptyText = "Drop a PBR texture set",
			EmptyHint = "Albedo, Normal, ORM or Roughness / Metallic / AO; maps are assigned by file name",
			AcceptsFiles = true,
			CompareLabel = "Original",
			ImageLabel = "Validated"
		};
		pvPreview.DragDrop += PbrDragDrop;
		// Checks and fixes.
		pvAdjustCard = Card(null);
		pvSection = new SegmentedToggle
		{
			Options = new string[4] { "Checks", "Albedo", "ORM", "Normal" }
		};
		pvSection.SelectedIndexChanged += delegate
		{
			ShowPbrSection();
		};
		pvCheckAlbedo = PbrCheck("BaseColor range (non-metal)", true);
		pvAlbedoMin = PbrSlider("Min sRGB", 0.0, 100.0, 30.0, 0, "");
		pvAlbedoMax = PbrSlider("Max sRGB", 200.0, 255.0, 240.0, 0, "");
		pvCheckMetal = PbrCheck("Metal reflectance", true);
		pvMetalThreshold = new SegmentedToggle
		{
			Options = new string[2] { "60–100%", "70–100%" }
		};
		pvMetalThreshold.SelectedIndex = 1;
		pvMetalThreshold.SelectedIndexChanged += delegate
		{
			PbrControlsChanged();
		};
		pvCheckRough = PbrCheck("Roughness range", true);
		pvRoughMin = PbrSlider("Min", 0.0, 0.5, 0.05, 2, "");
		pvRoughMax = PbrSlider("Max", 0.5, 1.0, 1.0, 2, "");
		pvShowWarnings = PbrCheck("Show warnings", true);
		pvAnimate = PbrCheck("Animated warning stripes", true);
		pvChecksPanel = PbrPanel(pvCheckAlbedo, pvAlbedoMin, pvAlbedoMax, pvCheckMetal, pvMetalThreshold, pvCheckRough, pvRoughMin, pvRoughMax, pvShowWarnings, pvAnimate);
		pvFixDarks = PbrSlider("Fix too-dark albedo", 0.0, 100.0, 0.0, 0, "%");
		pvFixBrights = PbrSlider("Fix too-bright albedo", 0.0, 100.0, 0.0, 0, "%");
		pvFixMetal = PbrSlider("Fix metal reflectance", 0.0, 100.0, 0.0, 0, "%");
		pvAlbedoPanel = PbrPanel(pvFixDarks, pvFixBrights, pvFixMetal, PbrHint("Picks out the illegal blacks and whites (and dark metals) and eases them into the legal range, keeping their hue and gradients. Metal is where metallic ≥ 0.5."));
		pvChannel = new SegmentedToggle
		{
			Options = new string[3] { "AO", "Roughness", "Metallic" }
		};
		pvChannel.SelectedIndex = 1;
		pvChannel.SelectedIndexChanged += delegate
		{
			SyncPbrControls();
		};
		pvChMin = PbrSlider("Min value", 0.0, 1.0, 0.0, 2, "");
		pvChMax = PbrSlider("Max value", 0.0, 1.0, 1.0, 2, "");
		pvChMid = PbrSlider("Mid-grey shift", -0.4, 0.4, 0.0, 2, "");
		pvChContrast = PbrSlider("Contrast", -1.0, 1.0, 0.0, 2, "");
		pvOrmPanel = PbrPanel(pvChannel, pvChMin, pvChMax, pvChMid, pvChContrast, PbrHint("Applies to the channel in the ORM map, or to its separate map."));
		pvNormalIntensity = PbrSlider("Intensity", 0.0, 3.0, 1.0, 2, "×");
		pvNormalPanel = PbrPanel(pvNormalIntensity, PbrHint("Scales the normal map's slopes; 0 is flat, above 1 is stronger."));
		pvCapSave = Caption("Save fixed maps");
		pvBitToggle = new SegmentedToggle
		{
			Options = new string[2] { "8-bit", "16-bit" }
		};
		pvSaveBtn = new FlatButton
		{
			Text = "Save fixed maps",
			Role = FlatButton.ButtonRole.Accent,
			Enabled = false
		};
		pvSaveBtn.Click += async delegate
		{
			await SavePbrMaps();
		};
		pvProgress = new ThemedProgress();
		pvAdjustCard.Controls.AddRange(new Control[9] { pvSection, pvChecksPanel, pvAlbedoPanel, pvOrmPanel, pvNormalPanel, pvCapSave, pvBitToggle, pvProgress, pvSaveBtn });
		pageValidator.Controls.Add(pvMapsCard);
		pageValidator.Controls.Add(pvViewCombo);
		pageValidator.Controls.Add(pvPreview);
		pageValidator.Controls.Add(pvAdjustCard);
		pageValidator.Resize += delegate
		{
			LayoutValidatorPage();
		};
		ShowPbrSection();
		SyncPbrControls();
	}

	private static string SlotLabel(PbrCore.Slot s)
	{
		return s switch
		{
			PbrCore.Slot.Albedo => "Albedo",
			PbrCore.Slot.Normal => "Normal",
			PbrCore.Slot.ORM => "ORM",
			PbrCore.Slot.Roughness => "Roughness",
			PbrCore.Slot.Metallic => "Metallic",
			_ => "AO",
		};
	}

	private ThemedCheckBox PbrCheck(string text, bool on)
	{
		ThemedCheckBox c = new ThemedCheckBox
		{
			Text = text,
			Checked = on
		};
		c.CheckedChanged += delegate
		{
			PbrControlsChanged();
		};
		return c;
	}

	private ThemedSlider PbrSlider(string caption, double min, double max, double value, int decimals, string suffix)
	{
		ThemedSlider s = new ThemedSlider(caption, min, max, value, decimals, suffix);
		s.ValueChanged += delegate
		{
			PbrControlsChanged();
		};
		return s;
	}

	private static Label PbrHint(string text)
	{
		return new Label
		{
			Name = "capPvHint",
			AutoSize = false,
			Text = text,
			Font = Ui.Small,
			Height = Ui.S(52)
		};
	}

	// Transparent panel stacking its controls; laid out in LayoutValidatorPage.
	private static Panel PbrPanel(params Control[] controls)
	{
		Panel p = new Panel
		{
			BackColor = Color.Transparent
		};
		p.Controls.AddRange(controls);
		return p;
	}

	private void ShowPbrSection()
	{
		int i = pvSection.SelectedIndex;
		pvChecksPanel.Visible = i == 0;
		pvAlbedoPanel.Visible = i == 1;
		pvOrmPanel.Visible = i == 2;
		pvNormalPanel.Visible = i == 3;
	}

	private void LayoutValidatorPage()
	{
		if (pageValidator == null)
		{
			return;
		}
		int width = pageValidator.Width;
		int height = pageValidator.Height;
		int side = SideWidth;
		int inner = side - Pad * 2;
		// Left: map slots, results, auto-correct.
		pvMapsCard.Location = new Point(0, 0);
		pvMapsCard.Size = new Size(side, height);
		int y = pvMapsCard.ContentTop;
		foreach (PbrCore.Slot slot in PbrSlots)
		{
			FileDropBox b = pvBoxes[slot];
			b.Location = new Point(Pad, y);
			b.Width = inner;
			y = b.Bottom + Ui.S(6);
		}
		int by = height - Pad - pvAutoBtn.Height;
		int half = (inner - Ui.S(8)) / 2;
		pvAutoBtn.Location = new Point(Pad, by);
		pvAutoBtn.Width = inner - half - Ui.S(8);
		pvResetBtn.Location = new Point(pvAutoBtn.Right + Ui.S(8), by);
		pvResetBtn.Width = half;
		pvLegend.Location = new Point(Pad, y + Ui.S(10));
		pvLegend.Size = new Size(inner, Math.Max(Ui.S(40), by - Ui.S(12) - pvLegend.Top));
		// Right: checks and fixes.
		int rx = width - side;
		pvAdjustCard.Location = new Point(rx, 0);
		pvAdjustCard.Size = new Size(side, height);
		pvSection.Location = new Point(Pad, Pad);
		pvSection.Width = inner;
		int top = pvSection.Bottom + Ui.S(16);
		foreach (Panel p in new Panel[4] { pvChecksPanel, pvAlbedoPanel, pvOrmPanel, pvNormalPanel })
		{
			p.Location = new Point(Pad, top);
			p.Width = inner;
			LayoutPbrPanel(p, inner);
		}
		int sy = height - Pad - pvSaveBtn.Height;
		pvSaveBtn.Location = new Point(Pad, sy);
		pvSaveBtn.Width = inner;
		pvProgress.Location = new Point(Pad, sy - Ui.S(14));
		pvProgress.Width = inner;
		pvBitToggle.Location = new Point(Pad + inner - Ui.S(130), pvProgress.Top - Ui.S(10) - pvBitToggle.Height);
		pvBitToggle.Width = Ui.S(130);
		pvCapSave.Location = new Point(Pad, pvBitToggle.Top + (pvBitToggle.Height - pvCapSave.Height) / 2);
		// Centre: texture view.
		int px = side + Gap;
		int pw = Math.Max(Ui.S(120), rx - Gap - px);
		pvViewCombo.Location = new Point(px, 0);
		pvViewCombo.Width = Math.Min(pw, Ui.S(200));
		pvPreview.Location = new Point(px, pvViewCombo.Bottom + Ui.S(10));
		pvPreview.Size = new Size(pw, height - pvPreview.Top);
	}

	// Stacks a section's controls; pairs of Min/Max sliders share a row.
	private void LayoutPbrPanel(Panel p, int width)
	{
		int y = 0;
		int half = (width - Ui.S(16)) / 2;
		Control[] items = p.Controls.Cast<Control>().ToArray();
		for (int i = 0; i < items.Length; i++)
		{
			Control c = items[i];
			bool pair = (c == pvAlbedoMin || c == pvRoughMin) && i + 1 < items.Length;
			if (pair)
			{
				c.Location = new Point(0, y);
				c.Width = half;
				items[i + 1].Location = new Point(half + Ui.S(16), y);
				items[i + 1].Width = width - half - Ui.S(16);
				y += c.Height + Ui.S(10);
				i++;
				continue;
			}
			if (c is ThemedCheckBox && y > 0)
			{
				y += Ui.S(6);
			}
			c.Location = new Point(c == pvMetalThreshold ? Ui.S(28) : 0, y);
			c.Width = c == pvMetalThreshold ? Ui.S(170) : width;
			y += c.Height + (c is ThemedCheckBox ? Ui.S(4) : Ui.S(10));
		}
		p.Height = y;
	}

	// ---------------------------------------------------------------- loading

	private void PbrDragDrop(object sender, DragEventArgs e)
	{
		if (!(e.Data.GetData(DataFormats.FileDrop) is string[] files))
		{
			return;
		}
		List<string> ignored = new List<string>();
		foreach (string f in files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
		{
			AtlasCore.Detect(f, out var map, out _);
			PbrCore.Slot? slot = map switch
			{
				AtlasCore.Map.Albedo => PbrCore.Slot.Albedo,
				AtlasCore.Map.Normal => PbrCore.Slot.Normal,
				AtlasCore.Map.ORM => PbrCore.Slot.ORM,
				AtlasCore.Map.Roughness => PbrCore.Slot.Roughness,
				AtlasCore.Map.Metallic => PbrCore.Slot.Metallic,
				AtlasCore.Map.AO => PbrCore.Slot.AO,
				_ => null,
			};
			if (slot.HasValue)
			{
				pvBoxes[slot.Value].FilePath = f;
			}
			else
			{
				ignored.Add(Path.GetFileName(f));
			}
		}
		if (ignored.Count > 0)
		{
			Notifier.Info("Not used for validation: " + string.Join(", ", ignored));
		}
	}

	private async void LoadPbrMap(PbrCore.Slot slot, string path)
	{
		if (string.IsNullOrEmpty(path))
		{
			pvMaps.Remove(slot);
			pvPaths.Remove(slot);
			RebuildPbrWorking();
			return;
		}
		try
		{
			FloatImage img = await Task.Run(() => FloatImage.Load(path));
			pvMaps[slot] = img;
			pvPaths[slot] = path;
			pvBoxes[slot].Info = img.Width + "×" + img.Height + (img.SourceBitDepth >= 16 ? " • 16-bit" : "");
			pvBoxes[slot].Invalidate();
			if (img.SourceBitDepth >= 16)
			{
				pvBitToggle.SelectedIndex = 1;
			}
			RebuildPbrWorking();
		}
		catch (Exception ex)
		{
			Notifier.Error("Couldn't load " + Path.GetFileName(path) + ": " + ex.Message);
			pvBoxes[slot].FilePath = null;
		}
	}

	private async void RebuildPbrWorking()
	{
		int version = ++pvBuildVersion;
		pvOriginal = null;
		if (!PbrCore.ReferenceSize(pvMaps, out var rw, out var rh))
		{
			RefreshPbrViewList();
			ClearPbrView();
			return;
		}
		Size ps = FloatImage.FitSize(rw, rh, Math.Min(PbrPreviewSide, Math.Max(rw, rh)));
		Dictionary<PbrCore.Slot, FloatImage> maps = new Dictionary<PbrCore.Slot, FloatImage>(pvMaps);
		PbrCore.Working w = await Task.Run(() => PbrCore.Build(maps, ps.Width, ps.Height));
		if (version != pvBuildVersion)
		{
			return;
		}
		pvOriginal = w;
		RefreshPbrViewList();
		QueueValidation();
	}

	private void RefreshPbrViewList()
	{
		string current = pvViewCombo.SelectedItem as string;
		pvViewCombo.Items.Clear();
		if (pvOriginal != null)
		{
			if (pvOriginal.Albedo != null)
			{
				pvViewCombo.Items.Add("Albedo");
			}
			if (pvOriginal.Rough != null)
			{
				pvViewCombo.Items.Add("Roughness");
			}
			if (pvOriginal.Metal != null)
			{
				pvViewCombo.Items.Add("Metallic");
			}
			if (pvOriginal.AO != null)
			{
				pvViewCombo.Items.Add("AO");
			}
			if (pvOriginal.Normal != null)
			{
				pvViewCombo.Items.Add("Normal");
			}
		}
		int keep = current == null ? -1 : pvViewCombo.Items.IndexOf(current);
		pvViewCombo.SelectedIndex = keep >= 0 ? keep : (pvViewCombo.Items.Count > 0 ? 0 : -1);
		pvViewCombo.Invalidate();
		bool any = pvOriginal != null;
		pvAutoBtn.Enabled = any;
		pvSaveBtn.Enabled = any && !pvSaving;
		pvAutoBtn.Invalidate();
		pvSaveBtn.Invalidate();
	}

	private void ClearPbrView()
	{
		pvAnimTimer.Stop();
		pvFlags = null;
		pvViewPixels = null;
		Image a = pvPreview.Image;
		Image b = pvPreview.CompareImage;
		pvPreview.CompareImage = null;
		pvPreview.Image = null;
		a?.Dispose();
		b?.Dispose();
		pvLegend.SetStats(null, pvSettings);
	}

	// ---------------------------------------------------------------- settings <-> controls

	private PbrCore.ChannelAdjust SelectedChannel()
	{
		return pvChannel.SelectedIndex switch
		{
			0 => pvSettings.AO,
			2 => pvSettings.Metal,
			_ => pvSettings.Rough,
		};
	}

	// Copies the settings into the controls (after auto-correct, reset or a channel switch).
	private void SyncPbrControls()
	{
		pvSyncing = true;
		PbrCore.Settings s = pvSettings;
		pvCheckAlbedo.Checked = s.CheckAlbedo;
		pvAlbedoMin.Value = s.AlbedoMin;
		pvAlbedoMax.Value = s.AlbedoMax;
		pvCheckMetal.Checked = s.CheckMetal;
		pvMetalThreshold.SelectedIndex = s.MetalMinPercent < 65f ? 0 : 1;
		pvCheckRough.Checked = s.CheckRoughness;
		pvRoughMin.Value = s.RoughMin;
		pvRoughMax.Value = s.RoughMax;
		pvFixDarks.Value = s.FixDarks * 100f;
		pvFixBrights.Value = s.FixBrights * 100f;
		pvFixMetal.Value = s.FixMetal * 100f;
		PbrCore.ChannelAdjust c = SelectedChannel();
		pvChMin.Value = c.Min;
		pvChMax.Value = c.Max;
		pvChMid.Value = c.Mid;
		pvChContrast.Value = c.Contrast;
		pvNormalIntensity.Value = s.NormalIntensity;
		pvSyncing = false;
	}

	private void PbrControlsChanged()
	{
		if (pvSyncing || pvSection == null)
		{
			return;
		}
		PbrCore.Settings s = pvSettings;
		s.CheckAlbedo = pvCheckAlbedo.Checked;
		s.AlbedoMin = (float)pvAlbedoMin.Value;
		s.AlbedoMax = (float)pvAlbedoMax.Value;
		s.CheckMetal = pvCheckMetal.Checked;
		s.MetalMinPercent = pvMetalThreshold.SelectedIndex == 0 ? 60f : 70f;
		s.CheckRoughness = pvCheckRough.Checked;
		s.RoughMin = (float)pvRoughMin.Value;
		s.RoughMax = (float)pvRoughMax.Value;
		s.FixDarks = (float)(pvFixDarks.Value / 100.0);
		s.FixBrights = (float)(pvFixBrights.Value / 100.0);
		s.FixMetal = (float)(pvFixMetal.Value / 100.0);
		PbrCore.ChannelAdjust c = SelectedChannel();
		c.Min = (float)pvChMin.Value;
		c.Max = (float)pvChMax.Value;
		c.Mid = (float)pvChMid.Value;
		c.Contrast = (float)pvChContrast.Value;
		s.NormalIntensity = (float)pvNormalIntensity.Value;
		QueueValidation();
	}

	// ---------------------------------------------------------------- validation and view

	private void QueueValidation()
	{
		pvUpdateTimer.Stop();
		pvUpdateTimer.Start();
	}

	private async void RunValidation()
	{
		PbrCore.Working original = pvOriginal;
		if (original == null || pvViewCombo.SelectedIndex < 0)
		{
			ClearPbrView();
			return;
		}
		int version = ++pvVersion;
		PbrCore.Settings settings = pvSettings.Clone();
		string view = (string)pvViewCombo.SelectedItem;
		try
		{
			var result = await Task.Run(() =>
			{
				PbrCore.Working fixedW = PbrCore.Fix(original, settings);
				PbrCore.Issue[] flags = PbrCore.Validate(fixedW, settings, out var stats);
				return new
				{
					Original = ViewPixels(original, view),
					Fixed = ViewPixels(fixedW, view),
					Flags = flags,
					Stats = stats
				};
			});
			if (version != pvVersion)
			{
				return;
			}
			pvViewW = original.W;
			pvViewH = original.H;
			pvViewPixels = result.Fixed;
			pvFlags = result.Flags;
			Image oldCompare = pvPreview.CompareImage;
			pvPreview.CompareImage = PixelsToBitmap(result.Original, pvViewW, pvViewH);
			oldCompare?.Dispose();
			ComposeValidated();
			pvPreview.InfoText = view + " • " + (pvMaps.TryGetValue(PbrCore.Slot.Albedo, out var a) ? a.Width + "×" + a.Height : pvViewW + "×" + pvViewH);
			pvLegend.SetStats(result.Stats, settings, original);
			bool animate = pvAnimate.Checked && pvShowWarnings.Checked && result.Stats.Any;
			if (animate && !pvAnimTimer.Enabled)
			{
				pvAnimTimer.Start();
			}
			else if (!animate)
			{
				pvAnimTimer.Stop();
			}
		}
		catch (Exception ex)
		{
			Notifier.Error("Validation failed: " + ex.Message);
		}
	}

	// BGRA pixels of one map of a working set (greyscale channels shown as grey).
	private static byte[] ViewPixels(PbrCore.Working w, string view)
	{
		int n = w.W * w.H;
		byte[] px = new byte[n * 4];
		float[] rgb = view == "Albedo" ? w.Albedo : (view == "Normal" ? w.Normal : null);
		float[] gray = view == "Roughness" ? w.Rough : (view == "Metallic" ? w.Metal : (view == "AO" ? w.AO : null));
		for (int i = 0; i < n; i++)
		{
			byte r;
			byte g;
			byte b;
			if (rgb != null)
			{
				r = B(rgb[i * 3]);
				g = B(rgb[i * 3 + 1]);
				b = B(rgb[i * 3 + 2]);
			}
			else
			{
				r = g = b = gray == null ? (byte)0 : B(gray[i]);
			}
			px[i * 4] = b;
			px[i * 4 + 1] = g;
			px[i * 4 + 2] = r;
			px[i * 4 + 3] = byte.MaxValue;
		}
		return px;
	}

	private static byte B(float v)
	{
		return (byte)(v <= 0f ? 0 : (v >= 1f ? 255 : (int)(v * 255f + 0.5f)));
	}

	// Validated view: the fixed map with each failing texel tinted in its issue's colour, and
	// moving diagonal stripes when animation is on.
	private void ComposeValidated()
	{
		if (pvViewPixels == null || pvFlags == null)
		{
			return;
		}
		byte[] outPx = (byte[])pvViewPixels.Clone();
		if (pvShowWarnings.Checked)
		{
			int w = pvViewW;
			int phase = pvAnimate.Checked ? pvPhase : 0;
			PbrCore.Issue[] flags = pvFlags;
			Color[] colors = IssueLegend.Colors;
			Parallel.For(0, pvViewH, y =>
			{
				for (int x = 0; x < w; x++)
				{
					int i = y * w + x;
					PbrCore.Issue f = flags[i];
					if (f == PbrCore.Issue.None)
					{
						continue;
					}
					int k = (f & PbrCore.Issue.TooDark) != 0 ? 0 : ((f & PbrCore.Issue.TooBright) != 0 ? 1 : ((f & PbrCore.Issue.MetalLow) != 0 ? 2 : 3));
					Color c = colors[k];
					bool stripe = ((x + y + phase) & 15) < 7;
					float t = stripe ? 0.85f : 0.45f;
					int o = i * 4;
					outPx[o] = (byte)(outPx[o] + (c.B - outPx[o]) * t);
					outPx[o + 1] = (byte)(outPx[o + 1] + (c.G - outPx[o + 1]) * t);
					outPx[o + 2] = (byte)(outPx[o + 2] + (c.R - outPx[o + 2]) * t);
				}
			});
		}
		Image old = pvPreview.Image;
		pvPreview.Image = PixelsToBitmap(outPx, pvViewW, pvViewH);
		old?.Dispose();
	}

	private static Bitmap PixelsToBitmap(byte[] px, int w, int h)
	{
		Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
		BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
		try
		{
			for (int y = 0; y < h; y++)
			{
				Marshal.Copy(px, y * w * 4, (IntPtr)(data.Scan0.ToInt64() + (long)y * data.Stride), w * 4);
			}
		}
		finally
		{
			bmp.UnlockBits(data);
		}
		return bmp;
	}

	// ---------------------------------------------------------------- auto-correct and save

	private void AutoCorrectPbr()
	{
		if (pvOriginal == null)
		{
			return;
		}
		List<string> changes = PbrCore.AutoCorrect(pvOriginal, pvSettings);
		SyncPbrControls();
		QueueValidation();
		if (changes.Count == 0)
		{
			Notifier.Success("Everything already passes; nothing to correct.");
		}
		else
		{
			string text = string.Join("; ", changes);
			Notifier.Success("Auto-correct: " + char.ToUpper(text[0]) + text.Substring(1) + ".");
		}
	}

	private async Task SavePbrMaps()
	{
		if (pvSaving || pvMaps.Count == 0)
		{
			return;
		}
		PbrCore.Settings s = pvSettings.Clone();
		// Only maps whose fixes change them; the albedo also depends on the metallic adjustment.
		List<PbrCore.Slot> slots = new List<PbrCore.Slot>();
		foreach (PbrCore.Slot slot in pvMaps.Keys)
		{
			bool changed = slot switch
			{
				PbrCore.Slot.Albedo => s.FixDarks > 0f || s.FixBrights > 0f || s.FixMetal > 0f,
				PbrCore.Slot.Normal => s.NormalIntensity != 1f,
				PbrCore.Slot.ORM => !s.AO.IsIdentity || !s.Rough.IsIdentity || !s.Metal.IsIdentity,
				PbrCore.Slot.Roughness => !s.Rough.IsIdentity,
				PbrCore.Slot.Metallic => !s.Metal.IsIdentity,
				_ => !s.AO.IsIdentity,
			};
			if (changed)
			{
				slots.Add(slot);
			}
		}
		if (slots.Count == 0)
		{
			Notifier.Info("No fixes applied yet, so there's nothing to save. Try Auto-correct.");
			return;
		}
		string folder;
		using (FolderBrowserDialog dialog = new FolderBrowserDialog())
		{
			dialog.Description = "Folder for the fixed maps (saved as <name>_fixed.png)";
			dialog.SelectedPath = Path.GetDirectoryName(pvPaths[slots[0]]);
			if (dialog.ShowDialog() != DialogResult.OK)
			{
				return;
			}
			folder = dialog.SelectedPath;
		}
		bool sixteen = pvBitToggle.SelectedIndex == 1;
		Dictionary<PbrCore.Slot, FloatImage> maps = new Dictionary<PbrCore.Slot, FloatImage>(pvMaps);
		Dictionary<PbrCore.Slot, string> paths = new Dictionary<PbrCore.Slot, string>(pvPaths);
		pvSaving = true;
		pvSaveBtn.Enabled = false;
		try
		{
			PbrCore.Stats check = null;
			await Task.Run(delegate
			{
				Dictionary<PbrCore.Slot, FloatImage> fixedMaps = new Dictionary<PbrCore.Slot, FloatImage>(maps);
				for (int i = 0; i < slots.Count; i++)
				{
					FloatImage fixedMap = PbrCore.FixedMap(maps, slots[i], s);
					fixedMaps[slots[i]] = fixedMap;
					string file = Path.Combine(folder, Path.GetFileNameWithoutExtension(paths[slots[i]]) + "_fixed.png");
					PngIO.Write(file, fixedMap, sixteen);
					int done = (i + 1) * 90 / slots.Count;
					BeginInvoke((Action)delegate
					{
						pvProgress.Value = done;
					});
				}
				// Re-check the saved maps at full resolution.
				PbrCore.ReferenceSize(fixedMaps, out var w, out var h);
				PbrCore.Settings checks = s.Clone();
				checks.ResetFixes();
				PbrCore.Validate(PbrCore.Build(fixedMaps, w, h), checks, out check);
			});
			string verdict = check.Any ? " Still failing at full resolution: " + IssueLegend.Summary(check) + "." : " All enabled checks pass at full resolution.";
			Notifier.Success("Saved " + slots.Count + " fixed map(s) to " + folder + "." + verdict);
		}
		catch (Exception ex)
		{
			Notifier.Error("Save failed: " + ex.Message);
		}
		finally
		{
			pvSaving = false;
			pvProgress.Value = 0;
			pvSaveBtn.Enabled = pvOriginal != null;
		}
	}
}

// Results list: one row per check with its colour, what it means and the share of texels failing.
public class IssueLegend : Control, IThemeable
{
	public static readonly Color[] Colors = new Color[4]
	{
		Color.FromArgb(61, 115, 255),
		Color.FromArgb(240, 80, 75),
		Color.FromArgb(240, 170, 40),
		Color.FromArgb(200, 90, 240)
	};

	private PbrCore.Stats stats;

	private PbrCore.Settings settings;

	private PbrCore.Working working;

	public IssueLegend()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		BackColor = Color.Transparent;
	}

	public void SetStats(PbrCore.Stats s, PbrCore.Settings set, PbrCore.Working w = null)
	{
		stats = s;
		settings = set;
		working = w;
		Invalidate();
	}

	public void ApplyTheme(Palette p)
	{
		Invalidate();
	}

	public static string Summary(PbrCore.Stats s)
	{
		List<string> parts = new List<string>();
		if (s.TooDark > 0)
		{
			parts.Add(P(s.TooDark) + " too dark");
		}
		if (s.TooBright > 0)
		{
			parts.Add(P(s.TooBright) + " too bright");
		}
		if (s.MetalLow > 0)
		{
			parts.Add(P(s.MetalLow) + " metal too dark");
		}
		if (s.RoughOut > 0)
		{
			parts.Add(P(s.RoughOut) + " roughness out of range");
		}
		return string.Join(", ", parts);
	}

	private static string P(double v)
	{
		return v <= 0 ? "0%" : (v < 0.001 ? "<0.1%" : (v * 100.0).ToString("0.#") + "%");
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Palette p = Theme.Current;
		Graphics g = e.Graphics;
		g.SmoothingMode = SmoothingMode.AntiAlias;
		g.Clear(GfxUtil.ResolveBack(this));
		TextRenderer.DrawText(g, "RESULTS", Ui.Label, new Point(0, 0), p.TextMuted);
		int y = Ui.S(22);
		if (stats == null)
		{
			TextRenderer.DrawText(g, "Load maps to validate them.", Ui.Small, new Rectangle(0, y, base.Width, Ui.S(20)), p.TextMuted, TextFormatFlags.VerticalCenter);
			return;
		}
		string metalRange = settings.MetalMinPercent.ToString("0") + "%";
		var rows = new (string label, double value, bool active, bool hasData)[4]
		{
			("Albedo too dark (< " + settings.AlbedoMin.ToString("0") + ")", stats.TooDark, settings.CheckAlbedo, working?.Albedo != null),
			("Albedo too bright (> " + settings.AlbedoMax.ToString("0") + ")", stats.TooBright, settings.CheckAlbedo, working?.Albedo != null),
			("Metal reflectance below " + metalRange, stats.MetalLow, settings.CheckMetal, working?.Albedo != null && working?.Metal != null),
			("Roughness outside " + settings.RoughMin.ToString("0.00") + "–" + settings.RoughMax.ToString("0.00"), stats.RoughOut, settings.CheckRoughness, working?.Rough != null)
		};
		int rowH = Ui.S(24);
		for (int i = 0; i < rows.Length && y + rowH <= base.Height; i++)
		{
			var r = rows[i];
			int d = Ui.S(10);
			using (SolidBrush brush = new SolidBrush(r.active && r.hasData ? Colors[i] : Theme.Mix(p.Border, p.TextMuted, 0.3)))
			{
				g.FillEllipse(brush, 0, y + (rowH - d) / 2, d, d);
			}
			Color text = r.active && r.hasData ? p.Text : p.TextMuted;
			TextRenderer.DrawText(g, r.label, Ui.Small, new Rectangle(d + Ui.S(8), y, base.Width - d - Ui.S(64), rowH), text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
			string value = !r.hasData ? "no map" : (!r.active ? "off" : (r.value <= 0 ? "pass" : P(r.value)));
			Color vc = !r.hasData || !r.active ? p.TextMuted : (r.value <= 0 ? p.Success : Colors[i]);
			TextRenderer.DrawText(g, value, Ui.Label, new Rectangle(0, y, base.Width, rowH), vc, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
			y += rowH;
		}
	}
}
