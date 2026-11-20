using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace CommsTablet.Tablet
{
    /// <summary>One one-time action as the Settings app draws it: its place in the mod's list, its label, and the line under it.</summary>
    internal struct ModAction
    {
        internal int Index;
        internal string Label;
        internal string Help;
    }

 internal sealed class ModActions
    {
        internal const string ClassName = "CommsTabletSettings";

        /// <summary>The tablet's own: a spare tablet, as the button in its settings window gives one.</summary>
        internal const string OwnLabel = "Give me a comms tablet";

        private static readonly string[] OwnLabels = { OwnLabel };
        private static readonly string[] OwnHelp = { "A spare comms tablet in front of you, as the button in the mod's settings window gives." };

        private readonly string owner;
        private readonly Func<string[]> actions;
        private readonly Func<string[]> help;
        private readonly Func<int, string> act;
        private bool helpWarned;

        /// <summary>A call threw: the mod's actions are hidden for the rest of the session.</summary>
        internal bool Failed { get; private set; }

        private ModActions(string owner, Func<string[]> actions, Func<string[]> help, Func<int, string> act)
        {
            this.owner = owner;
            this.actions = actions;
            this.help = help;
            this.act = act;
        }

        /// <summary>The Comms Tablet's own action: "Give me a comms tablet".</summary>
        internal static ModActions Own() => new ModActions("Comms Tablet", () => OwnLabels, () => OwnHelp, i => i == 0 ? TabletItem.GiveToPlayer() : null);

        /// <summary>
        /// A mod's actions from its assembly's <c>CommsTabletSettings</c>, or
        /// null when it has none or they cannot be bound (logged once, with
        /// why).
        /// </summary>
        internal static ModActions Find(Assembly assembly, string owner)
        {
            Type type;
            try
            {
                type = assembly?.GetType(assembly.GetName().Name + "." + ClassName, false);
            }
            catch (Exception)
            {
                return null;
            }
            if (type == null)
                return null;
            ModActions found;
            string why;
            try
            {
                found = Bind(type, owner, out why);
            }
            catch (Exception e)
            {
                found = null;
                why = "reading it threw: " + e.GetBaseException().Message;
            }
            if (found == null)
                Main.Mod?.Logger.Warning($"{owner}'s {ClassName} gives the Settings app no buttons: {why}");
            return found;
        }

        /// <summary>Binds a <c>CommsTabletSettings</c> class: Actions and Act together, ActionHelp optional; null with why if not.</summary>
        internal static ModActions Bind(Type type, string owner, out string why)
        {
            why = null;
            var problems = new List<string>();
            var actions = Method<Func<string[]>>(type, "Actions", problems);
            var act = Method<Func<int, string>>(type, "Act", problems);
            var help = Method<Func<string[]>>(type, "ActionHelp", problems);
            if (problems.Count == 0 && (actions == null || act == null))
                problems.Add(actions == null && act == null ? "it has neither Actions nor Act" : $"it has {(actions != null ? "Actions" : "Act")} without {(actions != null ? "Act" : "Actions")}; they come together");
            if (problems.Count > 0)
            {
                why = string.Join("; ", problems);
                return null;
            }
            return new ModActions(owner, actions, help, act);
        }

        private static T Method<T>(Type type, string name, List<string> problems) where T : Delegate
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == name).ToList();
            if (methods.Count == 0)
                return null;
            foreach (var method in methods)
                if (Delegate.CreateDelegate(typeof(T), method, false) is T bound)
                    return bound;
            var invoke = typeof(T).GetMethod("Invoke");
            problems.Add($"{name} has the wrong signature ({Describe(methods[0])}; wanted {Describe(invoke, name)})");
            return null;
        }

        private static string Describe(MethodInfo method, string name = null) =>
            $"{method.ReturnType.Name} {name ?? method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})";

        /// <summary>
        /// The actions to draw now, in the mod's order: each with a label (an
        /// empty one skipped) and its help line when ActionHelp gives one of
        /// the same length. Empty after a failure.
        /// </summary>
        internal List<ModAction> List()
        {
            var list = new List<ModAction>();
            if (Failed)
                return list;
            string[] labels, lines = null;
            try
            {
                labels = actions();
                if (help != null)
                    lines = help();
            }
            catch (Exception e)
            {
                Fail("Actions or ActionHelp", e);
                return list;
            }
            if (labels == null)
                return list;
            if (lines != null && lines.Length != labels.Length)
            {
                if (!helpWarned)
                    Main.Mod?.Logger.Warning($"{owner}'s ActionHelp has {lines.Length} lines for {labels.Length} actions; its buttons are drawn without them");
                helpWarned = true;
                lines = null;
            }
            for (var i = 0; i < labels.Length; i++)
                if (!string.IsNullOrEmpty(labels[i]))
                    list.Add(new ModAction { Index = i, Label = labels[i], Help = string.IsNullOrEmpty(lines?[i]) ? null : lines[i] });
            return list;
        }

        /// <summary>Runs an action; the notice the mod returned, else what the tablet says instead.</summary>
        internal string Run(ModAction action)
        {
            if (Failed)
                return $"{action.Label}: not available; see the mod log";
            string notice;
            try
            {
                notice = act(action.Index);
            }
            catch (Exception e)
            {
                Fail("Act", e);
                return $"{action.Label}: it failed; see the mod log";
            }
            return string.IsNullOrEmpty(notice) ? $"{action.Label}: nothing happened" : notice;
        }

        private void Fail(string call, Exception error)
        {
            if (Failed)
                return;
            Failed = true;
            Main.Mod?.Logger.Error($"{owner}'s {ClassName} threw in {call}; its buttons are hidden for the rest of the session: {error}");
        }
    }
}
