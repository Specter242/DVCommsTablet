using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
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

        /// <summary>The mod's one-time actions, or null for none.</summary>
        internal readonly ModActions Actions;

        internal OwnerMod(UnityModManager.ModEntry entry, Func<UnityModManager.ModSettings> current, ModActions actions = null)
        {
            Entry = entry;
            this.current = current;
            Actions = actions;
        }

        internal string Id => Entry.Info.Id;

        internal string Name => string.IsNullOrEmpty(Entry.Info.DisplayName) ? Entry.Info.Id : Entry.Info.DisplayName;

        /// <summary>The mod's settings object now (a mod may load a new one).</summary>
        internal UnityModManager.ModSettings Settings
        {
            get
            {
                if (current == null)
                    return null;
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

        private static readonly char[] CutChars = { '(', ':', '.', ';' };

        /// <summary>Bumped whenever <see cref="Apply"/> writes a setting.</summary>
        internal int Version { get; private set; }

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
        /// Exact snapshot of container references and VisibleOn/InvisibleOn
        /// results for a mod's settings. Returns false when a visible nested
        /// value-type (struct) box is present, so rows holding boxed struct
        /// copies are never cached across refreshes.
        /// </summary>
        internal bool TryVisibilitySnapshot(out VisibilitySnapshot snapshot)
        {
            var settings = Settings;
            snapshot = new VisibilitySnapshot(Version);
            if (settings == null)
                return true;
            try
            {
                return ModSettingsLink.CaptureVisibility(settings, settings.GetType(), DrawFieldMask.OnlyDrawAttr, 0, snapshot);
            }
            catch (Exception)
            {
                snapshot = null;
                return false;
            }
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
                Version++;
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
            var cut = label.IndexOfAny(CutChars);
            var text = cut > 12 ? label.Substring(0, cut).TrimEnd() : label;
            return text.Length > 48 ? text.Substring(0, 46).TrimEnd() + "…" : text;
        }
    }

 internal sealed class VisibilitySnapshot
    {
        internal struct NestedSlot
        {
            internal readonly byte Kind; // 0 = null, 1 = container, 2 = UnityEngine.Object
            internal readonly object Obj;
            internal readonly string Name;

            internal NestedSlot(byte kind, object obj, string name)
            {
                Kind = kind;
                Obj = obj;
                Name = name;
            }
        }

        internal readonly int Version;
        internal readonly List<NestedSlot> Slots = new List<NestedSlot>();
        internal readonly List<bool> Visibility = new List<bool>();

        internal VisibilitySnapshot(int version)
        {
            Version = version;
        }

        internal bool SameAs(VisibilitySnapshot other)
        {
            if (other == null || Version != other.Version
                || Slots.Count != other.Slots.Count
                || Visibility.Count != other.Visibility.Count)
                return false;
            for (var i = 0; i < Slots.Count; i++)
            {
                var a = Slots[i];
                var b = other.Slots[i];
                if (a.Kind != b.Kind
                    || !ReferenceEquals(a.Obj, b.Obj)
                    || !string.Equals(a.Name, b.Name, StringComparison.Ordinal))
                    return false;
            }
            for (var i = 0; i < Visibility.Count; i++)
                if (Visibility[i] != other.Visibility[i])
                    return false;
            return true;
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

        /// <summary>The Comms Tablet's own id, its section after the other mods'.</summary>
        internal const string OwnId = "CommsTablet";

        /// <summary>Where a mod comes in the list: its place in <see cref="Order"/>, any other after, then the Comms Tablet's own, the self-test's last.</summary>
        internal static int Rank(string id)
        {
#if !PUBLIC_RELEASE
            if (string.Equals(id, SelfTestId, StringComparison.OrdinalIgnoreCase))
                return Order.Length + 2;
#endif
            if (string.Equals(id, OwnId, StringComparison.OrdinalIgnoreCase))
                return Order.Length + 1;
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
                    if (entry?.Info == null || !entry.Active || entry.Assembly == null || entry == Main.Mod)
                        continue;
                    var owners = entry.Info.Author == Author;
                    if (Excluded(entry.Info.Id, entry.Info.DisplayName))
                    {
                        if (owners)
                            PassedOver.Add($"{entry.Info.Id}: kept off the tablet");
                        continue;
                    }
                    var current = owners ? SettingsOf(entry.Assembly) : null;
                    if (current != null && current() == null)
                        current = null;
                    var actions = ModActions.Find(entry.Assembly, string.IsNullOrEmpty(entry.Info.DisplayName) ? entry.Info.Id : entry.Info.DisplayName);
                    if (current != null || actions != null)
                        found.Add(new OwnerMod(entry, current, actions));
                }
                catch (Exception e)
                {
                    Main.Mod?.Logger.Warning($"Could not find {entry?.Info?.Id}'s settings: {e.GetBaseException().Message}");
                }
            }
            if (Main.Mod != null)
                found.Add(new OwnerMod(Main.Mod, null, ModActions.Own()));
            found = Ordered(found, m => m.Id, m => m.Name).ToList();
            Main.Mod?.Logger.Log("Settings app: " + string.Join(", ", found.Select(m => m.Name + (m.Actions != null ? " (with Equipment)" : "")))
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

        private struct PrefixMeta
        {
            internal SettingKind Kind;
            internal string Label;
            internal float Space;
        }

        private sealed class ParsedRule
        {
            internal bool Valid;
            internal FieldInfo Field;
            internal PropertyInfo Property;
            internal object Wanted;
            internal int WantedHash;
        }

        private sealed class FieldMeta
        {
            internal FieldInfo Field;
            internal bool HasDrawAttr;
            internal bool IsPublic;
            internal bool IsSerialized;
            internal bool IsNotSerialized;
            internal bool IsIgnore;
            internal bool IsOneTimeBool;
            internal bool IsCustomGui;
            internal bool IsNested;
            internal bool IsValueType;
            internal string VisibleOn;
            internal string InvisibleOn;
            internal ParsedRule VisibleRule;
            internal ParsedRule InvisibleRule;
            internal PrefixMeta[] Prefixes;
            internal string RawLabel;
            internal string BadgedLabel;
            internal string Badge;
            internal string Description;
            internal double Min, Max;
            internal int Precision;
            internal SettingKind Kind;
            internal DrawFieldMask? FieldMask;
        }

        private sealed class TypeMeta
        {
            internal DrawFieldMask? TypeMask;
            internal FieldMeta[] Fields;
        }

        private static readonly Dictionary<Type, TypeMeta> typeCache = new Dictionary<Type, TypeMeta>();
        private static readonly Dictionary<(Type, string), ParsedRule> ruleCache = new Dictionary<(Type, string), ParsedRule>();
        private static readonly Dictionary<(Type, object), string> enumTextCache = new Dictionary<(Type, object), string>();

        private static TypeMeta MetaOf(Type type)
        {
            if (typeCache.TryGetValue(type, out var cached))
                return cached;
            var meta = new TypeMeta();
            foreach (DrawFieldsAttribute fields in type.GetCustomAttributes(typeof(DrawFieldsAttribute), false))
                meta.TypeMask = fields.Mask;
            var rawFields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var list = new FieldMeta[rawFields.Length];
            for (var i = 0; i < rawFields.Length; i++)
            {
                var f = rawFields[i];
                var ft = f.FieldType;
                var draw = f.GetCustomAttributes(typeof(DrawAttribute), false).OfType<DrawAttribute>().LastOrDefault();
                var hasDrawAttr = draw != null;
                if (draw == null)
                {
                    draw = new DrawAttribute();
                    if (f.GetCustomAttributes(typeof(RangeAttribute), false).FirstOrDefault() is RangeAttribute range)
                    {
                        draw.Type = DrawType.Slider;
                        draw.Min = range.min;
                        draw.Max = range.max;
                    }
                }
                var prefixes = new List<PrefixMeta>();
                foreach (var attribute in f.GetCustomAttributes(false).OfType<PropertyAttribute>().OrderBy(a => a.order))
                {
                    if (attribute is SpaceAttribute space)
                        prefixes.Add(new PrefixMeta { Kind = SettingKind.Space, Space = space.height });
                    else if (attribute is DrawSpaceAttribute drawSpace)
                        prefixes.Add(new PrefixMeta { Kind = SettingKind.Space, Space = drawSpace.Height });
                    else if (attribute is HeaderAttribute header && !string.IsNullOrEmpty(header.header))
                        prefixes.Add(new PrefixMeta { Kind = SettingKind.Heading, Label = header.header });
                    else if (attribute is DrawHeaderAttribute drawHeader && !string.IsNullOrEmpty(drawHeader.Header))
                        prefixes.Add(new PrefixMeta { Kind = SettingKind.Heading, Label = drawHeader.Header });
                }
                DrawFieldMask? fieldMask = null;
                foreach (DrawFieldsAttribute fields in f.GetCustomAttributes(typeof(DrawFieldsAttribute), false))
                    fieldMask = fields.Mask;
                var rawLabel = draw.Label ?? f.Name;
                var badgedLabel = Badged(rawLabel, out var badge);
                var nested = ((ft.IsClass && !ft.IsArray && !typeof(Delegate).IsAssignableFrom(ft)) || (ft.IsValueType && !ft.IsPrimitive && !ft.IsEnum))
                    && !SpecialTypes.Contains(ft.Name);
                var isFlagsEnum = ft.IsEnum && ft.GetCustomAttributes(typeof(FlagsAttribute), false).Length != 0;
                var kind = draw.Type;
                if (kind == DrawType.Auto)
                {
                    if (ft == typeof(bool))
                        kind = DrawType.Toggle;
                    else if (ft.IsEnum)
                        kind = !isFlagsEnum ? DrawType.PopupList : DrawType.PopupToggleMulti;
                    else if (Numeric(ft))
                        kind = DrawType.Field;
                    else if (ft == typeof(KeyBinding))
                        kind = DrawType.KeyBinding;
                    else if (typeof(Delegate).IsAssignableFrom(ft))
                        kind = DrawType.CustomGUI;
                }
                SettingKind settingKind;
                if (kind == DrawType.Toggle && ft == typeof(bool))
                    settingKind = SettingKind.Toggle;
                else if ((kind == DrawType.PopupList || kind == DrawType.ToggleGroup) && ft.IsEnum && !isFlagsEnum)
                    settingKind = SettingKind.Choice;
                else if ((kind == DrawType.Slider || kind == DrawType.Field) && Numeric(ft))
                    settingKind = SettingKind.Number;
                else
                    settingKind = SettingKind.Shown;

                var isIgnore = hasDrawAttr && draw.Type == DrawType.Ignore;
                var visibleOn = hasDrawAttr && !isIgnore && !string.IsNullOrEmpty(draw.VisibleOn) ? draw.VisibleOn : null;
                var invisibleOn = hasDrawAttr && !isIgnore && visibleOn == null && !string.IsNullOrEmpty(draw.InvisibleOn) ? draw.InvisibleOn : null;
                list[i] = new FieldMeta
                {
                    Field = f,
                    HasDrawAttr = hasDrawAttr,
                    IsPublic = f.IsPublic,
                    IsSerialized = f.GetCustomAttributes(typeof(SerializeField), false).Length != 0,
                    IsNotSerialized = f.IsNotSerialized,
                    IsIgnore = isIgnore,
                    IsOneTimeBool = ft == typeof(bool) && OneTimeAction(f, draw),
                    IsCustomGui = kind == DrawType.CustomGUI,
                    IsNested = nested,
                    IsValueType = ft.IsValueType,
                    VisibleOn = visibleOn,
                    InvisibleOn = invisibleOn,
                    VisibleRule = visibleOn != null ? ResolveRule(visibleOn, type) : null,
                    InvisibleRule = invisibleOn != null ? ResolveRule(invisibleOn, type) : null,
                    Prefixes = prefixes.Count > 0 ? prefixes.ToArray() : Array.Empty<PrefixMeta>(),
                    RawLabel = rawLabel,
                    BadgedLabel = badgedLabel,
                    Badge = badge,
                    Description = Flowing(draw.Tooltip),
                    Min = draw.Min,
                    Max = draw.Max,
                    Precision = draw.Precision,
                    Kind = settingKind,
                    FieldMask = fieldMask,
                };
            }
            meta.Fields = list;
            typeCache[type] = meta;
            return meta;
        }

        private static bool IsFieldVisible(FieldMeta fm, object container, Type type)
        {
            if (fm.VisibleRule != null)
                return EvaluateRule(fm.VisibleRule, container, fm.VisibleOn, type) == true;
            if (fm.InvisibleRule != null)
                return EvaluateRule(fm.InvisibleRule, container, fm.InvisibleOn, type) == false;
            return true;
        }

        /// <summary>
        /// Captures exact container references and VisibleOn/InvisibleOn outcomes.
        /// Returns false if any visible nested field is a value type (struct), because
        /// reflection boxes a fresh copy on each GetValue and cached rows would go stale.
        /// </summary>
        internal static bool CaptureVisibility(object container, Type type, DrawFieldMask mask, int depth, VisibilitySnapshot snapshot)
        {
            if (container == null)
                return true;
            snapshot.Slots.Add(new VisibilitySnapshot.NestedSlot(1, container, null));
            if (depth > 6)
                return true;
            var meta = MetaOf(type);
            if (meta.TypeMask.HasValue)
                mask = meta.TypeMask.Value;
            var fields = meta.Fields;
            for (var i = 0; i < fields.Length; i++)
            {
                var fm = fields[i];
                if (fm.HasDrawAttr)
                {
                    if (fm.IsIgnore)
                        continue;
                    var vis = IsFieldVisible(fm, container, type);
                    if (fm.VisibleRule != null || fm.InvisibleRule != null)
                        snapshot.Visibility.Add(vis);
                    if (!vis)
                        continue;
                }
                else if (Skipped(fm, mask))
                    continue;
                if (fm.IsOneTimeBool || !fm.IsNested)
                    continue;
                if (fm.Field.FieldType.IsValueType)
                    return false;
                var value = fm.Field.GetValue(container);
                if (value is UnityEngine.Object unityObject)
                {
                    snapshot.Slots.Add(new VisibilitySnapshot.NestedSlot(2, unityObject, unityObject.name));
                    continue;
                }
                if (value == null)
                {
                    snapshot.Slots.Add(new VisibilitySnapshot.NestedSlot(0, null, null));
                    continue;
                }
                var inner = fm.FieldMask ?? mask;
                if (!CaptureVisibility(value, fm.Field.FieldType, inner, depth + 1, snapshot))
                    return false;
            }
            return true;
        }

        /// <summary>Unity Mod Manager's <c>UI.Draw</c>, as rows: which fields it draws, and how.</summary>
        internal static void Walk(object container, Type type, DrawFieldMask mask, Action writeBack, List<SettingRow> rows, UnityModManager.ModEntry mod, int depth)
        {
            if (depth > 6)
                return;
            var meta = MetaOf(type);
            if (meta.TypeMask.HasValue)
                mask = meta.TypeMask.Value;
            var fields = meta.Fields;
            for (var i = 0; i < fields.Length; i++)
            {
                var fm = fields[i];
                if (fm.HasDrawAttr)
                {
                    if (fm.IsIgnore || !IsFieldVisible(fm, container, type))
                        continue;
                }
                else if (Skipped(fm, mask))
                    continue;
                // A one-time action drawn as a switch ("unticks itself") is the mod's own window's button:
                // the tablet saves a change at once, so a tick here would be saved and act again at the
                // next start. Realistic Firing 1.23.0, as published, draws six such switches.
                if (fm.IsOneTimeBool)
                    continue;
                // Drawn before the field, in their order, as that window draws them.
                var prefixes = fm.Prefixes;
                for (var p = 0; p < prefixes.Length; p++)
                {
                    var prefix = prefixes[p];
                    rows.Add(new SettingRow { Kind = prefix.Kind, Label = prefix.Label, Space = prefix.Space });
                }
                var f = fm.Field;
                if (fm.IsNested)
                {
                    var value = f.GetValue(container);
                    if (value is UnityEngine.Object unityObject)
                    {
                        rows.Add(Shown(fm.RawLabel, f, container, unityObject.name));
                        continue;
                    }
                    if (value == null)
                        continue;
                    var inner = fm.FieldMask ?? mask;
                    if (!string.IsNullOrEmpty(fm.RawLabel))
                        rows.Add(new SettingRow { Kind = SettingKind.Heading, Label = fm.RawLabel });
                    Action back = fm.IsValueType ? () =>
                    {
                        f.SetValue(container, value);
                        writeBack?.Invoke();
                    } : writeBack;
                    Walk(value, f.FieldType, inner, back, rows, mod, depth + 1);
                    continue;
                }
                if (fm.IsCustomGui)
                    continue;
                rows.Add(new SettingRow
                {
                    Kind = fm.Kind,
                    Label = fm.BadgedLabel,
                    Badge = fm.Badge,
                    Description = fm.Description,
                    Field = f,
                    Container = container,
                    WriteBack = writeBack,
                    Min = fm.Min,
                    Max = fm.Max,
                    Precision = fm.Precision,
                });
            }
        }

        private static bool Numeric(Type t) => t == typeof(int) || t == typeof(long) || t == typeof(float) || t == typeof(double);

        private static SettingRow Shown(string label, FieldInfo f, object container, string text) =>
            new SettingRow { Kind = SettingKind.Shown, Label = label + ": " + text, Field = f, Container = container };

        // Unity Mod Manager's rule for a field with no Draw attribute.
        private static bool Skipped(FieldMeta fm, DrawFieldMask mask)
        {
            if ((mask & DrawFieldMask.OnlyDrawAttr) != 0 || ((mask & DrawFieldMask.SkipNotSerialized) != 0 && fm.IsNotSerialized))
                return true;
            return ((mask & DrawFieldMask.Public) == 0 || !fm.IsPublic) && ((mask & DrawFieldMask.Serialized) == 0 || !fm.IsSerialized)
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

        private static ParsedRule ResolveRule(string rule, Type type)
        {
            var key = (type, rule ?? "");
            if (ruleCache.TryGetValue(key, out var cached))
                return cached;
            var parsed = new ParsedRule();
            try
            {
                var parts = rule.Split('|');
                if (parts.Length != 2)
                {
                    Unreadable(rule, type, "it must be 'Name|Value'");
                    ruleCache[key] = parsed;
                    return parsed;
                }
                const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                Type valueType;
                var name = parts[0].TrimStart('#');
                if (parts[0].StartsWith("#"))
                {
                    var property = type.GetProperty(name, Instance);
                    if (property == null || property.GetIndexParameters().Length != 0 || property.GetGetMethod(true) == null)
                    {
                        Unreadable(rule, type, $"no instance property {name}");
                        ruleCache[key] = parsed;
                        return parsed;
                    }
                    valueType = property.PropertyType;
                    parsed.Property = property;
                }
                else
                {
                    var field = type.GetField(name, Instance);
                    if (field == null)
                    {
                        Unreadable(rule, type, $"no instance field {name}");
                        ruleCache[key] = parsed;
                        return parsed;
                    }
                    valueType = field.FieldType;
                    parsed.Field = field;
                }
                if (!valueType.IsPrimitive && !valueType.IsEnum)
                {
                    Unreadable(rule, type, $"{valueType.Name} is not a primitive or an enum");
                    ruleCache[key] = parsed;
                    return parsed;
                }
                var wanted = valueType.IsEnum ? Enum.Parse(valueType, parts[1]) : Convert.ChangeType(parts[1], valueType);
                parsed.Wanted = wanted;
                parsed.WantedHash = wanted != null ? wanted.GetHashCode() : 0;
                parsed.Valid = true;
            }
            catch (Exception e)
            {
                Unreadable(rule, type, e.GetBaseException().Message);
            }
            ruleCache[key] = parsed;
            return parsed;
        }

        private static bool? EvaluateRule(ParsedRule parsed, object container, string rule, Type type)
        {
            if (parsed == null || !parsed.Valid)
                return null;
            try
            {
                var current = parsed.Property != null ? parsed.Property.GetValue(container, null) : parsed.Field.GetValue(container);
                return current != null && parsed.Wanted != null && current.GetHashCode() == parsed.WantedHash;
            }
            catch (Exception e)
            {
                return Unreadable(rule, type, e.GetBaseException().Message);
            }
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
        internal static bool? DependsOn(string rule, object container, Type type) =>
            EvaluateRule(ResolveRule(rule, type), container, rule, type);

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
            var key = (type, value);
            if (enumTextCache.TryGetValue(key, out var cached))
                return cached;
            var name = Enum.GetName(type, value);
            string text;
            if (name == null)
                text = value.ToString();
            else
            {
                text = null;
                var member = type.GetField(name, BindingFlags.Public | BindingFlags.Static);
                if (member != null)
                {
                    foreach (var attribute in member.GetCustomAttributes(false))
                    {
                        if (attribute is InspectorNameAttribute inspector && !string.IsNullOrEmpty(inspector.displayName))
                        {
                            text = inspector.displayName;
                            break;
                        }
                        if (attribute is DescriptionAttribute described && !string.IsNullOrEmpty(described.Description))
                        {
                            text = described.Description;
                            break;
                        }
                    }
                }
                if (text == null)
                    text = Words(name);
            }
            enumTextCache[key] = text;
            return text;
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
