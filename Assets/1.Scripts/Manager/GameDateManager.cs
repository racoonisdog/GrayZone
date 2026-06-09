using System;
using UnityEngine;

public class GameDateManager : MonoBehaviour
{
    public static GameDateManager Instance { get; private set; }

    public event Action<int, int> DayAdvanced;

    public int CurrentDay
    {
        get
        {
            if (GameDataManager.Instance == null)
            {
                return 1;
            }

            return Mathf.Max(1, GameDataManager.Instance.RuntimeData.currentDay);
        }
    }

    private void Awake()
    {
        if (TryRejectDuplicateOrInvalidRoot())
        {
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public bool AdvanceDay(int days = 1)
    {
        if (days <= 0)
        {
            return false;
        }

        if (GameDataManager.Instance == null)
        {
            Debug.LogWarning("[GameDateManager] GameDataManager.Instance is null.");
            return false;
        }

        int previousDay = CurrentDay;
        int nextDay = previousDay + days;

        GameDataManager.Instance.SetCurrentDay(nextDay);
        DayAdvanced?.Invoke(previousDay, nextDay);
        return true;
    }

    public int GetRemainingDays(int completeDay)
    {
        return Mathf.Max(0, completeDay - CurrentDay);
    }

    private bool TryRejectDuplicateOrInvalidRoot()
    {
        GameManager rootManager = GetComponentInParent<GameManager>();
        if (rootManager == null)
        {
            Debug.LogWarning("[GameDateManager] Parent GameManager not found. Destroying duplicate/orphan instance.");
            Destroy(gameObject);
            return true;
        }

        if (GameManager.Instance != null && rootManager != GameManager.Instance)
        {
            Destroy(gameObject);
            return true;
        }

        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return true;
        }

        return false;
    }
}