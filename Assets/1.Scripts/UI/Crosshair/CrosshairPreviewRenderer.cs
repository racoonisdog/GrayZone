using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 설정 화면 미리보기용으로 <see cref="CrosshairStyle"/>을 텍스처 픽셀에 그립니다.
/// </summary>
/// <remarks>
/// 크기 계산은 <see cref="CrosshairController"/>의 배치 규칙을 따릅니다. 링은 기준 지름에서 1px 기준 두께를 뺀 안쪽 지름을
/// 유지하고 바깥쪽으로 두께가 늘어나며, 테두리는 각 도형 뒤에 바깥쪽으로 덧그립니다. 십자 팔은 중앙 간격 바깥에서 시작합니다.
/// 가장자리는 거리 함수로 부드럽게 처리합니다.
/// </remarks>
public static class CrosshairPreviewRenderer
{
    private const float RingReferenceThickness = 1f;

    private readonly struct Layer
    {
        public readonly Func<float, float, float> Distance;
        public readonly Color Color;

        public Layer(Func<float, float, float> distance, Color color)
        {
            Distance = distance;
            Color = color;
        }
    }

    /// <summary>조준선이 중심에서 차지하는 최대 반경(픽셀)입니다. 미리보기 확대율을 정할 때 씁니다.</summary>
    public static float Extent(CrosshairStyle s)
    {
        float extent = 1f;
        float mainStroke = Mathf.Max(0f, s.mainStrokeThicknessPixels);
        if (s.mainShape == CrosshairController.MainShape.Dot)
        {
            extent = Mathf.Max(extent, s.mainSizePixels * 0.5f + mainStroke);
        }
        else if (s.mainShape == CrosshairController.MainShape.Ring)
        {
            RingRadii(s.mainRingSizePixels, s.mainRingThicknessPixels, out _, out float outer);
            extent = Mathf.Max(extent, outer + mainStroke);
        }

        float gap = Mathf.Max(0f, s.centerSpacePixels);
        float subStroke = Mathf.Max(0f, s.subStrokeThicknessPixels);
        switch (s.subShape)
        {
            case CrosshairController.SubShape.RoundedCross:
            case CrosshairController.SubShape.SquareCross:
                extent = Mathf.Max(extent, gap + s.subWidthPixels + subStroke);
                break;
            case CrosshairController.SubShape.Dot:
                extent = Mathf.Max(extent, gap + s.subSizePixels + subStroke);
                break;
            case CrosshairController.SubShape.Ring:
                RingRadii(s.subRingSizePixels + gap * 2f, s.subRingThicknessPixels, out _, out float outer);
                extent = Mathf.Max(extent, outer + subStroke);
                break;
        }

        return extent;
    }

    /// <summary>
    /// 조준선을 픽셀 배열에 그립니다. 배열은 투명으로 비워 둔 상태여야 합니다.
    /// </summary>
    /// <param name="zoom">조준선 1픽셀이 텍스처 몇 픽셀이 되는지입니다.</param>
    public static void Render(CrosshairStyle s, Color32[] pixels, int width, int height, float zoom)
    {
        var layers = new List<Layer>();
        AddMain(s, layers);
        AddSub(s, layers);
        if (layers.Count == 0)
        {
            return;
        }

        float halfWidth = width * 0.5f;
        float halfHeight = height * 0.5f;
        for (int py = 0; py < height; py++)
        {
            float y = (py + 0.5f - halfHeight) / zoom;
            for (int px = 0; px < width; px++)
            {
                float x = (px + 0.5f - halfWidth) / zoom;
                float r = 0f, g = 0f, b = 0f, a = 0f;
                foreach (Layer layer in layers)
                {
                    float coverage = Mathf.Clamp01(0.5f - layer.Distance(x, y) * zoom) * layer.Color.a;
                    if (coverage <= 0f) continue;

                    // 직선 알파 기준의 over 합성입니다.
                    float outA = coverage + a * (1f - coverage);
                    r = (layer.Color.r * coverage + r * a * (1f - coverage)) / outA;
                    g = (layer.Color.g * coverage + g * a * (1f - coverage)) / outA;
                    b = (layer.Color.b * coverage + b * a * (1f - coverage)) / outA;
                    a = outA;
                }

                pixels[py * width + px] = new Color(r, g, b, a);
            }
        }
    }

    private static void AddMain(CrosshairStyle s, List<Layer> layers)
    {
        float stroke = Mathf.Max(0f, s.mainStrokeThicknessPixels);
        if (s.mainShape == CrosshairController.MainShape.Dot && s.mainSizePixels > 0f)
        {
            AddCircle(layers, 0f, 0f, s.mainSizePixels * 0.5f, stroke, s.mainColor, s.mainStrokeColor);
        }
        else if (s.mainShape == CrosshairController.MainShape.Ring)
        {
            AddRing(layers, s.mainRingSizePixels, s.mainRingThicknessPixels, stroke, s.mainColor, s.mainStrokeColor);
        }
    }

    private static void AddSub(CrosshairStyle s, List<Layer> layers)
    {
        float gap = Mathf.Max(0f, s.centerSpacePixels);
        float stroke = Mathf.Max(0f, s.subStrokeThicknessPixels);
        switch (s.subShape)
        {
            case CrosshairController.SubShape.RoundedCross:
            case CrosshairController.SubShape.SquareCross:
            {
                float length = Mathf.Max(0f, s.subWidthPixels);
                float thickness = Mathf.Max(0f, s.subThicknessPixels);
                if (length <= 0f || thickness <= 0f) return;

                float radius = s.subShape == CrosshairController.SubShape.RoundedCross ? Mathf.Max(0f, s.cornerRadiusPixels) : 0f;
                float offset = gap + length * 0.5f;
                Func<float, float, float, float, float, float> arms = (x, y, hx, hy, rad) => Mathf.Min(
                    Mathf.Min(RoundedBox(x - offset, y, hx, hy, rad), RoundedBox(x + offset, y, hx, hy, rad)),
                    Mathf.Min(RoundedBox(x, y - offset, hy, hx, rad), RoundedBox(x, y + offset, hy, hx, rad)));
                float halfLength = length * 0.5f;
                float halfThickness = thickness * 0.5f;
                if (stroke > 0f)
                {
                    layers.Add(new Layer((x, y) => arms(x, y, halfLength + stroke, halfThickness + stroke, radius + stroke), s.subStrokeColor));
                }

                layers.Add(new Layer((x, y) => arms(x, y, halfLength, halfThickness, radius), s.subColor));
                break;
            }

            case CrosshairController.SubShape.Dot:
            {
                float size = Mathf.Max(0f, s.subSizePixels);
                if (size <= 0f) return;

                float offset = gap + size * 0.5f;
                Func<float, float, float, float> dots = (x, y, rad) => Mathf.Min(
                    Mathf.Min(Circle(x - offset, y, rad), Circle(x + offset, y, rad)),
                    Mathf.Min(Circle(x, y - offset, rad), Circle(x, y + offset, rad)));
                float radius = size * 0.5f;
                if (stroke > 0f)
                {
                    layers.Add(new Layer((x, y) => dots(x, y, radius + stroke), s.subStrokeColor));
                }

                layers.Add(new Layer((x, y) => dots(x, y, radius), s.subColor));
                break;
            }

            case CrosshairController.SubShape.Ring:
                AddRing(layers, s.subRingSizePixels + gap * 2f, s.subRingThicknessPixels, stroke, s.subColor, s.subStrokeColor);
                break;
        }
    }

    private static void AddCircle(List<Layer> layers, float cx, float cy, float radius, float stroke, Color color, Color strokeColor)
    {
        if (stroke > 0f)
        {
            layers.Add(new Layer((x, y) => Circle(x - cx, y - cy, radius + stroke), strokeColor));
        }

        layers.Add(new Layer((x, y) => Circle(x - cx, y - cy, radius), color));
    }

    private static void AddRing(List<Layer> layers, float diameter, float thickness, float stroke, Color color, Color strokeColor)
    {
        if (diameter <= 0f || thickness <= 0f) return;

        RingRadii(diameter, thickness, out float inner, out float outer);
        float clampedStroke = Mathf.Clamp(stroke, 0f, inner);
        if (clampedStroke > 0f)
        {
            float strokeInner = inner - clampedStroke;
            float strokeOuter = outer + clampedStroke;
            layers.Add(new Layer((x, y) => Annulus(x, y, strokeInner, strokeOuter), strokeColor));
        }

        layers.Add(new Layer((x, y) => Annulus(x, y, inner, outer), color));
    }

    /// <summary>CrosshairController.ApplyRing과 같은 방식으로 링의 안쪽·바깥쪽 반지름을 구합니다.</summary>
    private static void RingRadii(float diameter, float thickness, out float inner, out float outer)
    {
        float innerDiameter = Mathf.Max(0f, diameter - RingReferenceThickness * 2f);
        float rendered = Mathf.Max(RingReferenceThickness, thickness);
        inner = innerDiameter * 0.5f;
        outer = (innerDiameter + rendered * 2f) * 0.5f;
    }

    private static float Circle(float x, float y, float radius)
    {
        return Mathf.Sqrt(x * x + y * y) - radius;
    }

    private static float Annulus(float x, float y, float inner, float outer)
    {
        float mid = (inner + outer) * 0.5f;
        return Mathf.Abs(Mathf.Sqrt(x * x + y * y) - mid) - (outer - inner) * 0.5f;
    }

    private static float RoundedBox(float x, float y, float halfX, float halfY, float radius)
    {
        radius = Mathf.Min(radius, Mathf.Min(halfX, halfY));
        float qx = Mathf.Abs(x) - halfX + radius;
        float qy = Mathf.Abs(y) - halfY + radius;
        float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
        return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
    }
}
