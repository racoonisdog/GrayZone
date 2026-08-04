using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 스쿼드 캐릭터에 <see cref="CharacterNoiseEmitter"/>를 붙이는 에디터 도구입니다.
/// </summary>
/// <remarks>
/// 손으로 씬 YAML에 컴포넌트를 적지 않는 이유는 fileID와 스크립트 GUID를 바깥에서 재현해야 하고,
/// 틀리면 "Missing script"가 되기 때문입니다. 에셋 API로 붙이면 유니티가 그 값을 만들어 줍니다.
///
/// 현재 스쿼드 캐릭터는 프리팹 인스턴스가 아니라 씬 전용 오브젝트라 씬을 저장합니다.
/// 나중에 프리팹화하면 이 도구를 프리팹 대상으로 바꿔야 합니다.
///
/// 발신 컴포넌트를 <see cref="SquadMemberController"/>와 같은 오브젝트에 두는 것이 중요합니다.
/// 소음 위치가 이 컴포넌트의 위치이므로 캐릭터 루트여야 하며, 총기가 부모 방향으로 찾아 올라오기 때문입니다.
/// </remarks>
public static class CharacterNoiseEmitterWiring
{
    [MenuItem("GrayZone/Character/소음 발신 컴포넌트 배선")]
    public static void Wire()
    {
        SquadMemberController[] members =
            Object.FindObjectsByType<SquadMemberController>(FindObjectsSortMode.None);

        if (members.Length == 0)
        {
            Debug.LogWarning("[CharacterNoiseEmitterWiring] 씬에서 SquadMemberController를 찾지 못했습니다.");
            return;
        }

        List<string> log = new List<string>();
        int added = 0;

        foreach (SquadMemberController member in members)
        {
            if (member.GetComponent<CharacterNoiseEmitter>() != null)
            {
                log.Add($"{member.name}: 이미 있습니다.");
                continue;
            }

            Undo.AddComponent<CharacterNoiseEmitter>(member.gameObject);
            added++;
            log.Add($"{member.name}: 추가했습니다.");
        }

        if (added > 0)
        {
            EditorSceneManager.MarkSceneDirty(members[0].gameObject.scene);
            EditorSceneManager.SaveScene(members[0].gameObject.scene);
            log.Add($"씬을 저장했습니다: {members[0].gameObject.scene.name}");
        }

        Debug.Log($"[CharacterNoiseEmitterWiring] 대상 {members.Length}개 / 추가 {added}개\n"
                  + string.Join("\n", log));
    }
}
