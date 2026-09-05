using Unity.Mathematics;

namespace Cs2Saver
{
    /// <summary>
    /// How aggressively each category of thing stops being drawn.
    ///
    /// The game stores a per-entity <c>CullingInfo.m_MinLod</c> and keeps an entity only while
    /// <c>CalculateMaxLod(bounds, camera) &gt;= m_MinLod</c>. The LOD value it compares against
    /// falls as distance grows, so a HIGHER floor culls SOONER.
    ///
    /// The scale is logarithmic, and <c>RenderingUtils.CalculateDistanceFactor</c> pins it down
    /// exactly:
    /// <code>
    ///     distanceFactor(lod) = pow(2, (128 - lod) / 6)
    /// </code>
    /// so <b>every +6 on the floor halves the distance at which something disappears</b>. Every
    /// number here is therefore expressed as a count of halvings rather than a raw LOD value:
    /// it is the only unit in which these settings mean anything.
    ///
    /// <para><b>Why this exists at all.</b> The game's own <c>levelOfDetail</c> slider does the
    /// same job far more bluntly. Measured on this project's <c>sharp</c> profile, taking it to
    /// its floor was worth 12ms of a 32.5ms GPU frame — geometry is roughly sixty percent of the
    /// cost of a sharp city. But that slider scales every LOD transition at once, buildings
    /// included, which is exactly what makes a city look like modelling clay. This structure
    /// exists to buy the same geometry back from trees, props and traffic while
    /// <b>never touching a building</b>. That restriction is the entire point of the mod, and it
    /// is enforced by prefab category rather than by hoping the numbers stay small.</para>
    /// </summary>
    public struct RenderBudget
    {
        /// <summary>Halvings applied to pedestrians and animals. 0 leaves the game's own value alone.</summary>
        public byte CitizenHalvings;

        /// <summary>Halvings applied to vehicles.</summary>
        public byte VehicleHalvings;

        /// <summary>Halvings applied to trees and plants.</summary>
        public byte TreeHalvings;

        /// <summary>
        /// Halvings applied to props: street furniture, signs, fences, the small static clutter
        /// that a city is full of. Buildings are excluded from this category by construction.
        /// </summary>
        public byte PropHalvings;

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
        /// This matters more than it looks: the entity pass only ever raises the floor, so any
        /// value below these does precisely nothing. An earlier version of this file used
        /// 96/112/128 for citizens and was therefore a complete no-op.
        /// </summary>
        public const byte CitizenSeed = 129;

        /// <inheritdoc cref="CitizenSeed"/>
        public const byte VehicleSeed = 120;

        /// <summary>One halving is +6 on the LOD floor. There is no other magic number here.</summary>
        public const int PerHalving = 6;

        /// <summary>Absolute floor for the live-entity pass, derived from the seed above.</summary>
        public byte Citizens => Floor(CitizenSeed, CitizenHalvings);

        /// <inheritdoc cref="Citizens"/>
        public byte Vehicles => Floor(VehicleSeed, VehicleHalvings);

        private static byte Floor(byte seed, byte halvings) =>
            halvings == 0 ? (byte)0 : (byte)math.min(255, seed + PerHalving * halvings);

        /// <summary>Off. Every category keeps whatever the game decided.</summary>
        public static RenderBudget Off => default;

        /// <summary>
        /// Citizens disappear at half their usual distance; vehicles are untouched, because on a
        /// traffic-focused save the cars ARE the thing being looked at.
        /// </summary>
        public static RenderBudget Balanced => new RenderBudget
        {
            CitizenHalvings = 1,
            PropHalvings = 1,
        };

        /// <summary>
        /// The "I want the city, not the people" setting. Traffic stays legible for longer than
        /// the crowds do.
        /// </summary>
        public static RenderBudget TrafficFocus => new RenderBudget
        {
            CitizenHalvings = 2,
            VehicleHalvings = 1,
            PropHalvings = 1,
        };

        /// <summary>Everything that moves, cut hard. Scenery left alone.</summary>
        public static RenderBudget Aggressive => new RenderBudget
        {
            CitizenHalvings = 3,
            VehicleHalvings = 2,
            PropHalvings = 2,
        };

        /// <summary>
        /// The one this mod exists for: everything that is not a building, cut hard. Trees,
        /// street furniture, traffic and crowds all give up distance so that the buildings do
        /// not have to.
        /// </summary>
        public static RenderBudget Declutter => new RenderBudget
        {
            CitizenHalvings = 3,
            VehicleHalvings = 2,
            TreeHalvings = 3,
            PropHalvings = 4,
        };

        /// <summary>
        /// Declutter, pushed as far as the scale usefully goes. Props at a sixty-fourth of their
        /// usual distance are effectively only drawn under the camera, which is invisible from
        /// the air and obvious at street level. Buildings are still untouched.
        /// </summary>
        public static RenderBudget DeclutterMax => new RenderBudget
        {
            CitizenHalvings = 4,

            // Deliberately not raised past Declutter. Traffic is 99 prefabs against 14,291 props,
            // so cutting it further buys almost nothing measurable, and it is the one category a
            // city-builder player is actually watching.
            VehicleHalvings = 2,

            // Only one halving of the foliage, against four for everything else.
            //
            // Four was tried and it is what a bare city looks like: from any height the trees are
            // the green, and taking them to a sixteenth of their distance leaves flat fields where
            // a forest was. A before-and-after against the untouched game made that obvious in a
            // way no frame-time column ever would have.
            //
            // It is also nearly free to give back. The city has twenty tree prefabs and fifty-two
            // plants against fourteen thousand props, so foliage was never where the frames were.
            TreeHalvings = 3,

            PropHalvings = 6,
        };

        /// <summary>
        /// How much foliage distance to keep, as halvings. Separated from the presets because it
        /// is the one cut in this mod that is a matter of taste rather than of degree.
        ///
        /// Measured on the `handsome` profile at normal play speed:
        /// <code>
        ///     halvings   fps   1% low   frames at 60
        ///     1         56.0     30.3            42%
        ///     3         57.9     31.4            53%
        ///     4         61.4     39.2            67%
        /// </code>
        /// Five frames per second between a city with trees in it and a city without. Nobody
        /// else should be deciding that, so it is a setting.
        /// </summary>
        public enum Foliage : byte
        {
            /// <summary>The game's own distances. Every tree it would have drawn.</summary>
            Untouched = 0,

            /// <summary>Half distance. Still a green city from the air.</summary>
            Full = 1,

            /// <summary>An eighth. The default: visibly thinner, still recognisably planted.</summary>
            Balanced = 3,

            /// <summary>A sixteenth. Fields where a forest was, and five frames for it.</summary>
            Thin = 4,
        }

        /// <summary>Trees and plants alone. Exists to measure what foliage actually costs.</summary>
        public static RenderBudget TreesOnly => new RenderBudget { TreeHalvings = 3 };

        /// <summary>Static clutter alone. Exists to measure what props actually cost.</summary>
        public static RenderBudget PropsOnly => new RenderBudget { PropHalvings = 4 };

        /// <summary>
        /// Turns a floor into the roughly equivalent distance multiplier relative to another
        /// floor, purely so a UI can say "about half as far" instead of showing a raw number.
        /// </summary>
        public static float DistanceRatio(byte from, byte to) => math.pow(2f, (from - to) / 6f);
    }
}
