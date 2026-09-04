using Unity.Mathematics;

namespace Cs2Saver
{
    /// <summary>
    /// How aggressively each category of thing stops being drawn.
    ///
    /// The game stores a per-entity <c>CullingInfo.m_MinLod</c> byte and keeps an entity only
    /// while <c>CalculateMaxLod(bounds, camera) &gt;= m_MinLod</c>. The LOD value it compares
    /// against falls as distance grows, so a HIGHER floor culls SOONER.
    ///
    /// The scale is logarithmic, and <c>RenderingUtils.CalculateDistanceFactor</c> pins it down
    /// exactly:
    /// <code>
    ///     distanceFactor(lod) = pow(2, (128 - lod) / 6)
    /// </code>
    /// so <b>every +6 on the floor halves the distance at which something disappears</b>, and
    /// +12 quarters it. That is the whole model; the numbers below are just choices along it.
    /// </summary>
    public struct RenderBudget
    {
        /// <summary>Floor applied to pedestrians and animals. 0 leaves the game's own value alone.</summary>
        public byte Citizens;

        /// <summary>Floor applied to vehicles.</summary>
        public byte Vehicles;

        /// <summary>Off. Every category keeps whatever the game decided.</summary>
        public static RenderBudget Off => new RenderBudget { Citizens = 0, Vehicles = 0 };

        /// <summary>
        /// Citizens fade out noticeably earlier; vehicles are left alone, because on a
        /// traffic-focused save the cars ARE the thing being looked at.
        /// </summary>
        public static RenderBudget Balanced => new RenderBudget { Citizens = 96, Vehicles = 0 };

        /// <summary>
        /// Citizens become a close-range detail only. Vehicles pull in somewhat.
        /// This is the "I want the city, not the people" setting.
        /// </summary>
        public static RenderBudget TrafficFocus => new RenderBudget { Citizens = 112, Vehicles = 88 };

        /// <summary>Everything animate is drawn only very close to the camera.</summary>
        public static RenderBudget Aggressive => new RenderBudget { Citizens = 128, Vehicles = 104 };

        /// <summary>
        /// Turns a floor into the roughly equivalent distance multiplier relative to another
        /// floor, purely so a UI can say "about half as far" instead of showing a raw byte.
        /// </summary>
        public static float DistanceRatio(byte from, byte to) => math.pow(2f, (from - to) / 6f);
    }
}
