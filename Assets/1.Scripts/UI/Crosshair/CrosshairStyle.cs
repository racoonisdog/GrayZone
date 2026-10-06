using System;
using UnityEngine;

/// <summary>
/// 조준선의 모양 값(내부 Main + 외부 Sub) 한 벌입니다. 설정 화면, 저장 파일, <see cref="CrosshairController"/> 사이에서 값을 옮길 때 씁니다.
/// </summary>
/// <remarks>
/// JsonUtility로 저장하므로 필드 이름이 저장 파일의 키입니다. 이름을 바꾸면 기존 저장값을 읽지 못합니다.
/// 탄퍼짐 연동 여부(<see cref="useSpreadAccuracy"/>)는 투척물 조준선에서만 적용합니다. 캐릭터 조준선은 컨트롤러의 씬 설정을 그대로 둡니다.
/// </remarks>
[Serializable]
public sealed class CrosshairStyle
{
    [Tooltip("탄퍼짐에 따라 간격을 바꿀지 여부입니다. 투척물 조준선에만 적용합니다.")]
    public bool useSpreadAccuracy = true;

    [Header("내부 (Main)")]
    [Tooltip("중앙 표시 형태입니다.")]
    public CrosshairController.MainShape mainShape = CrosshairController.MainShape.Dot;
    [Tooltip("중앙 점 지름(픽셀)입니다. 형태가 점일 때 씁니다.")]
    public float mainSizePixels = 3.0f;
    [Tooltip("중앙 원의 기준 바깥 지름(픽셀)입니다. 형태가 원형일 때 씁니다.")]
    public float mainRingSizePixels = 8.0f;
    [Tooltip("중앙 원 두께(픽셀)입니다. 안쪽 지름은 유지하고 바깥쪽으로 늘어납니다.")]
    public float mainRingThicknessPixels = 1.0f;
    [Tooltip("중앙 표시 색입니다. 알파가 불투명도입니다.")]
    public Color mainColor = Color.white;
    [Tooltip("중앙 표시 테두리 두께(픽셀)입니다. 0이면 테두리가 없습니다.")]
    public float mainStrokeThicknessPixels = 1.0f;
    [Tooltip("중앙 표시 테두리 색입니다.")]
    public Color mainStrokeColor = Color.black;

    [Header("외부 (Sub)")]
    [Tooltip("주변 표시 형태입니다.")]
    public CrosshairController.SubShape subShape = CrosshairController.SubShape.SquareCross;
    [Tooltip("중심에서 주변 표시까지의 기본 간격(픽셀)입니다. 탄퍼짐 간격이 여기에 더해집니다.")]
    public float centerSpacePixels = 5.0f;
    [Tooltip("주변 점 지름(픽셀)입니다. 형태가 점일 때 씁니다.")]
    public float subSizePixels = 1.0f;
    [Tooltip("십자 선 길이(픽셀)입니다.")]
    public float subWidthPixels = 10.0f;
    [Tooltip("십자 선 두께(픽셀)입니다.")]
    public float subThicknessPixels = 1.0f;
    [Tooltip("주변 원의 기준 바깥 지름(픽셀)입니다. 형태가 원형일 때 씁니다.")]
    public float subRingSizePixels = 16.0f;
    [Tooltip("주변 원 두께(픽셀)입니다.")]
    public float subRingThicknessPixels = 1.0f;
    [Tooltip("주변 표시 색입니다. 알파가 불투명도입니다.")]
    public Color subColor = Color.white;
    [Tooltip("주변 표시 테두리 두께(픽셀)입니다. 0이면 테두리가 없습니다.")]
    public float subStrokeThicknessPixels = 1.0f;
    [Tooltip("주변 표시 테두리 색입니다.")]
    public Color subStrokeColor = Color.black;
    [Tooltip("둥근 십자의 모서리 반지름(픽셀)입니다.")]
    public float cornerRadiusPixels = 2.0f;

    /// <summary>컨트롤러의 현재 모양 값을 복사합니다.</summary>
    public static CrosshairStyle Capture(CrosshairController crosshair)
    {
        return new CrosshairStyle
        {
            useSpreadAccuracy = crosshair.SpreadAccuracyEnabled,
            mainShape = crosshair.CurrentMainShape,
            mainSizePixels = crosshair.MainSizePixels,
            mainRingSizePixels = crosshair.MainRingSizePixels,
            mainRingThicknessPixels = crosshair.MainRingThicknessPixels,
            mainColor = crosshair.MainColor,
            mainStrokeThicknessPixels = crosshair.MainStrokeThicknessPixels,
            mainStrokeColor = crosshair.MainStrokeColor,
            subShape = crosshair.CurrentSubShape,
            centerSpacePixels = crosshair.CenterSpacePixels,
            subSizePixels = crosshair.SubSizePixels,
            subWidthPixels = crosshair.SubWidthPixels,
            subThicknessPixels = crosshair.SubThicknessPixels,
            subRingSizePixels = crosshair.SubRingSizePixels,
            subRingThicknessPixels = crosshair.SubRingThicknessPixels,
            subColor = crosshair.SubColor,
            subStrokeThicknessPixels = crosshair.SubStrokeThicknessPixels,
            subStrokeColor = crosshair.SubStrokeColor,
            cornerRadiusPixels = crosshair.CornerRadiusPixels,
        };
    }

    /// <summary>모양 값을 컨트롤러에 적용합니다.</summary>
    /// <param name="includeSpreadAccuracy">true이면 탄퍼짐 연동 여부도 덮어씁니다. 투척물 조준선에서만 씁니다.</param>
    public void ApplyTo(CrosshairController crosshair, bool includeSpreadAccuracy)
    {
        if (includeSpreadAccuracy)
        {
            crosshair.SetSpreadAccuracyEnabled(useSpreadAccuracy);
        }

        crosshair.CurrentMainShape = mainShape;
        crosshair.MainSizePixels = mainSizePixels;
        crosshair.MainRingSizePixels = mainRingSizePixels;
        crosshair.MainRingThicknessPixels = mainRingThicknessPixels;
        crosshair.MainColor = mainColor;
        crosshair.MainStrokeThicknessPixels = mainStrokeThicknessPixels;
        crosshair.MainStrokeColor = mainStrokeColor;
        crosshair.CurrentSubShape = subShape;
        crosshair.CenterSpacePixels = centerSpacePixels;
        crosshair.SubSizePixels = subSizePixels;
        crosshair.SubWidthPixels = subWidthPixels;
        crosshair.SubThicknessPixels = subThicknessPixels;
        crosshair.SubRingSizePixels = subRingSizePixels;
        crosshair.SubRingThicknessPixels = subRingThicknessPixels;
        crosshair.SubColor = subColor;
        crosshair.SubStrokeThicknessPixels = subStrokeThicknessPixels;
        crosshair.SubStrokeColor = subStrokeColor;
        crosshair.CornerRadiusPixels = cornerRadiusPixels;
    }

    /// <summary>값이 같은 새 인스턴스를 만듭니다.</summary>
    public CrosshairStyle Clone()
    {
        return (CrosshairStyle)MemberwiseClone();
    }
}
