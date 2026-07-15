#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace GrayZone.EditorTools
{
    /// <summary>
    /// Play Mode 디버그 전용 트레이너 창입니다.
    /// </summary>
    /// <remarks>
    /// VInspector의 <c>[Button]</c> 메서드가 이 환경에서 Inspector에 렌더링되지 않는 문제를 우회하기 위해,
    /// 같은 디버그 기능(즉시 기절/전투 이탈/살리기, 무한 체력/장탄수/탄창)을 일반 <see cref="EditorWindow"/> +
    /// <c>GUILayout</c>으로 별도 창에 모아 둡니다. 파일 전체가 <c>Assets/1.Scripts/Editor/Tool</c> 아래 있고
    /// <c>#if UNITY_EDITOR</c>로 감싸져 있어 Player 빌드에는 포함되지 않습니다.
    /// </remarks>
    public class DebugTrainerWindow : EditorWindow
    {
        // -1이면 "자동"(현재 조작 중인 멤버를 계속 따라감). 0 이상이면 수동으로 고정한 스쿼드 멤버 인덱스입니다.
        private int m_selectedMemberIndex = -1;

        [MenuItem("Tools/GrayZone/Debug/Debug Trainer")]
        private static void Open()
        {
            GetWindow<DebugTrainerWindow>("GZ Debug Trainer");
        }

        private void OnEnable()
        {
            EditorApplication.update += Repaint;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Repaint;
        }

        private void OnGUI()
        {
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Play Mode에서만 사용할 수 있습니다.", MessageType.Info);
                return;
            }

            SquadManager squadManager = FindFirstObjectByType<SquadManager>();
            if (squadManager == null)
            {
                EditorGUILayout.HelpBox("SquadManager를 찾을 수 없습니다.", MessageType.Warning);
                return;
            }

            var members = squadManager.SquadMembers;

            DrawTargetPicker(squadManager, members);
            EditorGUILayout.Space();

            PlayerbleUnitData playerData = ResolveTargetPlayerData(squadManager, members);
            if (playerData == null)
            {
                EditorGUILayout.HelpBox("대상의 PlayerbleUnitData를 찾을 수 없습니다.", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("대상: " + playerData.name, EditorStyles.boldLabel);
            EditorGUILayout.Space();

            DrawHealthSection(playerData);
            EditorGUILayout.Space();
            DrawWeaponSection(playerData);
        }

        /// <summary>
        /// 스쿼드 매니저가 관리하는 멤버 중 디버그 대상을 고르는 버튼 열입니다. "자동"을 고르면
        /// 항상 현재 조작 중인 멤버를 따라가고, 특정 멤버를 고르면(다운/AI 조작 포함) 그 멤버로 고정됩니다.
        /// </summary>
        private void DrawTargetPicker(SquadManager squadManager, System.Collections.Generic.IReadOnlyList<SquadMemberController> members)
        {
            EditorGUILayout.LabelField("대상 선택", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                bool isAuto = m_selectedMemberIndex < 0;
                if (GUILayout.Toggle(isAuto, "자동(조작 중)", "Button") && !isAuto)
                {
                    m_selectedMemberIndex = -1;
                }

                for (int i = 0; i < members.Count; i++)
                {
                    SquadMemberController member = members[i];
                    if (member == null)
                    {
                        continue;
                    }

                    bool isSelected = m_selectedMemberIndex == i;
                    string label = member.MemberName
                        + (member.IsPlayerSquadMember ? " (조작중)" : "")
                        + (!member.IsAlive ? " (사망)" : member.IsDown ? " (다운)" : "");

                    int capturedIndex = i;
                    if (GUILayout.Toggle(isSelected, label, "Button") && !isSelected)
                    {
                        m_selectedMemberIndex = capturedIndex;
                    }
                }
            }
        }

        /// <summary>
        /// "자동"이면 현재 조작 중인 멤버, 아니면 수동으로 고른 멤버의 PlayerbleUnitData를 반환합니다.
        /// </summary>
        private PlayerbleUnitData ResolveTargetPlayerData(SquadManager squadManager, System.Collections.Generic.IReadOnlyList<SquadMemberController> members)
        {
            if (m_selectedMemberIndex < 0 || m_selectedMemberIndex >= members.Count)
            {
                return squadManager.PlayerSquadMemberData;
            }

            SquadMemberController member = members[m_selectedMemberIndex];
            return member != null ? member.GetComponent<PlayerbleUnitData>() : null;
        }

        private static void DrawHealthSection(PlayerbleUnitData playerData)
        {
            EditorGUILayout.LabelField("체력", EditorStyles.boldLabel);

            PlayerHealth health = playerData.GetComponent<PlayerHealth>();
            if (health == null)
            {
                EditorGUILayout.HelpBox("PlayerHealth를 찾을 수 없습니다.", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField($"HP {health.CurrentHP} / {health.MaxHP}  (다운={health.IsDowned}, 사망={health.IsDead}, 부활 {health.ReviveCount}/{health.MaxReviveCount})");

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("즉시 기절시키기"))
                {
                    health.Debug_InstantDown();
                }

                if (GUILayout.Button("즉시 전투 이탈"))
                {
                    health.Debug_InstantCombatOut();
                }

                if (GUILayout.Button("즉시 살리기"))
                {
                    health.Debug_InstantRevive();
                }
            }

            // 저장/판정은 PlayerHealth가 소유하고, 접근은 playerData 단일 진입점을 거칩니다.
            playerData.DebugInfiniteHealth = EditorGUILayout.ToggleLeft("무한 체력", playerData.DebugInfiniteHealth);
        }

        private static void DrawWeaponSection(PlayerbleUnitData playerData)
        {
            EditorGUILayout.LabelField("무기 / 탄약", EditorStyles.boldLabel);

            WeaponController weapon = playerData.WeaponController;
            if (weapon == null)
            {
                EditorGUILayout.HelpBox("WeaponController를 찾을 수 없습니다.", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField($"장전 {weapon.CurrentBullet} / {weapon.MaxBullet}, 예비 {playerData.ReserveAmmo} / {playerData.MaxReserveAmmo}");

            // 저장/판정은 각각 WeaponController/PlayerbleUnitData가 소유하고, 접근은 playerData 단일 진입점을 거칩니다.
            playerData.DebugInfiniteMagazine = EditorGUILayout.ToggleLeft("무한 장탄수(현재 탄창)", playerData.DebugInfiniteMagazine);
            playerData.DebugInfiniteReserveAmmo = EditorGUILayout.ToggleLeft("무한 탄창(예비 탄약)", playerData.DebugInfiniteReserveAmmo);
        }
    }
}
#endif
