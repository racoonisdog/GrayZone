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
            if (ShelterDataManager.Instance != null)
            {
                return ShelterDataManager.Instance.CurrentDay;
            }

            return 1;
        }
    }

    private void Awake()
    {
        if (TryRejectDuplicate())
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

        if (ShelterDataManager.Instance == null)
        {
            Debug.LogWarning("[GameDateManager] ShelterDataManager.Instance is null.");
            return false;
        }

        int previousDay = CurrentDay;
        int nextDay = previousDay + days;

        ShelterDataManager.Instance.SetCurrentDay(nextDay);
        DayAdvanced?.Invoke(previousDay, nextDay);
        return true;
    }

    public int GetRemainingDays(int completeDay)
    {
        return Mathf.Max(0, completeDay - CurrentDay);
    }

    private bool TryRejectDuplicate()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return true;
        }

        return false;
    }
}
