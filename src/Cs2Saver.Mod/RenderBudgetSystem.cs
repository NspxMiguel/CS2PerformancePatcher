using Game;
using Game.Creatures;
using Game.Rendering;
using Game.Vehicles;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Cs2Saver
{
    /// <summary>
    /// Raises the culling floor on things the player does not look closely at, so the renderer
    /// stops submitting them long before the game would have.
    ///
    /// This runs in <see cref="SystemUpdatePhase.PreCulling"/>, immediately before
    /// <c>PreCullingSystem</c> decides what is visible, so a floor written here takes effect on
    /// the same frame.
    ///
    /// The write is <b>idempotent by construction</b>: it only ever raises the floor, never sets
    /// an absolute value and never adds an offset. <c>max(max(x, f), f) == max(x, f)</c>, so
    /// running every frame is stable, and when the game recomputes an entity's culling data from
    /// its prefab the floor is simply re-applied on the next frame. That avoids needing to
    /// remember each entity's original value, which would otherwise mean a structural change per
    /// citizen — expensive on a population that spawns and despawns continuously.
    /// </summary>
    public partial class RenderBudgetSystem : GameSystemBase
    {
        private EntityQuery m_CitizenQuery;
        private EntityQuery m_VehicleQuery;
        private ComponentTypeHandle<CullingInfo> m_CullingInfoType;

        private RenderBudget m_Budget = RenderBudget.Off;
        private bool m_Dirty = true;

        /// <summary>Live configuration. Assign from the mod's settings; takes effect next frame.</summary>
        public RenderBudget Budget
        {
            get => m_Budget;
            set
            {
                if (value.Citizens == m_Budget.Citizens && value.Vehicles == m_Budget.Vehicles) return;
                m_Budget = value;
                m_Dirty = true;
            }
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            // Creature covers pedestrians, animals and pets — everything that walks.
            m_CitizenQuery = GetEntityQuery(
                ComponentType.ReadWrite<CullingInfo>(),
                ComponentType.ReadOnly<Creature>());

            m_VehicleQuery = GetEntityQuery(
                ComponentType.ReadWrite<CullingInfo>(),
                ComponentType.ReadOnly<Vehicle>());

            m_CullingInfoType = GetComponentTypeHandle<CullingInfo>();

            RequireAnyForUpdate(m_CitizenQuery, m_VehicleQuery);
        }

        protected override void OnUpdate()
        {
            // Sweeping every pedestrian and vehicle each frame was measured costing 5ms of CPU
            // to save under 1ms of GPU — a net loss large enough to erase the whole point.
            //
            // It is also unnecessary. The game refills CullingInfo.m_MinLod from
            // ObjectGeometryData.m_MinLod whenever it rebuilds an entity, and
            // PrefabLodFloorSystem has already raised the floor on the prefab, so anything
            // spawned or refreshed from here on inherits it for free. The only entities this
            // pass exists for are the ones already alive when the budget changed, and one
            // sweep catches all of them.
            if (!m_Dirty) return;

            var budget = m_Budget;
            m_Dirty = false;

            if (budget.Citizens == 0 && budget.Vehicles == 0) return;

            m_CullingInfoType.Update(this);

            var handle = Dependency;

            if (budget.Citizens > 0)
            {
                handle = new RaiseCullingFloorJob
                {
                    m_CullingInfoType = m_CullingInfoType,
                    m_Floor = budget.Citizens,
                }.ScheduleParallel(m_CitizenQuery, handle);
            }

            if (budget.Vehicles > 0)
            {
                handle = new RaiseCullingFloorJob
                {
                    m_CullingInfoType = m_CullingInfoType,
                    m_Floor = budget.Vehicles,
                }.ScheduleParallel(m_VehicleQuery, handle);
            }

            Dependency = handle;
        }

        [BurstCompile]
        private struct RaiseCullingFloorJob : IJobChunk
        {
            public ComponentTypeHandle<CullingInfo> m_CullingInfoType;
            public byte m_Floor;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex,
                bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var infos = chunk.GetNativeArray(ref m_CullingInfoType);

                for (var i = 0; i < infos.Length; i++)
                {
                    var info = infos[i];
                    // Explicit int casts: byte promotes to both int and uint2 here, which makes
                    // the math.max overload ambiguous under Unity.Mathematics.
                    var raised = (byte)math.max((int)info.m_MinLod, (int)m_Floor);

                    // Skip the write when nothing changes: chunks of already-floored entities
                    // are the common case once a frame or two has passed.
                    if (raised == info.m_MinLod) continue;

                    info.m_MinLod = raised;
                    infos[i] = info;
                }
            }
        }
    }
}
