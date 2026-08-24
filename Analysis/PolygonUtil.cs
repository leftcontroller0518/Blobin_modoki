using System.Numerics;

namespace Blobin.Analysis
{
    /// <summary>
    /// 多角形に関する補助処理（内外判定など）。
    /// </summary>
    public static class PolygonUtil
    {
        /// <summary>
        /// レイキャスト法による点と多角形の内外判定。
        /// </summary>
        public static bool Contains(IReadOnlyList<Vector2> polygon, float x, float y)
        {
            if (polygon.Count < 3)
                return false;

            bool inside = false;
            int j = polygon.Count - 1;
            for (int i = 0; i < polygon.Count; i++)
            {
                var pi = polygon[i];
                var pj = polygon[j];

                if ((pi.Y > y) != (pj.Y > y))
                {
                    float xIntersect = (pj.X - pi.X) * (y - pi.Y) / (pj.Y - pi.Y + float.Epsilon) + pi.X;
                    if (x < xIntersect)
                        inside = !inside;
                }
                j = i;
            }
            return inside;
        }
    }
}
