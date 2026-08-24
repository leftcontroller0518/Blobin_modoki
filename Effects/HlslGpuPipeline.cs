using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Blobin.Analysis;
using Blobin.Core;
using Blobin.Effects.Shaders;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;
using YukkuriMovieMaker.Commons;

namespace Blobin.Effects
{
    /// <summary>
    /// HLSLシェーダーおよびDirect2D GPUパイプラインを用いた高速処理エンジン。
    /// 1. HLSLカスタムシェーダーによるGPU上の特徴マップ抽出
    /// 2. Direct2Dエフェクト（色反転・色相回転・色変更・ぼかし等）のGPU処理
    /// 3. ブロブ領域およびオーバーレイの合成
    /// </summary>
    public sealed class HlslGpuPipeline : IDisposable
    {
        readonly IGraphicsDevicesAndContext devices;
        readonly ID2D1DeviceContext dc;

        FeatureExtractorCustomEffect? featureExtractor;
        Vortice.Direct2D1.Effects.Scale? scaleDownEffect;

        ID2D1Bitmap1? renderBitmap;
        ID2D1Bitmap1? readBitmap;
        byte[]? featureBuffer;
        int renderW, renderH;

        Vortice.Direct2D1.Effects.ColorMatrix? invertEffect;
        Vortice.Direct2D1.Effects.ColorMatrix? tintEffect;
        Vortice.Direct2D1.Effects.HueRotation? hueRotateEffect;
        Vortice.Direct2D1.Effects.GaussianBlur? blurEffect;

        bool disposed;

        public HlslGpuPipeline(IGraphicsDevicesAndContext devices)
        {
            this.devices = devices;
            dc = devices.DeviceContext;
        }

        public List<Blob> ProcessGpuFeatureMapAndDetect(
            ID2D1Image input,
            Vector4 inputBounds,
            DetectionParams p,
            FrameBuffer? prevBuffer,
            out FrameBuffer featureFrameBuffer)
        {
            float srcW = inputBounds.Z - inputBounds.X;
            float srcH = inputBounds.W - inputBounds.Y;
            int w = Math.Max(1, (int)Math.Ceiling(srcW));
            int h = Math.Max(1, (int)Math.Ceiling(srcH));

            int longSide = Math.Max(16, p.MaxBlobCount > 0 ? 240 : 240);
            float scale = longSide / Math.Max(srcW, srcH);
            int aw = Math.Max(2, (int)Math.Round(srcW * scale));
            int ah = Math.Max(2, (int)Math.Round(srcH * scale));

            EnsureBuffers(aw, ah);

            featureExtractor ??= new FeatureExtractorCustomEffect(devices);
            scaleDownEffect ??= new Vortice.Direct2D1.Effects.Scale(dc);

            featureExtractor.SetInput(0, input, true);
            featureExtractor.UpdateParameters(
                p.Mode,
                System.Windows.Media.Color.FromRgb(p.KeyR, p.KeyG, p.KeyB),
                (float)(p.KeyTolerance * 100.0),
                (float)p.HueMin,
                (float)p.HueMax);

            float scaleX = aw / (float)w;
            float scaleY = ah / (float)h;

            scaleDownEffect.SetInput(0, featureExtractor.Output, true);
            scaleDownEffect.Value = new Vector2(scaleX, scaleY);
            scaleDownEffect.CenterPoint = new Vector2(inputBounds.X, inputBounds.Y);
            scaleDownEffect.InterpolationMode = ScaleInterpolationMode.NearestNeighbor;

            var prevTarget = dc.Target;
            var prevTransform = dc.Transform;
            try
            {
                dc.Target = renderBitmap;
                dc.Transform = Matrix3x2.Identity;
                dc.BeginDraw();
                dc.Clear(new Color4(0, 0, 0, 0));
                dc.DrawImage(scaleDownEffect.Output, new Vector2(-inputBounds.X, -inputBounds.Y));
            }
            finally
            {
                dc.EndDraw();
                dc.Target = prevTarget;
                dc.Transform = prevTransform;

                featureExtractor.SetInput(0, null, true);
                scaleDownEffect.SetInput(0, null, true);
            }

            readBitmap!.CopyFromBitmap(renderBitmap!);
            var mapped = readBitmap.Map(MapOptions.Read);

            featureFrameBuffer = new FrameBuffer(aw, ah);
            try
            {
                int pitch = mapped.Pitch;
                for (int y = 0; y < ah; y++)
                {
                    IntPtr lineSrc = mapped.Bits + y * pitch;
                    for (int x = 0; x < aw; x++)
                    {
                        byte b = Marshal.ReadByte(lineSrc, x * 4);
                        byte g = Marshal.ReadByte(lineSrc, x * 4 + 1);
                        byte r = Marshal.ReadByte(lineSrc, x * 4 + 2);
                        byte a = Marshal.ReadByte(lineSrc, x * 4 + 3);
                        featureFrameBuffer.SetPixel(x, y, b, g, r, a);
                    }
                }
            }
            finally
            {
                readBitmap.Unmap();
            }

            var blobs = BlobDetector.Detect(featureFrameBuffer, prevBuffer, p);
            return blobs;
        }

        public ID2D1Image ApplyHlslInBlobEffect(
            ID2D1Image input,
            Vector4 inputBounds,
            IReadOnlyList<Blob> blobsOutput,
            InBlobEffectType effectType,
            double intensity01,
            int width,
            int height)
        {
            if (effectType == InBlobEffectType.None || blobsOutput.Count == 0)
                return input;

            ID2D1Image currentChain = input;

            switch (effectType)
            {
                case InBlobEffectType.Invert:
                    invertEffect ??= new Vortice.Direct2D1.Effects.ColorMatrix(dc);
                    invertEffect.SetInput(0, currentChain, true);
                    float ia = (float)Math.Clamp(intensity01, 0.0, 1.0);
                    invertEffect.Matrix = new Matrix5x4
                    {
                        M11 = 1 - 2 * ia, M22 = 1 - 2 * ia, M33 = 1 - 2 * ia, M44 = 1,
                        M51 = ia, M52 = ia, M53 = ia
                    };
                    currentChain = invertEffect.Output;
                    break;

                case InBlobEffectType.HueRotate:
                    hueRotateEffect ??= new Vortice.Direct2D1.Effects.HueRotation(dc);
                    hueRotateEffect.SetInput(0, currentChain, true);
                    hueRotateEffect.Angle = (float)(intensity01 * 360.0);
                    currentChain = hueRotateEffect.Output;
                    break;

                case InBlobEffectType.ColorShift:
                    tintEffect ??= new Vortice.Direct2D1.Effects.ColorMatrix(dc);
                    tintEffect.SetInput(0, currentChain, true);
                    float ta = (float)Math.Clamp(intensity01, 0.0, 1.0);
                    tintEffect.Matrix = new Matrix5x4
                    {
                        M11 = 1, M22 = 1 - ta, M33 = 1 + ta, M44 = 1
                    };
                    currentChain = tintEffect.Output;
                    break;

                case InBlobEffectType.Bleed:
                    blurEffect ??= new Vortice.Direct2D1.Effects.GaussianBlur(dc);
                    blurEffect.SetInput(0, currentChain, true);
                    blurEffect.StandardDeviation = (float)(intensity01 * 10.0);
                    currentChain = blurEffect.Output;
                    break;

                default:
                    return input;
            }

            return currentChain;
        }

        public void ClearEffectInputs()
        {
            featureExtractor?.SetInput(0, null, true);
            scaleDownEffect?.SetInput(0, null, true);
            invertEffect?.SetInput(0, null, true);
            tintEffect?.SetInput(0, null, true);
            hueRotateEffect?.SetInput(0, null, true);
            blurEffect?.SetInput(0, null, true);
        }

        void EnsureBuffers(int aw, int ah)
        {
            if (renderBitmap != null && renderW == aw && renderH == ah)
                return;

            renderBitmap?.Dispose();
            readBitmap?.Dispose();

            renderW = aw;
            renderH = ah;
            featureBuffer = new byte[aw * ah];

            var pixelFormat = new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied);
            var size = new SizeI(aw, ah);

            renderBitmap = dc.CreateBitmap(
                size,
                new BitmapProperties1(pixelFormat, 96, 96, BitmapOptions.Target));

            readBitmap = dc.CreateBitmap(
                size,
                new BitmapProperties1(pixelFormat, 96, 96, BitmapOptions.CpuRead | BitmapOptions.CannotDraw));
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            ClearEffectInputs();
            scaleDownEffect?.Dispose();
            featureExtractor?.Dispose();
            renderBitmap?.Dispose();
            readBitmap?.Dispose();
            invertEffect?.Dispose();
            tintEffect?.Dispose();
            hueRotateEffect?.Dispose();
            blurEffect?.Dispose();
        }
    }
}
