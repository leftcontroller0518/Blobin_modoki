using System.IO;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;

namespace Blobin.Analysis
{
    /// <summary>
    /// GPU上のID2D1Imageを指定サイズのオフスクリーンビットマップに描画し、
    /// CPUから読み取れる形（<see cref="FrameBuffer"/>）にコピーする。
    /// また、CPUで加工したピクセルを再度GPUビットマップにアップロードする。
    /// </summary>
    public sealed class FrameSampler : IDisposable
    {
        readonly Vortice.Direct2D1.Effects.Scale scaleEffect;
        static readonly string LogPath = Path.Combine(Path.GetDirectoryName(typeof(FrameSampler).Assembly.Location) ?? ".", "blobin-debug.log");
        int sampleCounter;

        ID2D1Bitmap1? renderTargetBitmap;
        ID2D1Bitmap1? cpuReadBitmap;
        int currentWidth = -1, currentHeight = -1;

        public FrameSampler(IGraphicsDevicesAndContext devices)
        {
            scaleEffect = new Vortice.Direct2D1.Effects.Scale(devices.DeviceContext);
            scaleEffect.InterpolationMode = Vortice.Direct2D1.ScaleInterpolationMode.Linear;
        }

        /// <summary>
        /// 指定した画像を width x height のCPU読み取り可能なバッファへ描画・コピーする。
        /// </summary>
        public FrameBuffer Sample(IGraphicsDevicesAndContext devices, ID2D1Image source, Vortice.RawRectF sourceBounds, int width, int height)
        {
            var context = devices.DeviceContext;
            EnsureBitmaps(devices, width, height);

            float srcW = Math.Max(1, sourceBounds.Right - sourceBounds.Left);
            float srcH = Math.Max(1, sourceBounds.Bottom - sourceBounds.Top);

            scaleEffect.SetInput(0, source, true);
            scaleEffect.Value = new System.Numerics.Vector2(width / srcW, height / srcH);
            scaleEffect.CenterPoint = new System.Numerics.Vector2(sourceBounds.Left, sourceBounds.Top);

            var previousTarget = context.Target;
            var previousTransform = context.Transform;
            bool drawing = false;
            try
            {
                context.Target = renderTargetBitmap;
                context.Transform = System.Numerics.Matrix3x2.Identity;
                context.BeginDraw();
                drawing = true;
                context.Clear(new Color4(0, 0, 0, 0));
                // 入力画像のローカル座標は原点中心（例：-960..960）の場合がある。
                // ScaleエフェクトはCenterPoint=(Left,Top)の周りで拡縮するため出力は(Left,Top)起点になる。
                // それを(-Left,-Top)だけずらして描画することで、ターゲット(0,0)-(width,height)に正しく収める。
                context.DrawImage(scaleEffect.Output, new System.Numerics.Vector2(-sourceBounds.Left, -sourceBounds.Top));
            }
            finally
            {
                // BeginDrawに成功した場合のみEndDrawする（描画状態のまま残すと後続描画が壊れる）
                if (drawing)
                    context.EndDraw();
                context.Target = previousTarget;
                context.Transform = previousTransform;
                scaleEffect.SetInput(0, null, true);
            }

            cpuReadBitmap!.CopyFromBitmap(renderTargetBitmap!);

            var mapped = cpuReadBitmap.Map(MapOptions.Read);
            try
            {
                var fb = FrameBuffer.FromMappedBytes(mapped.Bits, mapped.Pitch, width, height);
                if (sampleCounter++ < 5)
                {
                    var (nz, bright, avg) = fb.Stats();
                    System.IO.File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] FrameSampler {width}x{height} scale=({width / srcW:F3},{height / srcH:F3}) nzPx={nz} brightPx={bright} avgLum={avg:F3}\r\n");
                }
                return fb;
            }
            finally
            {
                cpuReadBitmap.Unmap();
            }
        }

        /// <summary>
        /// CPU上で加工したピクセルバッファを、描画に使えるID2D1Bitmapとして作成する。
        /// 呼び出し側でDisposeする必要がある。
        /// </summary>
        public static ID2D1Bitmap UploadToBitmap(IGraphicsDevicesAndContext devices, FrameBuffer buffer)
        {
            var props = new BitmapProperties(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied));
            unsafe
            {
                fixed (byte* p = buffer.Pixels)
                {
                    return devices.DeviceContext.CreateBitmap(
                        new Vortice.Mathematics.SizeI(buffer.Width, buffer.Height),
                        (nint)p,
                        buffer.Width * 4,
                        props);
                }
            }
        }

        void EnsureBitmaps(IGraphicsDevicesAndContext devices, int width, int height)
        {
            if (width == currentWidth && height == currentHeight && renderTargetBitmap is not null && cpuReadBitmap is not null)
                return;

            renderTargetBitmap?.Dispose();
            cpuReadBitmap?.Dispose();

            var context = devices.DeviceContext;
            var pixelFormat = new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied);
            var size = new Vortice.Mathematics.SizeI(width, height);

            renderTargetBitmap = context.CreateBitmap(
                size,
                new BitmapProperties1(pixelFormat, 96, 96, BitmapOptions.Target));

            cpuReadBitmap = context.CreateBitmap(
                size,
                new BitmapProperties1(pixelFormat, 96, 96, BitmapOptions.CpuRead | BitmapOptions.CannotDraw));

            currentWidth = width;
            currentHeight = height;
        }

        public void Dispose()
        {
            scaleEffect.SetInput(0, null, true);
            scaleEffect.Dispose();
            renderTargetBitmap?.Dispose();
            cpuReadBitmap?.Dispose();
        }
    }
}
