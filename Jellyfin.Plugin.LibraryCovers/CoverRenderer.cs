using SkiaSharp;

namespace Jellyfin.Plugin.LibraryCovers;

/// <summary>Shape of the wall tiles.</summary>
public enum TileStyle
{
    /// <summary>Cropped 2:3 posters (movies, shows).</summary>
    Poster,

    /// <summary>Square artwork (music).</summary>
    Square,

    /// <summary>Channel logos laid on a card, never cropped (Live TV).</summary>
    Logo,
}

/// <summary>What to draw for one cover.</summary>
public sealed class CoverSpec
{
    public required string Title { get; init; }

    public required SKColor Accent { get; init; }

    public string? Subtitle { get; init; }

    public TileStyle Style { get; init; }

    /// <summary>Candidate images (local paths), best rated first.</summary>
    public required IReadOnlyList<string> ImagePaths { get; init; }

    public int Seed { get; init; }
}

/// <summary>
/// SkiaSharp drawing: tilted poster wall, midnight-blue fade, coloured glow, Unbounded title and item count.
/// </summary>
public static class CoverRenderer
{
    public const int Width = 1920;
    public const int Height = 1080;
    private const int MaxTiles = 24;

    private static readonly SKColor Bg = new(12, 18, 32);
    private static readonly Lazy<SKTypeface> TitleFace = new(() => LoadFont("Unbounded-Bold.ttf"));
    private static readonly Lazy<SKTypeface> SubFace = new(() => LoadFont("NotoSans-SemiBold.ttf"));

    public static byte[] Render(CoverSpec spec, int quality = 90)
    {
        var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info) ?? throw new InvalidOperationException("SKSurface.Create failed");
        var canvas = surface.Canvas;
        canvas.Clear(Bg);

        var rng = new Random(spec.Seed);
        var (pw, ph) = spec.Style switch
        {
            TileStyle.Square => (330f, 330f),
            TileStyle.Logo => (300f, 190f),
            _ => (270f, 405f),
        };
        var tiles = LoadTiles(spec.ImagePaths, rng, (int)pw, (int)ph, spec.Style == TileStyle.Logo);
        try
        {
            if (tiles.Count > 0)
            {
                DrawWall(canvas, tiles, rng, pw, ph, spec.Style);
            }
            else
            {
                // Empty library: a wall of tinted cards rather than a flat background.
                DrawPlaceholderWall(canvas, rng, pw, ph, spec.Accent);
            }

            using (var veil = new SKPaint { Color = Bg.WithAlpha(77) })
            {
                canvas.DrawRect(0, 0, Width, Height, veil);
            }

            DrawOverlays(canvas, spec.Accent);
            DrawTexts(canvas, spec);
        }
        finally
        {
            foreach (var t in tiles)
            {
                t.Dispose();
            }
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data.ToArray();
    }

    private static List<SKImage> LoadTiles(IReadOnlyList<string> paths, Random rng, int tw, int th, bool keepOrder)
    {
        // Deterministic shuffle (except logos, already sorted by channel number), then at most 24 decodable images.
        var shuffled = keepOrder ? paths.Distinct().ToList() : paths.Distinct().OrderBy(_ => rng.Next()).ToList();
        var result = new List<SKImage>();
        foreach (var p in shuffled)
        {
            if (result.Count >= MaxTiles)
            {
                break;
            }

            var img = LoadTile(p, tw, th);
            if (img is not null)
            {
                result.Add(img);
            }
        }

        return result;
    }

    private static SKImage? LoadTile(string path, int tw, int th)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            SKBitmap? bmp = null;
            using (var codec = SKCodec.Create(path))
            {
                if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
                {
                    return null;
                }

                float need = Math.Max((float)tw / codec.Info.Width, (float)th / codec.Info.Height) * 1.3f;
                var dims = codec.GetScaledDimensions(Math.Min(1f, need));
                var bi = new SKImageInfo(dims.Width, dims.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
                bmp = new SKBitmap(bi);
                var res = codec.GetPixels(bi, bmp.GetPixels());
                if (res != SKCodecResult.Success && res != SKCodecResult.IncompleteInput)
                {
                    bmp.Dispose();
                    bmp = SKBitmap.Decode(path);
                }
            }

            if (bmp is null)
            {
                return null;
            }

            using (bmp)
            {
                float scale = Math.Max((float)tw / bmp.Width, (float)th / bmp.Height) * 1.3f;
                if (scale < 0.75f)
                {
                    var target = new SKImageInfo(
                        Math.Max(1, (int)(bmp.Width * scale)),
                        Math.Max(1, (int)(bmp.Height * scale)),
                        SKColorType.Rgba8888,
                        SKAlphaType.Premul);
                    using var resized = bmp.Resize(target, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                    return resized is null ? null : SKImage.FromPixelCopy(resized.PeekPixels());
                }

                return SKImage.FromPixelCopy(bmp.PeekPixels());
            }
        }
        catch
        {
            return null;
        }
    }

    private static void DrawPlaceholderWall(SKCanvas c, Random rng, float pw, float ph, SKColor accent)
    {
        const float Gap = 26;
        using var paint = new SKPaint { IsAntialias = true };
        c.Save();
        c.Translate(1250, 540);
        c.RotateRadians(-0.17f);
        for (int col = -3; col <= 3; col++)
        {
            float off = (col % 2 == 0) ? 0 : ph / 2;
            for (int row = -3; row <= 2; row++)
            {
                float x = (col * (pw + Gap)) - (pw / 2);
                float y = (row * (ph + Gap)) + off - (ph / 2);
                var rect = new SKRect(x, y, x + pw, y + ph);
                byte a1 = (byte)rng.Next(26, 70);
                using var sh = SKShader.CreateLinearGradient(
                    new SKPoint(x, y),
                    new SKPoint(x + pw, y + ph),
                    new[] { accent.WithAlpha(a1), new SKColor(30, 41, 59, 200) },
                    null,
                    SKShaderTileMode.Clamp);
                paint.Shader = sh;
                c.DrawRoundRect(new SKRoundRect(rect, 18, 18), paint);
            }
        }

        paint.Shader = null;
        c.Restore();
    }

    /// <summary>Light logo (white text…) → dark card; dark logo → light card. Opaque logo → full frame.</summary>
    private static (bool Light, bool Opaque) AnalyzeLogo(SKImage im)
    {
        using var pix = im.PeekPixels();
        if (pix is null)
        {
            return (false, false);
        }

        double alphaSum = 0, lumaSum = 0;
        int n = 0;
        for (int sy = 0; sy < 20; sy++)
        {
            for (int sx = 0; sx < 30; sx++)
            {
                var col = pix.GetPixelColor((int)((sx + 0.5) * pix.Width / 30), (int)((sy + 0.5) * pix.Height / 20));
                double a = col.Alpha / 255.0;
                alphaSum += a;
                lumaSum += a * ((0.2126 * col.Red) + (0.7152 * col.Green) + (0.0722 * col.Blue)) / 255.0;
                n++;
            }
        }

        double alpha = alphaSum / n;
        double luma = alphaSum > 0 ? lumaSum / alphaSum : 0;
        return (luma > 0.62, alpha > 0.97);
    }

    private static void DrawWall(SKCanvas c, List<SKImage> imgs, Random rng, float pw, float ph, TileStyle style)
    {
        const float Gap = 26;
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        using var paint = new SKPaint { IsAntialias = true };
        using var card = new SKPaint { IsAntialias = true };
        var grid = new Dictionary<(int Col, int Row), int>();
        var logoInfo = style == TileStyle.Logo ? imgs.Select(AnalyzeLogo).ToList() : null;
        int k = 0;

        c.Save();
        c.Translate(1250, 540);
        c.RotateRadians(-0.17f);
        for (int col = -3; col <= 3; col++)
        {
            float off = (col % 2 == 0) ? 0 : ph / 2;
            for (int row = -3; row <= 2; row++)
            {
                // Keep a poster from touching its own duplicate.
                var avoid = new HashSet<int>();
                foreach (var key in new[] { (col - 1, row), (col, row - 1), (col - 1, row + 1), (col - 1, row - 1) })
                {
                    if (grid.TryGetValue(key, out var v))
                    {
                        avoid.Add(v);
                    }
                }

                int idx = -1;
                for (int t = 0; t < 12; t++)
                {
                    int cand = rng.Next(imgs.Count);
                    if (!avoid.Contains(cand) || imgs.Count < 5)
                    {
                        idx = cand;
                        break;
                    }
                }

                if (idx < 0)
                {
                    idx = k++ % imgs.Count;
                }

                grid[(col, row)] = idx;
                var im = imgs[idx];
                float x = (col * (pw + Gap)) - (pw / 2);
                float y = (row * (ph + Gap)) + off - (ph / 2);
                var dst = new SKRect(x, y, x + pw, y + ph);
                c.Save();
                c.ClipRoundRect(new SKRoundRect(dst, 18, 18), SKClipOperation.Intersect, true);

                if (logoInfo is not null && !logoInfo[idx].Opaque)
                {
                    // Card + whole logo centred with a margin.
                    card.Color = logoInfo[idx].Light ? new SKColor(28, 38, 58) : new SKColor(236, 240, 246);
                    c.DrawRect(dst, card);
                    var inner = new SKRect(dst.Left + 34, dst.Top + 30, dst.Right - 34, dst.Bottom - 30);
                    float fit = Math.Min(inner.Width / im.Width, inner.Height / im.Height);
                    float w = im.Width * fit, h = im.Height * fit;
                    var target = new SKRect(inner.MidX - (w / 2), inner.MidY - (h / 2), inner.MidX + (w / 2), inner.MidY + (h / 2));
                    c.DrawImage(im, new SKRect(0, 0, im.Width, im.Height), target, sampling, paint);
                }
                else
                {
                    float s = Math.Max(pw / im.Width, ph / im.Height);
                    float sw = pw / s, sh = ph / s;
                    var src = new SKRect((im.Width - sw) / 2, (im.Height - sh) / 2, (im.Width + sw) / 2, (im.Height + sh) / 2);
                    c.DrawImage(im, src, dst, sampling, paint);
                }

                c.Restore();
            }
        }

        c.Restore();
    }

    private static void DrawOverlays(SKCanvas c, SKColor accent)
    {
        using var p = new SKPaint { IsAntialias = true };

        using (var sh = SKShader.CreateLinearGradient(
                   new SKPoint(0, 0),
                   new SKPoint(1300, 0),
                   new[] { Bg, Bg.WithAlpha(245), Bg.WithAlpha(0) },
                   new[] { 0f, 0.42f, 1f },
                   SKShaderTileMode.Clamp))
        {
            p.Shader = sh;
            c.DrawRect(0, 0, Width, Height, p);
        }

        using (var sh = SKShader.CreateLinearGradient(
                   new SKPoint(0, Height),
                   new SKPoint(0, Height * 0.55f),
                   new[] { Bg.WithAlpha(217), Bg.WithAlpha(0) },
                   null,
                   SKShaderTileMode.Clamp))
        {
            p.Shader = sh;
            c.DrawRect(0, 0, Width, Height, p);
        }

        using (var sh = SKShader.CreateRadialGradient(
                   new SKPoint(260, 540),
                   900,
                   new[] { accent.WithAlpha(56), accent.WithAlpha(0) },
                   null,
                   SKShaderTileMode.Clamp))
        {
            p.Shader = sh;
            c.DrawRect(0, 0, Width, Height, p);
        }

        p.Shader = null;
    }

    private static void DrawTexts(SKCanvas c, CoverSpec spec)
    {
        const float X0 = 120;
        float fs = 168;
        using var font = new SKFont(TitleFace.Value, fs) { Edging = SKFontEdging.Antialias, Subpixel = true };

        var title = spec.Title.Trim();
        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string> { title };
        if (font.MeasureText(title) > 980 && words.Length > 1)
        {
            int half = (int)Math.Ceiling(words.Length / 2.0);
            lines = new List<string> { string.Join(' ', words.Take(half)), string.Join(' ', words.Skip(half)) };
        }

        while (lines.Max(l => font.MeasureText(l)) > 1000 && fs > 80)
        {
            fs -= 6;
            font.Size = fs;
        }

        float lh = fs * 1.08f;
        float y = 540 - (lines.Count * lh / 2) + (fs * 0.78f) + 20;

        using (var bar = new SKPaint { Color = spec.Accent, IsAntialias = true })
        {
            c.DrawRect(X0, y - (fs * 0.78f) - 64, 90, 10, bar);
        }

        using (var shadow = SKImageFilter.CreateDropShadow(0, 0, 15, 15, new SKColor(0, 0, 0, 115)))
        using (var tp = new SKPaint { Color = SKColors.White, IsAntialias = true, ImageFilter = shadow })
        {
            foreach (var l in lines)
            {
                c.DrawText(l, X0 - 6, y, SKTextAlign.Left, font, tp);
                y += lh;
            }
        }

        if (!string.IsNullOrWhiteSpace(spec.Subtitle))
        {
            using var sf = new SKFont(SubFace.Value, 46) { Edging = SKFontEdging.Antialias, Subpixel = true };
            using var sp = new SKPaint { Color = new SKColor(226, 232, 240, 199), IsAntialias = true };
            c.DrawText(spec.Subtitle, X0, y - lh + (fs * 0.22f) + 70, SKTextAlign.Left, sf, sp);
        }
    }

    private static SKTypeface LoadFont(string file)
    {
        var asm = typeof(CoverRenderer).Assembly;
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(file, StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            return SKTypeface.Default;
        }

        using var s = asm.GetManifestResourceStream(name)!;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return SKTypeface.FromData(SKData.CreateCopy(ms.ToArray())) ?? SKTypeface.Default;
    }
}
