using UnityEngine;

namespace ProjectionSpatialKit
{
    /// <summary>
    /// A self-emissive display panel (LCD/OLED monitor) as a venue output device. Unlike
    /// the projector there is no throw, lens or focus: the panel has a fixed physical size
    /// (from the <see cref="MonitorDeviceProfile"/> diagonal + aspect) and shows the content
    /// channel's captured frame directly, unlit, regardless of room lighting.
    ///
    /// The panel faces the transform's +Z (forward). A housing box (bezel) is drawn behind
    /// it and a BoxCollider covers the device so observer clicks can hit it; when the
    /// profile is a touch panel the surface maps those hits back to content UV.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class MonitorSurface : MonoBehaviour, IContentUVSurface
    {
        [Header("Source")]
        [Tooltip("Which content display this monitor shows (0 = the content's Display 1). " +
                 "The capture channel is resolved automatically — no wiring needed.")]
        [ContentDisplay, SerializeField] private int contentDisplay;

        [Header("Device profile (catalogue)")]
        [Tooltip("Monitor model catalogue. Drives the physical panel size, aspect and touch " +
                 "capability. Leave empty to use the manual size below.")]
        [SerializeField] private MonitorDeviceProfile deviceProfile;

        [Header("Manual size (used when no device profile)")]
        [Tooltip("Visible panel size in metres (width, height).")]
        [SerializeField] private Vector2 panelSizeMetres = new Vector2(1.21f, 0.68f); // ~55" 16:9
        [SerializeField] private float bezelMetres = 0.012f;

        [Header("Mounting")]
        [Tooltip("ON (recommended): the panel takes the SHAPE of its content channel and shows " +
                 "the image upright. A portrait content channel (e.g. 1080×1920) therefore " +
                 "displays on a portrait-shaped panel with no rotation and no horizontal squish. " +
                 "Turn OFF only to force a physical mount rotation of landscape content via " +
                 "Orientation below.")]
        [SerializeField] private bool matchContentAspect = true;
        [Tooltip("Used only when Match Content Aspect is OFF. Portrait = the panel is physically " +
                 "installed rotated 90° and LANDSCAPE content is rotated onto it.")]
        [SerializeField] private DisplayOrientation orientation = DisplayOrientation.Landscape;

        [Header("Appearance")]
        [Tooltip("Shown when there is no live content frame (Editor before Play).")]
        [SerializeField] private Color previewColor = new Color(0.10f, 0.35f, 1f);
        [SerializeField] private Color housingColor = new Color(0.08f, 0.08f, 0.09f);
        [SerializeField] private float housingDepthMetres = 0.045f;
        [Tooltip("Allow touch mapping even without a touch-panel profile (manual setups).")]
        [SerializeField] private bool forceTouchEnabled;

        // Scene-view info-plate display state (driven by the plate's own handles).
        [HideInInspector, SerializeField] private float infoPlateScale = 0.7f;
        [HideInInspector, SerializeField] private bool infoPlateMinimized;

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private BoxCollider box;
        private Mesh mesh;
        private Material panelMaterial;
        private Material housingMaterial;
        private Vector2 builtPanelSize = Vector2.zero;
        private float builtBezel = -1f;
        private bool builtRotateContent;
        private bool builtRotateContentValid;

        public MonitorDeviceProfile DeviceProfile => deviceProfile;
        public float InfoPlateScale => infoPlateScale;
        public bool InfoPlateMinimized => infoPlateMinimized;

        /// <summary>Content display this monitor shows (0 = the content's Display 1).</summary>
        public int ContentDisplay
        {
            get => contentDisplay;
            set => contentDisplay = value;
        }

        public int ContentDisplayIndex => contentDisplay;

        private ContentScreenCaptureSource resolvedSource;
        private int resolveAttemptFrame = -1;

        /// <summary>
        /// Capture channel auto-resolved from <see cref="ContentDisplay"/> (read-only;
        /// scene scans are throttled to one per frame while unresolved).
        /// </summary>
        public ContentScreenCaptureSource CaptureSource
        {
            get
            {
                if (resolvedSource != null && resolvedSource.DisplayIndex != contentDisplay)
                {
                    resolvedSource = null;
                }
                if (resolvedSource == null && Time.frameCount != resolveAttemptFrame)
                {
                    resolveAttemptFrame = Time.frameCount;
                    resolvedSource = OutputRouter.FindChannel(contentDisplay);
                }
                return resolvedSource;
            }
        }

        public DisplayOrientation Orientation
        {
            get => orientation;
            set => orientation = value;
        }

        /// <summary>Match the panel shape + image to the content channel's aspect (no rotation).</summary>
        public bool MatchContentAspect
        {
            get => matchContentAspect;
            set => matchContentAspect = value;
        }

        /// <summary>
        /// Visible panel size in metres AS INSTALLED, and whether the content image is rotated
        /// 90° on it. With <see cref="matchContentAspect"/> the panel is reshaped to the content
        /// channel's aspect and shown upright; otherwise the physical <see cref="orientation"/>
        /// mount decides (portrait rotates landscape content).
        /// </summary>
        private void GetInstalled(out Vector2 size, out bool rotateContent)
        {
            Vector2 native = deviceProfile != null ? deviceProfile.PanelSizeMetres : panelSizeMetres;

            if (matchContentAspect && TryGetContentAspect(out float contentAspect))
            {
                // Keep the panel's physical diagonal, but reshape it to the content's aspect so a
                // portrait channel gets a portrait panel — and never rotate (the content is
                // already authored at that aspect, so rotating would be the reported bug).
                float diagonal = Mathf.Max(0.05f, native.magnitude);
                float height = diagonal / Mathf.Sqrt(1f + contentAspect * contentAspect);
                size = new Vector2(height * contentAspect, height);
                rotateContent = false;
                return;
            }

            bool portrait = orientation == DisplayOrientation.Portrait;
            size = portrait ? new Vector2(native.y, native.x) : native;
            rotateContent = portrait;
        }

        /// <summary>
        /// The content channel's pixel aspect (width/height): from the live capture source at
        /// runtime, or the simulator's channel config in Edit mode. False when unknown.
        /// </summary>
        private bool TryGetContentAspect(out float aspect)
        {
            aspect = 16f / 9f;
            Vector2Int res = Vector2Int.zero;

            ContentScreenCaptureSource source = CaptureSource;
            if (source != null)
            {
                res = source.CanvasResolution;
            }
            if ((res.x <= 0 || res.y <= 0))
            {
                SpatialKitSimulator simulator = FindFirstObjectByType<SpatialKitSimulator>();
                if (simulator != null)
                {
                    res = simulator.GetChannelResolution(contentDisplay);
                }
            }
            if (res.x <= 0 || res.y <= 0)
            {
                return false;
            }
            aspect = (float)res.x / res.y;
            return true;
        }

        /// <summary>Visible panel size in metres as installed.</summary>
        public Vector2 PanelSize
        {
            get
            {
                GetInstalled(out Vector2 size, out _);
                return size;
            }
        }

        public float Bezel => deviceProfile != null ? deviceProfile.bezelMillimeters * 0.001f : bezelMetres;

        public bool IsTouchEnabled => forceTouchEnabled || (deviceProfile != null && deviceProfile.isTouchPanel);

        /// <summary>Force touch mapping even without a touch-panel profile.</summary>
        public bool ForceTouchEnabled
        {
            get => forceTouchEnabled;
            set => forceTouchEnabled = value;
        }

        /// <summary>
        /// Maps a world point on the panel front face back to content UV. Only touch-capable
        /// monitors map (a plain display should not synthesise touches from clicks).
        /// </summary>
        public bool TryWorldToContentUV(Vector3 worldPoint, out Vector2 contentUV)
        {
            contentUV = default;
            if (!IsTouchEnabled)
            {
                return false;
            }
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            GetInstalled(out Vector2 size, out bool rotateContent);
            if (Mathf.Abs(local.z) > 0.03f)
            {
                return false; // not on the front face (side or back of the housing)
            }
            // Panel "screen" coords as the AUDIENCE sees them: the viewer stands on the
            // +z (facing) side looking along -z, where local +x appears to their LEFT —
            // so the horizontal axis flips (same reason Unity's Quad primitive faces -z).
            float px = 0.5f - local.x / size.x;
            float py = local.y / size.y + 0.5f;
            contentUV = rotateContent
                ? new Vector2(py, 1f - px) // inverse of the portrait image rotation
                : new Vector2(px, py);
            return true;
        }

        /// <summary>
        /// The panel is self-emissive: its image is present exactly on the visible panel
        /// face (the observer ray already had line of sight to hit it — no beam to occlude).
        /// </summary>
        public bool IsImagePresentAt(Vector3 worldPoint)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            Vector2 size = PanelSize;
            return Mathf.Abs(local.z) <= 0.03f
                && Mathf.Abs(local.x) <= size.x * 0.5f
                && Mathf.Abs(local.y) <= size.y * 0.5f;
        }

        private void OnEnable()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            box = GetComponent<BoxCollider>();
            EnsureMesh();
            EnsureMaterials();
        }

        private void OnDisable()
        {
            if (mesh != null)
            {
                DestroyImmediate(mesh);
                mesh = null;
            }
            if (panelMaterial != null)
            {
                DestroyImmediate(panelMaterial);
                panelMaterial = null;
            }
            if (housingMaterial != null)
            {
                DestroyImmediate(housingMaterial);
                housingMaterial = null;
            }
            builtPanelSize = Vector2.zero;
            builtBezel = -1f;
            builtRotateContentValid = false;
        }

        private void LateUpdate()
        {
            EnsureMesh();
            EnsureMaterials();
            UpdatePanelImage();
        }

        private void UpdatePanelImage()
        {
            if (panelMaterial == null)
            {
                return;
            }
            Texture canvas = null;
            ContentScreenCaptureSource source = CaptureSource;
            bool hasFrame = source != null
                && source.TryGetFrameTexture(out canvas, out _)
                && canvas != null;
            if (hasFrame)
            {
                panelMaterial.SetTexture(BaseMapId, canvas);
                panelMaterial.SetColor(BaseColorId, Color.white);
            }
            else
            {
                panelMaterial.SetTexture(BaseMapId, null);
                panelMaterial.SetColor(BaseColorId, previewColor);
            }
            housingMaterial.SetColor(BaseColorId, housingColor);
        }

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private void EnsureMaterials()
        {
            if (panelMaterial != null && housingMaterial != null)
            {
                return;
            }
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null)
            {
                return;
            }
            // NOT ??= : after a domain reload the fields can hold DESTROYED material
            // wrappers (reference non-null, Unity-null), which ??= would keep forever.
            // The == below uses Unity's overload, so destroyed materials are recreated.
            if (panelMaterial == null)
            {
                panelMaterial = new Material(unlit) { hideFlags = HideFlags.HideAndDontSave, name = "SpatialKit Monitor Panel" };
            }
            if (housingMaterial == null)
            {
                housingMaterial = new Material(unlit) { hideFlags = HideFlags.HideAndDontSave, name = "SpatialKit Monitor Housing" };
            }
            meshRenderer.sharedMaterials = new[] { panelMaterial, housingMaterial };
        }

        private void EnsureMesh()
        {
            GetInstalled(out Vector2 size, out bool rotateContent);
            float bezel = Bezel;
            if (mesh != null && size == builtPanelSize && Mathf.Approximately(bezel, builtBezel)
                && builtRotateContentValid && rotateContent == builtRotateContent)
            {
                return;
            }
            builtPanelSize = size;
            builtBezel = bezel;
            builtRotateContent = rotateContent;
            builtRotateContentValid = true;

            if (mesh == null)
            {
                mesh = new Mesh { name = "SpatialKit Monitor", hideFlags = HideFlags.HideAndDontSave };
            }
            mesh.Clear();

            float hw = size.x * 0.5f;
            float hh = size.y * 0.5f;
            float ow = hw + bezel;
            float oh = hh + bezel;
            float depth = Mathf.Max(0.005f, housingDepthMetres);

            // Panel quad at z=0, facing +Z; housing box behind it (z in [-depth, 0.0005]).
            var vertices = new System.Collections.Generic.List<Vector3>
            {
                new Vector3(-hw, -hh, 0f), new Vector3(hw, -hh, 0f),
                new Vector3(hw, hh, 0f), new Vector3(-hw, hh, 0f)
            };
            // Texture coords mirror horizontally so the image reads correctly from the
            // audience (+z) side — local +x appears to the viewer's LEFT (see
            // TryWorldToContentUV). Portrait additionally rotates the content 90°.
            var uv = rotateContent
                ? new System.Collections.Generic.List<Vector2>
                {
                    new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f)
                }
                : new System.Collections.Generic.List<Vector2>
                {
                    new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f)
                };
            // Wound counter-clockwise when seen from +Z (the facing side).
            int[] panelTriangles = { 0, 1, 2, 0, 2, 3 };

            // Housing: cube corners. Its front face must sit BEHIND the panel (local -z;
            // +z is the facing direction) or the dark housing covers the image — recessed
            // 2 mm so the bezel ring shows without z-fighting.
            float zf = -0.002f;
            float zb = -depth;
            Vector3[] c =
            {
                new Vector3(-ow, -oh, zf), new Vector3(ow, -oh, zf), new Vector3(ow, oh, zf), new Vector3(-ow, oh, zf),
                new Vector3(-ow, -oh, zb), new Vector3(ow, -oh, zb), new Vector3(ow, oh, zb), new Vector3(-ow, oh, zb)
            };
            int baseIndex = vertices.Count;
            vertices.AddRange(c);
            for (int i = 0; i < 8; i++)
            {
                uv.Add(Vector2.zero);
            }
            int[] q =
            {
                0, 1, 2, 3, // front (+Z)
                5, 4, 7, 6, // back (-Z)
                4, 0, 3, 7, // left
                1, 5, 6, 2, // right
                3, 2, 6, 7, // top
                4, 5, 1, 0  // bottom
            };
            var housingTriangles = new System.Collections.Generic.List<int>();
            for (int f = 0; f < 6; f++)
            {
                int a = baseIndex + q[f * 4];
                int b = baseIndex + q[f * 4 + 1];
                int cc = baseIndex + q[f * 4 + 2];
                int d = baseIndex + q[f * 4 + 3];
                housingTriangles.AddRange(new[] { a, b, cc, a, cc, d });
            }

            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(panelTriangles, 0);
            mesh.SetTriangles(housingTriangles, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            meshFilter.sharedMesh = mesh;

            box.size = new Vector3(ow * 2f, oh * 2f, depth + 0.001f);
            box.center = new Vector3(0f, 0f, (zf + zb) * 0.5f);
        }
    }
}
