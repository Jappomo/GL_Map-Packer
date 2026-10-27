using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace GLMapPacker;

// Minimal PNG codec that keeps full 16-bit precision (GDI+ reads 16-bit PNGs as 8-bit and cannot write them).
public static class PngIO
{
	private static readonly byte[] Signature = new byte[8] { 137, 80, 78, 71, 13, 10, 26, 10 };

	private static readonly uint[] CrcTable = BuildCrcTable();

	// Returns an RGBA float image, or null for PNG variants this reader does not handle
	// (palette, sub-8-bit, interlaced), so the caller can fall back to GDI+.
	public static FloatImage Read(string path)
	{
		byte[] file = File.ReadAllBytes(path);
		if (file.Length < 8)
		{
			return null;
		}
		for (int i = 0; i < 8; i++)
		{
			if (file[i] != Signature[i])
			{
				return null;
			}
		}
		int width = 0;
		int height = 0;
		int depth = 0;
		int colorType = 0;
		int interlace = 0;
		MemoryStream idat = new MemoryStream();
		int pos = 8;
		while (pos + 8 <= file.Length)
		{
			int len = ReadInt32BE(file, pos);
			string type = Encoding.ASCII.GetString(file, pos + 4, 4);
			int dataPos = pos + 8;
			if (len < 0 || dataPos + len > file.Length)
			{
				break;
			}
			if (type == "IHDR")
			{
				width = ReadInt32BE(file, dataPos);
				height = ReadInt32BE(file, dataPos + 4);
				depth = file[dataPos + 8];
				colorType = file[dataPos + 9];
				interlace = file[dataPos + 12];
			}
			else if (type == "IDAT")
			{
				idat.Write(file, dataPos, len);
			}
			else if (type == "IEND")
			{
				break;
			}
			pos = dataPos + len + 4;
		}
		int channels = colorType switch
		{
			0 => 1,
			2 => 3,
			4 => 2,
			6 => 4,
			_ => 0,
		};
		if (width <= 0 || height <= 0 || channels == 0 || (depth != 8 && depth != 16) || interlace != 0)
		{
			return null;
		}
		int bytesPerSample = depth / 8;
		int bpp = channels * bytesPerSample;
		int rowBytes = width * bpp;
		byte[] raw = Inflate(idat.ToArray(), (rowBytes + 1) * height);
		byte[] prev = new byte[rowBytes];
		byte[] cur = new byte[rowBytes];
		FloatImage img = new FloatImage(width, height, 4);
		img.SourceBitDepth = depth;
		float scale = depth == 16 ? 1f / 65535f : 1f / 255f;
		for (int y = 0; y < height; y++)
		{
			int rowStart = y * (rowBytes + 1);
			int filter = raw[rowStart];
			Buffer.BlockCopy(raw, rowStart + 1, cur, 0, rowBytes);
			Unfilter(filter, cur, prev, bpp);
			int d = y * width * 4;
			for (int x = 0; x < width; x++, d += 4)
			{
				int s = x * bpp;
				float[] px = img.Data;
				for (int c = 0; c < channels; c++)
				{
					int o = s + c * bytesPerSample;
					int v = depth == 16 ? (cur[o] << 8) | cur[o + 1] : cur[o];
					float f = v * scale;
					switch (channels)
					{
					case 1:
						px[d] = px[d + 1] = px[d + 2] = f;
						px[d + 3] = 1f;
						break;
					case 2:
						if (c == 0)
						{
							px[d] = px[d + 1] = px[d + 2] = f;
						}
						else
						{
							px[d + 3] = f;
						}
						break;
					case 3:
						px[d + c] = f;
						px[d + 3] = 1f;
						break;
					default:
						px[d + c] = f;
						break;
					}
				}
			}
			byte[] t = prev;
			prev = cur;
			cur = t;
		}
		return img;
	}

	// Writes a 1-channel image as grayscale, 3 as RGB, 4 as RGBA, at 8 or 16 bits per sample.
	public static void Write(string path, FloatImage img, bool sixteenBit)
	{
		int rowLen = img.Width * img.Channels;
		WriteRows(path, img.Width, img.Height, img.Channels, sixteenBit, (y, row) => Array.Copy(img.Data, (long)y * rowLen, row, 0, rowLen));
	}

	// Streams the image a row at a time: fillRow(y, row) fills `row` with width * channels values 0..1.
	public static void WriteRows(string path, int width, int height, int channels, bool sixteenBit, Action<int, float[]> fillRow)
	{
		int colorType = channels switch
		{
			1 => 0,
			3 => 2,
			4 => 6,
			_ => throw new ArgumentException("Unsupported channel count: " + channels),
		};
		int bytesPerSample = sixteenBit ? 2 : 1;
		int bpp = channels * bytesPerSample;
		int rowBytes = width * bpp;
		float[] values = new float[width * channels];
		using FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write);
		fs.Write(Signature, 0, Signature.Length);
		byte[] ihdr = new byte[13];
		WriteInt32BE(ihdr, 0, width);
		WriteInt32BE(ihdr, 4, height);
		ihdr[8] = (byte)(sixteenBit ? 16 : 8);
		ihdr[9] = (byte)colorType;
		WriteChunk(fs, "IHDR", ihdr, ihdr.Length);
		// The compressed data goes out in IDAT chunks as it is produced, so large images never sit
		// in memory whole.
		using (IdatStream idat = new IdatStream(fs))
		{
			idat.WriteByte(0x78);
			idat.WriteByte(0x9C);
			uint adlerA = 1;
			uint adlerB = 0;
			using (DeflateStream deflate = new DeflateStream(idat, CompressionLevel.Optimal, leaveOpen: true))
			{
				byte[] prev = new byte[rowBytes];
				byte[] cur = new byte[rowBytes];
				byte[] filtered = new byte[rowBytes + 1];
				for (int y = 0; y < height; y++)
				{
					fillRow(y, values);
					for (int i = 0; i < values.Length; i++)
					{
						float v = values[i];
						v = v < 0f ? 0f : (v > 1f ? 1f : v);
						if (sixteenBit)
						{
							int q = (int)(v * 65535f + 0.5f);
							cur[i * 2] = (byte)(q >> 8);
							cur[i * 2 + 1] = (byte)q;
						}
						else
						{
							cur[i] = (byte)(v * 255f + 0.5f);
						}
					}
					// Paeth filter on every row: smooth height data compresses well with it.
					filtered[0] = 4;
					for (int i = 0; i < rowBytes; i++)
					{
						int a = i >= bpp ? cur[i - bpp] : 0;
						int b = prev[i];
						int c = i >= bpp ? prev[i - bpp] : 0;
						filtered[i + 1] = (byte)(cur[i] - Paeth(a, b, c));
					}
					deflate.Write(filtered, 0, filtered.Length);
					for (int i = 0; i < filtered.Length; i++)
					{
						adlerA = (adlerA + filtered[i]) % 65521;
						adlerB = (adlerB + adlerA) % 65521;
					}
					byte[] t = prev;
					prev = cur;
					cur = t;
				}
			}
			uint adler = (adlerB << 16) | adlerA;
			idat.WriteByte((byte)(adler >> 24));
			idat.WriteByte((byte)(adler >> 16));
			idat.WriteByte((byte)(adler >> 8));
			idat.WriteByte((byte)adler);
		}
		WriteChunk(fs, "IEND", new byte[0], 0);
	}

	// Buffers written bytes and emits them as IDAT chunks of up to 256 KB.
	private sealed class IdatStream : Stream
	{
		private readonly Stream target;

		private readonly MemoryStream buffer = new MemoryStream();

		public IdatStream(Stream target)
		{
			this.target = target;
		}

		public override bool CanRead => false;

		public override bool CanSeek => false;

		public override bool CanWrite => true;

		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get
			{
				throw new NotSupportedException();
			}
			set
			{
				throw new NotSupportedException();
			}
		}

		public override void Write(byte[] data, int offset, int count)
		{
			buffer.Write(data, offset, count);
			if (buffer.Length >= 262144)
			{
				Flush();
			}
		}

		public override void Flush()
		{
			if (buffer.Length > 0)
			{
				WriteChunk(target, "IDAT", buffer.GetBuffer(), (int)buffer.Length);
				buffer.SetLength(0);
			}
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				Flush();
			}
			base.Dispose(disposing);
		}

		public override int Read(byte[] data, int offset, int count)
		{
			throw new NotSupportedException();
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			throw new NotSupportedException();
		}

		public override void SetLength(long value)
		{
			throw new NotSupportedException();
		}
	}

	private static byte[] Inflate(byte[] zlib, int expected)
	{
		byte[] outBuf = new byte[expected];
		using MemoryStream ms = new MemoryStream(zlib, 2, zlib.Length - 2);
		using DeflateStream ds = new DeflateStream(ms, CompressionMode.Decompress);
		int total = 0;
		while (total < expected)
		{
			int n = ds.Read(outBuf, total, expected - total);
			if (n <= 0)
			{
				throw new InvalidDataException("PNG image data is truncated.");
			}
			total += n;
		}
		return outBuf;
	}

	private static void Unfilter(int filter, byte[] cur, byte[] prev, int bpp)
	{
		int n = cur.Length;
		switch (filter)
		{
		case 0:
			break;
		case 1:
			for (int i = bpp; i < n; i++)
			{
				cur[i] = (byte)(cur[i] + cur[i - bpp]);
			}
			break;
		case 2:
			for (int i = 0; i < n; i++)
			{
				cur[i] = (byte)(cur[i] + prev[i]);
			}
			break;
		case 3:
			for (int i = 0; i < n; i++)
			{
				int a = i >= bpp ? cur[i - bpp] : 0;
				cur[i] = (byte)(cur[i] + ((a + prev[i]) >> 1));
			}
			break;
		case 4:
			for (int i = 0; i < n; i++)
			{
				int a = i >= bpp ? cur[i - bpp] : 0;
				int c = i >= bpp ? prev[i - bpp] : 0;
				cur[i] = (byte)(cur[i] + Paeth(a, prev[i], c));
			}
			break;
		default:
			throw new InvalidDataException("Unknown PNG filter type " + filter + ".");
		}
	}

	private static int Paeth(int a, int b, int c)
	{
		int p = a + b - c;
		int pa = Math.Abs(p - a);
		int pb = Math.Abs(p - b);
		int pc = Math.Abs(p - c);
		if (pa <= pb && pa <= pc)
		{
			return a;
		}
		return pb <= pc ? b : c;
	}

	private static void WriteChunk(Stream s, string type, byte[] data, int length)
	{
		byte[] head = new byte[8];
		WriteInt32BE(head, 0, length);
		Encoding.ASCII.GetBytes(type, 0, 4, head, 4);
		s.Write(head, 0, 8);
		s.Write(data, 0, length);
		uint crc = 0xFFFFFFFFu;
		for (int i = 4; i < 8; i++)
		{
			crc = CrcTable[(crc ^ head[i]) & 0xFF] ^ (crc >> 8);
		}
		for (int i = 0; i < length; i++)
		{
			crc = CrcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
		}
		crc ^= 0xFFFFFFFFu;
		byte[] tail = new byte[4];
		WriteInt32BE(tail, 0, (int)crc);
		s.Write(tail, 0, 4);
	}

	private static uint[] BuildCrcTable()
	{
		uint[] table = new uint[256];
		for (uint n = 0; n < 256; n++)
		{
			uint c = n;
			for (int k = 0; k < 8; k++)
			{
				c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
			}
			table[n] = c;
		}
		return table;
	}

	private static int ReadInt32BE(byte[] b, int o)
	{
		return (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
	}

	private static void WriteInt32BE(byte[] b, int o, int v)
	{
		b[o] = (byte)(v >> 24);
		b[o + 1] = (byte)(v >> 16);
		b[o + 2] = (byte)(v >> 8);
		b[o + 3] = (byte)v;
	}
}
