using UnityEngine;
using System.Collections.Generic;

public class WanderManager : MonoBehaviour
{
    [Header("웨이포인트 (씬에 배치한 빈 오브젝트들)")]
    public Transform[] waypoints;

    
    public int agentsPerFrame = 6;

    private static WanderManager instance;
    private static readonly List<WanderAgent> agents = new();
    public static IReadOnlyList<WanderAgent> Agents => agents;

    private int cursor;

    void Awake()
    {
        instance = this;
    }

    void Update()
    {
        int total = agents.Count;
        if (total == 0) return;

        float dt = Time.deltaTime;
        int count = Mathf.Min(agentsPerFrame, total);

        for (int i = 0; i < count; i++)
        {
            var agent = agents[(cursor + i) % total];
            if (agent != null && agent.isActiveAndEnabled)
                agent.ManagedUpdate(dt);
        }

        cursor = (cursor + count) % total;
    }

    public static void Register(WanderAgent agent)
    {
        if (agents.Contains(agent)) return;
        agents.Add(agent);
        if (instance != null)
            agent.SetWaypoint(instance.PickWaypoint(null));
    }

    public static void Unregister(WanderAgent agent)
    {
        agents.Remove(agent);
    }

    public static Transform PickRandomWaypoint(Transform exclude)
    {
        return instance?.PickWaypoint(exclude);
    }

    Transform PickWaypoint(Transform exclude)
    {
        if (waypoints == null || waypoints.Length == 0) return null;
        if (waypoints.Length == 1) return waypoints[0];

        Transform picked;
        int tries = 0;
        do
        {
            picked = waypoints[Random.Range(0, waypoints.Length)];
            tries++;
        }
        while (picked == exclude && tries < 10);

        return picked;
    }
}
