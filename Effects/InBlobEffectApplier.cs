using Blobin.Analysis;
using Blobin.Core;

namespace Blobin.Effects
{
    /// <summary>
    /// 検出されたブロブの領域だけに、色反転・グリッチ等のピクセルエフェクトを適用する。
    /// </summary>
    public static class InBlobEffectApplier
    {
        public static FrameBuffer Apply(
            FrameBuffer source,
            IReadOnlyList<Blob> blobs,
            InBlobEffectType effectType,
            double intensity01,
            int frameNumber,
            int updateInterval,
            int randomSeed,
            int timeOffsetFrames,
            FrameHistoryCache? history)
        {
            var dst = Clone(source);
            if (effectType == InBlobEffectType.None || blobs.Count == 0)
                return dst;

            updateInterval = Math.Max(1, updateInterval);
            int updateTick = frameNumber / updateInterval;

            foreach (var blob in blobs)
            {
                var type = effectType == InBlobEffectType.Random
                    ? PickRandomType(blob.Id, updateTick, randomSeed)
                    : effectType;

                if (type == InBlobEffectType.None)
                    continue;

                var rnd = new Random(HashCode.Combine(randomSeed, blob.Id, updateTick, (int)type));
                ApplyToBlob(source, dst, blob, type, intensity01, rnd, timeOffsetFrames, history);
            }

            return dst;
        }

        static InBlobEffectType PickRandomType(int blobId, int updateTick, int seed)
        {
            var rnd = new Random(HashCode.Combine(seed, blobId, updateTick));
            //None と Random 自身を除いた候補からランダム選択
            InBlobEffectType[] candidates =
            [
                InBlobEffectType.Invert, InBlobEffectType.HueRotate, InBlobEffectType.ColorShift,
                InBlobEffectType.Glitch, InBlobEffectType.BlockDuplicate, InBlobEffectType.Bleed,
                InBlobEffectType.DiagonalStripe, InBlobEffectType.TimeOffset,
            ];
            return candidates[rnd.Next(candidates.Length)];
        }

        static void ApplyToBlob(FrameBuffer src, FrameBuffer dst, Blob blob, InBlobEffectType type,
            double intensity, Random rnd, int timeOffsetFrames, FrameHistoryCache? history)
        {
            int w = src.Width, h = src.Height;
            int minX = Math.Clamp((int)Math.Floor(blob.MinX), 0, w - 1);
            int minY = Math.Clamp((int)Math.Floor(blob.MinY), 0, h - 1);
            int maxX = Math.Clamp((int)Math.Ceiling(blob.MaxX), 0, w - 1);
            int maxY = Math.Clamp((int)Math.Ceiling(blob.MaxY), 0, h - 1);
            if (maxX <= minX || maxY <= minY)
                return;

            var polygon = blob.Contour;
            bool UseMask(int x, int y) => polygon.Count < 3 || PolygonUtil.Contains(polygon, x + 0.5f, y + 0.5f);

            switch (type)
            {
                case InBlobEffectType.Invert:
                    ForEach(minX, minY, maxX, maxY, UseMask, (x, y) =>
                    {
                        src.GetPixel(x, y, out byte b, out byte g, out byte r, out byte a);
                        byte nb = Lerp(b, (byte)(255 - b), intensity);
                        byte ng = Lerp(g, (byte)(255 - g), intensity);
                        byte nr = Lerp(r, (byte)(255 - r), intensity);
                        dst.SetPixel(x, y, nb, ng, nr, a);
                    });
                    break;

                case InBlobEffectType.HueRotate:
                    {
                        double hueShift = intensity * 360.0;
                        ForEach(minX, minY, maxX, maxY, UseMask, (x, y) =>
                        {
                            src.GetPixel(x, y, out byte b, out byte g, out byte r, out byte a);
                            FrameBuffer.RgbToHsv(r, g, b, out double hh, out double ss, out double vv);
                            hh = (hh + hueShift) % 360;
                            if (hh < 0) hh += 360;
                            HsvToRgbByte(hh, ss, vv, out byte nr, out byte ng, out byte nb);
                            dst.SetPixel(x, y, nb, ng, nr, a);
                        });
                        break;
                    }

                case InBlobEffectType.ColorShift:
                    {
                        int shift = (int)Math.Round(1 + intensity * 14);
                        ForEach(minX, minY, maxX, maxY, UseMask, (x, y) =>
                        {
                            src.GetPixel(x, y, out _, out _, out _, out byte a);
                            src.GetPixel(x - shift, y, out _, out _, out byte r, out _);
                            src.GetPixel(x, y, out _, out byte g, out _, out _);
                            src.GetPixel(x + shift, y, out byte b, out _, out _, out _);
                            dst.SetPixel(x, y, b, g, r, a);
                        });
                        break;
                    }

                case InBlobEffectType.Glitch:
                    {
                        int maxShift = (int)Math.Round(intensity * (maxX - minX + 1) * 0.5) + 1;
                        var rowShift = new Dictionary<int, int>();
                        for (int y = minY; y <= maxY; y++)
                            rowShift[y] = rnd.NextDouble() < 0.35 ? rnd.Next(-maxShift, maxShift + 1) : 0;

                        ForEach(minX, minY, maxX, maxY, UseMask, (x, y) =>
                        {
                            int sx = x + rowShift[y];
                            sx = Math.Clamp(sx, minX, maxX);
                            src.GetPixel(sx, y, out byte b, out byte g, out byte r, out byte a);
                            dst.SetPixel(x, y, b, g, r, a);
                        });
                        break;
                    }

                case InBlobEffectType.BlockDuplicate:
                    {
                        int blockSize = Math.Max(2, (int)Math.Round(4 + (1 - intensity) * 20));
                        int bw = maxX - minX + 1, bh = maxY - minY + 1;
                        for (int by = minY; by <= maxY; by += blockSize)
                        {
                            for (int bx = minX; bx <= maxX; bx += blockSize)
                            {
                                int srcOffX = rnd.Next(0, Math.Max(1, bw)) - (bx - minX);
                                int srcOffY = rnd.Next(0, Math.Max(1, bh)) - (by - minY);

                                int bxEnd = Math.Min(bx + blockSize - 1, maxX);
                                int byEnd = Math.Min(by + blockSize - 1, maxY);
                                for (int y = by; y <= byEnd; y++)
                                {
                                    for (int x = bx; x <= bxEnd; x++)
                                    {
                                        if (!UseMask(x, y)) continue;
                                        int sx = Math.Clamp(x + srcOffX, minX, maxX);
                                        int sy = Math.Clamp(y + srcOffY, minY, maxY);
                                        src.GetPixel(sx, sy, out byte b, out byte g, out byte r, out byte a);
                                        dst.SetPixel(x, y, b, g, r, a);
                                    }
                                }
                            }
                        }
                        break;
                    }

                case InBlobEffectType.Bleed:
                    {
                        int radius = Math.Max(1, (int)Math.Round(intensity * 6));
                        ForEach(minX, minY, maxX, maxY, UseMask, (x, y) =>
                        {
                            int sumB = 0, sumG = 0, sumR = 0, sumA = 0, count = 0;
                            for (int dy = -radius; dy <= radius; dy++)
                            {
                                for (int dx = -radius; dx <= radius; dx++)
                                {
                                    int sx = Math.Clamp(x + dx, minX, maxX);
                                    int sy = Math.Clamp(y + dy, minY, maxY);
                                    src.GetPixel(sx, sy, out byte b, out byte g, out byte r, out byte a);
                                    sumB += b; sumG += g; sumR += r; sumA += a; count++;
                                }
                            }
                            dst.SetPixel(x, y, (byte)(sumB / count), (byte)(sumG / count), (byte)(sumR / count), (byte)(sumA / count));
                        });
                        break;
                    }

                case InBlobEffectType.DiagonalStripe:
                    {
                        int stripeWidth = Math.Max(2, (int)Math.Round(20 - intensity * 16));
                        ForEach(minX, minY, maxX, maxY, UseMask, (x, y) =>
                        {
                            src.GetPixel(x, y, out byte b, out byte g, out byte r, out byte a);
                            bool onStripe = ((x + y) / stripeWidth) % 2 == 0;
                            if (onStripe)
                            {
                                byte nb = Lerp(b, (byte)Math.Min(255, b + 90), 0.6);
                                byte ng = Lerp(g, (byte)Math.Min(255, g + 90), 0.6);
                                byte nr = Lerp(r, (byte)Math.Min(255, r + 90), 0.6);
                                dst.SetPixel(x, y, nb, ng, nr, a);
                            }
                            else
                            {
                                dst.SetPixel(x, y, b, g, r, a);
                            }
                        });
                        break;
                    }

                case InBlobEffectType.TimeOffset:
                    {
                        var pastFrame = history?.GetFrame(Math.Max(1, timeOffsetFrames));
                        if (pastFrame is null)
                            break;
                        //historyには軽量化のため解析解像度のフレームを保持しているため、
                        //作業バッファ（src/dst）との解像度比を掛けて座標を変換する
                        float rx = pastFrame.Width / (float)src.Width;
                        float ry = pastFrame.Height / (float)src.Height;
                        ForEach(minX, minY, maxX, maxY, UseMask, (x, y) =>
                        {
                            int sx = Math.Clamp((int)(x * rx), 0, pastFrame.Width - 1);
                            int sy = Math.Clamp((int)(y * ry), 0, pastFrame.Height - 1);
                            pastFrame.GetPixel(sx, sy, out byte b, out byte g, out byte r, out byte a);
                            dst.SetPixel(x, y, b, g, r, a);
                        });
                        break;
                    }
            }
        }

        static void ForEach(int minX, int minY, int maxX, int maxY, Func<int, int, bool> mask, Action<int, int> action)
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (mask(x, y))
                        action(x, y);
                }
            }
        }

        static byte Lerp(byte a, byte b, double t) => (byte)Math.Clamp(a + (b - a) * t, 0, 255);

        static void HsvToRgbByte(double h, double s, double v, out byte r, out byte g, out byte b)
        {
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
            double m = v - c;
            double rd, gd, bd;
            if (h < 60) { rd = c; gd = x; bd = 0; }
            else if (h < 120) { rd = x; gd = c; bd = 0; }
            else if (h < 180) { rd = 0; gd = c; bd = x; }
            else if (h < 240) { rd = 0; gd = x; bd = c; }
            else if (h < 300) { rd = x; gd = 0; bd = c; }
            else { rd = c; gd = 0; bd = x; }

            r = (byte)Math.Round(Math.Clamp((rd + m) * 255, 0, 255));
            g = (byte)Math.Round(Math.Clamp((gd + m) * 255, 0, 255));
            b = (byte)Math.Round(Math.Clamp((bd + m) * 255, 0, 255));
        }

        static FrameBuffer Clone(FrameBuffer src)
        {
            var dst = new FrameBuffer(src.Width, src.Height);
            Array.Copy(src.Pixels, dst.Pixels, src.Pixels.Length);
            return dst;
        }
    }
}
