using System.IO;
using UnityEditor;
using UnityEngine;

namespace ProjectionSpatialKit.Editor
{
    /// <summary>
    /// Materials the kit generates into the host project (room walls, device bodies, bulbs).
    /// Written to a kit-owned generated folder so dropping the kit into any project needs no
    /// pre-existing material library.
    /// </summary>
    internal static class SpatialKitMaterials
    {
        private const string Folder = "Assets/ProjectionSpatialKit/Generated/Materials";

        internal static Material Room()
        {
            return EnsureLit("SpatialKit_Room", new Color(0.78f, 0.78f, 0.80f), 0f);
        }

        /// <summary>
        /// Device bodies: the main things the user manipulates, so give them a slightly lifted,
        /// cyan-tinted body with a faint emissive — clearly readable in the dark venue instead
        /// of disappearing as flat black.
        /// </summary>
        internal static Material Device()
        {
            Material material = EnsureLit("SpatialKit_Device", new Color(0.16f, 0.19f, 0.23f), 0.35f);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.SetColor("_EmissionColor", new Color(0f, 0.35f, 0.5f) * 0.5f);
            EditorUtility.SetDirty(material);
            return material;
        }

        internal static Material Bulb()
        {
            Material material = EnsureLit("SpatialKit_Bulb", new Color(0.05f, 0.05f, 0.05f), 0f);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.SetColor("_EmissionColor", new Color(1f, 0.93f, 0.78f) * 6f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material EnsureLit(string name, Color baseColor, float smoothness)
        {
            Directory.CreateDirectory(Folder);
            string path = $"{Folder}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader lit = Shader.Find("Universal Render Pipeline/Lit");
                if (lit == null)
                {
                    Debug.LogError("[SpatialKit] URP の Lit シェーダが見つかりません(URP プロジェクトが必要です)。");
                    return null;
                }
                AssetDatabase.Refresh();
                material = new Material(lit);
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", baseColor);
            material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
