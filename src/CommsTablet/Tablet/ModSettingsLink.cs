using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityModManagerNet;

namespace CommsTablet.Tablet
{
    /// <summary>What a row of a mod's settings is: a heading, a space, or a setting changed on the tablet or only shown.</summary>
    internal enum SettingKind
    {
        Heading,
        Toggle,
        Choice,
        Number,
        Shown,
        Space,
    }

 internal sealed class SettingRow
    {
        internal SettingKind Kind;
        /// <summary>The Draw attribute's label, a suffix that became <see cref="Badge"/> taken off.</summary>
        internal string Label;
        /// <summary>The Draw attribute's Tooltip (Unity Mod Manager's hover box): a line under the label on the tablet.</summary>
        internal string Description;
        /// <summary>"Restart", "Reload", or "Next load" for a label ending so in brackets, drawn as a badge by the control.</summary>
        internal string Badge;
        /// <summary>A space's height, in Unity Mod Manager's pixels.</summary>
        internal float Space;
        internal FieldInfo Field;
        internal object Container;
        /// <summary>After a change to a field of a struct, puts the struct back into what holds it.</summary>
        internal Action WriteBack;
        internal double Min = double.MinValue, Max = double.MaxValue;
        internal int Precision = 2;

        internal object Value => Field?.GetValue(Container);

        internal bool Integral => Field != null && (Field.FieldType == typeof(int) || Field.FieldType == typeof(long));

        internal bool Bounded => Min > double.MinValue / 2 && Max < double.MaxValue / 2;

        internal bool IsKey => Field != null && Field.FieldType == typeof(KeyBinding);

        /// <summary>The value as the tablet shows it.</summary>
        internal string Text
        {
            get
            {
                var value = Value;
                switch (Kind)
                {
                    case SettingKind.Toggle:
                        return value is bool on && on ? "ON" : "OFF";
                    case SettingKind.Number:
                        return Integral ? Convert.ToInt64(value).ToString() : Convert.ToDouble(value).ToString("F" + Mathf.Clamp(Precision, 0, 6));
                    case SettingKind.Choice:
                        return value == null ? "" : ModSettingsLink.EnumText(Field.FieldType, value);
                    default:
                        return value?.ToString() ?? "";
                }
            }
        }

        /// <summary>
        /// The step of the − and + buttons: a twentieth of the field's range,
        /// or a tenth of its value where it has none, rounded down to 1, 2, or
        /// 5 of a power of ten, and never finer than its precision (whole
        /// numbers: 1).
        /// </summary>
        internal double Step
        {
            get
            {
                var unit = Integral ? 1d : Math.Pow(10, -Mathf.Clamp(Precision, 0, 6));
                var raw = Bounded ? (Max - Min) / 20d : Math.Abs(Convert.ToDouble(Value)) / 10d;
                var step = Math.Max(unit, Nice(raw));
                return Integral ? Math.Max(1d, Math.Round(step)) : step;
            }
        }

        private static double Nice(double x)
        {
            if (!(x > 0) || double.IsInfinity(x))
                return 0;
            var power = Math.Pow(10, Math.Floor(Math.Log10(x)));
            var f = x / power;
            return (f >= 5 ? 5 : f >= 2 ? 2 : 1) * power;
        }

        /// <summary>The value one step up or down, kept within the field's limits and rounded to its precision.</summary>
        internal object Stepped(bool up)
        {
            var value = Convert.ToDouble(Value) + (up ? Step : -Step);
            value = Math.Max(Min, Math.Min(Max, value));
            if (Field.FieldType == typeof(int))
                return (int)Math.Round(Math.Max(int.MinValue, Math.Min(int.MaxValue, value)));
            if (Field.FieldType == typeof(long))
                return (long)Math.Round(value);
            value = Math.Round(value, Mathf.Clamp(Precision, 0, 15));
            return Field.FieldType == typeof(float) ? (object)(float)value : value;
        }

        /// <summary>The next or previous value of an enum, going round.</summary>
        internal object Cycled(bool forward)
        {
            var values = Enum.GetValues(Field.FieldType);
            var index = Array.IndexOf(values, Value);
            var next = ((index < 0 ? 0 : index) + (forward ? 1 : -1) + values.Length) % values.Length;
            return values.GetValue(next);
        }
    }

 internal sealed class OwnerMod
    {
        internal readonly UnityModManager.ModEntry Entry;
        private readonly Func<UnityModManager.ModSettings> current;

        internal OwnerMod(UnityModManager.ModEntry entry, Func<UnityModManager.ModSettings> current)
        {
            Entry = entry;
            this.current = current;
        }

        internal string Id => Entry.Info.Id;

        internal string Name => string.IsNullOrEmpty(Entry.Info.DisplayName) ? Entry.Info.Id : Entry.Info.DisplayName;

        /// <summary>The mod's settings object now (a mod may load a new one).</summary>
        internal UnityModManager.ModSettings Settings
        {
            get
            {
                try
                {
                    return current();
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        /// <summary>The rows Unity Mod Manager's window would draw now (what is visible follows other settings).</summary>
        internal List<SettingRow> Rows()
        {
            var rows = new List<SettingRow>();
            var settings = Settings;
            if (settings == null)
                return rows;
            try
            {
                ModSettingsLink.Walk(settings, settings.GetType(), DrawFieldMask.OnlyDrawAttr, null, rows, Entry, 0);
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.Warning($"Could not read {Name}'s settings for the tablet: {e.GetBaseException().Message}");
            }
            return rows;
        }

        /// <summary>
        /// Changes a setting as Unity Mod Manager's window does (the field set,
        /// the settings' OnChange called) and saves it through the mod's own
        /// Save at once, so the tablet and the window always agree. What
        /// happened, for the status strip.
        /// </summary>
        internal string Apply(SettingRow row, object value)
        {
            var settings = Settings;
            if (settings == null || row.Field == null)
                return "Could not change it: the mod's settings are gone";
            try
            {
                row.Field.SetValue(row.Container, value);
                row.WriteBack?.Invoke();
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.Warning($"Could not change {Name}'s {row.Label}: {e.GetBaseException().Message}");
                return "Could not change it; see the log";
            }
            try
            {
                (settings as IDrawable)?.OnChange();
            }
            catch (Exception e)
            {
                // As Unity Mod Manager's window: the mod's own handler's exception is logged.
                Entry.Logger.LogException(e);
            }
            try
            {
                settings.Save(Entry);
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.Warning($"Could not save {Name}'s settings: {e.GetBaseException().Message}");
                return "Changed, but not saved; see the log";
            }
            return $"{Short(row.Label)}: {row.Text}. Saved";
        }

        private static string Short(string label)
        {
            var cut = label.IndexOfAny(new[] { '(', ':', '.', ';' });
            var text = cut > 12 ? label.Substring(0, cut).TrimEnd() : label;
            return text.Length > 48 ? text.Substring(0, 46).TrimEnd() + "…" : text;
        }
    }

 internal static class ModSettingsLink
    {
        internal const string Author = "Specter242";

 internal static readonly string[] Order = { "RealisticFiring", "AIEngineer", "RealisticBrakes", "RealisticStackEffects", "MurphySignals" };

 internal static readonly string[] LeftOut = { "SnowPlows", "MurphyOps" };

#if !PUBLIC_RELEASE
        internal const string SelfTestId = "CommsTabletSelfTest";
#endif

        private static List<OwnerMod> mods;

 internal static readonly List<string> PassedOver = new List<string>();

        private static readonly HashSet<string> warned = new HashSet<string>();

        // Unity Mod Manager's own: these are values, not boxes of fields.
        private static readonly string[] SpecialTypes = { "Vector2", "Vector3", "Vector4", "Color", "KeyBinding", "String", "Vector2i", "Vector3i" };

 internal static IReadOnlyList<OwnerMod> Mods
        {
            get
            {
                if (mods == null)
                    mods = Find();
                return mods;
            }
        }

 internal static bool Excluded(string id, string name) =>
            LeftOut.Contains(id, StringComparer.OrdinalIgnoreCase)
            || id.EndsWith("Probe", StringComparison.OrdinalIgnoreCase)
            || (name ?? "").IndexOf("probe", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Where a mod comes in the list: its place in <see cref="Order"/>, any other after, the self-test's last.</summary>
        internal static int Rank(string id)
        {
#if !PUBLIC_RELEASE
            if (string.Equals(id, SelfTestId, StringComparison.OrdinalIgnoreCase))
                return Order.Length + 1;
#endif
            var index = Array.FindIndex(Order, o => string.Equals(o, id, StringComparison.OrdinalIgnoreCase));
            return index >= 0 ? index : Order.Length;
        }

        /// <summary>Realistic Firing's one-time actions in 1.23.0 (rf-v1.23.0, Settings.cs:143-217), kept off the tablet even if their labels change.</summary>
        internal static readonly string[] OneTimeFields = { "GiveFireIron", "GiveFiringTablet", "GiveFiringManual", "GiveWoodCrate", "GiveRagTin", "RenewBoilerNow" };

        /// <summary>Whether a switch is a one-time action rather than a setting: labeled "unticks itself", or one of <see cref="OneTimeFields"/>.</summary>
        internal static bool OneTimeAction(FieldInfo field, DrawAttribute draw) =>
            OneTimeFields.Contains(field.Name)
            || (draw?.Label ?? "").IndexOf("unticks itself", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Ids and names in the list's order.</summary>
        internal static IEnumerable<T> Ordered<T>(IEnumerable<T> items, Func<T, string> id, Func<T, string> name) =>
            items.OrderBy(m => Rank(id(m))).ThenBy(name, StringComparer.OrdinalIgnoreCase);

        private static List<OwnerMod> Find()
        {
            var found = new List<OwnerMod>();
            PassedOver.Clear();
            foreach (var entry in UnityModManager.modEntries)
            {
                try
                {
                    if (entry?.Info == null || entry.Info.Author != Author || !entry.Active || entry.Assembly == null)
                        continue;
                    if (Excluded(entry.Info.Id, entry.Info.DisplayName))
                    {
                        PassedOver.Add($"{entry.Info.Id}: kept off the tablet");
                        continue;
                    }
                    var current = SettingsOf(entry.Assembly);
                    if (current != null && current() != null)
                        found.Add(new OwnerMod(entry, current));
                }
                catch (Exception e)
                {
                    Main.Mod?.Logger.Warning($"Could not find {entry?.Info?.Id}'s settings: {e.GetBaseException().Message}");
                }
            }
            found = Ordered(found, m => m.Id, m => m.Name).ToList();
            Main.Mod?.Logger.Log("Settings app: " + (found.Count > 0 ? string.Join(", ", found.Select(m => m.Name)) : "none of the owner's mods has settings")
                                 + (PassedOver.Count > 0 ? "; left out " + string.Join(", ", PassedOver) : ""));
            return found;
        }

        /// <summary>The static member a mod keeps its settings in (its Main's first), read anew each time.</summary>
        private static Func<UnityModManager.ModSettings> SettingsOf(Assembly assembly)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.Where(t => t != null).ToArray();
            }
            const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (var type in types.OrderBy(t => t.Name == "Main" ? 0 : 1))
            {
                try
                {
                    if (type.ContainsGenericParameters)
                        continue;
                    foreach (var field in type.GetFields(Static))
                        if (typeof(UnityModManager.ModSettings).IsAssignableFrom(field.FieldType) && field.GetValue(null) != null)
                            return () => field.GetValue(null) as UnityModManager.ModSettings;
                    foreach (var property in type.GetProperties(Static))
                        if (typeof(UnityModManager.ModSettings).IsAssignableFrom(property.PropertyType) && property.GetIndexParameters().Length == 0
                            && property.GetGetMethod(true) != null && property.GetValue(null) != null)
                            return () => property.GetValue(null) as UnityModManager.ModSettings;
                }
                catch (Exception)
                {
                    // A type whose members cannot load (a missing optional dependency) holds no settings here.
                }
            }
            return null;
        }

        /// <summary>Unity Mod Manager's <c>UI.Draw</c>, as rows: which fields it draws, and how.</summary>
        internal static void Walk(object container, Type type, DrawFieldMask mask, Action writeBack, List<SettingRow> rows, UnityModManager.ModEntry mod, int depth)
        {
            if (depth > 6)
                return;
            foreach (DrawFieldsAttribute fields in type.GetCustomAttributes(typeof(DrawFieldsAttribute), false))
                mask = fields.Mask;
            foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var draw = f.GetCustomAttributes(typeof(DrawAttribute), false).OfType<DrawAttribute>().LastOrDefault();
                if (draw != null)
                {
                    if (draw.Type == DrawType.Ignore || !Visible(draw, container, type))
                        continue;
                }
                else
                {
                    if (Skipped(f, mask))
                        continue;
                    draw = new DrawAttribute();
                    if (f.GetCustomAttributes(typeof(RangeAttribute), false).FirstOrDefault() is RangeAttribute range)
                    {
                        draw.Type = DrawType.Slider;
                        draw.Min = range.min;
                        draw.Max = range.max;
                    }
                }
                // A one-time action drawn as a switch ("unticks itself") is the mod's own window's button:
                // the tablet saves a change at once, so a tick here would be saved and act again at the
                // next start. Realistic Firing 1.23.0, as published, draws six such switches.
                if (f.FieldType == typeof(bool) && OneTimeAction(f, draw))
                    continue;
                // Drawn before the field, in their order, as that window draws them.
                foreach (var attribute in f.GetCustomAttributes(false).OfType<PropertyAttribute>().OrderBy(a => a.order))
                {
                    if (attribute is SpaceAttribute space)
                        rows.Add(new SettingRow { Kind = SettingKind.Space, Space = space.height });
                    else if (attribute is DrawSpaceAttribute drawSpace)
                        rows.Add(new SettingRow { Kind = SettingKind.Space, Space = drawSpace.Height });
                    else if (attribute is HeaderAttribute header && !string.IsNullOrEmpty(header.header))
                        rows.Add(new SettingRow { Kind = SettingKind.Heading, Label = header.header });
                    else if (attribute is DrawHeaderAttribute drawHeader && !string.IsNullOrEmpty(drawHeader.Header))
                        rows.Add(new SettingRow { Kind = SettingKind.Heading, Label = drawHeader.Header });
                }
                var label = draw.Label ?? f.Name;
                var description = Flowing(draw.Tooltip);
                var ft = f.FieldType;
                var nested = ((ft.IsClass && !ft.IsArray && !typeof(Delegate).IsAssignableFrom(ft)) || (ft.IsValueType && !ft.IsPrimitive && !ft.IsEnum))
                    && !SpecialTypes.Contains(ft.Name);
                if (nested)
                {
                    var value = f.GetValue(container);
                    if (value is UnityEngine.Object unityObject)
                    {
                        rows.Add(Shown(label, f, container, unityObject.name));
                        continue;
                    }
                    if (value == null)
                        continue;
                    var inner = mask;
                    foreach (DrawFieldsAttribute fields in f.GetCustomAttributes(typeof(DrawFieldsAttribute), false))
                        inner = fields.Mask;
                    if (!string.IsNullOrEmpty(label))
                        rows.Add(new SettingRow { Kind = SettingKind.Heading, Label = label });
                    Action back = ft.IsValueType ? () =>
                    {
                        f.SetValue(container, value);
                        writeBack?.Invoke();
                    } : writeBack;
                    Walk(value, ft, inner, back, rows, mod, depth + 1);
                    continue;
                }
                var kind = draw.Type;
                if (kind == DrawType.Auto)
                {
                    if (ft == typeof(bool))
                        kind = DrawType.Toggle;
                    else if (ft.IsEnum)
                        kind = ft.GetCustomAttributes(typeof(FlagsAttribute), false).Length == 0 ? DrawType.PopupList : DrawType.PopupToggleMulti;
                    else if (Numeric(ft))
                        kind = DrawType.Field;
                    else if (ft == typeof(KeyBinding))
                        kind = DrawType.KeyBinding;
                    else if (typeof(Delegate).IsAssignableFrom(ft))
                        kind = DrawType.CustomGUI;
                }
                if (kind == DrawType.CustomGUI)
                    continue;
                var row = new SettingRow
                {
                    Label = Badged(label, out var badge), Badge = badge, Description = description,
                    Field = f, Container = container, WriteBack = writeBack, Min = draw.Min, Max = draw.Max, Precision = draw.Precision,
                };
                if (kind == DrawType.Toggle && ft == typeof(bool))
                    row.Kind = SettingKind.Toggle;
                else if ((kind == DrawType.PopupList || kind == DrawType.ToggleGroup) && ft.IsEnum && ft.GetCustomAttributes(typeof(FlagsAttribute), false).Length == 0)
                    row.Kind = SettingKind.Choice;
                else if ((kind == DrawType.Slider || kind == DrawType.Field) && Numeric(ft))
                    row.Kind = SettingKind.Number;
                else
                    row.Kind = SettingKind.Shown;
                rows.Add(row);
            }
        }

        private static bool Numeric(Type t) => t == typeof(int) || t == typeof(long) || t == typeof(float) || t == typeof(double);

        private static SettingRow Shown(string label, FieldInfo f, object container, string text) =>
            new SettingRow { Kind = SettingKind.Shown, Label = label + ": " + text, Field = f, Container = container };

        // Unity Mod Manager's rule for a field with no Draw attribute.
        private static bool Skipped(FieldInfo f, DrawFieldMask mask)
        {
            if ((mask & DrawFieldMask.OnlyDrawAttr) != 0 || ((mask & DrawFieldMask.SkipNotSerialized) != 0 && f.IsNotSerialized))
                return true;
            var serialized = f.GetCustomAttributes(typeof(SerializeField), false).Length != 0;
            return ((mask & DrawFieldMask.Public) == 0 || !f.IsPublic) && ((mask & DrawFieldMask.Serialized) == 0 || !serialized)
                && ((mask & DrawFieldMask.Public) != 0 || (mask & DrawFieldMask.Serialized) != 0);
        }

        // ---- Visibility ------------------------------------------------------

        /// <summary>
        /// Whether Unity Mod Manager's window would draw a field with this Draw
        /// attribute: its VisibleOn rule true, else its InvisibleOn rule not
        /// true. A rule that window cannot read throws there, and nothing more
        /// of the mod's settings is drawn; here the row alone is left out, so
        /// a row meant for the mod's own window never reaches the tablet.
        /// </summary>
        internal static bool Visible(DrawAttribute draw, object container, Type type)
        {
            if (!string.IsNullOrEmpty(draw.VisibleOn))
                return DependsOn(draw.VisibleOn, container, type) == true;
            if (!string.IsNullOrEmpty(draw.InvisibleOn))
                return DependsOn(draw.InvisibleOn, container, type) == false;
            return true;
        }

        /// <summary>
        /// Unity Mod Manager's VisibleOn and InvisibleOn ("Field|Value",
        /// "#Property|Value"), exactly as its <c>UI.DependsOn</c> (0.32.4)
        /// reads them: an instance field or property of the settings' (or
        /// box's) own type, public or not, of a primitive or enum type, its
        /// value's hash compared with the value named's. A static member is
        /// not found there (it throws "not found"), nor here: null, where the
        /// rule cannot be read.
        /// </summary>
        internal static bool? DependsOn(string rule, object container, Type type)
        {
            try
            {
                var parts = rule.Split('|');
                if (parts.Length != 2)
                    return Unreadable(rule, type, "it must be 'Name|Value'");
                const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                Type valueType;
                object current;
                var name = parts[0].TrimStart('#');
                if (parts[0].StartsWith("#"))
                {
                    var property = type.GetProperty(name, Instance);
                    if (property == null || property.GetIndexParameters().Length != 0 || property.GetGetMethod(true) == null)
                        return Unreadable(rule, type, $"no instance property {name}");
                    valueType = property.PropertyType;
                    current = property.GetValue(container, null);
                }
                else
                {
                    var field = type.GetField(name, Instance);
                    if (field == null)
                        return Unreadable(rule, type, $"no instance field {name}");
                    valueType = field.FieldType;
                    current = field.GetValue(container);
                }
                if (!valueType.IsPrimitive && !valueType.IsEnum)
                    return Unreadable(rule, type, $"{valueType.Name} is not a primitive or an enum");
                var wanted = valueType.IsEnum ? Enum.Parse(valueType, parts[1]) : Convert.ChangeType(parts[1], valueType);
                return current != null && wanted != null && current.GetHashCode() == wanted.GetHashCode();
            }
            catch (Exception e)
            {
                return Unreadable(rule, type, e.GetBaseException().Message);
            }
        }

        private static bool? Unreadable(string rule, Type type, string why)
        {
            if (warned.Add(type.FullName + "/" + rule))
                Main.Mod?.Logger.Warning($"Settings app: {type.FullName}'s rule '{rule}' cannot be read ({why}); its row is left out");
            return null;
        }

        // ---- Words -------------------------------------------------------------

        // A bracketed suffix that says when a change takes effect, and its badge.
        private static readonly KeyValuePair<string, string>[] Suffixes =
        {
            new KeyValuePair<string, string>("(restart)", "Restart"),
            new KeyValuePair<string, string>("(restart required)", "Restart"),
            new KeyValuePair<string, string>("(reload)", "Reload"),
            new KeyValuePair<string, string>("(next load)", "Next load"),
        };

        /// <summary>
        /// A tooltip as flowing text: Unity Mod Manager's hover box is one line,
        /// so a mod breaks a long tooltip at about 80 characters; the tablet
        /// wraps it to its own width, so a single line break becomes a space (a
        /// blank line stays a paragraph's end). Null for none.
        /// </summary>
        internal static string Flowing(string tooltip)
        {
            if (string.IsNullOrWhiteSpace(tooltip))
                return null;
            var paragraphs = System.Text.RegularExpressions.Regex.Split(tooltip.Replace("\r\n", "\n").Replace('\r', '\n').Trim(), @"\n[ \t]*\n\s*");
            return string.Join("\n", paragraphs.Select(p => System.Text.RegularExpressions.Regex.Replace(p.Trim(), @"\s+", " ")));
        }

        /// <summary>A label without a suffix such as "(restart)", and the badge that suffix becomes.</summary>
        internal static string Badged(string label, out string badge)
        {
            badge = null;
            if (string.IsNullOrEmpty(label))
                return label;
            var trimmed = label.TrimEnd();
            foreach (var suffix in Suffixes)
                if (trimmed.Length > suffix.Key.Length && trimmed.EndsWith(suffix.Key, StringComparison.OrdinalIgnoreCase))
                {
                    badge = suffix.Value;
                    return trimmed.Substring(0, trimmed.Length - suffix.Key.Length).TrimEnd(' ', ',', ';', ':');
                }
            return label;
        }

        /// <summary>
        /// An enum value as the tablet shows it: the name the mod gives it
        /// (Unity's InspectorName, or a Description), else its name in words
        /// ("SpringOrAutumn": "Spring or autumn"). The saved value is the name.
        /// </summary>
        internal static string EnumText(Type type, object value)
        {
            var name = Enum.GetName(type, value);
            if (name == null)
                return value.ToString();
            var member = type.GetField(name, BindingFlags.Public | BindingFlags.Static);
            if (member != null)
            {
                foreach (var attribute in member.GetCustomAttributes(false))
                {
                    if (attribute is InspectorNameAttribute inspector && !string.IsNullOrEmpty(inspector.displayName))
                        return inspector.displayName;
                    if (attribute is DescriptionAttribute described && !string.IsNullOrEmpty(described.Description))
                        return described.Description;
                }
            }
            return Words(name);
        }

        /// <summary>
        /// A code name in words: split where a capital follows a small letter or
        /// a digit, where an acronym meets a word, and at underscores; the first
        /// word as written (capitalized), the rest in small letters unless an
        /// acronym or holding a digit ("S282Stoker": "S282 stoker").
        /// </summary>
        internal static string Words(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;
            var words = new List<string>();
            var word = new StringBuilder();
            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (c == '_' || c == ' ')
                {
                    if (word.Length > 0)
                        words.Add(word.ToString());
                    word.Length = 0;
                    continue;
                }
                if (word.Length > 0 && char.IsUpper(c))
                {
                    var previous = name[i - 1];
                    var nextSmall = i + 1 < name.Length && char.IsLower(name[i + 1]);
                    if (char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && nextSmall))
                    {
                        words.Add(word.ToString());
                        word.Length = 0;
                    }
                }
                word.Append(c);
            }
            if (word.Length > 0)
                words.Add(word.ToString());
            for (var i = 0; i < words.Count; i++)
            {
                var w = words[i];
                var kept = w.Count(char.IsUpper) > 1 || w.Any(char.IsDigit);
                if (i == 0)
                    words[i] = char.ToUpperInvariant(w[0]) + w.Substring(1);
                else if (!kept)
                    words[i] = w.ToLowerInvariant();
            }
            return string.Join(" ", words);
        }
    }
}
