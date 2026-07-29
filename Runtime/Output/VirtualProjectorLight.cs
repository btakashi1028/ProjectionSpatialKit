using UnityEngine;

namespace ProjectionSpatialKit
{
    /// <summary>
    /// Spec §4.3 / §12 spike: a projector is hardware defined only by its pose and
    /// intrinsics (throw ratio + image aspect). It emits a fixed frustum forward and
    /// the image lands wherever that frustum hits geometry — the projector does NOT
    /// know where any wall is and never re-aims at one. Moving or rotating it makes
    /// the projected image slide and keystone on the surface, exactly like real
    /// hardware.
    ///
    /// Implemented as a URP spot light whose cone half-angle is derived once from the
    /// throw ratio (independent of distance). The captured content canvas is
    /// letterboxed into the square cookie at <see cref="cookieContentScale"/> so its
    /// rectangle subtends the projector's horizontal FOV; the circular cone
    /// attenuation masks the surrounding black.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Light))]
    public sealed class VirtualProjectorLight : MonoBehaviour, IContentUVSurface
    {
        /// <summary>Blit shader the cookie is drawn with (shipped inside the kit).</summary>
        public const string CookieShaderName = "Hidden/SpatialKitCookieBlit";

        [Header("Source")]
        [Tooltip("Which content display this projector shows (0 = the content's Display 1). " +
                 "The capture channel is resolved automatically — no wiring needed.")]
        [ContentDisplay, SerializeField] private int contentDisplay;

        [Header("Device profile (catalogue)")]
        [Tooltip("Projector model catalogue. When set, it drives throw ratio (via Zoom), image " +
                 "aspect and lens-shift range. Leave empty to use the manual intrinsics below.")]
        [SerializeField] private ProjectorDeviceProfile deviceProfile;
        [Tooltip("Zoom position within the device's throw-ratio range (0 = widest, 1 = tightest).")]
        [SerializeField, Range(0f, 1f)] private float zoom = 0.5f;

        [Header("Intrinsics (used when no device profile)")]
        [Tooltip("Throw ratio = throw distance / image width. Smaller = wider image. Catalogue value.")]
        [SerializeField] private float throwRatio = 1.5f;
        [Tooltip("Projected image aspect (width/height). Overridden by capture source / profile.")]
        [SerializeField] private float imageAspect = 16f / 9f;
        [Tooltip("Max throw distance the light reaches, metres.")]
        [SerializeField] private float maxThrowDistance = 15f;

        [Header("Cookie / appearance")]
        [Tooltip("Fraction of the cone the rectangular image fills. Keep well below 1 so the " +
                 "rectangle stays inside the circular cone (no rounded clipping) even with lens shift.")]
        [SerializeField, Range(0.2f, 0.9f)] private float cookieContentScale = 0.5f;
        [SerializeField] private int cookieResolution = 1024;
        [SerializeField] private float intensity = 6f;
        [Tooltip("Solid colour projected as a static test image when there is no live content " +
                 "(e.g. in the Editor before Play), so lens shift / keystone / focus can be checked.")]
        [SerializeField] private Color previewColor = new Color(0.10f, 0.35f, 1f);
        [Tooltip("REAR projection: the image is mirrored because the audience views the screen " +
                 "from the far side. Leave OFF for normal FRONT projection — a front-projected " +
                 "image reads correctly (verified: content-left lands on the viewer's left).")]
        [SerializeField] private bool mirrorCookieHorizontally;
        [Tooltip("Extra vertical flip of the cookie, if the projected image is upside down.")]
        [SerializeField] private bool flipCookieVertically;
        [Tooltip("Projector shadows: occluders in the beam cast shadows on the wall (realistic).")]
        [SerializeField] private LightShadows projectorShadows = LightShadows.Soft;

        [Header("Mounting")]
        [Tooltip("Portrait = the projector is installed rotated 90°, so the image stands " +
                 "upright on the wall (content top edge along the image's left side). " +
                 "Touch mapping follows automatically.")]
        [SerializeField] private DisplayOrientation imageOrientation = DisplayOrientation.Landscape;

        [Header("Lens / keystone / focus (short-throw)")]
        [Tooltip("Lens shift: shifts the image within the cone WITHOUT moving the projector. " +
                 "(-1..1 of the available margin in each axis.) Typical of short-throw projectors.")]
        [SerializeField] private Vector2 lensShift = Vector2.zero;
        [Tooltip("Vertical keystone correction: trapezoid that makes the top edge narrower (+) or " +
                 "wider (-) than the bottom, to counter an up/down projector tilt.")]
        [SerializeField, Range(-0.6f, 0.6f)] private float verticalKeystone = 0f;
        [Tooltip("Focus distance in metres: the throw distance at which the image is sharp.")]
        [SerializeField] private float focusDistance = 3f;
        [Tooltip("Aperture as an f-number. Lower f (e.g. f/1.4) = shallow depth of field = strong " +
                 "defocus away from the focus distance. Higher f (e.g. f/16) ≈ pinhole, mostly sharp.")]
        [SerializeField, Range(1f, 22f)] private float aperture = 8f;

        // Scene-view info-plate display state (driven by the plate's own handles, not the Inspector).
        [HideInInspector, SerializeField] private float infoPlateScale = 0.7f;
        [HideInInspector, SerializeField] private bool infoPlateMinimized;

        private const float FocusBlurScale = 3f;

        private Light projectorLight;
        private RenderTexture cookieRt;
        private RenderTexture scaledRt;
        private Material cookieBlitMaterial;

        public Light ProjectorLight => projectorLight;

        // Read-only accessors for the Scene-view info gizmo.
        public ProjectorDeviceProfile DeviceProfile => deviceProfile;
        public float Zoom => zoom;
        public float FocusDistance => focusDistance;
        public float Aperture => aperture;
        public Vector2 LensShift => lensShift;
        public float VerticalKeystone => verticalKeystone;
        public float ImageAspect => Aspect;
        public DisplayOrientation ImageOrientation
        {
            get => imageOrientation;
            set => imageOrientation = value;
        }
        public float InfoPlateScale => infoPlateScale;
        public bool InfoPlateMinimized => infoPlateMinimized;

        /// <summary>Effective throw ratio: from the device profile (via Zoom) or the manual value.</summary>
        public float EffectiveThrowRatio => deviceProfile != null ? deviceProfile.ThrowRatioAt(zoom) : throwRatio;

        /// <summary>Logical output channel = the content display index this projector shows.</summary>
        public int ContentDisplayIndex => contentDisplay;

        /// <summary>Content display this projector shows (0 = the content's Display 1).</summary>
        public int ContentDisplay
        {
            get => contentDisplay;
            set => contentDisplay = value;
        }

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

        /// <summary>Horizontal field of view (degrees) derived from the throw ratio.</summary>
        public float HorizontalFovDegrees => 2f * Mathf.Atan(0.5f / Mathf.Max(0.05f, EffectiveThrowRatio)) * Mathf.Rad2Deg;

        private float Aspect
        {
            get
            {
                ContentScreenCaptureSource source = CaptureSource;
                return source != null && source.Height > 0
                    ? (float)source.Width / source.Height
                    : (deviceProfile != null ? deviceProfile.ImageAspect : imageAspect);
            }
        }

        // Lens-shift reach scaled by the device's max shift (reference 0.6 = a typical full-shift
        // projector). A model with a smaller spec shifts the image proportionally less.
        private float LensShiftFactorH => deviceProfile != null ? Mathf.Clamp01(deviceProfile.lensShiftMaxHorizontal / 0.6f) : 1f;
        private float LensShiftFactorV => deviceProfile != null ? Mathf.Clamp01(deviceProfile.lensShiftMaxVertical / 0.6f) : 1f;

        /// <summary>
        /// Inverse of the projection: maps a world point that the beam hits back to the
        /// content image's UV (0..1). Used so a click on the projected image lands on the
        /// matching content pixel — correctly handling cone projection, content scale, lens
        /// shift and the horizontal/vertical mirror. (Keystone is not inverted; it is 0 by
        /// default.) Returns false if the point is behind the projector.
        /// </summary>
        public bool TryWorldToContentUV(Vector3 worldPoint, out Vector2 contentUV)
        {
            contentUV = default;
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            if (local.z <= 0.001f)
            {
                return false;
            }
            float t = Mathf.Tan(SpotAngleDegrees * 0.5f * Mathf.Deg2Rad);
            if (t <= 0.0001f)
            {
                return false;
            }
            // World point -> cookie UV (the cookie square spans ±tan(spot/2) of the cone).
            float cookieU = (local.x / (local.z * t)) * 0.5f + 0.5f;
            float cookieV = (local.y / (local.z * t)) * 0.5f + 0.5f;

            // Cookie UV -> content UV (inverse of the quad placed in UpdateCookie).
            GetQuadHalfExtents(out float halfW, out float halfV);
            float cx = 0.5f + lensShift.x * (0.5f - halfW) * LensShiftFactorH;
            float cy = 0.5f + lensShift.y * (0.5f - halfV) * LensShiftFactorV;

            float qx = (cookieU - (cx - halfW)) / (2f * halfW);
            float qy = (cookieV - (cy - halfV)) / (2f * halfV);
            contentUV = QuadToContentUV(qx, qy);
            return true;
        }

        /// <summary>
        /// Whether the beam actually LANDS at the world point: within throw range and not
        /// blocked by anything nearer (the first surface the projector ray meets toward the
        /// point must be the point's own surface). Combined with the UV mapping this makes
        /// the touchable area exactly the visible image — clicks behind an occluder or on
        /// geometry the image never reaches are rejected.
        /// </summary>
        public bool IsImagePresentAt(Vector3 worldPoint)
        {
            if (transform.InverseTransformPoint(worldPoint).z <= 0f)
            {
                return false; // behind the lens — never lit
            }
            Vector3 toPoint = worldPoint - transform.position;
            float distance = toPoint.magnitude;
            if (distance < 0.01f || distance > maxThrowDistance)
            {
                return false;
            }
            return Physics.Raycast(transform.position, toPoint / distance, out RaycastHit hit, maxThrowDistance)
                && hit.distance >= distance - 0.05f;
        }

        /// <summary>
        /// Half extents of the projected quad in cookie UV space. In portrait mounting the
        /// content's width runs vertically, so the extents swap (the longest edge keeps the
        /// cookieContentScale budget, staying inside the cone).
        /// </summary>
        private void GetQuadHalfExtents(out float halfW, out float halfV)
        {
            float halfU = Mathf.Clamp(cookieContentScale, 0.2f, 0.9f) * 0.5f;
            float halfContentV = halfU / Mathf.Max(0.1f, Aspect);
            bool portrait = imageOrientation == DisplayOrientation.Portrait;
            halfW = portrait ? halfContentV : halfU;
            halfV = portrait ? halfU : halfContentV;
        }

        /// <summary>
        /// The projected quad in cookie UV space [0,1]:
        ///  - rectangle sized by cookieContentScale + aspect + mounting orientation
        ///    (kept inside the cone),
        ///  - lens shift offsets it within the available margin,
        ///  - vertical keystone makes it a trapezoid (top vs bottom width).
        /// The cookie is what the light actually emits, so BOTH the rendered image and the
        /// geometric image trace (<see cref="TryTraceImage"/>) are derived from this — they
        /// can never disagree about where the picture is.
        /// </summary>
        private void GetCookieQuadCorners(out Vector2 bl, out Vector2 br, out Vector2 tr, out Vector2 tl)
        {
            GetQuadHalfExtents(out float halfW, out float halfV);
            float marginX = 0.5f - halfW;
            float marginY = 0.5f - halfV;
            float cx = 0.5f + lensShift.x * marginX * LensShiftFactorH;
            float cy = 0.5f + lensShift.y * marginY * LensShiftFactorV;
            float kv = Mathf.Clamp(verticalKeystone, -0.6f, 0.6f);

            bl = new Vector2(cx - halfW * (1f + kv), cy - halfV);
            br = new Vector2(cx + halfW * (1f + kv), cy - halfV);
            tr = new Vector2(cx + halfW * (1f - kv), cy + halfV);
            tl = new Vector2(cx - halfW * (1f - kv), cy + halfV);

            // The spot cone's lit area is the circle inscribed in the cookie square. If lens
            // shift / keystone push any corner past that circle, the cone clips it into a
            // round arc. Guard against it: if the quad exceeds the circle, shrink it uniformly
            // toward the centre so it stays fully inside (keeps a crisp rectangle/trapezoid,
            // just smaller — never a rounded edge).
            Vector2 cookieCenter = new Vector2(0.5f, 0.5f);
            const float coneRadius = 0.47f;
            float maxDist = Mathf.Max(
                Mathf.Max((bl - cookieCenter).magnitude, (br - cookieCenter).magnitude),
                Mathf.Max((tr - cookieCenter).magnitude, (tl - cookieCenter).magnitude));
            if (maxDist > coneRadius)
            {
                float fit = coneRadius / maxDist;
                bl = cookieCenter + (bl - cookieCenter) * fit;
                br = cookieCenter + (br - cookieCenter) * fit;
                tr = cookieCenter + (tr - cookieCenter) * fit;
                tl = cookieCenter + (tl - cookieCenter) * fit;
            }
        }

        /// <summary>Maximum distance the beam is modelled to reach, metres.</summary>
        public float MaxThrowDistance => maxThrowDistance;

        /// <summary>
        /// World-space direction of the ray carrying the image point (<paramref name="u"/>,
        /// <paramref name="v"/>), normalized quad coordinates with (0,0) at the image's
        /// bottom-left. Includes lens shift, keystone, portrait mounting and the cone-fit
        /// clamp, so it agrees with what is rendered.
        /// </summary>
        public Vector3 GetImageRayDirection(float u, float v)
        {
            GetCookieQuadCorners(out Vector2 bl, out Vector2 br, out Vector2 tr, out Vector2 tl);
            // Bilinear across the (possibly trapezoid) quad, in cookie space.
            Vector2 bottom = Vector2.Lerp(bl, br, u);
            Vector2 top = Vector2.Lerp(tl, tr, u);
            Vector2 cookie = Vector2.Lerp(bottom, top, v);

            // Cookie square [0,1] spans the spot cone: an offset of 0.5 from the centre maps
            // to tan(spotAngle / 2).
            float tanSpotHalf = Mathf.Tan(SpotAngleDegrees * Mathf.Deg2Rad * 0.5f);
            float tx = 2f * (cookie.x - 0.5f) * tanSpotHalf;
            float ty = 2f * (cookie.y - 0.5f) * tanSpotHalf;
            return transform.TransformDirection(new Vector3(tx, ty, 1f).normalized);
        }

        /// <summary>
        /// Traces the emitted image onto scene colliders and reports where it lands: distance,
        /// physical size, incidence angle and whether it stays on one surface. Returns false
        /// when the beam axis reaches nothing within <see cref="MaxThrowDistance"/> (the image
        /// is not landing anywhere). Pure geometry — no rendering and no Play mode needed.
        /// </summary>
        public bool TryTraceImage(out ProjectedImageFootprint footprint)
        {
            footprint = default;

            Vector3 origin = transform.position;
            if (!Physics.Raycast(origin, GetImageRayDirection(0.5f, 0.5f), out RaycastHit centre, maxThrowDistance))
            {
                return false;
            }

            footprint.Centre = centre.point;
            footprint.SurfaceNormal = centre.normal;
            footprint.SurfaceCollider = centre.collider;
            footprint.ThrowDistance = centre.distance;
            footprint.IncidenceDegrees = Vector3.Angle(-centre.normal, transform.forward);

            // (u,v) of the four corners, in the order the footprint stores them.
            Vector2[] uv = { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            Vector3[] points = new Vector3[4];
            bool allHit = true;
            int onSurface = 0;
            for (int i = 0; i < 4; i++)
            {
                Vector3 direction = GetImageRayDirection(uv[i].x, uv[i].y);
                if (Physics.Raycast(origin, direction, out RaycastHit hit, maxThrowDistance))
                {
                    points[i] = hit.point;
                    if (hit.collider == centre.collider)
                    {
                        onSurface++;
                    }
                }
                else
                {
                    // Nothing out there: report where the ray would be at maximum throw so the
                    // caller can still show the direction the image escapes to.
                    points[i] = origin + direction * maxThrowDistance;
                    allHit = false;
                }
            }

            footprint.BottomLeft = points[0];
            footprint.BottomRight = points[1];
            footprint.TopRight = points[2];
            footprint.TopLeft = points[3];
            footprint.AllCornersHit = allHit;
            footprint.CornersOnSurface = onSurface;
            footprint.Width = 0.5f * (Vector3.Distance(points[0], points[1]) + Vector3.Distance(points[3], points[2]));
            footprint.Height = 0.5f * (Vector3.Distance(points[0], points[3]) + Vector3.Distance(points[1], points[2]));
            return true;
        }

        // Normalized quad coordinates (0..1 across the projected rectangle) to content UV:
        // the portrait rotation first, then the mirror/flip corrections.
        private Vector2 QuadToContentUV(float qx, float qy)
        {
            Vector2 uv = imageOrientation == DisplayOrientation.Portrait
                ? new Vector2(qy, 1f - qx)
                : new Vector2(qx, qy);
            if (mirrorCookieHorizontally)
            {
                uv.x = 1f - uv.x;
            }
            if (flipCookieVertically)
            {
                uv.y = 1f - uv.y;
            }
            return uv;
        }

        private void Awake()
        {
            projectorLight = GetComponent<Light>();
        }

        private void OnEnable()
        {
            EnsureTargets();
            ConfigureLight();
        }

        private void OnDisable()
        {
            if (cookieRt != null)
            {
                cookieRt.Release();
                DestroyImmediate(cookieRt);
                cookieRt = null;
            }
            if (scaledRt != null)
            {
                scaledRt.Release();
                DestroyImmediate(scaledRt);
                scaledRt = null;
            }
            if (cookieBlitMaterial != null)
            {
                DestroyImmediate(cookieBlitMaterial);
                cookieBlitMaterial = null;
            }
        }

        private void EnsureTargets()
        {
            // No early-out on a missing capture source: the cookie still renders a preview.
            int scaledWidth = Mathf.RoundToInt(cookieResolution * cookieContentScale);
            int scaledHeight = Mathf.RoundToInt(scaledWidth / Aspect);
            scaledWidth = Mathf.Clamp(scaledWidth, 1, cookieResolution);
            scaledHeight = Mathf.Clamp(scaledHeight, 1, cookieResolution);

            if (cookieRt == null)
            {
                cookieRt = new RenderTexture(cookieResolution, cookieResolution, 0, RenderTextureFormat.ARGB32)
                {
                    name = "SpatialKit Projector Cookie RT",
                    wrapMode = TextureWrapMode.Clamp
                };
                cookieRt.Create();
            }

            if (scaledRt == null || scaledRt.width != scaledWidth || scaledRt.height != scaledHeight)
            {
                if (scaledRt != null)
                {
                    scaledRt.Release();
                    Destroy(scaledRt);
                }
                scaledRt = new RenderTexture(scaledWidth, scaledHeight, 0, RenderTextureFormat.ARGB32)
                {
                    name = "SpatialKit Projector Scaled RT"
                };
                scaledRt.Create();
            }
        }

        private void ConfigureLight()
        {
            if (projectorLight == null)
            {
                return;
            }

            projectorLight.type = LightType.Spot;
            projectorLight.intensity = intensity;
            projectorLight.shadows = projectorShadows;
            projectorLight.cookie = cookieRt;
        }

        /// <summary>
        /// Spot cone whose full square subtends the FOV the canvas needs. The canvas
        /// occupies cookieContentScale of that square horizontally and subtends the
        /// projector's horizontal FOV, so:
        ///   tan(spot/2) = tan(hfov/2) / cookieContentScale
        /// This depends only on the intrinsics, never on distance to any surface.
        /// </summary>
        public float SpotAngleDegrees
        {
            get
            {
                float hfovRad = HorizontalFovDegrees * Mathf.Deg2Rad;
                float spotRad = 2f * Mathf.Atan(Mathf.Tan(hfovRad * 0.5f) / Mathf.Clamp(cookieContentScale, 0.3f, 1f));
                return Mathf.Clamp(spotRad * Mathf.Rad2Deg, 1f, 170f);
            }
        }

        private void LateUpdate()
        {
            if (projectorLight == null)
            {
                return;
            }

            EnsureTargets();

            // Intrinsic, pose-independent. No wall, no LookAt, no distance term.
            projectorLight.spotAngle = SpotAngleDegrees;
            // Inner ≈ outer: no angular (circular) falloff, so the cookie defines a crisp
            // rectangle rather than a soft round pool of light.
            projectorLight.innerSpotAngle = projectorLight.spotAngle * 0.995f;
            projectorLight.range = maxThrowDistance;
            projectorLight.intensity = intensity;
            if (projectorLight.cookie != cookieRt)
            {
                projectorLight.cookie = cookieRt;
            }

            UpdateCookie();
        }

        private void UpdateCookie()
        {
            if (cookieRt == null || scaledRt == null)
            {
                return;
            }
            EnsureBlitMaterial();
            if (cookieBlitMaterial == null)
            {
                return;
            }

            // Source buffer: live content frame, or a solid preview colour when there is none
            // (Editor before Play). The preview's rectangle edges still show lens shift /
            // keystone / focus immediately.
            Texture canvas = null;
            ContentScreenCaptureSource source = CaptureSource;
            bool hasFrame = source != null
                && source.TryGetFrameTexture(out canvas, out _)
                && canvas != null;
            if (hasFrame)
            {
                Graphics.Blit(canvas, scaledRt);
            }
            else
            {
                ClearRt(scaledRt, previewColor);
            }

            // Focus: blur the source the further the wall is from the focus plane.
            float defocus = ComputeDefocus();
            if (defocus > 0.01f)
            {
                BlurInPlace(scaledRt, defocus);
            }

            GetCookieQuadCorners(out Vector2 bl, out Vector2 br, out Vector2 tr, out Vector2 tl);

            // Texture coordinates come from the same quad→content mapping the inverse
            // projection uses, so clicks and pixels stay in agreement in every orientation.
            Vector2 uvBl = QuadToContentUV(0f, 0f);
            Vector2 uvBr = QuadToContentUV(1f, 0f);
            Vector2 uvTr = QuadToContentUV(1f, 1f);
            Vector2 uvTl = QuadToContentUV(0f, 1f);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = cookieRt;
            GL.Clear(true, true, Color.black);
            cookieBlitMaterial.mainTexture = scaledRt;
            cookieBlitMaterial.SetPass(0);
            GL.PushMatrix();
            GL.LoadOrtho();
            GL.Begin(GL.QUADS);
            GL.TexCoord2(uvBl.x, uvBl.y); GL.Vertex3(bl.x, bl.y, 0f);
            GL.TexCoord2(uvBr.x, uvBr.y); GL.Vertex3(br.x, br.y, 0f);
            GL.TexCoord2(uvTr.x, uvTr.y); GL.Vertex3(tr.x, tr.y, 0f);
            GL.TexCoord2(uvTl.x, uvTl.y); GL.Vertex3(tl.x, tl.y, 0f);
            GL.End();
            GL.PopMatrix();
            RenderTexture.active = previous;
        }

        private static bool warnedAboutMissingShader;

        private void EnsureBlitMaterial()
        {
            if (cookieBlitMaterial != null)
            {
                return;
            }
            Shader shader = Shader.Find(CookieShaderName);
            if (shader != null)
            {
                cookieBlitMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                return;
            }
            if (!warnedAboutMissingShader)
            {
                warnedAboutMissingShader = true;
                // In a player this means the shader was stripped: it is only reached through
                // Shader.Find, so it must be registered in Always Included Shaders. The kit's
                // Setup panel does that ("投影シェーダ (ビルド同梱)").
                Debug.LogError($"[SpatialKit] シェーダ '{CookieShaderName}' が見つかりません。投影は表示されません。" +
                               "Scene ビューの Projection Spatial Kit オーバーレイの Setup から修復してください。", this);
            }
        }

        private static void ClearRt(RenderTexture rt, Color color)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, color);
            RenderTexture.active = previous;
        }

        private float ComputeDefocus()
        {
            float wallDistance = maxThrowDistance * 0.3f;
            if (Physics.Raycast(transform.position, transform.forward, out RaycastHit hit, maxThrowDistance))
            {
                wallDistance = hit.distance;
            }
            // Optical-ish circle of confusion: grows with distance from the focus plane,
            // shrinks with the f-number (smaller aperture opening = deeper depth of field).
            float coc = Mathf.Abs(wallDistance - focusDistance) / Mathf.Max(1f, aperture);
            return Mathf.Clamp01(coc * FocusBlurScale);
        }

        private static void BlurInPlace(RenderTexture rt, float amount)
        {
            // Cheap bilinear blur: downscale then upscale. Smaller intermediate = more blur.
            int w = Mathf.Max(2, Mathf.RoundToInt(rt.width / (1f + amount * 14f)));
            int h = Mathf.Max(2, Mathf.RoundToInt(rt.height / (1f + amount * 14f)));
            RenderTexture tmp = RenderTexture.GetTemporary(w, h, 0, rt.format);
            Graphics.Blit(rt, tmp);
            Graphics.Blit(tmp, rt);
            RenderTexture.ReleaseTemporary(tmp);
        }

        private void OnDrawGizmos()
        {
            // Draw the emitted image frustum from pose + intrinsics. Where the corner rays
            // strike a collider is where the image lands — discovered, not assumed. Uses the
            // same trace the preflight checks use, so the gizmo, the projected picture and the
            // reported footprint always agree (including lens shift and keystone).
            if (!TryTraceImage(out ProjectedImageFootprint footprint))
            {
                return;
            }

            Vector3[] corners =
            {
                footprint.BottomLeft, footprint.BottomRight, footprint.TopRight, footprint.TopLeft
            };
            Gizmos.color = Color.yellow;
            for (int i = 0; i < 4; i++)
            {
                Gizmos.DrawLine(transform.position, corners[i]);
            }

            Gizmos.color = Color.green;
            for (int i = 0; i < 4; i++)
            {
                Gizmos.DrawLine(corners[i], corners[(i + 1) % 4]);
            }
        }
    }
}
