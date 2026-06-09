using System.Collections.Generic;

public class NpcRoster
{
    private readonly List<NPCRuntimeData> npcs = new();
    private readonly Dictionary<string, NPCRuntimeData> byRuntimeId = new();

    public IReadOnlyList<NPCRuntimeData> All => npcs;
    public int Count => npcs.Count;

    public bool TryAdd(NPCChar npcData, out NPCRuntimeData runtimeData)
    {
        return TryAdd(npcData, null, out runtimeData);
    }

    public bool TryAdd(NPCChar npcData, string runtimeId, out NPCRuntimeData runtimeData)
    {
        runtimeData = null;

        if (npcData == null)
        {
            return false;
        }

        NPCRuntimeData newRuntimeData = new NPCRuntimeData(npcData, runtimeId);
        if (!Add(newRuntimeData))
        {
            return false;
        }

        runtimeData = newRuntimeData;
        return true;
    }

    public bool Add(NPCRuntimeData runtimeData)
    {
        if (runtimeData == null || string.IsNullOrWhiteSpace(runtimeData.RuntimeId))
        {
            return false;
        }

        string runtimeId = runtimeData.RuntimeId.Trim();
        if (byRuntimeId.ContainsKey(runtimeId))
        {
            return false;
        }

        npcs.Add(runtimeData);
        byRuntimeId[runtimeId] = runtimeData;
        return true;
    }

    public bool Remove(string runtimeId)
    {
        if (!TryGet(runtimeId, out NPCRuntimeData runtimeData))
        {
            return false;
        }

        byRuntimeId.Remove(runtimeData.RuntimeId.Trim());
        npcs.Remove(runtimeData);
        return true;
    }

    public bool Remove(NPCRuntimeData runtimeData)
    {
        if (runtimeData == null)
        {
            return false;
        }

        return Remove(runtimeData.RuntimeId);
    }

    public bool Contains(string runtimeId)
    {
        return !string.IsNullOrWhiteSpace(runtimeId) && byRuntimeId.ContainsKey(runtimeId.Trim());
    }

    public bool TryGet(string runtimeId, out NPCRuntimeData runtimeData)
    {
        runtimeData = null;

        if (string.IsNullOrWhiteSpace(runtimeId))
        {
            return false;
        }

        return byRuntimeId.TryGetValue(runtimeId.Trim(), out runtimeData);
    }

    public bool TryGetFirstByDefinitionId(string definitionId, out NPCRuntimeData runtimeData)
    {
        runtimeData = null;

        if (string.IsNullOrWhiteSpace(definitionId))
        {
            return false;
        }

        string trimmedDefinitionId = definitionId.Trim();
        foreach (NPCRuntimeData npc in npcs)
        {
            if (npc != null && npc.DefinitionId == trimmedDefinitionId)
            {
                runtimeData = npc;
                return true;
            }
        }

        return false;
    }

    public void Clear()
    {
        npcs.Clear();
        byRuntimeId.Clear();
    }
}
