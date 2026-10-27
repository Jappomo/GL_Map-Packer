using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GLMapPacker;

// PBR texture validation and correction (metal/roughness workflow).
// Checks: dielectric albedo brightness in a legal sRGB range, metal albedo reflectance above a
// minimum, and roughness inside a range. Fixes: smooth albedo compression towards the legal range,
// per-channel remapping of AO / roughness / metallic, and normal-map intensity.
public static class PbrCore
{
	public enum Slot
	{
		Albedo,
		Normal,
		ORM,
		Roughness,
		Metallic,
		AO
	}

	[Flags]
	public enum Issue : byte
	{
		None = 0,
		TooDark = 1,
		TooBright = 2,
		MetalLow = 4,
		RoughLow = 8,
		RoughHigh = 16
	}

	// Output remap for one greyscale channel: mid-grey shift (a gamma around 0.5), contrast around
	// 0.5, then the 0..1 result is mapped into Min..Max.
	public sealed class ChannelAdjust
	{
		public float Min;

		public float Max = 1f;

		public float Mid;

		public float Contrast;

		public bool IsIdentity => Min == 0f && Max == 1f && Mid == 0f && Contrast == 0f;

		public ChannelAdjust Clone()
		{
			return (ChannelAdjust)MemberwiseClone();
		}

		public float Apply(float v)
		{
			v = v < 0f ? 0f : (v > 1f ? 1f : v);
			if (Mid != 0f)
			{
				// Mid-grey 0.5 moves to 0.5 + Mid.
				double gamma = Math.Log(0.5 + Mid) / Math.Log(0.5);
				v = (float)Math.Pow(v, gamma);
			}
			if (Contrast != 0f)
			{
				// -1..1 maps to a slope of about 0.4..2.4 around mid-grey.
				float k = (float)Math.Tan((Contrast * 0.5 + 1.0) * Math.PI / 4.0);
				v = 0.5f + (v - 0.5f) * k;
				v = v < 0f ? 0f : (v > 1f ? 1f : v);
			}
			return Min + v * (Max - Min);
		}
	}

	public sealed class Settings
	{
		public bool CheckAlbedo = true;

		// Dielectric albedo brightness range, in sRGB 0..255.
		public float AlbedoMin = 30f;

		public float AlbedoMax = 240f;

		public bool CheckMetal = true;

		// Minimum metal reflectance, in percent of sRGB white (70% = 179).
		public float MetalMinPercent = 70f;

		public bool CheckRoughness = true;

		public float RoughMin = 0.05f;

		public float RoughMax = 1f;

		// Texels with metallic at or above this count as metal.
		public float MetalMask = 0.5f;

		// Albedo fixes, 0..1: how far out-of-range texels move towards the legal range.
		public float FixDarks;

		public float FixBrights;

		public float FixMetal;

		public ChannelAdjust AO = new ChannelAdjust();

		public ChannelAdjust Rough = new ChannelAdjust();

		public ChannelAdjust Metal = new ChannelAdjust();

		public float NormalIntensity = 1f;

		public Settings Clone()
		{
			Settings s = (Settings)MemberwiseClone();
			s.AO = AO.Clone();
			s.Rough = Rough.Clone();
			s.Metal = Metal.Clone();
			return s;
		}

		public void ResetFixes()
		{
			FixDarks = FixBrights = FixMetal = 0f;
			AO = new ChannelAdjust();
			Rough = new ChannelAdjust();
			Metal = new ChannelAdjust();
			NormalIntensity = 1f;
		}
	}

	public sealed class Stats
	{
		// Share of checked texels failing each check, 0..1.
		public double TooDark;

		public double TooBright;

		public double MetalLow;

		public double RoughOut;

		public bool Any => TooDark > 0 || TooBright > 0 || MetalLow > 0 || RoughOut > 0;
	}

	// All maps resampled to one size, as float channels (albedo and normal as 0..1 RGB, sRGB for
	// albedo). Missing maps are null.
	public sealed class Working
	{
		public int W;

		public int H;

		public float[] Albedo;

		public float[] Normal;

		public float[] AO;

		public float[] Rough;

		public float[] Metal;
	}

	// Size of reference for a set: the albedo, else the first map.
	public static bool ReferenceSize(IDictionary<Slot, FloatImage> maps, out int w, out int h)
	{
		FloatImage r = null;
		if (!maps.TryGetValue(Slot.Albedo, out r))
		{
			foreach (FloatImage m in maps.Values)
			{
				r = m;
				break;
			}
		}
		w = r?.Width ?? 0;
		h = r?.Height ?? 0;
		return r != null;
	}

	public static Working Build(IDictionary<Slot, FloatImage> maps, int w, int h)
	{
		return new Working
		{
			W = w,
			H = h,
			Albedo = maps.TryGetValue(Slot.Albedo, out var a) ? Rgb(a, w, h) : null,
			Normal = maps.TryGetValue(Slot.Normal, out var n) ? Rgb(n, w, h) : null,
			AO = Channel(maps, Slot.AO, w, h),
			Rough = Channel(maps, Slot.Roughness, w, h),
			Metal = Channel(maps, Slot.Metallic, w, h)
		};
	}

	// A greyscale channel from its own map, or from the ORM (R = AO, G = roughness, B = metallic).
	public static float[] Channel(IDictionary<Slot, FloatImage> maps, Slot slot, int w, int h)
	{
		if (maps.TryGetValue(slot, out var single))
		{
			FloatImage g = Sized(single, w, h).ToGray();
			return g.Data;
		}
		if (maps.TryGetValue(Slot.ORM, out var orm))
		{
			FloatImage o = Sized(orm, w, h);
			int ch = slot == Slot.AO ? 0 : (slot == Slot.Roughness ? 1 : 2);
			int c = o.Channels;
			float[] r = new float[w * h];
			for (int i = 0; i < r.Length; i++)
			{
				r[i] = o.Data[i * c + Math.Min(ch, c - 1)];
			}
			return r;
		}
		return null;
	}

	private static FloatImage Sized(FloatImage img, int w, int h)
	{
		return img.Width == w && img.Height == h ? img : img.Resize(w, h);
	}

	private static float[] Rgb(FloatImage img, int w, int h)
	{
		FloatImage s = Sized(img, w, h);
		int c = s.Channels;
		float[] r = new float[w * h * 3];
		for (int i = 0; i < w * h; i++)
		{
			r[i * 3] = s.Data[i * c];
			r[i * 3 + 1] = s.Data[i * c + Math.Min(1, c - 1)];
			r[i * 3 + 2] = s.Data[i * c + Math.Min(2, c - 1)];
		}
		return r;
	}

	// ---------------------------------------------------------------- colour helpers

	private static readonly float[] ToLinearLut = BuildLinearLut();

	private static float[] BuildLinearLut()
	{
		float[] t = new float[4097];
		for (int i = 0; i <= 4096; i++)
		{
			double v = i / 4096.0;
			t[i] = (float)(v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4));
		}
		return t;
	}

	public static float ToLinear(float s)
	{
		s = s < 0f ? 0f : (s > 1f ? 1f : s);
		float f = s * 4096f;
		int i = (int)f;
		if (i >= 4096)
		{
			return 1f;
		}
		return ToLinearLut[i] + (ToLinearLut[i + 1] - ToLinearLut[i]) * (f - i);
	}

	public static float ToSrgb(float l)
	{
		l = l < 0f ? 0f : (l > 1f ? 1f : l);
		return (float)(l <= 0.0031308 ? l * 12.92 : 1.055 * Math.Pow(l, 1.0 / 2.4) - 0.055);
	}

	// Brightness of an sRGB colour, in sRGB 0..255 (luminance computed in linear light).
	public static float Brightness(float r, float g, float b)
	{
		float lum = 0.2126f * ToLinear(r) + 0.7152f * ToLinear(g) + 0.0722f * ToLinear(b);
		return ToSrgb(lum) * 255f;
	}

	// ---------------------------------------------------------------- fixing

	// Width of the soft zone (sRGB units) over which corrected values ease into the legal range,
	// so gradients stay smooth instead of clipping flat.
	private const float Knee = 16f;

	public static Working Fix(Working src, Settings s)
	{
		Working o = new Working
		{
			W = src.W,
			H = src.H,
			AO = AdjustChannel(src.AO, s.AO),
			Rough = AdjustChannel(src.Rough, s.Rough),
			Metal = AdjustChannel(src.Metal, s.Metal),
			Normal = src.Normal == null ? null : FixNormal(src.Normal, s.NormalIntensity)
		};
		o.Albedo = src.Albedo == null ? null : FixAlbedo(src.Albedo, o.Metal, s);
		return o;
	}

	public static float[] AdjustChannel(float[] src, ChannelAdjust a)
	{
		if (src == null)
		{
			return null;
		}
		if (a.IsIdentity)
		{
			return src;
		}
		float[] r = new float[src.Length];
		Parallel.For(0, src.Length / 4096 + 1, block =>
		{
			int end = Math.Min(src.Length, (block + 1) * 4096);
			for (int i = block * 4096; i < end; i++)
			{
				r[i] = a.Apply(src[i]);
			}
		});
		return r;
	}

	public static float[] FixNormal(float[] src, float intensity)
	{
		if (intensity == 1f)
		{
			return src;
		}
		float[] r = new float[src.Length];
		int n = src.Length / 3;
		Parallel.For(0, n / 4096 + 1, block =>
		{
			int end = Math.Min(n, (block + 1) * 4096);
			for (int i = block * 4096; i < end; i++)
			{
				float x = (src[i * 3] * 2f - 1f) * intensity;
				float y = (src[i * 3 + 1] * 2f - 1f) * intensity;
				float z = Math.Max(src[i * 3 + 2] * 2f - 1f, 0f);
				float len = (float)Math.Sqrt(x * x + y * y + z * z);
				if (len < 1e-6f)
				{
					x = y = 0f;
					z = len = 1f;
				}
				r[i * 3] = x / len * 0.5f + 0.5f;
				r[i * 3 + 1] = y / len * 0.5f + 0.5f;
				r[i * 3 + 2] = z / len * 0.5f + 0.5f;
			}
		});
		return r;
	}

	// Moves too dark / too bright dielectric texels and too dark metal texels towards the legal
	// range, keeping their hue (the linear colour is scaled, not shifted).
	public static float[] FixAlbedo(float[] src, float[] metal, Settings s)
	{
		if (s.FixDarks <= 0f && s.FixBrights <= 0f && s.FixMetal <= 0f)
		{
			return src;
		}
		float[] r = new float[src.Length];
		int n = src.Length / 3;
		float metalMin = s.MetalMinPercent * 2.55f;
		Parallel.For(0, n / 4096 + 1, block =>
		{
			int end = Math.Min(n, (block + 1) * 4096);
			for (int i = block * 4096; i < end; i++)
			{
				float sr = src[i * 3];
				float sg = src[i * 3 + 1];
				float sb = src[i * 3 + 2];
				float l = Brightness(sr, sg, sb);
				bool isMetal = metal != null && metal[i] >= s.MetalMask;
				float target = l;
				if (isMetal)
				{
					if (s.FixMetal > 0f)
					{
						target = RaiseDarks(l, metalMin, s.FixMetal);
					}
				}
				else
				{
					if (s.FixDarks > 0f)
					{
						target = RaiseDarks(target, s.AlbedoMin, s.FixDarks);
					}
					if (s.FixBrights > 0f)
					{
						target = LowerBrights(target, s.AlbedoMax, s.FixBrights);
					}
				}
				if (Math.Abs(target - l) < 1e-4f)
				{
					r[i * 3] = sr;
					r[i * 3 + 1] = sg;
					r[i * 3 + 2] = sb;
					continue;
				}
				float lr = ToLinear(sr);
				float lg = ToLinear(sg);
				float lb = ToLinear(sb);
				float lum = 0.2126f * lr + 0.7152f * lg + 0.0722f * lb;
				float want = ToLinear(target / 255f);
				if (lum < 1e-5f)
				{
					lr = lg = lb = want;
				}
				else
				{
					float k = want / lum;
					lr *= k;
					lg *= k;
					lb *= k;
					// A saturated channel can't go above 1: clamp, then blend towards white just enough
					// to still reach the wanted brightness.
					if (lr > 1f || lg > 1f || lb > 1f)
					{
						lr = Math.Min(1f, lr);
						lg = Math.Min(1f, lg);
						lb = Math.Min(1f, lb);
						float have = 0.2126f * lr + 0.7152f * lg + 0.0722f * lb;
						if (want > have && have < 1f)
						{
							float t = (want - have) / (1f - have);
							lr += (1f - lr) * t;
							lg += (1f - lg) * t;
							lb += (1f - lb) * t;
						}
					}
				}
				r[i * 3] = ToSrgb(lr);
				r[i * 3 + 1] = ToSrgb(lg);
				r[i * 3 + 2] = ToSrgb(lb);
			}
		});
		return r;
	}

	// Brightness below min + Knee is remapped onto [min, min + Knee] (continuous, keeps order).
	private static float RaiseDarks(float l, float min, float amount)
	{
		float top = min + Knee;
		if (l >= top)
		{
			return l;
		}
		float f = min + Knee * Math.Max(0f, l) / top;
		return l + (f - l) * amount;
	}

	private static float LowerBrights(float l, float max, float amount)
	{
		float bottom = max - Knee;
		if (l <= bottom)
		{
			return l;
		}
		float f = max - Knee * Math.Max(0f, 255f - l) / (255f - bottom);
		return l + (f - l) * amount;
	}

	// ---------------------------------------------------------------- validation

	// Per-texel issues for the given (fixed) maps, plus the share of texels failing each check.
	public static Issue[] Validate(Working w, Settings s, out Stats stats)
	{
		int n = w.W * w.H;
		Issue[] flags = new Issue[n];
		float metalMin = s.MetalMinPercent * 2.55f;
		const float tol = 0.5f;
		Parallel.For(0, n / 4096 + 1, block =>
		{
			int end = Math.Min(n, (block + 1) * 4096);
			for (int i = block * 4096; i < end; i++)
			{
				Issue f = Issue.None;
				bool isMetal = w.Metal != null && w.Metal[i] >= s.MetalMask;
				if (w.Albedo != null && (s.CheckAlbedo || s.CheckMetal))
				{
					float l = Brightness(w.Albedo[i * 3], w.Albedo[i * 3 + 1], w.Albedo[i * 3 + 2]);
					if (isMetal)
					{
						if (s.CheckMetal && l < metalMin - tol)
						{
							f |= Issue.MetalLow;
						}
					}
					else if (s.CheckAlbedo)
					{
						if (l < s.AlbedoMin - tol)
						{
							f |= Issue.TooDark;
						}
						else if (l > s.AlbedoMax + tol)
						{
							f |= Issue.TooBright;
						}
					}
				}
				if (w.Rough != null && s.CheckRoughness)
				{
					float r = w.Rough[i];
					if (r < s.RoughMin - 0.002f)
					{
						f |= Issue.RoughLow;
					}
					else if (r > s.RoughMax + 0.002f)
					{
						f |= Issue.RoughHigh;
					}
				}
				flags[i] = f;
			}
		});
		long dark = 0;
		long bright = 0;
		long metalLow = 0;
		long rough = 0;
		long metals = 0;
		for (int i = 0; i < n; i++)
		{
			Issue f = flags[i];
			if ((f & Issue.TooDark) != 0)
			{
				dark++;
			}
			if ((f & Issue.TooBright) != 0)
			{
				bright++;
			}
			if ((f & Issue.MetalLow) != 0)
			{
				metalLow++;
			}
			if ((f & (Issue.RoughLow | Issue.RoughHigh)) != 0)
			{
				rough++;
			}
			if (w.Metal != null && w.Metal[i] >= s.MetalMask)
			{
				metals++;
			}
		}
		long dielectrics = n - metals;
		stats = new Stats
		{
			TooDark = dielectrics > 0 ? (double)dark / dielectrics : 0,
			TooBright = dielectrics > 0 ? (double)bright / dielectrics : 0,
			MetalLow = metals > 0 ? (double)metalLow / metals : 0,
			RoughOut = w.Rough != null ? (double)rough / n : 0
		};
		return flags;
	}

	// ---------------------------------------------------------------- auto-correct

	// Sets the fixes so that every enabled check passes, with the smallest change for each: albedo
	// fixes only for the problems found, roughness remapped into its range only on the side that's
	// out. Returns a short description of what changed (empty when everything already passes).
	public static List<string> AutoCorrect(Working original, Settings s)
	{
		List<string> changes = new List<string>();
		Working current = Fix(original, s);
		Validate(current, s, out var stats);
		if (s.CheckAlbedo && stats.TooDark > 0 && s.FixDarks < 1f)
		{
			s.FixDarks = 1f;
			changes.Add("raised " + Pct(stats.TooDark) + " too-dark albedo");
		}
		if (s.CheckAlbedo && stats.TooBright > 0 && s.FixBrights < 1f)
		{
			s.FixBrights = 1f;
			changes.Add("lowered " + Pct(stats.TooBright) + " too-bright albedo");
		}
		if (s.CheckMetal && stats.MetalLow > 0 && s.FixMetal < 1f)
		{
			s.FixMetal = 1f;
			changes.Add("brightened " + Pct(stats.MetalLow) + " of metal reflectance");
		}
		if (s.CheckRoughness && current.Rough != null && stats.RoughOut > 0)
		{
			float lo = float.MaxValue;
			float hi = float.MinValue;
			foreach (float v in current.Rough)
			{
				lo = Math.Min(lo, v);
				hi = Math.Max(hi, v);
			}
			if (lo < s.RoughMin)
			{
				s.Rough.Min = Math.Max(s.Rough.Min, s.RoughMin);
			}
			if (hi > s.RoughMax)
			{
				s.Rough.Max = Math.Min(s.Rough.Max, s.RoughMax);
			}
			changes.Add("remapped roughness into " + s.Rough.Min.ToString("0.00") + "–" + s.Rough.Max.ToString("0.00"));
		}
		return changes;
	}

	private static string Pct(double v)
	{
		return (v * 100.0 < 0.1 ? "<0.1" : (v * 100.0).ToString("0.#")) + "%";
	}

	// ---------------------------------------------------------------- export

	// One fixed map at its own resolution (the metal mask for albedo fixes is resampled to fit).
	public static FloatImage FixedMap(IDictionary<Slot, FloatImage> maps, Slot slot, Settings s)
	{
		FloatImage src = maps[slot];
		int w = src.Width;
		int h = src.Height;
		switch (slot)
		{
		case Slot.Albedo:
		{
			float[] metal = AdjustChannel(Channel(maps, Slot.Metallic, w, h), s.Metal);
			float[] fixedRgb = FixAlbedo(Rgb(src, w, h), metal, s);
			FloatImage o = new FloatImage(w, h, src.Channels == 4 ? 4 : 3);
			for (int i = 0; i < w * h; i++)
			{
				o.Data[i * o.Channels] = fixedRgb[i * 3];
				o.Data[i * o.Channels + 1] = fixedRgb[i * 3 + 1];
				o.Data[i * o.Channels + 2] = fixedRgb[i * 3 + 2];
				if (o.Channels == 4)
				{
					o.Data[i * 4 + 3] = src.Data[i * 4 + 3];
				}
			}
			return o;
		}
		case Slot.Normal:
		{
			float[] n = FixNormal(Rgb(src, w, h), s.NormalIntensity);
			FloatImage o = new FloatImage(w, h, 3);
			Array.Copy(n, o.Data, n.Length);
			return o;
		}
		case Slot.ORM:
		{
			FloatImage o = new FloatImage(w, h, 3);
			ChannelAdjust[] adj = new ChannelAdjust[3] { s.AO, s.Rough, s.Metal };
			int c = src.Channels;
			for (int i = 0; i < w * h; i++)
			{
				for (int k = 0; k < 3; k++)
				{
					o.Data[i * 3 + k] = adj[k].Apply(src.Data[i * c + Math.Min(k, c - 1)]);
				}
			}
			return o;
		}
		default:
		{
			ChannelAdjust a = slot == Slot.AO ? s.AO : (slot == Slot.Roughness ? s.Rough : s.Metal);
			float[] g = AdjustChannel(src.ToGray().Data, a);
			FloatImage o = new FloatImage(w, h, 1);
			Array.Copy(g, o.Data, g.Length);
			return o;
		}
		}
	}
}
