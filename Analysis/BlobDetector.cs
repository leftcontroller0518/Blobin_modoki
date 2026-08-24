using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Blobin.Core;

namespace Blobin.Analysis
{
    /// <summary>
    /// ブロブ検出に必要なパラメーターをまとめたもの。
    /// </summary>
    public struct DetectionParams
    {
        public DetectionEngine Engine;
        public DetectionMode Mode;
        public ThresholdDirection Direction;
        /// <summary>0～1に正規化されたしきい値</summary>
        public double Threshold;
        /// <summary>大津の二値化によるしきい値自動決定</summary>
        public bool UseOtsu;
        public byte KeyR, KeyG, KeyB;
        /// <summary>0～1に正規化されたキー色許容範囲</summary>
        public double KeyTolerance;
        public RgbChannelType RgbChannel;
        public double HueMin, HueMax;
        /// <summary>0～1に正規化された動き検出感度（大きいほど敏感）</summary>
        public double MotionSensitivity;
        /// <summary>0～1に正規化された最小面積（バッファ全体に対する割合）</summary>
        public double MinAreaFrac;
        /// <summary>0～1に正規化された最大面積（バッファ全体に対する割合）</summary>
        public double MaxAreaFrac;
        public int MaxBlobCount;
        public bool Denoise;
        /// <summary>0～1に正規化されたフレーム間平滑化の強さ（0=なし）</summary>
        public double Smoothing;
        /// <summary>画面端（枠）に接するブロブを除外するか</summary>
        public bool IgnoreEdgeBlobs;
        /// <summary>0～1に正規化された最大外接枠面積比（0=制限なし）</summary>
        public double MaxBoundingBoxFrac;

        /// <summary>重なり処理モード (HL_Blobox 準拠)</summary>
        public OverlapMode Overlap;
        /// <summary>前処理ブラー半径 (px)</summary>
        public int BlurRadius;
        /// <summary>モルフォロジー演算モード</summary>
        public MorphologyMode Morphology;
        /// <summary>マスク膨張/モルフォロジー半径 (px)</summary>
        public int DilateRadius;
    }

    /// <summary>
    /// CPU上のピクセルバッファからブロブ（特徴領域）を高精度に検出する。
    /// 積分画像ブラー、大津の二値化、モルフォロジーOpening/Closing、重なり処理を完全サポート。
    /// </summary>
    public static class BlobDetector
    {
        public static List<Blob> Detect(FrameBuffer frame, FrameBuffer? previousFrame, in DetectionParams p)
        {
            int w = frame.Width, h = frame.Height;
            if (w <= 0 || h <= 0) return [];

            // 1. しきい値の決定（大津の二値化またはユーザー指定値）
            double effectiveThreshold = p.Threshold;
            if (p.UseOtsu && p.Mode == DetectionMode.Brightness)
            {
                effectiveThreshold = ComputeOtsuThreshold(frame);
            }

            var localParams = p;
            localParams.Threshold = effectiveThreshold;

            // 2. 2値化マスクの生成（前処理ブラー付き）
            var mask = BuildMask(frame, previousFrame, localParams);

            // 3. モルフォロジー演算 (Dilate, Closing, Opening)
            int morphR = p.DilateRadius;
            if (p.Mode == DetectionMode.MotionDiff && morphR < 2)
            {
                morphR = 2; // モーション検出時は断片化防止のため最低2px
            }

            if (morphR > 0 && p.Morphology != MorphologyMode.None)
            {
                mask = ApplyMorphology(mask, w, h, p.Morphology, morphR);
            }

            // 4. 孤立ノイズ除去
            if (p.Denoise)
                Denoise(mask, w, h);

            // 5. 連結成分ラベリング (4近傍)
            var (labels, componentCount) = Label(mask, w, h);
            if (componentCount == 0)
                return [];

            var accs = new BlobAccumulator[componentCount];
            for (int i = 0; i < componentCount; i++)
                accs[i] = new BlobAccumulator();

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int lbl = labels[y * w + x];
                    if (lbl < 0) continue;
                    accs[lbl].Add(x, y);

                    // 境界ピクセル（4近傍のいずれかが背景、または画像端）を輪郭候補として収集
                    bool isBoundary =
                        x == 0 || y == 0 || x == w - 1 || y == h - 1 ||
                        labels[y * w + (x - 1)] != lbl ||
                        labels[y * w + (x + 1)] != lbl ||
                        labels[(y - 1) * w + x] != lbl ||
                        labels[(y + 1) * w + x] != lbl;

                    if (isBoundary)
                        accs[lbl].AddBoundary(x, y);
                }
            }

            double totalPixels = w * h;
            double minAreaPx = p.MinAreaFrac * totalPixels;
            double maxAreaPx = p.MaxAreaFrac * totalPixels;

            var blobs = new List<Blob>();
            foreach (var acc in accs)
            {
                if (acc.Count <= 0) continue;
                if (acc.Count < minAreaPx || acc.Count > maxAreaPx) continue;

                // 画面端ブロブの除外
                bool touchesEdge = acc.MinX <= 0 || acc.MinY <= 0 || acc.MaxX >= w - 1 || acc.MaxY >= h - 1;
                if (p.IgnoreEdgeBlobs && touchesEdge) continue;

                // 外接矩形占有率の制限
                double bboxAreaPx = (double)(acc.MaxX + 1 - acc.MinX) * (acc.MaxY + 1 - acc.MinY);
                double bboxFrac = bboxAreaPx / totalPixels;
                if (p.MaxBoundingBoxFrac > 0 && bboxFrac > p.MaxBoundingBoxFrac) continue;

                int cx = Math.Clamp((int)(acc.SumX / acc.Count), 0, w - 1);
                int cy = Math.Clamp((int)(acc.SumY / acc.Count), 0, h - 1);
                frame.GetPixel(cx, cy, out byte pb, out byte pg, out byte pr, out _);

                var blob = new Blob
                {
                    MinX = acc.MinX,
                    MinY = acc.MinY,
                    MaxX = acc.MaxX + 1,
                    MaxY = acc.MaxY + 1,
                    CentroidX = (float)(acc.SumX / acc.Count) + 0.5f,
                    CentroidY = (float)(acc.SumY / acc.Count) + 0.5f,
                    Area = acc.Count,
                    ColorR = pr,
                    ColorG = pg,
                    ColorB = pb,
                    Contour = ConvexHull(acc.Boundary),
                };
                blobs.Add(blob);
            }

            // 6. 重複ブロブ処理 (HL_Blobox 方式)
            blobs = ApplyOverlapMode(blobs, p.Overlap);

            // 7. 面積の大きい順に上限数まで採用
            blobs = blobs.OrderByDescending(b => b.Area).Take(Math.Max(1, p.MaxBlobCount)).ToList();

            // 8. 初期ID割り当て
            blobs = blobs.OrderBy(b => b.CentroidY).ThenBy(b => b.CentroidX).ToList();
            for (int i = 0; i < blobs.Count; i++)
                blobs[i].Id = i + 1;

            return blobs;
        }

        #region 大津の2値化 (Otsu Thresholding)

        /// <summary>
        /// 映像の輝度ヒストグラムからクラス間分散を最大化するしきい値 (0〜1) を自動計算する
        /// </summary>
        public static double ComputeOtsuThreshold(FrameBuffer frame)
        {
            int w = frame.Width, h = frame.Height;
            int total = w * h;
            if (total == 0) return 0.5;

            int[] hist = new int[256];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    frame.GetPixel(x, y, out byte b, out byte g, out byte r, out _);
                    int l = (int)Math.Clamp(Math.Round(FrameBuffer.Luminance(r, g, b) * 255.0), 0, 255);
                    hist[l]++;
                }
            }

            double sumAll = 0;
            for (int i = 0; i < 256; i++) sumAll += i * hist[i];

            double sumB = 0;
            int wB = 0;
            double maxVar = 0;
            int bestThreshold = 128;

            for (int t = 0; t < 256; t++)
            {
                wB += hist[t];
                if (wB == 0) continue;
                int wF = total - wB;
                if (wF == 0) break;

                sumB += t * hist[t];
                double mB = sumB / wB;
                double mF = (sumAll - sumB) / wF;

                double varBetween = (double)wB * wF * (mB - mF) * (mB - mF);
                if (varBetween > maxVar)
                {
                    maxVar = varBetween;
                    bestThreshold = t;
                }
            }

            return bestThreshold / 255.0;
        }

        #endregion

        #region 積分画像 & マスク生成

        sealed class IntegralImage
        {
            public readonly float[] S;
            public readonly int W, H;
            public IntegralImage(int w, int h)
            {
                W = w;
                H = h;
                S = new float[(w + 1) * (h + 1)];
            }

            public float Sample(int x, int y, int r)
            {
                int x0 = Math.Max(0, x - r);
                int y0 = Math.Max(0, y - r);
                int x1 = Math.Min(W, x + r + 1);
                int y1 = Math.Min(H, y + r + 1);
                if (x1 <= x0 || y1 <= y0) return 0f;

                int ww = W + 1;
                float a = S[y1 * ww + x1];
                float b = S[y0 * ww + x1];
                float c = S[y1 * ww + x0];
                float d = S[y0 * ww + x0];
                return (a - b - c + d) / ((x1 - x0) * (y1 - y0));
            }
        }

        static IntegralImage BuildLumaIntegral(FrameBuffer frame)
        {
            int w = frame.Width, h = frame.Height;
            var img = new IntegralImage(w, h);
            int ww = w + 1;
            for (int y = 0; y < h; y++)
            {
                float row = 0f;
                for (int x = 0; x < w; x++)
                {
                    frame.GetPixel(x, y, out byte b, out byte g, out byte r, out _);
                    row += (float)FrameBuffer.Luminance(r, g, b);
                    img.S[(y + 1) * ww + (x + 1)] = img.S[y * ww + (x + 1)] + row;
                }
            }
            return img;
        }

        static bool[] BuildMask(FrameBuffer frame, FrameBuffer? prev, in DetectionParams p)
        {
            int w = frame.Width, h = frame.Height;
            var mask = new bool[w * h];

            IntegralImage? lumaIntegral = null;
            IntegralImage? prevLumaIntegral = null;

            if (p.BlurRadius > 0)
            {
                if (p.Mode == DetectionMode.Brightness)
                {
                    lumaIntegral = BuildLumaIntegral(frame);
                }
                else if (p.Mode == DetectionMode.MotionDiff && prev != null)
                {
                    lumaIntegral = BuildLumaIntegral(frame);
                    prevLumaIntegral = BuildLumaIntegral(prev);
                }
            }

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    mask[y * w + x] = EvaluatePixel(frame, prev, x, y, p, lumaIntegral, prevLumaIntegral);
                }
            }
            return mask;
        }

        static bool EvaluatePixel(
            FrameBuffer frame,
            FrameBuffer? prev,
            int x,
            int y,
            in DetectionParams p,
            IntegralImage? lumaIntegral,
            IntegralImage? prevLumaIntegral)
        {
            frame.GetPixel(x, y, out byte b, out byte g, out byte r, out byte a);

            switch (p.Mode)
            {
                case DetectionMode.Alpha:
                    return CompareThreshold(a / 255.0, p.Threshold, p.Direction);

                case DetectionMode.Brightness:
                    {
                        double val = lumaIntegral != null
                            ? lumaIntegral.Sample(x, y, p.BlurRadius)
                            : FrameBuffer.Luminance(r, g, b);
                        return CompareThreshold(val, p.Threshold, p.Direction);
                    }

                case DetectionMode.KeyColor:
                    {
                        double dr = (r - p.KeyR) / 255.0;
                        double dg = (g - p.KeyG) / 255.0;
                        double db = (b - p.KeyB) / 255.0;
                        double dist = Math.Sqrt(dr * dr + dg * dg + db * db) / Math.Sqrt(3);
                        return dist <= Math.Max(0.0001, p.KeyTolerance);
                    }

                case DetectionMode.MotionDiff:
                    {
                        if (prev is null) return false;
                        double diff;
                        if (lumaIntegral != null && prevLumaIntegral != null)
                        {
                            float c = lumaIntegral.Sample(x, y, p.BlurRadius);
                            float prLuma = prevLumaIntegral.Sample(x, y, p.BlurRadius);
                            diff = Math.Abs(c - prLuma);
                        }
                        else
                        {
                            prev.GetPixel(x, y, out byte pb, out byte pg, out byte pr, out _);
                            diff = (Math.Abs(r - pr) + Math.Abs(g - pg) + Math.Abs(b - pb)) / (3.0 * 255.0);
                        }
                        double sensitivity = Math.Clamp(1.0 - p.MotionSensitivity, 0.02, 0.98);
                        return diff >= sensitivity;
                    }

                case DetectionMode.RgbChannel:
                    {
                        double v = p.RgbChannel switch
                        {
                            RgbChannelType.Red => r / 255.0,
                            RgbChannelType.Green => g / 255.0,
                            _ => b / 255.0,
                        };
                        return CompareThreshold(v, p.Threshold, p.Direction);
                    }

                case DetectionMode.Saturation:
                    {
                        FrameBuffer.RgbToHsv(r, g, b, out _, out double s, out _);
                        return CompareThreshold(s, p.Threshold, p.Direction);
                    }

                case DetectionMode.Hue:
                    {
                        FrameBuffer.RgbToHsv(r, g, b, out double hue, out double sat, out double val);
                        if (sat < 0.12 || val < 0.08) return false;
                        return HueInRange(hue, p.HueMin, p.HueMax);
                    }

                case DetectionMode.Edge:
                    {
                        double edge = SobelMagnitude(frame, x, y);
                        return CompareThreshold(edge, p.Threshold, ThresholdDirection.Above);
                    }
            }
            return false;
        }

        static bool CompareThreshold(double value, double threshold, ThresholdDirection direction) =>
            direction == ThresholdDirection.Above ? value >= threshold : value <= threshold;

        static bool HueInRange(double hue, double min, double max)
        {
            min = ((min % 360) + 360) % 360;
            max = ((max % 360) + 360) % 360;
            if (Math.Abs(min - max) < 0.001) return true;
            if (min <= max)
                return hue >= min && hue <= max;
            return hue >= min || hue <= max;
        }

        static double SobelMagnitude(FrameBuffer frame, int x, int y)
        {
            double L(int dx, int dy)
            {
                frame.GetPixel(x + dx, y + dy, out byte b, out byte g, out byte r, out _);
                return FrameBuffer.Luminance(r, g, b);
            }

            double gx =
                -L(-1, -1) - 2 * L(-1, 0) - L(-1, 1)
                + L(1, -1) + 2 * L(1, 0) + L(1, 1);
            double gy =
                -L(-1, -1) - 2 * L(0, -1) - L(1, -1)
                + L(-1, 1) + 2 * L(0, 1) + L(1, 1);

            return Math.Clamp(Math.Sqrt(gx * gx + gy * gy) / 1.4142, 0, 1);
        }

        #endregion

        #region モルフォロジー演算 (Dilate, Erode, Closing, Opening)

        static bool[] ApplyMorphology(bool[] mask, int w, int h, MorphologyMode mode, int r)
        {
            return mode switch
            {
                MorphologyMode.Dilate => DilateMask(mask, w, h, r),
                MorphologyMode.Closing => ErodeMask(DilateMask(mask, w, h, r), w, h, r),
                MorphologyMode.Opening => DilateMask(ErodeMask(mask, w, h, r), w, h, r),
                _ => mask,
            };
        }

        /// <summary>膨張 (Dilate: 2D Summed-Area Table による O(1) 処理)</summary>
        static bool[] DilateMask(bool[] mask, int w, int h, int r)
        {
            int ww = w + 1, hh = h + 1;
            var s = new int[ww * hh];
            for (int y = 0; y < h; y++)
            {
                int row = 0;
                for (int x = 0; x < w; x++)
                {
                    row += mask[y * w + x] ? 1 : 0;
                    s[(y + 1) * ww + (x + 1)] = s[y * ww + (x + 1)] + row;
                }
            }

            var outMask = new bool[w * h];
            for (int y = 0; y < h; y++)
            {
                int y0 = Math.Max(0, y - r);
                int y1 = Math.Min(h, y + r + 1);
                for (int x = 0; x < w; x++)
                {
                    int x0 = Math.Max(0, x - r);
                    int x1 = Math.Min(w, x + r + 1);
                    int sum = (s[y1 * ww + x1] - s[y1 * ww + x0]) - (s[y0 * ww + x1] - s[y0 * ww + x0]);
                    if (sum > 0)
                    {
                        outMask[y * w + x] = true;
                    }
                }
            }
            return outMask;
        }

        /// <summary>収縮 (Erode: 2D Summed-Area Table による O(1) 処理)</summary>
        static bool[] ErodeMask(bool[] mask, int w, int h, int r)
        {
            int ww = w + 1, hh = h + 1;
            var s = new int[ww * hh];
            for (int y = 0; y < h; y++)
            {
                int row = 0;
                for (int x = 0; x < w; x++)
                {
                    row += mask[y * w + x] ? 1 : 0;
                    s[(y + 1) * ww + (x + 1)] = s[y * ww + (x + 1)] + row;
                }
            }

            var outMask = new bool[w * h];
            for (int y = 0; y < h; y++)
            {
                int y0 = Math.Max(0, y - r);
                int y1 = Math.Min(h, y + r + 1);
                for (int x = 0; x < w; x++)
                {
                    int x0 = Math.Max(0, x - r);
                    int x1 = Math.Min(w, x + r + 1);
                    int windowArea = (x1 - x0) * (y1 - y0);
                    int sum = (s[y1 * ww + x1] - s[y1 * ww + x0]) - (s[y0 * ww + x1] - s[y0 * ww + x0]);
                    if (sum == windowArea)
                    {
                        outMask[y * w + x] = true;
                    }
                }
            }
            return outMask;
        }

        static void Denoise(bool[] mask, int w, int h)
        {
            var copy = (bool[])mask.Clone();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (!copy[y * w + x]) continue;
                    int count = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx >= 0 && nx < w && ny >= 0 && ny < h && copy[ny * w + nx])
                                count++;
                        }
                    }
                    if (count <= 2)
                        mask[y * w + x] = false;
                }
            }
        }

        #endregion

        #region 重複ブロブ処理 (HL_Blobox 準拠)

        static bool IsOverlapping(Blob a, Blob b) =>
            a.MinX < b.MaxX && b.MinX < a.MaxX && a.MinY < b.MaxY && b.MinY < a.MaxY;

        static List<Blob> ApplyOverlapMode(List<Blob> blobs, OverlapMode mode)
        {
            if (blobs.Count <= 1 || mode == OverlapMode.Keep)
                return blobs;

            switch (mode)
            {
                case OverlapMode.RemoveSmaller:
                    {
                        var sorted = blobs.OrderByDescending(b => b.Area).ToList();
                        var kept = new List<Blob>();
                        foreach (var b in sorted)
                        {
                            if (!kept.Any(k => IsOverlapping(b, k)))
                                kept.Add(b);
                        }
                        return kept;
                    }

                case OverlapMode.RemoveBigger:
                    {
                        var sorted = blobs.OrderBy(b => b.Area).ToList();
                        var kept = new List<Blob>();
                        foreach (var b in sorted)
                        {
                            if (!kept.Any(k => IsOverlapping(b, k)))
                                kept.Add(b);
                        }
                        return kept;
                    }

                case OverlapMode.Merge:
                    {
                        var merged = new List<Blob>();
                        var used = new bool[blobs.Count];

                        for (int i = 0; i < blobs.Count; i++)
                        {
                            if (used[i]) continue;
                            used[i] = true;

                            var group = new List<Blob> { blobs[i] };
                            for (int j = i + 1; j < blobs.Count; j++)
                            {
                                if (used[j]) continue;
                                if (group.Any(g => IsOverlapping(g, blobs[j])))
                                {
                                    used[j] = true;
                                    group.Add(blobs[j]);
                                }
                            }

                            if (group.Count == 1)
                            {
                                merged.Add(group[0]);
                            }
                            else
                            {
                                float minX = group.Min(b => b.MinX);
                                float minY = group.Min(b => b.MinY);
                                float maxX = group.Max(b => b.MaxX);
                                float maxY = group.Max(b => b.MaxY);
                                float totalArea = group.Sum(b => b.Area);
                                float cx = group.Sum(b => b.CentroidX * b.Area) / Math.Max(1f, totalArea);
                                float cy = group.Sum(b => b.CentroidY * b.Area) / Math.Max(1f, totalArea);

                                var allPoints = group.SelectMany(b => b.Contour).ToList();

                                merged.Add(new Blob
                                {
                                    MinX = minX,
                                    MinY = minY,
                                    MaxX = maxX,
                                    MaxY = maxY,
                                    CentroidX = cx,
                                    CentroidY = cy,
                                    Area = totalArea,
                                    ColorR = group[0].ColorR,
                                    ColorG = group[0].ColorG,
                                    ColorB = group[0].ColorB,
                                    Contour = ConvexHull(allPoints),
                                });
                            }
                        }
                        return merged;
                    }
            }

            return blobs;
        }

        #endregion

        #region ラベリング & 幾何計算

        sealed class BlobAccumulator
        {
            public int Count;
            public double SumX, SumY;
            public int MinX = int.MaxValue, MinY = int.MaxValue;
            public int MaxX = int.MinValue, MaxY = int.MinValue;
            public readonly List<Vector2> Boundary = [];

            public void Add(int x, int y)
            {
                Count++;
                SumX += x;
                SumY += y;
                if (x < MinX) MinX = x;
                if (x > MaxX) MaxX = x;
                if (y < MinY) MinY = y;
                if (y > MaxY) MaxY = y;
            }

            public void AddBoundary(int x, int y)
            {
                Boundary.Add(new Vector2(x + 0.5f, y + 0.5f));
            }
        }

        static (int[] labels, int count) Label(bool[] mask, int w, int h)
        {
            var parent = new List<int>();
            int Find(int i)
            {
                while (parent[i] != i)
                {
                    parent[i] = parent[parent[i]];
                    i = parent[i];
                }
                return i;
            }

            void Union(int i, int j)
            {
                int ri = Find(i);
                int rj = Find(j);
                if (ri != rj) parent[rj] = ri;
            }

            var tempLabels = new int[w * h];
            Array.Fill(tempLabels, -1);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (!mask[y * w + x]) continue;

                    int left = (x > 0) ? tempLabels[y * w + (x - 1)] : -1;
                    int up = (y > 0) ? tempLabels[(y - 1) * w + x] : -1;

                    if (left < 0 && up < 0)
                    {
                        int newLabel = parent.Count;
                        parent.Add(newLabel);
                        tempLabels[y * w + x] = newLabel;
                    }
                    else if (left >= 0 && up < 0)
                    {
                        tempLabels[y * w + x] = left;
                    }
                    else if (left < 0 && up >= 0)
                    {
                        tempLabels[y * w + x] = up;
                    }
                    else
                    {
                        tempLabels[y * w + x] = left;
                        Union(left, up!);
                    }
                }
            }

            var labelMap = new Dictionary<int, int>();
            int nextId = 0;
            var finalLabels = new int[w * h];

            for (int i = 0; i < tempLabels.Length; i++)
            {
                int raw = tempLabels[i];
                if (raw < 0)
                {
                    finalLabels[i] = -1;
                    continue;
                }
                int root = Find(raw);
                if (!labelMap.TryGetValue(root, out int mapped))
                {
                    mapped = nextId++;
                    labelMap[root] = mapped;
                }
                finalLabels[i] = mapped;
            }

            return (finalLabels, nextId);
        }

        static List<Vector2> ConvexHull(List<Vector2> points)
        {
            if (points.Count <= 3) return new List<Vector2>(points);

            var sorted = points.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();

            static float Cross(Vector2 o, Vector2 a, Vector2 b) =>
                (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

            var lower = new List<Vector2>();
            foreach (var p in sorted)
            {
                while (lower.Count >= 2 && Cross(lower[^2], lower[^1], p) <= 0)
                    lower.RemoveAt(lower.Count - 1);
                lower.Add(p);
            }

            var upper = new List<Vector2>();
            for (int i = sorted.Count - 1; i >= 0; i--)
            {
                var p = sorted[i];
                while (upper.Count >= 2 && Cross(upper[^2], upper[^1], p) <= 0)
                    upper.RemoveAt(upper.Count - 1);
                upper.Add(p);
            }

            lower.RemoveAt(lower.Count - 1);
            upper.RemoveAt(upper.Count - 1);
            lower.AddRange(upper);
            return lower;
        }

        #endregion
    }
}
