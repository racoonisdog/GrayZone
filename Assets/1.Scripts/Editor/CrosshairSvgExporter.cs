#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GrayZone.EditorTools
{
    public static class CrosshairSvgExporter
    {
        public const string DefaultExportFolder = "Assets/3.Resources/UI/CrossHair";

        private const float PrimitiveSize = 50.0f;
        private const float PrimitiveLineHeight = 17.04f;
        private const float RingReferenceThicknessPixels = 1.0f;

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        [MenuItem("GrayZone/UI/Crosshair/Export Primitive SVG Assets")]
        public static void ExportPrimitiveSvgAssetsMenu()
        {
            if (!EditorUtility.DisplayDialog(
                "Export Crosshair SVG Assets",
                $"This will write Dot.svg, Line.svg, Square.svg, and Ring.svg to:\n{DefaultExportFolder}",
                "Export",
                "Cancel"))
            {
                return;
            }

            int writtenCount = ExportPrimitiveSvgAssets(DefaultExportFolder, true);
            AssetDatabase.Refresh();
            Debug.Log($"[CrosshairSvgExporter] Exported {writtenCount} primitive SVG asset(s) to {DefaultExportFolder}.");
        }

        [MenuItem("GrayZone/UI/Crosshair/Export Selected Crosshair SVG")]
        public static void ExportSelectedCrosshairSvgMenu()
        {
            CrosshairController controller = FindSelectedCrosshairController();
            if (controller == null)
            {
                EditorUtility.DisplayDialog("Export Crosshair SVG", "Select a GameObject with a CrosshairController first.", "OK");
                return;
            }

            ExportCrosshairSvgWithDialog(controller);
        }

        [MenuItem("GrayZone/UI/Crosshair/Export Selected Crosshair SVG", true)]
        public static bool CanExportSelectedCrosshairSvgMenu()
        {
            return FindSelectedCrosshairController() != null;
        }

        [MenuItem("CONTEXT/CrosshairController/Export Base SVG To CrossHair Folder")]
        private static void ExportCrosshairSvgContext(MenuCommand command)
        {
            if (command.context is CrosshairController controller)
            {
                ExportCrosshairSvgWithDialog(controller);
            }
        }

        public static int ExportPrimitiveSvgAssets(string assetFolderPath = DefaultExportFolder, bool overwrite = true)
        {
            int writtenCount = 0;
            writtenCount += ExportSvgAsset(assetFolderPath, "Dot.svg", BuildDotSvg(), overwrite) != null ? 1 : 0;
            writtenCount += ExportSvgAsset(assetFolderPath, "Line.svg", BuildLineSvg(), overwrite) != null ? 1 : 0;
            writtenCount += ExportSvgAsset(assetFolderPath, "Square.svg", BuildSquareSvg(), overwrite) != null ? 1 : 0;
            writtenCount += ExportSvgAsset(assetFolderPath, "Ring.svg", BuildRingSvg(), overwrite) != null ? 1 : 0;
            return writtenCount;
        }

        public static string ExportCrosshairSvg(
            CrosshairController controller,
            string assetFolderPath = DefaultExportFolder,
            string fileName = null,
            bool overwrite = false)
        {
            if (controller == null)
            {
                throw new ArgumentNullException(nameof(controller));
            }

            string resolvedFileName = string.IsNullOrWhiteSpace(fileName)
                ? SanitizeFileName(controller.gameObject.name + "_Crosshair.svg")
                : EnsureSvgFileName(fileName);

            string svg = BuildCrosshairSvg(controller);
            return ExportSvgAsset(assetFolderPath, resolvedFileName, svg, overwrite);
        }

        public static string ExportSvgAsset(string assetFolderPath, string fileName, string svgContent, bool overwrite)
        {
            if (string.IsNullOrWhiteSpace(svgContent))
            {
                throw new ArgumentException("SVG content is empty.", nameof(svgContent));
            }

            string normalizedFolder = NormalizeAssetFolderPath(assetFolderPath);
            string resolvedFileName = EnsureSvgFileName(fileName);
            string assetPath = normalizedFolder + "/" + resolvedFileName;

            if (!overwrite)
            {
                assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
            }

            string absolutePath = ToAbsoluteAssetPath(assetPath);
            string absoluteFolder = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(absoluteFolder))
            {
                Directory.CreateDirectory(absoluteFolder);
            }

            File.WriteAllText(absolutePath, svgContent, Utf8NoBom);
            return assetPath;
        }

        public static string BuildCrosshairSvg(CrosshairController controller)
        {
            CrosshairSnapshot snapshot = ReadSnapshot(controller);
            return BuildCrosshairSvg(snapshot);
        }

        public static string BuildDotSvg()
        {
            StringBuilder builder = CreateSvgBuilder(0.0f, 0.0f, PrimitiveSize, PrimitiveSize);
            AppendCircle(builder, PrimitiveSize * 0.5f, PrimitiveSize * 0.5f, PrimitiveSize * 0.5f, Color.white);
            AppendSvgEnd(builder);
            return builder.ToString();
        }

        public static string BuildLineSvg()
        {
            StringBuilder builder = CreateSvgBuilder(0.0f, 0.0f, PrimitiveSize, PrimitiveLineHeight);
            AppendRect(builder, 0.0f, 0.0f, PrimitiveSize, PrimitiveLineHeight, Color.white, 0.0f);
            AppendSvgEnd(builder);
            return builder.ToString();
        }

        public static string BuildSquareSvg()
        {
            StringBuilder builder = CreateSvgBuilder(0.0f, 0.0f, PrimitiveSize, PrimitiveSize);
            AppendRect(builder, 0.0f, 0.0f, PrimitiveSize, PrimitiveSize, Color.white, 0.0f);
            AppendSvgEnd(builder);
            return builder.ToString();
        }

        public static string BuildRingSvg()
        {
            StringBuilder builder = CreateSvgBuilder(0.0f, 0.0f, PrimitiveSize, PrimitiveSize);
            AppendDonut(builder, PrimitiveSize * 0.5f, PrimitiveSize * 0.5f, PrimitiveSize * 0.5f, 17.0f, Color.white);
            AppendSvgEnd(builder);
            return builder.ToString();
        }

        private static void ExportCrosshairSvgWithDialog(CrosshairController controller)
        {
            string fileName = SanitizeFileName(controller.gameObject.name + "_Crosshair.svg");
            string assetPath = CombineAssetPath(DefaultExportFolder, fileName);
            string absolutePath = ToAbsoluteAssetPath(assetPath);
            bool overwrite = File.Exists(absolutePath);

            if (overwrite
                && !EditorUtility.DisplayDialog(
                    "Export Crosshair SVG",
                    $"{assetPath} already exists. Overwrite it?",
                    "Overwrite",
                    "Cancel"))
            {
                return;
            }

            try
            {
                string writtenAssetPath = ExportCrosshairSvg(controller, DefaultExportFolder, fileName, overwrite);
                AssetDatabase.Refresh();
                Debug.Log($"[CrosshairSvgExporter] Exported '{controller.name}' crosshair SVG to {writtenAssetPath}.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CrosshairSvgExporter] Failed to export '{controller.name}' crosshair SVG: {ex.Message}", controller);
            }
        }

        private static CrosshairController FindSelectedCrosshairController()
        {
            GameObject selected = Selection.activeGameObject;
            return selected == null ? null : selected.GetComponentInChildren<CrosshairController>(true);
        }

        private static string BuildCrosshairSvg(CrosshairSnapshot snapshot)
        {
            snapshot.Clamp();

            float halfExtent = Mathf.Max(PrimitiveSize * 0.5f, CalculateHalfExtent(snapshot));
            float padding = Mathf.Max(2.0f, CalculateMaxStroke(snapshot) + 1.0f);
            float viewMin = -halfExtent - padding;
            float viewSize = (halfExtent + padding) * 2.0f;

            StringBuilder builder = CreateSvgBuilder(viewMin, viewMin, viewSize, viewSize);

            AppendMainShape(builder, snapshot);
            AppendSubShape(builder, snapshot);

            AppendSvgEnd(builder);
            return builder.ToString();
        }

        private static void AppendMainShape(StringBuilder builder, CrosshairSnapshot snapshot)
        {
            switch (snapshot.MainShape)
            {
                case 1:
                    AppendLayerComment(builder, "Main Dot");
                    AppendDot(builder, 0.0f, 0.0f, snapshot.MainSizePixels, snapshot.MainColor, snapshot.MainStrokeThicknessPixels, snapshot.MainStrokeColor);
                    break;

                case 2:
                    AppendLayerComment(builder, "Main Ring");
                    AppendRing(builder, 0.0f, 0.0f, snapshot.MainRingSizePixels, snapshot.MainRingThicknessPixels, snapshot.MainColor, snapshot.MainStrokeThicknessPixels, snapshot.MainStrokeColor);
                    break;
            }
        }

        private static void AppendSubShape(StringBuilder builder, CrosshairSnapshot snapshot)
        {
            switch (snapshot.SubShape)
            {
                case 1:
                    AppendLayerComment(builder, "Sub Rounded Cross");
                    AppendSubCross(builder, snapshot, snapshot.CornerRadiusPixels);
                    break;

                case 2:
                    AppendLayerComment(builder, "Sub Square Cross");
                    AppendSubCross(builder, snapshot, 0.0f);
                    break;

                case 3:
                    AppendLayerComment(builder, "Sub Ring");
                    AppendRing(builder, 0.0f, 0.0f, snapshot.SubRingSizePixels + snapshot.CenterSpacePixels * 2.0f, snapshot.SubRingThicknessPixels, snapshot.SubColor, snapshot.SubStrokeThicknessPixels, snapshot.SubStrokeColor);
                    break;

                case 4:
                    AppendLayerComment(builder, "Sub Dots");
                    AppendSubDots(builder, snapshot);
                    break;
            }
        }

        private static void AppendSubCross(StringBuilder builder, CrosshairSnapshot snapshot, float cornerRadius)
        {
            float gap = snapshot.CenterSpacePixels;
            float width = snapshot.SubWidthPixels;
            float thickness = snapshot.SubThicknessPixels;

            AppendArm(builder, -gap - width, -thickness * 0.5f, width, thickness, cornerRadius, snapshot.SubColor, snapshot.SubStrokeThicknessPixels, snapshot.SubStrokeColor);
            AppendArm(builder, gap, -thickness * 0.5f, width, thickness, cornerRadius, snapshot.SubColor, snapshot.SubStrokeThicknessPixels, snapshot.SubStrokeColor);
            AppendArm(builder, -thickness * 0.5f, -gap - width, thickness, width, cornerRadius, snapshot.SubColor, snapshot.SubStrokeThicknessPixels, snapshot.SubStrokeColor);
            AppendArm(builder, -thickness * 0.5f, gap, thickness, width, cornerRadius, snapshot.SubColor, snapshot.SubStrokeThicknessPixels, snapshot.SubStrokeColor);
        }

        private static void AppendSubDots(StringBuilder builder, CrosshairSnapshot snapshot)
        {
            float offset = snapshot.CenterSpacePixels + snapshot.SubSizePixels * 0.5f;
            AppendDot(builder, -offset, 0.0f, snapshot.SubSizePixels, snapshot.SubColor, snapshot.SubStrokeThicknessPixels, snapshot.SubStrokeColor);
            AppendDot(builder, offset, 0.0f, snapshot.SubSizePixels, snapshot.SubColor, snapshot.SubStrokeThicknessPixels, snapshot.SubStrokeColor);
            AppendDot(builder, 0.0f, -offset, snapshot.SubSizePixels, snapshot.SubColor, snapshot.SubStrokeThicknessPixels, snapshot.SubStrokeColor);
            AppendDot(builder, 0.0f, offset, snapshot.SubSizePixels, snapshot.SubColor, snapshot.SubStrokeThicknessPixels, snapshot.SubStrokeColor);
        }

        private static void AppendDot(StringBuilder builder, float centerX, float centerY, float size, Color color, float strokeThickness, Color strokeColor)
        {
            if (size <= 0.0f)
            {
                return;
            }

            float radius = size * 0.5f;
            if (strokeThickness > 0.0f)
            {
                AppendCircle(builder, centerX, centerY, radius + strokeThickness, strokeColor);
            }

            AppendCircle(builder, centerX, centerY, radius, color);
        }

        private static void AppendRing(StringBuilder builder, float centerX, float centerY, float diameter, float thickness, Color color, float strokeThickness, Color strokeColor)
        {
            if (diameter <= 0.0f || thickness <= 0.0f)
            {
                return;
            }

            float renderedThickness = Mathf.Max(RingReferenceThicknessPixels, thickness);
            float innerDiameter = CalculateReferenceInnerDiameter(diameter);
            float colorOuterDiameter = CalculateOutwardRingOuterDiameter(innerDiameter, renderedThickness);
            float clampedStrokeThickness = CalculateClampedRingStrokeThickness(innerDiameter, strokeThickness);

            if (clampedStrokeThickness > 0.0f)
            {
                float strokeInnerDiameter = Mathf.Max(0.0f, innerDiameter - clampedStrokeThickness * 2.0f);
                float strokeOuterDiameter = colorOuterDiameter + clampedStrokeThickness * 2.0f;
                AppendDonut(builder, centerX, centerY, strokeOuterDiameter * 0.5f, strokeInnerDiameter * 0.5f, strokeColor);
            }

            AppendDonut(builder, centerX, centerY, colorOuterDiameter * 0.5f, innerDiameter * 0.5f, color);
        }

        private static void AppendArm(StringBuilder builder, float left, float top, float width, float height, float cornerRadius, Color color, float strokeThickness, Color strokeColor)
        {
            if (width <= 0.0f || height <= 0.0f)
            {
                return;
            }

            if (strokeThickness > 0.0f)
            {
                AppendRect(
                    builder,
                    left - strokeThickness,
                    top - strokeThickness,
                    width + strokeThickness * 2.0f,
                    height + strokeThickness * 2.0f,
                    strokeColor,
                    cornerRadius + strokeThickness);
            }

            AppendRect(builder, left, top, width, height, color, cornerRadius);
        }

        private static void AppendCircle(StringBuilder builder, float centerX, float centerY, float radius, Color color)
        {
            if (radius <= 0.0f)
            {
                return;
            }

            builder.Append("  <circle cx=\"");
            builder.Append(Format(centerX));
            builder.Append("\" cy=\"");
            builder.Append(Format(centerY));
            builder.Append("\" r=\"");
            builder.Append(Format(radius));
            builder.Append("\" fill=\"");
            builder.Append(ToSvgColor(color));
            builder.Append("\" fill-opacity=\"");
            builder.Append(Format(Mathf.Clamp01(color.a)));
            builder.AppendLine("\"/>");
        }

        private static void AppendRect(StringBuilder builder, float left, float top, float width, float height, Color color, float cornerRadius)
        {
            if (width <= 0.0f || height <= 0.0f)
            {
                return;
            }

            builder.Append("  <rect x=\"");
            builder.Append(Format(left));
            builder.Append("\" y=\"");
            builder.Append(Format(top));
            builder.Append("\" width=\"");
            builder.Append(Format(width));
            builder.Append("\" height=\"");
            builder.Append(Format(height));
            if (cornerRadius > 0.0f)
            {
                builder.Append("\" rx=\"");
                builder.Append(Format(cornerRadius));
                builder.Append("\" ry=\"");
                builder.Append(Format(cornerRadius));
            }

            builder.Append("\" fill=\"");
            builder.Append(ToSvgColor(color));
            builder.Append("\" fill-opacity=\"");
            builder.Append(Format(Mathf.Clamp01(color.a)));
            builder.AppendLine("\"/>");
        }

        private static void AppendDonut(StringBuilder builder, float centerX, float centerY, float outerRadius, float innerRadius, Color color)
        {
            if (outerRadius <= 0.0f)
            {
                return;
            }

            innerRadius = Mathf.Clamp(innerRadius, 0.0f, outerRadius);

            if (innerRadius <= 0.0f)
            {
                AppendCircle(builder, centerX, centerY, outerRadius, color);
                return;
            }

            builder.Append("  <path fill-rule=\"evenodd\" d=\"");
            AppendCirclePath(builder, centerX, centerY, outerRadius);
            builder.Append(" ");
            AppendCirclePath(builder, centerX, centerY, innerRadius);
            builder.Append("\" fill=\"");
            builder.Append(ToSvgColor(color));
            builder.Append("\" fill-opacity=\"");
            builder.Append(Format(Mathf.Clamp01(color.a)));
            builder.AppendLine("\"/>");
        }

        private static void AppendCirclePath(StringBuilder builder, float centerX, float centerY, float radius)
        {
            builder.Append("M ");
            builder.Append(Format(centerX));
            builder.Append(" ");
            builder.Append(Format(centerY - radius));
            builder.Append(" A ");
            builder.Append(Format(radius));
            builder.Append(" ");
            builder.Append(Format(radius));
            builder.Append(" 0 1 1 ");
            builder.Append(Format(centerX));
            builder.Append(" ");
            builder.Append(Format(centerY + radius));
            builder.Append(" A ");
            builder.Append(Format(radius));
            builder.Append(" ");
            builder.Append(Format(radius));
            builder.Append(" 0 1 1 ");
            builder.Append(Format(centerX));
            builder.Append(" ");
            builder.Append(Format(centerY - radius));
            builder.Append(" Z");
        }

        private static StringBuilder CreateSvgBuilder(float minX, float minY, float width, float height)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            builder.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.1\" viewBox=\"");
            builder.Append(Format(minX));
            builder.Append(" ");
            builder.Append(Format(minY));
            builder.Append(" ");
            builder.Append(Format(width));
            builder.Append(" ");
            builder.Append(Format(height));
            builder.AppendLine("\">");
            return builder;
        }

        private static void AppendSvgEnd(StringBuilder builder)
        {
            builder.AppendLine("</svg>");
        }

        private static void AppendLayerComment(StringBuilder builder, string label)
        {
            builder.Append("  <!-- ");
            builder.Append(label);
            builder.AppendLine(" -->");
        }

        private static CrosshairSnapshot ReadSnapshot(CrosshairController controller)
        {
            SerializedObject serializedObject = new SerializedObject(controller);
            serializedObject.Update();

            return new CrosshairSnapshot
            {
                MainShape = GetInt(serializedObject, "m_mainShape"),
                SubShape = GetInt(serializedObject, "m_subShape"),
                CenterSpacePixels = GetFloat(serializedObject, "m_centerSpacePixels"),
                MainSizePixels = GetFloat(serializedObject, "m_mainSizePixels"),
                MainRingSizePixels = GetFloat(serializedObject, "m_mainRingSizePixels"),
                MainRingThicknessPixels = GetFloat(serializedObject, "m_mainRingThicknessPixels"),
                MainColor = GetColor(serializedObject, "m_mainColor"),
                MainStrokeThicknessPixels = GetFloat(serializedObject, "m_mainStrokeThicknessPixels"),
                MainStrokeColor = GetColor(serializedObject, "m_mainStrokeColor"),
                SubSizePixels = GetFloat(serializedObject, "m_subSizePixels"),
                SubWidthPixels = GetFloat(serializedObject, "m_subWidthPixels"),
                SubThicknessPixels = GetFloat(serializedObject, "m_subThicknessPixels"),
                SubRingSizePixels = GetFloat(serializedObject, "m_subRingSizePixels"),
                SubRingThicknessPixels = GetFloat(serializedObject, "m_subRingThicknessPixels"),
                SubColor = GetColor(serializedObject, "m_subColor"),
                SubStrokeThicknessPixels = GetFloat(serializedObject, "m_subStrokeThicknessPixels"),
                SubStrokeColor = GetColor(serializedObject, "m_subStrokeColor"),
                CornerRadiusPixels = GetFloat(serializedObject, "m_cornerRadiusPixels"),
            };
        }

        private static int GetInt(SerializedObject serializedObject, string propertyName)
        {
            SerializedProperty property = RequireProperty(serializedObject, propertyName);
            return property.intValue;
        }

        private static float GetFloat(SerializedObject serializedObject, string propertyName)
        {
            SerializedProperty property = RequireProperty(serializedObject, propertyName);
            return property.floatValue;
        }

        private static Color GetColor(SerializedObject serializedObject, string propertyName)
        {
            SerializedProperty property = RequireProperty(serializedObject, propertyName);
            return property.colorValue;
        }

        private static SerializedProperty RequireProperty(SerializedObject serializedObject, string propertyName)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException($"Serialized property '{propertyName}' was not found.");
            }

            return property;
        }

        private static float CalculateHalfExtent(CrosshairSnapshot snapshot)
        {
            float extent = 0.0f;

            switch (snapshot.MainShape)
            {
                case 1:
                    extent = Mathf.Max(extent, snapshot.MainSizePixels * 0.5f + snapshot.MainStrokeThicknessPixels);
                    break;
                case 2:
                    extent = Mathf.Max(extent, CalculateRingOuterDiameter(snapshot.MainRingSizePixels, snapshot.MainRingThicknessPixels, snapshot.MainStrokeThicknessPixels) * 0.5f);
                    break;
            }

            switch (snapshot.SubShape)
            {
                case 1:
                case 2:
                    extent = Mathf.Max(extent, snapshot.CenterSpacePixels + snapshot.SubWidthPixels + snapshot.SubStrokeThicknessPixels);
                    extent = Mathf.Max(extent, snapshot.SubThicknessPixels * 0.5f + snapshot.SubStrokeThicknessPixels);
                    break;
                case 3:
                    extent = Mathf.Max(extent, CalculateRingOuterDiameter(snapshot.SubRingSizePixels + snapshot.CenterSpacePixels * 2.0f, snapshot.SubRingThicknessPixels, snapshot.SubStrokeThicknessPixels) * 0.5f);
                    break;
                case 4:
                    extent = Mathf.Max(extent, snapshot.CenterSpacePixels + snapshot.SubSizePixels + snapshot.SubStrokeThicknessPixels);
                    break;
            }

            return extent;
        }

        private static float CalculateMaxStroke(CrosshairSnapshot snapshot)
        {
            return Mathf.Max(snapshot.MainStrokeThicknessPixels, snapshot.SubStrokeThicknessPixels);
        }

        private static float CalculateRingOuterDiameter(float diameter, float thickness, float strokeThickness)
        {
            float renderedThickness = Mathf.Max(RingReferenceThicknessPixels, Mathf.Max(0.0f, thickness));
            float innerDiameter = CalculateReferenceInnerDiameter(Mathf.Max(0.0f, diameter));
            float colorOuterDiameter = CalculateOutwardRingOuterDiameter(innerDiameter, renderedThickness);
            float clampedStrokeThickness = CalculateClampedRingStrokeThickness(innerDiameter, strokeThickness);
            return colorOuterDiameter + clampedStrokeThickness * 2.0f;
        }

        private static float CalculateReferenceInnerDiameter(float referenceOuterDiameter)
        {
            return Mathf.Max(0.0f, referenceOuterDiameter - RingReferenceThicknessPixels * 2.0f);
        }

        private static float CalculateClampedRingStrokeThickness(float innerDiameter, float strokeThickness)
        {
            float maxInnerStrokeThickness = Mathf.Max(0.0f, innerDiameter * 0.5f);
            return Mathf.Clamp(Mathf.Max(0.0f, strokeThickness), 0.0f, maxInnerStrokeThickness);
        }

        private static float CalculateOutwardRingOuterDiameter(float innerDiameter, float thickness)
        {
            return innerDiameter + Mathf.Max(0.0f, thickness) * 2.0f;
        }

        private static string CombineAssetPath(string assetFolderPath, string fileName)
        {
            return NormalizeAssetFolderPath(assetFolderPath) + "/" + EnsureSvgFileName(fileName);
        }

        private static string NormalizeAssetFolderPath(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                throw new ArgumentException("Export folder path is empty.", nameof(folderPath));
            }

            string normalized = folderPath.Replace('\\', '/').TrimEnd('/');
            string dataPath = Application.dataPath.Replace('\\', '/').TrimEnd('/');

            if (Path.IsPathRooted(normalized))
            {
                if (!normalized.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException($"Export folder must be under this Unity project's Assets folder: {folderPath}", nameof(folderPath));
                }

                string relativeToAssets = normalized.Substring(dataPath.Length).TrimStart('/');
                return string.IsNullOrEmpty(relativeToAssets) ? "Assets" : "Assets/" + relativeToAssets;
            }

            if (!normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase)
                && !normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Export folder must be an asset path under Assets: {folderPath}", nameof(folderPath));
            }

            return normalized;
        }

        private static string ToAbsoluteAssetPath(string assetPath)
        {
            string normalized = assetPath.Replace('\\', '/');
            if (!normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase)
                && !normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Asset path must be under Assets: {assetPath}", nameof(assetPath));
            }

            string relativeToAssets = normalized.Length == "Assets".Length
                ? string.Empty
                : normalized.Substring("Assets".Length).TrimStart('/');
            return Path.Combine(Application.dataPath, relativeToAssets.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string EnsureSvgFileName(string fileName)
        {
            string sanitized = SanitizeFileName(fileName);
            return sanitized.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? sanitized : sanitized + ".svg";
        }

        private static string SanitizeFileName(string fileName)
        {
            string safeName = Path.GetFileName(string.IsNullOrWhiteSpace(fileName) ? "Crosshair.svg" : fileName);
            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                safeName = safeName.Replace(invalidChar, '_');
            }

            return string.IsNullOrWhiteSpace(safeName) ? "Crosshair.svg" : safeName;
        }

        private static string Format(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string ToSvgColor(Color color)
        {
            return "#" + ColorUtility.ToHtmlStringRGB(color);
        }

        private sealed class CrosshairSnapshot
        {
            public int MainShape;
            public int SubShape;
            public float CenterSpacePixels;
            public float MainSizePixels;
            public float MainRingSizePixels;
            public float MainRingThicknessPixels;
            public Color MainColor;
            public float MainStrokeThicknessPixels;
            public Color MainStrokeColor;
            public float SubSizePixels;
            public float SubWidthPixels;
            public float SubThicknessPixels;
            public float SubRingSizePixels;
            public float SubRingThicknessPixels;
            public Color SubColor;
            public float SubStrokeThicknessPixels;
            public Color SubStrokeColor;
            public float CornerRadiusPixels;

            public void Clamp()
            {
                CenterSpacePixels = Mathf.Max(0.0f, CenterSpacePixels);
                MainSizePixels = Mathf.Max(0.0f, MainSizePixels);
                MainRingSizePixels = Mathf.Max(0.0f, MainRingSizePixels);
                MainRingThicknessPixels = Mathf.Max(0.0f, MainRingThicknessPixels);
                MainStrokeThicknessPixels = Mathf.Max(0.0f, MainStrokeThicknessPixels);
                SubSizePixels = Mathf.Max(0.0f, SubSizePixels);
                SubWidthPixels = Mathf.Max(0.0f, SubWidthPixels);
                SubThicknessPixels = Mathf.Max(0.0f, SubThicknessPixels);
                SubRingSizePixels = Mathf.Max(0.0f, SubRingSizePixels);
                SubRingThicknessPixels = Mathf.Max(0.0f, SubRingThicknessPixels);
                SubStrokeThicknessPixels = Mathf.Max(0.0f, SubStrokeThicknessPixels);
                CornerRadiusPixels = Mathf.Max(0.0f, CornerRadiusPixels);
            }
        }
    }
}
#endif
