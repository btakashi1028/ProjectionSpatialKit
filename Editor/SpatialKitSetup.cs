using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ProjectionSpatialKit.Editor
{
    /// <summary>
    /// Project setup + compatibility for dropping the kit into an EXISTING project.
    /// Everything here is project-agnostic: the URP asset is resolved from GraphicsSettings
    /// (never a hard-coded path), the venue layer is claimed from the first free slot, and the
    /// runtime cookie shader is registered as an always-included shader so it survives a build
    /// (a Hidden shader reached only via Shader.Find is stripped otherwise — the projection
    /// would silently go black in a player).
    ///
    /// Deliberately NON-invasive: the project's DEFAULT URP renderer is left alone. The venue
    /// renderer is appended and assigned explicitly to venue cameras, so the host project's own
    /// scenes keep rendering exactly as before.
    /// </summary>
    internal static class SpatialKitSetup
    {
        internal const string VenueLayerName = "SpatialKitVenue";
        private const string VenueRendererName = "SpatialKit_VenueRenderer";
        private const string GeneratedFolder = "Assets/ProjectionSpatialKit/Generated";

        internal struct Check
        {
            public string Label;
            public bool Ok;
            public string Detail;
            public Action Fix;      // null when it cannot be auto-fixed
            public bool Blocking;   // the kit cannot work at all until this is resolved
        }

        // ------------------------------------------------------------------ diagnostics

        internal static List<Check> Diagnose()
        {
            var checks = new List<Check>();

            bool urp = CurrentUrpAsset() != null;
            checks.Add(new Check
            {
                Label = "URP (Universal Render Pipeline)",
                Ok = urp,
                Detail = urp ? "有効" : "URP が未設定です。本キットは URP 前提です。",
                Fix = null,
                Blocking = true
            });

#if ENABLE_INPUT_SYSTEM
            checks.Add(new Check { Label = "Input System", Ok = true, Detail = "有効", Fix = null });
#else
            checks.Add(new Check
            {
                Label = "Input System",
                Ok = false,
                Detail = "Input System が無効です。Player Settings の Active Input Handling を Input System (または Both) にしてください。",
                Fix = null,
                Blocking = true
            });
#endif

            int layer = FindVenueLayer();
            bool layerOk = layer >= 0;
            checks.Add(new Check
            {
                Label = "会場レイヤー (" + VenueLayerName + ")",
                Ok = layerOk,
                Detail = layerOk ? $"レイヤー {layer}" : "未登録(空きレイヤーに登録します)",
                Fix = layerOk ? null : (Action)(() => EnsureVenueLayer())
            });

            Shader cookie = SpatialKitPaths.LoadCookieShader();
            bool shaderFound = cookie != null;
            bool shaderIncluded = shaderFound && IsAlwaysIncluded(cookie);
            checks.Add(new Check
            {
                Label = "投影シェーダ (ビルド同梱)",
                Ok = shaderIncluded,
                Detail = !shaderFound
                    ? "シェーダが見つかりません(キットの導入が不完全です)"
                    : shaderIncluded
                        ? "Always Included Shaders に登録済み"
                        : "未登録。このままビルドすると投影が映りません。",
                Fix = shaderFound && !shaderIncluded ? (Action)EnsureCookieShaderAlwaysIncluded : null,
                Blocking = false
            });

            bool rendererOk = urp && FindVenueRendererIndex() >= 0;
            checks.Add(new Check
            {
                Label = "会場レンダラー",
                Ok = rendererOk,
                Detail = rendererOk
                    ? $"インデックス {FindVenueRendererIndex()}(会場カメラ専用)"
                    : "未作成(現行 URP に追加します。既定レンダラーは変更しません)",
                Fix = urp && !rendererOk ? (Action)(() => EnsureVenueRenderer()) : null
            });

            // Content scene must be in Build Settings so the venue's ADDITIVE load resolves
            // reliably (a cold Editor open can otherwise leave the projection blank, and builds
            // can't load it at all). Only checked when this scene has a simulator with a content
            // scene set, since that is the only time it is relevant.
            SpatialKitSimulator simulator = UnityEngine.Object.FindFirstObjectByType<SpatialKitSimulator>();
            string contentPath = simulator != null
                ? new SerializedObject(simulator).FindProperty("contentScenePath").stringValue
                : null;
            if (!string.IsNullOrEmpty(contentPath))
            {
                bool inBuild = false;
                foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
                {
                    if (s.path == contentPath && s.enabled) { inBuild = true; break; }
                }
                string capturedContentPath = contentPath;
                checks.Add(new Check
                {
                    Label = "コンテンツシーン (Build Settings)",
                    Ok = inBuild,
                    Detail = inBuild
                        ? "登録済み(additive ロードが確実に動作)"
                        : "未登録。初回オープンで投影が出ない/ビルドで読めない原因になります。",
                    Fix = inBuild ? null : (Action)(() =>
                        SpatialKitSceneBuilder.EnsureSceneInBuildSettings(capturedContentPath))
                });
            }

            return checks;
        }

        internal static bool HasBlockingIssue()
        {
            foreach (Check c in Diagnose())
            {
                if (!c.Ok && c.Blocking)
                {
                    return true;
                }
            }
            return false;
        }

        [MenuItem("Projection Spatial Kit/Run Project Setup", priority = 1)]
        private static void RunAllMenu()
        {
            RunAll();
            foreach (Check check in Diagnose())
            {
                Debug.Log($"[SpatialKit Setup] {(check.Ok ? "OK" : (check.Blocking ? "BLOCK" : "WARN"))} — {check.Label}: {check.Detail}");
            }
        }

        /// <summary>Runs every auto-fixable step. Safe to call repeatedly.</summary>
        internal static void RunAll()
        {
            EnsureVenueLayer();
            EnsureCookieShaderAlwaysIncluded();
            EnsureVenueRenderer();
            // The URP default renderer is deliberately NOT touched here — that is an explicit
            // opt-in (SetVenueAsDefaultRenderer), because it changes the host project's own rendering.
            AssetDatabase.SaveAssets();
        }

        /// <summary>True when the URP default renderer is the kit's venue renderer.</summary>
        internal static bool DefaultRendererIsVenue()
        {
            UniversalRenderPipelineAsset urp = CurrentUrpAsset();
            if (urp == null)
            {
                return false;
            }
            SerializedObject so = new SerializedObject(urp);
            int index = so.FindProperty("m_DefaultRendererIndex").intValue;
            SerializedProperty list = so.FindProperty("m_RendererDataList");
            if (index < 0 || index >= list.arraySize)
            {
                return false;
            }
            UnityEngine.Object data = list.GetArrayElementAtIndex(index).objectReferenceValue;
            return data != null && data.name == VenueRendererName;
        }

        /// <summary>
        /// OPT-IN: make the venue renderer the URP default, so the Scene view (and any camera
        /// without an explicit renderer) is drawn WITHOUT the host project's fullscreen renderer
        /// features — useful when those features tint the simulated venue. This DOES change how
        /// the host project's own scenes render, so it is never done automatically.
        /// </summary>
        internal static void SetVenueAsDefaultRenderer()
        {
            UniversalRenderPipelineAsset urp = CurrentUrpAsset();
            int venue = EnsureVenueRenderer();
            if (urp == null || venue < 0)
            {
                return;
            }
            SerializedObject so = new SerializedObject(urp);
            so.FindProperty("m_DefaultRendererIndex").intValue = venue;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(urp);
            AssetDatabase.SaveAssets();
            Debug.Log("[SpatialKit] 既定レンダラーを会場レンダラーにしました(Scene ビューからホストの描画機能を除外)。");
        }

        /// <summary>
        /// Points the URP default renderer back at the host's own renderer — the non-invasive
        /// state the kit ships with.
        /// </summary>
        internal static void RestoreHostDefaultRenderer()
        {
            UniversalRenderPipelineAsset urp = CurrentUrpAsset();
            if (urp == null || !DefaultRendererIsVenue())
            {
                return;
            }
            SerializedObject so = new SerializedObject(urp);
            SerializedProperty list = so.FindProperty("m_RendererDataList");
            for (int i = 0; i < list.arraySize; i++)
            {
                UnityEngine.Object data = list.GetArrayElementAtIndex(i).objectReferenceValue;
                if (data != null && data.name != VenueRendererName)
                {
                    so.FindProperty("m_DefaultRendererIndex").intValue = i;
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(urp);
                    AssetDatabase.SaveAssets();
                    Debug.Log($"[SpatialKit] 既定レンダラーをホストの '{data.name}' に戻しました。");
                    return;
                }
            }
        }

        // ----------------------------------------------------------------------- layer

        internal static int FindVenueLayer()
        {
            for (int i = 8; i < 32; i++)
            {
                if (LayerMask.LayerToName(i) == VenueLayerName)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>Venue layer index, claiming the first free user layer when absent.</summary>
        internal static int EnsureVenueLayer()
        {
            int existing = FindVenueLayer();
            if (existing >= 0)
            {
                return existing;
            }

            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets.Length == 0)
            {
                Debug.LogError("[SpatialKit] TagManager を読めませんでした。会場レイヤーを登録できません。");
                return -1;
            }
            SerializedObject tagManager = new SerializedObject(assets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            for (int i = 8; i < 32; i++)
            {
                SerializedProperty element = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(element.stringValue))
                {
                    element.stringValue = VenueLayerName;
                    tagManager.ApplyModifiedProperties();
                    return i;
                }
            }
            Debug.LogError($"[SpatialKit] 空きレイヤーがありません。'{VenueLayerName}' を手動で1つ空けてください。");
            return -1;
        }

        // ---------------------------------------------------------------------- shader

        private static bool IsAlwaysIncluded(Shader shader)
        {
            SerializedObject graphics = new SerializedObject(GraphicsSettings.GetGraphicsSettings());
            SerializedProperty list = graphics.FindProperty("m_AlwaysIncludedShaders");
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The projector resolves its blit shader with Shader.Find at runtime. Unity strips
        /// shaders no material references, so without this the built player has no cookie and
        /// projects nothing. Registering it as always-included is the supported fix.
        /// </summary>
        internal static void EnsureCookieShaderAlwaysIncluded()
        {
            Shader shader = SpatialKitPaths.LoadCookieShader();
            if (shader == null)
            {
                Debug.LogError("[SpatialKit] 投影シェーダ (" + SpatialKitPaths.CookieShaderName + ") が見つかりません。");
                return;
            }
            if (IsAlwaysIncluded(shader))
            {
                return;
            }
            SerializedObject graphics = new SerializedObject(GraphicsSettings.GetGraphicsSettings());
            SerializedProperty list = graphics.FindProperty("m_AlwaysIncludedShaders");
            int index = list.arraySize;
            list.InsertArrayElementAtIndex(index);
            list.GetArrayElementAtIndex(index).objectReferenceValue = shader;
            graphics.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }

        // -------------------------------------------------------------------- renderer

        internal static UniversalRenderPipelineAsset CurrentUrpAsset()
        {
            return (GraphicsSettings.currentRenderPipeline
                    ?? GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
        }

        /// <summary>Index of the venue renderer in the current URP asset, or -1.</summary>
        internal static int FindVenueRendererIndex()
        {
            UniversalRenderPipelineAsset urp = CurrentUrpAsset();
            if (urp == null)
            {
                return -1;
            }
            SerializedProperty list = new SerializedObject(urp).FindProperty("m_RendererDataList");
            for (int i = 0; i < list.arraySize; i++)
            {
                UnityEngine.Object data = list.GetArrayElementAtIndex(i).objectReferenceValue;
                if (data != null && data.name == VenueRendererName)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Appends a venue renderer (a copy of the project's default renderer, keeping only
        /// SSAO so the host project's own fullscreen features do not bleed onto the simulated
        /// venue) to the CURRENT URP asset. The project's default renderer index is deliberately
        /// left untouched — the host's scenes keep rendering as before; only the venue cameras
        /// are pointed at this renderer.
        /// </summary>
        internal static int EnsureVenueRenderer()
        {
            UniversalRenderPipelineAsset urp = CurrentUrpAsset();
            if (urp == null)
            {
                Debug.LogError("[SpatialKit] URP アセットが見つかりません(URP プロジェクトが必要です)。");
                return -1;
            }

            SerializedObject urpSerialized = new SerializedObject(urp);
            SerializedProperty list = urpSerialized.FindProperty("m_RendererDataList");
            SerializedProperty defaultIndexProp = urpSerialized.FindProperty("m_DefaultRendererIndex");
            if (list.arraySize == 0)
            {
                Debug.LogError("[SpatialKit] URP にレンダラーがありません。");
                return -1;
            }

            // Healthy fast path: exactly one venue entry, no missing (null) entries. Leave the
            // asset untouched so camera renderer indices stay stable.
            int venueCount = 0;
            bool hasMissing = false;
            for (int i = 0; i < list.arraySize; i++)
            {
                UnityEngine.Object data = list.GetArrayElementAtIndex(i).objectReferenceValue;
                if (data == null)
                {
                    hasMissing = true;
                }
                else if (data.name == VenueRendererName)
                {
                    venueCount++;
                }
            }
            if (venueCount == 1 && !hasMissing)
            {
                return FindVenueRendererIndex();
            }

            // Otherwise HEAL the list: a prior run may have left a missing reference (the venue
            // asset was deleted while still listed) and/or a duplicate venue entry. Snapshot the
            // asset the default points at so we can keep the default aimed at the SAME renderer
            // after compaction, then rebuild the list with the nulls and old venue entries removed.
            int oldDefault = Mathf.Clamp(defaultIndexProp.intValue, 0, list.arraySize - 1);
            UnityEngine.Object defaultAsset = list.GetArrayElementAtIndex(oldDefault).objectReferenceValue;
            bool defaultWasVenue = defaultAsset != null && defaultAsset.name == VenueRendererName;

            var kept = new System.Collections.Generic.List<UnityEngine.Object>();
            UnityEngine.Object cloneSource = (defaultAsset != null && !defaultWasVenue) ? defaultAsset : null;
            for (int i = 0; i < list.arraySize; i++)
            {
                UnityEngine.Object data = list.GetArrayElementAtIndex(i).objectReferenceValue;
                if (data == null || data.name == VenueRendererName)
                {
                    continue; // drop missing refs and any existing venue entries (dedupe)
                }
                kept.Add(data);
                if (cloneSource == null)
                {
                    cloneSource = data;
                }
            }
            string sourcePath = cloneSource != null ? AssetDatabase.GetAssetPath(cloneSource) : string.Empty;
            if (string.IsNullOrEmpty(sourcePath))
            {
                Debug.LogError("[SpatialKit] 複製元にできる(会場以外の)レンダラーが見つかりません。");
                return -1;
            }

            // Create the venue renderer fresh from the host's renderer, keeping only SSAO.
            Directory.CreateDirectory(GeneratedFolder);
            AssetDatabase.Refresh();
            string venuePath = $"{GeneratedFolder}/{VenueRendererName}.asset";
            if (AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(venuePath) != null)
            {
                AssetDatabase.DeleteAsset(venuePath);
            }
            if (!AssetDatabase.CopyAsset(sourcePath, venuePath))
            {
                Debug.LogError("[SpatialKit] 会場レンダラーを作成できませんでした: " + venuePath);
                return -1;
            }
            ScriptableRendererData venue = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(venuePath);
            if (venue == null)
            {
                return -1;
            }
            KeepSsaoOnly(venue);

            kept.Add(venue);
            int venueIndex = kept.Count - 1;

            // Write the compacted, deduped list back.
            urpSerialized = new SerializedObject(urp);
            list = urpSerialized.FindProperty("m_RendererDataList");
            defaultIndexProp = urpSerialized.FindProperty("m_DefaultRendererIndex");
            list.arraySize = kept.Count;
            for (int i = 0; i < kept.Count; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = kept[i];
            }

            // Keep the default pointing at the same renderer it did before (never silently move
            // it onto the venue): the venue if it already was, otherwise the host asset's new slot.
            int newDefault = defaultWasVenue ? venueIndex : Mathf.Max(0, kept.IndexOf(defaultAsset));
            defaultIndexProp.intValue = newDefault;

            urpSerialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(urp);
            AssetDatabase.SaveAssets();
            if (hasMissing || venueCount > 1)
            {
                Debug.Log($"[SpatialKit] URP レンダラー一覧を修復しました(欠損 {(hasMissing ? "あり" : "なし")}, " +
                          $"重複会場 {Mathf.Max(0, venueCount)} 個を統合)。会場レンダラー index={venueIndex}。");
            }
            return venueIndex;
        }

        /// <summary>Drops every renderer feature except SSAO from a copied renderer.</summary>
        private static void KeepSsaoOnly(ScriptableRendererData rendererData)
        {
            string path = AssetDatabase.GetAssetPath(rendererData);
            UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(path);
            UnityEngine.Object ssao = null;
            foreach (UnityEngine.Object asset in subAssets)
            {
                if (asset is ScreenSpaceAmbientOcclusion)
                {
                    ssao = asset;
                }
            }

            SerializedObject so = new SerializedObject(rendererData);
            SerializedProperty features = so.FindProperty("m_RendererFeatures");
            features.ClearArray();
            if (ssao != null)
            {
                features.arraySize = 1;
                features.GetArrayElementAtIndex(0).objectReferenceValue = ssao;
            }
            so.ApplyModifiedProperties();

            foreach (UnityEngine.Object asset in subAssets)
            {
                if (asset is ScriptableRendererFeature && !(asset is ScreenSpaceAmbientOcclusion))
                {
                    UnityEngine.Object.DestroyImmediate(asset, true);
                }
            }
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path); // rebuild the feature-map hash
        }
    }
}
