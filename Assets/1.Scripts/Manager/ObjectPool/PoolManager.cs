using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using VInspector;

/// <summary>
/// 프리팹 인스턴스를 미리 생성하고 재사용하는 오브젝트 풀 관리자입니다.
/// </summary>
/// <remarks>
/// <para>
/// 이 컴포넌트는 <see cref="m_prefabs"/> 배열의 인덱스를 기준으로 풀을 구성합니다.
/// 외부에서는 <see cref="GetObject(int, Vector3)"/>, <see cref="GetObject(int, Vector3, Quaternion)"/>,
/// <see cref="ActivateObj(int)"/>를 통해 비활성 오브젝트를 가져와 활성화할 수 있습니다.
/// </para>
/// <para>
/// 기존 코드의 <c>instance</c> 접근 방식과의 호환을 위해
/// 소문자 <see cref="instance"/> 프로퍼티도 제공합니다.
/// </para>
/// </remarks>
public class PoolManager : MonoBehaviour
{
    private static PoolManager m_instance;

    /// <summary>
    /// 현재 씬에서 활성화된 풀 매니저 인스턴스입니다.
    /// </summary>
    public static PoolManager Instance => m_instance;

    /// <summary>
    /// 기존 코드 호환을 위한 소문자 인스턴스 접근자입니다.
    /// </summary>
    /// <remarks>
    /// 신규 코드에서는 <see cref="Instance"/> 사용을 권장합니다.
    /// </remarks>
    public static PoolManager instance => m_instance;

    [Foldout("Pool Options")]
    [Tooltip("풀에서 사용할 프리팹 목록입니다. 인덱스는 외부 호출 시 사용하는 풀 번호와 대응됩니다.")]
    [FormerlySerializedAs("prefabs")]
    [SerializeField] private GameObject[] m_prefabs;

    [Tooltip("각 프리팹별로 초기 생성할 오브젝트 수입니다.")]
    [FormerlySerializedAs("poolSize")]
    [SerializeField] private int m_poolSize = 1;

    private List<GameObject>[] m_objPools;

    /// <summary>
    /// 풀에 등록된 프리팹 목록입니다.
    /// </summary>
    public GameObject[] Prefabs => m_prefabs;

    /// <summary>
    /// 각 프리팹별 초기 풀 크기입니다.
    /// </summary>
    public int PoolSize => m_poolSize;

    /// <summary>
    /// 초기 풀 크기를 설정합니다.
    /// </summary>
    /// <param name="value">설정할 초기 풀 크기입니다. 0보다 작은 값은 0으로 보정됩니다.</param>
    public void SetPoolSize(int value)
    {
        m_poolSize = Mathf.Max(0, value);
    }

    /// <summary>
    /// 풀에서 사용할 프리팹 배열을 설정합니다.
    /// </summary>
    /// <param name="value">새 프리팹 배열입니다.</param>
    /// <remarks>
    /// 런타임 중 이 값을 변경해도 이미 생성된 풀은 자동 재구성되지 않습니다.
    /// 풀을 다시 구성하려면 별도 초기화 절차가 필요합니다.
    /// </remarks>
    public void SetPrefabs(GameObject[] value)
    {
        m_prefabs = value;
    }

    /// <summary>
    /// 싱글톤 인스턴스를 등록하고 오브젝트 풀을 초기화합니다.
    /// </summary>
    private void Awake()
    {
        if (m_instance != null && m_instance != this)
        {
            Debug.LogWarning("[PoolManager] 이미 등록된 PoolManager 인스턴스가 있어 현재 오브젝트를 비활성화합니다.", this);
            enabled = false;
            return;
        }

        m_instance = this;

        if (!ValidatePoolOptions())
        {
            enabled = false;
            return;
        }

        InitObjPool();
    }

    /// <summary>
    /// 이 인스턴스가 제거될 때 싱글톤 참조를 정리합니다.
    /// </summary>
    private void OnDestroy()
    {
        if (m_instance == this)
        {
            m_instance = null;
        }
    }

    /// <summary>
    /// 풀 초기화에 필요한 옵션이 유효한지 검사합니다.
    /// </summary>
    /// <returns>초기화 가능한 상태이면 <c>true</c>, 아니면 <c>false</c>입니다.</returns>
    private bool ValidatePoolOptions()
    {
        bool isValid = true;

        if (m_prefabs == null || m_prefabs.Length == 0)
        {
            Debug.LogError("[PoolManager] Prefabs 배열이 비어 있습니다.", this);
            isValid = false;
        }
        else
        {
            for (int i = 0; i < m_prefabs.Length; i++)
            {
                if (m_prefabs[i] != null)
                {
                    continue;
                }

                Debug.LogError($"[PoolManager] Prefabs[{i}]가 비어 있습니다.", this);
                isValid = false;
            }
        }

        if (m_poolSize < 0)
        {
            Debug.LogWarning("[PoolManager] PoolSize가 0보다 작아 0으로 보정합니다.", this);
            m_poolSize = 0;
        }

        return isValid;
    }

    /// <summary>
    /// 등록된 프리팹별 오브젝트 풀을 생성합니다.
    /// </summary>
    private void InitObjPool()
    {
        m_objPools = new List<GameObject>[m_prefabs.Length];

        for (int i = 0; i < m_prefabs.Length; i++)
        {
            m_objPools[i] = new List<GameObject>();

            for (int j = 0; j < m_poolSize; j++)
            {
                GameObject obj = CreatePooledObject(i);
                m_objPools[i].Add(obj);
            }
        }
    }

    /// <summary>
    /// 지정한 풀 인덱스에 해당하는 새 오브젝트를 생성하고 비활성화합니다.
    /// </summary>
    /// <param name="index">생성할 프리팹의 풀 인덱스입니다.</param>
    /// <returns>생성된 비활성 오브젝트입니다. 인덱스가 유효하지 않으면 <c>null</c>입니다.</returns>
    private GameObject CreatePooledObject(int index)
    {
        if (!IsValidPoolIndex(index))
        {
            return null;
        }

        GameObject obj = Instantiate(m_prefabs[index], transform);
        obj.SetActive(false);
        return obj;
    }

    /// <summary>
    /// 지정한 풀 인덱스가 유효한지 검사합니다.
    /// </summary>
    /// <param name="index">검사할 풀 인덱스입니다.</param>
    /// <returns>유효한 인덱스이면 <c>true</c>, 아니면 <c>false</c>입니다.</returns>
    private bool IsValidPoolIndex(int index)
    {
        if (m_prefabs == null || index < 0 || index >= m_prefabs.Length)
        {
            Debug.LogWarning($"[PoolManager] 유효하지 않은 풀 인덱스입니다. index:{index}", this);
            return false;
        }

        if (m_prefabs[index] == null)
        {
            Debug.LogWarning($"[PoolManager] Prefabs[{index}]가 비어 있습니다.", this);
            return false;
        }

        if (m_objPools == null || index >= m_objPools.Length || m_objPools[index] == null)
        {
            Debug.LogWarning($"[PoolManager] ObjPools[{index}]가 초기화되지 않았습니다.", this);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 지정한 풀에서 비활성 오브젝트를 반환합니다.
    /// </summary>
    /// <param name="index">가져올 오브젝트의 풀 인덱스입니다.</param>
    /// <returns>사용 가능한 비활성 오브젝트입니다. 없으면 새로 생성합니다.</returns>
    private GameObject GetInactiveObj(int index)
    {
        if (!IsValidPoolIndex(index))
        {
            return null;
        }

        for (int i = 0; i < m_objPools[index].Count; i++)
        {
            GameObject pooledObj = m_objPools[index][i];

            if (pooledObj == null)
            {
                continue;
            }

            if (!pooledObj.activeInHierarchy)
            {
                return pooledObj;
            }
        }

        GameObject obj = CreatePooledObject(index);

        if (obj != null)
        {
            m_objPools[index].Add(obj);
        }

        return obj;
    }

    /// <summary>
    /// 지정한 풀의 오브젝트를 현재 위치와 회전값 그대로 활성화합니다.
    /// </summary>
    /// <param name="index">활성화할 오브젝트의 풀 인덱스입니다.</param>
    /// <returns>활성화된 오브젝트입니다. 실패 시 <c>null</c>입니다.</returns>
    public GameObject ActivateObj(int index)
    {
        GameObject obj = GetInactiveObj(index);

        if (obj == null)
        {
            return null;
        }

        obj.SetActive(true);
        return obj;
    }

    /// <summary>
    /// 지정한 위치에 풀 오브젝트를 배치하고 활성화합니다.
    /// </summary>
    /// <param name="index">가져올 오브젝트의 풀 인덱스입니다.</param>
    /// <param name="position">활성화할 월드 위치입니다.</param>
    /// <returns>활성화된 오브젝트입니다. 실패 시 <c>null</c>입니다.</returns>
    public GameObject GetObject(int index, Vector3 position)
    {
        GameObject obj = GetInactiveObj(index);

        if (obj == null)
        {
            return null;
        }

        obj.transform.position = position;
        obj.SetActive(true);
        return obj;
    }

    /// <summary>
    /// 지정한 위치와 회전값으로 풀 오브젝트를 배치하고 활성화합니다.
    /// </summary>
    /// <param name="index">가져올 오브젝트의 풀 인덱스입니다.</param>
    /// <param name="position">활성화할 월드 위치입니다.</param>
    /// <param name="rotation">활성화할 월드 회전값입니다.</param>
    /// <returns>활성화된 오브젝트입니다. 실패 시 <c>null</c>입니다.</returns>
    public GameObject GetObject(int index, Vector3 position, Quaternion rotation)
    {
        GameObject obj = GetInactiveObj(index);

        if (obj == null)
        {
            return null;
        }

        obj.transform.position = position;
        obj.transform.rotation = rotation;
        obj.SetActive(true);
        return obj;
    }
}
