using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace GLMapPacker;

// Image preview. Interactive previews add wheel zoom, drag to pan, double-click to fit, an R/G/B/A channel
// toggle, a size overlay and, when CompareImage is set, a draggable before/after split.
public class PreviewBox : Control, IThemeable
{
	private enum Drag
	{
		None,
		Pan,
		Split
	}

	private sealed class ScaledCache
	{
		public Image Source;

		public int Channel;

		public Size Size;

		public Bitmap Bitmap;

		public void Clear()
		{
			Bitmap?.Dispose();
			Bitmap = null;
			Source = null;
		}
	}

	private static readonly string[] ChannelNames = new string[5] { "RGB", "R", "G", "B", "A" };

	private Image image;

	private Image compareImage;

	private int channel;

	private float zoom = 1f;

	private PointF pan;

	private float split = 0.5f;

	private Drag drag;

	private Point lastMouse;

	private bool dragOver;

	private readonly List<KeyValuePair<Rectangle, int>> toolbarHits = new List<KeyValuePair<Rectangle, int>>();

	private readonly ScaledCache imageCache = new ScaledCache();

	private readonly ScaledCache compareCache = new ScaledCache();

	private TextureBrush checker;

	private Palette checkerPalette;

	public Image Image
	{
		get
		{
			return image;
		}
		set
		{
			if (value == null || image == null || value.Size != image.Size)
			{
				ResetView();
			}
			image = value;
			imageCache.Clear();
			Invalidate();
		}
	}

	// Shown on the left of the split ("Before"); Image is on the right ("After").
	public Image CompareImage
	{
		get
		{
			return compareImage;
		}
		set
		{
			compareImage = value;
			compareCache.Clear();
			Invalidate();
		}
	}

	public string EmptyText { get; set; }

	public string EmptyHint { get; set; }

	// Overlay text such as "2048×2048 • 16-bit"; the zoom level is appended.
	public string InfoText { get; set; }

	// Real image pixels per pixel of the displayed bitmap (for previews of a downscaled copy),
	// so the zoom readout refers to the real image.
	public float PixelRatio { get; set; } = 1f;

	public string CompareLabel { get; set; }

	public string ImageLabel { get; set; }

	public bool Interactive { get; set; }

	public int CornerRadius { get; set; }

	public bool AcceptsFiles
	{
		get
		{
			return AllowDrop;
		}
		set
		{
			AllowDrop = value;
		}
	}

	public event Action<string> FileDropped;

	public PreviewBox()
	{
		CornerRadius = Ui.S(10);
		EmptyText = "drop or load an image";
		CompareLabel = "Before";
		ImageLabel = "After";
		Interactive = true;
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
		BackColor = Color.Transparent;
	}

	public void ApplyTheme(Palette p)
	{
		Invalidate();
	}

	public void ResetView()
	{
		zoom = 1f;
		pan = PointF.Empty;
		Invalidate();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			imageCache.Clear();
			compareCache.Clear();
			checker?.Dispose();
		}
		base.Dispose(disposing);
	}

	// ---------------------------------------------------------------- geometry

	private float FitScale()
	{
		int m = Ui.S(8);
		return Math.Min((float)(base.Width - m * 2) / image.Width, (float)(base.Height - m * 2) / image.Height);
	}

	private float ViewScale => FitScale() * zoom;

	private RectangleF DestRect()
	{
		float s = ViewScale;
		float w = image.Width * s;
		float h = image.Height * s;
		return new RectangleF(base.Width / 2f + pan.X - w / 2f, base.Height / 2f + pan.Y - h / 2f, w, h);
	}

	private void ClampPan()
	{
		RectangleF d = DestRect();
		float mx = Math.Max(0f, (d.Width - base.Width) / 2f) + base.Width * 0.25f;
		float my = Math.Max(0f, (d.Height - base.Height) / 2f) + base.Height * 0.25f;
		if (zoom <= 1f)
		{
			mx = my = 0f;
		}
		pan = new PointF(Math.Max(-mx, Math.Min(mx, pan.X)), Math.Max(-my, Math.Min(my, pan.Y)));
	}

	private int SplitX => (int)(base.Width * split);

	private bool Interactable => Interactive && image != null;

	// ---------------------------------------------------------------- input

	protected override void OnMouseDown(MouseEventArgs e)
	{
		if (Interactable && e.Button == MouseButtons.Left)
		{
			Focus();
			foreach (KeyValuePair<Rectangle, int> hit in toolbarHits)
			{
				if (hit.Key.Contains(e.Location))
				{
					if (hit.Value < 0)
					{
						ResetView();
					}
					else
					{
						channel = hit.Value;
						imageCache.Clear();
						compareCache.Clear();
						Invalidate();
					}
					base.OnMouseDown(e);
					return;
				}
			}
			bool nearSplit = compareImage != null && Math.Abs(e.X - SplitX) <= Ui.S(10);
			if (compareImage != null && (nearSplit || zoom <= 1f))
			{
				drag = Drag.Split;
				SetSplit(e.X);
			}
			else if (zoom > 1f)
			{
				drag = Drag.Pan;
			}
			lastMouse = e.Location;
			base.Capture = drag != Drag.None;
		}
		base.OnMouseDown(e);
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		if (drag == Drag.Split)
		{
			SetSplit(e.X);
		}
		else if (drag == Drag.Pan)
		{
			pan = new PointF(pan.X + e.X - lastMouse.X, pan.Y + e.Y - lastMouse.Y);
			ClampPan();
			Invalidate();
		}
		lastMouse = e.Location;
		if (Interactable)
		{
			bool overToolbar = toolbarHits.Exists(h => h.Key.Contains(e.Location));
			if (overToolbar)
			{
				Cursor = Cursors.Hand;
			}
			else if (compareImage != null && (drag == Drag.Split || Math.Abs(e.X - SplitX) <= Ui.S(10) || zoom <= 1f))
			{
				Cursor = Cursors.SizeWE;
			}
			else
			{
				Cursor = zoom > 1f ? Cursors.SizeAll : Cursors.Default;
			}
		}
		base.OnMouseMove(e);
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		drag = Drag.None;
		base.Capture = false;
		base.OnMouseUp(e);
	}

	protected override void OnMouseWheel(MouseEventArgs e)
	{
		if (Interactable)
		{
			float old = ViewScale;
			zoom = Math.Max(1f, Math.Min(64f, zoom * (e.Delta > 0 ? 1.25f : 0.8f)));
			float now = ViewScale;
			// Keep the image point under the cursor where it is.
			float cx = base.Width / 2f + pan.X;
			float cy = base.Height / 2f + pan.Y;
			float ux = (e.X - cx) / old;
			float uy = (e.Y - cy) / old;
			pan = new PointF(e.X - ux * now - base.Width / 2f, e.Y - uy * now - base.Height / 2f);
			ClampPan();
			Invalidate();
		}
		base.OnMouseWheel(e);
	}

	protected override void OnDoubleClick(EventArgs e)
	{
		if (Interactable)
		{
			ResetView();
		}
		base.OnDoubleClick(e);
	}

	private void SetSplit(int x)
	{
		split = Math.Max(0f, Math.Min(1f, (float)x / Math.Max(1, base.Width)));
		Invalidate();
	}

	protected override void OnDragEnter(DragEventArgs e)
	{
		if (e.Data.GetDataPresent(DataFormats.FileDrop))
		{
			e.Effect = DragDropEffects.Copy;
			dragOver = true;
			Invalidate();
		}
		base.OnDragEnter(e);
	}

	protected override void OnDragLeave(EventArgs e)
	{
		dragOver = false;
		Invalidate();
		base.OnDragLeave(e);
	}

	protected override void OnDragDrop(DragEventArgs e)
	{
		dragOver = false;
		Invalidate();
		if (e.Data.GetData(DataFormats.FileDrop) is string[] array && array.Length > 0)
		{
			FileDropped?.Invoke(array[0]);
		}
		base.OnDragDrop(e);
	}

	// ---------------------------------------------------------------- painting

	protected override void OnPaint(PaintEventArgs e)
	{
		Palette current = Theme.Current;
		Graphics graphics = e.Graphics;
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		graphics.Clear(GfxUtil.ResolveBack(this));
		Rectangle rectangle = new Rectangle(0, 0, base.Width - 1, base.Height - 1);
		toolbarHits.Clear();
		using GraphicsPath graphicsPath = GfxUtil.RoundedRect(rectangle, CornerRadius);
		if (image == null)
		{
			PaintEmpty(graphics, rectangle, current);
			return;
		}
		using (SolidBrush brush = new SolidBrush(current.SurfaceAlt))
		{
			graphics.FillPath(brush, graphicsPath);
		}
		Region clip = graphics.Clip;
		graphics.SetClip(graphicsPath);
		graphics.FillRectangle(Checker(current), rectangle);
		RectangleF dest = DestRect();
		DrawLayer(graphics, image, dest, imageCache);
		if (compareImage != null)
		{
			int sx = SplitX;
			graphics.SetClip(new Rectangle(0, 0, sx, base.Height), CombineMode.Intersect);
			DrawLayer(graphics, compareImage, dest, compareCache);
			graphics.SetClip(graphicsPath);
			PaintSplitHandle(graphics, current, sx);
		}
		if (Interactive)
		{
			PaintOverlay(graphics, current);
		}
		graphics.Clip = clip;
		using Pen pen = new Pen(dragOver ? current.Accent : current.Border, dragOver ? Ui.S(2f) : 1f);
		graphics.DrawPath(pen, graphicsPath);
	}

	private void PaintEmpty(Graphics g, Rectangle r, Palette p)
	{
		Rectangle inner = Rectangle.Inflate(r, -Ui.S(3), -Ui.S(3));
		Color fill = dragOver ? Theme.Mix(p.SurfaceAlt, p.Accent, 0.14) : p.SurfaceAlt;
		GfxUtil.FillRounded(g, r, CornerRadius, Theme.Mix(p.SurfaceAlt, p.Surface, 0.5));
		GfxUtil.FillRounded(g, inner, CornerRadius - Ui.S(2), fill);
		GfxUtil.DrawRounded(g, inner, CornerRadius - Ui.S(2), dragOver ? p.Accent : Theme.Mix(p.Border, p.TextMuted, 0.35), Ui.S(1.2f), !dragOver);
		bool roomy = base.Height > Ui.S(110) && base.Width > Ui.S(120);
		int textTop = r.Top;
		int textHeight = r.Height;
		if (roomy)
		{
			// Upload glyph: an arrow rising out of a tray.
			float cx = r.Width / 2f;
			float cy = r.Height / 2f - Ui.S(22);
			float s = Ui.S(11f);
			Color ic = dragOver ? p.Accent : p.TextMuted;
			using Pen pen = new Pen(ic, Ui.S(1.8f));
			pen.StartCap = LineCap.Round;
			pen.EndCap = LineCap.Round;
			pen.LineJoin = LineJoin.Round;
			g.DrawLine(pen, cx, cy + s * 0.5f, cx, cy - s);
			g.DrawLines(pen, new PointF[3]
			{
				new PointF(cx - s * 0.55f, cy - s * 0.45f),
				new PointF(cx, cy - s),
				new PointF(cx + s * 0.55f, cy - s * 0.45f)
			});
			g.DrawLines(pen, new PointF[4]
			{
				new PointF(cx - s, cy + s * 0.2f),
				new PointF(cx - s, cy + s),
				new PointF(cx + s, cy + s),
				new PointF(cx + s, cy + s * 0.2f)
			});
			textTop = (int)(cy + s + Ui.S(10));
			textHeight = Ui.S(22);
		}
		if (!string.IsNullOrEmpty(EmptyText))
		{
			TextRenderer.DrawText(g, EmptyText, roomy ? Ui.Strong : Ui.Small, new Rectangle(r.Left + Ui.S(8), textTop, r.Width - Ui.S(16), textHeight), dragOver ? p.Accent : (roomy ? p.Text : p.TextMuted), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
		}
		if (roomy && !string.IsNullOrEmpty(EmptyHint))
		{
			TextRenderer.DrawText(g, EmptyHint, Ui.Small, new Rectangle(r.Left + Ui.S(8), textTop + textHeight, r.Width - Ui.S(16), Ui.S(20)), p.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
		}
	}

	private void DrawLayer(Graphics g, Image src, RectangleF dest, ScaledCache cache)
	{
		float scale = dest.Width / src.Width;
		using ImageAttributes attrs = ChannelAttributes();
		if (scale >= 1f)
		{
			// Magnified: crisp pixels; only the visible part of the source is drawn.
			RectangleF vis = RectangleF.Intersect(dest, new RectangleF(0f, 0f, base.Width, base.Height));
			if (vis.Width <= 0f || vis.Height <= 0f)
			{
				return;
			}
			int sx0 = Math.Max(0, (int)Math.Floor((vis.Left - dest.Left) / scale));
			int sy0 = Math.Max(0, (int)Math.Floor((vis.Top - dest.Top) / scale));
			int sx1 = Math.Min(src.Width, (int)Math.Ceiling((vis.Right - dest.Left) / scale));
			int sy1 = Math.Min(src.Height, (int)Math.Ceiling((vis.Bottom - dest.Top) / scale));
			Rectangle d = Rectangle.Round(new RectangleF(dest.Left + sx0 * scale, dest.Top + sy0 * scale, (sx1 - sx0) * scale, (sy1 - sy0) * scale));
			g.InterpolationMode = InterpolationMode.NearestNeighbor;
			g.PixelOffsetMode = PixelOffsetMode.Half;
			g.DrawImage(src, d, sx0, sy0, sx1 - sx0, sy1 - sy0, GraphicsUnit.Pixel, attrs);
			g.PixelOffsetMode = PixelOffsetMode.Default;
			return;
		}
		// Reduced: draw a high-quality downscaled copy, cached while the size and channel stay the same.
		Size size = new Size(Math.Max(1, (int)Math.Round(dest.Width)), Math.Max(1, (int)Math.Round(dest.Height)));
		if (cache.Bitmap == null || cache.Source != src || cache.Size != size || cache.Channel != channel)
		{
			cache.Clear();
			Bitmap b = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
			using (Graphics cg = Graphics.FromImage(b))
			{
				cg.InterpolationMode = InterpolationMode.HighQualityBicubic;
				cg.PixelOffsetMode = PixelOffsetMode.HighQuality;
				cg.DrawImage(src, new Rectangle(0, 0, size.Width, size.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, attrs);
			}
			cache.Bitmap = b;
			cache.Source = src;
			cache.Size = size;
			cache.Channel = channel;
		}
		g.DrawImageUnscaled(cache.Bitmap, (int)Math.Round(dest.Left), (int)Math.Round(dest.Top));
	}

	// Colour matrix that shows one channel as opaque grayscale; no matrix for normal RGB(A).
	private ImageAttributes ChannelAttributes()
	{
		ImageAttributes attrs = new ImageAttributes();
		if (channel == 0)
		{
			return attrs;
		}
		int src = channel switch
		{
			1 => 0,
			2 => 1,
			3 => 2,
			_ => 3,
		};
		float[][] m = new float[5][];
		for (int i = 0; i < 5; i++)
		{
			m[i] = new float[5];
		}
		m[src][0] = m[src][1] = m[src][2] = 1f;
		m[4][3] = 1f;
		m[4][4] = 1f;
		attrs.SetColorMatrix(new ColorMatrix(m));
		return attrs;
	}

	private void PaintSplitHandle(Graphics g, Palette p, int x)
	{
		using (Pen shadow = new Pen(Color.FromArgb(90, 0, 0, 0), Ui.S(4f)))
		{
			g.DrawLine(shadow, x, 0, x, base.Height);
		}
		using (Pen line = new Pen(Color.White, Ui.S(2f)))
		{
			g.DrawLine(line, x, 0, x, base.Height);
		}
		int r = Ui.S(14);
		int cy = base.Height / 2;
		Rectangle knob = new Rectangle(x - r, cy - r, r * 2, r * 2);
		using (SolidBrush brush = new SolidBrush(Color.White))
		{
			g.FillEllipse(brush, knob);
		}
		using (Pen arrow = new Pen(Color.FromArgb(60, 66, 80), Ui.S(1.8f)))
		{
			arrow.StartCap = LineCap.Round;
			arrow.EndCap = LineCap.Round;
			float a = Ui.S(4f);
			g.DrawLines(arrow, new PointF[3]
			{
				new PointF(x - a * 0.6f, cy - a),
				new PointF(x - a * 1.6f, cy),
				new PointF(x - a * 0.6f, cy + a)
			});
			g.DrawLines(arrow, new PointF[3]
			{
				new PointF(x + a * 0.6f, cy - a),
				new PointF(x + a * 1.6f, cy),
				new PointF(x + a * 0.6f, cy + a)
			});
		}
		Color chipBack = Color.FromArgb(200, 18, 20, 26);
		int m = Ui.S(10);
		Size before = TextRenderer.MeasureText(CompareLabel ?? "", Ui.Label);
		if (!string.IsNullOrEmpty(CompareLabel) && x - before.Width - Ui.S(24) > m)
		{
			GfxUtil.DrawChip(g, CompareLabel, Ui.Label, new Point(x - before.Width - Ui.S(16) - m, m), chipBack, Color.White);
		}
		// The right-hand label sits left of the channel toolbar (which is drawn over it).
		Size after = TextRenderer.MeasureText(ImageLabel ?? "", Ui.Label);
		int toolbarLeft = Interactive ? base.Width - Ui.S(10) - Ui.S(38) - Ui.S(6) - Ui.S(144) - Ui.S(4) : base.Width;
		if (!string.IsNullOrEmpty(ImageLabel) && x + m + after.Width + Ui.S(24) < toolbarLeft)
		{
			GfxUtil.DrawChip(g, ImageLabel, Ui.Label, new Point(x + m, m), chipBack, Color.White);
		}
	}

	private void PaintOverlay(Graphics g, Palette p)
	{
		Color back = Color.FromArgb(210, 18, 20, 26);
		Color fore = Color.FromArgb(225, 230, 238);
		int m = Ui.S(10);
		int h = Ui.S(24);
		// Channel toggle and Fit, top right.
		int[] widths = new int[5] { Ui.S(40), Ui.S(26), Ui.S(26), Ui.S(26), Ui.S(26) };
		int fitW = Ui.S(38);
		int total = 0;
		foreach (int w in widths)
		{
			total += w;
		}
		int x = base.Width - m - fitW - Ui.S(6) - total - Ui.S(4);
		Rectangle seg = new Rectangle(x, m, total + Ui.S(4), h);
		GfxUtil.FillRounded(g, seg, h / 2, back);
		int cx = x + Ui.S(2);
		for (int i = 0; i < 5; i++)
		{
			Rectangle r = new Rectangle(cx, m + Ui.S(2), widths[i], h - Ui.S(4));
			if (i == channel)
			{
				GfxUtil.FillRounded(g, r, r.Height / 2, p.Accent);
			}
			Color c = i switch
			{
				1 => Color.FromArgb(255, 120, 115),
				2 => Color.FromArgb(130, 210, 120),
				3 => Color.FromArgb(110, 165, 255),
				_ => fore,
			};
			TextRenderer.DrawText(g, ChannelNames[i], Ui.Label, r, i == channel ? Color.White : c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
			toolbarHits.Add(new KeyValuePair<Rectangle, int>(r, i));
			cx += widths[i];
		}
		Rectangle fit = new Rectangle(base.Width - m - fitW, m, fitW, h);
		GfxUtil.FillRounded(g, fit, h / 2, zoom > 1f ? p.Accent : back);
		TextRenderer.DrawText(g, "Fit", Ui.Label, fit, zoom > 1f ? Color.White : fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
		toolbarHits.Add(new KeyValuePair<Rectangle, int>(fit, -1));
		// Size and zoom, bottom left.
		string info = (string.IsNullOrEmpty(InfoText) ? (image.Width + "×" + image.Height) : InfoText) + "  •  " + (int)Math.Round(ViewScale / Math.Max(0.0001f, PixelRatio) * 100f) + "%";
		Size ts = TextRenderer.MeasureText(info, Ui.Label);
		GfxUtil.DrawChip(g, info, Ui.Label, new Point(m, base.Height - m - ts.Height - Ui.S(8)), back, fore);
	}

	private TextureBrush Checker(Palette p)
	{
		if (checker == null || checkerPalette != p)
		{
			checker?.Dispose();
			int cell = Ui.S(8);
			using Bitmap b = new Bitmap(cell * 2, cell * 2);
			using (Graphics g = Graphics.FromImage(b))
			{
				g.Clear(Theme.Mix(p.SurfaceAlt, p.Surface, 0.6));
				using SolidBrush brush = new SolidBrush(p.SurfaceAlt);
				g.FillRectangle(brush, cell, 0, cell, cell);
				g.FillRectangle(brush, 0, cell, cell, cell);
			}
			checker = new TextureBrush(b);
			checkerPalette = p;
		}
		return checker;
	}
}
