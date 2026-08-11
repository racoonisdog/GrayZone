/// <summary>AI가 이번 프레임에 요청하는 주 행동입니다.</summary>
/// <remarks>
/// 이동 축의 배타 선택입니다. 조준·사격·재장전은 이것과 <b>병행</b>될 수 있으므로 따로 둡니다
/// (§10.3 "이동 중에도 조준과 사격을 계속한다", §18.2 "재장전 중 합류에 일반 이동 필요 -> 병행").
/// </remarks>
public enum SquadAIActionKind
{
    /// <summary>활동 상태 제한으로 아무 요청도 내지 않습니다(§18.1 1).</summary>
    Restricted,

    /// <summary>자동 구조를 수행합니다(§18.1 2, §16).</summary>
    /// <remarks><b>미구현입니다.</b> 우선순위 자리만 잡아 둔 값이며 현재는 선택되지 않습니다.</remarks>
    Rescue,

    /// <summary>플레이어에게 합류합니다(§18.1 4).</summary>
    Join,

    /// <summary>현재 대상을 상대로 전투 행동을 합니다(§18.1 5).</summary>
    Combat,

    /// <summary>일반 동행입니다(§18.1 7).</summary>
    Follow,
}

/// <summary>이번 프레임에 요청하는 재장전입니다.</summary>
public enum SquadAIReloadIntent
{
    /// <summary>새로 시작할 재장전이 없습니다. 이미 진행 중인 재장전은 여기에 나타나지 않습니다.</summary>
    None,

    /// <summary>탄창이 비어 반드시 해야 하는 재장전입니다(§18.1 3의 "보류 중인 재장전", §12.1).</summary>
    EmptyMagazine,

    /// <summary>여유가 있을 때만 하는 전술 재장전입니다(§18.1 6).</summary>
    Tactical,
}

/// <summary>이번 결정을 확정한 우선순위 단계입니다(§18.1의 번호).</summary>
public enum SquadAIDecisionStep
{
    /// <summary>아직 결정하지 않았습니다.</summary>
    None = 0,

    /// <summary>1. 다운과 전투 이탈 같은 활동 상태 제한.</summary>
    ActivityRestricted = 1,

    /// <summary>2. 자동 구조.</summary>
    AutoRescue = 2,

    /// <summary>3. 진행 또는 보류 중인 재장전.</summary>
    PendingReload = 3,

    /// <summary>4. 합류와 경로 복구.</summary>
    Join = 4,

    /// <summary>5. 전투 대상 선정, 사격과 전투 위치 조정.</summary>
    Combat = 5,

    /// <summary>6. 전술 재장전 시작.</summary>
    TacticalReload = 6,

    /// <summary>7. 일반 동행과 자세 동조.</summary>
    Follow = 7,
}

/// <summary>
/// AI 조작 캐릭터가 이번 프레임에 무엇을 요청할지 정하는 판정입니다.
/// </summary>
/// <remarks>
/// 공용 문서 `스쿼드 AI 시스템` v0.2 §18이 정본입니다.
///
/// <para>
/// <b>이 클래스가 있는 이유</b>: §18.1의 우선순위 7단계가 이전에는 <see cref="SquadAIController"/>의
/// 호출 순서와 각 실행 함수 초입의 조기 반환에 흩어져 암묵적으로만 표현돼 있었습니다. 그래서 새 행동
/// (§13 전투 중 합류, §16 자동 구조, §17 재배치)을 넣을 때 어느 순서에 끼워야 하는지가 코드에 드러나지
/// 않았습니다. 여기서는 7단계를 <see cref="Resolve"/> 하나에 순서대로 적어 두고, 실행 계층은 그 결과만
/// 받아 수행합니다.
/// </para>
///
/// <para>
/// <b>배타 선택이 아닙니다.</b> 문서가 "다음 순서는 특정 FSM 구조를 의미하지 않으며 AI가 행동 요청을
/// 결정할 때 확인하는 기획 우선순위다"라고 못박습니다. 실제로 §18.2는 "재장전 중 합류에 일반 이동 필요
/// -> 일반 이동과 재장전 병행"처럼 공존을 규정합니다. 그래서 결과는 단일 상태가 아니라
/// 이동 축(<see cref="Result.Kind"/>)과 조준·사격·재장전 축을 함께 담습니다.
/// </para>
///
/// <para>
/// <b>판정하지 않는 것</b>: 이번 프레임의 몸 회전 결과에 의존하는 조준 정렬(<c>IsAimAligned</c>)은
/// 실행 계층에 남습니다. 회전을 돌리기 전에 판단하면 한 프레임 뒤처진 값을 보게 됩니다.
/// </para>
///
/// <para>
/// MonoBehaviour가 아니라 <see cref="SquadAIController"/>가 소유하는 일반 클래스입니다.
/// <see cref="SquadAITargeting"/>과 같은 방식으로, 호출자가 <see cref="Context"/>를 채워 넘기고
/// 이 클래스는 컴포넌트를 직접 뒤지지 않습니다.
/// </para>
///
/// <para>
/// <b>문서의 "AI 조작 슬롯"과의 차이</b>: 문서(§4.2)는 행동 판단 맥락을 AI 조작 슬롯이 소유한다고
/// 규정하지만 현재 코드에는 슬롯 개체가 없어 캐릭터의 <see cref="SquadAIController"/>에 붙어 있습니다.
/// <see cref="SquadAITargeting"/>과 같은 편차이며 슬롯 도입 시 함께 옮겨야 합니다.
/// </para>
/// </remarks>
public class SquadAIDecision
{
    /// <summary>판정에 필요한, 이 AI가 조작하는 캐릭터의 현재 사정입니다.</summary>
    public struct Context
    {
        /// <summary>지금 행동 요청을 낼 수 있는 상태인지입니다(§18.1 1).</summary>
        /// <remarks>다운·사망·전투 이탈, 리더 부재, 이동 수단 비활성이 모두 여기에 접힙니다.</remarks>
        public bool CanAct;

        /// <summary>활동 제한 상태에서 이동을 멈춰 세워야 하는지입니다.</summary>
        /// <remarks>
        /// 다운처럼 이동 수단이 이미 꺼진 경우와, 자기 자신이 리더라 갈 곳이 없는 경우를 가릅니다.
        /// 전자는 멈출 대상이 없고 후자는 명시적으로 세워야 합니다.
        /// </remarks>
        public bool HoldPosition;

        /// <summary>자동 구조가 필요한지입니다(§18.1 2, §16).</summary>
        /// <remarks><b>미구현이라 항상 false입니다.</b> 우선순위 자리만 잡아 둔 입력입니다.</remarks>
        public bool RescueRequested;

        /// <summary>무기를 들고 있는지입니다.</summary>
        public bool HasWeapon;

        /// <summary>이미 재장전이 진행 중인지입니다.</summary>
        /// <remarks>
        /// 진행 중이면 새 재장전 판단을 내지 않습니다. §12.2가 시작한 재장전을 끊지 않도록 규정하므로,
        /// 여기서 할 일은 "건드리지 않는 것"입니다.
        /// </remarks>
        public bool IsReloading;

        /// <summary>지금 재장전을 시작할 수 있는 상태인지입니다.</summary>
        public bool CanStartReload;

        /// <summary>현재 탄창 잔탄입니다.</summary>
        public int CurrentBullet;

        /// <summary>전술 재장전을 검토할 잔탄 기준입니다. 0 이하면 전술 재장전을 하지 않습니다.</summary>
        public int TacticalReloadThreshold;

        /// <summary>발사와 재장전이 허용된 상태인지입니다.</summary>
        /// <remarks>디버그 토글입니다. 꺼도 대상 선정과 조준은 그대로 둡니다.</remarks>
        public bool FiringEnabled;

        /// <summary>지금 합류 중인지입니다(§18.1 4).</summary>
        public bool IsJoining;

        /// <summary>현재 대상이 있는지입니다(§9).</summary>
        public bool HasTarget;

        /// <summary>이번 프레임에 겨눌 지점이 정해졌는지입니다.</summary>
        /// <remarks>합류 중에는 조준 요청을 종료하므로(§18.2) 이 값이 false로 들어옵니다.</remarks>
        public bool HasAimPoint;

        /// <summary>현재 대상에게 지금 사격할 수 있는지입니다(§10.1, §11.2).</summary>
        public bool CanFireAtTarget;
    }

    /// <summary>이번 프레임에 확정된 행동 요청입니다.</summary>
    public struct Result
    {
        /// <summary>이동 축의 주 행동입니다.</summary>
        public SquadAIActionKind Kind;

        /// <summary>이 결정을 확정한 §18.1 단계입니다. 여러 축이 걸리면 더 높은 우선순위를 적습니다.</summary>
        /// <remarks>진단용입니다. 왜 이 행동이 나왔는지를 문서 번호로 되짚기 위한 값입니다.</remarks>
        public SquadAIDecisionStep Step;

        /// <summary>제자리에 세워야 하는지입니다.</summary>
        public bool HoldPosition;

        /// <summary>대상을 겨누는 요청입니다.</summary>
        public bool Aim;

        /// <summary>발사 요청입니다. 조준 정렬 여부는 실행 계층이 따로 봅니다.</summary>
        public bool Fire;

        /// <summary>달려서 이동할지입니다(§13).</summary>
        /// <remarks>
        /// 합류일 때만 켜집니다. 전투 중 이동은 §10.3이 개인적인 회피를 위한 달리기 반복을 금지하고,
        /// 평상시 동행은 걷기에 여유 배수를 얹는 것으로 충분합니다.
        /// </remarks>
        public bool Sprint;

        /// <summary>새로 시작할 재장전입니다.</summary>
        public SquadAIReloadIntent Reload;
    }

    /// <summary>
    /// §18.1의 우선순위를 위에서부터 확인해 이번 프레임의 행동 요청을 정합니다.
    /// </summary>
    /// <param name="context">이 AI가 조작하는 캐릭터의 현재 사정입니다.</param>
    /// <returns>확정된 행동 요청입니다.</returns>
    /// <remarks>
    /// 아래 주석의 번호는 문서 §18.1의 번호와 같습니다. 새 행동을 넣을 때는 그 행동이 문서에서 몇 번인지
    /// 먼저 정하고 이 순서의 해당 위치에 넣어야 합니다.
    /// </remarks>
    public Result Resolve(in Context context)
    {
        Result result = default;

        // 1. 다운과 전투 이탈 같은 활동 상태 제한.
        //    조준·사격·재장전을 모두 내지 않습니다. 판단 정보 자체를 지우는 것은 SquadAITargeting이 합니다(§9.5).
        if (!context.CanAct)
        {
            result.Kind = SquadAIActionKind.Restricted;
            result.Step = SquadAIDecisionStep.ActivityRestricted;
            result.HoldPosition = context.HoldPosition;
            return result;
        }

        // 2. 자동 구조(§16).
        //    미구현이라 RescueRequested가 항상 false입니다. 자리를 비워 두면 나중에 넣을 때 어느 순서인지
        //    다시 찾아야 하므로 분기만 먼저 둡니다.
        if (context.RescueRequested)
        {
            result.Kind = SquadAIActionKind.Rescue;
            result.Step = SquadAIDecisionStep.AutoRescue;
            return result;
        }

        // 3 + 6. 재장전 축.
        //    보류된 재장전(빈 탄창)은 3번이라 합류·전투보다 위이고, 전술 재장전은 6번이라 그 아래입니다.
        //    두 판단이 같은 조건 묶음을 공유하므로 한 함수로 뽑되 우선순위 차이는 그 안에서 지킵니다.
        result.Reload = ResolveReload(in context);

        // 4. 합류와 경로 복구.
        //    합류가 전투 위치 조정을 이깁니다. 조준·사격 요청은 이미 종료된 상태로 들어옵니다
        //    (§18.2 "합류 시작 중 조준 또는 연속 사격 -> 실행 요청 즉시 종료 후 합류").
        if (context.IsJoining)
        {
            result.Kind = SquadAIActionKind.Join;
            result.Aim = context.HasAimPoint;

            // 재장전 중에는 걷습니다(§13 "진행 중인 재장전이 있으면 일반 이동과 함께 완료하고 필요한
            // 경우 이후 달린다", §18.2 "재장전 중 합류에 달리기 필요 -> 재장전 완료 후 달리기 재평가").
            // 합류를 멈추는 것이 아니라 이동 방식만 걷기로 두는 것이므로 §18.1 4번은 그대로 유지됩니다.
            // 재장전이 끝나면 이 조건이 저절로 풀려 달리기가 다시 켜집니다. 그것이 "재평가"입니다.
            // 이번 프레임에 재장전을 시작하기로 했다면 그것도 곧 진행 중이 되므로 함께 봅니다.
            result.Sprint = !context.IsReloading && result.Reload == SquadAIReloadIntent.None;

            result.Step = result.Reload == SquadAIReloadIntent.EmptyMagazine
                ? SquadAIDecisionStep.PendingReload
                : SquadAIDecisionStep.Join;
            return result;
        }

        // 5. 전투 대상 선정, 사격과 전투 위치 조정.
        //    겨누는 것과 쏘는 것은 다른 판정입니다(§8.5). 유예 중에는 대상도 조준점도 있지만
        //    CanFireAtTarget이 false라 겨누기만 합니다.
        if (context.HasTarget && context.HasAimPoint)
        {
            result.Kind = SquadAIActionKind.Combat;
            result.Aim = true;
            result.Fire = context.FiringEnabled && context.CanFireAtTarget;
            result.Step = result.Reload == SquadAIReloadIntent.EmptyMagazine
                ? SquadAIDecisionStep.PendingReload
                : SquadAIDecisionStep.Combat;
            return result;
        }

        // 7. 일반 동행과 자세 동조.
        result.Kind = SquadAIActionKind.Follow;
        result.Aim = context.HasAimPoint;
        result.Step = result.Reload switch
        {
            SquadAIReloadIntent.EmptyMagazine => SquadAIDecisionStep.PendingReload,
            SquadAIReloadIntent.Tactical => SquadAIDecisionStep.TacticalReload,
            _ => SquadAIDecisionStep.Follow,
        };

        return result;
    }

    /// <summary>
    /// 이번 프레임에 새로 시작할 재장전을 정합니다(§18.1 3과 6, §12.1).
    /// </summary>
    /// <param name="context">이 AI가 조작하는 캐릭터의 현재 사정입니다.</param>
    /// <returns>시작할 재장전 종류입니다.</returns>
    /// <remarks>
    /// <b>빈 탄창은 무조건입니다</b>(§12.1 "현재 탄창이 0이고 예비 탄약이 있으면 AI가 직접 재장전을
    /// 요청한다"). 3번이라 합류(4)보다 위이므로 합류 중에도 시작합니다.
    /// <para>
    /// <b>전술 재장전은 6번</b>이라 합류(4)와 전투(5) 아래입니다. 그래서 지금 쏠 수 있는 적이 있거나
    /// 합류 중이면 시작하지 않습니다. 기준 잔탄은 밸런스 영역이라 기본값이 0(비활성)입니다.
    /// </para>
    /// <para>
    /// 이미 진행 중인 재장전은 여기서 손대지 않습니다. §12.2가 시작한 재장전을 끊지 않도록 규정하고,
    /// 실제 완료는 무기가 예약해 두므로 아무것도 하지 않으면 끝납니다.
    /// </para>
    /// </remarks>
    private static SquadAIReloadIntent ResolveReload(in Context context)
    {
        if (!context.HasWeapon || !context.FiringEnabled || context.IsReloading || !context.CanStartReload)
        {
            return SquadAIReloadIntent.None;
        }

        // 3. 보류 중인 재장전 - 탄창이 비면 다른 무엇보다 먼저입니다.
        if (context.CurrentBullet <= 0)
        {
            return SquadAIReloadIntent.EmptyMagazine;
        }

        if (context.TacticalReloadThreshold <= 0 || context.CurrentBullet > context.TacticalReloadThreshold)
        {
            return SquadAIReloadIntent.None;
        }

        // 6. 전술 재장전 - 4번(합류)과 5번(사격 가능한 적)이 걸려 있으면 양보합니다.
        if (context.CanFireAtTarget || context.IsJoining)
        {
            return SquadAIReloadIntent.None;
        }

        return SquadAIReloadIntent.Tactical;
    }
}
