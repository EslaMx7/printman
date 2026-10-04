using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Printman.Core.Abstractions;

namespace Printman.Services.Renderers;

public enum RasterColorSpace
{
    Gray,  // 0 = black, 255 = white
    Black, // 0 = white, 255 = black (CUPS K)
    Rgb
}

/// <summary>Location and geometry of one compressed raster page inside a file.</summary>
public sealed record RasterPage(long DataOffset, int Width, int Height, int BitsPerPixel, RasterColorSpace ColorSpace, int Dpi);

/// <summary>
/// Base class for driverless-printing raster formats (PWG Raster, Apple URF) that share the
/// CUPS PackBits-style line compression. Pages are indexed once, then decoded one at a time.
/// </summary>
public abstract class RasterDocumentRenderer : IDocumentRenderer
{
    private const int MaxDimension = 20000;
    private const long MaxPageBytes = 300L * 1024 * 1024;

    private readonly ConcurrentDictionary<string, (long Length, DateTime Modified, IReadOnlyList<RasterPage> Pages)> _index =
        new(StringComparer.OrdinalIgnoreCase);

    public abstract bool CanHandle(string fileExtension);

    /// <summary>Reads the format's file and page headers, skipping page data with <see cref="SkipPage"/>.</summary>
    protected abstract IReadOnlyList<RasterPage> IndexPages(Stream stream);

    public Task<int> GetPageCountAsync(string filePath) => Task.FromResult(GetPages(filePath).Count);

    public Task RenderPageAsync(string filePath, int pageNumber, Graphics graphics, Rectangle printableArea, int dpi, bool fitToPage)
    {
        var pages = GetPages(filePath);
        if (pageNumber < 1 || pageNumber > pages.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber),
                $"Requested page {pageNumber} is outside the document range (1 to {pages.Count}).");
        }

        var page = pages[pageNumber - 1];
        using var file = OpenRead(filePath);
        file.Position = page.DataOffset;
        using var bitmap = DecodePage(file, page);

        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // Graphics units are 1/100 inch; raster pages carry their own resolution
        float pageDpi = page.Dpi > 0 ? page.Dpi : 300;
        float naturalWidth = page.Width / pageDpi * 100f;
        float naturalHeight = page.Height / pageDpi * 100f;

        if (fitToPage)
        {
            float scale = Math.Min(printableArea.Width / naturalWidth, printableArea.Height / naturalHeight);
            float w = naturalWidth * scale;
            float h = naturalHeight * scale;
            graphics.DrawImage(bitmap,
                printableArea.X + (printableArea.Width - w) / 2,
                printableArea.Y + (printableArea.Height - h) / 2,
                w, h);
        }
        else
        {
            graphics.DrawImage(bitmap, printableArea.X, printableArea.Y, naturalWidth, naturalHeight);
        }

        return Task.CompletedTask;
    }

    private IReadOnlyList<RasterPage> GetPages(string filePath)
    {
        var info = new FileInfo(filePath);
        if (_index.TryGetValue(filePath, out var cached) && cached.Length == info.Length && cached.Modified == info.LastWriteTimeUtc)
        {
            return cached.Pages;
        }

        using var file = OpenRead(filePath);
        var pages = IndexPages(file);
        if (pages.Count == 0)
        {
            throw new InvalidDataException("Raster document contains no pages.");
        }

        _index[filePath] = (info.Length, info.LastWriteTimeUtc, pages);
        return pages;
    }

    private static BufferedStream OpenRead(string path) =>
        new(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1), 64 * 1024);

    // ---------------------------------------------------------------- Helpers for format implementations

    protected static RasterPage CreatePage(long offset, int width, int height, int bitsPerPixel, RasterColorSpace colorSpace, int dpi)
    {
        if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension)
        {
            throw new InvalidDataException($"Unsupported raster page size {width}x{height}.");
        }

        bool supported = (bitsPerPixel == 8 && colorSpace != RasterColorSpace.Rgb) ||
                         (bitsPerPixel == 24 && colorSpace == RasterColorSpace.Rgb);
        if (!supported)
        {
            throw new NotSupportedException($"Unsupported raster pixel format ({bitsPerPixel} bpp, {colorSpace}).");
        }

        if ((long)width * height * 3 > MaxPageBytes)
        {
            throw new InvalidDataException("Raster page is too large.");
        }

        return new RasterPage(offset, width, height, bitsPerPixel, colorSpace, dpi);
    }

    /// <summary>Advances past one page of compressed data.</summary>
    protected static void SkipPage(Stream stream, RasterPage page) => DecodeRows(stream, page, null);

    protected static bool TryReadExactly(Stream stream, byte[] buffer)
    {
        int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        if (read == 0) return false;
        if (read < buffer.Length) throw new InvalidDataException("Truncated raster header.");
        return true;
    }

    protected static int ReadBigEndian(byte[] data, int offset) =>
        (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));

    // ---------------------------------------------------------------- Decoding

    private static Bitmap DecodePage(Stream stream, RasterPage page)
    {
        var bitmap = new Bitmap(page.Width, page.Height, PixelFormat.Format24bppRgb);
        if (page.Dpi > 0)
        {
            bitmap.SetResolution(page.Dpi, page.Dpi);
        }

        var data = bitmap.LockBits(new Rectangle(0, 0, page.Width, page.Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            var bgr = new byte[page.Width * 3];
            DecodeRows(stream, page, (y, line, isNewLine) =>
            {
                if (isNewLine)
                {
                    ToBgr(line, bgr, page.ColorSpace);
                }
                Marshal.Copy(bgr, 0, data.Scan0 + (y * data.Stride), bgr.Length);
            });
        }
        catch
        {
            bitmap.UnlockBits(data);
            bitmap.Dispose();
            throw;
        }

        bitmap.UnlockBits(data);
        return bitmap;
    }

    /// <summary>
    /// CUPS raster compression: each line starts with a repeat count (n+1 copies); then control bytes:
    /// 0-127 repeat the next pixel n+1 times, 129-255 copy 257-n literal pixels, 128 clears to end of line.
    /// </summary>
    private static void DecodeRows(Stream stream, RasterPage page, Action<int, byte[], bool>? onRow)
    {
        int bytesPerPixel = page.BitsPerPixel / 8;
        int rowBytes = page.Width * bytesPerPixel;
        var line = new byte[rowBytes];
        var pixel = new byte[bytesPerPixel];
        byte blank = page.ColorSpace == RasterColorSpace.Black ? (byte)0x00 : (byte)0xFF;

        int y = 0;
        while (y < page.Height)
        {
            int repeat = ReadByte(stream) + 1;
            int x = 0;

            while (x < rowBytes)
            {
                int control = ReadByte(stream);
                if (control == 128)
                {
                    Array.Fill(line, blank, x, rowBytes - x);
                    x = rowBytes;
                }
                else if (control < 128)
                {
                    stream.ReadExactly(pixel);
                    for (int i = 0; i <= control && x < rowBytes; i++)
                    {
                        pixel.CopyTo(line, x);
                        x += bytesPerPixel;
                    }
                }
                else
                {
                    int bytes = (257 - control) * bytesPerPixel;
                    int take = Math.Min(bytes, rowBytes - x);
                    stream.ReadExactly(line, x, take);
                    if (bytes > take)
                    {
                        stream.ReadExactly(new byte[bytes - take]); // malformed overrun: discard
                    }
                    x += take;
                }
            }

            for (int r = 0; r < repeat && y < page.Height; r++, y++)
            {
                onRow?.Invoke(y, line, r == 0);
            }
        }
    }

    private static int ReadByte(Stream stream)
    {
        int b = stream.ReadByte();
        if (b < 0) throw new InvalidDataException("Truncated raster page data.");
        return b;
    }

    private static void ToBgr(byte[] line, byte[] bgr, RasterColorSpace colorSpace)
    {
        switch (colorSpace)
        {
            case RasterColorSpace.Rgb:
                for (int i = 0; i < line.Length; i += 3)
                {
                    bgr[i] = line[i + 2];
                    bgr[i + 1] = line[i + 1];
                    bgr[i + 2] = line[i];
                }
                break;
            case RasterColorSpace.Gray:
                for (int i = 0, j = 0; i < line.Length; i++, j += 3)
                {
                    bgr[j] = bgr[j + 1] = bgr[j + 2] = line[i];
                }
                break;
            case RasterColorSpace.Black:
                for (int i = 0, j = 0; i < line.Length; i++, j += 3)
                {
                    bgr[j] = bgr[j + 1] = bgr[j + 2] = (byte)(255 - line[i]);
                }
                break;
        }
    }
}
