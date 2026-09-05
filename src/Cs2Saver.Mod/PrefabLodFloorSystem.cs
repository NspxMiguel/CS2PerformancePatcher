using System.Collections.Generic;
using Game;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Cs2Saver
{
    /// <summary>
    /// Pulls in the distance at which each <b>category</b> of prefab stops being drawn, and
    /// leaves buildings alone.
    ///
    /// <para><b>Why per category.</b> The game's <c>levelOfDetail</c> slider scales every LOD
    /// transition at once. Measured on the <c>sharp</c> profile, driving it to its floor was
    /// worth 12ms of a 32.5ms GPU frame, so the geometry is genuinely there to be won — but it
    /// pulls buildings in along with everything else, which is what makes a city look like
    /// modelling clay. Prefabs carry components that say what they are (<c>TreeData</c>,
    /// <c>StaticObjectData</c>, <c>VehicleData</c>, <c>CreatureData</c>, <c>BuildingData</c>),
    /// so the same geometry can be bought back from the trees and the street furniture while the
    /// skyline keeps every triangle. That distinction cannot be expressed as a setting, which is
    /// the reason this mod exists at all.</para>
    ///
    /// <para><b>Why prefabs rather than entities.</b> <c>PreCullingSystem</c> re-seeds
    /// <c>CullingInfo.m_MinLod</c> from the prefab whenever an entity is marked <c>Updated</c>:
    /// <code>
    ///     if (m_UpdateAll || chunk.Has(m_UpdatedType))
    ///         reference.m_MinLod = (byte)objectGeometryData.m_MinLod;
    /// </code>
    /// Writing the prefab means every re-seed hands back the value we want instead of undoing
    /// it, and there are a few thousand prefabs against millions of live instances.</para>
    ///
    /// <para><b>Why the original is remembered.</b> An earlier version only ever raised the
    /// floor, which made it idempotent but also made it a one-way door: turning the mod off left
    /// the city cut until the game was restarted. Keeping each prefab's untouched value means
    /// every pass is computed from the original rather than from the last result, so the work is
    /// idempotent, reversible, and — because the offset is added to a value the game derived from
    /// the object's own size — still relative. A large tree goes on outliving a shrub.</para>
    ///
    /// <para><b>Saves are not at risk.</b> <c>ObjectGeometryData.Serialize</c> does not write
    /// <c>m_MinLod</c> — the field is derived during prefab initialisation and never persisted,
    /// so changing it cannot travel into a save file.</para>
    /// </summary>
    public partial class PrefabLodFloorSystem : GameSystemBase
    {
        private struct Category
        {
            public string Name;
            public EntityQuery Query;
            public byte Halvings;
        }

        private EntityQuery m_Creatures;
        private EntityQuery m_Vehicles;
        private EntityQuery m_Trees;
        private EntityQuery m_Plants;
        private EntityQuery m_Props;
        private EntityQuery m_NetProps;

        /// <summary>
        /// Each prefab's value before this system first touched it. Managed rather than native
        /// on purpose: the pass runs on the main thread only when something changed, so there is
        /// nothing to gain from a job here, and a Dictionary cannot leak a native allocation.
        /// </summary>
        private readonly Dictionary<Entity, int> m_Original = new Dictionary<Entity, int>();

        private RenderBudget m_Budget;
        private bool m_Dirty = true;
        private int m_LastPrefabCount = -1;
        private bool m_Reported;

        /// <summary>Live configuration. Assign from the mod's settings; takes effect next frame.</summary>
        public RenderBudget Budget
        {
            get => m_Budget;
            set
            {
                if (Equal(value, m_Budget)) return;
                m_Budget = value;
                m_Dirty = true;
            }
        }

        private static bool Equal(RenderBudget a, RenderBudget b)
        {
            return a.CitizenHalvings == b.CitizenHalvings
                   && a.VehicleHalvings == b.VehicleHalvings
                   && a.TreeHalvings == b.TreeHalvings
                   && a.PropHalvings == b.PropHalvings;
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            // These sit on the prefab entity, not on the instances: CreaturePrefab puts
            // CreatureData in GetPrefabComponents and Creature in GetArchetypeComponents.
            m_Creatures = Geometry(ComponentType.ReadOnly<CreatureData>());
            m_Vehicles = Geometry(ComponentType.ReadOnly<VehicleData>());
            m_Trees = Geometry(ComponentType.ReadOnly<TreeData>());

            // A tree usually carries PlantData too. Excluding it here keeps the two queries
            // disjoint so nothing is cut twice at two different rates.
            m_Plants = GeometryExcept(
                ComponentType.ReadOnly<PlantData>(),
                ComponentType.ReadOnly<TreeData>());

            // Buildings are excluded here and in the query below, because these are the only two
            // that would otherwise reach them. That exclusion is the whole promise of this mod.
            m_Props = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadWrite<ObjectGeometryData>(),
                    ComponentType.ReadOnly<StaticObjectData>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<BuildingData>(),
                    ComponentType.ReadOnly<TreeData>(),
                    ComponentType.ReadOnly<PlantData>(),
                },
            });

            // Street furniture placed along roads: lamps, signs, barriers. Same rate as props.
            m_NetProps = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadWrite<ObjectGeometryData>(),
                    ComponentType.ReadOnly<NetObjectData>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<BuildingData>(),
                    ComponentType.ReadOnly<TreeData>(),
                    ComponentType.ReadOnly<PlantData>(),
                    ComponentType.ReadOnly<StaticObjectData>(),
                },
            });
        }

        private EntityQuery Geometry(ComponentType marker)
        {
            return GetEntityQuery(ComponentType.ReadWrite<ObjectGeometryData>(), marker);
        }

        private EntityQuery GeometryExcept(ComponentType marker, ComponentType excluded)
        {
            return GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadWrite<ObjectGeometryData>(), marker },
                None = new[] { excluded },
            });
        }

        protected override void OnUpdate()
        {
            // Two reasons to run: the player changed something, or prefabs appeared that this
            // system has never seen. Prefabs are created while a city loads, so counting them is
            // enough to notice, and it costs one integer per query rather than a sweep.
            var count = m_Creatures.CalculateEntityCount() + m_Vehicles.CalculateEntityCount()
                        + m_Trees.CalculateEntityCount() + m_Plants.CalculateEntityCount()
                        + m_Props.CalculateEntityCount() + m_NetProps.CalculateEntityCount();

            if (!m_Dirty && count == m_LastPrefabCount) return;

            m_Dirty = false;
            m_LastPrefabCount = count;

            var categories = new[]
            {
                new Category { Name = "creatures", Query = m_Creatures, Halvings = m_Budget.CitizenHalvings },
                new Category { Name = "vehicles",  Query = m_Vehicles,  Halvings = m_Budget.VehicleHalvings },
                new Category { Name = "trees",     Query = m_Trees,     Halvings = m_Budget.TreeHalvings },
                new Category { Name = "plants",    Query = m_Plants,    Halvings = m_Budget.TreeHalvings },
                new Category { Name = "props",     Query = m_Props,     Halvings = m_Budget.PropHalvings },
                new Category { Name = "net props", Query = m_NetProps,  Halvings = m_Budget.PropHalvings },
            };

            foreach (var category in categories) Apply(category);

            // Once per session. The counts are the only way to know that these component types
            // really do select what their names suggest, rather than quietly matching nothing —
            // which is how the previous version of this mod managed to be a no-op unnoticed.
            if (!m_Reported)
            {
                m_Reported = true;
                foreach (var c in categories)
                {
                    Mod.Log.Info($"LOD floor: {c.Query.CalculateEntityCount()} {c.Name} prefabs"
                                 + $", {c.Halvings} halving(s)");
                }
            }
        }

        private void Apply(Category category)
        {
            if (category.Query.IsEmptyIgnoreFilter) return;

            var entities = category.Query.ToEntityArray(Allocator.Temp);
            var geometry = category.Query.ToComponentDataArray<ObjectGeometryData>(Allocator.Temp);

            try
            {
                var changed = false;

                for (var i = 0; i < geometry.Length; i++)
                {
                    var data = geometry[i];

                    // First sight of this prefab: whatever the game derived from its size is the
                    // value every later pass is measured against.
                    if (!m_Original.TryGetValue(entities[i], out var original))
                    {
                        original = data.m_MinLod;
                        m_Original[entities[i]] = original;
                    }

                    var target = math.min(255, original + RenderBudget.PerHalving * category.Halvings);
                    if (data.m_MinLod == target) continue;

                    data.m_MinLod = target;
                    geometry[i] = data;
                    changed = true;
                }

                // Nothing to write when the budget is already in place, which is the common case
                // on the count-changed path.
                if (changed) category.Query.CopyFromComponentDataArray(geometry);
            }
            finally
            {
                geometry.Dispose();
                entities.Dispose();
            }
        }
    }
}
