using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace GLMapPacker;

// The atlas layer stack: one row per material with a drag grip, a colour swatch (matching its outline
// in the preview) showing its place in the stack, the maps found, a ◀ size ▶ stepper and × to remove.
// Drag a row up or down to reorder; in stack order, layer 1 is placed first (top-left).
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

	// Drag-to-reorder state.
	private int pressIndex = -1;

	private Point pressPoint;

	private bool dragging;

	private int dragY;

	private int grabOffset;

	private int insertIndex;

	private readonly Timer autoScroll;

	public AtlasCore.Entry Selected { get; private set; }

	// Largest size a material may take (the atlas size).
	public int MaxSize { get; set; }

	// Show each layer's number in its swatch (when placement follows the stack order).
	public bool ShowOrder { get; set; }

	public string EmptyText { get; set; }

	public event EventHandler EntriesChanged;

	public event EventHandler SelectionChanged;

	private static int RowH => Ui.S(64);

	public AtlasList()
	{
		SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, value: true);
		BackColor = Color.Transparent;
		MaxSize = 4096;
		ShowOrder = true;
		EmptyText = "Drop texture sets here";
		autoScroll = new Timer
		{
			Interval = 30
		};
		autoScroll.Tick += delegate
		{
			AutoScrollStep();
		};
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
		return colors[((index % colors.Length) + colors.Length) % colors.Length];
	}

	public void ApplyTheme(Palette p)
	{
		Invalidate();
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			autoScroll.Dispose();
		}
		base.Dispose(disposing);
	}

	private int MaxScroll => Math.Max(0, entries.Count * RowH - base.Height);

	private void ClampScroll()
	{
		scroll = Math.Max(0, Math.Min(scroll, MaxScroll));
	}

	private Rectangle SlotRect(int slot)
	{
		return new Rectangle(0, slot * RowH - scroll, base.Width - 1, RowH - Ui.S(6));
	}

	private static Rectangle RemoveRect(Rectangle row)
	{
		int s = Ui.S(24);
		return new Rectangle(row.Right - s - Ui.S(8), row.Y + Ui.S(5), s, s);
	}

	private static Rectangle StepperRect(Rectangle row)
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
		Rectangle r = SlotRect(row);
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

	protected override void OnMouseDown(MouseEventArgs e)
	{
		Focus();
		if (e.Button != MouseButtons.Left)
		{
			base.OnMouseDown(e);
			return;
		}
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
			case Part.Row:
				// Select on release; moving first turns this into a drag.
				pressIndex = row;
				pressPoint = e.Location;
				grabOffset = e.Y - SlotRect(row).Y;
				base.Capture = true;
				break;
			}
		}
		base.OnMouseDown(e);
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		if (pressIndex >= 0 && !dragging && Math.Abs(e.Y - pressPoint.Y) > Ui.S(5))
		{
			dragging = true;
			Cursor = Cursors.SizeNS;
			autoScroll.Start();
		}
		if (dragging)
		{
			dragY = e.Y;
			UpdateInsertIndex();
			Invalidate();
			base.OnMouseMove(e);
			return;
		}
		Part part = HitTest(e.Location, out var row);
		if (row != hoverRow || part != hoverPart)
		{
			hoverRow = row;
			hoverPart = part;
			Invalidate();
		}
		Cursor = part == Part.None ? Cursors.Default : (part == Part.Row ? Cursors.SizeNS : Cursors.Hand);
		base.OnMouseMove(e);
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		if (dragging)
		{
			AtlasCore.Entry moved = entries[pressIndex];
			entries.RemoveAt(pressIndex);
			entries.Insert(Math.Max(0, Math.Min(entries.Count, insertIndex)), moved);
			EndDrag();
			Changed();
		}
		else if (pressIndex >= 0)
		{
			AtlasCore.Entry entry = entries[pressIndex];
			Selected = Selected == entry ? null : entry;
			EndDrag();
			Invalidate();
			SelectionChanged?.Invoke(this, EventArgs.Empty);
		}
		base.OnMouseUp(e);
	}

	protected override void OnLostFocus(EventArgs e)
	{
		if (dragging)
		{
			EndDrag();
			Invalidate();
		}
		base.OnLostFocus(e);
	}

	private void EndDrag()
	{
		dragging = false;
		pressIndex = -1;
		autoScroll.Stop();
		base.Capture = false;
		Cursor = Cursors.Default;
	}

	// Where the dragged row would land: the slot under its centre.
	private void UpdateInsertIndex()
	{
		int centre = dragY - grabOffset + (RowH - Ui.S(6)) / 2 + scroll;
		insertIndex = Math.Max(0, Math.Min(entries.Count - 1, centre / RowH));
	}

	// Scrolls while a drag is held near the top or bottom edge.
	private void AutoScrollStep()
	{
		if (!dragging)
		{
			return;
		}
		int edge = Ui.S(28);
		int step = 0;
		if (dragY < edge)
		{
			step = -Ui.S(8);
		}
		else if (dragY > base.Height - edge)
		{
			step = Ui.S(8);
		}
		if (step != 0)
		{
			int old = scroll;
			scroll += step;
			ClampScroll();
			if (scroll != old)
			{
				UpdateInsertIndex();
				Invalidate();
			}
		}
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		hoverRow = -1;
		hoverPart = Part.None;
		Invalidate();
		base.OnMouseLeave(e);
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
		if (!dragging)
		{
			for (int i = 0; i < entries.Count; i++)
			{
				PaintRow(g, p, entries[i], i, SlotRect(i), floating: false);
			}
		}
		else
		{
			// The other rows close up around the gap where the dragged row will land.
			AtlasCore.Entry moving = entries[pressIndex];
			List<AtlasCore.Entry> rest = entries.Where(x => x != moving).ToList();
			for (int k = 0; k < rest.Count; k++)
			{
				int slot = k < insertIndex ? k : k + 1;
				PaintRow(g, p, rest[k], slot, SlotRect(slot), floating: false);
			}
			Rectangle gap = SlotRect(insertIndex);
			GfxUtil.DrawRounded(g, gap, Ui.S(8), p.Accent, Ui.S(1.5f), dashed: true);
			Rectangle floatRect = new Rectangle(0, dragY - grabOffset, base.Width - 1, RowH - Ui.S(6));
			using (SolidBrush shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
			{
				using GraphicsPath path = GfxUtil.RoundedRect(new Rectangle(floatRect.X + Ui.S(2), floatRect.Y + Ui.S(4), floatRect.Width, floatRect.Height), Ui.S(8));
				g.FillPath(shadow, path);
			}
			PaintRow(g, p, moving, insertIndex, floatRect, floating: true);
		}
		if (entries.Count * RowH > base.Height)
		{
			int thumbH = Math.Max(Ui.S(20), base.Height * base.Height / (entries.Count * RowH));
			int thumbY = (base.Height - thumbH) * scroll / Math.Max(1, MaxScroll);
			GfxUtil.FillRounded(g, new Rectangle(base.Width - Ui.S(4), thumbY, Ui.S(3), thumbH), Ui.S(1), Theme.Mix(p.Border, p.TextMuted, 0.4));
		}
	}

	private void PaintRow(Graphics g, Palette p, AtlasCore.Entry entry, int position, Rectangle row, bool floating)
	{
		if (row.Bottom < 0 || row.Top > base.Height)
		{
			return;
		}
		int index = entries.IndexOf(entry);
		bool sel = entry == Selected;
		bool hover = !dragging && index == hoverRow;
		Color fill = floating ? Theme.Mix(p.SurfaceAlt, p.Accent, 0.12) : (sel ? Theme.Mix(p.SurfaceAlt, p.Accent, 0.16) : (hover ? Theme.Mix(p.SurfaceAlt, p.Text, 0.04) : p.SurfaceAlt));
		GfxUtil.FillRounded(g, row, Ui.S(8), fill);
		GfxUtil.DrawRounded(g, row, Ui.S(8), (sel || floating) ? p.Accent : p.Border, floating ? Ui.S(1.5f) : 1f);
		// Grip: two columns of three dots.
		using (SolidBrush dots = new SolidBrush(Theme.Mix(p.TextMuted, fill, hover || floating ? 0.1 : 0.45)))
		{
			float gx = row.X + Ui.S(10);
			float gy = row.Y + Ui.S(11);
			float d = Ui.S(3f);
			for (int c = 0; c < 2; c++)
			{
				for (int r = 0; r < 3; r++)
				{
					g.FillEllipse(dots, gx + c * Ui.S(5f), gy + r * Ui.S(5f), d, d);
				}
			}
		}
		// Swatch with the layer number.
		Rectangle sw = new Rectangle(row.X + Ui.S(24), row.Y + Ui.S(7), Ui.S(20), Ui.S(20));
		GfxUtil.FillRounded(g, sw, Ui.S(4), SwatchColor(entry.ColorIndex));
		if (ShowOrder)
		{
			TextRenderer.DrawText(g, (position + 1).ToString(), Ui.Label, sw, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
		}
		Rectangle stepper = StepperRect(row);
		int textX = sw.Right + Ui.S(10);
		int nameW = stepper.X - Ui.S(8) - textX;
		TextRenderer.DrawText(g, entry.Name, Ui.Strong, new Rectangle(textX, row.Y + Ui.S(6), nameW, Ui.S(22)), p.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
		// Map chips (full row width, fixed order), or why it isn't in the atlas.
		int chipsX = row.X + Ui.S(24);
		int textW = row.Right - Ui.S(10) - chipsX;
		int cx = chipsX;
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
				if (chip.Right > chipsX + textW)
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
		bool hs = hover && hoverPart == Part.Smaller;
		bool hl = hover && hoverPart == Part.Larger;
		TextRenderer.DrawText(g, "◀", Ui.Small, new Rectangle(stepper.X + Ui.S(4), stepper.Y, Ui.S(18), stepper.Height), hs ? p.Accent : p.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
		TextRenderer.DrawText(g, "▶", Ui.Small, new Rectangle(stepper.Right - Ui.S(22), stepper.Y, Ui.S(18), stepper.Height), hl ? p.Accent : p.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
		TextRenderer.DrawText(g, entry.Size + " px", Ui.Label, new Rectangle(stepper.X + Ui.S(20), stepper.Y, stepper.Width - Ui.S(40), stepper.Height), p.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
		// Remove.
		Rectangle rm = RemoveRect(row);
		bool hr = hover && hoverPart == Part.Remove;
		if (hr)
		{
			using SolidBrush hb = new SolidBrush(Theme.Mix(fill, p.Danger, 0.2));
			g.FillEllipse(hb, rm);
		}
		using Pen pen = new Pen(hr ? p.Danger : p.TextMuted, Ui.S(1.4f));
		float cc = Ui.S(4f);
		float mx = rm.X + rm.Width / 2f;
		float my = rm.Y + rm.Height / 2f;
		g.DrawLine(pen, mx - cc, my - cc, mx + cc, my + cc);
		g.DrawLine(pen, mx + cc, my - cc, mx - cc, my + cc);
	}
}
