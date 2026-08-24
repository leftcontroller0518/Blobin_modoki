using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct2D1;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;
using Blobin.Core;
using System.Windows.Media;

namespace Blobin.Effects.Shaders;

internal class FeatureExtractorCustomEffect : D2D1CustomShaderEffectBase
{
    public DetectionMode Mode
    {
        set => SetValue((int)EffectImpl.Properties.Mode, (int)value);
        get => (DetectionMode)GetFloatValue((int)EffectImpl.Properties.Mode); // Wait, D2D1 Custom Effect Properties are limited, usually Float, Vector2/3/4 etc. We use Vector4 or Int32?
    }
    
    // Instead of properties, we can just use SetValue.

    public FeatureExtractorCustomEffect(IGraphicsDevicesAndContext devices) : base(Create<EffectImpl>(devices))
    {
    }

    public void UpdateParameters(DetectionMode mode, Color keyColor, float keyColorRange, float hueStart, float hueEnd)
    {
        SetValue((int)EffectImpl.Properties.Mode, (int)mode);
        SetValue((int)EffectImpl.Properties.KeyColor, new Vector3(keyColor.R / 255f, keyColor.G / 255f, keyColor.B / 255f));
        SetValue((int)EffectImpl.Properties.KeyColorRange, keyColorRange);
        SetValue((int)EffectImpl.Properties.HueStart, hueStart);
        SetValue((int)EffectImpl.Properties.HueEnd, hueEnd);
    }

    [CustomEffect(1)]
    class EffectImpl : D2D1CustomShaderEffectImplBase<EffectImpl>
    {
        ConstantBuffer constantBuffer;

        [CustomEffectProperty(PropertyType.Int32, (int)Properties.Mode)]
        public int Mode
        {
            get => constantBuffer.Mode;
            set { constantBuffer.Mode = value; UpdateConstants(); }
        }

        [CustomEffectProperty(PropertyType.Vector3, (int)Properties.KeyColor)]
        public Vector3 KeyColor
        {
            get => constantBuffer.KeyColor;
            set { constantBuffer.KeyColor = value; UpdateConstants(); }
        }

        [CustomEffectProperty(PropertyType.Float, (int)Properties.KeyColorRange)]
        public float KeyColorRange
        {
            get => constantBuffer.KeyColorRange;
            set { constantBuffer.KeyColorRange = value; UpdateConstants(); }
        }

        [CustomEffectProperty(PropertyType.Float, (int)Properties.HueStart)]
        public float HueStart
        {
            get => constantBuffer.HueStart;
            set { constantBuffer.HueStart = value; UpdateConstants(); }
        }

        [CustomEffectProperty(PropertyType.Float, (int)Properties.HueEnd)]
        public float HueEnd
        {
            get => constantBuffer.HueEnd;
            set { constantBuffer.HueEnd = value; UpdateConstants(); }
        }

        public EffectImpl() : base(ShaderResourceLoader.GetShaderResource("FeatureExtractor.cso"))
        {
        }

        protected override void UpdateConstants()
        {
            drawInformation?.SetPixelShaderConstantBuffer(constantBuffer);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ConstantBuffer
        {
            public int Mode;
            public Vector3 KeyColor;
            public float KeyColorRange;
            public float HueStart;
            public float HueEnd;
            float padding; // Ensure 16-byte alignment if needed
        }

        public enum Properties
        {
            Mode = 0,
            KeyColor = 1,
            KeyColorRange = 2,
            HueStart = 3,
            HueEnd = 4
        }
    }
}
