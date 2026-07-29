using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace ProjectionSpatialKit
{
    /// <summary>
    /// Additively loads an unmodified content scene into the running venue scene.
    /// Uses Build Settings when possible, otherwise falls back to the Editor-only
    /// in-play-mode loader so the spike does not depend on Build Settings entries.
    /// </summary>
    public sealed class ContentSceneLoader : MonoBehaviour
    {
        [Tooltip("Project path of the content scene to load additively (set by SpatialKitSimulator).")]
        [SerializeField] private string contentScenePath = "";
        [Tooltip("OPT-IN, NOT general. Offsets every root of the loaded content scene at load time. " +
                 "It does NOT follow objects spawned at runtime that use absolute world coordinates " +
                 "(e.g. projectiles/balls): those stay at the origin and desync from the moved content, " +
                 "which also drops them out of the content camera (so they vanish from the projection). " +
                 "Leave at (0,0,0) and rely on layer separation, which is the general mechanism. Use a " +
                 "non-zero offset only for content you know has no absolute-world runtime spawning.")]
        [SerializeField] private Vector3 contentSceneOffset = Vector3.zero;
        [Tooltip("Venue layer to exclude from the content camera so it never renders the venue " +
                 "geometry. Set by the bootstrap. -1 = no exclusion. (Defense-in-depth alongside the offset.)")]
        [SerializeField] private int venueLayerToExcludeFromContentCamera = -1;
        [Tooltip("Renderer index the content camera should use (the one WITH the content's " +
                 "fullscreen backdrop feature). The venue uses a different, default renderer. " +
                 "-1 = leave the camera on the URP default renderer.")]
        [SerializeField] private int contentCameraRendererIndex = -1;

        public string State { get; private set; } = "idle";
        public bool IsLoaded { get; private set; }

        // Programmatic configuration (used by SpatialKitSimulator; set before Start runs).
        public string ContentScenePath { get => contentScenePath; set => contentScenePath = value; }
        public int VenueLayerToExcludeFromContentCamera
        {
            get => venueLayerToExcludeFromContentCamera;
            set => venueLayerToExcludeFromContentCamera = value;
        }
        public int ContentCameraRendererIndex
        {
            get => contentCameraRendererIndex;
            set => contentCameraRendererIndex = value;
        }

        private IEnumerator Start()
        {
            if (string.IsNullOrWhiteSpace(contentScenePath))
            {
                // Not an error: a venue can be inspected without content loaded. Set the
                // content scene on the SpatialKitSimulator to project something.
                State = "no content scene set";
                yield break;
            }

            // Guard against the venue trying to load ITSELF as content — that recurses forever,
            // spawning venue after venue. It happens when the kit is added to the very scene that
            // is meant to BE the content. The venue must live in its own scene.
            if (contentScenePath == gameObject.scene.path)
            {
                State = "failed: content scene is the venue's own scene";
                Debug.LogError(
                    "[SpatialKit] コンテンツシーンが会場シーン自身に設定されています(無限ロードになるため中止)。\n" +
                    "会場はコンテンツとは別のシーンに置いてください。メニュー: " +
                    "Projection Spatial Kit ▸ Create Venue Scene For Current Scene", this);
                yield break;
            }

            State = "loading";
            AsyncOperation operation = null;

            if (Application.CanStreamedLevelBeLoaded(contentScenePath))
            {
                operation = SceneManager.LoadSceneAsync(contentScenePath, LoadSceneMode.Additive);
            }
#if UNITY_EDITOR
            else
            {
                // The content scene is NOT in Build Settings, so the reliable runtime path is
                // unavailable and we use an Editor-only fallback. This can leave the projection
                // BLANK on a cold first open (before the content scene has been opened once), and
                // it does NOT work in a build at all. Register the scene to fix both.
                Debug.LogWarning(
                    "[SpatialKit] コンテンツシーンが Build Settings に未登録のため、エディタ専用の" +
                    "フォールバックで additive ロードします。初回(コールド)時に投影が出ない/ビルドで" +
                    "読み込めない原因になります。File ▸ Build Settings に追加するか、" +
                    "『会場シーンを作成』/サンプル生成をやり直すと自動登録されます。\n  " + contentScenePath,
                    this);
                operation = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                    contentScenePath,
                    new LoadSceneParameters(LoadSceneMode.Additive));
            }
#endif

            if (operation == null)
            {
                State = "failed: scene not loadable: " + contentScenePath;
                Debug.LogError(State);
                yield break;
            }

            yield return operation;
            IsLoaded = true;
            State = "loaded: " + contentScenePath;
            Debug.Log("[SpatialKit] content scene loaded additively: " + contentScenePath);

            DeduplicateAudioListeners();

            // Move the whole content scene off-stage so it never overlaps the venue.
            // Blind to the content's structure — just shift every root object.
            if (contentSceneOffset != Vector3.zero)
            {
                Scene loaded = SceneManager.GetSceneByPath(contentScenePath);
                if (!loaded.IsValid())
                {
                    loaded = SceneManager.GetSceneAt(SceneManager.sceneCount - 1);
                }
                if (loaded.IsValid())
                {
                    foreach (GameObject root in loaded.GetRootGameObjects())
                    {
                        root.transform.position += contentSceneOffset;
                    }
                }
            }

            if (venueLayerToExcludeFromContentCamera >= 0 && Camera.main != null)
            {
                Camera.main.cullingMask &= ~(1 << venueLayerToExcludeFromContentCamera);
            }

            // Route the content camera to the renderer that carries the content's fullscreen
            // feature, so the projection keeps the backdrop even though the venue/default
            // renderer does not.
            if (contentCameraRendererIndex >= 0 && Camera.main != null)
            {
                Camera.main.GetUniversalAdditionalCameraData().SetRenderer(contentCameraRendererIndex);
            }
        }

        /// <summary>
        /// The content scene brings its own AudioListener (usually on its Main Camera). With the
        /// venue scene also loaded there can now be two active listeners, which Unity warns about
        /// every frame. Keep exactly one — preferring the content's, since audio belongs to the
        /// content — and disable any others (typically ones in the venue scene).
        /// </summary>
        private void DeduplicateAudioListeners()
        {
            AudioListener[] listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
            AudioListener keep = null;
            // Prefer a listener that lives in the content scene, not the venue scene.
            foreach (AudioListener listener in listeners)
            {
                if (listener.gameObject.scene != gameObject.scene)
                {
                    keep = listener;
                    break;
                }
            }
            if (keep == null && listeners.Length > 0)
            {
                keep = listeners[0]; // content had none: keep whatever exists (e.g. the venue's)
            }
            foreach (AudioListener listener in listeners)
            {
                if (listener != keep && listener.enabled)
                {
                    listener.enabled = false;
                }
            }
        }
    }
}
