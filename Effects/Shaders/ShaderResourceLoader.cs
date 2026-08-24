using System;
using System.IO;
using System.Reflection;

namespace Blobin.Effects.Shaders;

internal static class ShaderResourceLoader
{
    public static byte[] GetShaderResource(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"Blobin.Effects.Shaders.{name}";
        using var stream = assembly.GetManifestResourceStream(resourceName) ?? throw new Exception($"Resource {resourceName} not found.");
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }
}
