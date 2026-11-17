using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// TMP 리치 텍스트의 &lt;font="이름"&gt; 태그가 찾을 수 있도록 폰트 에셋을 이름으로 등록합니다.
/// </summary>
/// <remarks>
/// TMP는 &lt;font&gt; 태그의 폰트를 등록된 목록에서 찾고, 없으면 Resources/Fonts &amp; Materials 폴더에서 찾습니다.
/// 프로젝트 폰트(3.Resources)는 Resources 폴더 밖에 있어 그대로는 찾지 못하므로, 이 컴포넌트가 켜질 때 목록에 올립니다.
/// 본문 일부만 Bold 폰트로 쓰는 튜토리얼 패널에 붙입니다(&lt;b&gt;는 글자를 두껍게 늘려 그려 폭이 Figma와 달라집니다).
/// 등록한 뒤 아래 텍스트를 다시 그리게 해서, 등록 전에 그려진 글자도 지정한 폰트로 바뀝니다.
/// </remarks>
[ExecuteAlways]
[DisallowMultipleComponent]
public class TmpFontTagRegistry : MonoBehaviour
{
    [Tooltip("<font=\"이름\"> 태그로 쓸 폰트 에셋입니다. 태그에는 에셋 이름을 그대로 적습니다(예: GmarketSansTTFBold SDF).")]
    [SerializeField] private List<TMP_FontAsset> m_fonts = new();

    private void OnEnable()
    {
        bool added = false;
        foreach (TMP_FontAsset font in m_fonts)
        {
            if (font == null || MaterialReferenceManager.TryGetFontAsset(font.hashCode, out _))
            {
                continue;
            }

            MaterialReferenceManager.AddFontAsset(font);
            added = true;
        }

        if (!added)
        {
            return;
        }

        foreach (TMP_Text text in GetComponentsInChildren<TMP_Text>(true))
        {
            text.SetAllDirty();
        }
    }
}
