using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class NarinSdfTestBinder : MonoBehaviour
{
    [SerializeField] private string targetObjectName = "Narin_low";
    [SerializeField] private bool useHeadForBody = true;
    [SerializeField] private bool forwardInverted;
    [SerializeField] private bool rightInverted;

    [Header("Body / Face")]
    [SerializeField] private Material bodyMaterial;
    [SerializeField] private Texture2D bodySdfTexture;
    [SerializeField] private float bodyLightThreshold = 195f;
    [SerializeField] private float bodyHardness = 0.62f;

    [Header("Hair")]
    [SerializeField] private Material hairMaterial;
    [SerializeField] private Texture2D hairSdfTexture;
    [SerializeField] private float hairLightThreshold = 175f;
    [SerializeField] private float hairHardness = 0.68f;

    [Header("Cloth")]
    [SerializeField] private Material clothMaterial;
    [SerializeField] private Texture2D clothSdfTexture;
    [SerializeField] private float clothLightThreshold = 180f;
    [SerializeField] private float clothHardness = 0.7f;

    [Header("Accessory")]
    [SerializeField] private Material accessoryMaterial;
    [SerializeField] private Texture2D accessorySdfTexture;
    [SerializeField] private float accessoryLightThreshold = 185f;
    [SerializeField] private float accessoryHardness = 0.72f;

    private Transform cachedTarget;
    private Transform cachedHead;

    private static readonly int ShadowT = Shader.PropertyToID("_ShadowT");
    private static readonly int ShadowTLightThreshold = Shader.PropertyToID("_ShadowTLightThreshold");
    private static readonly int ShadowTHardness = Shader.PropertyToID("_ShadowTHardness");
    private static readonly int ObjectForward = Shader.PropertyToID("_ObjectForward");
    private static readonly int ObjectRight = Shader.PropertyToID("_ObjectRight");
    private static readonly int ShadowTFeature = Shader.PropertyToID("_N_F_ST");
    private static readonly int ShadowTSdfMode = Shader.PropertyToID("_N_F_STSDFM");

    private void OnEnable()
    {
        ApplySdfSettings();
    }

    private void OnValidate()
    {
        cachedTarget = null;
        cachedHead = null;
        ApplySdfSettings();
    }

    private void LateUpdate()
    {
        ApplySdfSettings();
    }

    private void ApplySdfSettings()
    {
        Transform target = ResolveTarget();
        if (target == null)
        {
            return;
        }

        Transform bodyFollow = useHeadForBody ? ResolveHead(target) : target;

        ApplyMaterial(bodyMaterial, bodySdfTexture, bodyFollow, bodyLightThreshold, bodyHardness);
        ApplyMaterial(hairMaterial, hairSdfTexture, bodyFollow, hairLightThreshold, hairHardness);
        ApplyMaterial(clothMaterial, clothSdfTexture, target, clothLightThreshold, clothHardness);
        ApplyMaterial(accessoryMaterial, accessorySdfTexture, target, accessoryLightThreshold, accessoryHardness);
    }

    private Transform ResolveTarget()
    {
        if (cachedTarget != null)
        {
            return cachedTarget;
        }

        GameObject targetObject = GameObject.Find(targetObjectName);
        cachedTarget = targetObject != null ? targetObject.transform : null;
        return cachedTarget;
    }

    private Transform ResolveHead(Transform target)
    {
        if (cachedHead != null)
        {
            return cachedHead;
        }

        Transform[] children = target.GetComponentsInChildren<Transform>(true);
        foreach (Transform child in children)
        {
            string childName = child.name.ToLowerInvariant();
            if (childName == "head" || childName == "ch_head" || childName == "head_def" || childName == "j_bip_c_head")
            {
                cachedHead = child;
                return cachedHead;
            }
        }

        foreach (Transform child in children)
        {
            string childName = child.name.ToLowerInvariant();
            if (childName.Contains("head") && !childName.Contains("gear") && !childName.Contains("hair"))
            {
                cachedHead = child;
                return cachedHead;
            }
        }

        cachedHead = target;
        return cachedHead;
    }

    private void ApplyMaterial(Material material, Texture2D sdfTexture, Transform followTarget, float lightThreshold, float hardness)
    {
        if (material == null || sdfTexture == null || followTarget == null)
        {
            return;
        }

        material.EnableKeyword("N_F_ST_ON");
        material.EnableKeyword("N_F_STSDFM_ON");
        material.SetFloat(ShadowTFeature, 1f);
        material.SetFloat(ShadowTSdfMode, 1f);
        material.SetTexture(ShadowT, sdfTexture);
        material.SetFloat(ShadowTLightThreshold, lightThreshold);
        material.SetFloat(ShadowTHardness, hardness);

        Vector3 forward = forwardInverted ? -followTarget.forward : followTarget.forward;
        Vector3 right = rightInverted ? -followTarget.right : followTarget.right;
        material.SetVector(ObjectForward, forward);
        material.SetVector(ObjectRight, right);
    }
}
