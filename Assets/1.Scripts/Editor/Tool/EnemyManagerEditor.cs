using UnityEditor;
using UnityEngine;

/// <summary>
/// <see cref="EnemyManager"/>의 종류별 슬롯을 고정 목록으로 그립니다.
/// </summary>
/// <remarks>
/// 기본 배열 UI를 그대로 쓰면 슬롯을 추가·삭제할 수 있고, 그러면 같은 종류가 둘이거나 종류가 빠진
/// 상태를 만들 수 있습니다. 슬롯 구성은 <see cref="EnemyType"/>이 정하는 것이므로 손댈 수 없어야 합니다.
/// 그래서 크기 필드와 추가·삭제 버튼을 그리지 않고, 각 슬롯을 종류 이름 아래에 펼쳐 놓습니다.
///
/// 슬롯 배열 자체의 동기화는 이 에디터가 아니라 컴포넌트의 OnValidate가 담당합니다. 에디터는 표시만
/// 맡습니다. 여기서 배열을 고치면 Inspector를 열지 않은 오브젝트는 동기화되지 않기 때문입니다.
/// </remarks>
[CustomEditor(typeof(EnemyManager))]
public sealed class EnemyManagerEditor : Editor
{
    /// <summary>슬롯이 담당하는 종류를 담은 필드 이름입니다. 이 필드는 그리지 않습니다.</summary>
    private const string EnemyTypeFieldName = "m_enemyType";

    /// <summary>종류별 슬롯 배열입니다.</summary>
    private SerializedProperty m_perTypeSettings;

    private void OnEnable()
    {
        m_perTypeSettings = serializedObject.FindProperty("m_perTypeSettings");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        if (m_perTypeSettings == null)
        {
            EditorGUILayout.HelpBox(
                "종류별 시체 설정 배열을 찾지 못했습니다. 필드 이름이 바뀌었는지 확인하십시오.",
                MessageType.Error);
            return;
        }

        EditorGUILayout.LabelField("Enemy Corpse Options", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "슬롯은 EnemyType 멤버와 1:1로 고정됩니다. 종류를 추가하려면 EnemyType에 멤버를 추가하십시오.\n" +
            "Unknown 슬롯은 종류를 지정하지 않은 개체가 사용합니다.",
            MessageType.None);

        for (int i = 0; i < m_perTypeSettings.arraySize; i++)
        {
            DrawEntry(m_perTypeSettings.GetArrayElementAtIndex(i));
        }

        serializedObject.ApplyModifiedProperties();
    }

    /// <summary>슬롯 하나를 종류 이름 헤더와 그 아래 설정 필드로 그립니다.</summary>
    private static void DrawEntry(SerializedProperty entry)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField(GetEntryLabel(entry), EditorStyles.boldLabel);

        EditorGUI.indentLevel++;

        // 슬롯 하위 필드를 이름으로 나열하지 않고 순회합니다. Entry에 필드를 추가해도 이 에디터를
        // 함께 고치지 않아도 되고, 이름을 두 곳에 적어 두면 한쪽만 바뀌어 조용히 어긋납니다.
        SerializedProperty iterator = entry.Copy();
        SerializedProperty end = entry.GetEndProperty();
        bool enterChildren = true;

        while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
        {
            enterChildren = false;

            if (iterator.name == EnemyTypeFieldName)
            {
                continue;
            }

            EditorGUILayout.PropertyField(iterator, true);
        }

        EditorGUI.indentLevel--;
    }

    /// <summary>슬롯 헤더에 쓸 종류 이름입니다. 종류를 읽지 못하면 배열 표시로 물러섭니다.</summary>
    private static string GetEntryLabel(SerializedProperty entry)
    {
        SerializedProperty type = entry.FindPropertyRelative(EnemyTypeFieldName);
        if (type == null || type.enumDisplayNames == null)
        {
            return entry.displayName;
        }

        int index = type.enumValueIndex;
        if (index < 0 || index >= type.enumDisplayNames.Length)
        {
            return entry.displayName;
        }

        return type.enumDisplayNames[index];
    }
}
