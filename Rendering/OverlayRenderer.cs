using System.Numerics;
using System.Windows.Media;
using Blobin.Analysis;
using Blobin.Core;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;
using Color = System.Windows.Media.Color;
using DashStyle = Vortice.Direct2D1.DashStyle;

namespace Blobin.Rendering
{
    /// <summary>枠の見た目に関する、その時点で評価済みの値</summary>
    public readonly record struct StyleValues(
        bool Enabled,
        BlobShape Shape,
        bool UseBlobColor,
        Color Color,
        double LineWidth,
        double Opacity,
        double Margin,
        double CornerRadius,
        double CornerLength,
        bool FillEnabled,
        double FillOpacity,
        double MaxFillAreaFrac,
        bool ExcludeEdgeFill);

    /// <summary>接続線に関する、その時点で評価済みの値</summary>
    public readonly record struct ConnectionValues(
        bool Enabled,
        ConnectionMode Mode,
        bool UseBlobColor,
        double MaxDistanceFrac,
        int MaxConnectionsPerBlob,
        ConnectionLineStyle LineStyle,
        Color Color,
        double Width,
        double Opacity,
        double CurveStrength,
        ArrowPosition ArrowPosition,
        double ArrowSize);

    /// <summary>ラベルに関する、その時点で評価済みの値</summary>
    public readonly record struct LabelValues(
        bool Enabled,
        bool ShowId,
        bool ShowCoordinate,
        bool ShowSize,
        bool ShowArea,
        string CustomText,
        string Font,
        double FontSize,
        bool UseBlobColor,
        Color Color,
        LabelPosition Position);

    /// <summary>マーカーに関する、その時点で評価済みの値</summary>
    public readonly record struct MarkerValues(
        bool Enabled,
        MarkerShape Shape,
        bool UseBlobColor,
        Color Color,
        double Size,
        double LineWidth,
        double Opacity);

    /// <summary>
    /// 検出したブロブの枠・接続線・ラベル・マーカーをオフスクリーンビットマップに描画する。
    /// </summary>
    public sealed class OverlayRenderer : IDisposable
    {
        readonly IDWriteFactory dwriteFactory;
        readonly Dictionary<(string font, int size), IDWriteTextFormat> textFormatCache = [];

        ID2D1StrokeStyle? dashStroke;
        ID2D1StrokeStyle? dotStroke;

        ID2D1Bitmap1? overlayBitmap;
        int currentWidth = -1;
        int currentHeight = -1;
        readonly HashSet<(int, int)> previousNearestPairs = [];

        public OverlayRenderer()
        {
            dwriteFactory = DWrite.DWriteCreateFactory<IDWriteFactory>(
                Vortice.DirectWrite.FactoryType.Shared);
        }

        public ID2D1Bitmap1 Render(
            IGraphicsDevicesAndContext devices,
            int width,
            int height,
            IReadOnlyList<Blob> blobs,
            StyleValues style,
            ConnectionValues connection,
            LabelValues label,
            MarkerValues marker)
        {
            var context = devices.DeviceContext;

            EnsureBitmap(context, width, height);
            EnsureStrokeStyles(context.Factory);

            var previousTarget = context.Target;
            bool drawing = false;

            try
            {
                context.Target = overlayBitmap;

                context.BeginDraw();
                drawing = true;
                context.Clear(new Color4(0, 0, 0, 0));

                if (!connection.Enabled || connection.Mode == ConnectionMode.All)
                    previousNearestPairs.Clear();

                if (connection.Enabled)
                    DrawConnections(context, blobs, connection);

                foreach (var blob in blobs)
                {
                    if (style.Enabled)
                        DrawBlobShape(context, blob, style);

                    if (marker.Enabled)
                        DrawMarker(context, blob, marker);

                    if (label.Enabled)
                        DrawLabel(context, blob, label);
                }

            }
            finally
            {
                // BeginDrawに成功した場合のみEndDrawする（描画状態のまま残すと後続描画が壊れる）
                if (drawing)
                    context.EndDraw();
                context.Target = previousTarget;
            }

            return overlayBitmap!;
        }

        #region ブロブ形状

        void DrawBlobShape(
            ID2D1DeviceContext context,
            Blob blob,
            StyleValues style)
        {
            var (minX, minY, maxX, maxY) =
                blob.ExpandedBounds((float)style.Margin);

            if (maxX <= minX || maxY <= minY)
                return;

            bool touchesEdge = blob.MinX <= 0.5f || blob.MinY <= 0.5f || blob.MaxX >= currentWidth - 0.5f || blob.MaxY >= currentHeight - 0.5f;
            float canvasArea = Math.Max(1f, currentWidth * currentHeight);
            float blobAreaFrac = blob.Area / canvasArea;

            bool shouldFill = style.FillEnabled &&
                              (!style.ExcludeEdgeFill || !touchesEdge) &&
                              (style.MaxFillAreaFrac <= 0 || blobAreaFrac <= style.MaxFillAreaFrac);

            Color effectiveColor = style.UseBlobColor
                ? Color.FromRgb(blob.ColorR, blob.ColorG, blob.ColorB)
                : style.Color;

            using var stroke =
                context.CreateSolidColorBrush(
                    ToColor4(effectiveColor, style.Opacity));

            using var fill =
                context.CreateSolidColorBrush(
                    ToColor4(
                        effectiveColor,
                        shouldFill ? style.FillOpacity : 0));

            float lw = (float)style.LineWidth;

            switch (style.Shape)
            {
                case BlobShape.Rectangle:
                    {
                        var rect = MakeRect(minX, minY, maxX, maxY);

                        if (shouldFill)
                            context.FillRectangle(rect, fill);

                        if (lw > 0)
                            context.DrawRectangle(rect, stroke, lw);

                        break;
                    }

                case BlobShape.RoundedRectangle:
                    {
                        var rr = new RoundedRectangle(
                            MakeRect(minX, minY, maxX, maxY),
                            (float)style.CornerRadius,
                            (float)style.CornerRadius);

                        if (shouldFill)
                            context.FillRoundedRectangle(rr, fill);

                        if (lw > 0)
                            context.DrawRoundedRectangle(rr, stroke, lw);

                        break;
                    }

                case BlobShape.Circle:
                    {
                        var ellipse = new Ellipse(
                            new Vector2(
                                (minX + maxX) / 2,
                                (minY + maxY) / 2),
                            (maxX - minX) / 2,
                            (maxY - minY) / 2);

                        if (shouldFill)
                            context.FillEllipse(ellipse, fill);

                        if (lw > 0)
                            context.DrawEllipse(ellipse, stroke, lw);

                        break;
                    }

                case BlobShape.Crop:
                    {
                        if (shouldFill)
                        {
                            var rect = MakeRect(minX, minY, maxX, maxY);
                            context.FillRectangle(rect, fill);
                        }

                        DrawCropCorners(
                            context,
                            minX,
                            minY,
                            maxX,
                            maxY,
                            (float)style.CornerLength,
                            stroke,
                            lw);
                        break;
                    }

                case BlobShape.Bracket:
                    {
                        if (shouldFill)
                        {
                            var rect = MakeRect(minX, minY, maxX, maxY);
                            context.FillRectangle(rect, fill);
                        }

                        DrawBrackets(
                            context,
                            minX,
                            minY,
                            maxX,
                            maxY,
                            (float)style.CornerLength,
                            stroke,
                            lw);
                        break;
                    }

                case BlobShape.Polygon:
                    {
                        using var geometry =
                            BuildPathGeometry(
                                context,
                                blob.Contour,
                                (float)style.Margin,
                                blob);

                        if (geometry is not null)
                        {
                            if (shouldFill)
                                context.FillGeometry(geometry, fill);

                            if (lw > 0)
                                context.DrawGeometry(geometry, stroke, lw);
                        }

                        break;
                    }

                case BlobShape.Line:
                    {
                        using var geometry =
                            BuildPathGeometry(
                                context,
                                blob.Contour,
                                (float)style.Margin,
                                blob);

                        if (geometry is not null && lw > 0)
                            context.DrawGeometry(geometry, stroke, lw);

                        break;
                    }
            }
        }

        static void DrawCropCorners(
            ID2D1DeviceContext context,
            float minX,
            float minY,
            float maxX,
            float maxY,
            float len,
            ID2D1Brush brush,
            float lw)
        {
            len = Math.Min(
                len,
                Math.Min(maxX - minX, maxY - minY) / 2f);

            if (len <= 0 || lw <= 0)
                return;

            void Corner(
                Vector2 corner,
                Vector2 dirX,
                Vector2 dirY)
            {
                context.DrawLine(
                    corner,
                    corner + dirX * len,
                    brush,
                    lw);

                context.DrawLine(
                    corner,
                    corner + dirY * len,
                    brush,
                    lw);
            }

            Corner(
                new Vector2(minX, minY),
                new Vector2(1, 0),
                new Vector2(0, 1));

            Corner(
                new Vector2(maxX, minY),
                new Vector2(-1, 0),
                new Vector2(0, 1));

            Corner(
                new Vector2(minX, maxY),
                new Vector2(1, 0),
                new Vector2(0, -1));

            Corner(
                new Vector2(maxX, maxY),
                new Vector2(-1, 0),
                new Vector2(0, -1));
        }

        static void DrawBrackets(
            ID2D1DeviceContext context,
            float minX,
            float minY,
            float maxX,
            float maxY,
            float len,
            ID2D1Brush brush,
            float lw)
        {
            len = Math.Min(
                len,
                (maxY - minY) / 2f);

            if (lw <= 0)
                return;

            // 左側 「
            context.DrawLine(
                new Vector2(minX, minY),
                new Vector2(minX, maxY),
                brush,
                lw);

            context.DrawLine(
                new Vector2(minX, minY),
                new Vector2(minX + len, minY),
                brush,
                lw);

            context.DrawLine(
                new Vector2(minX, maxY),
                new Vector2(minX + len, maxY),
                brush,
                lw);

            // 右側 」
            context.DrawLine(
                new Vector2(maxX, minY),
                new Vector2(maxX, maxY),
                brush,
                lw);

            context.DrawLine(
                new Vector2(maxX, minY),
                new Vector2(maxX - len, minY),
                brush,
                lw);

            context.DrawLine(
                new Vector2(maxX, maxY),
                new Vector2(maxX - len, maxY),
                brush,
                lw);
        }

        ID2D1PathGeometry? BuildPathGeometry(
            ID2D1DeviceContext context,
            List<Vector2> contour,
            float margin,
            Blob blob)
        {
            if (contour.Count < 3)
                return null;

            var cx = blob.CentroidX;
            var cy = blob.CentroidY;

            var geometry = context.Factory.CreatePathGeometry();

            using var sink = geometry.Open();

            sink.SetFillMode(FillMode.Winding);

            Vector2 Offset(Vector2 p)
            {
                var dir =
                    p - new Vector2(cx, cy);

                if (dir.LengthSquared() < 0.0001f)
                    return p;

                return p +
                       Vector2.Normalize(dir) * margin;
            }

            sink.BeginFigure(
                Offset(contour[0]),
                FigureBegin.Filled);

            for (int i = 1; i < contour.Count; i++)
                sink.AddLine(Offset(contour[i]));

            sink.EndFigure(FigureEnd.Closed);
            sink.Close();

            return geometry;
        }

        #endregion

        #region 接続線

        void DrawConnections(
            ID2D1DeviceContext context,
            IReadOnlyList<Blob> blobs,
            ConnectionValues c)
        {
            if (blobs.Count < 2)
            {
                previousNearestPairs.Clear();
                return;
            }

            var pairs =
                BuildConnectionPairs(blobs, c);

            foreach (var (a, b) in pairs)
            {
                Color effectiveColor = c.UseBlobColor
                    ? Color.FromRgb(a.ColorR, a.ColorG, a.ColorB)
                    : c.Color;

                using var brush =
                    context.CreateSolidColorBrush(
                        ToColor4(effectiveColor, c.Opacity));

                var p0 =
                    new Vector2(
                        a.CentroidX,
                        a.CentroidY);

                var p1 =
                    new Vector2(
                        b.CentroidX,
                        b.CentroidY);

                DrawConnectionLine(
                    context,
                    p0,
                    p1,
                    brush,
                    (float)c.Width,
                    null, // ここに適切なStrokeStyleを渡す必要があります
                    c);
            }
        }

        List<(Blob, Blob)> BuildConnectionPairs(
            IReadOnlyList<Blob> blobs,
            ConnectionValues c)
        {
            var result = new List<(Blob, Blob)>();

            if (c.Mode == ConnectionMode.All)
            {
                for (int i = 0; i < blobs.Count; i++)
                {
                    for (int j = i + 1; j < blobs.Count; j++)
                    {
                        result.Add(
                            (blobs[i], blobs[j]));
                    }
                }

                return result;
            }

            // 近いブロブ同士のみ。接続先が僅差のときに毎フレーム切り替わらないよう、
            // 前フレームのペアを少し広い距離まで保持する。
            double width = blobs.Max(b => b.MaxX) - blobs.Min(b => b.MinX);
            double height = blobs.Max(b => b.MaxY) - blobs.Min(b => b.MinY);
            double diagonal = Math.Sqrt(width * width + height * height);
            double maxDist = diagonal * c.MaxDistanceFrac;
            double holdDist = maxDist > 0 ? maxDist * 1.25 : double.PositiveInfinity;
            int maxPerBlob = Math.Max(1, c.MaxConnectionsPerBlob);
            var byId = blobs.ToDictionary(b => b.Id);
            var selected = new HashSet<(int, int)>();
            var degree = new Dictionary<int, int>();

            bool InRange(Blob a, Blob b, double limit) =>
                !double.IsFinite(limit) || Distance(a, b) <= limit;

            bool AddPair((int, int) key, double limit)
            {
                if (selected.Contains(key) || !byId.TryGetValue(key.Item1, out var a) || !byId.TryGetValue(key.Item2, out var b))
                    return false;
                if (!InRange(a, b, limit))
                    return false;
                degree.TryGetValue(a.Id, out int degreeA);
                degree.TryGetValue(b.Id, out int degreeB);
                if (degreeA >= maxPerBlob || degreeB >= maxPerBlob)
                    return false;
                selected.Add(key);
                degree[a.Id] = degreeA + 1;
                degree[b.Id] = degreeB + 1;
                return true;
            }

            // 既存接続を先に確保する（ヒステリシス）。
            foreach (var key in previousNearestPairs.ToArray())
                AddPair(key, holdDist);

            var candidates = new List<(double distance, (int, int) key)>();
            foreach (var a in blobs)
            {
                foreach (var b in blobs)
                {
                    if (a.Id >= b.Id)
                        continue;
                    double distance = Distance(a, b);
                    if (maxDist <= 0 || distance <= maxDist)
                        candidates.Add((distance, (a.Id, b.Id)));
                }
            }

            foreach (var candidate in candidates.OrderBy(x => x.distance))
                AddPair(candidate.key, maxDist > 0 ? maxDist : double.PositiveInfinity);

            previousNearestPairs.Clear();
            foreach (var key in selected)
            {
                previousNearestPairs.Add(key);
                result.Add((byId[key.Item1], byId[key.Item2]));
            }
            return result;
        }

        static double Distance(Blob a, Blob b)
        {
            double dx =
                a.CentroidX -
                b.CentroidX;

            double dy =
                a.CentroidY -
                b.CentroidY;

            return Math.Sqrt(
                dx * dx +
                dy * dy);
        }

        void DrawConnectionLine(
            ID2D1DeviceContext context,
            Vector2 p0,
            Vector2 p1,
            ID2D1Brush brush,
            float width,
            ID2D1StrokeStyle? stroke,
            ConnectionValues c)
        {
            Vector2 mid =
                (p0 + p1) / 2;

            Vector2 dir =
                p1 - p0;

            Vector2 normal =
                dir.LengthSquared() > 0.0001f
                    ? Vector2.Normalize(
                        new Vector2(-dir.Y, dir.X))
                    : Vector2.Zero;

            float bow =
                (float)(c.CurveStrength * 0.15);

            switch (c.LineStyle)
            {
                case ConnectionLineStyle.Curve:
                    {
                        var ctrl =
                            mid + normal * bow;

                        DrawQuadratic(
                            context,
                            p0,
                            ctrl,
                            p1,
                            brush,
                            width,
                            stroke);

                        break;
                    }

                case ConnectionLineStyle.Spline:
                    {
                        var ctrl1 =
                            p0 +
                            (mid - p0) * 0.5f +
                            normal * bow;

                        var ctrl2 =
                            p1 +
                            (mid - p1) * 0.5f -
                            normal * bow;

                        DrawCubic(
                            context,
                            p0,
                            ctrl1,
                            ctrl2,
                            p1,
                            brush,
                            width,
                            stroke);

                        break;
                    }

                default:
                    context.DrawLine(
                        p0,
                        p1,
                        brush,
                        width,
                        stroke);
                    break;
            }

            DrawArrowHeads(
                context,
                p0,
                p1,
                brush,
                width,
                c);
        }

        static void DrawQuadratic(
            ID2D1DeviceContext context,
            Vector2 p0,
            Vector2 ctrl,
            Vector2 p1,
            ID2D1Brush brush,
            float width,
            ID2D1StrokeStyle? stroke)
        {
            const int segments = 16;

            var prev = p0;

            for (int i = 1; i <= segments; i++)
            {
                float t =
                    i / (float)segments;

                var a =
                    Vector2.Lerp(p0, ctrl, t);

                var b =
                    Vector2.Lerp(ctrl, p1, t);

                var pt =
                    Vector2.Lerp(a, b, t);

                context.DrawLine(
                    prev,
                    pt,
                    brush,
                    width,
                    stroke);

                prev = pt;
            }
        }

        static void DrawCubic(
            ID2D1DeviceContext context,
            Vector2 p0,
            Vector2 c1,
            Vector2 c2,
            Vector2 p1,
            ID2D1Brush brush,
            float width,
            ID2D1StrokeStyle? stroke)
        {
            const int segments = 20;

            var prev = p0;

            for (int i = 1; i <= segments; i++)
            {
                float t =
                    i / (float)segments;

                float u =
                    1 - t;

                var pt =
                    u * u * u * p0 +
                    3 * u * u * t * c1 +
                    3 * u * t * t * c2 +
                    t * t * t * p1;

                context.DrawLine(
                    prev,
                    pt,
                    brush,
                    width,
                    stroke);

                prev = pt;
            }
        }

        static void DrawArrowHeads(
            ID2D1DeviceContext context,
            Vector2 p0,
            Vector2 p1,
            ID2D1Brush brush,
            float width,
            ConnectionValues c)
        {
            if (c.ArrowPosition == ArrowPosition.None ||
                c.ArrowSize <= 0)
                return;

            if (c.ArrowPosition is
                ArrowPosition.End or
                ArrowPosition.Both)
            {
                DrawArrow(
                    context,
                    p1,
                    p1 - p0,
                    brush,
                    (float)c.ArrowSize);
            }

            if (c.ArrowPosition is
                ArrowPosition.Start or
                ArrowPosition.Both)
            {
                DrawArrow(
                    context,
                    p0,
                    p0 - p1,
                    brush,
                    (float)c.ArrowSize);
            }
        }

        static void DrawArrow(
            ID2D1DeviceContext context,
            Vector2 tip,
            Vector2 dir,
            ID2D1Brush brush,
            float size)
        {
            if (dir.LengthSquared() < 0.0001f)
                return;

            dir =
                Vector2.Normalize(dir);

            var normal =
                new Vector2(-dir.Y, dir.X);

            var back =
                tip - dir * size;

            var wing1 =
                back + normal * size * 0.5f;

            var wing2 =
                back - normal * size * 0.5f;

            context.DrawLine(
                tip,
                wing1,
                brush,
                1.5f);

            context.DrawLine(
                tip,
                wing2,
                brush,
                1.5f);
        }

        #endregion

        #region ラベル・マーカー

        void DrawMarker(
            ID2D1DeviceContext context,
            Blob blob,
            MarkerValues m)
        {
            Color effectiveColor = m.UseBlobColor
                ? Color.FromRgb(blob.ColorR, blob.ColorG, blob.ColorB)
                : m.Color;

            using var brush =
                context.CreateSolidColorBrush(
                    ToColor4(effectiveColor, m.Opacity));

            var c =
                new Vector2(
                    blob.CentroidX,
                    blob.CentroidY);

            float s = (float)m.Size;
            float lw = (float)m.LineWidth;

            switch (m.Shape)
            {
                case MarkerShape.Dot:
                    context.FillEllipse(
                        new Ellipse(
                            c,
                            s / 2,
                            s / 2),
                        brush);
                    break;

                case MarkerShape.Ring:
                    context.DrawEllipse(
                        new Ellipse(
                            c,
                            s / 2,
                            s / 2),
                        brush,
                        lw);
                    break;

                case MarkerShape.Square:
                    context.DrawRectangle(
                        MakeRect(
                            c.X - s / 2,
                            c.Y - s / 2,
                            c.X + s / 2,
                            c.Y + s / 2),
                        brush,
                        lw);
                    break;

                case MarkerShape.Cross:
                    context.DrawLine(
                        new Vector2(
                            c.X - s,
                            c.Y - s),
                        new Vector2(
                            c.X + s,
                            c.Y + s),
                        brush,
                        lw);

                    context.DrawLine(
                        new Vector2(
                            c.X - s,
                            c.Y + s),
                        new Vector2(
                            c.X + s,
                            c.Y - s),
                        brush,
                        lw);
                    break;

                case MarkerShape.Plus:
                    context.DrawLine(
                        new Vector2(
                            c.X - s,
                            c.Y),
                        new Vector2(
                            c.X + s,
                            c.Y),
                        brush,
                        lw);

                    context.DrawLine(
                        new Vector2(
                            c.X,
                            c.Y - s),
                        new Vector2(
                            c.X,
                            c.Y + s),
                        brush,
                        lw);
                    break;

                case MarkerShape.Diamond:
                    {
                        using var geometry =
                            context.Factory.CreatePathGeometry();

                        using var sink =
                            geometry.Open();

                        sink.BeginFigure(
                            new Vector2(
                                c.X,
                                c.Y - s),
                            FigureBegin.Filled);

                        sink.AddLine(
                            new Vector2(
                                c.X + s,
                                c.Y));

                        sink.AddLine(
                            new Vector2(
                                c.X,
                                c.Y + s));

                        sink.AddLine(
                            new Vector2(
                                c.X - s,
                                c.Y));

                        sink.EndFigure(
                            FigureEnd.Closed);

                        sink.Close();

                        context.DrawGeometry(
                            geometry,
                            brush,
                            lw);

                        break;
                    }
            }
        }

        void DrawLabel(
            ID2D1DeviceContext context,
            Blob blob,
            LabelValues l)
        {
            var text =
                BuildLabelText(blob, l);

            if (string.IsNullOrEmpty(text))
                return;

            var format =
                GetTextFormat(
                    l.Font,
                    l.FontSize);

            Color effectiveColor = l.UseBlobColor
                ? Color.FromRgb(blob.ColorR, blob.ColorG, blob.ColorB)
                : l.Color;

            using var brush =
                context.CreateSolidColorBrush(
                    ToColor4(effectiveColor, 100));

            float boxW = 400;
            float boxH = 200;

            var (x, y) =
                l.Position switch
                {
                    LabelPosition.Top =>
                        (
                            blob.CentroidX - boxW / 2,
                            blob.MinY - (float)l.FontSize - 4
                        ),

                    LabelPosition.Bottom =>
                        (
                            blob.CentroidX - boxW / 2,
                            blob.MaxY + 4
                        ),

                    LabelPosition.Left =>
                        (
                            blob.MinX - boxW - 4,
                            blob.CentroidY - boxH / 2
                        ),

                    LabelPosition.Right =>
                        (
                            blob.MaxX + 4,
                            blob.CentroidY - boxH / 2
                        ),

                    LabelPosition.Inside =>
                        (
                            blob.MinX + 4,
                            blob.MinY + 4
                        ),

                    _ =>
                        (
                            blob.CentroidX - boxW / 2,
                            blob.CentroidY - boxH / 2
                        )
                };

            var rect =
                MakeRect(
                    x,
                    y,
                    x + boxW,
                    y + boxH);

            context.DrawText(
                text,
                format,
                rect,
                brush,
                DrawTextOptions.NoSnap,
                Vortice.DCommon.MeasuringMode.Natural);
        }

        static string BuildLabelText(
            Blob blob,
            LabelValues l)
        {
            var lines =
                new List<string>();

            if (l.ShowId)
                lines.Add(
                    $"ID:{blob.Id:D2}");

            if (l.ShowCoordinate)
                lines.Add(
                    $"X:{blob.CentroidX:F0} Y:{blob.CentroidY:F0}");

            if (l.ShowSize)
                lines.Add(
                    $"W:{blob.Width:F0} H:{blob.Height:F0}");

            if (l.ShowArea)
                lines.Add(
                    $"AREA:{blob.Area:F0}");

            if (!string.IsNullOrEmpty(l.CustomText))
            {
                var custom = l.CustomText.Replace("{area}", blob.Area.ToString("F0"));
                lines.Add(custom);
            }

            return string.Join(
                "\n",
                lines);
        }

        IDWriteTextFormat GetTextFormat(
            string font,
            double size)
        {
            var key =
                (
                    font,
                    (int)Math.Round(size)
                );

            if (textFormatCache.TryGetValue(
                key,
                out var cached))
            {
                return cached;
            }

            var format =
                dwriteFactory.CreateTextFormat(
                    font,
                    null,
                    Vortice.DirectWrite.FontWeight.Bold,
                    Vortice.DirectWrite.FontStyle.Normal,
                    Vortice.DirectWrite.FontStretch.Normal,
                    (float)size,
                    "");

            textFormatCache[key] = format;

            return format;
        }

        #endregion

        static Vortice.RawRectF MakeRect(
            float left,
            float top,
            float right,
            float bottom) =>
            new(left, top, right, bottom);

        static Color4 ToColor4(
            Color c,
            double opacityPercent)
        {
            float a =
                (float)Math.Clamp(
                    opacityPercent / 100.0,
                    0,
                    1) *
                (c.A / 255f);

            return new Color4(
                c.R / 255f,
                c.G / 255f,
                c.B / 255f,
                a);
        }

        void EnsureBitmap(
            ID2D1DeviceContext context,
            int width,
            int height)
        {
            if (width == currentWidth &&
                height == currentHeight &&
                overlayBitmap is not null)
            {
                return;
            }

            overlayBitmap?.Dispose();

            var pixelFormat =
                new Vortice.DCommon.PixelFormat(
                    Format.B8G8R8A8_UNorm,
                    Vortice.DCommon.AlphaMode.Premultiplied);

            overlayBitmap =
                context.CreateBitmap(
                    new Vortice.Mathematics.SizeI(
                        width,
                        height),
                    new BitmapProperties1(
                        pixelFormat,
                        96,
                        96,
                        BitmapOptions.Target));

            currentWidth = width;
            currentHeight = height;
        }

        void EnsureStrokeStyles(
            ID2D1Factory factory)
        {
            dashStroke ??=
                factory.CreateStrokeStyle(
                    new StrokeStyleProperties
                    {
                        StartCap = CapStyle.Flat,
                        EndCap = CapStyle.Flat,
                        DashCap = CapStyle.Flat,
                        LineJoin = LineJoin.Miter,
                        MiterLimit = 10f,
                        DashStyle = DashStyle.Dash,
                        DashOffset = 0f
                    });

            dotStroke ??=
                factory.CreateStrokeStyle(
                    new StrokeStyleProperties
                    {
                        StartCap = CapStyle.Flat,
                        EndCap = CapStyle.Flat,
                        DashCap = CapStyle.Flat,
                        LineJoin = LineJoin.Miter,
                        MiterLimit = 10f,
                        DashStyle = DashStyle.Dot,
                        DashOffset = 0f
                    });
        }

        public void Dispose()
        {
            overlayBitmap?.Dispose();
            dashStroke?.Dispose();
            dotStroke?.Dispose();

            foreach (var f in textFormatCache.Values)
                f.Dispose();

            dwriteFactory.Dispose();
        }

        public void ResetTemporalState() => previousNearestPairs.Clear();
    }
}