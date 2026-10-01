using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>활성 상태인 청솔 스킬을 기준으로 비활성 용숨결 자식의 판정 범위를 표시합니다.</summary>
internal static class DragonBreathEffectGizmoDrawer
{
    private static readonly Color RangeColor = new Color(1.0f, 0.35f, 0.02f, 0.9f);

    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
    private static void DrawLoadedRange(ChungSolDragonBreathSkill skill, GizmoType gizmoType)
    {
        if (skill == null || skill.SpecialRoundsRemaining <= 0)
        {
            return;
        }

        DragonBreathEffect effect = skill.GetComponentInChildren<DragonBreathEffect>(true);
        if (effect == null || !effect.DrawDebugRange)
        {
            return;
        }

        Vector3 direction = effect.transform.forward;
        if (direction.sqrMagnitude <= Mathf.Epsilon)
        {
            return;
        }

        direction.Normalize();
        Vector3 origin = effect.transform.position;
        Vector3 end = origin + direction * effect.DamageRange;

        Vector3 right = Vector3.Cross(direction, Vector3.up);
        if (right.sqrMagnitude < 0.0001f)
        {
            right = Vector3.Cross(direction, Vector3.forward);
        }

        right.Normalize();
        Vector3 up = Vector3.Cross(right, direction).normalized;
        Color previousColor = Handles.color;
        CompareFunction previousZTest = Handles.zTest;

        Handles.color = RangeColor;
        Handles.zTest = CompareFunction.Always;
        Handles.DrawWireDisc(origin, direction, effect.DamageRadius);
        Handles.DrawWireDisc(end, direction, effect.DamageRadius);

        const int sideCount = 8;
        for (int i = 0; i < sideCount; i++)
        {
            float angle = Mathf.PI * 2.0f * i / sideCount;
            Vector3 offset = (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * effect.DamageRadius;
            Handles.DrawLine(origin + offset, end + offset);
        }

        Handles.zTest = previousZTest;
        Handles.color = previousColor;
    }
}
