using UnityEditor;
using UnityEngine;

namespace ProjectionSpatialKit.Editor
{
    /// <summary>
    /// Locates the bundled read-only assets that may be resolved by name (shader and font).
    /// Generated host-project assets use separate, fixed paths under Assets/ProjectionSpatialKit.
    /// </summary>
    internal static class SpatialKitPaths
    {
        /// <summary>Name of the runtime blit shader the projector cookie uses.</summary>
        internal const string CookieShaderName = VirtualProjectorLight.CookieShaderName;

        private const string CookieShaderAsset = "SpatialKitCookieBlit";
        private const string MonoFontAsset = "JetBrainsMono-Thin";

        private static string cookieShaderPath;
        private static string monoFontPath;

        /// <summary>Project path of the kit's cookie shader, or empty when not found.</summary>
        internal static string CookieShaderPath =>
            cookieShaderPath ??= FindAssetPath(CookieShaderAsset, "Shader");

        /// <summary>Project path of the kit's bundled monospace font, or empty when not found.</summary>
        internal static string MonoFontPath =>
            monoFontPath ??= FindAssetPath(MonoFontAsset, "Font");

        internal static Shader LoadCookieShader()
        {
            string path = CookieShaderPath;
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Shader>(path);
        }

        internal static Font LoadMonoFont()
        {
            string path = MonoFontPath;
            Font font = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Font>(path);
            return font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private static string FindAssetPath(string assetName, string typeFilter)
        {
            foreach (string guid in AssetDatabase.FindAssets($"{assetName} t:{typeFilter}"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == assetName)
                {
                    return path;
                }
            }
            return string.Empty;
        }
    }
}
