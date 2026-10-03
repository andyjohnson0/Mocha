using System.Text.Json;

using Android.Content;


namespace Mocha.App
{
    /// <summary>
    /// The user's set of coffee brewing methods, persisted in shared preferences as JSON.
    /// </summary>
    public class Beverage
    {
        /// <summary>
        /// The brewing methods, sorted by name. Method names are non-empty and unique.
        /// Use <see cref="AddMethod"/>, <see cref="RenameMethod"/> and <see cref="DeleteMethod"/> to change them,
        /// which maintain this.
        /// </summary>
        public required Method[] Methods { get; set; }


        /// <summary>
        /// Load the saved methods or, if none have been saved, create and save the default methods.
        /// </summary>
        /// <param name="context">Context</param>
        /// <returns>Beverage</returns>
        public static Beverage Load(Context context)
        {
            ArgumentNullException.ThrowIfNull(context, nameof(context));

            using (var prefs = Beverage.GetSharedPreferences(context))
            {
                var json = prefs.GetString(Beverage._methodsKey, null);
                if (json != null)
                {
                    return new Beverage()
                    {
                        Methods = JsonSerializer.Deserialize<Method[]>(json)!
                    };
                }
            }

            // No methods have been saved: this is the first run, or Reset() has been called. Create the defaults, with
            // names in the current locale, and save them. Saving fixes the method list, so the names don't change if
            // the locale changes later.
            var defaults = Beverage.CreateDefaults(context);
            defaults.Save(context);
            return defaults;
        }


        /// <summary>
        /// Save the methods.
        /// </summary>
        /// <param name="context">Context</param>
        public void Save(Context context)
        {
            ArgumentNullException.ThrowIfNull(context, nameof(context));

            using (var prefs = Beverage.GetSharedPreferences(context))
            {
                using (var editor = prefs.Edit()!)
                {
                    editor.Clear();
                    editor.PutInt(Beverage._schemaVersionKey, Beverage._schemaVersionId);
                    editor.PutString(Beverage._methodsKey, JsonSerializer.Serialize(this.Methods));
                    editor.Apply();
                }
            }
        }


        /// <summary>
        /// Delete the saved methods, so that the next <see cref="Load(Context)"/> re-creates the default methods,
        /// with names in the locale at that time.
        /// </summary>
        /// <param name="context">Context</param>
        public static void Reset(Context context)
        {
            ArgumentNullException.ThrowIfNull(context, nameof(context));

            // Clear the shared preferences. This ensures that Load() will re-build the methods from the defaults.
            using (var prefs = Beverage.GetSharedPreferences(context))
            {
                using (var editor = prefs.Edit()!)
                {
                    editor.Clear();
                    editor.Apply();
                }
            }
        }


        /// <summary>
        /// Comparer that determines whether two method names are the same. Method names must be non-empty and unique.
        /// </summary>
        public static readonly StringComparer MethodNameComparer = StringComparer.CurrentCultureIgnoreCase;


        /// <summary>
        /// Get the names of the methods, optionally excluding one.
        /// </summary>
        /// <param name="excludeIdx">Index of a method to exclude, or -1 to include all.</param>
        public string[] GetMethodNames(int excludeIdx = -1)
        {
            return this.Methods.Where((m, i) => i != excludeIdx).Select(m => m.Name).ToArray();
        }


        /// <summary>
        /// Add a method, keeping the methods sorted by name.
        /// </summary>
        /// <param name="name">Method name. Must be non-empty, and not already in use.</param>
        /// <param name="duration">Brew time</param>
        /// <returns>Index of the added method.</returns>
        /// <exception cref="ArgumentException">The name is empty or already in use.</exception>
        public int AddMethod(
            string name,
            TimeSpan duration)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            if (GetMethodNames().Contains(name, MethodNameComparer))
                throw new ArgumentException($"Method name '{name}' is already in use", nameof(name));

            var method = new Method() { Name = name, BrewTime = duration };
            this.Methods = this.Methods.Append(method).OrderBy(m => m.Name).ToArray();

            // Return the added method's index after the sort.
            return Array.IndexOf(this.Methods, method);
        }


        /// <summary>
        /// Rename a method, keeping the methods sorted by name.
        /// </summary>
        /// <param name="idx">Index of the method to rename.</param>
        /// <param name="name">New name. Must be non-empty, and not in use by another method.</param>
        /// <returns>Index of the renamed method, which may have moved.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The index is out of range.</exception>
        /// <exception cref="ArgumentException">The name is empty or already in use.</exception>
        public int RenameMethod(
            int idx,
            string name)
        {
            if (idx < 0 || idx >= Methods.Length)
                throw new ArgumentOutOfRangeException(nameof(idx));
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            if (GetMethodNames(excludeIdx: idx).Contains(name, MethodNameComparer))
                throw new ArgumentException($"Method name '{name}' is already in use", nameof(name));

            // Rename and re-sort, then return the renamed method's new index.
            var method = this.Methods[idx];
            method.Name = name;
            this.Methods = this.Methods.OrderBy(m => m.Name).ToArray();
            return Array.IndexOf(this.Methods, method);
        }


        /// <summary>
        /// Delete a method.
        /// </summary>
        /// <param name="idx">Index of the method to delete.</param>
        /// <exception cref="ArgumentOutOfRangeException">The index is out of range.</exception>
        public void DeleteMethod(int idx)
        {
            if (idx < 0 || idx >= Methods.Length)
                throw new ArgumentOutOfRangeException(nameof(idx));

            var tmp = this.Methods.ToList();
            tmp.RemoveAt(idx);
            this.Methods = tmp.ToArray();
        }


        /// <summary>
        /// Format a brew time or time remaining for display, as minutes and seconds (e.g. "4:00").
        /// </summary>
        /// <param name="ts">Time. Fractional seconds are truncated.</param>
        /// <returns>Formatted time</returns>
        public static string FormatTime(TimeSpan ts)
        {
            // Use total minutes rather than the "m" format specifier, which is only the minutes component (0-59)
            // and so displays times of an hour or more incorrectly.
            return $"{(int)ts.TotalMinutes}:{ts.Seconds:00}";
        }


        #region Implementation

        private const string _schemaVersionKey = "_schema_version";
        private const int _schemaVersionId = 1;

        private const string _methodsKey = "methodsKey";


        /// <summary>
        /// Create the default methods, with names in the current locale.
        /// </summary>
        private static Beverage CreateDefaults(Context context)
        {
            var res = context.Resources!;
            var methods = new List<Method>()
            {
                new Method() { Name = res.GetString(Resource.String.aeropress), BrewTime = new TimeSpan(0, 2, 0) },
                new Method() { Name = res.GetString(Resource.String.coffeeBag), BrewTime = new TimeSpan(0, 4, 0) },
                new Method() { Name = res.GetString(Resource.String.dripSystem), BrewTime = new TimeSpan(0, 5, 0) },
                new Method() { Name = res.GetString(Resource.String.espresso), BrewTime = new TimeSpan(0, 0, 30) },
                new Method() { Name = res.GetString(Resource.String.frenchPress), BrewTime = new TimeSpan(0, 4, 0) },
                new Method() { Name = res.GetString(Resource.String.percolator), BrewTime = new TimeSpan(0, 10, 0) }
            };
            methods = methods.OrderBy(m => m.Name).ToList();

            return new Beverage()
            {
                Methods = methods.ToArray()
            };
        }



        private static ISharedPreferences GetSharedPreferences(Context context)
        {
            return context.GetSharedPreferences(context.PackageName + "BeveragePrefs", FileCreationMode.Private)!;
        }

        #endregion Implementation
    }
}
