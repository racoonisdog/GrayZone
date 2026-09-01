using System;
using UnityEngine;

/// <summary>피드백 SO가 소유하는 참조의 용도를 분류합니다.</summary>
public enum FeedbackReferenceKind
{
    Audio,
    VisualEffect,
    Decal,
    Tracer,
    Shell,
    SurfaceMaterial
}

/// <summary>
/// 피드백 SO 안에서 실제 사운드, 이펙트, 데칼 등의 Unity 에셋 참조 필드임을 표시합니다.
/// </summary>
/// <remarks>
/// 누락 검사 도구가 이 특성을 읽어 빈 참조와 빈 배열 항목을 자동으로 보고합니다.
/// 피드백 수명이나 볼륨처럼 참조가 아닌 표현 설정에는 붙이지 않습니다.
/// </remarks>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class FeedbackReferenceAttribute : PropertyAttribute
{
    /// <summary>참조의 용도와 검사 창에 표시할 이름을 지정합니다.</summary>
    /// <param name="kind">참조가 담당하는 피드백 종류입니다.</param>
    /// <param name="displayName">누락 검사 창에 표시할 필드 이름입니다.</param>
    public FeedbackReferenceAttribute(FeedbackReferenceKind kind, string displayName)
    {
        Kind = kind;
        DisplayName = displayName;
    }

    /// <summary>참조가 담당하는 피드백 종류입니다.</summary>
    public FeedbackReferenceKind Kind { get; }

    /// <summary>누락 검사 창에 표시할 필드 이름입니다.</summary>
    public string DisplayName { get; }
}
