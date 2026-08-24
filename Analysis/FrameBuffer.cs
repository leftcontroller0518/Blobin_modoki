namespace Blobin.Analysis
{
    /// <summary>
    /// CPU側に確保したBGRA(straight alpha, 1byte/成分)のピクセルバッファ。
    /// Direct2Dのビットマップからマップ(Map)して読み出した内容をコピーして保持する。
    /// </summary>
    public sealed class FrameBuffer
    {
        public int Width { get; }
        public int Height { get; }

        /// <summary>B,G,R,A の順で1ピクセル4byte</summary>
        public byte[] Pixels { get; }

        public FrameBuffer(int width, int height)
        {
            Width = Math.Max(1, width);
            Height = Math.Max(1, height);
            Pixels = new byte[Width * Height * 4];
        }

        /// <summary>
        /// マップされたビットマップ（pitch付きの生バイト列）からコピーして生成する。
        /// </summary>
        public static FrameBuffer FromMappedBytes(nint bitsPtr, int pitch, int width, int height)
        {
            var buffer = new FrameBuffer(width, height);
            unsafe
            {
                byte* src = (byte*)bitsPtr;
                fixed (byte* dstBase = buffer.Pixels)
                {
                    for (int y = 0; y < height; y++)
                    {
                        byte* srcRow = src + (long)y * pitch;
                        byte* dstRow = dstBase + (long)y * width * 4;
                        Buffer.MemoryCopy(srcRow, dstRow, width * 4, width * 4);
                    }
                }
            }
            return buffer;
        }

        /// <summary>
        /// 診断用：非透過ピクセル数・輝度しきい値(0〜1)以上のピクセル数・平均輝度を返す。
        /// </summary>
        public (int nonTransparent, int bright, double avgLuminance) Stats()
        {
            int nz = 0, bright = 0;
            long lumSum = 0;
            for (int i = 0; i < Pixels.Length; i += 4)
            {
                byte b = Pixels[i], g = Pixels[i + 1], r = Pixels[i + 2], a = Pixels[i + 3];
                if (a > 0) nz++;
                double lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
                if (lum >= 0.5) bright++;
                lumSum += (long)(lum * 1000);
            }
            int n = Pixels.Length / 4;
            return (nz, bright, n == 0 ? 0 : lumSum / (1000.0 * n));
        }

        public int IndexOf(int x, int y) => (y * Width + x) * 4;

        public void GetPixel(int x, int y, out byte b, out byte g, out byte r, out byte a)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height)
            {
                b = g = r = a = 0;
                return;
            }
            int i = IndexOf(x, y);
            b = Pixels[i + 0];
            g = Pixels[i + 1];
            r = Pixels[i + 2];
            a = Pixels[i + 3];
        }

        public void SetPixel(int x, int y, byte b, byte g, byte r, byte a)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height)
                return;
            int i = IndexOf(x, y);
            Pixels[i + 0] = b;
            Pixels[i + 1] = g;
            Pixels[i + 2] = r;
            Pixels[i + 3] = a;
        }

        /// <summary>相対輝度（0～1）。Rec.601近似。</summary>
        public static double Luminance(byte r, byte g, byte b) =>
            (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;

        /// <summary>RGB(0-255)をHSV(H:0-360, S:0-1, V:0-1)に変換する。</summary>
        public static void RgbToHsv(byte r, byte g, byte b, out double h, out double s, out double v)
        {
            double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
            double max = Math.Max(rd, Math.Max(gd, bd));
            double min = Math.Min(rd, Math.Min(gd, bd));
            double delta = max - min;

            v = max;
            s = max <= 0 ? 0 : delta / max;

            if (delta <= 0.00001)
            {
                h = 0;
                return;
            }

            if (max == rd)
                h = 60 * (((gd - bd) / delta) % 6);
            else if (max == gd)
                h = 60 * (((bd - rd) / delta) + 2);
            else
                h = 60 * (((rd - gd) / delta) + 4);

            if (h < 0)
                h += 360;
        }
    }
}
