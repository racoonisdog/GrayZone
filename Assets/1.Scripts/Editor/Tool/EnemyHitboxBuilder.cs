using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Humanoid 릭을 가진 적 프리팹에 부위별 피격 히트박스(<see cref="Hitbox"/>)를 구성합니다.
/// </summary>
/// <remarks>
/// 총은 2단계로 판정합니다. 먼저 <c>EnemyHitDetect</c> 볼륨을 찾아 그 적의 부위 히트박스를 켜고, 다음 레이에서 그 히트박스를 맞힙니다.
/// 그래서 부위 히트박스가 하나도 없는 적은 어떤 총에도 피해를 받지 않고, 방어전 지정사수도 목표로 잡지 못합니다.
///
/// <para>
/// 부위 구성은 <c>Assets/2.Prefabs/Enemy/Howler.prefab</c>(수작업 배치 15개)을 따릅니다. 모델마다 뼈 이름과 체격이 달라서
/// 크기를 복사하지 않고, Humanoid 뼈 매핑으로 뼈를 찾은 뒤 뼈 사이 길이로 크기를 계산합니다. 굵기 비율은 Howler.prefab 값에서 가져왔습니다.
/// </para>
///
/// <para>
/// 다시 실행하면 이 도구가 만든 <c>COL_*Hitbox</c> 오브젝트를 지우고 새로 만듭니다. 손으로 추가한 다른 이름의 히트박스는 건드리지 않습니다.
/// </para>
/// </remarks>
public static class EnemyHitboxBuilder
{
    private const string HitboxPrefix = "COL_";
    private const string HitboxSuffix = "Hitbox";
    private const string HitboxLayerName = "EnemyHitbox";

    // Howler.prefab 기준 굵기 비율(반지름 / 뼈 길이)입니다.
    private const float UpperArmRadiusRatio = 0.2f;
    private const float LowerArmRadiusRatio = 0.2f;
    private const float UpperLegRadiusRatio = 0.2f;
    private const float LowerLegRadiusRatio = 0.15f;

    private static readonly string[] DefenseEnemyPrefabPaths =
    {
        "Assets/2.Prefabs/Enemy/Defense/Enemy/Scratcher(Defense_Player).prefab",
        "Assets/2.Prefabs/Enemy/Defense/Enemy/Stalker(Defense_Player).prefab",
        "Assets/2.Prefabs/Enemy/Defense/Enemy/Bloater(Defense_Player).prefab",
        "Assets/2.Prefabs/Enemy/Defense/Enemy/Crusher(Defense_Run_Player).prefab",
        "Assets/2.Prefabs/Enemy/Defense/Enemy/Howler(Defense_Player).prefab",
    };

    /// <summary>방어전 적 프리팹 5종의 부위 히트박스를 각 Humanoid 골격 기준으로 다시 구성합니다.</summary>
    [MenuItem("GrayZone/Enemy/방어전 적 히트박스 구성")]
    public static void RebuildDefenseEnemyPrefabs()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Play Mode를 종료한 뒤 히트박스를 구성하십시오.");
            return;
        }

        foreach (string prefabPath in DefenseEnemyPrefabPaths)
        {
            RebuildPrefab(prefabPath);
        }
    }

    /// <summary>지정한 프리팹 하나의 부위 히트박스를 다시 만들고 저장합니다.</summary>
    /// <returns>구성과 저장에 성공하면 true입니다.</returns>
    public static bool RebuildPrefab(string prefabPath)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null)
        {
            Debug.LogError($"[적 히트박스] 프리팹을 열지 못했습니다: {prefabPath}");
            return false;
        }

        List<string> log = new List<string>();
        try
        {
            if (!Build(root, log))
            {
                Debug.LogError($"[적 히트박스] 구성 실패: {prefabPath}\n" + string.Join("\n", log));
                return false;
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log($"[적 히트박스] {prefabPath}\n" + string.Join("\n", log));
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>열린 프리팹 루트에 부위 히트박스를 구성합니다. 저장은 하지 않습니다.</summary>
    /// <param name="root">프리팹 루트입니다.</param>
    /// <param name="log">진행 내역을 쌓을 목록입니다.</param>
    /// <returns>필요한 뼈를 모두 찾아 구성했으면 true입니다.</returns>
    public static bool Build(GameObject root, List<string> log)
    {
        int layer = LayerMask.NameToLayer(HitboxLayerName);
        if (layer < 0)
        {
            log.Add($"'{HitboxLayerName}' 레이어가 없습니다.");
            return false;
        }

        Animator animator = FindHumanoidAnimator(root);
        if (animator == null)
        {
            log.Add("Humanoid Avatar를 가진 Animator를 찾지 못했습니다.");
            return false;
        }

        if (root.GetComponentInChildren<HitboxGroup>(true) == null)
        {
            log.Add("HitboxGroup이 없습니다. 히트박스를 만들어도 사격 판정에서 켜지지 않으므로 중단합니다.");
            return false;
        }

        Dictionary<HumanBodyBones, Transform> bones = CollectBones(animator, log);
        if (bones == null)
        {
            return false;
        }

        int removed = RemoveGeneratedHitboxes(root);
        log.Add($"기존 생성 히트박스 {removed}개 제거, 아바타 {animator.avatar.name}");

        BuildHead(bones, layer, log);
        BuildTorso(bones, layer, log);
        BuildPelvis(bones, layer, log);

        BuildLimb(bones, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, UpperArmRadiusRatio, "UpperarmHitbox_L", layer, log);
        BuildLimb(bones, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, UpperArmRadiusRatio, "UpperarmHitbox_R", layer, log);
        BuildLimb(bones, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, LowerArmRadiusRatio, "ForearmHitbox_L", layer, log);
        BuildLimb(bones, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, LowerArmRadiusRatio, "ForearmHitbox_R", layer, log);
        BuildLimb(bones, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, UpperLegRadiusRatio, "ThighHitbox_L", layer, log);
        BuildLimb(bones, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, UpperLegRadiusRatio, "ThighHitbox_R", layer, log);
        BuildLimb(bones, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, LowerLegRadiusRatio, "CalfHitbox_L", layer, log);
        BuildLimb(bones, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, LowerLegRadiusRatio, "CalfHitbox_R", layer, log);

        BuildHand(bones, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, "HandHitbox_L", layer, log);
        BuildHand(bones, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, "HandHitbox_R", layer, log);
        BuildFoot(bones, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes, "FootHitbox_L", layer, log);
        BuildFoot(bones, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes, "FootHitbox_R", layer, log);

        return true;
    }

    private static Animator FindHumanoidAnimator(GameObject root)
    {
        Animator[] animators = root.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            Animator animator = animators[i];
            if (animator.avatar != null
                && animator.avatar.isHuman
                && animator.GetBoneTransform(HumanBodyBones.Hips) != null
                && animator.GetBoneTransform(HumanBodyBones.Head) != null)
            {
                return animator;
            }
        }

        return null;
    }

    private static Dictionary<HumanBodyBones, Transform> CollectBones(Animator animator, List<string> log)
    {
        HumanBodyBones[] required =
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,
        };

        Dictionary<HumanBodyBones, Transform> bones = new Dictionary<HumanBodyBones, Transform>();
        List<string> missing = new List<string>();
        foreach (HumanBodyBones bone in required)
        {
            Transform transform = animator.GetBoneTransform(bone);
            if (transform == null)
            {
                missing.Add(bone.ToString());
            }
            else
            {
                bones[bone] = transform;
            }
        }

        if (missing.Count > 0)
        {
            log.Add("히트박스 구성에 필요한 Humanoid 뼈가 없습니다: " + string.Join(", ", missing));
            return null;
        }

        // 선택 뼈입니다. 없으면 가까운 뼈로 대신합니다.
        foreach (HumanBodyBones optional in new[] { HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.LeftToes, HumanBodyBones.RightToes })
        {
            Transform transform = animator.GetBoneTransform(optional);
            if (transform != null)
            {
                bones[optional] = transform;
            }
        }

        return bones;
    }

    /// <summary>이 도구가 만든 히트박스 오브젝트(COL_*Hitbox*, Hitbox 컴포넌트, 자식 없음)를 모두 지웁니다.</summary>
    private static int RemoveGeneratedHitboxes(GameObject root)
    {
        List<GameObject> targets = new List<GameObject>();
        foreach (Hitbox hitbox in root.GetComponentsInChildren<Hitbox>(true))
        {
            string name = hitbox.gameObject.name;
            // 좌우 부위는 COL_ThighHitbox_L처럼 접미사 뒤에 _L/_R이 붙으므로 끝 문자열이 아니라 포함 여부로 봅니다.
            if (name.StartsWith(HitboxPrefix) && name.Contains(HitboxSuffix) && hitbox.transform.childCount == 0)
            {
                targets.Add(hitbox.gameObject);
            }
        }

        foreach (GameObject target in targets)
        {
            Object.DestroyImmediate(target);
        }

        return targets.Count;
    }

    /// <summary>
    /// 머리 히트박스를 만듭니다. 약점(헤드샷) 부위입니다.
    /// </summary>
    /// <remarks>
    /// 반지름은 목(없으면 흉부)부터 머리 뼈까지 거리의 비율로 잡습니다. 머리 뼈 원점은 보통 목 쪽 끝이라,
    /// 구를 목 반대편으로 밀어 머리 중심에 놓습니다.
    /// </remarks>
    private static void BuildHead(Dictionary<HumanBodyBones, Transform> bones, int layer, List<string> log)
    {
        Transform head = bones[HumanBodyBones.Head];
        Transform reference = bones.TryGetValue(HumanBodyBones.Neck, out Transform neck)
            ? neck
            : bones.TryGetValue(HumanBodyBones.Chest, out Transform chest) ? chest : bones[HumanBodyBones.Spine];

        float neckLength = Vector3.Distance(head.position, reference.position);
        float spineLength = Vector3.Distance(head.position, bones[HumanBodyBones.Hips].position);
        // 목 뼈가 잘게 나뉜 릭은 목 거리만으로 머리가 지나치게 작아지므로, 척추 전체 길이 비율을 하한으로 둡니다.
        float radius = Mathf.Max(neckLength * 0.9f, spineLength * 0.13f, 0.06f);

        GameObject go = CreateHitboxObject(head, "HeadHitbox", layer);
        Vector3 up = (head.position - reference.position).normalized;
        go.transform.position = head.position + up * radius * 0.6f;

        SphereCollider collider = go.AddComponent<SphereCollider>();
        collider.radius = radius / Mathf.Max(0.0001f, go.transform.lossyScale.x);
        FinishHitbox(go, true);
        log.Add($"머리 Sphere: r={radius:F3}m");
    }

    /// <summary>몸통(가슴~목) 히트박스를 만듭니다. 좌우 폭은 양쪽 상완 뼈 간격으로 잡습니다.</summary>
    private static void BuildTorso(Dictionary<HumanBodyBones, Transform> bones, int layer, List<string> log)
    {
        Transform owner = bones.TryGetValue(HumanBodyBones.Chest, out Transform chest) ? chest : bones[HumanBodyBones.Spine];
        Vector3 top = bones.TryGetValue(HumanBodyBones.Neck, out Transform neck)
            ? neck.position
            : Vector3.Lerp(owner.position, bones[HumanBodyBones.Head].position, 0.5f);

        GameObject go = CreateHitboxObject(owner, "SpineHitbox", layer);
        BoxCollider collider = go.AddComponent<BoxCollider>();
        FitBox(collider, go.transform, 0.35f,
            bones[HumanBodyBones.Spine].position,
            bones[HumanBodyBones.LeftUpperArm].position,
            bones[HumanBodyBones.RightUpperArm].position,
            top);
        FinishHitbox(go, false);
        log.Add($"몸통 Box: size={collider.size:F3}");
    }

    /// <summary>골반 히트박스를 만듭니다. 골반 뼈, 양쪽 허벅지 시작점, 척추 시작점을 감쌉니다.</summary>
    private static void BuildPelvis(Dictionary<HumanBodyBones, Transform> bones, int layer, List<string> log)
    {
        Transform hips = bones[HumanBodyBones.Hips];
        GameObject go = CreateHitboxObject(hips, "PelvisHitbox", layer);
        BoxCollider collider = go.AddComponent<BoxCollider>();
        FitBox(collider, go.transform, 0.45f,
            bones[HumanBodyBones.LeftUpperLeg].position,
            bones[HumanBodyBones.RightUpperLeg].position,
            bones[HumanBodyBones.Spine].position);
        FinishHitbox(go, false);
        log.Add($"골반 Box: size={collider.size:F3}");
    }

    /// <summary>
    /// 팔다리 한 마디의 캡슐 히트박스를 만듭니다.
    /// </summary>
    /// <remarks>
    /// 히트박스 오브젝트를 시작 뼈의 자식으로 두고 로컬 Y축을 다음 뼈 방향으로 돌립니다. 뼈의 로컬 축 방향은 모델마다 다르므로,
    /// 가장 가까운 축을 고르는 대신 방향 자체를 맞춰 비스듬한 뼈에서도 캡슐이 어긋나지 않게 합니다.
    /// </remarks>
    private static void BuildLimb(
        Dictionary<HumanBodyBones, Transform> bones,
        HumanBodyBones startBone,
        HumanBodyBones endBone,
        float radiusRatio,
        string partName,
        int layer,
        List<string> log)
    {
        Transform start = bones[startBone];
        Transform end = bones[endBone];
        float length = Vector3.Distance(start.position, end.position);
        float radius = Mathf.Max(0.03f, length * radiusRatio);

        GameObject go = CreateHitboxObject(start, partName, layer);
        AlignUp(go.transform, end.position - start.position);

        CapsuleCollider collider = go.AddComponent<CapsuleCollider>();
        float scale = Mathf.Max(0.0001f, go.transform.lossyScale.y);
        collider.direction = 1;
        collider.center = new Vector3(0.0f, length * 0.5f / scale, 0.0f);
        collider.height = Mathf.Max(length, radius * 2.0f) / scale;
        collider.radius = radius / scale;
        FinishHitbox(go, false);
        log.Add($"{partName} Capsule: len={length:F3}m r={radius:F3}m");
    }

    /// <summary>손 히트박스를 만듭니다. 전완 방향으로 전완 길이의 약 40%만큼 뻗은 상자입니다.</summary>
    private static void BuildHand(
        Dictionary<HumanBodyBones, Transform> bones,
        HumanBodyBones lowerArm,
        HumanBodyBones handBone,
        string partName,
        int layer,
        List<string> log)
    {
        Transform hand = bones[handBone];
        Vector3 forward = hand.position - bones[lowerArm].position;
        float length = forward.magnitude * 0.4f;

        GameObject go = CreateHitboxObject(hand, partName, layer);
        AlignUp(go.transform, forward);

        BoxCollider collider = go.AddComponent<BoxCollider>();
        float scale = Mathf.Max(0.0001f, go.transform.lossyScale.y);
        collider.center = new Vector3(0.0f, length * 0.5f / scale, 0.0f);
        collider.size = new Vector3(length * 0.6f, length, length * 0.8f) / scale;
        FinishHitbox(go, false);
        log.Add($"{partName} Box: len={length:F3}m");
    }

    /// <summary>
    /// 발 히트박스를 만듭니다. 발가락 뼈가 있으면 그쪽으로, 없으면 루트 정면으로 종아리 길이의 약 35%만큼 뻗습니다.
    /// </summary>
    private static void BuildFoot(
        Dictionary<HumanBodyBones, Transform> bones,
        HumanBodyBones lowerLeg,
        HumanBodyBones footBone,
        HumanBodyBones toesBone,
        string partName,
        int layer,
        List<string> log)
    {
        Transform foot = bones[footBone];
        float calfLength = Vector3.Distance(bones[lowerLeg].position, foot.position);
        Vector3 forward = bones.TryGetValue(toesBone, out Transform toes)
            ? toes.position - foot.position
            : foot.root.forward * calfLength * 0.35f;
        float length = Mathf.Max(forward.magnitude, calfLength * 0.3f);

        GameObject go = CreateHitboxObject(foot, partName, layer);
        AlignUp(go.transform, forward);

        BoxCollider collider = go.AddComponent<BoxCollider>();
        float scale = Mathf.Max(0.0001f, go.transform.lossyScale.y);
        collider.center = new Vector3(0.0f, length * 0.5f / scale, 0.0f);
        collider.size = new Vector3(length * 0.45f, length, length * 0.35f) / scale;
        FinishHitbox(go, false);
        log.Add($"{partName} Box: len={length:F3}m ({(toes != null ? "발가락 방향" : "정면 방향")})");
    }

    private static GameObject CreateHitboxObject(Transform bone, string partName, int layer)
    {
        GameObject go = new GameObject(HitboxPrefix + partName);
        go.layer = layer;
        go.transform.SetParent(bone, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        return go;
    }

    /// <summary>히트박스 오브젝트의 로컬 Y축이 지정한 월드 방향을 향하도록 돌립니다.</summary>
    private static void AlignUp(Transform transform, Vector3 worldDirection)
    {
        if (worldDirection.sqrMagnitude > 0.000001f)
        {
            transform.rotation = Quaternion.FromToRotation(Vector3.up, worldDirection.normalized);
        }
    }

    /// <summary>
    /// Howler.prefab 규칙대로 마무리합니다. 트리거, 평소 꺼짐, Hitbox 컴포넌트를 붙입니다.
    /// </summary>
    /// <remarks>평소 꺼 두는 이유는 <see cref="HitboxGroup"/>이 사격 레이가 지나갈 때만 켜기 때문입니다.</remarks>
    private static void FinishHitbox(GameObject go, bool isHeadshot)
    {
        Collider collider = go.GetComponent<Collider>();
        collider.isTrigger = true;
        collider.enabled = false;

        Hitbox hitbox = go.AddComponent<Hitbox>();
        SerializedObject serialized = new SerializedObject(hitbox);
        serialized.FindProperty("m_isHeadshot").boolValue = isHeadshot;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>지정한 월드 지점들을 감싸는 상자로 맞춥니다. 가장 긴 변의 일정 비율을 최소 두께로 둡니다.</summary>
    private static void FitBox(BoxCollider collider, Transform owner, float minimumThicknessRatio, params Vector3[] worldPoints)
    {
        Bounds bounds = new Bounds(owner.InverseTransformPoint(worldPoints[0]), Vector3.zero);
        for (int i = 1; i < worldPoints.Length; i++)
        {
            bounds.Encapsulate(owner.InverseTransformPoint(worldPoints[i]));
        }

        bounds.Encapsulate(Vector3.zero);

        Vector3 size = bounds.size;
        float span = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
        size += Vector3.one * Mathf.Max(span * 0.10f, 0.01f) * 2.0f;

        float minimumThickness = span * minimumThicknessRatio;
        size.x = Mathf.Max(size.x, minimumThickness);
        size.y = Mathf.Max(size.y, minimumThickness);
        size.z = Mathf.Max(size.z, minimumThickness);

        collider.center = bounds.center;
        collider.size = size;
    }
}
