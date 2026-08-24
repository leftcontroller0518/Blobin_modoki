Texture2D InputTexture : register(t0);
SamplerState InputSampler : register(s0);

cbuffer constants : register(b0)
{
    int Mode : packoffset(c0.x);
    float3 KeyColor : packoffset(c0.y);
    float KeyColorRange : packoffset(c1.x);
    float HueStart : packoffset(c1.y);
    float HueEnd : packoffset(c1.z);
};

// Mode Constants
#define MODE_ALPHA 0
#define MODE_BRIGHT 1
#define MODE_DARK 2
#define MODE_KEYCOLOR 3
#define MODE_MOTION 4 // Motion is handled differently (brightness output + CPU diff, or GPU diff)
#define MODE_RED 5
#define MODE_GREEN 6
#define MODE_BLUE 7
#define MODE_HIGH_SATURATION 8
#define MODE_LOW_SATURATION 9
#define MODE_HUE_RANGE 10
#define MODE_EDGE 11 // Edge requires Sobel, which samples neighbors, but we can do it on CPU or separate shader. For now, we output Brightness for Edge, and do Sobel on CPU since the feature map is very small (e.g. 480x270).

float RgbToHue(float r, float g, float b)
{
    float max_val = max(r, max(g, b));
    float min_val = min(r, min(g, b));
    float d = max_val - min_val;
    if (d == 0) return 0;
    
    float h = 0;
    if (max_val == r)
        h = 60.0 * fmod(((g - b) / d), 6.0);
    else if (max_val == g)
        h = 60.0 * (((b - r) / d) + 2.0);
    else
        h = 60.0 * (((r - g) / d) + 4.0);
        
    if (h < 0) h += 360.0;
    return h;
}

float4 main(
    float4 pos : SV_POSITION,
    float4 posScene : SCENE_POSITION,
    float4 uv0 : TEXCOORD0
) : SV_Target
{
    float4 color = InputTexture.Sample(InputSampler, uv0.xy);
    
    // Premultiplied Alpha to Straight Alpha for color processing
    float3 rgb = color.rgb;
    float a = color.a;
    if (a > 0)
    {
        rgb /= a;
    }
    
    float r = rgb.r;
    float g = rgb.g;
    float b = rgb.b;
    
    float feature = 0.0;
    
    if (Mode == MODE_ALPHA)
    {
        feature = a;
    }
    else if (Mode == MODE_BRIGHT || Mode == MODE_EDGE || Mode == MODE_MOTION)
    {
        feature = dot(rgb, float3(0.299, 0.587, 0.114));
    }
    else if (Mode == MODE_DARK)
    {
        feature = 1.0 - dot(rgb, float3(0.299, 0.587, 0.114));
    }
    else if (Mode == MODE_KEYCOLOR)
    {
        float3 diff = rgb - KeyColor;
        float dist = length(diff) / sqrt(3.0);
        float range = max(0.001, KeyColorRange);
        feature = saturate(1.0 - (dist / range));
    }
    else if (Mode == MODE_RED)
    {
        feature = r;
    }
    else if (Mode == MODE_GREEN)
    {
        feature = g;
    }
    else if (Mode == MODE_BLUE)
    {
        feature = b;
    }
    else if (Mode == MODE_HIGH_SATURATION)
    {
        float max_val = max(r, max(g, b));
        float min_val = min(r, min(g, b));
        if (max_val > 0)
            feature = (max_val - min_val) / max_val;
        else
            feature = 0.0;
    }
    else if (Mode == MODE_LOW_SATURATION)
    {
        float max_val = max(r, max(g, b));
        float min_val = min(r, min(g, b));
        if (max_val > 0)
            feature = 1.0 - (max_val - min_val) / max_val;
        else
            feature = 1.0;
    }
    else if (Mode == MODE_HUE_RANGE)
    {
        float h = RgbToHue(r, g, b);
        if (HueStart <= HueEnd)
        {
            feature = (h >= HueStart && h <= HueEnd) ? 1.0 : 0.0;
        }
        else
        {
            feature = (h >= HueStart || h <= HueEnd) ? 1.0 : 0.0;
        }
    }
    
    // We only need a single channel for the feature map, but output as grayscale with Alpha=1
    return float4(feature, feature, feature, 1.0);
}
