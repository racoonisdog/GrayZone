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

    [Header("동적")]
    [Tooltip("켜면 외곽이 탄퍼짐·반동·조준에 따라 벌어지고 줄어듭니다(벌어지는 양은 고정값). 비조준·조준 조준선을 나누지 않습니다. 끄면 반동·조준과 관계없이 설정 화면 미리보기 크기 그대로 고정되고, 비조준·조준 조준선(내부·외부)을 따로 정할 수 있습니다.")]
    public bool subDynamic = true;
    // 캐릭터마다 기본값 에셋이 정합니다. 설정 화면에서는 고치지 않습니다.
    [Tooltip("외곽이 벌어지는 방식입니다. CurrentSpread는 현재 탄퍼짐을 따라 벌어지고 회복하며, WeaponMaxSpread는 자세별 최대 탄퍼짐에 고정하고 쏠 때 반동 펄스로 잠깐 벌어집니다.")]
    public AimController.CrosshairSpreadMode subSpreadMode = AimController.CrosshairSpreadMode.CurrentSpread;

    [Header("조준 시")]
    [Tooltip("켜면 조준(ADS) 중에는 아래 조준 시 내부·외부 값을 씁니다. 동적 크로스헤어를 끈 경우에만 씁니다.")]
    public bool separateAdsSub;
    [Tooltip("조준(ADS) 중 내부 값입니다. separateAdsSub가 켜져 있을 때만 씁니다.")]
    public CrosshairMainStyle adsMain = new CrosshairMainStyle();
    [Tooltip("조준(ADS) 중 외곽 값입니다. separateAdsSub가 켜져 있을 때만 씁니다.")]
    public CrosshairSubStyle adsSub = new CrosshairSubStyle();

    /// <summary>비조준 내부 값을 한 벌로 꺼냅니다.</summary>
    public CrosshairMainStyle GetHipMain()
    {
        return new CrosshairMainStyle
        {
            mainShape = mainShape,
            mainSizePixels = mainSizePixels,
            mainRingSizePixels = mainRingSizePixels,
            mainRingThicknessPixels = mainRingThicknessPixels,
            mainColor = mainColor,
            mainStrokeThicknessPixels = mainStrokeThicknessPixels,
            mainStrokeColor = mainStrokeColor,
        };
    }

    /// <summary>조준 중 내부 값입니다. 동적 크로스헤어를 켰거나 따로 정하지 않았으면 비조준 내부와 같습니다.</summary>
    public CrosshairMainStyle GetAdsMain()
    {
        return !subDynamic && separateAdsSub && adsMain != null ? adsMain.Clone() : GetHipMain();
    }

    /// <summary>지정한 내부 값을 이 스타일의 비조준 내부 칸에 씁니다.</summary>
    public void SetHipMain(CrosshairMainStyle main)
    {
        mainShape = main.mainShape;
        mainSizePixels = main.mainSizePixels;
        mainRingSizePixels = main.mainRingSizePixels;
        mainRingThicknessPixels = main.mainRingThicknessPixels;
        mainColor = main.mainColor;
        mainStrokeThicknessPixels = main.mainStrokeThicknessPixels;
        mainStrokeColor = main.mainStrokeColor;
    }

    /// <summary>비조준 외곽 값을 한 벌로 꺼냅니다.</summary>
    public CrosshairSubStyle GetHipSub()
    {
        return new CrosshairSubStyle
        {
            subShape = subShape,
            centerSpacePixels = centerSpacePixels,
            subSizePixels = subSizePixels,
            subWidthPixels = subWidthPixels,
            subThicknessPixels = subThicknessPixels,
            subRingSizePixels = subRingSizePixels,
            subRingThicknessPixels = subRingThicknessPixels,
            subColor = subColor,
            subStrokeThicknessPixels = subStrokeThicknessPixels,
            subStrokeColor = subStrokeColor,
            cornerRadiusPixels = cornerRadiusPixels,
            subSpreadMode = subSpreadMode,
        };
    }

    /// <summary>조준 중 외곽 값입니다. 동적 크로스헤어를 켰거나 따로 정하지 않았으면 비조준 외곽과 같습니다.</summary>
    public CrosshairSubStyle GetAdsSub()
    {
        return !subDynamic && separateAdsSub && adsSub != null ? adsSub.Clone() : GetHipSub();
    }

    /// <summary>지정한 외곽 값을 이 스타일의 비조준 외곽 칸에 씁니다.</summary>
    public void SetHipSub(CrosshairSubStyle sub)
    {
        subShape = sub.subShape;
        centerSpacePixels = sub.centerSpacePixels;
        subSizePixels = sub.subSizePixels;
        subWidthPixels = sub.subWidthPixels;
        subThicknessPixels = sub.subThicknessPixels;
        subRingSizePixels = sub.subRingSizePixels;
        subRingThicknessPixels = sub.subRingThicknessPixels;
        subColor = sub.subColor;
        subStrokeThicknessPixels = sub.subStrokeThicknessPixels;
        subStrokeColor = sub.subStrokeColor;
        cornerRadiusPixels = sub.cornerRadiusPixels;
        subSpreadMode = sub.subSpreadMode;
    }

    /// <summary>
    /// 조준 중 내부·외부를 비조준 칸에 넣은 보기용 사본을 만듭니다. 설정 화면이 같은 편집 줄로 조준 조준선을 고칠 때 씁니다.
    /// </summary>
    public CrosshairStyle CreateAdsView()
    {
        CrosshairStyle view = Clone();
        view.SetHipMain(GetAdsMain());
        view.SetHipSub(GetAdsSub());
        return view;
    }

    /// <summary>보기용 사본에서 고친 내부·외부 값을 조준 중 값으로 저장하고, 조준 조준선을 따로 쓰도록 켭니다.</summary>
    public void StoreAdsView(CrosshairStyle view)
    {
        adsMain = view.GetHipMain();
        adsSub = view.GetHipSub();
        separateAdsSub = true;
    }

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

    /// <summary>값이 같은 새 인스턴스를 만듭니다. 조준 중 내부·외부도 따로 복사합니다(같은 객체를 공유하지 않도록).</summary>
    public CrosshairStyle Clone()
    {
        CrosshairStyle copy = (CrosshairStyle)MemberwiseClone();
        copy.adsMain = adsMain != null ? adsMain.Clone() : new CrosshairMainStyle();
        copy.adsSub = adsSub != null ? adsSub.Clone() : new CrosshairSubStyle();
        return copy;
    }
}

/// <summary>
/// 외곽(Sub) 조준선 한 벌입니다. 조준(ADS) 중 외곽을 비조준과 따로 둘 때 씁니다.
/// </summary>
/// <remarks>필드 이름은 <see cref="CrosshairStyle"/>의 외곽 필드와 같습니다. JsonUtility 저장 키라 이름을 바꾸지 않습니다.</remarks>
[Serializable]
public sealed class CrosshairSubStyle
{
    public CrosshairController.SubShape subShape = CrosshairController.SubShape.SquareCross;
    public float centerSpacePixels = 5.0f;
    public float subSizePixels = 1.0f;
    public float subWidthPixels = 10.0f;
    public float subThicknessPixels = 1.0f;
    public float subRingSizePixels = 16.0f;
    public float subRingThicknessPixels = 1.0f;
    public Color subColor = Color.white;
    public float subStrokeThicknessPixels = 1.0f;
    public Color subStrokeColor = Color.black;
    public float cornerRadiusPixels = 2.0f;
    public AimController.CrosshairSpreadMode subSpreadMode = AimController.CrosshairSpreadMode.CurrentSpread;

    /// <summary>외곽 모양 값을 컨트롤러에 적용합니다. 벌어짐 방식과 강도는 조준 제어(AimController)가 씁니다.</summary>
    public void ApplyTo(CrosshairController crosshair)
    {
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

    public CrosshairSubStyle Clone()
    {
        return (CrosshairSubStyle)MemberwiseClone();
    }
}

/// <summary>
/// 내부(Main) 조준선 한 벌입니다. 동적 크로스헤어를 끄고 조준(ADS) 중 내부를 비조준과 따로 둘 때 씁니다.
/// </summary>
/// <remarks>필드 이름은 <see cref="CrosshairStyle"/>의 내부 필드와 같습니다. JsonUtility 저장 키라 이름을 바꾸지 않습니다.</remarks>
[Serializable]
public sealed class CrosshairMainStyle
{
    public CrosshairController.MainShape mainShape = CrosshairController.MainShape.Dot;
    public float mainSizePixels = 3.0f;
    public float mainRingSizePixels = 8.0f;
    public float mainRingThicknessPixels = 1.0f;
    public Color mainColor = Color.white;
    public float mainStrokeThicknessPixels = 1.0f;
    public Color mainStrokeColor = Color.black;

    /// <summary>내부 모양 값을 컨트롤러에 적용합니다.</summary>
    public void ApplyTo(CrosshairController crosshair)
    {
        crosshair.CurrentMainShape = mainShape;
        crosshair.MainSizePixels = mainSizePixels;
        crosshair.MainRingSizePixels = mainRingSizePixels;
        crosshair.MainRingThicknessPixels = mainRingThicknessPixels;
        crosshair.MainColor = mainColor;
        crosshair.MainStrokeThicknessPixels = mainStrokeThicknessPixels;
        crosshair.MainStrokeColor = mainStrokeColor;
    }

    public CrosshairMainStyle Clone()
    {
        return (CrosshairMainStyle)MemberwiseClone();
    }
}
