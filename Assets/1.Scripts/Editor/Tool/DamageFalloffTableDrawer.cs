using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 거리 감쇠 구간표를 트랙과 마커로 편집하는 인스펙터 드로어입니다.
/// </summary>
/// <remarks>
/// <para>
/// 애니메이션 이벤트처럼 가로 트랙 위에 마커를 놓고, 선택한 마커의 값을 아래에서 고칩니다.
/// 목록으로 두면 거리 순서가 눈에 보이지 않아 뒤섞여도 알아채기 어렵습니다.
/// 트랙 위에서는 순서가 위치로 드러나고, 마커를 놓을 때마다 정렬하므로 순서가 어긋난 상태로 저장되지 않습니다.
/// </para>
/// <para>
/// 트랙 범위를 비워 두면 같은 오브젝트의 <c>m_hitscanRange</c>를 씁니다. 사거리와 어긋나면 트랙에 보이는
/// 위치가 실제와 달라져 판단을 그르치므로, 좁은 구간을 확대해 볼 때만 직접 지정합니다.
/// </para>
/// </remarks>
[CustomPropertyDrawer(typeof(DamageFalloffTable))]
public sealed class DamageFalloffTableDrawer : PropertyDrawer
{
    private const float TrackHeight = 26.0f;
    private const float RulerHeight = 14.0f;
    private const float MarkerWidth = 11.0f;
    private const float Padding = 4.0f;

    /// <summary>마커를 잡았다고 판정할 가로 거리(픽셀)입니다.</summary>
    private const float GrabThreshold = 10.0f;

    /// <summary>사거리를 읽지 못했을 때 쓸 트랙 최대 거리(m)입니다.</summary>
    private const float FallbackMaxDistance = 100.0f;

    /// <summary>
    /// 프로퍼티 경로별 선택 인덱스입니다.
    /// </summary>
    /// <remarks>드로어 인스턴스는 여러 프로퍼티에 재사용되므로 필드 하나에 담으면 선택이 섞입니다.</remarks>
    private static readonly Dictionary<string, int> s_selected = new Dictionary<string, int>();

    private static int GetSelected(string key, int count)
    {
        if (!s_selected.TryGetValue(key, out int index) || index >= count)
        {
            return -1;
        }

        return index;
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float line = EditorGUIUtility.singleLineHeight;
        float spacing = EditorGUIUtility.standardVerticalSpacing;
        float row = line + spacing;

        // 제목 + 모드 + 트랙범위 + 트랙 + 눈금 + 버튼줄
        float height = row * 3.0f + TrackHeight + RulerHeight + spacing + row;

        SerializedProperty steps = property.FindPropertyRelative("m_steps");
        bool hasSelection = steps != null && GetSelected(property.propertyPath, steps.arraySize) >= 0;

        // 선택 시 거리·값·결과 3줄, 아니면 안내 1줄
        height += hasSelection ? row * 3.0f : row;
        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        SerializedProperty steps = property.FindPropertyRelative("m_steps");
        SerializedProperty mode = property.FindPropertyRelative("m_mode");
        SerializedProperty trackMin = property.FindPropertyRelative("m_trackMinDistance");
        SerializedProperty trackMax = property.FindPropertyRelative("m_trackMaxDistance");

        if (steps == null || mode == null)
        {
            EditorGUI.LabelField(position, label.text, "구간표 필드를 찾지 못했습니다.");
            return;
        }

        float line = EditorGUIUtility.singleLineHeight;
        float spacing = EditorGUIUtility.standardVerticalSpacing;
        float step = line + spacing;

        float weaponRange = ResolveWeaponRange(property);
        float minDistance = Mathf.Max(0.0f, trackMin.floatValue);
        float maxDistance = trackMax.floatValue > 0.0f
            ? Mathf.Max(minDistance + 1.0f, trackMax.floatValue)
            : weaponRange;

        Rect row = new Rect(position.x, position.y, position.width, line);
        EditorGUI.LabelField(row, label, EditorStyles.boldLabel);

        // ── 값 표기 방식
        row.y += step;
        Rect modeLabel = new Rect(row.x, row.y, EditorGUIUtility.labelWidth, line);
        Rect modeField = new Rect(row.x + EditorGUIUtility.labelWidth, row.y,
            row.width - EditorGUIUtility.labelWidth, line);
        EditorGUI.LabelField(modeLabel, "값 표기");
        mode.enumValueIndex = GUI.Toolbar(modeField, mode.enumValueIndex, new[] { "배율", "고정 피해" });
        bool flatMode = mode.enumValueIndex == (int)DamageFalloffMode.FlatDamage;

        // ── 트랙 범위
        row.y += step;
        DrawTrackRange(row, trackMin, trackMax, weaponRange, line);

        // ── 트랙과 눈금
        row.y += step;
        Rect track = new Rect(row.x, row.y, row.width, TrackHeight);
        Rect ruler = new Rect(row.x, row.y + TrackHeight, row.width, RulerHeight);

        int selected = GetSelected(property.propertyPath, steps.arraySize);
        selected = DrawTrack(track, property.propertyPath, steps, minDistance, maxDistance, selected);
        DrawRuler(ruler, minDistance, maxDistance);

        row.y += TrackHeight + RulerHeight + spacing;

        // ── 추가·삭제
        Rect addRect = new Rect(row.x, row.y, 90.0f, line);
        Rect removeRect = new Rect(row.x + 94.0f, row.y, 90.0f, line);
        Rect hintRect = new Rect(row.x + 190.0f, row.y, Mathf.Max(0.0f, row.width - 190.0f), line);

        if (GUI.Button(addRect, "구간 추가"))
        {
            AddStep(steps, minDistance, maxDistance, flatMode, ResolveBaseDamage(property));
            SortSteps(steps);
            selected = FindIndexByDistance(steps, GetDistance(steps, steps.arraySize - 1));
            s_selected[property.propertyPath] = selected;
        }

        using (new EditorGUI.DisabledScope(selected < 0))
        {
            if (GUI.Button(removeRect, "선택 삭제") && selected >= 0)
            {
                steps.DeleteArrayElementAtIndex(selected);
                s_selected.Remove(property.propertyPath);
                selected = -1;
            }
        }

        if (steps.arraySize == 0)
        {
            EditorGUI.LabelField(hintRect, "구간 없음 = 감쇠 없음 (거리 무관 기본 피해)", EditorStyles.miniLabel);
        }

        row.y += step;

        if (selected < 0 || selected >= steps.arraySize)
        {
            EditorGUI.LabelField(row, "트랙에서 마커를 선택하면 값을 고칠 수 있습니다.", EditorStyles.miniLabel);
            return;
        }

        DrawSelectedStep(row, property, steps, selected, maxDistance, flatMode, line, spacing);
    }

    /// <summary>트랙에 보일 최소·최대 거리를 편집합니다.</summary>
    private static void DrawTrackRange(
        Rect row,
        SerializedProperty trackMin,
        SerializedProperty trackMax,
        float weaponRange,
        float line)
    {
        Rect labelRect = new Rect(row.x, row.y, EditorGUIUtility.labelWidth, line);
        EditorGUI.LabelField(labelRect, "트랙 범위(m)");

        float fieldsX = row.x + EditorGUIUtility.labelWidth;
        float fieldsWidth = row.width - EditorGUIUtility.labelWidth;
        float half = (fieldsWidth - 58.0f) * 0.5f;

        Rect minRect = new Rect(fieldsX, row.y, half, line);
        Rect tildeRect = new Rect(fieldsX + half, row.y, 14.0f, line);
        Rect maxRect = new Rect(fieldsX + half + 14.0f, row.y, half, line);
        Rect autoRect = new Rect(fieldsX + half * 2.0f + 16.0f, row.y, 42.0f, line);

        trackMin.floatValue = Mathf.Max(0.0f, EditorGUI.FloatField(minRect, trackMin.floatValue));
        EditorGUI.LabelField(tildeRect, "~");

        if (trackMax.floatValue > 0.0f)
        {
            trackMax.floatValue = EditorGUI.FloatField(maxRect, trackMax.floatValue);
            if (GUI.Button(autoRect, "자동", EditorStyles.miniButton))
            {
                trackMax.floatValue = 0.0f;
            }
        }
        else
        {
            // 0 이하이면 무기 사거리를 쓴다는 뜻입니다. 실제로 쓰이는 값을 보여 주어야 오해가 없습니다.
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUI.TextField(maxRect, $"{weaponRange:0} (사거리)");
            }

            if (GUI.Button(autoRect, "지정", EditorStyles.miniButton))
            {
                trackMax.floatValue = weaponRange;
            }
        }
    }

    /// <summary>트랙과 마커를 그리고 마우스 조작을 처리합니다.</summary>
    private static int DrawTrack(
        Rect track,
        string key,
        SerializedProperty steps,
        float minDistance,
        float maxDistance,
        int selected)
    {
        EditorGUI.DrawRect(track, new Color(0.16f, 0.16f, 0.16f));
        EditorGUI.DrawRect(new Rect(track.x, track.center.y - 1.0f, track.width, 2.0f), new Color(0.32f, 0.32f, 0.32f));

        Rect inner = new Rect(track.x + Padding, track.y, track.width - Padding * 2.0f, track.height);

        Event current = Event.current;
        int controlId = GUIUtility.GetControlID(FocusType.Passive, track);

        // 선택된 마커를 마지막에 그려 겹쳐도 위로 오게 합니다.
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < steps.arraySize; i++)
            {
                bool isSelected = i == selected;
                if ((pass == 0) == isSelected)
                {
                    continue;
                }

                float distance = GetDistance(steps, i);

                // 트랙 범위 밖의 마커는 가장자리에 눌러 표시합니다. 안 그리면 사라진 것처럼 보입니다.
                bool outside = distance < minDistance || distance > maxDistance;
                float x = DistanceToX(inner, distance, minDistance, maxDistance);
                Rect marker = new Rect(x - MarkerWidth * 0.5f, track.y + 3.0f, MarkerWidth, track.height - 6.0f);

                Color color = isSelected ? new Color(1.0f, 0.72f, 0.2f) : new Color(0.55f, 0.62f, 0.72f);
                if (outside)
                {
                    color.a = 0.35f;
                }

                EditorGUI.DrawRect(marker, color);
                EditorGUI.DrawRect(new Rect(x - 0.5f, track.y, 1.0f, track.height),
                    isSelected ? new Color(1.0f, 0.85f, 0.4f) : new Color(0.7f, 0.75f, 0.85f));
            }
        }

        switch (current.type)
        {
            case EventType.MouseDown when track.Contains(current.mousePosition) && current.button == 0:
            {
                int hit = FindNearestMarker(inner, steps, minDistance, maxDistance, current.mousePosition.x);
                s_selected[key] = hit;
                selected = hit;

                if (hit >= 0)
                {
                    GUIUtility.hotControl = controlId;
                }

                GUI.changed = true;
                current.Use();
                break;
            }

            case EventType.MouseDrag when GUIUtility.hotControl == controlId && selected >= 0:
            {
                float distance = XToDistance(inner, current.mousePosition.x, minDistance, maxDistance);
                SetDistance(steps, selected, Mathf.Round(distance * 10.0f) / 10.0f);
                GUI.changed = true;
                current.Use();
                break;
            }

            case EventType.MouseUp when GUIUtility.hotControl == controlId:
            {
                // 놓는 순간 정렬합니다. 조회가 거리 오름차순을 전제하므로 어긋난 채로 저장되면 안 됩니다.
                float kept = selected >= 0 ? GetDistance(steps, selected) : -1.0f;
                SortSteps(steps);
                if (kept >= 0.0f)
                {
                    s_selected[key] = FindIndexByDistance(steps, kept);
                    selected = s_selected[key];
                }

                GUIUtility.hotControl = 0;
                current.Use();
                break;
            }
        }

        return selected;
    }

    /// <summary>거리 눈금을 그립니다.</summary>
    private static void DrawRuler(Rect ruler, float minDistance, float maxDistance)
    {
        Rect inner = new Rect(ruler.x + Padding, ruler.y, ruler.width - Padding * 2.0f, ruler.height);
        GUIStyle style = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter };

        const int Divisions = 5;
        for (int i = 0; i <= Divisions; i++)
        {
            float ratio = i / (float)Divisions;
            float x = inner.x + inner.width * ratio;
            float distance = Mathf.Lerp(minDistance, maxDistance, ratio);
            EditorGUI.LabelField(new Rect(x - 24.0f, inner.y, 48.0f, inner.height), $"{distance:0}m", style);
        }
    }

    /// <summary>선택한 마커의 거리·값과 계산 결과를 그립니다.</summary>
    private static void DrawSelectedStep(
        Rect row,
        SerializedProperty property,
        SerializedProperty steps,
        int selected,
        float maxDistance,
        bool flatMode,
        float line,
        float spacing)
    {
        SerializedProperty step = steps.GetArrayElementAtIndex(selected);
        SerializedProperty distance = step.FindPropertyRelative("m_maxDistance");
        SerializedProperty multiplier = step.FindPropertyRelative("m_damageMultiplier");
        SerializedProperty flatDamage = step.FindPropertyRelative("m_flatDamage");

        float from = selected > 0 ? GetDistance(steps, selected - 1) : 0.0f;

        EditorGUI.BeginChangeCheck();
        float newDistance = EditorGUI.Slider(row, "구간 끝 거리(m)", distance.floatValue, 0.0f, maxDistance);
        if (EditorGUI.EndChangeCheck())
        {
            distance.floatValue = newDistance;
            SortSteps(steps);
            s_selected[property.propertyPath] = FindIndexByDistance(steps, newDistance);
        }

        row.y += line + spacing;

        int baseDamage = ResolveBaseDamage(property);
        int resultDamage;

        if (flatMode)
        {
            flatDamage.intValue = Mathf.Max(0, EditorGUI.IntField(row, "구간 피해", flatDamage.intValue));
            resultDamage = Mathf.Max(1, flatDamage.intValue);
        }
        else
        {
            multiplier.floatValue = EditorGUI.Slider(row, "피해 배율", multiplier.floatValue, 0.0f, 1.0f);
            resultDamage = Mathf.Max(1, Mathf.RoundToInt(baseDamage * Mathf.Clamp01(multiplier.floatValue)));
        }

        row.y += line + spacing;

        // 결과를 함께 보여 줍니다. 값만 보면 몇 발에 죽는지 매번 계산해야 합니다.
        string result = flatMode
            ? $"{from:0}m ~ {distance.floatValue:0}m  →  피해 {resultDamage}  (기본 피해와 무관)"
            : $"{from:0}m ~ {distance.floatValue:0}m  →  피해 {resultDamage}  (기본 {baseDamage})";

        EditorGUI.LabelField(row, " ", result, EditorStyles.miniLabel);
    }

    // ─────────────────────────────────────────────────────────────
    // 헬퍼
    // ─────────────────────────────────────────────────────────────

    /// <summary>같은 오브젝트의 사거리를 읽습니다.</summary>
    private static float ResolveWeaponRange(SerializedProperty property)
    {
        SerializedProperty range = property.serializedObject.FindProperty("m_hitscanRange");
        float value = range != null ? range.floatValue : FallbackMaxDistance;
        return value > 0.0f ? value : FallbackMaxDistance;
    }

    /// <summary>같은 오브젝트의 기본 피해를 읽습니다.</summary>
    private static int ResolveBaseDamage(SerializedProperty property)
    {
        SerializedProperty damage = property.serializedObject.FindProperty("m_hitscanDamage");
        return damage != null ? damage.intValue : 1;
    }

    private static float GetDistance(SerializedProperty steps, int index)
    {
        return steps.GetArrayElementAtIndex(index).FindPropertyRelative("m_maxDistance").floatValue;
    }

    private static void SetDistance(SerializedProperty steps, int index, float value)
    {
        steps.GetArrayElementAtIndex(index).FindPropertyRelative("m_maxDistance").floatValue = Mathf.Max(0.0f, value);
    }

    private static float DistanceToX(Rect inner, float distance, float minDistance, float maxDistance)
    {
        float ratio = Mathf.Clamp01(Mathf.InverseLerp(minDistance, maxDistance, distance));
        return inner.x + inner.width * ratio;
    }

    private static float XToDistance(Rect inner, float x, float minDistance, float maxDistance)
    {
        float ratio = Mathf.Clamp01((x - inner.x) / Mathf.Max(1.0f, inner.width));
        return Mathf.Lerp(minDistance, maxDistance, ratio);
    }

    /// <summary>잡을 수 있는 거리 안에서 가장 가까운 마커를 찾습니다. 없으면 -1입니다.</summary>
    private static int FindNearestMarker(
        Rect inner,
        SerializedProperty steps,
        float minDistance,
        float maxDistance,
        float mouseX)
    {
        int best = -1;
        float bestDelta = GrabThreshold;

        for (int i = 0; i < steps.arraySize; i++)
        {
            float x = DistanceToX(inner, GetDistance(steps, i), minDistance, maxDistance);
            float delta = Mathf.Abs(x - mouseX);
            if (delta <= bestDelta)
            {
                bestDelta = delta;
                best = i;
            }
        }

        return best;
    }

    private static int FindIndexByDistance(SerializedProperty steps, float distance)
    {
        for (int i = 0; i < steps.arraySize; i++)
        {
            if (Mathf.Approximately(GetDistance(steps, i), distance))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>새 구간을 마지막 구간 뒤에 놓습니다.</summary>
    private static void AddStep(
        SerializedProperty steps,
        float minDistance,
        float maxDistance,
        bool flatMode,
        int baseDamage)
    {
        float last = steps.arraySize > 0 ? GetDistance(steps, steps.arraySize - 1) : minDistance;
        float span = Mathf.Max(1.0f, maxDistance - minDistance);
        float distance = steps.arraySize == 0
            ? minDistance + span * 0.3f
            : Mathf.Min(maxDistance, last + span * 0.25f);

        // 새 구간은 앞 구간보다 한 단계 약하게 시작합니다. 같은 값이면 추가한 티가 나지 않습니다.
        float previousMultiplier = 1.0f;
        int previousFlat = baseDamage;
        if (steps.arraySize > 0)
        {
            SerializedProperty previous = steps.GetArrayElementAtIndex(steps.arraySize - 1);
            previousMultiplier = previous.FindPropertyRelative("m_damageMultiplier").floatValue;
            previousFlat = previous.FindPropertyRelative("m_flatDamage").intValue;
        }

        steps.InsertArrayElementAtIndex(steps.arraySize);
        SerializedProperty step = steps.GetArrayElementAtIndex(steps.arraySize - 1);
        step.FindPropertyRelative("m_maxDistance").floatValue = distance;

        // 쓰지 않는 쪽도 함께 채웁니다. 모드를 바꿨을 때 0으로 비어 있으면 값을 다시 다 적어야 합니다.
        float nextMultiplier = Mathf.Max(0.0f, previousMultiplier - 0.15f);
        step.FindPropertyRelative("m_damageMultiplier").floatValue = nextMultiplier;
        step.FindPropertyRelative("m_flatDamage").intValue = flatMode
            ? Mathf.Max(1, Mathf.RoundToInt(previousFlat * 0.85f))
            : Mathf.Max(1, Mathf.RoundToInt(baseDamage * nextMultiplier));
    }

    /// <summary>구간을 거리 오름차순으로 정렬합니다.</summary>
    private static void SortSteps(SerializedProperty steps)
    {
        // SerializedProperty에는 정렬이 없어 인접 교환으로 처리합니다. 구간 수가 적어 비용이 문제되지 않습니다.
        for (int i = 1; i < steps.arraySize; i++)
        {
            for (int j = i; j > 0 && GetDistance(steps, j) < GetDistance(steps, j - 1); j--)
            {
                steps.MoveArrayElement(j, j - 1);
            }
        }
    }
}
