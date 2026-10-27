using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace GLMapPacker;

// Small drop target for one file (a 3D model, a mask): dashed when empty, file name and a short
// info line once set. Click to browse, × to clear.
public class FileDropBox : Control, IThemeable
{
	private readonly IconButton clearBtn;

	private bool dragHover;

	private bool hover;

	private string filePath;

	public string Prompt { get; set; }

	// Optional slot name shown in a fixed column on the left (e.g. "Albedo").
	public string Caption { get; set; }

	public int CaptionWidth { get; set; } = Ui.S(74);

	public string Filter { get; set; }

	// Shown after the file name, e.g. "1,204 tris • 8 islands".
	public string Info { get; set; }

	public string FilePath
	{
		get
		{
			return filePath;
		}
		set
		{
			filePath = value;
			if (string.IsNullOrEmpty(value))
			{
				Info = null;
			}
			clearBtn.Visible = !string.IsNullOrEmpty(value);
			Invalidate();
			if (this.FileChanged != null)
			{
				this.FileChanged(this, EventArgs.Empty);
			}
		}
	}

	public event EventHandler FileChanged;

	public FileDropBox()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		BackColor = Color.Transparent;
		Cursor = Cursors.Hand;
		AllowDrop = true;
		Prompt = "Drop a file or click to browse";
		Filter = "All files|*.*";
		clearBtn = new IconButton
		{
			Text = "×",
			Size = Ui.S(26, 26),
			Font = Ui.Font(12f),
			Visible = false
		};
		clearBtn.Click += delegate
		{
			FilePath = null;
		};
		base.Controls.Add(clearBtn);
		base.Height = Ui.S(40);
	}

	protected override void OnLayout(LayoutEventArgs levent)
	{
		if (clearBtn == null)
		{
			base.OnLayout(levent);
			return;
		}
		clearBtn.Location = new Point(base.Width - clearBtn.Width - Ui.S(7), (base.Height - clearBtn.Height) / 2);
		base.OnLayout(levent);
	}

	public void ApplyTheme(Palette p)
	{
		clearBtn.Backdrop = p.SurfaceAlt;
		Invalidate();
	}

	protected override void OnClick(EventArgs e)
	{
		using OpenFileDialog dialog = new OpenFileDialog();
		dialog.Filter = Filter;
		if (dialog.ShowDialog() == DialogResult.OK)
		{
			FilePath = dialog.FileName;
		}
		base.OnClick(e);
	}

	protected override void OnMouseEnter(EventArgs e)
	{
		hover = true;
		Invalidate();
		base.OnMouseEnter(e);
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		hover = false;
		Invalidate();
		base.OnMouseLeave(e);
	}

	protected override void OnDragEnter(DragEventArgs e)
	{
		if (e.Data.GetDataPresent(DataFormats.FileDrop))
		{
			e.Effect = DragDropEffects.Copy;
			dragHover = true;
			Invalidate();
		}
		base.OnDragEnter(e);
	}

	protected override void OnDragLeave(EventArgs e)
	{
		dragHover = false;
		Invalidate();
		base.OnDragLeave(e);
	}

	protected override void OnDragDrop(DragEventArgs e)
	{
		dragHover = false;
		if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
		{
			FilePath = files[0];
		}
		Invalidate();
		base.OnDragDrop(e);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Palette current = Theme.Current;
		Graphics g = e.Graphics;
		g.SmoothingMode = SmoothingMode.AntiAlias;
		g.Clear(GfxUtil.ResolveBack(this));
		Rectangle r = new Rectangle(0, 0, base.Width - 1, base.Height - 1);
		bool set = !string.IsNullOrEmpty(filePath);
		Color fill = dragHover ? Theme.Mix(current.SurfaceAlt, current.Accent, 0.2) : (hover ? Theme.Mix(current.SurfaceAlt, current.Accent, 0.06) : current.SurfaceAlt);
		GfxUtil.FillRounded(g, r, Ui.S(8), fill);
		Color border = dragHover ? current.Accent : (set ? current.Border : Theme.Mix(current.Border, current.TextMuted, 0.35));
		GfxUtil.DrawRounded(g, r, Ui.S(8), border, dragHover ? Ui.S(1.5f) : 1f, !set && !dragHover);
		int pad = Ui.S(12);
		int left = pad;
		if (!string.IsNullOrEmpty(Caption))
		{
			TextRenderer.DrawText(g, Caption, Ui.Label, new Rectangle(pad, 0, CaptionWidth - pad, base.Height), set ? current.Text : current.TextMuted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
			left = CaptionWidth;
		}
		Rectangle text = new Rectangle(left, 0, base.Width - left - pad - (set ? clearBtn.Width + Ui.S(6) : 0), base.Height);
		if (!set)
		{
			TextRenderer.DrawText(g, Prompt, Ui.Small, text, dragHover ? current.Accent : current.TextMuted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
			return;
		}
		string name = Path.GetFileName(filePath);
		Size ns = TextRenderer.MeasureText(g, name, Ui.Body, text.Size, TextFormatFlags.NoPadding);
		TextRenderer.DrawText(g, name, Ui.Body, text, current.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
		if (!string.IsNullOrEmpty(Info))
		{
			// Only when it fits whole on the line; a cut-off size reads worse than none.
			Size infoSize = TextRenderer.MeasureText(g, Info, Ui.Small, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
			if (ns.Width + Ui.S(10) + infoSize.Width <= text.Width)
			{
				Rectangle info = new Rectangle(text.X + ns.Width + Ui.S(10), 0, infoSize.Width + Ui.S(2), base.Height);
				TextRenderer.DrawText(g, Info, Ui.Small, info, current.TextMuted, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
			}
		}
	}
}
