using System.Collections.Generic;
using UnityEngine;

public class PoolManager_TPZ : MonoBehaviour
{
    public static PoolManager_TPZ instance;

    [SerializeField] private GameObject[] prefabs;
    private int poolSize = 1;
    private List<GameObject>[] objPools;

    private void Awake()
    {
        instance = this;
        InitObjPool();
    }

    private void InitObjPool()
    {
        objPools = new List<GameObject>[prefabs.Length];

        for (int i = 0; i < prefabs.Length; i++)
        {
            objPools[i] = new List<GameObject>();

            for (int j = 0; j < poolSize; j++)
            {
                GameObject obj = Instantiate(prefabs[i]);
                obj.SetActive(false);
                objPools[i].Add(obj);
            }
        }
    }

    private GameObject GetInactiveObj(int index)
    {
        GameObject obj = null;

        for (int i = 0; i < objPools[index].Count; i++)
        {
            if (!objPools[index][i].activeInHierarchy)
            {
                obj = objPools[index][i];
                return obj;
            }
        }

        obj = Instantiate(prefabs[index]);
        obj.SetActive(false);
        objPools[index].Add(obj);

        return obj;
    }

    public GameObject ActivateObj(int index)
    {
        GameObject obj = GetInactiveObj(index);
        if (obj == null) return null;

        obj.SetActive(true);
        return obj;
    }

    public GameObject GetObject(int index, Vector3 position)
    {
        GameObject obj = GetInactiveObj(index);
        if (obj == null) return null;

        obj.transform.position = position;
        obj.SetActive(true);
        return obj;
    }

    public GameObject GetObject(int index, Vector3 position, Quaternion rotation)
    {
        GameObject obj = GetInactiveObj(index);
        if (obj == null) return null;

        obj.transform.position = position;
        obj.transform.rotation = rotation;
        obj.SetActive(true);
        return obj;
    }
}