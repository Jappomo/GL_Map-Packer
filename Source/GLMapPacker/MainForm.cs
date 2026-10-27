using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GLMapPacker;

public partial class MainForm : Form
{
	private const string AppVersion = "2.1";

	private Panel header;

	private ThemeToggle toggle;

	private TabStrip tabs;

	private Panel pageHost;

	private StatusBar statusBar;

	private Toast toast;

	private Panel pagePack;

	private Panel pageUnpack;

	private Panel pageConvert;

	private Panel pageUtility;

	private ThemedComboBox formatCombo;

	private ChannelSlot slotR;

	private ChannelSlot slotG;

	private ChannelSlot slotB;

	private ChannelSlot slotA;

	private PreviewBox packResult;

	private TitledCard packCard;

	private FlatButton packBtn;

	private FlatButton packSaveBtn;

	private ThemedComboBox presetCombo;

	private Bitmap packedImage;

	private ThemedProgress packProgress;

	private TitledCard unpackCard;

	private PreviewBox unpackSource;

	private FlatButton unpackLoadBtn;

	private FlatButton unpackSaveAllBtn;

	private ThemedCheckBox sameFolderChk;

	private ChannelView viewR;

	private ChannelView viewG;

	private ChannelView viewB;

	private ChannelView viewA;

	private string unpackPath;

	private ThemedProgress unpackProgress;

	private TitledCard utilCard;

	private PreviewBox utilPreview;

	private FlatButton utilLoadBtn;

	private FlatButton utilApplyBtn;

	private FlatButton utilSaveBtn;

	private ThemedComboBox utilOpCombo;

	private ThemedComboBox utilFormatCombo;

	private Bitmap utilImage;

	private Bitmap utilOriginal;

	private string utilPath;

	private Panel pageRenamer;

	private TitledCard renameRulesCard;

	private TitledCard renamePreviewCard;

	private LabeledField dirField;

	private LabeledField searchField;

	private LabeledField replaceField;

	private LabeledField prefixField;

	private LabeledField suffixField;

	private LabeledField digitsField;

	private FlatButton browseBtn;

	private FlatButton renameBtn;

	private ThemedCheckBox recurseChk;

	private ThemedCheckBox keepExtChk;

	private RenamePreviewList renamePreview;

	private Label renameStatus;

	private Panel pageInjector;

	private TitledCard injectorMapsCard;

	private TitledCard injectorMaterialCard;

	private Dictionary<MapType, MapSlotRow> slotRows;

	private ThemedComboBox shaderCombo;

	private LabeledField materialNameField;

	private FlatButton injectBtn;

	private FlatButton injectorClearBtn;

	private Label injectorStatus;

	private ThemedCheckBox showOptionalChk;

	private List<RenameEngine.Item> currentPlan;

	// Shared layout metrics (96 DPI values, scaled).
	private static int Gap => Ui.S(16);

	private static int SideWidth => Ui.S(320);

	private static int Pad => TitledCard.Pad;

	public MainForm()
	{
		Text = "GL Map Packer";
		Font = Ui.Body;
		MinimumSize = Ui.S(960, 740);
		Rectangle work = Screen.PrimaryScreen.WorkingArea;
		base.Size = new Size(Math.Min(Ui.S(1180), work.Width), Math.Min(Ui.S(820), work.Height));
		base.StartPosition = FormStartPosition.CenterScreen;
		DoubleBuffered = true;
		try
		{
			Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
		}
		catch (Exception)
		{
		}
		BuildHeader();
		BuildPages();
		statusBar = new StatusBar
		{
			Dock = DockStyle.Bottom,
			RightText = "GL Map Packer " + AppVersion + "  •  Float32 processing  •  16-bit PNG"
		};
		toast = new Toast
		{
			BottomOffset = statusBar.Height
		};
		base.Controls.Add(toast);
		base.Controls.Add(pageHost);
		base.Controls.Add(header);
		base.Controls.Add(statusBar);
		base.Resize += delegate
		{
			toast.Reposition();
		};
		Notifier.Posted += OnNotice;
		Theme.ThemeChanged += delegate
		{
			ApplyTheme();
		};
		ApplyTheme();
		ShowPage(0);
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		Ui.ApplyWindowFrame(this, Theme.Current);
	}

	protected override void OnFormClosed(FormClosedEventArgs e)
	{
		Notifier.Posted -= OnNotice;
		base.OnFormClosed(e);
	}

	private void OnNotice(NoticeKind kind, string message)
	{
		statusBar.SetMessage(kind, message);
		toast.Show(kind, message);
	}

	// ---------------------------------------------------------------- header

	private void BuildHeader()
	{
		header = new Panel
		{
			Dock = DockStyle.Top,
			Height = Ui.S(64)
		};
		header.Paint += PaintHeader;
		toggle = new ThemeToggle();
		toggle.Toggled += delegate
		{
			ApplyTheme();
		};
		tabs = new TabStrip();
		tabs.PillWidth = Ui.S(80);
		tabs.Tabs = new string[8] { "Pack", "Unpack", "Convert", "Atlas", "Validator", "Utility", "Renamer", "Injector" };
		tabs.SelectedIndexChanged += delegate
		{
			ShowPage(tabs.SelectedIndex);
		};
		header.Controls.Add(toggle);
		header.Controls.Add(tabs);
		header.Resize += delegate
		{
			LayoutHeader();
		};
	}

	private static int BrandWidth => Ui.S(214);

	private void LayoutHeader()
	{
		tabs.Location = new Point(BrandWidth, (header.Height - tabs.Height) / 2);
		toggle.Location = new Point(header.Width - Ui.S(24) - toggle.Width, (header.Height - toggle.Height) / 2);
	}

	// App mark (a 2x2 grid of channel colours) and name, plus the header's bottom border.
	private void PaintHeader(object sender, PaintEventArgs e)
	{
		Palette p = Theme.Current;
		Graphics g = e.Graphics;
		g.SmoothingMode = SmoothingMode.AntiAlias;
		DrawAppMark(g, new Rectangle(Ui.S(24), (header.Height - Ui.S(30)) / 2, Ui.S(30), Ui.S(30)));
		TextRenderer.DrawText(g, "GL Map Packer", Ui.Font(12f, semibold: true), new Rectangle(Ui.S(64), 0, BrandWidth - Ui.S(64), header.Height), p.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
		using Pen pen = new Pen(p.Border);
		g.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
	}

	public static void DrawAppMark(Graphics g, Rectangle r)
	{
		int gap = Math.Max(1, r.Width / 12);
		int cell = (r.Width - gap) / 2;
		Color[] colors = new Color[4]
		{
			Color.FromArgb(232, 84, 80),
			Color.FromArgb(108, 196, 92),
			Color.FromArgb(66, 133, 255),
			Color.FromArgb(160, 168, 180)
		};
		for (int i = 0; i < 4; i++)
		{
			Rectangle c = new Rectangle(r.X + i % 2 * (cell + gap), r.Y + i / 2 * (cell + gap), cell, cell);
			GfxUtil.FillRounded(g, c, Math.Max(1, cell / 4), colors[i]);
		}
	}

	// ---------------------------------------------------------------- pages

	private void BuildPages()
	{
		pageHost = new Panel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(Ui.S(24), Ui.S(20), Ui.S(24), Ui.S(20))
		};
		BuildPackPage();
		BuildUnpackPage();
		BuildConvertPage();
		BuildAtlasPage();
		BuildValidatorPage();
		BuildUtilityPage();
		BuildRenamerPage();
		BuildInjectorPage();
		pageHost.Controls.Add(pagePack);
		pageHost.Controls.Add(pageUnpack);
		pageHost.Controls.Add(pageConvert);
		pageHost.Controls.Add(pageAtlas);
		pageHost.Controls.Add(pageValidator);
		pageHost.Controls.Add(pageUtility);
		pageHost.Controls.Add(pageRenamer);
		pageHost.Controls.Add(pageInjector);
	}

	private void ShowPage(int index)
	{
		pagePack.Visible = index == 0;
		pageUnpack.Visible = index == 1;
		pageConvert.Visible = index == 2;
		pageAtlas.Visible = index == 3;
		pageValidator.Visible = index == 4;
		pageUtility.Visible = index == 5;
		pageRenamer.Visible = index == 6;
		pageInjector.Visible = index == 7;
		if (tabs.SelectedIndex != index)
		{
			tabs.SelectedIndex = index;
		}
	}

	private static TitledCard Card(string title)
	{
		return new TitledCard
		{
			Title = title
		};
	}

	// Small muted caption above a control; names starting with "cap" are styled muted.
	private static Label Caption(string text)
	{
		return new Label
		{
			Name = "cap" + text,
			AutoSize = true,
			Text = text,
			Font = Ui.Small
		};
	}

	// Places a caption and its control at (x, y) and returns the y below them.
	private static int Stack(Label caption, Control control, int x, int y, int width)
	{
		caption.Location = new Point(x, y);
		control.Location = new Point(x, y + Ui.S(20));
		control.Width = width;
		return control.Bottom + Ui.S(14);
	}

	private static object[] OutputFormats => new object[5] { "PNG", "TGA", "TIFF", "BMP", "JPEG" };

	private const string ImageFilter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.tga|All files|*.*";

	// ---------------------------------------------------------------- Pack

	private Label capPreset;

	private Label capFormat;

	private void BuildPackPage()
	{
		pagePack = new Panel
		{
			Dock = DockStyle.Fill
		};
		slotR = new ChannelSlot("Channel R", Color.FromArgb(232, 84, 80));
		slotG = new ChannelSlot("Channel G", Color.FromArgb(108, 196, 92));
		slotB = new ChannelSlot("Channel B", Color.FromArgb(66, 133, 255));
		slotA = new ChannelSlot("Channel A", Color.FromArgb(160, 168, 180));
		ChannelSlot[] array = new ChannelSlot[4] { slotR, slotG, slotB, slotA };
		foreach (ChannelSlot channelSlot in array)
		{
			channelSlot.Changed += delegate
			{
				packedImage = null;
				packResult.Image = null;
				packSaveBtn.Enabled = false;
			};
			pagePack.Controls.Add(channelSlot);
		}
		packResult = new PreviewBox
		{
			EmptyText = "Packed result appears here",
			EmptyHint = "Load channel images above, then press Pack now"
		};
		pagePack.Controls.Add(packResult);
		packCard = Card("Output");
		capPreset = Caption("Preset");
		presetCombo = new ThemedComboBox();
		presetCombo.Items.AddRange(new object[4] { "Custom", "Unreal ORM (R=AO, G=Rough, B=Metal)", "Unity HDRP mask (R=Metal, G=AO, B=Detail, A=Smooth)", "glTF metallic-roughness (G=Rough, B=Metal)" });
		presetCombo.SelectedIndex = 0;
		capFormat = Caption("Format");
		formatCombo = new ThemedComboBox();
		formatCombo.Items.AddRange(OutputFormats);
		formatCombo.SelectedIndex = 0;
		packProgress = new ThemedProgress();
		packBtn = new FlatButton
		{
			Text = "Pack now",
			Role = FlatButton.ButtonRole.Secondary
		};
		packBtn.Click += async delegate
		{
			await DoPack();
		};
		packSaveBtn = new FlatButton
		{
			Text = "Save packed map",
			Role = FlatButton.ButtonRole.Accent,
			Enabled = false
		};
		packSaveBtn.Click += delegate
		{
			SavePacked();
		};
		packCard.Controls.Add(capPreset);
		packCard.Controls.Add(presetCombo);
		packCard.Controls.Add(capFormat);
		packCard.Controls.Add(formatCombo);
		packCard.Controls.Add(packProgress);
		packCard.Controls.Add(packBtn);
		packCard.Controls.Add(packSaveBtn);
		pagePack.Controls.Add(packCard);
		pagePack.Resize += delegate
		{
			LayoutPackPage();
		};
	}

	private void LayoutPackPage()
	{
		int width = pagePack.Width;
		int height = pagePack.Height;
		int gap = Gap;
		int slotW = (width - gap * 3) / 4;
		int slotH = Math.Max(Ui.S(180), Math.Min(Ui.S(260), (int)(height * 0.42)));
		ChannelSlot[] array = new ChannelSlot[4] { slotR, slotG, slotB, slotA };
		for (int i = 0; i < 4; i++)
		{
			array[i].Location = new Point(i * (slotW + gap), 0);
			array[i].Size = new Size(slotW, slotH);
		}
		int top = slotH + gap;
		int side = SideWidth;
		packCard.Location = new Point(width - side, top);
		packCard.Size = new Size(side, height - top);
		packResult.Location = new Point(0, top);
		packResult.Size = new Size(width - side - gap, height - top);
		int inner = side - Pad * 2;
		int y = Stack(capPreset, presetCombo, Pad, packCard.ContentTop, inner);
		Stack(capFormat, formatCombo, Pad, y, inner);
		int by = packCard.Height - Pad - packSaveBtn.Height;
		packSaveBtn.Location = new Point(Pad, by);
		packSaveBtn.Width = inner;
		packBtn.Location = new Point(Pad, by - Ui.S(8) - packBtn.Height);
		packBtn.Width = inner;
		packProgress.Location = new Point(Pad, packBtn.Top - Ui.S(18));
		packProgress.Width = inner;
	}

	private async Task DoPack()
	{
		if (slotR.SourceImage == null && slotG.SourceImage == null && slotB.SourceImage == null && slotA.SourceImage == null)
		{
			Notifier.Warning("Load at least one channel image first.");
			return;
		}
		packBtn.Enabled = false;
		Progress<int> progress = new Progress<int>(delegate(int v)
		{
			packProgress.Value = v;
		});
		try
		{
			Bitmap result = await Task.Run(() => ImageCore.Pack(slotR.SourceImage, slotR.Pick, slotG.SourceImage, slotG.Pick, slotB.SourceImage, slotB.Pick, slotA.SourceImage, slotA.Pick, progress));
			packedImage = result;
			packResult.Image = result;
			packSaveBtn.Enabled = result != null;
			if (result != null)
			{
				Notifier.Success("Packed a " + result.Width + "×" + result.Height + " map.");
			}
		}
		catch (Exception ex)
		{
			Notifier.Error("Packing failed: " + ex.Message);
		}
		finally
		{
			packBtn.Enabled = true;
			packProgress.Value = 0;
		}
	}

	private void SavePacked()
	{
		if (packedImage == null)
		{
			return;
		}
		ImageCore.OutputFormat outputFormat = SelectedFormat(formatCombo);
		using SaveFileDialog saveFileDialog = new SaveFileDialog();
		saveFileDialog.Filter = FilterFor(outputFormat);
		saveFileDialog.FileName = "packed" + ImageCore.ExtensionFor(outputFormat);
		if (saveFileDialog.ShowDialog() == DialogResult.OK)
		{
			try
			{
				ImageCore.Save(packedImage, saveFileDialog.FileName, ImageCore.FormatFromExtension(saveFileDialog.FileName, outputFormat));
				Notifier.Success("Saved " + Path.GetFileName(saveFileDialog.FileName));
			}
			catch (Exception ex)
			{
				Notifier.Error("Save failed: " + ex.Message);
			}
		}
	}

	// ---------------------------------------------------------------- Unpack

	private void BuildUnpackPage()
	{
		pageUnpack = new Panel
		{
			Dock = DockStyle.Fill
		};
		unpackCard = Card("Source texture");
		unpackSource = new PreviewBox
		{
			EmptyText = "Drop a packed texture",
			EmptyHint = "or use Load texture",
			AcceptsFiles = true
		};
		unpackSource.FileDropped += LoadUnpackFile;
		unpackLoadBtn = new FlatButton
		{
			Text = "Load texture",
			Role = FlatButton.ButtonRole.Secondary
		};
		unpackLoadBtn.Click += delegate
		{
			LoadUnpack();
		};
		sameFolderChk = new ThemedCheckBox
		{
			Text = "Save next to the source (no dialog)"
		};
		unpackSaveAllBtn = new FlatButton
		{
			Text = "Save all channels",
			Role = FlatButton.ButtonRole.Accent
		};
		unpackSaveAllBtn.Click += delegate
		{
			SaveAllChannels();
		};
		unpackProgress = new ThemedProgress();
		unpackCard.Controls.Add(unpackSource);
		unpackCard.Controls.Add(unpackLoadBtn);
		unpackCard.Controls.Add(unpackProgress);
		unpackCard.Controls.Add(sameFolderChk);
		unpackCard.Controls.Add(unpackSaveAllBtn);
		pageUnpack.Controls.Add(unpackCard);
		viewR = new ChannelView("Channel R", Color.FromArgb(232, 84, 80), "_R");
		viewG = new ChannelView("Channel G", Color.FromArgb(108, 196, 92), "_G");
		viewB = new ChannelView("Channel B", Color.FromArgb(66, 133, 255), "_B");
		viewA = new ChannelView("Channel A", Color.FromArgb(160, 168, 180), "_A");
		ChannelView[] array = new ChannelView[4] { viewR, viewG, viewB, viewA };
		foreach (ChannelView value in array)
		{
			pageUnpack.Controls.Add(value);
		}
		pageUnpack.Resize += delegate
		{
			LayoutUnpackPage();
		};
	}

	private void LayoutUnpackPage()
	{
		int width = pageUnpack.Width;
		int height = pageUnpack.Height;
		int side = SideWidth;
		int inner = side - Pad * 2;
		unpackCard.Location = new Point(0, 0);
		unpackCard.Size = new Size(side, height);
		int by = unpackCard.Height - Pad - unpackSaveAllBtn.Height;
		unpackSaveAllBtn.Location = new Point(Pad, by);
		unpackSaveAllBtn.Width = inner;
		sameFolderChk.Location = new Point(Pad, by - Ui.S(10) - sameFolderChk.Height);
		sameFolderChk.Width = inner;
		unpackLoadBtn.Location = new Point(Pad, sameFolderChk.Top - Ui.S(18) - unpackLoadBtn.Height);
		unpackLoadBtn.Width = inner;
		unpackProgress.Location = new Point(Pad, unpackLoadBtn.Top - Ui.S(16));
		unpackProgress.Width = inner;
		int top = unpackCard.ContentTop;
		unpackSource.Location = new Point(Pad, top);
		unpackSource.Size = new Size(inner, Math.Max(Ui.S(80), unpackProgress.Top - Ui.S(12) - top));
		int gx = side + Gap;
		int gw = (width - gx - Gap) / 2;
		int gh = (height - Gap) / 2;
		ChannelView[] array = new ChannelView[4] { viewR, viewG, viewB, viewA };
		for (int i = 0; i < 4; i++)
		{
			array[i].Location = new Point(gx + i % 2 * (gw + Gap), i / 2 * (gh + Gap));
			array[i].Size = new Size(gw, gh);
		}
	}

	private void LoadUnpack()
	{
		using OpenFileDialog openFileDialog = new OpenFileDialog();
		openFileDialog.Filter = ImageFilter;
		if (openFileDialog.ShowDialog() == DialogResult.OK)
		{
			LoadUnpackFile(openFileDialog.FileName);
		}
	}

	private async void LoadUnpackFile(string path)
	{
		try
		{
			Bitmap src = ImageCore.Load(path);
			unpackPath = path;
			unpackSource.Image = src;
			unpackSource.InfoText = src.Width + "×" + src.Height;
			Progress<int> progress = new Progress<int>(delegate(int v)
			{
				unpackProgress.Value = v;
			});
			Bitmap[] outs = await Task.Run(delegate
			{
				ImageCore.Split(src, forceOpaqueAlpha: true, out var outR, out var outG, out var outB, out var outA, progress);
				return new Bitmap[4] { outR, outG, outB, outA };
			});
			viewR.SetImage(outs[0], path);
			viewG.SetImage(outs[1], path);
			viewB.SetImage(outs[2], path);
			viewA.SetImage(outs[3], path);
			Notifier.Info("Split " + Path.GetFileName(path) + " into 4 channels.");
		}
		catch (Exception ex)
		{
			Notifier.Error("Couldn't load or split: " + ex.Message);
		}
		finally
		{
			unpackProgress.Value = 0;
		}
	}

	private void SaveAllChannels()
	{
		if (!viewR.HasImage)
		{
			Notifier.Warning("Load a texture to split first.");
			return;
		}
		bool autoSameFolder = sameFolderChk.Checked;
		ChannelView[] array = new ChannelView[4] { viewR, viewG, viewB, viewA };
		foreach (ChannelView channelView in array)
		{
			channelView.Save(autoSameFolder);
		}
		if (autoSameFolder && unpackPath != null)
		{
			Notifier.Success("Saved 4 channels next to " + Path.GetFileName(unpackPath));
		}
	}

	// ---------------------------------------------------------------- Utility

	private Label capOperation;

	private Label capUtilFormat;

	private Label utilHint;

	private void BuildUtilityPage()
	{
		pageUtility = new Panel
		{
			Dock = DockStyle.Fill
		};
		utilPreview = new PreviewBox
		{
			EmptyText = "Drop a texture to process",
			EmptyHint = "After Apply, drag the divider to compare",
			AcceptsFiles = true,
			CompareLabel = "Original",
			ImageLabel = "Result"
		};
		utilPreview.FileDropped += LoadUtilFile;
		utilCard = Card("Operation");
		capOperation = Caption("Operation");
		utilOpCombo = new ThemedComboBox();
		utilOpCombo.Items.AddRange(new object[4] { "Flip Y (normal map: OpenGL <-> DirectX)", "Flip vertical (whole image)", "Flip horizontal (whole image)", "Invert RGB" });
		utilOpCombo.SelectedIndex = 0;
		capUtilFormat = Caption("Format");
		utilFormatCombo = new ThemedComboBox();
		utilFormatCombo.Items.AddRange(OutputFormats);
		utilFormatCombo.SelectedIndex = 0;
		utilLoadBtn = new FlatButton
		{
			Text = "Load texture",
			Role = FlatButton.ButtonRole.Secondary
		};
		utilLoadBtn.Click += delegate
		{
			LoadUtil();
		};
		utilApplyBtn = new FlatButton
		{
			Text = "Apply",
			Role = FlatButton.ButtonRole.Secondary,
			Enabled = false
		};
		utilApplyBtn.Click += delegate
		{
			ApplyUtil();
		};
		utilSaveBtn = new FlatButton
		{
			Text = "Save result",
			Role = FlatButton.ButtonRole.Accent,
			Enabled = false
		};
		utilSaveBtn.Click += delegate
		{
			SaveUtil();
		};
		utilHint = new Label
		{
			Name = "capHint",
			AutoSize = false,
			Text = "Operations stack: Apply runs on the current result. Load the file again to start over.",
			Font = Ui.Small
		};
		utilCard.Controls.Add(capOperation);
		utilCard.Controls.Add(utilOpCombo);
		utilCard.Controls.Add(capUtilFormat);
		utilCard.Controls.Add(utilFormatCombo);
		utilCard.Controls.Add(utilHint);
		utilCard.Controls.Add(utilLoadBtn);
		utilCard.Controls.Add(utilApplyBtn);
		utilCard.Controls.Add(utilSaveBtn);
		pageUtility.Controls.Add(utilCard);
		pageUtility.Controls.Add(utilPreview);
		pageUtility.Resize += delegate
		{
			LayoutUtilityPage();
		};
	}

	private void LayoutUtilityPage()
	{
		int width = pageUtility.Width;
		int height = pageUtility.Height;
		int side = SideWidth;
		int inner = side - Pad * 2;
		utilCard.Location = new Point(0, 0);
		utilCard.Size = new Size(side, height);
		int y = Stack(capOperation, utilOpCombo, Pad, utilCard.ContentTop, inner);
		y = Stack(capUtilFormat, utilFormatCombo, Pad, y, inner);
		utilHint.Location = new Point(Pad, y);
		utilHint.Size = new Size(inner, Ui.S(40));
		int by = utilCard.Height - Pad - utilSaveBtn.Height;
		utilSaveBtn.Location = new Point(Pad, by);
		utilSaveBtn.Width = inner;
		int half = (inner - Ui.S(8)) / 2;
		utilLoadBtn.Location = new Point(Pad, by - Ui.S(8) - utilLoadBtn.Height);
		utilLoadBtn.Width = half;
		utilApplyBtn.Location = new Point(Pad + half + Ui.S(8), utilLoadBtn.Top);
		utilApplyBtn.Width = inner - half - Ui.S(8);
		utilPreview.Location = new Point(side + Gap, 0);
		utilPreview.Size = new Size(width - side - Gap, height);
	}

	private void LoadUtil()
	{
		using OpenFileDialog openFileDialog = new OpenFileDialog();
		openFileDialog.Filter = ImageFilter;
		if (openFileDialog.ShowDialog() == DialogResult.OK)
		{
			LoadUtilFile(openFileDialog.FileName);
		}
	}

	private void LoadUtilFile(string path)
	{
		try
		{
			Bitmap loaded = ImageCore.Load(path);
			utilPreview.CompareImage = null;
			utilImage?.Dispose();
			utilOriginal?.Dispose();
			utilImage = loaded;
			utilOriginal = ImageCore.CloneArgb32(loaded);
			utilPath = path;
			utilPreview.Image = utilImage;
			utilPreview.InfoText = loaded.Width + "×" + loaded.Height;
			utilApplyBtn.Enabled = true;
			utilSaveBtn.Enabled = true;
		}
		catch (Exception ex)
		{
			Notifier.Error("Couldn't load: " + ex.Message);
		}
	}

	private void ApplyUtil()
	{
		if (utilImage != null)
		{
			Bitmap bitmap = utilOpCombo.SelectedIndex switch
			{
				1 => ImageCore.FlipVertical(utilImage),
				2 => ImageCore.FlipHorizontal(utilImage),
				3 => ImageCore.InvertRgb(utilImage),
				_ => ImageCore.FlipGreen(utilImage),
			};
			Bitmap old = utilImage;
			utilImage = bitmap;
			utilPreview.Image = utilImage;
			utilPreview.CompareImage = utilOriginal;
			old.Dispose();
			Notifier.Info("Applied: " + utilOpCombo.Text);
		}
	}

	private void SaveUtil()
	{
		if (utilImage == null)
		{
			return;
		}
		ImageCore.OutputFormat outputFormat = SelectedFormat(utilFormatCombo);
		using SaveFileDialog saveFileDialog = new SaveFileDialog();
		saveFileDialog.Filter = FilterFor(outputFormat);
		string text = ((utilPath == null) ? "texture" : Path.GetFileNameWithoutExtension(utilPath));
		saveFileDialog.FileName = text + "_util" + ImageCore.ExtensionFor(outputFormat);
		if (utilPath != null)
		{
			saveFileDialog.InitialDirectory = Path.GetDirectoryName(utilPath);
		}
		if (saveFileDialog.ShowDialog() == DialogResult.OK)
		{
			try
			{
				ImageCore.Save(utilImage, saveFileDialog.FileName, ImageCore.FormatFromExtension(saveFileDialog.FileName, outputFormat));
				Notifier.Success("Saved " + Path.GetFileName(saveFileDialog.FileName));
			}
			catch (Exception ex)
			{
				Notifier.Error("Save failed: " + ex.Message);
			}
		}
	}

	// ---------------------------------------------------------------- Renamer

	private void BuildRenamerPage()
	{
		pageRenamer = new Panel
		{
			Dock = DockStyle.Fill
		};
		renameRulesCard = Card("Rename rules");
		renamePreviewCard = Card("Preview");
		dirField = new LabeledField("Folder");
		dirField.Box.ReadOnly = true;
		browseBtn = new FlatButton
		{
			Text = "Browse",
			Role = FlatButton.ButtonRole.Secondary
		};
		browseBtn.Click += delegate
		{
			BrowseFolder();
		};
		searchField = new LabeledField("Search");
		replaceField = new LabeledField("Replace with");
		prefixField = new LabeledField("Prefix");
		suffixField = new LabeledField("Suffix");
		digitsField = new LabeledField("Increment index (digits, 0 = off)");
		digitsField.Value = "0";
		recurseChk = new ThemedCheckBox
		{
			Text = "Include subfolders"
		};
		keepExtChk = new ThemedCheckBox
		{
			Text = "Keep file extension",
			Checked = true
		};
		LabeledField[] array = new LabeledField[5] { searchField, replaceField, prefixField, suffixField, digitsField };
		foreach (LabeledField labeledField in array)
		{
			labeledField.ValueChanged += delegate
			{
				RefreshRenamePreview();
			};
		}
		recurseChk.CheckedChanged += delegate
		{
			RefreshRenamePreview();
		};
		keepExtChk.CheckedChanged += delegate
		{
			RefreshRenamePreview();
		};
		renamePreview = new RenamePreviewList();
		renameStatus = new Label
		{
			AutoSize = false,
			Text = "",
			Font = Ui.Small,
			AutoEllipsis = true,
			TextAlign = ContentAlignment.MiddleLeft
		};
		renameBtn = new FlatButton
		{
			Text = "Rename files",
			Role = FlatButton.ButtonRole.Accent
		};
		renameBtn.Click += delegate
		{
			DoRename();
		};
		renameRulesCard.Controls.Add(dirField);
		renameRulesCard.Controls.Add(browseBtn);
		foreach (LabeledField field in array)
		{
			renameRulesCard.Controls.Add(field);
		}
		renameRulesCard.Controls.Add(recurseChk);
		renameRulesCard.Controls.Add(keepExtChk);
		renamePreviewCard.Controls.Add(renamePreview);
		renamePreviewCard.Controls.Add(renameStatus);
		renamePreviewCard.Controls.Add(renameBtn);
		pageRenamer.Controls.Add(renameRulesCard);
		pageRenamer.Controls.Add(renamePreviewCard);
		pageRenamer.Resize += delegate
		{
			LayoutRenamerPage();
		};
	}

	private void LayoutRenamerPage()
	{
		int width = pageRenamer.Width;
		int height = pageRenamer.Height;
		int side = Ui.S(360);
		int inner = side - Pad * 2;
		renameRulesCard.Location = new Point(0, 0);
		renameRulesCard.Size = new Size(side, height);
		int y = renameRulesCard.ContentTop;
		int browseW = Ui.S(96);
		dirField.Location = new Point(Pad, y);
		dirField.Width = inner - browseW - Ui.S(8);
		browseBtn.Size = new Size(browseW, Ui.S(34));
		browseBtn.Location = new Point(Pad + inner - browseW, y + Ui.S(20));
		y += dirField.Height + Ui.S(4);
		LabeledField[] array = new LabeledField[5] { searchField, replaceField, prefixField, suffixField, digitsField };
		foreach (LabeledField labeledField in array)
		{
			labeledField.Location = new Point(Pad, y);
			labeledField.Width = inner;
			y += labeledField.Height + Ui.S(4);
		}
		recurseChk.Location = new Point(Pad, y + Ui.S(4));
		recurseChk.Width = inner;
		keepExtChk.Location = new Point(Pad, recurseChk.Bottom + Ui.S(4));
		keepExtChk.Width = inner;
		int px = side + Gap;
		renamePreviewCard.Location = new Point(px, 0);
		renamePreviewCard.Size = new Size(width - px, height);
		int pin = renamePreviewCard.Width - Pad * 2;
		int by = height - Pad - renameBtn.Height;
		renameBtn.Width = Ui.S(180);
		renameBtn.Location = new Point(renamePreviewCard.Width - Pad - renameBtn.Width, by);
		renameStatus.Location = new Point(Pad, by);
		renameStatus.Size = new Size(pin - renameBtn.Width - Ui.S(12), renameBtn.Height);
		renamePreview.Location = new Point(Pad, renamePreviewCard.ContentTop);
		renamePreview.Size = new Size(pin, by - Ui.S(12) - renamePreviewCard.ContentTop);
	}

	private void BrowseFolder()
	{
		using FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
		if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
		{
			dirField.Value = folderBrowserDialog.SelectedPath;
			RefreshRenamePreview();
		}
	}

	private RenameEngine.Rule CurrentRule()
	{
		int.TryParse(digitsField.Value.Trim(), out var result);
		if (result < 0)
		{
			result = 0;
		}
		RenameEngine.Rule rule = new RenameEngine.Rule();
		rule.Search = searchField.Value;
		rule.Replace = replaceField.Value;
		rule.Prefix = prefixField.Value;
		rule.Suffix = suffixField.Value;
		rule.Digits = result;
		rule.KeepExtension = keepExtChk.Checked;
		rule.IncludeSubfolders = recurseChk.Checked;
		rule.SortByName = true;
		return rule;
	}

	private void RefreshRenamePreview()
	{
		string value = dirField.Value;
		if (string.IsNullOrEmpty(value) || !Directory.Exists(value))
		{
			renamePreview.SetRows(null);
			renameStatus.Text = "";
			currentPlan = null;
			return;
		}
		currentPlan = RenameEngine.Plan(value, CurrentRule());
		List<RenamePreviewList.Row> list = new List<RenamePreviewList.Row>();
		int num = 0;
		int num2 = 0;
		foreach (RenameEngine.Item item in currentPlan)
		{
			if (item.Changed)
			{
				num++;
			}
			if (!string.IsNullOrEmpty(item.Warning))
			{
				num2++;
			}
			list.Add(new RenamePreviewList.Row
			{
				Left = item.OldName,
				Right = ((!string.IsNullOrEmpty(item.Warning)) ? (item.NewName + "  (" + item.Warning + ")") : item.NewName),
				Warn = !string.IsNullOrEmpty(item.Warning),
				Changed = item.Changed
			});
		}
		renamePreview.SetRows(list);
		renameStatus.Text = currentPlan.Count + " files • " + num + " will change" + ((num2 <= 0) ? "" : (" • " + num2 + " warning" + ((num2 != 1) ? "s" : "")));
		renameStatus.ForeColor = ((num2 <= 0) ? Theme.Current.TextMuted : Theme.Current.Danger);
	}

	private void DoRename()
	{
		if (currentPlan == null || currentPlan.Count == 0)
		{
			Notifier.Warning("Pick a folder first.");
			return;
		}
		int count = currentPlan.FindAll((RenameEngine.Item i) => i.Changed).Count;
		if (count == 0)
		{
			Notifier.Info("These settings don't change any file names.");
			return;
		}
		int count2 = currentPlan.FindAll((RenameEngine.Item i) => !string.IsNullOrEmpty(i.Warning)).Count;
		string text = "Rename " + count + " file(s)?";
		if (count2 > 0)
		{
			text = text + "\n\n" + count2 + " have warnings (name collisions) and will be skipped.";
		}
		if (MessageBox.Show(text, "Confirm rename", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
		{
			List<string> errors;
			int num = RenameEngine.Apply(currentPlan, force: false, out errors);
			RefreshRenamePreview();
			if (errors.Count > 0)
			{
				Notifier.Warning(num + " file(s) renamed, " + errors.Count + " failed: " + string.Join("; ", errors.ToArray()));
			}
			else
			{
				Notifier.Success(num + " file(s) renamed.");
			}
		}
	}

	// ---------------------------------------------------------------- Injector

	private Label capShader;

	private void BuildInjectorPage()
	{
		pageInjector = new Panel
		{
			Dock = DockStyle.Fill
		};
		injectorMapsCard = Card("Texture maps");
		injectorMaterialCard = Card("Material");
		slotRows = new Dictionary<MapType, MapSlotRow>();
		MapType[] array = (MapType[])Enum.GetValues(typeof(MapType));
		foreach (MapType mapType in array)
		{
			MapSlotRow mapSlotRow = new MapSlotRow(mapType);
			mapSlotRow.Changed += delegate
			{
				OnInjectorChanged();
			};
			slotRows[mapType] = mapSlotRow;
			injectorMapsCard.Controls.Add(mapSlotRow);
		}
		showOptionalChk = new ThemedCheckBox
		{
			Text = "Show optional maps (AO, Opacity, Emission)",
			Checked = false
		};
		showOptionalChk.CheckedChanged += delegate
		{
			LayoutInjectorPage();
		};
		injectorMapsCard.Controls.Add(showOptionalChk);
		capShader = Caption("Shader type");
		shaderCombo = new ThemedComboBox();
		shaderCombo.Items.AddRange(new object[2] { "OpenPBR", "USD" });
		shaderCombo.SelectedIndex = 0;
		materialNameField = new LabeledField("Material name (derived)");
		materialNameField.Box.ReadOnly = true;
		materialNameField.Value = "M_material";
		injectorClearBtn = new FlatButton
		{
			Text = "Clear",
			Role = FlatButton.ButtonRole.Secondary
		};
		injectorClearBtn.Click += delegate
		{
			ClearInjector();
		};
		injectBtn = new FlatButton
		{
			Text = "Export preset",
			Role = FlatButton.ButtonRole.Accent
		};
		injectBtn.Click += delegate
		{
			ExportPreset();
		};
		injectorStatus = new Label
		{
			AutoSize = false,
			Text = "Writes %TEMP%/maya_bridge/material_preset.json for the Maya importer.",
			Font = Ui.Small
		};
		injectorMaterialCard.Controls.Add(capShader);
		injectorMaterialCard.Controls.Add(shaderCombo);
		injectorMaterialCard.Controls.Add(materialNameField);
		injectorMaterialCard.Controls.Add(injectorStatus);
		injectorMaterialCard.Controls.Add(injectorClearBtn);
		injectorMaterialCard.Controls.Add(injectBtn);
		pageInjector.Controls.Add(injectorMapsCard);
		pageInjector.Controls.Add(injectorMaterialCard);
		pageInjector.Resize += delegate
		{
			LayoutInjectorPage();
		};
	}

	private void LayoutInjectorPage()
	{
		if (slotRows == null)
		{
			return;
		}
		int width = pageInjector.Width;
		int height = pageInjector.Height;
		int side = SideWidth;
		int mapsW = Math.Min(width - side - Gap, Ui.S(720));
		injectorMapsCard.Location = new Point(0, 0);
		injectorMapsCard.Size = new Size(mapsW, height);
		int inner = mapsW - Pad * 2;
		int y = injectorMapsCard.ContentTop;
		MapType[] array = (MapType[])Enum.GetValues(typeof(MapType));
		foreach (MapType mapType in array)
		{
			MapSlotRow mapSlotRow = slotRows[mapType];
			if (mapSlotRow.Visible = MaterialPreset.IsCore(mapType) || showOptionalChk.Checked)
			{
				mapSlotRow.Location = new Point(Pad, y);
				mapSlotRow.Size = new Size(inner, Ui.S(46));
				y += Ui.S(50);
			}
		}
		showOptionalChk.Location = new Point(Pad, y + Ui.S(8));
		showOptionalChk.Width = inner;
		int mx = mapsW + Gap;
		int mw = Math.Max(side, width - mx);
		injectorMaterialCard.Location = new Point(mx, 0);
		injectorMaterialCard.Size = new Size(mw, height);
		int min = mw - Pad * 2;
		int my = Stack(capShader, shaderCombo, Pad, injectorMaterialCard.ContentTop, min);
		materialNameField.Location = new Point(Pad, my);
		materialNameField.Width = min;
		injectorStatus.Location = new Point(Pad, materialNameField.Bottom + Ui.S(10));
		injectorStatus.Size = new Size(min, Ui.S(54));
		int by = height - Pad - injectBtn.Height;
		injectBtn.Location = new Point(Pad, by);
		injectBtn.Width = min;
		injectorClearBtn.Location = new Point(Pad, by - Ui.S(8) - injectorClearBtn.Height);
		injectorClearBtn.Width = min;
	}

	private Dictionary<MapType, string> CurrentMaps()
	{
		Dictionary<MapType, string> dictionary = new Dictionary<MapType, string>();
		foreach (KeyValuePair<MapType, MapSlotRow> slotRow in slotRows)
		{
			if (!string.IsNullOrEmpty(slotRow.Value.FilePath))
			{
				dictionary[slotRow.Key] = slotRow.Value.FilePath;
			}
		}
		return dictionary;
	}

	private void OnInjectorChanged()
	{
		materialNameField.Value = MaterialPreset.DeriveMaterialName(CurrentMaps());
	}

	private void ClearInjector()
	{
		foreach (KeyValuePair<MapType, MapSlotRow> slotRow in slotRows)
		{
			slotRow.Value.Clear();
		}
		shaderCombo.SelectedIndex = 0;
		materialNameField.Value = "M_material";
	}

	private void ExportPreset()
	{
		Dictionary<MapType, string> dictionary = CurrentMaps();
		if (dictionary.Count == 0)
		{
			Notifier.Warning("Assign at least one texture map first.");
			return;
		}
		string shaderType = ((shaderCombo.SelectedIndex != 1) ? "openPBRSurface" : "usdPreviewSurface");
		string text = MaterialPreset.DeriveMaterialName(dictionary);
		string message = text + " — exported " + DateTime.Now.ToString("yyyy-MM-dd HH:mm");
		string json = MaterialPreset.BuildJson(message, text, shaderType, dictionary);
		try
		{
			string path = MaterialPreset.WriteAtomic(json);
			injectorStatus.ForeColor = Theme.Current.TextMuted;
			injectorStatus.Text = "Wrote " + MaterialPreset.ForwardSlashes(path);
			Notifier.Success("Exported the " + text + " preset.");
		}
		catch (Exception ex)
		{
			injectorStatus.ForeColor = Theme.Current.Danger;
			injectorStatus.Text = "Export failed: " + ex.Message;
			Notifier.Error("Couldn't write the preset: " + ex.Message);
		}
	}

	// ---------------------------------------------------------------- shared

	private static ImageCore.OutputFormat SelectedFormat(ThemedComboBox combo)
	{
		return combo.SelectedIndex switch
		{
			1 => ImageCore.OutputFormat.Tga,
			2 => ImageCore.OutputFormat.Tiff,
			3 => ImageCore.OutputFormat.Bmp,
			4 => ImageCore.OutputFormat.Jpeg,
			_ => ImageCore.OutputFormat.Png,
		};
	}

	private static string FilterFor(ImageCore.OutputFormat fmt)
	{
		return fmt switch
		{
			ImageCore.OutputFormat.Tga => "Targa|*.tga",
			ImageCore.OutputFormat.Tiff => "TIFF|*.tif",
			ImageCore.OutputFormat.Bmp => "Bitmap|*.bmp",
			ImageCore.OutputFormat.Jpeg => "JPEG|*.jpg",
			_ => "PNG|*.png",
		};
	}

	private void ApplyTheme()
	{
		Palette current = Theme.Current;
		BackColor = current.AppBackground;
		header.BackColor = current.Surface;
		pageHost.BackColor = current.AppBackground;
		Panel[] array = new Panel[8] { pagePack, pageUnpack, pageConvert, pageAtlas, pageValidator, pageUtility, pageRenamer, pageInjector };
		foreach (Panel panel in array)
		{
			panel.BackColor = current.AppBackground;
		}
		StyleLabels(this, current);
		ThemeAll(this, current);
		Ui.ApplyWindowFrame(this, current);
		LayoutHeader();
		LayoutPackPage();
		LayoutUnpackPage();
		LayoutConvertPage();
		LayoutAtlasPage();
		LayoutValidatorPage();
		LayoutUtilityPage();
		LayoutRenamerPage();
		LayoutInjectorPage();
		Invalidate(invalidateChildren: true);
	}

	private void StyleLabels(Control c, Palette p)
	{
		if (c is LabeledField)
		{
			return;
		}
		foreach (Control control in c.Controls)
		{
			if (control is Label label && !(control is LinkLabel))
			{
				bool muted = label.Name.StartsWith("cap") || label == injectorStatus || label == renameStatus || label == cvStatus;
				label.ForeColor = muted ? p.TextMuted : p.Text;
				label.BackColor = Color.Transparent;
			}
			StyleLabels(control, p);
		}
	}

	private void ThemeAll(Control c, Palette p)
	{
		foreach (Control control in c.Controls)
		{
			if (control is IThemeable themeable)
			{
				themeable.ApplyTheme(p);
			}
			ThemeAll(control, p);
		}
	}
}
