using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace GLMapPacker;

// List of atlas materials: colour swatch (matches the preview outline), name, the maps found,
// a ◀ size ▶ stepper and × to remove. Scrolls with the mouse wheel.
public class AtlasList : Control, IThemeable
{
	private enum Part
	{
		None,
		Row,
		Smaller,
		Larger,
		Remove
	}

	private List<AtlasCore.Entry> entries = new List<AtlasCore.Entry>();

	private int scroll;

	private int hoverRow = -1;

	private Part hoverPart;

	public AtlasCore.Entry Selected { get; private set; }

	// Largest size a material may take (the atlas size).
	public int MaxSize { get; set; }

	public string EmptyText { get; set; }

	public event EventHandler EntriesChanged;

	public event EventHandler SelectionChanged;

	private static int RowH => Ui.S(64);

	public AtlasList()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, value: true);
		BackColor = Color.Transparent;
		MaxSize = 4096;
		EmptyText = "Drop texture sets here";
	}

	public void SetEntries(List<AtlasCore.Entry> list)
	{
		entries = list;
		if (Selected != null && !entries.Contains(Selected))
		{
			Selected = null;
		}
		ClampScroll();
		Invalidate();
	}

	// Colour used for an entry's swatch and its outline in the preview.
	public static Color SwatchColor(int index)
	{
		Color[] colors = new Color[8]
		{
			Color.FromArgb(66, 133, 255),
			Color.FromArgb(232, 84, 80),
			Color.FromArgb(108, 196, 92),
			Color.FromArgb(240, 180, 50),
			Color.FromArgb(170, 110, 240),
			Color.FromArgb(40, 190, 200),
			Color.FromArgb(240, 120, 190),
			Color.FromArgb(150, 160, 175)
		};
		return colors[index % colors.Length];
	}

	public void ApplyTheme(Palette p)
	{
		Invalidate();
	}

	private void ClampScroll()
	{
		scroll = Math.Max(0, Math.Min(scroll, entries.Count * RowH - base.Height));
	}

	private Rectangle RowRect(int i)
	{
		return new Rectangle(0, i * RowH - scroll, base.Width - 1, RowH - Ui.S(6));
	}

	private Rectangle RemoveRect(Rectangle row)
	{
		int s = Ui.S(24);
		return new Rectangle(row.Right - s - Ui.S(8), row.Y + Ui.S(5), s, s);
	}

	private Rectangle StepperRect(Rectangle row)
	{
		Rectangle rm = RemoveRect(row);
		int w = Ui.S(112);
		return new Rectangle(rm.X - w - Ui.S(6), row.Y + Ui.S(3), w, Ui.S(28));
	}

	private Part HitTest(Point p, out int row)
	{
		row = (p.Y + scroll) / RowH;
		if (row < 0 || row >= entries.Count || p.Y < 0)
		{
			row = -1;
			return Part.None;
		}
		Rectangle r = RowRect(row);
		if (RemoveRect(r).Contains(p))
		{
			return Part.Remove;
		}
		Rectangle st = StepperRect(r);
		if (st.Contains(p))
		{
			return p.X < st.X + st.Width / 2 ? Part.Smaller : Part.Larger;
		}
		return r.Contains(p) ? Part.Row : Part.None;
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		Part part = HitTest(e.Location, out var row);
		if (row != hoverRow || part != hoverPart)
		{
			hoverRow = row;
			hoverPart = part;
			Invalidate();
		}
		Cursor = part == Part.None ? Cursors.Default : Cursors.Hand;
		base.OnMouseMove(e);
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		hoverRow = -1;
		hoverPart = Part.None;
		Invalidate();
		base.OnMouseLeave(e);
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		Focus();
		Part part = HitTest(e.Location, out var row);
		if (row >= 0)
		{
			AtlasCore.Entry entry = entries[row];
			switch (part)
			{
			case Part.Remove:
				entries.RemoveAt(row);
				if (Selected == entry)
				{
					Selected = null;
				}
				ClampScroll();
				Changed();
				break;
			case Part.Smaller:
				entry.Size = Math.Max(32, entry.Size / 2);
				Changed();
				break;
			case Part.Larger:
				entry.Size = Math.Min(MaxSize, entry.Size * 2);
				Changed();
				break;
			default:
				Selected = Selected == entry ? null : entry;
				Invalidate();
				SelectionChanged?.Invoke(this, EventArgs.Empty);
				break;
			}
		}
		base.OnMouseDown(e);
	}

	private void Changed()
	{
		Invalidate();
		EntriesChanged?.Invoke(this, EventArgs.Empty);
	}

	protected override void OnMouseWheel(MouseEventArgs e)
	{
		scroll -= Math.Sign(e.Delta) * RowH;
		ClampScroll();
		Invalidate();
		base.OnMouseWheel(e);
	}

	protected override void OnResize(EventArgs e)
	{
		ClampScroll();
		base.OnResize(e);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Palette p = Theme.Current;
		Graphics g = e.Graphics;
		g.SmoothingMode = SmoothingMode.AntiAlias;
		g.Clear(GfxUtil.ResolveBack(this));
		if (entries.Count == 0)
		{
			Rectangle r = new Rectangle(0, 0, base.Width - 1, base.Height - 1);
			GfxUtil.DrawRounded(g, Rectangle.Inflate(r, -Ui.S(2), -Ui.S(2)), Ui.S(10), Theme.Mix(p.Border, p.TextMuted, 0.35), Ui.S(1.2f), dashed: true);
			TextRenderer.DrawText(g, EmptyText, Ui.Strong, new Rectangle(0, r.Height / 2 - Ui.S(22), r.Width, Ui.S(22)), p.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
			TextRenderer.DrawText(g, "Albedo, Normal, ORM or Roughness / AO / Metallic, Emissive…", Ui.Small, new Rectangle(Ui.S(8), r.Height / 2, r.Width - Ui.S(16), Ui.S(20)), p.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
			TextRenderer.DrawText(g, "files are grouped into materials by name", Ui.Small, new Rectangle(Ui.S(8), r.Height / 2 + Ui.S(20), r.Width - Ui.S(16), Ui.S(20)), p.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
			return;
		}
		for (int i = 0; i < entries.Count; i++)
		{
			Rectangle row = RowRect(i);
			if (row.Bottom < 0 || row.Top > base.Height)
			{
				continue;
			}
			AtlasCore.Entry entry = entries[i];
			bool sel = entry == Selected;
			Color fill = sel ? Theme.Mix(p.SurfaceAlt, p.Accent, 0.16) : (i == hoverRow ? Theme.Mix(p.SurfaceAlt, p.Text, 0.04) : p.SurfaceAlt);
			GfxUtil.FillRounded(g, row, Ui.S(8), fill);
			GfxUtil.DrawRounded(g, row, Ui.S(8), sel ? p.Accent : p.Border);
			// Swatch.
			Rectangle sw = new Rectangle(row.X + Ui.S(12), row.Y + Ui.S(10), Ui.S(14), Ui.S(14));
			GfxUtil.FillRounded(g, sw, Ui.S(3), SwatchColor(i));
			Rectangle stepper = StepperRect(row);
			int textX = sw.Right + Ui.S(10);
			int textW = stepper.X - Ui.S(8) - textX;
			TextRenderer.DrawText(g, entry.Name, Ui.Strong, new Rectangle(textX, row.Y + Ui.S(6), textW, Ui.S(20)), p.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
			// Map chips (full row width, in a fixed order), or why it isn't in the atlas.
			textW = row.Right - Ui.S(10) - textX;
			int cx = textX;
			int cy = row.Y + Ui.S(34);
			if (!entry.Placed)
			{
				TextRenderer.DrawText(g, "doesn't fit: make it smaller or the atlas bigger", Ui.Small, new Rectangle(cx, cy, textW, Ui.S(18)), p.Danger, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
			}
			else
			{
				foreach (AtlasCore.Map m in entry.Files.Keys.OrderBy(k => (int)k))
				{
					string label = AtlasCore.ShortLabel(m);
					Size ts = TextRenderer.MeasureText(g, label, Ui.Label, Size.Empty, TextFormatFlags.NoPadding);
					Rectangle chip = new Rectangle(cx, cy, ts.Width + Ui.S(10), Ui.S(18));
					if (chip.Right > textX + textW)
					{
						break;
					}
					GfxUtil.FillRounded(g, chip, chip.Height / 2, Theme.Mix(p.SurfaceAlt, p.Text, 0.09));
					TextRenderer.DrawText(g, label, Ui.Label, chip, p.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
					cx = chip.Right + Ui.S(4);
				}
			}
			// Size stepper.
			GfxUtil.FillRounded(g, stepper, stepper.Height / 2, p.Surface);
			GfxUtil.DrawRounded(g, stepper, stepper.Height / 2, p.Border);
			int half = stepper.Width / 2;
			bool hs = i == hoverRow && hoverPart == Part.Smaller;
			bool hl = i == hoverRow && hoverPart == Part.Larger;
			TextRenderer.DrawText(g, "◀", Ui.Small, new Rectangle(stepper.X + Ui.S(4), stepper.Y, Ui.S(18), stepper.Height), hs ? p.Accent : p.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
			TextRenderer.DrawText(g, "▶", Ui.Small, new Rectangle(stepper.Right - Ui.S(22), stepper.Y, Ui.S(18), stepper.Height), hl ? p.Accent : p.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
			TextRenderer.DrawText(g, entry.Size + " px", Ui.Label, new Rectangle(stepper.X + Ui.S(20), stepper.Y, stepper.Width - Ui.S(40), stepper.Height), p.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
			// Remove.
			Rectangle rm = RemoveRect(row);
			bool hr = i == hoverRow && hoverPart == Part.Remove;
			if (hr)
			{
				using SolidBrush hb = new SolidBrush(Theme.Mix(fill, p.Danger, 0.2));
				g.FillEllipse(hb, rm);
			}
			using Pen pen = new Pen(hr ? p.Danger : p.TextMuted, Ui.S(1.4f));
			float c = Ui.S(4f);
			float mx = rm.X + rm.Width / 2f;
			float my = rm.Y + rm.Height / 2f;
			g.DrawLine(pen, mx - c, my - c, mx + c, my + c);
			g.DrawLine(pen, mx + c, my - c, mx - c, my + c);
		}
		if (entries.Count * RowH > base.Height)
		{
			int trackH = base.Height;
			int thumbH = Math.Max(Ui.S(20), trackH * base.Height / (entries.Count * RowH));
			int maxScroll = entries.Count * RowH - base.Height;
			int thumbY = (trackH - thumbH) * scroll / Math.Max(1, maxScroll);
			GfxUtil.FillRounded(g, new Rectangle(base.Width - Ui.S(4), thumbY, Ui.S(3), thumbH), Ui.S(1), Theme.Mix(p.Border, p.TextMuted, 0.4));
		}
	}
}
