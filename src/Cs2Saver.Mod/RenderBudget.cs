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

        /// <summary>
        /// What the game already assigns, so the presets below can be expressed as offsets from
        /// something real rather than picked out of the air.
        ///
        /// The seed is <c>CalculateLodLimit(GetRenderingSize(size))</c>, and because rendering
        /// size is the mean of the bounds, a smaller object is given a HIGHER floor — small
        /// things are already meant to vanish sooner. Evaluating that for typical dimensions:
        /// <code>
        ///     citizen  0.5 x 1.8 x 0.5   ->  129
        ///     car      2.0 x 1.5 x 4.5   ->  120
        ///     house     10 x 8 x 10      ->  109
        ///     tower     30 x 60 x 30     ->   97
        /// </code>
        /// This matters more than it looks: the job only ever raises the floor, so any preset
        /// value below these does precisely nothing. An earlier version of this file used
        /// 96/112/128 for citizens and was therefore a complete no-op.
        /// </summary>
        public const byte CitizenSeed = 129;

        /// <inheritdoc cref="CitizenSeed"/>
        public const byte VehicleSeed = 120;

        /// <summary>Off. Every category keeps whatever the game decided.</summary>
        public static RenderBudget Off => new RenderBudget { Citizens = 0, Vehicles = 0 };

        /// <summary>
        /// Citizens disappear at half their usual distance; vehicles are untouched, because on a
        /// traffic-focused save the cars ARE the thing being looked at.
        /// </summary>
        public static RenderBudget Balanced => new RenderBudget
        {
            Citizens = CitizenSeed + 6, // half distance
            Vehicles = 0,
        };

        /// <summary>
        /// Citizens at a quarter of their usual distance, vehicles at half.
        /// The "I want the city, not the people" setting.
        /// </summary>
        public static RenderBudget TrafficFocus => new RenderBudget
        {
            Citizens = CitizenSeed + 12, // quarter distance
            Vehicles = VehicleSeed + 6,  // half distance
        };

        /// <summary>Citizens at an eighth of their usual distance, vehicles at a quarter.</summary>
        public static RenderBudget Aggressive => new RenderBudget
        {
            Citizens = CitizenSeed + 18, // eighth distance
            Vehicles = VehicleSeed + 12, // quarter distance
        };

        /// <summary>
        /// Turns a floor into the roughly equivalent distance multiplier relative to another
        /// floor, purely so a UI can say "about half as far" instead of showing a raw byte.
        /// </summary>
        public static float DistanceRatio(byte from, byte to) => math.pow(2f, (from - to) / 6f);
    }
}
