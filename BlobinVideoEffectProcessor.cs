using System.IO;
using System.Numerics;
using System.Reflection;
using Blobin.Analysis;
using Blobin.Core;
using Blobin.Effects;
using Blobin.Rendering;
using Vortice.Direct2D1;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace Blobin
{
    /// <summary>診断用の簡易ファイルログ（プラグインフォルダに blobin-debug.log を出力）</summary>
    internal static class BlobinDebug
    {
        static readonly string logPath = Path.Combine(Path.GetDirectoryName(typeof(BlobinVideoEffectProcessor).Assembly.Location) ?? ".", "blobin-debug.log");

        public static void Log(string message)
        {
            try
            {
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\r\n");
            }
            catch { }
        }
    }
}

namespace Blobin
{
    /// <summary>
    /// Blobinの実行時処理。
    /// 毎フレーム、映像を解析してブロブを検出し、枠・接続線・ラベル・マーカー・
    /// ブロブ内エフェクトを合成した結果を出力する。
    /// </summary>
    internal class BlobinVideoEffectProcessor : IVideoEffectProcessor
    {
        readonly IGraphicsDevicesAndContext devices;
        readonly BlobinVideoEffect item;

        readonly FrameSampler analysisSampler;
        readonly FrameSampler? fullResSampler;
        readonly OverlayRenderer overlayRenderer;
        readonly FrameHistoryCache historyCache = new();
        readonly BlobTracker blobTracker = new();
        readonly HlslGpuPipeline hlslPipeline;
        int lastStabilizedFrame = -1;
        List<Blob>? lastStabilizedBlobs;
        DetectionEngine? lastDetectionEngine;
        readonly Vortice.Direct2D1.Effects.Composite compositeEffect;
        // 入力映像は原点中心（例：-960..960）のローカル座標を持つ。
        // オーバーレイ等の自前ビットマップ（原点基準 0..w, 0..h）を同じ座標系に合わせるための平行移動エフェクト。
        readonly Vortice.Direct2D1.Effects.AffineTransform2D overlayTranslate;
        Vortice.Direct2D1.Effects.AffineTransform2D? baseTranslate;
        readonly ID2D1Bitmap emptyBitmap;
        readonly object syncRoot = new();

        readonly ID2D1Image output;

        ID2D1Image? input;
        FrameBuffer? previousAnalysisBuffer;
        int lastAnalysisFrame = -1;
        ID2D1Bitmap? uploadedBaseBitmap;
        int updateCounter;
        int lastLoggedBlobCount = -1;
        bool disposed;

        public ID2D1Image Output => output;

        public BlobinVideoEffectProcessor(IGraphicsDevicesAndContext devices, BlobinVideoEffect item)
        {
            this.devices = devices;
            this.item = item;

            analysisSampler = new FrameSampler(devices);
            fullResSampler = new FrameSampler(devices);
            overlayRenderer = new OverlayRenderer();
            hlslPipeline = new HlslGpuPipeline(devices);
            emptyBitmap = devices.DeviceContext.CreateEmptyBitmap();

            compositeEffect = new Vortice.Direct2D1.Effects.Composite(devices.DeviceContext);
            overlayTranslate = new Vortice.Direct2D1.Effects.AffineTransform2D(devices.DeviceContext)
            {
                BorderMode = BorderMode.Soft,
            };
            // 生成直後からグラフを有効な状態にしておく。SetInput直後や最初のUpdate前でも
            // YMM4がOutput（compositeEffect.Output）を評価することがあり、
            // 入力が未設定(null)のまま評価されると0x8899001Eでクラッシュする。
            compositeEffect.SetInput(0, emptyBitmap, true);
            compositeEffect.SetInput(1, emptyBitmap, true);

            output = compositeEffect.Output; //EffectからgetしたOutputは必ずDisposeする

            BlobinDebug.Log($"Processor created (mode={(int)item.Detection.Mode} thr={item.Detection.Threshold.DefaultValue})");
        }

        public void SetInput(ID2D1Image? input)
        {
            lock (syncRoot)
            {
                if (disposed)
                    return;

                this.input = input;
                // YMM4 may evaluate Output immediately after SetInput. Keep the
                // Direct2D chain valid even before the next Update call.
                compositeEffect.SetInput(0, input, true);
            }
        }

        public void ClearInput()
        {
            lock (syncRoot)
            {
                if (disposed)
                    return;

                input = null;
                ClearEffectInputs();
                ResetTemporalState();
            }
        }

        public DrawDescription Update(EffectDescription effectDescription)
        {
            lock (syncRoot)
            {
                var drawDescription = effectDescription.DrawDescription;
                if (disposed)
                    return drawDescription;

                try
                {
                    return UpdateCore(effectDescription, drawDescription);
                }
                catch (Exception ex)
                {
                    BlobinDebug.Log($"Update EXCEPTION: {ex}");
                    try
                    {
                        // グラフを必ず有効な状態に保つ。Compositeの入力にnullを設定したまま
                        // YMM4側がGetImageLocalBounds等でOutputを評価すると
                        // D2DERR_INVALID_GRAPH_CONFIGURATION(0x8899001E)でクラッシュする。
                        baseTranslate?.SetInput(0, null, true);
                        ReleaseUploadedBaseBitmap();
                        compositeEffect.SetInput(0, input ?? emptyBitmap, true);
                        compositeEffect.SetInput(1, emptyBitmap, true);
                    }
                    catch { }
                    return drawDescription;
                }
            }
        }

        DrawDescription UpdateCore(EffectDescription effectDescription, DrawDescription drawDescription)
        {
            int updateSeq = ++updateCounter;

            if (input is null)
            {
                if (updateSeq == 1 || updateSeq % 120 == 0) BlobinDebug.Log($"Update #{updateSeq}: input=null");
                ClearEffectInputs();
                ResetTemporalState();
                return drawDescription;
            }

            int frame = effectDescription.ItemPosition.Frame;
            int length = effectDescription.ItemDuration.Frame;
            double fpsValue = effectDescription.FPS;
            if (!double.IsFinite(fpsValue) || fpsValue <= 0)
                fpsValue = 30;
            int fps = Math.Max(1, (int)Math.Round(fpsValue));

            var bounds = devices.DeviceContext.GetImageLocalBounds(input);
            float srcW = bounds.Right - bounds.Left;
            float srcH = bounds.Bottom - bounds.Top;

            if (!float.IsFinite(srcW) || !float.IsFinite(srcH) || srcW <= 0 || srcH <= 0)
            {
                ReleaseUploadedBaseBitmap();
                compositeEffect.SetInput(0, input, true);
                compositeEffect.SetInput(1, null, true);
                ResetTemporalState();
                return drawDescription;
            }

            //---- 1. 解析用に縮小したバッファへ読み出し ----
            int longSide = Math.Max(16, item.Detection.AnalysisResolution);
            float analysisScale = longSide / Math.Max(srcW, srcH);
            int aw = Math.Max(2, (int)Math.Round(srcW * analysisScale));
            int ah = Math.Max(2, (int)Math.Round(srcH * analysisScale));

            var analysisBuffer = analysisSampler.Sample(devices, input, bounds, aw, ah);

            // シーク・再生開始・プレビュー更新ではフレームが連続しないことがある。
            // そのフレームを「前フレーム」として差分検出すると、画面全体が動いた
            // ように扱われてブロブが一斉に入れ替わるため、差分と追跡状態を捨てる。
            bool frameDiscontinuity =
                lastAnalysisFrame >= 0 &&
                frame != lastAnalysisFrame &&
                frame != lastAnalysisFrame + 1;
            if (frameDiscontinuity)
            {
                previousAnalysisBuffer = null;
                blobTracker.Reset();
                overlayRenderer.ResetTemporalState();
                lastStabilizedFrame = -1;
                lastStabilizedBlobs = null;
                BlobinDebug.Log($"Analysis reset at frame={frame} previous={lastAnalysisFrame}");
            }

            if (updateSeq == 1 || updateSeq % 60 == 0)
            {
                var (nz, bright, avgLum) = analysisBuffer.Stats();
                BlobinDebug.Log($"Update #{updateSeq}: frame={frame} bounds=({bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom}) aw={aw} ah={ah} nzPx={nz} brightPx={bright} avgLum={avgLum:F3}");
            }

            //---- 2. ブロブ検出 ----
            var detectionParams = BuildDetectionParams(frame, length, fps);
            if (lastDetectionEngine != detectionParams.Engine)
            {
                previousAnalysisBuffer = null;
                blobTracker.Reset();
                overlayRenderer.ResetTemporalState();
                lastStabilizedFrame = -1;
                lastStabilizedBlobs = null;
                lastDetectionEngine = detectionParams.Engine;
            }

            List<Blob> blobs;
            if (detectionParams.Engine == DetectionEngine.Hlsl)
            {
                blobs = hlslPipeline.ProcessGpuFeatureMapAndDetect(
                    input,
                    new Vector4(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom),
                    detectionParams,
                    previousAnalysisBuffer,
                    out var gpuFeatureBuffer);
                previousAnalysisBuffer = gpuFeatureBuffer;
            }
            else
            {
                blobs = BlobDetector.Detect(analysisBuffer, previousAnalysisBuffer, detectionParams);
                previousAnalysisBuffer = analysisBuffer;
            }
            lastAnalysisFrame = frame;

            //---- 2b. フレーム間の荒ぶり抑制（平滑化） ----
            // 同一フレームの再描画時は前回の平滑化結果を再利用し、多重平滑化を防ぐ
            if (detectionParams.Smoothing > 0.0005)
            {
                if (frame != lastStabilizedFrame)
                {
                    blobs = blobTracker.Stabilize(blobs, detectionParams.Smoothing);
                    lastStabilizedBlobs = blobs;
                    lastStabilizedFrame = frame;
                }
                else if (lastStabilizedBlobs is not null)
                {
                    blobs = lastStabilizedBlobs;
                }
            }
            else
            {
                blobTracker.Reset();
                lastStabilizedFrame = frame;
                lastStabilizedBlobs = blobs;
            }

            if (updateSeq == 1 || updateSeq % 60 == 0 || lastLoggedBlobCount != blobs.Count)
            {
                BlobinDebug.Log($"Update #{updateSeq}: blobs={blobs.Count} mode={detectionParams.Mode} thr={detectionParams.Threshold:F3} smooth={detectionParams.Smoothing:F2} style={item.Style.Enabled} marker={item.Marker.Enabled} label={item.Label2.Enabled} showSrc={item.ShowSourceImage}");
                lastLoggedBlobCount = blobs.Count;
            }

            //時間ずらしエフェクト用の履歴（軽量化のため解析解像度で保持）
            var inBlobSettings = item.InBlobEffect;
            historyCache.SetCapacity(Math.Max(2, inBlobSettings.TimeOffsetFrames + 4));
            historyCache.Push(analysisBuffer);

            //---- 3. 出力解像度へスケール ----
            int ow = Math.Max(1, (int)Math.Round(srcW));
            int oh = Math.Max(1, (int)Math.Round(srcH));
            float scaleX = ow / (float)aw;
            float scaleY = oh / (float)ah;
            var blobsOutput = blobs.Select(b => b.Scaled(scaleX, scaleY)).ToList();

            //---- 4. ブロブ内エフェクト（必要な場合のみ読み出し/処理）----
            ID2D1Image baseImage;
            if (inBlobSettings.Enabled && blobsOutput.Count > 0 && detectionParams.Engine == DetectionEngine.Hlsl)
            {
                double intensity01 = inBlobSettings.Intensity.GetValue(frame, length, fps) / 100.0;
                var gpuEffectImage = hlslPipeline.ApplyHlslInBlobEffect(
                    input,
                    new Vector4(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom),
                    blobsOutput,
                    inBlobSettings.EffectType,
                    intensity01,
                    ow, oh);

                compositeEffect.SetInput(0, gpuEffectImage, true);
                ReleaseUploadedBaseBitmap();
                baseImage = gpuEffectImage;
            }
            else if (inBlobSettings.Enabled && blobsOutput.Count > 0 && fullResSampler is not null)
            {
                var fullBuffer = fullResSampler.Sample(devices, input, bounds, ow, oh);
                double intensity01 = inBlobSettings.Intensity.GetValue(frame, length, fps) / 100.0;

                var processed = InBlobEffectApplier.Apply(
                    fullBuffer,
                    blobsOutput,
                    inBlobSettings.EffectType,
                    intensity01,
                    frame,
                    inBlobSettings.UpdateInterval,
                    inBlobSettings.RandomSeed,
                    inBlobSettings.TimeOffsetFrames,
                    historyCache);

                var newBitmap = FrameSampler.UploadToBitmap(devices, processed);
                // 生成したビットマップは原点基準なので、入力映像（原点中心）と同じ座標系へ平行移動して合成する
                baseTranslate ??= new Vortice.Direct2D1.Effects.AffineTransform2D(devices.DeviceContext)
                {
                    BorderMode = BorderMode.Soft,
                };
                baseTranslate.SetInput(0, newBitmap, true);
                baseTranslate.TransformMatrix = System.Numerics.Matrix3x2.CreateTranslation(-ow / 2f, -oh / 2f);
                compositeEffect.SetInput(0, baseTranslate.Output, true);
                uploadedBaseBitmap?.Dispose();
                uploadedBaseBitmap = newBitmap;
                baseImage = newBitmap;
            }
            else
            {
                // baseTranslate may still retain the bitmap from the previous
                // frame. Detach it before releasing the COM wrapper.
                baseTranslate?.SetInput(0, null, true);
                compositeEffect.SetInput(0, input, true);
                ReleaseUploadedBaseBitmap();
                baseImage = input;
            }

            if (!item.ShowSourceImage)
            {
                compositeEffect.SetInput(0, emptyBitmap, true);
            }
            _ = baseImage; //(将来の拡張用に保持。現状はcompositeEffect側のInputで完結)

            //---- 5. オーバーレイ（枠・接続線・ラベル・マーカー）描画 ----
            var styleValues = BuildStyleValues(frame, length, fps);
            var connectionValues = BuildConnectionValues(frame, length, fps);
            var labelValues = BuildLabelValues(frame, length, fps);
            var markerValues = BuildMarkerValues(frame, length, fps);

            var overlayBitmap = overlayRenderer.Render(devices, ow, oh, blobsOutput, styleValues, connectionValues, labelValues, markerValues);
            // オーバーレイ（原点基準）を入力映像（原点中心）と同じ座標系へ平行移動して合成する
            overlayTranslate.SetInput(0, overlayBitmap, true);
            overlayTranslate.TransformMatrix = System.Numerics.Matrix3x2.CreateTranslation(-ow / 2f, -oh / 2f);
            compositeEffect.SetInput(1, overlayTranslate.Output, true);

            if (updateSeq == 1 || updateSeq % 60 == 0)
                BlobinDebug.Log($"Update #{updateSeq}: overlay rendered {ow}x{oh}");

            return drawDescription;
        }

        DetectionParams BuildDetectionParams(int frame, int length, int fps)
        {
            var d = item.Detection;
            return new DetectionParams
            {
                Engine = d.Engine,
                Mode = d.Mode,
                Direction = d.Direction,
                Threshold = d.Threshold.GetValue(frame, length, fps) / 100.0,
                KeyR = d.KeyColor.R,
                KeyG = d.KeyColor.G,
                KeyB = d.KeyColor.B,
                KeyTolerance = d.KeyColorTolerance.GetValue(frame, length, fps) / 100.0,
                RgbChannel = d.RgbChannel,
                HueMin = d.HueMin.GetValue(frame, length, fps),
                HueMax = d.HueMax.GetValue(frame, length, fps),
                MotionSensitivity = d.MotionSensitivity.GetValue(frame, length, fps) / 100.0,
                MinAreaFrac = d.MinArea.GetValue(frame, length, fps) / 100.0,
                MaxAreaFrac = d.MaxArea.GetValue(frame, length, fps) / 100.0,
                MaxBlobCount = d.MaxBlobCount,
                Denoise = d.DenoiseEnabled,
                Smoothing = d.Smoothing.GetValue(frame, length, fps) / 100.0,
                IgnoreEdgeBlobs = d.IgnoreEdgeBlobs,
                MaxBoundingBoxFrac = d.MaxBoundingBoxArea.GetValue(frame, length, fps) / 100.0,
                Overlap = d.Overlap,
                BlurRadius = (int)Math.Round(d.BlurRadius.GetValue(frame, length, fps)),
                DilateRadius = (int)Math.Round(d.DilateRadius.GetValue(frame, length, fps)),
            };
        }

        StyleValues BuildStyleValues(int frame, int length, int fps)
        {
            var s = item.Style;
            return new StyleValues(
                s.Enabled, s.Shape, s.UseBlobColor, s.Color,
                s.LineWidth.GetValue(frame, length, fps),
                s.Opacity.GetValue(frame, length, fps),
                s.Margin.GetValue(frame, length, fps),
                s.CornerRadius.GetValue(frame, length, fps),
                s.CornerLength.GetValue(frame, length, fps),
                s.FillEnabled,
                s.FillOpacity.GetValue(frame, length, fps),
                s.MaxFillArea.GetValue(frame, length, fps) / 100.0,
                s.ExcludeEdgeFill);
        }

        ConnectionValues BuildConnectionValues(int frame, int length, int fps)
        {
            var c = item.Connection;
            return new ConnectionValues(
                c.Enabled, c.Mode, c.UseBlobColor,
                c.MaxDistance.GetValue(frame, length, fps) / 100.0,
                c.MaxConnectionsPerBlob, c.LineStyle, c.Color,
                c.Width.GetValue(frame, length, fps),
                c.Opacity.GetValue(frame, length, fps),
                c.CurveStrength.GetValue(frame, length, fps),
                c.ArrowPosition,
                c.ArrowSize.GetValue(frame, length, fps));
        }

        LabelValues BuildLabelValues(int frame, int length, int fps)
        {
            var l = item.Label2;
            return new LabelValues(
                l.Enabled, l.ShowId, l.ShowCoordinate, l.ShowSize, l.ShowArea,
                l.CustomText, l.Font,
                l.FontSize.GetValue(frame, length, fps),
                l.UseBlobColor,
                l.Color, l.Position);
        }

        MarkerValues BuildMarkerValues(int frame, int length, int fps)
        {
            var m = item.Marker;
            return new MarkerValues(
                m.Enabled, m.Shape, m.UseBlobColor,
                m.Color,
                m.Size.GetValue(frame, length, fps),
                m.LineWidth.GetValue(frame, length, fps),
                m.Opacity.GetValue(frame, length, fps));
        }

        public void Dispose()
        {
            lock (syncRoot)
            {
                if (disposed)
                    return;
                disposed = true;

                input = null;
                ClearEffectInputs();
                ResetTemporalState();

                baseTranslate?.Dispose();
                baseTranslate = null;
                overlayTranslate.Dispose();
                compositeEffect.Dispose();

                uploadedBaseBitmap?.Dispose();
                uploadedBaseBitmap = null;
                output.Dispose();
                emptyBitmap.Dispose();

                analysisSampler.Dispose();
                fullResSampler?.Dispose();
                overlayRenderer.Dispose();
                hlslPipeline.Dispose();
            }
        }

        void ClearEffectInputs()
        {
            // Compositeの入力はnullにせず必ず有効な画像（空ビットマップ）を設定する。
            // null入力のままYMM4がOutputを評価すると0x8899001Eでクラッシュするため。
            compositeEffect.SetInput(0, emptyBitmap, true);
            compositeEffect.SetInput(1, emptyBitmap, true);
            overlayTranslate.SetInput(0, null, true);
            baseTranslate?.SetInput(0, null, true);
            hlslPipeline.ClearEffectInputs();
            ReleaseUploadedBaseBitmap();
        }

        void ReleaseUploadedBaseBitmap()
        {
            uploadedBaseBitmap?.Dispose();
            uploadedBaseBitmap = null;
        }

        void ResetTemporalState()
        {
            previousAnalysisBuffer = null;
            lastAnalysisFrame = -1;
            historyCache.Clear();
            blobTracker.Reset();
            overlayRenderer.ResetTemporalState();
            lastStabilizedFrame = -1;
            lastStabilizedBlobs = null;
            lastDetectionEngine = null;
        }
    }
}