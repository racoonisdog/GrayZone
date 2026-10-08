using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>활성 상태인 청솔 스킬을 기준으로 비활성 용숨결 자식의 원뿔 판정 범위를 표시합니다.</summary>
/// <remarks>벽에 막히기 전의 최대 범위입니다. 실제 발사 때 벽에 잘린 원뿔과 착탄 범위는 <see cref="DragonBreathEffect"/>가 따로 그립니다.</remarks>
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
        DragonBreathEffect.GetPerpendicularAxes(direction, out Vector3 right, out Vector3 up);

        Color previousColor = Handles.color;
        CompareFunction previousZTest = Handles.zTest;

        Handles.color = RangeColor;
        Handles.zTest = CompareFunction.Always;
        Handles.DrawWireDisc(origin, direction, effect.DamageStartRadius);
        Handles.DrawWireDisc(end, direction, effect.DamageEndRadius);

        const int sideCount = 8;
        for (int i = 0; i < sideCount; i++)
        {
            float angle = Mathf.PI * 2.0f * i / sideCount;
            Vector3 radial = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
            Handles.DrawLine(origin + radial * effect.DamageStartRadius, end + radial * effect.DamageEndRadius);
        }

        Handles.zTest = previousZTest;
        Handles.color = previousColor;
    }
}
