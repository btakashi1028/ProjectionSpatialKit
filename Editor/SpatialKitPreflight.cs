using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ProjectionSpatialKit.Editor
{
    /// <summary>
    /// PREFLIGHT: answers "what will be wrong when we get on site?" by CALCULATING, not by
    /// looking. Every check here is pure geometry / configuration analysis, so none of it
    /// depends on rendering, on Play mode, or on the Game View being able to show more than
    /// one display at a time — which is exactly where previewing a venue inside Unity breaks
    /// down. The output is a verdict with numbers and a concrete fix, meant to be read before
    /// anyone loads a projector into a van.
    ///
    /// Scope: A = projection placement, B = screen configuration. Input/touch checks (C) are
    /// deliberately out of scope for now.
    /// </summary>
    internal static class SpatialKitPreflight
    {
        internal enum Severity
        {
            Ok,
            Info,
            Warn,
            Fail
        }

        internal sealed class Finding
        {
            public Severity Severity;
            public string Category;    // 投影配置 / 画面構成
            public string Target;      // device or scene object the finding is about
            public string Summary;     // what is wrong (with measured numbers)
            public string Fix;         // what to do about it (with numbers where possible)
            public Object Context;     // ping target in the Editor
        }

        internal const string PlacementCategory = "投影配置";
        internal const string ScreensCategory = "画面構成";

        // Incidence beyond this is hard to correct cleanly; beyond the second, treat as a fault.
        private const float IncidenceWarnDegrees = 25f;
        private const float IncidenceFailDegrees = 40f;
        // Landed image width within this fraction of the target counts as on-target.
        private const float WidthTolerance = 0.1f;
        private const int OcclusionGrid = 7; // 7x7 samples over the image

        internal static List<Finding> Run()
        {
            var findings = new List<Finding>();
            SpatialKitSimulator simulator = Object.FindFirstObjectByType<SpatialKitSimulator>();

            CheckPlacement(findings);
            CheckScreens(findings, simulator);

            findings.Sort((a, b) => b.Severity.CompareTo(a.Severity));
            return findings;
        }

        private static void Add(List<Finding> list, Severity severity, string category,
            string target, string summary, string fix, Object context)
        {
            list.Add(new Finding
            {
                Severity = severity,
                Category = category,
                Target = target,
                Summary = summary,
                Fix = fix,
                Context = context
            });
        }

        // ------------------------------------------------------------------ A: 投影配置

        private static void CheckPlacement(List<Finding> findings)
        {
            VirtualProjectorLight[] projectors =
                Object.FindObjectsByType<VirtualProjectorLight>(FindObjectsSortMode.InstanceID);
            if (projectors.Length == 0)
            {
                Add(findings, Severity.Info, PlacementCategory, "—",
                    "シーンにプロジェクタがありません。", "モニタのみの構成であれば問題ありません。", null);
                return;
            }

            foreach (VirtualProjectorLight projector in projectors)
            {
                string name = projector.gameObject.name;

                if (!projector.TryTraceImage(out ProjectedImageFootprint footprint))
                {
                    Add(findings, Severity.Fail, PlacementCategory, name,
                        $"ビームが何にも当たっていません（最大投射距離 {projector.MaxThrowDistance:F1}m 以内に面がない）。",
                        "プロジェクタの位置・向きを投影したい面へ向けるか、Max Throw Distance を伸ばしてください。",
                        projector.gameObject);
                    continue;
                }

                CheckFootprintFits(findings, projector, footprint, name);
                CheckTargetWidthAndThrowRatio(findings, projector, footprint, name);
                CheckLensShiftWithinDevice(findings, projector, name);
                CheckIncidence(findings, footprint, name, projector.gameObject);
                CheckFocus(findings, projector, footprint, name);
                CheckOcclusion(findings, projector, footprint, name);
            }
        }

        private static void CheckFootprintFits(List<Finding> findings, VirtualProjectorLight projector,
            ProjectedImageFootprint footprint, string name)
        {
            if (!footprint.AllCornersHit)
            {
                Add(findings, Severity.Fail, PlacementCategory, name,
                    $"像の一部が到達していません（4隅中 {footprint.CornersOnSurface} 隅のみ投影面に着弾）。開口部や面の外へ抜けています。",
                    "投影面に収まる位置へ寄せる、画像幅を小さくする、または面を広げてください。",
                    projector.gameObject);
                return;
            }

            if (footprint.CornersOnSurface < 4)
            {
                Add(findings, Severity.Warn, PlacementCategory, name,
                    $"像が複数の面にまたがっています（同一面に乗っているのは 4 隅中 {footprint.CornersOnSurface} 隅）。壁の端をはみ出し、隣の面や床に折れて映ります。",
                    "投影面の中央寄りへ振り直すか、画像幅を小さくしてください（オーバーレイの『壁をクリックして投影面を設定』が使えます）。",
                    projector.gameObject);
            }
        }

        private static void CheckTargetWidthAndThrowRatio(List<Finding> findings, VirtualProjectorLight projector,
            ProjectedImageFootprint footprint, string name)
        {
            ProjectionRig rig = projector.GetComponentInParent<ProjectionRig>();
            if (rig == null)
            {
                Add(findings, Severity.Info, PlacementCategory, name,
                    $"着弾サイズ {footprint.Width:F2}m × {footprint.Height:F2}m（投射距離 {footprint.ThrowDistance:F2}m）。",
                    "目標サイズと比較したい場合は Projection Set（ProjectionRig）配下に置いて『画像幅』を設定してください。",
                    projector.gameObject);
                return;
            }

            float target = rig.TargetImageWidth;
            if (target <= 0.01f)
            {
                return;
            }

            float error = footprint.Width - target;
            if (Mathf.Abs(error) / target > WidthTolerance)
            {
                // Distance that WOULD produce the target width at the current throw ratio.
                float neededDistance = target * projector.EffectiveThrowRatio;
                float move = neededDistance - footprint.ThrowDistance;
                Add(findings, Severity.Warn, PlacementCategory, name,
                    $"画像幅が目標に一致しません：実測 {footprint.Width:F2}m / 目標 {target:F2}m（{error:+0.00;-0.00}m）。",
                    $"投射距離を {neededDistance:F2}m にしてください（現在 {footprint.ThrowDistance:F2}m、" +
                    $"{(move >= 0f ? "後退" : "前進")} {Mathf.Abs(move):F2}m）。またはズームで調整します。",
                    projector.gameObject);
            }

            // The decision that actually drives equipment choice: can this MODEL make the
            // target width from where it stands?
            ProjectorDeviceProfile profile = projector.DeviceProfile;
            if (profile == null)
            {
                return;
            }

            // Throw ratio is a spec measured head-on (distance ÷ image width on a surface the
            // projector faces squarely). On a steeply angled surface that formula stops
            // describing anything real, so refusing to judge the MODEL here is the honest
            // move: otherwise a pure aiming problem would be reported as "buy another
            // projector". The incidence check already tells the user to fix the angle first.
            if (footprint.IncidenceDegrees >= IncidenceWarnDegrees)
            {
                Add(findings, Severity.Info, PlacementCategory, name,
                    $"入射角 {footprint.IncidenceDegrees:F0}° が大きいため、スローレシオによる機種適合の判定は保留しました" +
                    $"（実測の着弾幅は {footprint.Width:F2}m）。",
                    "先に入射角を小さくしてから再検査してください。角度が付いたままではスローレシオの規格値は当てになりません。",
                    projector.gameObject);
                return;
            }

            float requiredRatio = footprint.ThrowDistance / target;
            float min = profile.throwRatioMin;
            float max = profile.throwRatioMax;
            if (requiredRatio < min - 0.001f)
            {
                float closest = target * min;
                Add(findings, Severity.Fail, PlacementCategory, name,
                    $"必要スローレシオ {requiredRatio:F2} は {profile.name} の範囲 {min:F2}–{max:F2} を下回ります" +
                    $"（この距離 {footprint.ThrowDistance:F2}m で幅 {target:F2}m は出せません）。",
                    $"より短焦点の機種が必要です。この機種のままなら {closest:F2}m まで後退してください" +
                    $"（{closest - footprint.ThrowDistance:F2}m 後ろへ）。",
                    projector.gameObject);
            }
            else if (requiredRatio > max + 0.001f)
            {
                float closest = target * max;
                Add(findings, Severity.Fail, PlacementCategory, name,
                    $"必要スローレシオ {requiredRatio:F2} は {profile.name} の範囲 {min:F2}–{max:F2} を上回ります" +
                    $"（この距離では像が目標より大きくなります）。",
                    $"より長焦点／ズーム幅のある機種が必要です。この機種のままなら {closest:F2}m まで前進してください" +
                    $"（{footprint.ThrowDistance - closest:F2}m 前へ）。",
                    projector.gameObject);
            }
            else
            {
                float zoom01 = Mathf.Approximately(max, min) ? 0f : Mathf.InverseLerp(min, max, requiredRatio);
                if (Mathf.Abs(zoom01 - projector.Zoom) > 0.05f)
                {
                    Add(findings, Severity.Info, PlacementCategory, name,
                        $"この配置で目標幅 {target:F2}m を出すスローレシオは {requiredRatio:F2}（機種範囲 {min:F2}–{max:F2} 内）。",
                        $"Zoom を {zoom01:F2} にすると目標幅に一致します（現在 {projector.Zoom:F2}）。",
                        projector.gameObject);
                }
            }
        }

        private static void CheckLensShiftWithinDevice(List<Finding> findings, VirtualProjectorLight projector, string name)
        {
            ProjectorDeviceProfile profile = projector.DeviceProfile;
            if (profile == null)
            {
                return;
            }

            Vector2 shift = projector.LensShift;
            if (Mathf.Abs(shift.x) > profile.lensShiftMaxHorizontal + 0.001f)
            {
                Add(findings, Severity.Fail, PlacementCategory, name,
                    $"水平レンズシフト {shift.x:F2} は {profile.name} の上限 ±{profile.lensShiftMaxHorizontal:F2} を超えています（実機では出せない設定です）。",
                    "シフト量を上限内に戻し、不足分はプロジェクタ本体の位置移動で補ってください。",
                    projector.gameObject);
            }
            if (Mathf.Abs(shift.y) > profile.lensShiftMaxVertical + 0.001f)
            {
                Add(findings, Severity.Fail, PlacementCategory, name,
                    $"垂直レンズシフト {shift.y:F2} は {profile.name} の上限 ±{profile.lensShiftMaxVertical:F2} を超えています（実機では出せない設定です）。",
                    "シフト量を上限内に戻し、不足分は設置高さの変更で補ってください。",
                    projector.gameObject);
            }
        }

        private static void CheckIncidence(List<Finding> findings, ProjectedImageFootprint footprint,
            string name, GameObject context)
        {
            float incidence = footprint.IncidenceDegrees;
            if (incidence >= IncidenceFailDegrees)
            {
                Add(findings, Severity.Fail, PlacementCategory, name,
                    $"投影面への入射角が {incidence:F0}° です。台形歪みが大きく、面内でフォーカスと明るさが偏ります。",
                    "プロジェクタを面の正面へ寄せてください（目安 25° 未満）。難しい場合は台形補正／レンズシフトの併用と、実機での確認が必要です。",
                    context);
            }
            else if (incidence >= IncidenceWarnDegrees)
            {
                Add(findings, Severity.Warn, PlacementCategory, name,
                    $"投影面への入射角が {incidence:F0}° です。台形補正が必要になります。",
                    "正面寄りへ振り直すか、Vertical Keystone / Lens Shift での補正を前提にしてください。",
                    context);
            }
        }

        private static void CheckFocus(List<Finding> findings, VirtualProjectorLight projector,
            ProjectedImageFootprint footprint, string name)
        {
            float distance = footprint.ThrowDistance;
            float error = Mathf.Abs(projector.FocusDistance - distance);
            // Same shape as the runtime defocus model: error scaled by the f-number.
            float defocus = error / Mathf.Max(1f, projector.Aperture);
            if (defocus > 0.25f)
            {
                Add(findings, Severity.Warn, PlacementCategory, name,
                    $"フォーカス距離 {projector.FocusDistance:F2}m に対し実際の投射距離は {distance:F2}m です（差 {error:F2}m）。像がボケます。",
                    $"Focus Distance を {distance:F2}m に合わせてください。",
                    projector.gameObject);
            }

            if (distance > projector.MaxThrowDistance * 0.9f)
            {
                Add(findings, Severity.Warn, PlacementCategory, name,
                    $"投射距離 {distance:F2}m が Max Throw Distance {projector.MaxThrowDistance:F1}m に迫っています。",
                    "Max Throw Distance を伸ばすか、プロジェクタを面へ寄せてください（この距離を超えると像が消えます）。",
                    projector.gameObject);
            }
        }

        private static void CheckOcclusion(List<Finding> findings, VirtualProjectorLight projector,
            ProjectedImageFootprint footprint, string name)
        {
            if (footprint.SurfaceCollider == null)
            {
                return;
            }

            int blocked = 0;
            int total = 0;
            string blockerName = null;
            Vector3 origin = projector.transform.position;
            for (int y = 0; y < OcclusionGrid; y++)
            {
                for (int x = 0; x < OcclusionGrid; x++)
                {
                    float u = (x + 0.5f) / OcclusionGrid;
                    float v = (y + 0.5f) / OcclusionGrid;
                    total++;
                    if (!Physics.Raycast(origin, projector.GetImageRayDirection(u, v),
                            out RaycastHit hit, projector.MaxThrowDistance))
                    {
                        continue;
                    }
                    // Anything that is not the projection surface, standing in front of it,
                    // is an occluder: the picture (and any touch behind it) is lost there.
                    if (hit.collider != footprint.SurfaceCollider)
                    {
                        blocked++;
                        blockerName ??= hit.collider.gameObject.name;
                    }
                }
            }

            if (blocked == 0)
            {
                return;
            }

            float percent = 100f * blocked / total;
            Add(findings, percent > 25f ? Severity.Fail : Severity.Warn, PlacementCategory, name,
                $"ビーム上の遮蔽物が像の約 {percent:F0}% を遮っています（例: {blockerName}）。影の部分は映像もタッチも失われます。",
                "遮蔽物を動かすか、プロジェクタの位置・角度を変えて回り込ませてください。",
                projector.gameObject);
        }

        // ---------------------------------------------------------------- B: 画面構成

        private static void CheckScreens(List<Finding> findings, SpatialKitSimulator simulator)
        {
            if (simulator == null)
            {
                Add(findings, Severity.Fail, ScreensCategory, "—",
                    "シーンに SpatialKitSimulator がありません。会場の構成を判定できません。",
                    "オーバーレイの『この開いているシーンから会場を作る』で会場シーンを作成してください。", null);
                return;
            }

            CheckContentScene(findings, simulator);
            CheckChannelsAndDevices(findings, simulator);
            CheckObserverCollision(findings, simulator);
            CheckGameViewResolution(findings, simulator);
        }

        private static void CheckContentScene(List<Finding> findings, SpatialKitSimulator simulator)
        {
            SerializedObject so = new SerializedObject(simulator);
            string contentPath = so.FindProperty("contentScenePath").stringValue;

            if (string.IsNullOrEmpty(contentPath))
            {
                Add(findings, Severity.Fail, ScreensCategory, "Content Scene",
                    "投影するコンテンツシーンが未設定です。会場には何も映りません。",
                    "Simulator の Content Scene にシーンアセットをドラッグしてください。", simulator.gameObject);
                return;
            }

            if (contentPath == simulator.gameObject.scene.path)
            {
                Add(findings, Severity.Fail, ScreensCategory, "Content Scene",
                    "コンテンツシーンが会場シーン自身に設定されています（無限ロードになります）。",
                    "会場とコンテンツは別シーンにしてください。", simulator.gameObject);
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(contentPath) == null)
            {
                Add(findings, Severity.Fail, ScreensCategory, "Content Scene",
                    $"設定されたコンテンツシーンが見つかりません：{contentPath}",
                    "Simulator の Content Scene を設定し直してください。", simulator.gameObject);
                return;
            }

            bool registered = false;
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.path == contentPath && scene.enabled)
                {
                    registered = true;
                    break;
                }
            }
            if (!registered)
            {
                Add(findings, Severity.Fail, ScreensCategory, "Content Scene",
                    "コンテンツシーンが Build Settings に未登録です。会場は additive ロードするため、" +
                    "初回（コールド）再生で投影が出ない、ビルドでは読み込めない、という症状になります。",
                    $"Build Settings に追加してください：{contentPath}（セットアップ診断の修復ボタンでも登録できます）。",
                    simulator.gameObject);
            }
        }

        private static void CheckChannelsAndDevices(List<Finding> findings, SpatialKitSimulator simulator)
        {
            IReadOnlyList<OutputRouter.ChannelConfig> channels = simulator.Channels;
            var channelByDisplay = new Dictionary<int, Vector2Int>();
            foreach (OutputRouter.ChannelConfig channel in channels)
            {
                if (channel.resolution.x <= 0 || channel.resolution.y <= 0)
                {
                    Add(findings, Severity.Fail, ScreensCategory, $"画面 {channel.displayIndex + 1}",
                        $"チャネル解像度が不正です（{channel.resolution.x}×{channel.resolution.y}）。",
                        "実機ディスプレイの解像度（例 1920×1080 / 1080×1920）を設定してください。",
                        simulator.gameObject);
                    continue;
                }
                if (channelByDisplay.ContainsKey(channel.displayIndex))
                {
                    Add(findings, Severity.Warn, ScreensCategory, $"画面 {channel.displayIndex + 1}",
                        "同じ画面番号のチャネルが重複定義されています。",
                        "重複するチャネルを削除してください（1画面につき1チャネル）。", simulator.gameObject);
                    continue;
                }
                channelByDisplay[channel.displayIndex] = channel.resolution;
            }

            var displaysUsedByDevices = new Dictionary<int, int>();

            foreach (VirtualProjectorLight projector in
                     Object.FindObjectsByType<VirtualProjectorLight>(FindObjectsSortMode.InstanceID))
            {
                CountDevice(displaysUsedByDevices, projector.ContentDisplay);
                if (!channelByDisplay.TryGetValue(projector.ContentDisplay, out Vector2Int resolution))
                {
                    Add(findings, Severity.Fail, ScreensCategory, projector.gameObject.name,
                        $"割り当て先の画面 {projector.ContentDisplay + 1} にチャネルがありません。何も投影されません（黒）。",
                        $"オーバーレイの『画面 (チャネル)』に画面 {projector.ContentDisplay + 1} を追加してください。",
                        projector.gameObject);
                    continue;
                }

                bool channelPortrait = resolution.y > resolution.x;
                bool projectorPortrait = projector.ImageOrientation == DisplayOrientation.Portrait;
                if (channelPortrait != projectorPortrait)
                {
                    Add(findings, Severity.Warn, ScreensCategory, projector.gameObject.name,
                        $"画面 {projector.ContentDisplay + 1} は{(channelPortrait ? "縦" : "横")}（{resolution.x}×{resolution.y}）ですが、" +
                        $"このプロジェクタは{(projectorPortrait ? "縦" : "横")}置き設定です。像が 90° 回って映ります。",
                        $"Image Orientation を {(channelPortrait ? "Portrait" : "Landscape")} にするか、チャネル解像度を見直してください。",
                        projector.gameObject);
                }
            }

            foreach (MonitorSurface monitor in
                     Object.FindObjectsByType<MonitorSurface>(FindObjectsSortMode.InstanceID))
            {
                CountDevice(displaysUsedByDevices, monitor.ContentDisplay);
                if (!channelByDisplay.TryGetValue(monitor.ContentDisplay, out Vector2Int resolution))
                {
                    Add(findings, Severity.Fail, ScreensCategory, monitor.gameObject.name,
                        $"割り当て先の画面 {monitor.ContentDisplay + 1} にチャネルがありません。パネルは黒のままです。",
                        $"オーバーレイの『画面 (チャネル)』に画面 {monitor.ContentDisplay + 1} を追加してください。",
                        monitor.gameObject);
                    continue;
                }

                if (!monitor.MatchContentAspect)
                {
                    Vector2 panel = monitor.PanelSize;
                    float panelAspect = panel.y > 0.0001f ? panel.x / panel.y : 0f;
                    float channelAspect = (float)resolution.x / resolution.y;
                    if (panelAspect > 0.0001f && Mathf.Abs(panelAspect - channelAspect) / channelAspect > 0.1f)
                    {
                        Add(findings, Severity.Warn, ScreensCategory, monitor.gameObject.name,
                            $"パネル比 {panelAspect:F2} に対しチャネルは {channelAspect:F2}（{resolution.x}×{resolution.y}）です。" +
                            "映像が引き伸ばされます。",
                            "Match Content Aspect を ON にする（推奨）か、パネルサイズ／チャネル解像度を揃えてください。",
                            monitor.gameObject);
                    }
                }
            }

            foreach (KeyValuePair<int, Vector2Int> channel in channelByDisplay)
            {
                if (!displaysUsedByDevices.ContainsKey(channel.Key))
                {
                    Add(findings, Severity.Info, ScreensCategory, $"画面 {channel.Key + 1}",
                        $"チャネル（{channel.Value.x}×{channel.Value.y}）は定義されていますが、表示する機材がありません。",
                        "この画面を出す機材を追加するか、不要ならチャネルを削除してください。", simulator.gameObject);
                }
                else if (displaysUsedByDevices[channel.Key] > 1)
                {
                    Add(findings, Severity.Info, ScreensCategory, $"画面 {channel.Key + 1}",
                        $"{displaysUsedByDevices[channel.Key]} 台の機材が同じ画面を表示します（ミラー）。",
                        "意図した構成であれば問題ありません。", simulator.gameObject);
                }
            }
        }

        private static void CountDevice(Dictionary<int, int> counts, int display)
        {
            counts[display] = counts.TryGetValue(display, out int n) ? n + 1 : 1;
        }

        private static void CheckObserverCollision(List<Finding> findings, SpatialKitSimulator simulator)
        {
            SerializedObject so = new SerializedObject(simulator);
            Camera observer = so.FindProperty("observerCamera").objectReferenceValue as Camera;
            if (observer == null)
            {
                ObserverFlyCamera fly = Object.FindFirstObjectByType<ObserverFlyCamera>();
                observer = fly != null ? fly.GetComponent<Camera>() : null;
            }
            if (observer == null)
            {
                return;
            }

            foreach (OutputRouter.ChannelConfig channel in simulator.Channels)
            {
                if (channel.displayIndex == observer.targetDisplay)
                {
                    Add(findings, Severity.Fail, ScreensCategory, observer.gameObject.name,
                        $"Observer カメラが Display {observer.targetDisplay + 1} を使っていますが、" +
                        $"コンテンツの画面 {channel.displayIndex + 1} も同じ Display です（衝突）。会場ビューとコンテンツが重なります。",
                        $"Observer の Target Display を空いている番号へ移すか、画面 {channel.displayIndex + 1} のチャネル番号を変更してください。",
                        observer.gameObject);
                }
            }
        }

        /// <summary>
        /// The Editor's Game View shows ONE resolution at a time, so a venue whose channels do
        /// not match it will preview (and hit-test) wrongly — the classic "touch lands in the
        /// wrong place / UI does not react" symptom. Builds are unaffected, so this is reported
        /// as a warning about the Editor session, not about the venue design.
        /// </summary>
        private static void CheckGameViewResolution(List<Finding> findings, SpatialKitSimulator simulator)
        {
            IReadOnlyList<OutputRouter.ChannelConfig> channels = simulator.Channels;
            if (channels.Count == 0)
            {
                return;
            }

            var distinct = new HashSet<Vector2Int>();
            foreach (OutputRouter.ChannelConfig channel in channels)
            {
                if (channel.resolution.x > 0 && channel.resolution.y > 0)
                {
                    distinct.Add(channel.resolution);
                }
            }

            if (distinct.Count > 1)
            {
                Add(findings, Severity.Warn, ScreensCategory, "Game View",
                    $"解像度の異なるチャネルが {distinct.Count} 種類あります（縦横混在など）。Editor の Game View は" +
                    "一度に1解像度しか出せないため、表示していない側はズーム／崩れて見え、UI のヒットテストもずれます。",
                    "確認したい画面へ Game View を切り替え、その解像度をチャネルに合わせてください。実機／ビルドでは各ディスプレイが実解像度になるため問題になりません。",
                    simulator.gameObject);
            }

            if (!TryGetMainGameViewSize(out Vector2Int gameView))
            {
                return;
            }

            bool matchesAny = false;
            foreach (Vector2Int resolution in distinct)
            {
                if (resolution == gameView)
                {
                    matchesAny = true;
                    break;
                }
            }
            if (!matchesAny)
            {
                Add(findings, Severity.Warn, ScreensCategory, "Game View",
                    $"Game View は {gameView.x}×{gameView.y} ですが、どのチャネル解像度とも一致しません。" +
                    "エディタ上では表示とタッチ位置がずれ、UI が反応しないことがあります。",
                    "Game View の解像度をチャネル（例: " + FormatResolutions(distinct) + "）に合わせてください。",
                    simulator.gameObject);
            }
        }

        private static string FormatResolutions(HashSet<Vector2Int> resolutions)
        {
            var parts = new List<string>();
            foreach (Vector2Int resolution in resolutions)
            {
                parts.Add($"{resolution.x}×{resolution.y}");
            }
            return string.Join(" / ", parts);
        }

        /// <summary>Main Game View size via the internal Editor API; false when unavailable.</summary>
        private static bool TryGetMainGameViewSize(out Vector2Int size)
        {
            size = default;
            try
            {
                MethodInfo method = typeof(Handles).GetMethod("GetMainGameViewSize",
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (method == null)
                {
                    return false;
                }
                Vector2 value = (Vector2)method.Invoke(null, null);
                if (value.x < 1f || value.y < 1f)
                {
                    return false;
                }
                size = new Vector2Int(Mathf.RoundToInt(value.x), Mathf.RoundToInt(value.y));
                return true;
            }
            catch
            {
                return false; // internal API moved: skip this check rather than fail the report
            }
        }
    }
}
