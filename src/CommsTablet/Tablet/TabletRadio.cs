using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CommsTablet.Guests;
using CommsTablet.Pages;
using DV;
using DV.CabControls;
using DV.Customization.Gadgets;
using DV.InventorySystem;
using DV.UI;
using DV.UI.Inventory;
using DV.Utils;
using UnityEngine;

namespace CommsTablet.Tablet
{
    /// <summary>
    /// Drives the tablet's copies of the radio's list modes as
    /// <c>CommsRadioController</c> drives the radio's: enabled while held,
    /// updated each frame, used by a click or trigger, turned by the scroll
    /// wheel. The current page's mode is the active one. Multiplayer's and
    /// Custom Car Loader's patches on the mode classes act on these copies
    /// as on the radio's.
    /// <para>
    /// The item is also a gadget that mounts in a cab (decision 18). Its use
    /// places it on a mount only while it is aimed at a free one
    /// (<see cref="TabletMount"/>); everywhere else the use is the radio's.
    /// Mounted, the item is put away and its mounted form's screen shows
    /// this tablet; the modes, which aim from the tablet in hand, wait.
    /// </para>
    /// </summary>
    internal sealed class TabletRadio : MonoBehaviour
    {
        internal CommsRadioCrewVehicle Summon { get; private set; }
        internal CommsRadioCarSpawner Spawner { get; private set; }
        internal CommsRadioCargoLoader Cargo { get; private set; }
        internal TabletPaintMode Paint { get; private set; }
        internal TabletRearrangeMode Rearrange { get; private set; }
        internal JunctionRemoteLogic Switch { get; private set; }
        internal RerailController Rerail { get; private set; }
        internal CommsRadioCarDeleter Clear { get; private set; }
        internal CommsRadioStartup Startup { get; private set; }
        internal CommsRadioDamage Damage { get; private set; }
        internal CommsRadioLight Light { get; private set; }
        internal CommsRadioDisplay Display { get; private set; }
        internal ArrowLCD Arrow { get; private set; }
        private LaserBeamLineRenderer laser;

        internal readonly List<TabletPage> Pages = new List<TabletPage>();

        /// <summary>The home screen's apps, each a few of <see cref="Pages"/> behind tabs (decision 15).</summary>
        internal readonly List<TabletApp> Apps = new List<TabletApp>();

        /// <summary>The app the shown page belongs to; null on the home screen.</summary>
        internal TabletApp CurrentApp => Current == null ? null : Apps.FirstOrDefault(a => a.Holds(Current));
        internal TabletPage Current { get; private set; }

        /// <summary>Whether the tablet is in hand, its mode enabled and updating.</summary>
        internal bool Held { get; private set; }

        /// <summary>Raised when the tablet is taken in hand (true) or let go, stashed, or mounted (false): for the guests' views.</summary>
        internal event Action<bool> HeldChanged;

        /// <summary>The stock gadget item that mounts the tablet.</summary>
        internal GadgetItem Gadget { get; private set; }

        /// <summary>Whether the tablet is on a mount: its mounted form is out, this item put away.</summary>
        internal bool Mounted => Gadget != null && Gadget.Gadget != null && Gadget.Gadget.gameObject.activeInHierarchy;

        /// <summary>Where the tablet is now: its mounted form on a mount, else the item.</summary>
        internal Transform Where => Mounted ? Gadget.Gadget.transform : transform;

        /// <summary>The page's notice when a choice needs the tablet in hand.</summary>
        internal string HandNotice => Mounted ? TabletScreen.MountedNotice : "Pick the tablet up to aim";

        /// <summary>Counts uses, choices, and scrolls, so the screen redraws at once after one.</summary>
        internal int Version { get; private set; }

        private ItemBase item;
        private TabletScreen screen;
        private ItemScrolling scrolling;
        private Delegate workTrains;
        private bool listening;
        private Coroutine initializing;

        private void Awake()
        {
            Summon = GetComponentInChildren<CommsRadioCrewVehicle>(true);
            Spawner = GetComponentInChildren<CommsRadioCarSpawner>(true);
            Cargo = GetComponentInChildren<CommsRadioCargoLoader>(true);
            Paint = GetComponentInChildren<TabletPaintMode>(true);
            Rearrange = GetComponentInChildren<TabletRearrangeMode>(true);
            Switch = GetComponentInChildren<JunctionRemoteLogic>(true);
            Rerail = GetComponentInChildren<RerailController>(true);
            Clear = GetComponentInChildren<CommsRadioCarDeleter>(true);
            Startup = GetComponentInChildren<CommsRadioStartup>(true);
            Damage = GetComponentInChildren<CommsRadioDamage>(true);
            Light = GetComponentInChildren<CommsRadioLight>(true);
            Display = GetComponentInChildren<CommsRadioDisplay>(true);
            Arrow = GetComponentInChildren<ArrowLCD>(true);
            laser = GetComponentInChildren<LaserBeamLineRenderer>(true);
            Gadget = GetComponent<GadgetItem>();
            screen = GetComponent<TabletScreen>();
            var summon = new SummonPage(this);
            var buy = new BuyPage(this, summon);
            var spawn = new SpawnPage(this);
            var cargo = new CargoPage(this);
            var paint = new PaintPage(this);
            var wheels = new RearrangePage(this);
            var switches = new SwitchPage(this);
            var map = new MapPage(this);
            var rerail = new RerailPage(this);
            var clear = new ClearPage(this);
            var startup = new StartupPage(this);
            var damage = new DamagePage(this);
            Pages.AddRange(new TabletPage[] { summon, buy, spawn, cargo, paint, wheels, switches, map, rerail, clear, startup, damage });
            // The game's own interface glyphs where it has one (a paint roller is drawn in their style).
            Apps.Add(new TabletApp("Vehicles", "UI_TrainLoco", "vehicles", summon, buy, spawn));
            Apps.Add(new TabletApp("Cargo", "UI_Item", "cargo", cargo));
            Apps.Add(new TabletApp("Workshop", null, "workshop", paint, wheels));
            Apps.Add(new TabletApp("Switches", "UI_BookletSchematicMap", "switches", switches, map));
            Apps.Add(new TabletApp("Tools", "UI_Mods", "tools", rerail, clear, startup, damage));
            // The radio's flashlight, a tile that switches it, offered where the player's radio has its Light.
            // Not on the mount: the light is the item's, put away there.
            Apps.Add(new TabletApp("Light", "UI_Light", "light", ToggleLight, () => LightOn,
                () => Light != null && Light.light != null && isActiveAndEnabled && Radios.Allow(r => r.commsRadioLight)));
            foreach (var guest in GuestRegistry.All)
            {
                var page = new GuestPage(this, guest);
                Pages.Add(page);
                Apps.Add(new TabletApp(page));
            }
            var settings = new SettingsPage(this);
            Pages.Add(settings);
            Apps.Add(new TabletApp("Settings", null, "settings", settings));
            if (Paint != null)
                Paint.CarChanged += Changed;
            if (Rearrange != null)
                Rearrange.CarChanged += Changed;
            if (Summon != null)
            {
                Summon.CarSummoned += WorkTrains.Summoned;
                workTrains = WorkTrains.Follow(WorkTrainBought);
            }
            HideRadioFace();
        }

        // A work train bought at the radio joins the radio's summon list
        // only; this copy's is rebuilt here.
        private void WorkTrainBought(DV.ThingTypes.TrainCarLivery _)
        {
            if (Summon != null)
                GameMembers.SummonRefresh?.Invoke(Summon);
        }

        // The display's texts and the direction arrow are read onto the
        // screen; their own meshes, at the radio's face, stay out of sight.
        private void HideRadioFace()
        {
            if (Display != null)
                foreach (var renderer in Display.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = false;
            if (Arrow != null)
                foreach (var renderer in Arrow.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = false;
        }

        private void Start()
        {
            item = GetComponent<ItemBase>();
            if (item == null)
            {
                Main.Mod?.Logger.Error("The comms tablet has no item; its modes stay off");
                enabled = false;
                return;
            }
            // A saved guest's tablet loads as this one under the name it was saved by.
            if (name != TabletItem.PrefabName)
                name = TabletItem.PrefabName;
            laser?.EnableBeam(false);
            // The radio's direction arrow starts on in the prefab; the modes turn it
            // on as they aim, but none turns it off before it has been used.
            Arrow?.TurnOff();
            if (VRManager.IsVREnabled())
            {
                // In VR the modes aim along the laser, which follows the hand's pointer.
                // The gadget item's own scrolling is shared: one wheel, one handler each.
                scrolling = GetComponent<ItemScrolling>() ?? gameObject.AddComponent<ItemScrollingVR>();
                SetSignalOrigin(laser != null ? laser.transform : transform);
                Listen(true);
            }
            else
                initializing = SingletonBehaviour<CoroutineManager>.Instance.Run(InitializeNonVr());
        }

        // As the radio: on a desktop the modes aim from the player camera.
        private IEnumerator InitializeNonVr()
        {
            while (PlayerManager.PlayerCamera == null)
                yield return null;
            while (!SingletonBehaviour<HotbarController>.Instance)
                yield return null;
            initializing = null;
            if (this == null)
                yield break;
            scrolling = GetComponent<ItemScrolling>() ?? gameObject.AddComponent<ItemScrollingNonVR>();
            SetSignalOrigin(PlayerManager.PlayerCamera.transform);
            Listen(true);
        }

        private void SetSignalOrigin(Transform origin)
        {
            foreach (var mode in GetComponentsInChildren<ICommsRadioMode>(true))
                mode.OverrideSignalOrigin(origin);
        }

        private void Listen(bool on)
        {
            if (on == listening || item == null)
                return;
            listening = on;
            if (on)
            {
                item.Grabbed += OnGrabStart;
                item.Ungrabbed += OnGrabEnd;
                item.Used += OnUse;
                if (scrolling != null) scrolling.Scrolled += OnScrolled;
                // Taken up before the listeners existed: a saved tablet is
                // grabbed as it is first unstashed, before Start, and the
                // desktop setup waits a few frames.
                if (item.IsGrabbed()) OnGrabStart(item);
                return;
            }
            item.Grabbed -= OnGrabStart;
            item.Ungrabbed -= OnGrabEnd;
            item.Used -= OnUse;
            if (scrolling != null) scrolling.Scrolled -= OnScrolled;
        }

        private void OnGrabStart(ControlImplBase _)
        {
            var inventory = SingletonBehaviour<InventoryViewBase>.Instance;
            if (inventory != null) inventory.inventoryUI.OpenedOrClosed += BigInventoryToggled;
            CheckState();
        }

        private void OnGrabEnd(ControlImplBase _)
        {
            var inventory = SingletonBehaviour<InventoryViewBase>.Instance;
            if (inventory != null) inventory.inventoryUI.OpenedOrClosed -= BigInventoryToggled;
            CheckState();
        }

        private void BigInventoryToggled(bool _) => CheckState();

        private void CheckState()
        {
            var inventory = SingletonBehaviour<InventoryViewBase>.Instance;
            Toggle(item.IsGrabbed() && (inventory == null || !inventory.BigInventoryOpen));
        }

        private void Toggle(bool on)
        {
            if (on == Held)
                return;
            var mode = Current?.Mode;
            if (on)
            {
                Held = true;
                HeldChanged?.Invoke(true);
                if (mode == null)
                    return;
                mode.Enable();
                Beam(mode);
                Current.Reapply();
                return;
            }
            Current?.Capture();
            mode?.Disable();
            laser?.EnableBeam(false);
            Held = false;
            HeldChanged?.Invoke(false);
        }

        private void Beam(ICommsRadioMode mode)
        {
            if (laser == null)
                return;
            laser.SetBeamColor(mode.GetLaserBeamColor());
            laser.EnableBeam(true);
        }

        private void Update()
        {
            if (Held && Current?.Mode != null && !SingletonBehaviour<AppUtil>.Instance.IsTimePaused)
                Current.Mode.OnUpdate();
        }

        // The item's use: placing it, while aimed at a free mount (the gadget item takes it then); else the radio's.
        private void OnUse()
        {
            if (TabletMount.AimingAtMount(Gadget))
                return;
            UseMode();
        }

        private void UseMode()
        {
            if (SingletonBehaviour<ACanvasController<CanvasController.ElementType>>.Instance.IsOn(CanvasController.ElementType.Blockers))
                return;
            var page = Current;
            if (page is GuestPage guest)
            {
                if (screen != null && screen.GuestUse(guest))
                    Changed();
                return;
            }
            if (page?.Mode == null)
                return;
            page.Mode.OnUse();
            page.Used();
            Changed();
        }

        private void Changed() => Version++;

        // Aimed at a free mount, the wheel picks how the tablet sits on it (the gadget item's own handler).
        private void OnScrolled(ScrollAction direction)
        {
            if (TabletMount.AimingAtMount(Gadget))
                return;
            Scroll(direction.IsPositive());
        }

        /// <summary>The scroll wheel: to a guest's app, or the active mode's B (forward) or A button.</summary>
        internal bool Scroll(bool forward)
        {
            if (Current is GuestPage guest)
                return screen != null && screen.GuestScroll(guest, forward);
            return Step(forward);
        }

        /// <summary>
        /// Presses the active mode's B (<paramref name="forward"/>) or A
        /// button, as the scroll wheel does, in the steps where the mode takes
        /// them over; the radio's Switch and Clear modes complain otherwise.
        /// </summary>
        internal bool Step(bool forward)
        {
            var mode = Current?.Mode;
            if (mode == null || mode.ButtonBehaviour != ButtonBehaviourType.Override)
                return false;
            if (!Held)
            {
                Current.Notice = HandNotice;
                Changed();
                return false;
            }
            var acted = forward ? mode.ButtonBCustomAction() : mode.ButtonACustomAction();
            if (!acted)
                return false;
            Changed();
            if (TabletItem.SelectionSound != null)
                TabletItem.SelectionSound.Play(transform.position, 1f, 1f, 0f, 1f, 10f, default, null, transform);
            return true;
        }

        /// <summary>Plays a radio sound from the tablet, if there is one.</summary>
        internal void Sound(AudioClip clip)
        {
            if (clip != null)
                CommsRadioController.PlayAudioFromRadio(clip, transform);
        }

        /// <summary>Whether the tablet's flashlight is on.</summary>
        internal bool LightOn => Light != null && Light.light != null && Light.light.gameObject.activeSelf;

        /// <summary>Switches the flashlight, as the radio's Light mode does (with its click).</summary>
        internal void ToggleLight()
        {
            if (Light == null || Light.light == null)
                return;
            Light.OnUse();
            Changed();
        }

        /// <summary>Uses the active mode, as a click or trigger away from the screen does (a screen button: never a placement).</summary>
        internal void Use()
        {
            if (!Held && Current?.Mode != null)
            {
                Current.Notice = HandNotice;
                Changed();
                return;
            }
            UseMode();
        }

        /// <summary>Starts the active mode over from its first step.</summary>
        internal void Restart()
        {
            var mode = Current?.Mode;
            if (mode == null)
                return;
            if (!Held)
            {
                Current.Notice = HandNotice;
                Changed();
                return;
            }
            mode.Disable();
            Arrow?.TurnOff();
            if (Held) mode.Enable();
            mode.SetStartingDisplay();
            Changed();
        }

        /// <summary>
        /// Opens <paramref name="page"/>'s app and makes its mode the active
        /// one, or with null goes to the home screen. As on a phone, an app
        /// takes up where it was left: the choice it was aiming with when it
        /// was closed is taken up again.
        /// </summary>
        internal void Show(TabletPage page) => Show(page, true);

        private void Show(TabletPage page, bool resume)
        {
            if (page == Current)
                return;
            var old = Current?.Mode;
            // Paint and Wheels both choose a car: going from one to the other keeps it.
            var carried = old is TabletCarMode chooser ? chooser.Car : null;
            if (Held) Current?.Capture();
            old?.Disable();
            Arrow?.TurnOff();
            Current = page;
            var app = CurrentApp;
            if (app != null) app.Last = page;
            var mode = page?.Mode;
            if (mode == null)
            {
                laser?.EnableBeam(false);
                Changed();
                return;
            }
            if (Held)
            {
                mode.Enable();
                Beam(mode);
            }
            mode.SetStartingDisplay();
            if (Held && resume)
                page.Reapply();
            if (Held && carried != null && mode is TabletCarMode next && next.Car == null)
                next.Choose(carried);
            Changed();
        }

        /// <summary>
        /// Resets the page's mode, runs <paramref name="steps"/> (writing its
        /// choice and pressing it on to aiming, sounds held back), and checks
        /// where it landed. Off the hand the modes cannot aim (the radio's
        /// aiming steps start coroutines and update only while held), so the
        /// choice then waits for the tablet to be taken up.
        /// </summary>
        internal void Drive(TabletPage page, AudioClip confirm, Func<bool> steps)
        {
            Show(page, false);
            Changed();
            var mode = page.Mode;
            if (!Held || mode == null)
            {
                page.Notice = HandNotice;
                return;
            }
            bool landed;
            QuietSteps.Active = true;
            try
            {
                mode.Disable();
                mode.Enable();
                landed = steps();
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.LogException($"Choosing on the {page.Name} page failed", e);
                landed = false;
            }
            finally
            {
                QuietSteps.Active = false;
            }
            if (landed)
            {
                page.Notice = null;
                CommsRadioController.PlayAudioFromRadio(confirm, mode is Component c ? c.transform : transform);
                return;
            }
            // Another mod's patch held a step back (Multiplayer's summon cooldown, for one): start clean.
            mode.Disable();
            mode.Enable();
            mode.SetStartingDisplay();
            page.Notice = "Not ready; try again";
        }

        private void OnDisable()
        {
            if (UnloadWatcher.isUnloading)
                return;
            // Stashed, mounted, or dropped out of the world: as the radio, the mode stops.
            var wasHeld = Held;
            if (Held) Current?.Capture();
            Current?.Mode?.Disable();
            laser?.EnableBeam(false);
            Held = false;
            if (wasHeld)
                HeldChanged?.Invoke(false);
        }

        private void OnDestroy()
        {
            if (initializing != null && SingletonBehaviour<CoroutineManager>.Instance != null)
                SingletonBehaviour<CoroutineManager>.Instance.Stop(initializing);
            if (Summon != null)
                Summon.CarSummoned -= WorkTrains.Summoned;
            if (Paint != null)
                Paint.CarChanged -= Changed;
            if (Rearrange != null)
                Rearrange.CarChanged -= Changed;
            WorkTrains.Unfollow(workTrains);
            workTrains = null;
            if (!UnloadWatcher.isUnloading)
                Listen(false);
        }
    }
}
