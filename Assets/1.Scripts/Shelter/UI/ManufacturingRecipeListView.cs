using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 제조 레시피 행을 한 번 생성해 캐시하고 시설 상태에 따라 잠금 표시만 갱신합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class ManufacturingRecipeListView : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RectTransform m_contentRoot;
    [SerializeField] private ManufacturingRecipeListItemView m_rowPrefab;
    [SerializeField] private ManufacturingManager m_manager;
    [SerializeField] private ItemListTooltipPresenter m_tooltipPresenter;

    private readonly List<CachedRecipeRow> m_rows = new();
    private readonly HashSet<int> m_invalidRecipeIndicesLogged = new();
    private bool m_isPrewarmed;
    private bool m_isSubscribed;
    private bool m_setupErrorLogged;

    public event Action<string> RecipeSelected;

    public int CachedRowCount => m_rows.Count;

    private void Awake()
    {
        Prewarm();
    }

    private void OnEnable()
    {
        Subscribe();
        RefreshRecipeStates();
    }

    private void OnDisable()
    {
        Unsubscribe();
        m_tooltipPresenter?.Hide();
    }

    /// <summary>
    /// 매니저의 현재 Inspector 배열 순서대로 유효한 레시피 행을 한 번만 생성합니다.
    /// 비활성 CreateView도 활성 상위 컨트롤러가 이 메서드를 직접 호출할 수 있습니다.
    /// </summary>
    public bool Prewarm()
    {
        if (m_isPrewarmed)
            return true;

        RectTransform contentRoot = m_contentRoot != null
            ? m_contentRoot
            : transform as RectTransform;
        if (contentRoot == null || m_rowPrefab == null || m_manager == null)
        {
            LogSetupErrorOnce();
            return false;
        }

        // TODO(CSV): CSV 연동 시 Inspector 배열 순서 대신 명시적인 displayOrder와
        // recipeId 보조 정렬을 사용해 한 번 정렬한 목록을 생성합니다.
        IReadOnlyList<ManufacturingRecipeDefinition> recipes = m_manager.Recipes;
        HashSet<string> registeredRecipeIds = new(StringComparer.Ordinal);
        for (int i = 0; i < recipes.Count; i++)
        {
            ManufacturingRecipeDefinition recipe = recipes[i];
            if (recipe == null
                || string.IsNullOrWhiteSpace(recipe.RecipeId)
                || !registeredRecipeIds.Add(recipe.RecipeId))
            {
                LogInvalidRecipeOnce(i);
                continue;
            }

            ManufacturingRecipeListItemView row = Instantiate(
                m_rowPrefab,
                contentRoot,
                false);
            row.name = $"{m_rowPrefab.name}_{recipe.RecipeId}";
            row.SetTooltipPresenter(m_tooltipPresenter);
            row.Bind(
                recipe,
                m_manager.IsRecipeUnlocked(recipe),
                HandleRowClicked);
            m_rows.Add(new CachedRecipeRow(recipe, row));
        }

        m_isPrewarmed = true;
        return true;
    }

    /// <summary>
    /// 캐시된 행을 다시 만들지 않고 현재 시설 레벨 기준 잠금 상태를 갱신합니다.
    /// </summary>
    public bool RefreshRecipeStates()
    {
        if (!Prewarm())
            return false;

        for (int i = 0; i < m_rows.Count; i++)
        {
            CachedRecipeRow cached = m_rows[i];
            cached.Row.SetTooltipPresenter(m_tooltipPresenter);
            cached.Row.Bind(
                cached.Recipe,
                m_manager.IsRecipeUnlocked(cached.Recipe),
                HandleRowClicked);
        }

        return true;
    }

    public bool TryGetRow(
        string recipeId,
        out ManufacturingRecipeListItemView row)
    {
        row = null;
        if (string.IsNullOrWhiteSpace(recipeId))
            return false;

        for (int i = 0; i < m_rows.Count; i++)
        {
            CachedRecipeRow cached = m_rows[i];
            if (string.Equals(
                    cached.Recipe.RecipeId,
                    recipeId,
                    StringComparison.Ordinal))
            {
                row = cached.Row;
                return row != null;
            }
        }

        return false;
    }

    private void HandleRowClicked(string recipeId)
    {
        RecipeSelected?.Invoke(recipeId);
    }

    private void Subscribe()
    {
        if (m_isSubscribed || m_manager == null)
            return;

        m_manager.StateChanged += RefreshRecipeStatesFromEvent;
        m_isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (!m_isSubscribed)
            return;

        if (m_manager != null)
            m_manager.StateChanged -= RefreshRecipeStatesFromEvent;

        m_isSubscribed = false;
    }

    private void RefreshRecipeStatesFromEvent()
    {
        RefreshRecipeStates();
    }

    private void LogSetupErrorOnce()
    {
        if (m_setupErrorLogged)
            return;

        Debug.LogError(
            $"[{nameof(ManufacturingRecipeListView)}] "
            + "Content root, row prefab, and ManufacturingManager must be assigned.",
            this);
        m_setupErrorLogged = true;
    }

    private void LogInvalidRecipeOnce(int recipeIndex)
    {
        if (!m_invalidRecipeIndicesLogged.Add(recipeIndex))
            return;

        Debug.LogError(
            $"[{nameof(ManufacturingRecipeListView)}] "
            + $"Recipe entry {recipeIndex} is null, has no ID, or duplicates an earlier ID.",
            this);
    }

    private readonly struct CachedRecipeRow
    {
        public ManufacturingRecipeDefinition Recipe { get; }
        public ManufacturingRecipeListItemView Row { get; }

        public CachedRecipeRow(
            ManufacturingRecipeDefinition recipe,
            ManufacturingRecipeListItemView row)
        {
            Recipe = recipe;
            Row = row;
        }
    }
}
