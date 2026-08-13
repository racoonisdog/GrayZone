#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityCliConnector;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UIElements;
using UIImage = UnityEngine.UI.Image;

namespace GrayZone.EditorTools
{
    /// <summary>
    /// unity-cli 커스텀 명령입니다. 이미 컴파일된 상태로 실행되므로 <c>exec</c>와 달리 Play Mode에서도
    /// 컴파일 지연 없이 즉시 결과를 반환합니다.
    /// </summary>
    /// <remarks>
    /// 호출: <c>unity-cli --project . gz_dump --params '{"what":"sceneui"}'</c>
    /// what: <c>combat</c>(플레이어 장전/조준/무기 상태), <c>sceneui</c>(캔버스·UIDocument·Image·SpriteRenderer),
    /// <c>go</c>(name 파라미터로 지정한 GameObject의 컴포넌트/활성/위치),
    /// <c>anim</c>(Animator 레이어별 현재/다음 스테이트, 전이 진행도, 파라미터 값),
    /// <c>ragdoll</c>(변이체 래그돌 전환 상태와 뼈 속도).
    ///
    /// <c>killenemy</c>는 조회가 아니라 <b>상태를 바꾸는 디버그 동작</b>입니다. 변이체 하나를 즉사시켜
    /// 사망 연출을 검증합니다. 이름이 dump인 도구에 동작이 섞여 있으므로 호출 시 유의하십시오.
    /// Play Mode에서 <c>exec</c>이 컴파일 지연으로 멈춰 검증 진입점이 필요해 여기에 두었습니다.
    /// </remarks>
    [UnityCliTool(Name = "gz_dump", Group = "GrayZone",
        Description = "GrayZone 런타임/씬 상태를 한 번에 덤프합니다. what: combat | sceneui | go | revive | anim.")]
    public static class GZDebugDump
    {
        public class Parameters
        {
            [ToolParameter("덤프 종류: combat | sceneui | go | revive | anim", Required = true)]
            public string What { get; set; }

            [ToolParameter("what=go 일 때 조회할 GameObject 이름(부분 일치). what=anim 에서는 대상 필터로 쓰입니다.")]
            public string Name { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            var p = new ToolParams(@params ?? new JObject());
            string what = p.Get("what", "").ToLowerInvariant();

            switch (what)
            {
                case "combat": return DumpCombat();
                case "sceneui": return DumpSceneUI();
                case "go": return DumpGameObject(p.Get("name", ""));
                case "revive": return DumpRevive();
                case "anim": return DumpAnimator(p.Get("name", ""));
                case "ragdoll": return DumpRagdoll(p.Get("name", ""));
                case "killenemy": return KillEnemy(p.Get("name", ""));
                case "noise": return DumpNoise(p.Get("name", ""));
                default:
                    return new ErrorResponse(
                        "what 파라미터가 필요합니다: combat | sceneui | go | revive | anim | ragdoll | killenemy | noise");
            }
        }

        // ── combat ───────────────────────────────────────────────────────────
        private static object DumpCombat()
        {
            var members = new List<object>();
            foreach (var m in Object.FindObjectsByType<SquadMemberController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var tpc = m.GetComponent<ThirdPersonController>();
                var aim = m.GetComponent<AimController>();
                var wc = m.GetComponentInChildren<Gun>();
                var inp = m.GetComponent<PlayerInputController>();
                var ai = m.GetComponent<SquadAIController>();
                var tg = ai != null ? ai.Targeting : null;

                members.Add(new
                {
                    name = m.MemberName,
                    go = m.name,
                    playerSquadMember = m.IsPlayerSquadMember,
                    alive = m.IsAlive,
                    down = m.IsDown,
                    tpc = tpc == null ? null : new
                    {
                        enabled = tpc.enabled,
                        isReload = tpc.IsReload,
                        isCombatStance = tpc.IsCombatStance,
                        viewContext = tpc.CurrentViewContext.ToString(),
                        viewMode = tpc.ViewMode.ToString(),
                    },
                    weapon = wc == null ? null : new
                    {
                        enabled = wc.enabled,
                        isReloading = wc.IsReloading,
                        canShoot = wc.CanShoot,
                        canReload = wc.CanReload,
                        allowFullMagReload = wc.AllowFullMagReload,
                        bullet = wc.CurrentBullet,
                        maxBullet = wc.MaxBullet,
                    },
                    aim = aim == null ? null : new
                    {
                        enabled = aim.enabled,
                        inCombatStance = GetPrivate<bool>(aim, "m_inCombatStance"),
                        isAds = GetPrivate<bool>(aim, "m_isAds"),
                        hipfireTimer = GetPrivate<float>(aim, "m_hipfireTimer"),
                    },
                    input = inp == null ? null : new
                    {
                        enabled = inp.enabled,
                        aim = inp.Aim,
                        shoot = inp.Shoot,
                        reload = inp.Reload,
                    },
                    // AI 개인 대상 판단(§9). 유예 중이면 겨누되 쏘지 않는 상태입니다.
                    targeting = tg == null ? null : new
                    {
                        aiEnabled = ai.enabled,
                        target = tg.CurrentTarget != null ? tg.CurrentTarget.name : null,
                        holdingLost = tg.IsHoldingLostTarget,
                        graceLeft = tg.GraceRemaining.ToString("F1"),
                        canFire = tg.CanFireAtCurrentTarget,
                        aiming = ai.TryGetCurrentAimPoint(out Vector3 aimPt),
                        aimPoint = aimPt.ToString("F1"),
                        aimAngleError = ai.AimAngleError.ToString("F1"),
                        aimAligned = ai.IsAimAligned,
                        bursting = ai.IsBursting,
                        allowFiring = ai.AllowFiring,
                    },
                    // AI 행동 판정(§18.1). step은 이 결정을 확정한 문서 우선순위 번호입니다.
                    // 컴포넌트가 꺼져 있으면 Update가 돌지 않아 판정도 없습니다. 그때 마지막 값을 그대로
                    // 내보내면 조작 멤버가 판정을 내린 것처럼 읽히므로 null로 비웁니다.
                    decision = (ai == null || !ai.enabled) ? null : new
                    {
                        kind = ai.CurrentDecision.Kind.ToString(),
                        step = ai.CurrentDecision.Step.ToString(),
                        aim = ai.CurrentDecision.Aim,
                        fire = ai.CurrentDecision.Fire,
                        sprint = ai.CurrentDecision.Sprint,
                        reload = ai.CurrentDecision.Reload.ToString(),
                        holdPosition = ai.CurrentDecision.HoldPosition,
                        joining = ai.IsJoining,
                        repositioning = ai.IsRepositioning,
                        // 자동 구조(§16). 대상과 기립 홀드 진행도입니다.
                        rescueTarget = ai.RescueTarget != null ? ai.RescueTarget.name : null,
                        rescueHold = ai.RescueHoldProgress01.ToString("F2"),
                    },
                });
            }

            // 스쿼드 공유 전투 상태(§4.2). 교전 중인 변이체가 하나라도 있으면 전투입니다.
            var sm = SquadManager.Instance;
            var engagement = sm != null ? sm.Engagement : null;
            // 스쿼드 공용 적 정보(§8). 교전 적별로 지금 실시간 위치를 아는지와 마지막 확인 위치를 봅니다.
            var intel = sm != null ? sm.EnemyIntel : null;
            var intelRows = new List<object>();
            if (intel != null)
            {
                foreach (var e in intel.All)
                {
                    intelRows.Add(new
                    {
                        enemy = e.Enemy != null ? e.Enemy.name : "(destroyed)",
                        live = e.HasLivePosition,
                        knownPos = e.KnownPosition.ToString("F1"),
                        lastKnownAge = e.LastKnownTime > 0f ? (Time.time - e.LastKnownTime).ToString("F1") : "-",
                        attackerHold = Mathf.Max(0f, e.AttackerHoldExpireTime - Time.time).ToString("F1"),
                    });
                }
            }

            var squad = new
            {
                inCombat = engagement != null ? (bool?)engagement.IsInCombat : null,
                engagedEnemyCount = engagement != null ? (int?)engagement.EngagedEnemyCount : null,
                playerSquadMemberIndex = sm != null ? (int?)sm.PlayerSquadMemberIndex : null,
                intelTracked = intel != null ? (int?)intel.TrackedCount : null,
                intelLive = intel != null ? (int?)intel.LiveCount : null,
                intel = intelRows,
            };

            return new SuccessResponse(
                $"combat: {members.Count} member(s), inCombat={squad.inCombat}, engaged={squad.engagedEnemyCount}.",
                new { squad = squad, members = members });
        }

        // ── sceneui ──────────────────────────────────────────────────────────
        private static object DumpSceneUI()
        {
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(c => c.gameObject.activeInHierarchy)
                .Select(c => (object)new { name = c.name, renderMode = c.renderMode.ToString(), path = Path(c.transform) })
                .ToList();

            var documents = Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(d => d.gameObject.activeInHierarchy)
                .Select(d => (object)new { name = d.name, panel = d.panelSettings != null ? d.panelSettings.name : null, path = Path(d.transform) })
                .ToList();

            // Image: 특히 type=Filled + fillAmount 이 "부분 링/호" 의 정체일 가능성이 큽니다.
            var images = Object.FindObjectsByType<UIImage>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(i => i.gameObject.activeInHierarchy)
                .Select(i => (object)new
                {
                    name = i.name,
                    sprite = i.sprite != null ? i.sprite.name : null,
                    type = i.type.ToString(),
                    fillMethod = i.type == UIImage.Type.Filled ? i.fillMethod.ToString() : null,
                    fillAmount = i.type == UIImage.Type.Filled ? (float?)i.fillAmount : null,
                    color = ColorHex(i.color),
                    path = Path(i.transform),
                })
                .ToList();

            var sprites = Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(s => s.gameObject.activeInHierarchy)
                .Select(s => (object)new { name = s.name, sprite = s.sprite != null ? s.sprite.name : null, path = Path(s.transform) })
                .ToList();

            return new SuccessResponse(
                $"sceneui: canvases={canvases.Count} docs={documents.Count} images={images.Count} sprites={sprites.Count}.",
                new { canvases, documents, images, sprites });
        }

        // ── go ───────────────────────────────────────────────────────────────
        private static object DumpGameObject(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return new ErrorResponse("go 조회에는 name 파라미터가 필요합니다.");

            var matches = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(t => t.name.IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0)
                .Take(20)
                .Select(t => (object)new
                {
                    name = t.name,
                    path = Path(t),
                    activeSelf = t.gameObject.activeSelf,
                    activeInHierarchy = t.gameObject.activeInHierarchy,
                    position = new { t.position.x, t.position.y, t.position.z },
                    components = t.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name).ToArray(),
                })
                .ToList();

            return new SuccessResponse($"go '{name}': {matches.Count} match(es).", matches);
        }

        // ── revive ───────────────────────────────────────────────────────────
        private static object DumpRevive()
        {
            var sm = Object.FindFirstObjectByType<SquadManager>();
            var cam = Camera.main;

            SquadMemberController playerSquadMember = null;
            foreach (var m in Object.FindObjectsByType<SquadMemberController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (m.IsPlayerSquadMember) { playerSquadMember = m; break; }
            }

            var ic = playerSquadMember != null ? playerSquadMember.GetComponent<InteractionController>() : null;
            var pud = playerSquadMember != null ? playerSquadMember.GetComponent<PlayerbleUnitData>() : null;
            Vector3 origin = playerSquadMember != null ? playerSquadMember.transform.position : Vector3.zero;
            Vector3 facing = cam != null ? cam.transform.forward : Vector3.forward;

            var targets = new List<object>();
            foreach (var dai in Object.FindObjectsByType<DownedAllyInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var member = dai.TargetMember;
                var health = dai.TargetHealth;
                Vector3 to = dai.transform.position - origin;
                float dist = to.magnitude;
                float angle = to.sqrMagnitude > 0.0001f ? Vector3.Angle(facing, to) : -1f;
                var col = dai.GetComponentInChildren<Collider>();

                targets.Add(new
                {
                    go = dai.name,
                    canInteract = playerSquadMember != null && dai.CanInteract(playerSquadMember.gameObject),
                    holdDuration = dai.HoldDuration,
                    holdActive = dai.IsReviveHoldActive,
                    holdProgress = dai.ReviveHoldProgress01,
                    isSelf = playerSquadMember != null && member == playerSquadMember,
                    memberIsAlive = member != null && member.IsAlive,
                    memberIsDown = member != null && member.IsDown,
                    healthIsDowned = health != null && health.IsDowned,
                    healthCurrentHp = health != null ? health.CurrentHP : -1,
                    healthIsDead = health != null && health.IsDead,
                    distance = dist,
                    angleFromCamera = angle,
                    colliderEnabled = col != null && col.enabled,
                    colliderType = col != null ? col.GetType().Name : "none",
                });
            }

            return new SuccessResponse("revive diagnostic", new
            {
                playerSquadMember = playerSquadMember != null ? playerSquadMember.name : "none",
                interactPressed = playerSquadMember != null && playerSquadMember.GetComponent<PlayerInputController>() != null && playerSquadMember.GetComponent<PlayerInputController>().Interact,
                holdProgress01 = ic != null ? ic.HoldProgress01 : -1f,
                interactionRadius = ic != null ? GetPrivate<float>(ic, "m_radius") : -1f,
                interactionMaxAngle = ic != null ? GetPrivate<float>(ic, "m_maxAngle") : -1f,
                currentTarget = ic != null && ic.Current is Component cc ? cc.name : "null",
                hasReviveTarget = pud != null && pud.HasReviveInteractionTarget,
                playerSquadMemberData = sm != null && sm.PlayerSquadMemberData != null ? sm.PlayerSquadMemberData.name : "none",
                cameraForward = cam != null ? facing.ToString("F2") : "noCam",
                targetCount = targets.Count,
                targets,
            });
        }

        // ── anim ─────────────────────────────────────────────────────────────
        /// <summary>
        /// Animator의 레이어별 현재/다음 스테이트와 전이 진행도, 파라미터 값을 덤프합니다.
        /// </summary>
        /// <param name="nameFilter">비면 스쿼드 멤버가 붙은 오브젝트만, 값이 있으면 이름 부분 일치로 고릅니다.</param>
        /// <remarks>
        /// 런타임 <c>AnimatorStateInfo</c>는 스테이트 해시만 주므로 이름을 알 수 없습니다.
        /// 에디터 전용 코드이므로 <see cref="AnimatorController"/>에서 해시→이름 표를 만들어 이름까지 붙입니다.
        /// <c>isInTransition</c>이 계속 true이고 다음 스테이트가 현재와 같으면 조건 없는 자기 전이가
        /// 매 프레임 재시작되고 있다는 신호입니다.
        /// </remarks>
        private static object DumpAnimator(string nameFilter)
        {
            bool useFilter = !string.IsNullOrWhiteSpace(nameFilter);

            var animators = Object.FindObjectsByType<Animator>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(a => useFilter
                    ? a.name.IndexOf(nameFilter, System.StringComparison.OrdinalIgnoreCase) >= 0
                    : a.GetComponentInParent<SquadMemberController>() != null)
                .Take(8)
                .ToList();

            var dumps = new List<object>();
            foreach (var animator in animators)
            {
                var nameByHash = BuildStateNameMap(animator);

                var layers = new List<object>();
                for (int i = 0; i < animator.layerCount; i++)
                {
                    AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(i);
                    bool inTransition = animator.IsInTransition(i);
                    AnimatorStateInfo next = inTransition ? animator.GetNextAnimatorStateInfo(i) : default;

                    layers.Add(new
                    {
                        layer = animator.GetLayerName(i),
                        weight = animator.GetLayerWeight(i),
                        currentState = StateLabel(current, nameByHash),
                        currentNormalizedTime = current.normalizedTime,
                        currentLength = current.length,
                        isInTransition = inTransition,
                        nextState = inTransition ? StateLabel(next, nameByHash) : null,
                        nextNormalizedTime = inTransition ? (float?)next.normalizedTime : null,
                        transitionProgress = inTransition
                            ? (float?)animator.GetAnimatorTransitionInfo(i).normalizedTime
                            : null,
                        clips = animator.GetCurrentAnimatorClipInfo(i)
                            .Select(c => (object)new { clip = c.clip != null ? c.clip.name : null, weight = c.weight })
                            .ToArray(),
                    });
                }

                var parameters = new List<object>();
                foreach (var parameter in animator.parameters)
                {
                    object value = parameter.type switch
                    {
                        AnimatorControllerParameterType.Bool => animator.GetBool(parameter.nameHash),
                        AnimatorControllerParameterType.Trigger => animator.GetBool(parameter.nameHash),
                        AnimatorControllerParameterType.Int => animator.GetInteger(parameter.nameHash),
                        AnimatorControllerParameterType.Float => animator.GetFloat(parameter.nameHash),
                        _ => null,
                    };

                    parameters.Add(new { name = parameter.name, type = parameter.type.ToString(), value });
                }

                dumps.Add(new
                {
                    go = animator.name,
                    path = Path(animator.transform),
                    enabled = animator.enabled,
                    controller = animator.runtimeAnimatorController != null
                        ? animator.runtimeAnimatorController.name
                        : null,
                    applyRootMotion = animator.applyRootMotion,
                    layers,
                    parameters,
                });
            }

            return new SuccessResponse(
                $"anim: {dumps.Count} animator(s){(useFilter ? $" matching '{nameFilter}'" : " on squad members")}.",
                dumps);
        }

        // ── ragdoll ──────────────────────────────────────────────────────────
        /// <summary>
        /// 변이체의 래그돌 전환 상태를 덤프합니다. 즉시 래그돌 사망 연출 검증용입니다.
        /// </summary>
        /// <param name="nameFilter">대상 GameObject 이름 필터입니다. 비우면 전부 봅니다.</param>
        /// <remarks>
        /// 확인 대상은 세 가지입니다. Animator가 꺼졌는지(래그돌이 주도권을 가졌는지),
        /// 뼈가 동역학으로 전환됐는지(<c>isKinematic</c>), 그리고 속도 인계가 실제로 들어갔는지입니다.
        /// 인계가 실패하면 전환 직후 속도가 모두 0으로 찍힙니다.
        /// </remarks>
        private static object DumpRagdoll(string nameFilter)
        {
            bool useFilter = !string.IsNullOrWhiteSpace(nameFilter);

            var dumps = new List<object>();
            foreach (var controller in Object.FindObjectsByType<EnemyController>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (useFilter && !controller.name.Contains(nameFilter))
                {
                    continue;
                }

                var ragdoll = controller.GetComponent<RagdollController>();
                var animator = controller.GetComponentInChildren<Animator>(true);
                var bodies = controller.GetComponentsInChildren<Rigidbody>(true);
                bool visible = controller.GetComponentsInChildren<Renderer>(true).Any(r => r.isVisible);

                int kinematic = 0;
                float maxSpeed = 0.0f;
                float maxAngular = 0.0f;
                foreach (var body in bodies)
                {
                    if (body.isKinematic)
                    {
                        kinematic++;
                        continue;
                    }

                    maxSpeed = Mathf.Max(maxSpeed, body.linearVelocity.magnitude);
                    maxAngular = Mathf.Max(maxAngular, body.angularVelocity.magnitude);
                }

                dumps.Add(new
                {
                    go = controller.name,
                    enemyType = controller.EnemyType.ToString(),
                    hp = controller.CurrentHP,
                    state = controller.Current != null ? controller.Current.GetType().Name : null,
                    ragdollConfigured = ragdoll != null && ragdoll.IsConfigured,
                    ragdollActive = ragdoll != null && ragdoll.IsRagdollActive,
                    animatorEnabled = animator != null && animator.enabled,
                    animatorCulling = animator != null ? animator.cullingMode.ToString() : null,
                    rendererVisible = visible,
                    boneCount = bodies.Length,
                    kinematicBones = kinematic,
                    maxBoneSpeed = maxSpeed,
                    maxBoneAngularSpeed = maxAngular,
                    measuredMaxBoneSpeed = ragdoll != null ? ragdoll.MeasuredMaxBoneSpeed : 0.0f,
                    measuredMaxBoneAngularSpeed = ragdoll != null ? ragdoll.MeasuredMaxBoneAngularSpeed : 0.0f,
                    rootY = controller.transform.position.y,
                });
            }

            return new SuccessResponse($"ragdoll: {dumps.Count} enemy(s).", dumps);
        }

        // ── noise ────────────────────────────────────────────────────────────
        /// <summary>
        /// 변이체별 소음 감지 상태와 차폐 판정 결과를 덤프합니다.
        /// </summary>
        /// <param name="nameFilter">대상 GameObject 이름 필터입니다. 비우면 전부 덤프합니다.</param>
        /// <remarks>
        /// 차폐가 실제로 걸렸는지는 콘솔로 알 수 없습니다. 소음이 조용히 약해지는 것이 전부라
        /// 에러도 경고도 나지 않습니다. 그래서 통과 비율과 인지 게이지를 함께 봐야 판별됩니다.
        ///
        /// <c>transmission</c>은 "직전에 판정한 소음"의 값입니다. 지금 추적 중인 소음의 것이 아닙니다.
        /// 여러 소음이 섞이는 상황에서는 마지막 하나만 보이므로, 총성 한 발씩 끊어 확인하는 편이 낫습니다.
        /// </remarks>
        private static object DumpNoise(string nameFilter)
        {
            bool useFilter = !string.IsNullOrWhiteSpace(nameFilter);

            var noiseManager = FieldManager.Instance != null ? FieldManager.Instance.NoiseManager : null;

            var dumps = new List<object>();
            foreach (var controller in Object.FindObjectsByType<EnemyController>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (useFilter && !controller.name.Contains(nameFilter))
                {
                    continue;
                }

                var sensor = controller.Sensor;
                if (sensor == null)
                {
                    continue;
                }

                bool hasNoise = sensor.TryGetNoisePosition(out Vector3 noisePosition);

                dumps.Add(new
                {
                    go = controller.name,
                    state = controller.Current != null ? controller.Current.GetType().Name : null,
                    hasNoise,
                    noisePos = hasNoise ? $"{noisePosition.x:F2},{noisePosition.y:F2},{noisePosition.z:F2}" : null,
                    transmission = sensor.LastNoiseTransmission,
                    awareness = sensor.NoiseAwareness,
                    awareness01 = sensor.NoiseAwareness01,
                    awarenessFull = sensor.IsNoiseAwarenessFull,
                    alert = sensor.IsNoiseAlert,
                });
            }

            var table = noiseManager != null ? noiseManager.OcclusionTable : null;

            return new SuccessResponse(
                $"noise: {dumps.Count} enemy(s), noiseManager={(noiseManager != null ? "present" : "MISSING")}, " +
                $"table={(table != null ? table.name : "MISSING")}, " +
                $"defaultOcclusion={(table != null ? table.DefaultOcclusion.ToString("F2") : "-")}, " +
                $"occluderMask={(noiseManager != null ? noiseManager.OccluderMask.ToString() : "-")}, " +
                $"tags={Object.FindObjectsByType<SurfaceMaterialTag>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length}, " +
                $"listeners={NoiseSystem.ListenerCount}.",
                dumps);
        }

        // ── killenemy ────────────────────────────────────────────────────────
        /// <summary>
        /// 변이체 하나를 즉사시켜 사망 연출을 검증할 수 있게 합니다.
        /// </summary>
        /// <param name="nameFilter">대상 GameObject 이름 필터입니다. 비우면 첫 생존 개체를 씁니다.</param>
        /// <remarks>
        /// 정상 피해 경로(<c>TakeDamage</c>)를 그대로 타므로 사망 이벤트 체인이 실제와 같습니다.
        /// Play Mode에서 <c>exec</c>이 컴파일 지연으로 멈추기 때문에 검증용 진입점을 여기에 둡니다.
        /// </remarks>
        private static object KillEnemy(string nameFilter)
        {
            bool useFilter = !string.IsNullOrWhiteSpace(nameFilter);

            foreach (var controller in Object.FindObjectsByType<EnemyController>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (useFilter && !controller.name.Contains(nameFilter))
                {
                    continue;
                }

                var health = controller.Health;
                if (health == null || controller.CurrentHP <= 0)
                {
                    continue;
                }

                health.TakeDamage(int.MaxValue);
                return new SuccessResponse(
                    $"killenemy: '{controller.name}' 처치 요청 완료.",
                    new
                    {
                        go = controller.name,
                        hp = controller.CurrentHP,
                        state = controller.Current != null ? controller.Current.GetType().Name : null,
                    });
            }

            return new ErrorResponse("처치할 생존 변이체를 찾지 못했습니다.");
        }

        /// <summary>스테이트 해시를 사람이 읽을 이름으로 바꿉니다. 표에 없으면 해시를 그대로 씁니다.</summary>
        private static string StateLabel(AnimatorStateInfo info, Dictionary<int, string> nameByHash)
        {
            return nameByHash.TryGetValue(info.shortNameHash, out string name)
                ? name
                : $"#{info.shortNameHash}";
        }

        /// <summary>
        /// AnimatorController를 훑어 스테이트 해시→이름 표를 만듭니다.
        /// </summary>
        /// <remarks>하위 스테이트 머신도 재귀로 포함합니다. Override 컨트롤러면 원본 컨트롤러를 따라갑니다.</remarks>
        private static Dictionary<int, string> BuildStateNameMap(Animator animator)
        {
            var map = new Dictionary<int, string>();

            RuntimeAnimatorController runtime = animator.runtimeAnimatorController;
            if (runtime is AnimatorOverrideController overrideController)
            {
                runtime = overrideController.runtimeAnimatorController;
            }

            if (runtime is not AnimatorController controller)
            {
                return map;
            }

            foreach (var layer in controller.layers)
            {
                CollectStateNames(layer.stateMachine, map);
            }

            return map;
        }

        private static void CollectStateNames(AnimatorStateMachine machine, Dictionary<int, string> map)
        {
            if (machine == null)
            {
                return;
            }

            foreach (var child in machine.states)
            {
                if (child.state != null)
                {
                    map[child.state.nameHash] = child.state.name;
                }
            }

            foreach (var child in machine.stateMachines)
            {
                CollectStateNames(child.stateMachine, map);
            }
        }

        // ── helpers ──────────────────────────────────────────────────────────
        private static T GetPrivate<T>(object target, string field)
        {
            var fi = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            return fi != null && fi.GetValue(target) is T v ? v : default;
        }

        private static string Path(Transform t)
        {
            var s = t.name;
            while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
            return s;
        }

        private static string ColorHex(Color c) => "#" + ColorUtility.ToHtmlStringRGBA(c);
    }
}
#endif
