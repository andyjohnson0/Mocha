namespace Mocha.App
{
    /// <summary>
    /// A coffee brewing method, such as French press, and its brew time.
    /// Persisted as part of <see cref="Beverage"/>.
    /// </summary>
    public class Method
    {
        /// <summary>
        /// Maximum length of a method name, in characters.
        /// </summary>
        public const int MaxNameLen = 20;

        /// <summary>
        /// Maximum brew time. This keeps brew times to at most two minute digits when displayed.
        /// </summary>
        public static readonly TimeSpan MaxBrewTime = new TimeSpan(0, 99, 59);

        /// <summary>
        /// Method name. Must be non-empty and unique within a <see cref="Beverage"/>, as determined by
        /// <see cref="Beverage.MethodNameComparer"/>.
        /// </summary>
        public required string Name { get; set; }

        /// <summary>
        /// Brew time. At most <see cref="MaxBrewTime"/>.
        /// </summary>
        public required TimeSpan BrewTime { get; set; }
    }
}
