using Game;
using Game.Prefabs;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Mathematics;

namespace Cs2Saver
{
    /// <summary>
    /// Raises the LOD floor on creature <b>prefabs</b>, which is the durable half of the job.
    ///
    /// <see cref="RenderBudgetSystem"/> writes <c>CullingInfo.m_MinLod</c> on live entities, but
    /// PreCullingSystem re-seeds that field from the prefab whenever an entity is marked
    /// <c>Updated</c>:
    /// <code>
    ///     if (m_UpdateAll || chunk.Has(m_UpdatedType))
    ///         reference.m_MinLod = (byte)objectGeometryData.m_MinLod;
    /// </code>
    /// Citizens move constantly, so relying on the entity write alone risks being overwritten as
    /// fast as it is applied. Raising the value at the source means every re-seed hands back the
    /// floor rather than undoing it — and there are a few dozen creature prefabs against many
    /// thousands of live citizens, so it is far cheaper too.
    ///
    /// The two systems are complementary rather than redundant: this one governs everything the
    /// game re-derives, while the entity system catches citizens that already exist and are not
    /// re-seeded for a while.
    ///
    /// <para><b>Saves are not at risk.</b> <c>ObjectGeometryData.Serialize</c> does not write
    /// <c>m_MinLod</c> — the field is derived during prefab initialisation and never persisted,
    /// so changing it cannot travel into a save file.</para>
    /// </summary>
    public partial class PrefabLodFloorSystem : GameSystemBase
    {
        private EntityQuery m_CreaturePrefabQuery;
        private ComponentTypeHandle<ObjectGeometryData> m_GeometryType;

        /// <summary>Floor applied to creature prefabs. 0 leaves the game's own values alone.</summary>
        public byte Floor { get; set; }

        protected override void OnCreate()
        {
            base.OnCreate();

            // CreatureData sits on the prefab entity (CreaturePrefab.GetPrefabComponents),
            // whereas Creature sits on the instances (GetArchetypeComponents).
            m_CreaturePrefabQuery = GetEntityQuery(
                ComponentType.ReadWrite<ObjectGeometryData>(),
                ComponentType.ReadOnly<CreatureData>());

            m_GeometryType = GetComponentTypeHandle<ObjectGeometryData>();

            RequireForUpdate(m_CreaturePrefabQuery);
        }

        protected override void OnUpdate()
        {
            if (Floor == 0) return;

            // Deliberately unconditional. Caching "already applied" would miss prefabs loaded
            // later, and there are a few dozen creature prefabs rather than thousands, so the
            // job costs nothing worth the extra state and the bug it invites.
            m_GeometryType.Update(this);

            Dependency = new RaisePrefabFloorJob
            {
                m_GeometryType = m_GeometryType,
                m_Floor = Floor,
            }.ScheduleParallel(m_CreaturePrefabQuery, Dependency);
        }

        [BurstCompile]
        private struct RaisePrefabFloorJob : IJobChunk
        {
            public ComponentTypeHandle<ObjectGeometryData> m_GeometryType;
            public byte m_Floor;

            public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex,
                bool useEnabledMask, in v128 chunkEnabledMask)
            {
                var geometry = chunk.GetNativeArray(ref m_GeometryType);

                for (var i = 0; i < geometry.Length; i++)
                {
                    var data = geometry[i];

                    // Only ever raise. Never assign, never accumulate — that is what makes
                    // re-running this safe no matter how often it happens.
                    var raised = math.max(data.m_MinLod, (int)m_Floor);
                    if (raised == data.m_MinLod) continue;

                    data.m_MinLod = raised;
                    geometry[i] = data;
                }
            }
        }
    }
}
