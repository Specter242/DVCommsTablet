using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace CommsTablet.Guests
{
 internal static class GuestRegistry
    {
        internal const string ClassName = "CommsTabletApp";

        private static readonly List<GuestApp> bound = new List<GuestApp>();
        private static readonly List<string> rejected = new List<string>();
        private static readonly HashSet<Assembly> seen = new HashSet<Assembly>();
        private static int assemblyCount = -1;

        /// <summary>The guests bound: their contract taken and every member bound. A guest that later threw is still here, <see cref="GuestApp.Failed"/>.</summary>
        internal static IReadOnlyList<GuestApp> All
        {
            get
            {
                Scan();
                return bound;
            }
        }

        /// <summary>Each guest class found and not bound, with why (for the log and the self-test).</summary>
        internal static IReadOnlyList<string> Rejected
        {
            get
            {
                Scan();
                return rejected;
            }
        }

        internal static bool IsMerged(string id) => !string.IsNullOrEmpty(id) && All.Any(g => g.Id == id);

        internal static bool AnyMerged => All.Count > 0;

        /// <summary>Whether <paramref name="prefab"/> is a merged guest's own tablet, which the Comms Tablet replaces.</summary>
        internal static bool Retires(string prefab)
        {
            if (string.IsNullOrEmpty(prefab))
                return false;
            foreach (var guest in All)
                if (guest.Tablet == prefab)
                    return true;
            return false;
        }

        private static void Scan()
        {
            Assembly[] assemblies;
            try
            {
                assemblies = AppDomain.CurrentDomain.GetAssemblies();
            }
            catch (Exception)
            {
                return;
            }
            if (assemblies.Length == assemblyCount)
                return;
            assemblyCount = assemblies.Length;
            var own = typeof(GuestRegistry).Assembly;
            foreach (var assembly in assemblies)
            {
                if (assembly == own || !seen.Add(assembly))
                    continue;
                Type type;
                string name;
                try
                {
                    name = assembly.GetName().Name;
                    type = assembly.GetType(name + "." + ClassName, false);
                }
                catch (Exception)
                {
                    continue;
                }
                if (type == null)
                    continue;
                GuestApp guest;
                string why;
                try
                {
                    guest = GuestApp.Bind(type, name, out why);
                }
                catch (Exception e)
                {
                    guest = null;
                    why = "reading it threw: " + e.GetBaseException().Message;
                }
                var taken = guest != null ? bound.FirstOrDefault(g => g.Id == guest.Id) : null;
                if (taken != null)
                {
                    why = $"its Id '{taken.Id}' is taken by {taken.Assembly}";
                    guest = null;
                }
                if (guest == null)
                {
                    rejected.Add($"{name}: {why}");
                    Main.Mod?.Logger.Warning($"The guest app in {name} is not shown: {why}");
                    continue;
                }
                bound.Add(guest);
                Main.Mod?.Logger.Log($"Guest app '{guest.Name}' ({guest.Id}, contract {guest.Contract}) from {name} merged"
                    + (guest.Tablet != null ? $"; its own tablet '{guest.Tablet}' loads as the Comms Tablet" : ""));
            }
        }
    }

    /// <summary>
    /// One guest's <c>CommsTabletApp</c>, bound to delegates once (a member
    /// with the wrong signature fails here, never mid-frame). Every call goes
    /// through a guard: the first exception is logged, the guest is marked
    /// <see cref="Failed"/>, the screens close its views, and its app is
    /// hidden for the session.
    /// </summary>
    internal sealed class GuestApp
    {
        internal readonly string Id, Name, Assembly, Tablet;
        internal readonly int Contract;

        private Func<Texture2D> icon;
        private Func<bool> available;
        private Func<RectTransform, GameObject, Action, Action<int, Rect, bool>, Action<int>, int, bool, int> open;
        private Action<int> close, update, draw;
        private Action<int, bool> shown, held;
        private Action<int, int> pressed;
        private Func<int, Rect> pressArea;
        private Action<int, Vector2> pressAt;
        private Func<int, bool> use;
        private Func<int, bool, bool> scroll;
        // Contract 2: a car-first app (TabletGuestCarMode).
        private Func<TrainCar, bool> fits;
        private Action<int, TrainCar> chosen;
        private Func<Color> beamColor;

        private Texture2D iconTexture;
        private bool iconAsked;

        /// <summary>The guest threw: its app is hidden for the session and its views are closed.</summary>
        internal bool Failed { get; private set; }

        /// <summary>Whether the app works car first (contract 2's Fits and Chosen): the tablet aims, highlights, and chooses the car for it.</summary>
        internal bool CarFirst => fits != null;

#if !PUBLIC_RELEASE
        /// <summary>The call that threw, for the self-test.</summary>
        internal string FailedIn { get; private set; }
#endif

        private GuestApp(string id, string name, string assembly, string tablet, int contract)
        {
            Id = id;
            Name = name;
            Assembly = assembly;
            Tablet = tablet;
            Contract = contract;
        }

        internal static GuestApp Bind(Type type, string assembly, out string why)
        {
            why = null;
            if (!Constant(type, "Contract", out int contract))
            {
                why = "it has no int constant Contract";
                return null;
            }
            if (contract < GuestContract.Min || contract > GuestContract.Max)
            {
                why = $"it is written for guest contract {contract}; this Comms Tablet takes contracts {GuestContract.Min} to {GuestContract.Max}";
                return null;
            }
            if (!Constant(type, "Id", out string id) || string.IsNullOrEmpty(id) || !Constant(type, "Name", out string name) || string.IsNullOrEmpty(name))
            {
                why = "it needs string constants Id and Name, not empty";
                return null;
            }
            Constant(type, "Tablet", out string tablet);
            var guest = new GuestApp(id, name, assembly, string.IsNullOrEmpty(tablet) ? null : tablet, contract);
            var problems = new List<string>();
            guest.icon = Method<Func<Texture2D>>(type, "Icon", true, problems);
            guest.available = Method<Func<bool>>(type, "Available", true, problems);
            guest.open = Method<Func<RectTransform, GameObject, Action, Action<int, Rect, bool>, Action<int>, int, bool, int>>(type, "Open", true, problems);
            guest.close = Method<Action<int>>(type, "Close", true, problems);
            guest.shown = Method<Action<int, bool>>(type, "Shown", true, problems);
            guest.held = Method<Action<int, bool>>(type, "Held", true, problems);
            guest.update = Method<Action<int>>(type, "Update", true, problems);
            guest.draw = Method<Action<int>>(type, "Draw", true, problems);
            guest.pressed = Method<Action<int, int>>(type, "Pressed", true, problems);
            guest.pressArea = Method<Func<int, Rect>>(type, "PressArea", true, problems);
            guest.pressAt = Method<Action<int, Vector2>>(type, "PressAt", true, problems);
            guest.use = Method<Func<int, bool>>(type, "Use", false, problems);
            guest.scroll = Method<Func<int, bool, bool>>(type, "Scroll", false, problems);
            // Contract 2's car-first members, only for a guest written for it: Fits and Chosen together, BeamColor optional.
            if (contract >= 2)
            {
                guest.fits = Method<Func<TrainCar, bool>>(type, "Fits", false, problems);
                guest.chosen = Method<Action<int, TrainCar>>(type, "Chosen", false, problems);
                guest.beamColor = Method<Func<Color>>(type, "BeamColor", false, problems);
                var hasFits = type.GetMethods(BindingFlags.Public | BindingFlags.Static).Any(m => m.Name == "Fits");
                var hasChosen = type.GetMethods(BindingFlags.Public | BindingFlags.Static).Any(m => m.Name == "Chosen");
                if (hasFits != hasChosen)
                    problems.Add("a car-first app needs both Fits and Chosen");
                if (guest.beamColor != null && !hasFits)
                    problems.Add("BeamColor is for a car-first app (Fits and Chosen)");
            }
            if (problems.Count > 0)
            {
                why = string.Join("; ", problems);
                return null;
            }
            return guest;
        }

        private static bool Constant<T>(Type type, string name, out T value)
        {
            value = default;
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Static);
            if (field == null || field.FieldType != typeof(T))
                return false;
            value = (T)(field.IsLiteral ? field.GetRawConstantValue() : field.GetValue(null));
            return true;
        }

        private static T Method<T>(Type type, string name, bool required, List<string> problems) where T : Delegate
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == name).ToList();
            if (methods.Count == 0)
            {
                if (required)
                    problems.Add(name + " is missing");
                return null;
            }
            foreach (var method in methods)
                if (Delegate.CreateDelegate(typeof(T), method, false) is T bound)
                    return bound;
            problems.Add($"{name} has the wrong signature ({Describe(methods[0])}; wanted {Describe(typeof(T).GetMethod("Invoke"))})");
            return null;
        }

        private static string Describe(MethodInfo method) =>
            $"{method.ReturnType.Name} {method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})";

        private void Fail(string call, Exception error)
        {
            if (Failed)
                return;
            Failed = true;
#if !PUBLIC_RELEASE
            FailedIn = call;
#endif
            Main.Mod?.Logger.Error($"Guest app '{Name}' ({Id}, from {Assembly}) threw in {call}; it is closed and hidden for the rest of the session: {error}");
        }

        // ---- Guarded calls -----------------------------------------------------

        /// <summary>The guest's icon (white on clear), asked for once; null if it has none or threw.</summary>
        internal Texture2D Icon
        {
            get
            {
                if (!iconAsked && !Failed)
                {
                    iconAsked = true;
                    try
                    {
                        iconTexture = icon();
                    }
                    catch (Exception e)
                    {
                        Fail(nameof(Icon), e);
                    }
                }
                return iconTexture;
            }
        }

        internal bool Available
        {
            get
            {
                if (Failed)
                    return false;
                try
                {
                    return available();
                }
                catch (Exception e)
                {
                    Fail(nameof(Available), e);
                    return false;
                }
            }
        }

        /// <summary>Opens a view on a region; -1 if it threw.</summary>
        internal int Open(RectTransform region, GameObject tablet, Action invalidate, Action<int, Rect, bool> placeSlot, Action<int> hideSlotsFrom, int slotCount, bool vr)
        {
            if (Failed)
                return -1;
            try
            {
                return open(region, tablet, invalidate, placeSlot, hideSlotsFrom, slotCount, vr);
            }
            catch (Exception e)
            {
                Fail(nameof(Open), e);
                return -1;
            }
        }

        /// <summary>Closes a view; called even after the guest failed, its exception then only logged.</summary>
        internal void Close(int view)
        {
            try
            {
                close(view);
            }
            catch (Exception e)
            {
                if (Failed)
                    Main.Mod?.Logger.Warning($"Guest app '{Name}' also threw closing its view: {e.GetBaseException().Message}");
                else
                    Fail(nameof(Close), e);
            }
        }

        internal void Shown(int view, bool on)
        {
            if (Failed)
                return;
            try
            {
                shown(view, on);
            }
            catch (Exception e)
            {
                Fail(nameof(Shown), e);
            }
        }

        internal void Held(int view, bool on)
        {
            if (Failed)
                return;
            try
            {
                held(view, on);
            }
            catch (Exception e)
            {
                Fail(nameof(Held), e);
            }
        }

        internal void Update(int view)
        {
            if (Failed)
                return;
            try
            {
                update(view);
            }
            catch (Exception e)
            {
                Fail(nameof(Update), e);
            }
        }

        internal void Draw(int view)
        {
            if (Failed)
                return;
            try
            {
                draw(view);
            }
            catch (Exception e)
            {
                Fail(nameof(Draw), e);
            }
        }

        internal void Pressed(int view, int slot)
        {
            if (Failed)
                return;
            try
            {
                pressed(view, slot);
            }
            catch (Exception e)
            {
                Fail(nameof(Pressed), e);
            }
        }

        internal Rect PressArea(int view)
        {
            if (Failed)
                return Rect.zero;
            try
            {
                return pressArea(view);
            }
            catch (Exception e)
            {
                Fail(nameof(PressArea), e);
                return Rect.zero;
            }
        }

        internal void PressAt(int view, Vector2 canvas)
        {
            if (Failed)
                return;
            try
            {
                pressAt(view, canvas);
            }
            catch (Exception e)
            {
                Fail(nameof(PressAt), e);
            }
        }

        /// <summary>The item's use (a click or trigger away from the screen); false if the guest has no Use or did not take it.</summary>
        internal bool Use(int view)
        {
            if (Failed || use == null)
                return false;
            try
            {
                return use(view);
            }
            catch (Exception e)
            {
                Fail(nameof(Use), e);
                return false;
            }
        }

        /// <summary>Whether a car aimed at can be chosen for the app; false if it is not car first, or threw.</summary>
        internal bool Fits(TrainCar car)
        {
            if (Failed || fits == null || car == null)
                return false;
            try
            {
                return fits(car);
            }
            catch (Exception e)
            {
                Fail(nameof(Fits), e);
                return false;
            }
        }

        /// <summary>Tells a view the car the tablet chose for it; null when it let the car go.</summary>
        internal void Chosen(int view, TrainCar car)
        {
            if (Failed || chosen == null)
                return;
            try
            {
                chosen(view, car);
            }
            catch (Exception e)
            {
                Fail(nameof(Chosen), e);
            }
        }

        /// <summary>The beam's color while the app aims; null to take the tablet's.</summary>
        internal Color? BeamColor()
        {
            if (Failed || beamColor == null)
                return null;
            try
            {
                return beamColor();
            }
            catch (Exception e)
            {
                Fail(nameof(BeamColor), e);
                return null;
            }
        }

        /// <summary>The scroll wheel (or the VR stick); false if the guest has no Scroll or did not take it.</summary>
        internal bool Scroll(int view, bool forward)
        {
            if (Failed || scroll == null)
                return false;
            try
            {
                return scroll(view, forward);
            }
            catch (Exception e)
            {
                Fail(nameof(Scroll), e);
                return false;
            }
        }
    }
}
