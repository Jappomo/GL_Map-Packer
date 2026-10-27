using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GLMapPacker;

// Texture atlasing: group texture files into materials, pack each material into the atlas at the
// size it was given (top-most, then left-most free spot), and render one atlas per map type with the
// same layout, so every material lines up across its albedo, normal, ORM and other maps.
public static class AtlasCore
{
	public enum Map
	{
		Albedo,
		Normal,
		ORM,
		Roughness,
		AO,
		Metallic,
		Emissive,
		Opacity,
		Height
	}

	public enum MaskOutput
	{
		// Occlusion, roughness and metallic packed in one ORM atlas (R = AO, G = roughness, B = metallic).
		Packed,
		// Separate roughness, AO and metallic atlases.
		Separate
	}

	public enum Order
	{
		// The layer stack's order, top to bottom: layer 1 is placed first.
		Stack,
		LargestFirst
	}

	public sealed class Entry
	{
		public string Name;

		public readonly Dictionary<Map, string> Files = new Dictionary<Map, string>();

		// Longest side in the atlas, in pixels.
		public int Size;

		// Swatch / outline colour; stays with the material when the stack is reordered.
		public int ColorIndex;

		// Source size of the first map (for the aspect ratio).
		public int SourceWidth;

		public int SourceHeight;

		// Placement in atlas pixels; Placed is false when it didn't fit.
		public bool Placed;

		public int X;

		public int Y;

		public int W;

		public int H;

		public void TileSize(int size, out int w, out int h)
		{
			if (SourceWidth <= 0 || SourceHeight <= 0 || SourceWidth == SourceHeight)
			{
				w = h = size;
				return;
			}
			if (SourceWidth > SourceHeight)
			{
				w = size;
				h = Math.Max(1, (int)Math.Round((double)size * SourceHeight / SourceWidth));
			}
			else
			{
				h = size;
				w = Math.Max(1, (int)Math.Round((double)size * SourceWidth / SourceHeight));
			}
		}
	}

	// ---------------------------------------------------------------- file names

	// Suffixes per map type, longest first so "_basecolor" wins over "_color".
	private static readonly KeyValuePair<string, Map>[] Suffixes = BuildSuffixes();

	private static KeyValuePair<string, Map>[] BuildSuffixes()
	{
		Dictionary<Map, string[]> table = new Dictionary<Map, string[]>
		{
			{ Map.Albedo, new string[] { "albedo", "basecolor", "base_color", "basecolour", "diffuse", "diff", "color", "colour", "col", "bc" } },
			{ Map.Normal, new string[] { "normal", "normalgl", "normal_gl", "normaldx", "normal_dx", "nrm", "nor", "norm", "n" } },
			{ Map.ORM, new string[] { "orm", "arm", "occlusionroughnessmetallic" } },
			{ Map.Roughness, new string[] { "roughness", "rough", "rgh" } },
			{ Map.AO, new string[] { "ao", "ambientocclusion", "ambient_occlusion", "occlusion", "occ" } },
			{ Map.Metallic, new string[] { "metallic", "metalness", "metal", "mtl" } },
			{ Map.Emissive, new string[] { "emissive", "emission", "emit", "glow" } },
			{ Map.Opacity, new string[] { "opacity", "alpha", "transparency" } },
			{ Map.Height, new string[] { "height", "displacement", "disp", "bump" } }
		};
		List<KeyValuePair<string, Map>> list = new List<KeyValuePair<string, Map>>();
		foreach (KeyValuePair<Map, string[]> t in table)
		{
			foreach (string s in t.Value)
			{
				list.Add(new KeyValuePair<string, Map>(s, t.Key));
			}
		}
		return list.OrderByDescending(p => p.Key.Length).ToArray();
	}

	// Map type and material name from a file name, e.g. "T_Rock_BaseColor.png" -> (Albedo, "Rock").
	public static bool Detect(string path, out Map map, out string name)
	{
		string stem = Path.GetFileNameWithoutExtension(path);
		string lower = stem.ToLowerInvariant();
		foreach (KeyValuePair<string, Map> s in Suffixes)
		{
			if (lower.Length > s.Key.Length + 1 && lower.EndsWith(s.Key))
			{
				char sep = lower[lower.Length - s.Key.Length - 1];
				if (sep == '_' || sep == '-' || sep == '.' || sep == ' ')
				{
					map = s.Value;
					name = CleanName(stem.Substring(0, stem.Length - s.Key.Length - 1));
					return true;
				}
			}
		}
		map = Map.Albedo;
		name = CleanName(stem);
		return false;
	}

	private static string CleanName(string s)
	{
		s = s.Trim('_', '-', '.', ' ');
		if (s.Length > 2 && (s.StartsWith("T_", StringComparison.OrdinalIgnoreCase) || s.StartsWith("M_", StringComparison.OrdinalIgnoreCase)))
		{
			s = s.Substring(2);
		}
		return s.Length == 0 ? "material" : s;
	}

	public static string Suffix(Map m)
	{
		return m switch
		{
			Map.Albedo => "BaseColor",
			Map.Normal => "Normal",
			Map.ORM => "ORM",
			Map.Roughness => "Roughness",
			Map.AO => "AO",
			Map.Metallic => "Metallic",
			Map.Emissive => "Emissive",
			Map.Opacity => "Opacity",
			_ => "Height",
		};
	}

	public static string ShortLabel(Map m)
	{
		return m switch
		{
			Map.Albedo => "Albedo",
			Map.Normal => "Normal",
			Map.ORM => "ORM",
			Map.Roughness => "Rough",
			Map.AO => "AO",
			Map.Metallic => "Metal",
			Map.Emissive => "Emit",
			Map.Opacity => "Opacity",
			_ => "Height",
		};
	}

	public static int Channels(Map m)
	{
		return m switch
		{
			Map.Albedo => 4,
			Map.Normal => 3,
			Map.ORM => 3,
			Map.Emissive => 3,
			_ => 1,
		};
	}

	// Value used where a material has no map of this type, and for empty atlas space.
	public static float[] Neutral(Map m)
	{
		return m switch
		{
			Map.Albedo => new float[4] { 0.5f, 0.5f, 0.5f, 1f },
			Map.Normal => new float[3] { 0.5f, 0.5f, 1f },
			Map.ORM => new float[3] { 1f, 0.5f, 0f },
			Map.Roughness => new float[1] { 0.5f },
			Map.AO => new float[1] { 1f },
			Map.Metallic => new float[1] { 0f },
			Map.Emissive => new float[3] { 0f, 0f, 0f },
			Map.Opacity => new float[1] { 1f },
			_ => new float[1] { 0.5f },
		};
	}

	// Adds files to the entry list, grouping them into materials by name.
	public static List<string> AddFiles(List<Entry> entries, IEnumerable<string> paths, int defaultSize)
	{
		List<string> skipped = new List<string>();
		foreach (string path in paths)
		{
			Detect(path, out var map, out var name);
			Entry e = entries.Find(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
			if (e == null)
			{
				e = new Entry
				{
					Name = name,
					Size = defaultSize,
					ColorIndex = entries.Count == 0 ? 0 : entries.Max(x => x.ColorIndex) + 1
				};
				entries.Add(e);
			}
			if (e.Files.ContainsKey(map))
			{
				skipped.Add(Path.GetFileName(path));
				continue;
			}
			e.Files[map] = path;
		}
		return skipped;
	}

	// Which atlases the current materials produce.
	public static List<Map> OutputMaps(IEnumerable<Entry> entries, MaskOutput mask)
	{
		HashSet<Map> have = new HashSet<Map>();
		foreach (Entry e in entries)
		{
			foreach (Map m in e.Files.Keys)
			{
				have.Add(m);
			}
		}
		bool anyMask = have.Contains(Map.ORM) || have.Contains(Map.Roughness) || have.Contains(Map.AO) || have.Contains(Map.Metallic);
		List<Map> list = new List<Map>();
		foreach (Map m in (Map[])Enum.GetValues(typeof(Map)))
		{
			bool isMask = m == Map.ORM || m == Map.Roughness || m == Map.AO || m == Map.Metallic;
			if (!isMask && have.Contains(m))
			{
				list.Add(m);
			}
			else if (isMask && anyMask)
			{
				if (mask == MaskOutput.Packed && m == Map.ORM)
				{
					list.Add(m);
				}
				else if (mask == MaskOutput.Separate && m != Map.ORM && (have.Contains(m) || have.Contains(Map.ORM)))
				{
					list.Add(m);
				}
			}
		}
		return list;
	}

	// ---------------------------------------------------------------- packing

	private struct Rect
	{
		public int X;

		public int Y;

		public int W;

		public int H;

		public Rect(int x, int y, int w, int h)
		{
			X = x;
			Y = y;
			W = w;
			H = h;
		}

		public bool Contains(Rect r)
		{
			return r.X >= X && r.Y >= Y && r.X + r.W <= X + W && r.Y + r.H <= Y + H;
		}

		public bool Intersects(Rect r)
		{
			return r.X < X + W && r.X + r.W > X && r.Y < Y + H && r.Y + r.H > Y;
		}
	}

	// Places every entry with a MaxRects packer that always takes the top-most free spot, then the
	// left-most: the atlas fills from the top-left corner and moves down when a row is full.
	// Each tile reserves `padding` extra pixels right and below; the bin is grown by the same amount,
	// so tiles still reach the atlas edges. Returns how many entries didn't fit.
	public static int Pack(List<Entry> entries, int atlasW, int atlasH, int padding, Order order, int sizeScaleShift = 0)
	{
		List<Entry> list = order == Order.LargestFirst ? entries.OrderByDescending(e => e.Size).ToList() : new List<Entry>(entries);
		List<Rect> free = new List<Rect>
		{
			new Rect(0, 0, atlasW + padding, atlasH + padding)
		};
		int missing = 0;
		foreach (Entry e in list)
		{
			e.TileSize(Math.Max(1, e.Size >> sizeScaleShift), out var tw, out var th);
			int rw = tw + padding;
			int rh = th + padding;
			int bestIndex = -1;
			Rect best = default(Rect);
			for (int i = 0; i < free.Count; i++)
			{
				Rect f = free[i];
				if (f.W >= rw && f.H >= rh && (bestIndex < 0 || f.Y < best.Y || (f.Y == best.Y && f.X < best.X)))
				{
					bestIndex = i;
					best = new Rect(f.X, f.Y, rw, rh);
				}
			}
			if (bestIndex < 0)
			{
				e.Placed = false;
				missing++;
				continue;
			}
			e.Placed = true;
			e.X = best.X;
			e.Y = best.Y;
			e.W = tw;
			e.H = th;
			SplitFree(free, best);
		}
		return missing;
	}

	private static void SplitFree(List<Rect> free, Rect used)
	{
		List<Rect> added = new List<Rect>();
		for (int i = free.Count - 1; i >= 0; i--)
		{
			Rect f = free[i];
			if (!f.Intersects(used))
			{
				continue;
			}
			free.RemoveAt(i);
			if (used.X > f.X)
			{
				added.Add(new Rect(f.X, f.Y, used.X - f.X, f.H));
			}
			if (used.X + used.W < f.X + f.W)
			{
				added.Add(new Rect(used.X + used.W, f.Y, f.X + f.W - used.X - used.W, f.H));
			}
			if (used.Y > f.Y)
			{
				added.Add(new Rect(f.X, f.Y, f.W, used.Y - f.Y));
			}
			if (used.Y + used.H < f.Y + f.H)
			{
				added.Add(new Rect(f.X, used.Y + used.H, f.W, f.Y + f.H - used.Y - used.H));
			}
		}
		free.AddRange(added);
		// Drop free rectangles contained in another one.
		for (int i = free.Count - 1; i >= 0; i--)
		{
			for (int j = 0; j < free.Count; j++)
			{
				if (i != j && free[j].Contains(free[i]))
				{
					free.RemoveAt(i);
					break;
				}
			}
		}
	}

	// Share of the atlas covered by placed tiles (0..1).
	public static double Coverage(IEnumerable<Entry> entries, int atlasW, int atlasH)
	{
		long used = 0;
		foreach (Entry e in entries)
		{
			if (e.Placed)
			{
				used += (long)e.W * e.H;
			}
		}
		return (double)used / ((long)atlasW * atlasH);
	}

	// ---------------------------------------------------------------- rendering

	// Atlas pixels for one map type, 16 bits per channel (half the memory of floats at 8K).
	public sealed class Canvas
	{
		public readonly int W;

		public readonly int H;

		public readonly int Channels;

		public readonly ushort[] Data;

		public Canvas(int w, int h, int channels)
		{
			W = w;
			H = h;
			Channels = channels;
			Data = new ushort[(long)w * h * channels];
		}

		public FloatImage ToFloatImage()
		{
			FloatImage img = new FloatImage(W, H, Channels);
			for (int i = 0; i < Data.Length; i++)
			{
				img.Data[i] = Data[i] / 65535f;
			}
			return img;
		}
	}

	// Renders one map type. `scale` shrinks the whole atlas for previews; `load` returns the image
	// for a file path (the caller can hand out cached, downscaled copies for previews).
	public static Canvas Render(List<Entry> entries, Map map, int atlasW, int atlasH, int padding, double scale, Func<string, FloatImage> load, IProgress<int> progress)
	{
		int cw = Math.Max(1, (int)Math.Round(atlasW * scale));
		int ch = Math.Max(1, (int)Math.Round(atlasH * scale));
		int channels = Channels(map);
		Canvas canvas = new Canvas(cw, ch, channels);
		float[] neutral = Neutral(map);
		ushort[] fill = new ushort[channels];
		for (int c = 0; c < channels; c++)
		{
			fill[c] = Q(neutral[c]);
		}
		Parallel.For(0, ch, y =>
		{
			long row = (long)y * cw * channels;
			for (int x = 0; x < cw; x++)
			{
				Array.Copy(fill, 0, canvas.Data, row + (long)x * channels, channels);
			}
		});
		List<Entry> placed = entries.Where(e => e.Placed).ToList();
		int pad = (int)Math.Floor(padding * scale / 2.0);
		List<KeyValuePair<Entry, FloatImage>> tiles = new List<KeyValuePair<Entry, FloatImage>>();
		for (int i = 0; i < placed.Count; i++)
		{
			Entry e = placed[i];
			int x0 = (int)Math.Round(e.X * scale);
			int y0 = (int)Math.Round(e.Y * scale);
			int tw = Math.Max(1, (int)Math.Round((e.X + e.W) * scale) - x0);
			int th = Math.Max(1, (int)Math.Round((e.Y + e.H) * scale) - y0);
			FloatImage tile = TileFor(e, map, tw, th, load) ?? Solid(tw, th, neutral);
			tiles.Add(new KeyValuePair<Entry, FloatImage>(e, tile));
			// Padding first (edge pixels stretched outwards), so the gutter between tiles bleeds the
			// right colours into mipmaps; the tile itself is drawn on top.
			Blit(canvas, tile, x0, y0, pad);
			progress?.Report((i + 1) * 90 / Math.Max(1, placed.Count));
		}
		foreach (KeyValuePair<Entry, FloatImage> t in tiles)
		{
			Blit(canvas, t.Value, (int)Math.Round(t.Key.X * scale), (int)Math.Round(t.Key.Y * scale), 0);
		}
		progress?.Report(100);
		return canvas;
	}

	private static ushort Q(float v)
	{
		return (ushort)(v <= 0f ? 0 : (v >= 1f ? 65535 : (int)(v * 65535f + 0.5f)));
	}

	private static FloatImage Solid(int w, int h, float[] value)
	{
		FloatImage img = new FloatImage(w, h, value.Length);
		for (int i = 0; i < w * h; i++)
		{
			Array.Copy(value, 0, img.Data, i * value.Length, value.Length);
		}
		return img;
	}

	// Writes a tile at (x0, y0), extended by `pad` pixels of clamped edge colour on every side.
	private static void Blit(Canvas canvas, FloatImage tile, int x0, int y0, int pad)
	{
		int c = canvas.Channels;
		int tc = tile.Channels;
		int ys = Math.Max(0, y0 - pad);
		int ye = Math.Min(canvas.H, y0 + tile.Height + pad);
		int xs = Math.Max(0, x0 - pad);
		int xe = Math.Min(canvas.W, x0 + tile.Width + pad);
		Parallel.For(ys, ye, y =>
		{
			int ty = Math.Min(tile.Height - 1, Math.Max(0, y - y0));
			for (int x = xs; x < xe; x++)
			{
				int tx = Math.Min(tile.Width - 1, Math.Max(0, x - x0));
				int s = (ty * tile.Width + tx) * tc;
				long d = ((long)y * canvas.W + x) * c;
				for (int k = 0; k < c; k++)
				{
					canvas.Data[d + k] = Q(k < tc ? tile.Data[s + k] : 1f);
				}
			}
		});
	}

	// The tile for one map type at its atlas size, building or splitting ORM channels when the
	// material has the other form. Null when the material has nothing for this map type.
	private static FloatImage TileFor(Entry e, Map map, int w, int h, Func<string, FloatImage> load)
	{
		FloatImage Get(Map m)
		{
			return e.Files.TryGetValue(m, out var path) ? load(path) : null;
		}
		switch (map)
		{
		case Map.Albedo:
		case Map.Emissive:
		{
			FloatImage src = Get(map);
			return src == null ? null : Fit(src, w, h, Channels(map));
		}
		case Map.Normal:
		{
			FloatImage src = Get(map);
			if (src == null)
			{
				return null;
			}
			FloatImage n = Fit(src, w, h, 3);
			if (n == src)
			{
				n = n.Clone();
			}
			// Averaging normals shortens them; restore unit length.
			for (int i = 0; i < w * h; i++)
			{
				float x = n.Data[i * 3] * 2f - 1f;
				float y = n.Data[i * 3 + 1] * 2f - 1f;
				float z = n.Data[i * 3 + 2] * 2f - 1f;
				float len = (float)Math.Sqrt(x * x + y * y + z * z);
				if (len > 1e-6f)
				{
					n.Data[i * 3] = x / len * 0.5f + 0.5f;
					n.Data[i * 3 + 1] = y / len * 0.5f + 0.5f;
					n.Data[i * 3 + 2] = z / len * 0.5f + 0.5f;
				}
			}
			return n;
		}
		case Map.ORM:
		{
			FloatImage orm = Get(Map.ORM);
			if (orm != null)
			{
				return Fit(orm, w, h, 3);
			}
			FloatImage ao = Get(Map.AO);
			FloatImage rough = Get(Map.Roughness);
			FloatImage metal = Get(Map.Metallic);
			if (ao == null && rough == null && metal == null)
			{
				return null;
			}
			float[] neutral = Neutral(Map.ORM);
			FloatImage outImg = new FloatImage(w, h, 3);
			FloatImage[] parts = new FloatImage[3]
			{
				ao == null ? null : Fit(ao, w, h, 1),
				rough == null ? null : Fit(rough, w, h, 1),
				metal == null ? null : Fit(metal, w, h, 1)
			};
			for (int i = 0; i < w * h; i++)
			{
				for (int k = 0; k < 3; k++)
				{
					outImg.Data[i * 3 + k] = parts[k] == null ? neutral[k] : parts[k].Data[i];
				}
			}
			return outImg;
		}
		case Map.Roughness:
		case Map.AO:
		case Map.Metallic:
		{
			FloatImage single = Get(map);
			if (single != null)
			{
				return Fit(single, w, h, 1);
			}
			FloatImage orm = Get(Map.ORM);
			if (orm == null)
			{
				return null;
			}
			int channel = map == Map.AO ? 0 : (map == Map.Roughness ? 1 : 2);
			FloatImage o = Fit(orm, w, h, 3);
			FloatImage outImg = new FloatImage(w, h, 1);
			for (int i = 0; i < w * h; i++)
			{
				outImg.Data[i] = o.Data[i * 3 + channel];
			}
			return outImg;
		}
		default:
		{
			FloatImage src = Get(map);
			return src == null ? null : Fit(src, w, h, 1);
		}
		}
	}

	// Resizes and converts to the wanted channel count (1 = grey, 3 = RGB, 4 = RGBA).
	private static FloatImage Fit(FloatImage src, int w, int h, int channels)
	{
		FloatImage r = src.Width == w && src.Height == h ? src : src.Resize(w, h);
		if (r.Channels == channels)
		{
			return r;
		}
		if (channels == 1)
		{
			return r.ToGray();
		}
		FloatImage o = new FloatImage(w, h, channels);
		int sc = r.Channels;
		for (int i = 0; i < w * h; i++)
		{
			for (int k = 0; k < channels; k++)
			{
				o.Data[i * channels + k] = k < 3 ? r.Data[i * sc + Math.Min(k, sc - 1)] : (sc == 4 ? r.Data[i * sc + 3] : 1f);
			}
		}
		return o;
	}

	// ---------------------------------------------------------------- export

	public static void SavePng(Canvas canvas, string path, bool sixteenBit)
	{
		int c = canvas.Channels;
		PngIO.WriteRows(path, canvas.W, canvas.H, c, sixteenBit, (y, row) =>
		{
			long s = (long)y * canvas.W * c;
			for (int i = 0; i < canvas.W * c; i++)
			{
				row[i] = canvas.Data[s + i] / 65535f;
			}
		});
	}

	public static void SaveTga(Canvas canvas, string path)
	{
		int w = canvas.W;
		int h = canvas.H;
		int c = canvas.Channels;
		using FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write);
		using BinaryWriter bw = new BinaryWriter(fs);
		bool gray = c == 1;
		int bits = gray ? 8 : (c == 4 ? 32 : 24);
		bw.Write((byte)0);
		bw.Write((byte)0);
		bw.Write((byte)(gray ? 3 : 2));
		bw.Write(new byte[5]);
		bw.Write((short)0);
		bw.Write((short)0);
		bw.Write((short)w);
		bw.Write((short)h);
		bw.Write((byte)bits);
		bw.Write((byte)(0x20 | (c == 4 ? 8 : 0)));
		int bpp = bits / 8;
		byte[] row = new byte[w * bpp];
		for (int y = 0; y < h; y++)
		{
			long s = (long)y * w * c;
			for (int x = 0; x < w; x++)
			{
				long p = s + (long)x * c;
				if (gray)
				{
					row[x] = (byte)(canvas.Data[p] >> 8);
				}
				else
				{
					row[x * bpp] = (byte)(canvas.Data[p + 2] >> 8);
					row[x * bpp + 1] = (byte)(canvas.Data[p + 1] >> 8);
					row[x * bpp + 2] = (byte)(canvas.Data[p] >> 8);
					if (bpp == 4)
					{
						row[x * bpp + 3] = (byte)(canvas.Data[p + 3] >> 8);
					}
				}
			}
			bw.Write(row);
		}
	}

	// Layout for remapping UVs: per material its pixel rectangle, and the scale and offset that move
	// its 0..1 UVs into the atlas (V up, as in Maya, Blender and most engines: v' = v * scale + offset).
	public static string LayoutJson(List<Entry> entries, string atlasName, int atlasW, int atlasH, int padding, IEnumerable<Map> maps, string extension)
	{
		CultureInfo inv = CultureInfo.InvariantCulture;
		StringBuilder sb = new StringBuilder();
		sb.Append("{\n");
		sb.Append("  \"atlas\": \"").Append(Escape(atlasName)).Append("\",\n");
		sb.Append("  \"width\": ").Append(atlasW).Append(",\n");
		sb.Append("  \"height\": ").Append(atlasH).Append(",\n");
		sb.Append("  \"padding\": ").Append(padding).Append(",\n");
		sb.Append("  \"textures\": {");
		bool first = true;
		foreach (Map m in maps)
		{
			sb.Append(first ? "\n" : ",\n").Append("    \"").Append(Suffix(m).ToLowerInvariant()).Append("\": \"").Append(Escape(atlasName + "_" + Suffix(m) + extension)).Append("\"");
			first = false;
		}
		sb.Append("\n  },\n");
		sb.Append("  \"materials\": [");
		first = true;
		foreach (Entry e in entries)
		{
			if (!e.Placed)
			{
				continue;
			}
			double sx = (double)e.W / atlasW;
			double sy = (double)e.H / atlasH;
			double ox = (double)e.X / atlasW;
			double oy = 1.0 - (double)(e.Y + e.H) / atlasH;
			sb.Append(first ? "\n" : ",\n");
			first = false;
			sb.Append("    { \"name\": \"").Append(Escape(e.Name)).Append("\", ");
			sb.Append("\"x\": ").Append(e.X).Append(", \"y\": ").Append(e.Y).Append(", \"w\": ").Append(e.W).Append(", \"h\": ").Append(e.H).Append(", ");
			sb.Append("\"uvScale\": [").Append(sx.ToString("0.########", inv)).Append(", ").Append(sy.ToString("0.########", inv)).Append("], ");
			sb.Append("\"uvOffset\": [").Append(ox.ToString("0.########", inv)).Append(", ").Append(oy.ToString("0.########", inv)).Append("] }");
		}
		sb.Append("\n  ]\n}\n");
		return sb.ToString();
	}

	private static string Escape(string s)
	{
		return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
	}
}
