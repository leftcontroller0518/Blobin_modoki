using System;
using System.Collections.Generic;
using System.Numerics;

namespace Blobin.Analysis
{
    /// <summary>
    /// 検出された1つのブロブ（特徴領域）。
    /// 座標は生成時点の空間（解析用の縮小バッファ、または出力解像度）のピクセル単位。
    /// </summary>
    public sealed class Blob
    {
        public int Id;

        public float MinX, MinY, MaxX, MaxY;

        public float CentroidX, CentroidY;

        /// <summary>ピクセル数（実効面積）</summary>
        public float Area;

        /// <summary>移動速度（px/frame）</summary>
        public float Speed;

        /// <summary>ブロブの代表色（RGB: 0〜255）</summary>
        public byte ColorR = 255, ColorG = 255, ColorB = 255;

        /// <summary>
        /// 輪郭を単純化した多角形の頂点列（時計回り）。
        /// 解析バッファ空間の座標。矩形近似の場合は4点になることもある。
        /// </summary>
        public List<Vector2> Contour = [];

        public float Width => Math.Max(0, MaxX - MinX);
        public float Height => Math.Max(0, MaxY - MinY);
        public float BBoxArea => Width * Height;

        /// <summary>
        /// 別の空間（例：解析バッファ→出力解像度）にスケールしたコピーを作成する。
        /// </summary>
        public Blob Scaled(float scaleX, float scaleY, float offsetX = 0, float offsetY = 0)
        {
            var result = new Blob
            {
                Id = Id,
                MinX = MinX * scaleX + offsetX,
                MinY = MinY * scaleY + offsetY,
                MaxX = MaxX * scaleX + offsetX,
                MaxY = MaxY * scaleY + offsetY,
                CentroidX = CentroidX * scaleX + offsetX,
                CentroidY = CentroidY * scaleY + offsetY,
                Area = Area * scaleX * scaleY,
                Speed = Speed * MathF.Sqrt(scaleX * scaleY),
                ColorR = ColorR,
                ColorG = ColorG,
                ColorB = ColorB,
            };
            result.Contour = new List<Vector2>(Contour.Count);
            foreach (var p in Contour)
                result.Contour.Add(new Vector2(p.X * scaleX + offsetX, p.Y * scaleY + offsetY));
            return result;
        }

        /// <summary>
        /// 枠の描画等のために、境界ボックスを指定量だけ外側（または内側）に広げる。
        /// </summary>
        public (float minX, float minY, float maxX, float maxY) ExpandedBounds(float margin) =>
            (MinX - margin, MinY - margin, MaxX + margin, MaxY + margin);
    }
}
