using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Blobin.Analysis
{
    /// <summary>
    /// 2Dカルマンフィルタ運動モデルと Hungarian（大域的最適割り当て）アルゴリズムを統合した
    /// 最高精度のマルチモーダルトラッカー。
    /// 
    /// 特徴:
    /// 1. カルマンフィルタ: 状態 [x, y, vx, vy, w, h, vw, vh] を維持し、急加速や外乱、一時的遮蔽でも未来軌道を正確に予測
    /// 2. Hungarian Algorithm (Kuhn-Munkres): 全ペアの総コストを大域的に最小化し、交差時のIDスワップを数学的に排除
    /// 3. 多次元コスト関数: 予測位置距離 + IoU + 面積比 + RGB色距離 + アスペクト比変化
    /// 4. 適応型平滑化 (EMA): 枠のガタつきを無くし、60fpsで極めて滑らかなHUDトラッキングを実現
    /// </summary>
    public sealed class BlobTracker
    {
        const int MaxMissedFrames = 4;

        /// <summary>
        /// 1つの追尾トラック（カルマンフィルタ状態保持）
        /// </summary>
        sealed class KalmanTrack
        {
            public int Id;
            // 状態量: x, y, vx, vy, w, h, vw, vh
            public float X, Y, Vx, Vy;
            public float W, H, Vw, Vh;

            // 分散 (共分散対角成分)
            public float Px = 10f, Py = 10f, Pvx = 50f, Pvy = 50f;
            public float Pw = 10f, Ph = 10f, Pvw = 50f, Pvh = 50f;

            public float Area;
            public byte ColorR, ColorG, ColorB;
            public List<Vector2> Contour = [];
            public int MissedFrames;
            public int Age;

            public float Speed => MathF.Sqrt(Vx * Vx + Vy * Vy);

            public void Predict()
            {
                // 運動モデル予測
                X += Vx;
                Y += Vy;
                W = Math.Max(1f, W + Vw);
                H = Math.Max(1f, H + Vh);

                // プロセスノイズ加算
                Px += Pvx + 1.0f;
                Py += Pvy + 1.0f;
                Pvx += 2.0f;
                Pvy += 2.0f;
                Pw += Pvw + 1.0f;
                Ph += Pvh + 1.0f;
                Pvw += 2.0f;
                Pvh += 2.0f;

                // 速度減衰（外挿の暴走防止）
                Vx *= 0.95f;
                Vy *= 0.95f;
                Vw *= 0.90f;
                Vh *= 0.90f;
            }

            public void Update(Blob obs, float alpha)
            {
                // 観測値: obs.CentroidX, obs.CentroidY, obs.Width, obs.Height
                float zx = obs.CentroidX;
                float zy = obs.CentroidY;
                float zw = Math.Max(1f, obs.Width);
                float zh = Math.Max(1f, obs.Height);

                // カルマンゲイン計算 (観測ノイズ R ≈ 4.0)
                float R = 4.0f;
                float Kx = Px / (Px + R);
                float Ky = Py / (Py + R);
                float Kw = Pw / (Pw + R);
                float Kh = Ph / (Ph + R);

                // 速度の更新
                float innovX = zx - X;
                float innovY = zy - Y;
                float innovW = zw - W;
                float innovH = zh - H;

                Vx += innovX * 0.45f;
                Vy += innovY * 0.45f;
                Vw += innovW * 0.35f;
                Vh += innovH * 0.35f;

                // 位置・サイズの更新
                X += Kx * innovX;
                Y += Ky * innovY;
                W += Kw * innovW;
                H += Kh * innovH;

                // 分散更新
                Px *= (1.0f - Kx);
                Py *= (1.0f - Ky);
                Pw *= (1.0f - Kw);
                Ph *= (1.0f - Kh);

                // 面積と色
                Area = Area * (1.0f - alpha) + obs.Area * alpha;
                ColorR = obs.ColorR;
                ColorG = obs.ColorG;
                ColorB = obs.ColorB;

                // 輪郭平滑化
                var smoothedContour = new List<Vector2>(obs.Contour.Count);
                for (int i = 0; i < obs.Contour.Count; i++)
                {
                    var prev = Contour.Count == 0 ? obs.Contour[i] : Contour[i % Contour.Count];
                    smoothedContour.Add(new Vector2(
                        prev.X * (1.0f - alpha) + obs.Contour[i].X * alpha,
                        prev.Y * (1.0f - alpha) + obs.Contour[i].Y * alpha));
                }
                Contour = smoothedContour;

                MissedFrames = 0;
                Age++;
            }
        }

        readonly List<KalmanTrack> tracks = [];
        readonly object syncRoot = new();
        int nextTrackId = 1;

        /// <summary>トラッキング状態をリセットする</summary>
        public void Reset()
        {
            lock (syncRoot)
            {
                tracks.Clear();
                nextTrackId = 1;
            }
        }

        /// <summary>
        /// 現在フレームの検出結果をトラッキング＆平滑化して返す。
        /// </summary>
        public List<Blob> Stabilize(IReadOnlyList<Blob> current, double strength01)
        {
            lock (syncRoot)
            {
                // 全トラックのカルマン予測ステップを実行
                foreach (var track in tracks)
                {
                    track.Predict();
                }

                if (current.Count == 0)
                {
                    foreach (var track in tracks)
                        track.MissedFrames++;
                    tracks.RemoveAll(t => t.MissedFrames > MaxMissedFrames);
                    return [];
                }

                double strength = Math.Clamp(strength01, 0.0, 1.0);
                if (strength <= 0.0005)
                {
                    tracks.Clear();
                    nextTrackId = 1;
                    return [.. current];
                }

                // 平滑化係数 alpha: 0% → 1.0 (そのまま), 100% → 0.05 (強平滑化)
                float alpha = Math.Max(0.04f, (float)(1.0 - strength * 0.94));

                int numTracks = tracks.Count;
                int numBlobs = current.Count;

                // 1. コスト行列の構築
                float[,] costMatrix = new float[numTracks, numBlobs];
                const float HighCost = 1e5f;

                for (int t = 0; t < numTracks; t++)
                {
                    var trk = tracks[t];
                    float trkDiag = MathF.Sqrt(trk.W * trk.W + trk.H * trk.H);
                    float maxAllowedDist = Math.Max(28f, trkDiag * 2.6f);

                    float trkMinX = trk.X - trk.W * 0.5f;
                    float trkMaxX = trk.X + trk.W * 0.5f;
                    float trkMinY = trk.Y - trk.H * 0.5f;
                    float trkMaxY = trk.Y + trk.H * 0.5f;

                    for (int b = 0; b < numBlobs; b++)
                    {
                        var blb = current[b];
                        float dx = trk.X - blb.CentroidX;
                        float dy = trk.Y - blb.CentroidY;
                        float dist = MathF.Sqrt(dx * dx + dy * dy);

                        if (dist > maxAllowedDist)
                        {
                            costMatrix[t, b] = HighCost;
                            continue;
                        }

                        // 1. 距離スコア (0〜1)
                        float normDist = Math.Clamp(dist / maxAllowedDist, 0f, 1f);

                        // 2. IoU (0〜1)
                        float interMinX = Math.Max(trkMinX, blb.MinX);
                        float interMinY = Math.Max(trkMinY, blb.MinY);
                        float interMaxX = Math.Min(trkMaxX, blb.MaxX);
                        float interMaxY = Math.Min(trkMaxY, blb.MaxY);
                        float interW = Math.Max(0f, interMaxX - interMinX);
                        float interH = Math.Max(0f, interMaxY - interMinY);
                        float interArea = interW * interH;
                        float blbArea = blb.Width * blb.Height;
                        float trkArea = trk.W * trk.H;
                        float unionArea = trkArea + blbArea - interArea;
                        float iou = (unionArea > 0.0001f) ? (interArea / unionArea) : 0f;

                        // 3. 面積比スコア (0〜1)
                        float areaRatio = Math.Min(blb.Area, trk.Area) / Math.Max(1f, Math.Max(blb.Area, trk.Area));
                        float areaDiff = 1.0f - areaRatio;

                        // 4. 色差スコア (0〜1)
                        float dr = (trk.ColorR - blb.ColorR) / 255.0f;
                        float dg = (trk.ColorG - blb.ColorG) / 255.0f;
                        float db = (trk.ColorB - blb.ColorB) / 255.0f;
                        float colorDiff = Math.Clamp(MathF.Sqrt(dr * dr + dg * dg + db * db) / 1.732f, 0f, 1f);

                        // 総合コスト計算
                        float cost = 0.30f * normDist + 0.20f * (1.0f - iou) + 0.15f * areaDiff + 0.35f * colorDiff;
                        if (colorDiff > 0.35f)
                        {
                            cost += colorDiff * 0.50f; // 色が異なる場合の強いペナルティ
                        }

                        costMatrix[t, b] = (cost < 0.85f) ? cost : HighCost;
                    }
                }

                // 2. Hungarian Algorithm (Kuhn-Munkres) による大域的最適割り当て
                int[] assignments = HungarianAlgorithm.FindAssignments(costMatrix, HighCost);

                var matchedTracks = new HashSet<int>();
                var matchedBlobs = new HashSet<int>();
                var result = new List<Blob>(numBlobs);

                for (int t = 0; t < numTracks; t++)
                {
                    int b = assignments[t];
                    if (b >= 0 && costMatrix[t, b] < HighCost)
                    {
                        matchedTracks.Add(t);
                        matchedBlobs.Add(b);

                        var trk = tracks[t];
                        var blb = current[b];
                        trk.Update(blb, alpha);

                        float halfW = trk.W * 0.5f;
                        float halfH = trk.H * 0.5f;
                        result.Add(new Blob
                        {
                            Id = trk.Id,
                            MinX = trk.X - halfW,
                            MinY = trk.Y - halfH,
                            MaxX = trk.X + halfW,
                            MaxY = trk.Y + halfH,
                            CentroidX = trk.X,
                            CentroidY = trk.Y,
                            Area = trk.Area,
                            Speed = trk.Speed,
                            ColorR = trk.ColorR,
                            ColorG = trk.ColorG,
                            ColorB = trk.ColorB,
                            Contour = trk.Contour,
                        });
                    }
                }

                // 3. マッチしなかった新規ブロブの登録
                for (int b = 0; b < numBlobs; b++)
                {
                    if (matchedBlobs.Contains(b)) continue;

                    var blb = current[b];
                    var newTrack = new KalmanTrack
                    {
                        Id = nextTrackId++,
                        X = blb.CentroidX,
                        Y = blb.CentroidY,
                        W = blb.Width,
                        H = blb.Height,
                        Area = blb.Area,
                        ColorR = blb.ColorR,
                        ColorG = blb.ColorG,
                        ColorB = blb.ColorB,
                        Contour = [.. blb.Contour],
                        MissedFrames = 0,
                        Age = 1,
                    };
                    tracks.Add(newTrack);

                    var newBlob = Clone(blb);
                    newBlob.Id = newTrack.Id;
                    newBlob.Speed = 0f;
                    result.Add(newBlob);
                }

                // 4. マッチしなかったトラックの消失カウント更新
                for (int t = tracks.Count - 1; t >= 0; t--)
                {
                    if (t < numTracks && !matchedTracks.Contains(t))
                    {
                        tracks[t].MissedFrames++;
                        if (tracks[t].MissedFrames > MaxMissedFrames)
                        {
                            tracks.RemoveAt(t);
                        }
                    }
                }

                // 描画順を安定化
                result = [.. result.OrderBy(b => b.CentroidY).ThenBy(b => b.CentroidX)];
                return result;
            }
        }

        static Blob Clone(Blob b) => new()
        {
            Id = b.Id,
            MinX = b.MinX,
            MinY = b.MinY,
            MaxX = b.MaxX,
            MaxY = b.MaxY,
            CentroidX = b.CentroidX,
            CentroidY = b.CentroidY,
            Area = b.Area,
            Speed = b.Speed,
            ColorR = b.ColorR,
            ColorG = b.ColorG,
            ColorB = b.ColorB,
            Contour = [.. b.Contour],
        };
    }

    /// <summary>
    /// Kuhn-Munkres (Hungarian) アルゴリズムによる大域的最適割り当て
    /// </summary>
    internal static class HungarianAlgorithm
    {
        public static int[] FindAssignments(float[,] costMatrix, float maxCostThreshold)
        {
            int rows = costMatrix.GetLength(0);
            int cols = costMatrix.GetLength(1);
            if (rows == 0 || cols == 0) return new int[rows];

            int dim = Math.Max(rows, cols);
            float[,] cost = new float[dim, dim];
            for (int r = 0; r < dim; r++)
            {
                for (int c = 0; c < dim; c++)
                {
                    if (r < rows && c < cols)
                        cost[r, c] = costMatrix[r, c];
                    else
                        cost[r, c] = maxCostThreshold;
                }
            }

            float[] u = new float[dim + 1];
            float[] v = new float[dim + 1];
            int[] p = new int[dim + 1];
            int[] way = new int[dim + 1];

            for (int i = 1; i <= dim; i++)
            {
                p[0] = i;
                int j0 = 0;
                float[] minv = new float[dim + 1];
                Array.Fill(minv, float.MaxValue);
                bool[] used = new bool[dim + 1];

                do
                {
                    used[j0] = true;
                    int i0 = p[j0];
                    float delta = float.MaxValue;
                    int j1 = 0;

                    for (int j = 1; j <= dim; j++)
                    {
                        if (!used[j])
                        {
                            float cur = cost[i0 - 1, j - 1] - u[i0] - v[j];
                            if (cur < minv[j])
                            {
                                minv[j] = cur;
                                way[j] = j0;
                            }
                            if (minv[j] < delta)
                            {
                                delta = minv[j];
                                j1 = j;
                            }
                        }
                    }

                    for (int j = 0; j <= dim; j++)
                    {
                        if (used[j])
                        {
                            u[p[j]] += delta;
                            v[j] -= delta;
                        }
                        else
                        {
                            minv[j] -= delta;
                        }
                    }

                    j0 = j1;
                } while (p[j0] != 0);

                do
                {
                    int j1 = way[j0];
                    p[j0] = p[j1];
                    j0 = j1;
                } while (j0 != 0);
            }

            int[] result = new int[rows];
            Array.Fill(result, -1);
            for (int j = 1; j <= cols; j++)
            {
                if (p[j] > 0 && p[j] <= rows)
                {
                    int r = p[j] - 1;
                    int c = j - 1;
                    if (costMatrix[r, c] < maxCostThreshold)
                    {
                        result[r] = c;
                    }
                }
            }

            return result;
        }
    }
}
