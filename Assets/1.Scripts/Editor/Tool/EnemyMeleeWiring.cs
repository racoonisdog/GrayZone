using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 변이체 프리팹의 근접 판정 오브젝트에 <see cref="Melee"/>를 붙이고 공격 모듈과 연결하는 에디터 도구입니다.
/// </summary>
/// <remarks>
/// 프리팹 YAML을 직접 고치지 않는 이유는 컴포넌트 추가가 GameObject와 컴포넌트 양쪽의
/// fileID를 새로 만들고 서로 참조를 걸어야 해서, 손으로 쓰면 어긋나기 쉽기 때문입니다.
/// </remarks>
public static class EnemyMeleeWiring
{
    private const string PrefabPath = "Assets/2.Prefabs/Enemy/Enemy(Test).prefab";
    private const string BalancePath = "Assets/5.Data/ScriptableObject/Enemy/ZombieAttackBalance.asset";
    private static readonly string[] HitboxNames = { "AttackPoint_L", "AttackPoint_R" };

    [MenuItem("GrayZone/Enemy/근접 판정 배선")]
    public static void Wire()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null)
        {
            Debug.LogError($"프리팹을 찾지 못했습니다: {PrefabPath}");
            return;
        }

        List<string> log = new List<string>();

        try
        {
            MeleeBalanceSO balance = AssetDatabase.LoadAssetAtPath<MeleeBalanceSO>(BalancePath);
            if (balance == null)
            {
                log.Add($"밸런스 SO를 찾지 못했습니다: {BalancePath}");
            }

            EnemyAttack attack = root.GetComponentInChildren<EnemyAttack>(true);
            if (attack == null)
            {
                Debug.LogError("EnemyAttack을 찾지 못했습니다.");
                return;
            }

            List<Melee> melees = new List<Melee>();

            foreach (string name in HitboxNames)
            {
                Transform point = FindDeep(root.transform, name);
                if (point == null)
                {
                    log.Add($"{name}을 찾지 못했습니다.");
                    continue;
                }

                Collider collider = point.GetComponent<Collider>();
                if (collider == null)
                {
                    log.Add($"{name}에 콜라이더가 없습니다. 건너뜁니다.");
                    continue;
                }

                // 트리거가 아니면 CharacterController인 플레이어와의 접촉을 받지 못합니다.
                if (!collider.isTrigger)
                {
                    collider.isTrigger = true;
                    log.Add($"{name} 콜라이더를 트리거로 바꿨습니다.");
                }

                // 공격 구간에서만 켜지므로 평소에는 꺼 둡니다.
                collider.enabled = false;

                Melee melee = point.GetComponent<Melee>();
                if (melee == null)
                {
                    melee = point.gameObject.AddComponent<Melee>();
                    log.Add($"{name}에 Melee를 추가했습니다.");
                }

                SerializedObject so = new SerializedObject(melee);
                so.FindProperty("m_balanceSO").objectReferenceValue = balance;
                so.FindProperty("m_attack").objectReferenceValue = attack;
                so.ApplyModifiedPropertiesWithoutUndo();

                melees.Add(melee);
            }

            SerializedObject attackSo = new SerializedObject(attack);
            SerializedProperty array = attackSo.FindProperty("m_melees");
            array.arraySize = melees.Count;
            for (int i = 0; i < melees.Count; i++)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue = melees[i];
            }

            SerializedProperty animatorProp = attackSo.FindProperty("m_animator");
            if (animatorProp.objectReferenceValue == null)
            {
                animatorProp.objectReferenceValue = attack.GetComponent<Animator>();
            }

            attackSo.ApplyModifiedPropertiesWithoutUndo();
            log.Add($"EnemyAttack에 근접 무기 {melees.Count}개를 연결했습니다.");

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.Refresh();
        Debug.Log("[EnemyMeleeWiring] 완료\n" + string.Join("\n", log));
    }

    /// <summary>이름이 같은 자손을 깊이 우선으로 찾습니다.</summary>
    private static Transform FindDeep(Transform parent, string name)
    {
        if (parent.name == name)
        {
            return parent;
        }

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindDeep(parent.GetChild(i), name);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
