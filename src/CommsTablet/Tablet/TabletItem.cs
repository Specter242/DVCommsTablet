using System;
using System.Linq;
using DV;
using DV.CabControls;
using DV.Customization;
using DV.Customization.Gadgets;
using DV.Interaction;
using DV.Utils;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CommsTablet.Tablet
{
 internal static class TabletItem
    {
        public const string PrefabName = "CommsTablet_Tablet";
        public const string DisplayName = "Comms tablet";
        /// <summary>The stock item the tablet is a copy of: a gadget item, as Realistic Firing's and AI Engineer's tablets.</summary>
        internal const string SourcePrefab = "Clinometer";
        /// <summary>The stock item whose modes the tablet carries.</summary>
        internal const string RadioPrefab = "CommsRadio";
        // The stock gadget body and nested-control layers: the mounted body, and the screen's buttons.
        internal const int BodyLayer = 13, ControlLayer = 22;
        internal const float Thickness = 0.016f;
        internal static readonly Vector3 Size = new Vector3(0.34f, 0.31f, Thickness);

        /// <summary>The radio's mode-selection click, taken from its controller.</summary>
        internal static AudioClip SelectionSound { get; private set; }

        private static GameObject template;
        private static bool buildFailed;

        public static GameObject Template
        {
            get
            {
                if (template == null && !buildFailed)
                {
                    template = Build();
                    buildFailed = template == null;
                }
                return template;
            }
        }

        private static GameObject Build()
        {
            GameObject holder = null;
            try
            {
                return BuildTemplate(ref holder);
            }
            catch (Exception error)
            {
                // A changed stock prefab must not abort the saved-item loader.
                if (holder != null) Object.Destroy(holder);
                Main.Mod?.Logger.Warning("Cannot build the comms tablet; saved tablets will be skipped: " + error);
                return null;
            }
        }

        private static GameObject Source(string name)
        {
            var spec = Globals.G?.Items?.items?.FirstOrDefault(s => s != null && s.ItemPrefabName == name);
            return spec != null ? spec.gameObject : Resources.Load<GameObject>(name);
        }

        private static GameObject BuildTemplate(ref GameObject holder)
        {
            var source = Source(SourcePrefab);
            var radioSource = Source(RadioPrefab);
            if (source == null || source.GetComponent<GadgetItem>() == null)
            {
                Main.Mod?.Logger.Warning($"No '{SourcePrefab}' gadget item to make the comms tablet from");
                return null;
            }
            if (radioSource == null || radioSource.GetComponent<CommsRadioController>() == null)
            {
                Main.Mod?.Logger.Warning($"No '{RadioPrefab}' prefab with a radio controller to take the comms tablet's modes from");
                return null;
            }
            holder = new GameObject("[CommsTablet templates]");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            var item = Object.Instantiate(source, holder.transform, false);
            item.name = PrefabName;
            item.transform.localScale = Vector3.one;
            RemoveStockModel(item.transform);

            // The radio's parts, from a copy of the radio that never wakes, moved onto the clinometer copy.
            var radioCopy = Object.Instantiate(radioSource, holder.transform, false);
            var radio = radioCopy.GetComponent<CommsRadioController>();
            SelectionSound = radio.selectionAction;
            var laser = radioCopy.GetComponentInChildren<LaserBeamLineRenderer>(true);
            if (laser == null || radioCopy.GetComponentInChildren<CommsRadioDisplay>(true) == null)
                throw new InvalidOperationException("the radio prefab has no laser or display");
            Strip(radioCopy.transform, laser.transform);
            ReplacePaintMode(radioCopy.transform);
            foreach (var part in radioCopy.transform.Cast<Transform>().ToList())
                part.SetParent(item.transform, false);
            Object.DestroyImmediate(radioCopy);

            foreach (var anchor in item.GetComponents<ICustomNonVRGrabAnchor>().OfType<Component>().ToList())
                Object.DestroyImmediate(anchor);
            foreach (var collider in item.GetComponents<Collider>())
                Object.DestroyImmediate(collider);

            // The beam leaves the top edge; in VR it is also where the modes aim from.
            laser.transform.localPosition = new Vector3(0f, Size.y * 0.5f, 0f);
            laser.transform.localRotation = Quaternion.identity;

            var box = item.AddComponent<BoxCollider>();
            box.size = Size;
            var spec = item.GetComponent<DV.CabControls.Spec.Item>();
            spec.colliderGameObjects = new[] { item };
            spec.rigidbodyMass = 1.2f;
            spec.allowPlayerRotationXAxisVr = true;
            spec.preventNestedGrabWhenUnGrabbed = false;
            var inventory = item.GetComponent<InventoryItemSpec>();
            AccessTools.Field(typeof(InventoryItemSpec), "itemPrefabName").SetValue(inventory, PrefabName);
            inventory.NameOverride = DisplayName;
            inventory.IsEssential = false;
            inventory.ImmuneToDumpster = false;
            inventory.PreviewBounds = new Bounds(Vector3.zero, Size);
            inventory.PreviewPrefab = TabletBody.Build(holder.transform).gameObject;
            inventory.PreviewPrefab.name = "Comms tablet preview";
            TabletBody.SetIcons(inventory);
            TabletBody.Build(item.transform);

            // The mounted form: the shell and a screen of the same tablet, on a gadget mount.
            var mounted = new GameObject(PrefabName + "_Gadget");
            mounted.transform.SetParent(holder.transform, false);
            var gadget = mounted.AddComponent<TabletGadget>();
            ConfigureGadget(gadget);
            TabletBody.Build(mounted.transform);
            mounted.AddComponent<BoxCollider>().size = Size;
            mounted.layer = BodyLayer;
            mounted.AddComponent<TabletScreen>();
            var itemGadget = item.GetComponent<GadgetItem>();
            AccessTools.Field(typeof(GadgetItem), "gadgetPrefab").SetValue(itemGadget, gadget);
            AccessTools.Field(typeof(GadgetItem), "onlyPlaceInOneAxis").SetValue(itemGadget, true);

            item.AddComponent<TabletRadio>();
            item.AddComponent<TabletScreen>();
            item.AddComponent<TabletDescription>();
            item.AddComponent<TabletHold>();
            Main.Mod?.Logger.Log("Comms tablet built from the stock clinometer (a gadget for a large mount) with the stock comms radio's Summon, Spawn, Cargo, Switch, Rerail, Clear, Startup, Damage, and Light, and a Paint mode of its own"
                + (SkinManagerLink.Present ? " (Skin Manager's skins)" : ""));
            return item;
        }

        /// <summary>
        /// Removes the clinometer's model, keeping the stock hand anchors:
        /// inventory and distance-grab align through them, even with
        /// precision grabbing (Realistic Firing's <c>RemoveStockModel</c>).
        /// </summary>
        private static void RemoveStockModel(Transform item)
        {
            for (var i = item.childCount - 1; i >= 0; i--)
            {
                var child = item.GetChild(i);
                if (child.name != "[anchor]" && child.name != "[left anchor]" && child.name != "[right anchor]")
                    Object.DestroyImmediate(child.gameObject);
            }
        }

        /// <summary>
        /// The mounted form, as Realistic Firing's (<c>FiringTablet.ConfigureGadget</c>):
        /// its bounds with the screen's buttons, two mount points (the stock
        /// large mount), taken off with an empty hand or the remover, no
        /// wiring, in a cab.
        /// </summary>
        private static void ConfigureGadget(GadgetBase gadget)
        {
            var bounds = new Bounds(Vector3.zero, Size);
            bounds.Encapsulate(new Vector3(0f, 0f, TabletScreen.ButtonFront));
            AccessTools.Field(typeof(GadgetBase), "boundsCenter").SetValue(gadget, bounds.center);
            AccessTools.Field(typeof(GadgetBase), "boundsSize").SetValue(gadget, bounds.size);
            AccessTools.Field(typeof(GadgetBase), "requiredMountPoints").SetValue(gadget, 2);
            AccessTools.Field(typeof(GadgetBase), "removalMethod").SetValue(gadget,
                GadgetBase.GadgetRemovalMethod.EmptyHand | GadgetBase.GadgetRemovalMethod.Remover);
            AccessTools.Field(typeof(GadgetBase), "vrIgnoreColliders").SetValue(gadget, new Collider[0]);
            AccessTools.Field(typeof(GadgetBase), "highlightMeshes").SetValue(gadget, new MeshFilter[0]);
            AccessTools.Field(typeof(TrainCarCustomization.TrainCarCustomizerBase), "requirements").SetValue(gadget,
                new TrainCarCustomization.TrainCarCustomizerBase.TrainCarRequirements
                {
                    trainCarPresence = TrainCarCustomization.TrainCarCustomizerBase.CustomizerTrainCarRequirements.RequireInterior,
                });
        }

        /// <summary>
        /// Removes every child of the radio's copy but the four list modes and
        /// the five point-and-press ones (with their highlighters) and the
        /// Light, the display, the direction arrow, and the laser, and points
        /// the kept modes at the laser, so none aims from a removed part. The
        /// radio's hand anchors go: the clinometer's are the tablet's.
        /// </summary>
        private static void Strip(Transform item, Transform laser)
        {
            for (var i = item.childCount - 1; i >= 0; i--)
            {
                var child = item.GetChild(i);
                var keep = child == laser
                    || child.GetComponent<CommsRadioCrewVehicle>() != null || child.GetComponent<CommsRadioCarSpawner>() != null
                    || child.GetComponent<CommsRadioCargoLoader>() != null || child.GetComponent<CommsRadioPaintjob>() != null
                    || child.GetComponent<JunctionRemoteLogic>() != null || child.GetComponent<RerailController>() != null
                    || child.GetComponent<CommsRadioCarDeleter>() != null || child.GetComponent<CommsRadioStartup>() != null
                    || child.GetComponent<CommsRadioDamage>() != null || child.GetComponent<CommsRadioLight>() != null
                    || child.GetComponent<CommsRadioDisplay>() != null || child.GetComponent<ArrowLCD>() != null;
                if (!keep)
                    Object.DestroyImmediate(child.gameObject);
            }
            foreach (var mode in item.GetComponentsInChildren<CommsRadioCrewVehicle>(true)) mode.signalOrigin = laser;
            foreach (var mode in item.GetComponentsInChildren<CommsRadioCarSpawner>(true)) mode.signalOrigin = laser;
            foreach (var mode in item.GetComponentsInChildren<CommsRadioCargoLoader>(true)) mode.signalOrigin = laser;
            foreach (var mode in item.GetComponentsInChildren<CommsRadioPaintjob>(true)) mode.signalOrigin = laser;
            // The radio's flashlight, on the tablet's top edge beside the laser, shining away from the player.
            foreach (var light in item.GetComponentsInChildren<CommsRadioLight>(true))
            {
                light.transform.localPosition = new Vector3(0.06f, Size.y * 0.5f, Thickness * 0.5f);
                light.transform.localRotation = Quaternion.identity;
                if (light.light != null)
                {
                    light.light.transform.localPosition = Vector3.zero;
                    light.light.transform.localRotation = Quaternion.identity;
                }
            }
            // The point-and-press modes, by their field: their OverrideSignalOrigin needs Awake first.
            foreach (var mode in item.GetComponentsInChildren<MonoBehaviour>(true))
                if (mode is JunctionRemoteLogic || mode is RerailController || mode is CommsRadioCarDeleter || mode is CommsRadioStartup || mode is CommsRadioDamage)
                {
                    var origin = AccessTools.Field(mode.GetType(), "signalOrigin");
                    if (origin != null && origin.FieldType == typeof(Transform))
                        origin.SetValue(mode, laser);
                    else
                        Main.Mod?.Logger.Warning($"The tablet's {mode.GetType().Name} has no signal origin to point at its laser");
                }
        }

        /// <summary>
        /// Puts the tablet's own car-first Paint mode in the stock one's place,
        /// with its highlighter, sounds, and themes (<see cref="TabletPaintMode"/>).
        /// </summary>
        private static void ReplacePaintMode(Transform item)
        {
            var stock = item.GetComponentInChildren<CommsRadioPaintjob>(true);
            if (stock == null)
                throw new InvalidOperationException("the radio prefab has no Paint mode");
            var mode = stock.gameObject.AddComponent<TabletPaintMode>();
            mode.signalOrigin = stock.signalOrigin;
            mode.display = stock.display;
            mode.validMaterial = stock.validMaterial;
            mode.themes = stock.themes;
            mode.hoverOverCar = stock.hoverOverCar;
            mode.confirmSound = stock.confirmSound;
            mode.cancelSound = stock.cancelSound;
            mode.trainHighlighter = stock.trainHighlighter;

            // Rearrange (decision 13) chooses locos the same way, with its own copy of the highlighter.
            var rearrange = new GameObject("RearrangeController");
            rearrange.transform.SetParent(stock.transform.parent, false);
            var wheels = rearrange.AddComponent<TabletRearrangeMode>();
            wheels.signalOrigin = stock.signalOrigin;
            wheels.display = stock.display;
            wheels.validMaterial = stock.validMaterial;
            wheels.hoverOverCar = stock.hoverOverCar;
            wheels.confirmSound = stock.confirmSound;
            wheels.cancelSound = stock.cancelSound;
            if (stock.trainHighlighter != null)
                wheels.trainHighlighter = Object.Instantiate(stock.trainHighlighter, rearrange.transform, false);

            // Change (DV Car Changer) too, with a copy of its own.
            var changer = new GameObject("ChangeController");
            changer.transform.SetParent(stock.transform.parent, false);
            var change = changer.AddComponent<TabletChangeMode>();
            change.signalOrigin = stock.signalOrigin;
            change.display = stock.display;
            change.validMaterial = stock.validMaterial;
            change.hoverOverCar = stock.hoverOverCar;
            change.confirmSound = stock.confirmSound;
            change.cancelSound = stock.cancelSound;
            if (stock.trainHighlighter != null)
                change.trainHighlighter = Object.Instantiate(stock.trainHighlighter, changer.transform, false);

            // Car-first guest apps (guest contract 2): kept inactive, copied for each such guest as the tablet builds its apps (TabletRadio).
            var guests = new GameObject("GuestCarController");
            guests.SetActive(false);
            guests.transform.SetParent(stock.transform.parent, false);
            var guest = guests.AddComponent<TabletGuestCarMode>();
            guest.signalOrigin = stock.signalOrigin;
            guest.display = stock.display;
            guest.validMaterial = stock.validMaterial;
            guest.hoverOverCar = stock.hoverOverCar;
            guest.confirmSound = stock.confirmSound;
            guest.cancelSound = stock.cancelSound;
            if (stock.trainHighlighter != null)
                guest.trainHighlighter = Object.Instantiate(stock.trainHighlighter, guests.transform, false);
            Object.DestroyImmediate(stock);
        }

        /// <summary>
        /// Adds the tablet to the game's item catalog, which Multiplayer reads
        /// to make items on clients (<see cref="MultiplayerItems"/>).
        /// </summary>
        public static bool RegisterAsGameItem()
        {
            var items = Globals.G?.Items?.items;
            var spec = Template != null ? Template.GetComponent<InventoryItemSpec>() : null;
            if (items == null || spec == null)
                return false;
            if (!items.Contains(spec))
                items.Add(spec);
            return true;
        }

        public static string GiveToPlayer()
        {
            var camera = PlayerManager.ActiveCamera;
            if (camera == null)
                return "No tablet: load a game first";
            if (Session.IsMultiplayerClientFresh)
                return "In multiplayer, only the host can give tablets";
            var source = Template;
            if (source == null)
                return "The comms tablet could not be built; see the mod log";
            var item = Object.Instantiate(source, camera.transform.position + camera.transform.forward * 0.6f, camera.transform.rotation);
            item.name = PrefabName;
            item.GetComponent<InventoryItemSpec>().BelongsToPlayer = true;
            var impl = item.GetComponent<ItemBase>();
            if (impl == null)
            {
                Object.Destroy(item);
                return "The comms tablet could not become an inventory item";
            }
            SingletonBehaviour<StorageController>.Instance.AddItemToWorldStorage(impl);
            return "A comms tablet is in front of you";
        }
    }

    /// <summary>
    /// The tablet on a gadget mount: no state of its own. Its screen shows the
    /// item's tablet (<see cref="TabletScreen"/>), pressed by pointing and
    /// clicking as the cab's own controls; the item is put away while it is
    /// mounted, so the radio's modes wait for it in hand.
    /// </summary>
    internal sealed class TabletGadget : GadgetBase
    {
    }

    internal sealed class TabletDescription : MonoBehaviour, IInventoryItemLocalizer
    {
        private void Awake() => GetComponent<InventoryItemSpec>().NameOverride = TabletItem.DisplayName;
        public string GetNameParam() => TabletItem.DisplayName;
        public string GetCustomDescription() => "The comms radio on a screen: press the round button for the home screen and open an app " +
            "(mouse mode while held on desktop), choose on the screen, then aim the tablet and use it as you would the radio. " +
            "To paint or rearrange, use it at a car first. The maps throw switches with the Dispatcher license. " +
            "It fits a large gadget mount in a cab (use it aimed at the mount; take it back with an empty hand); " +
            "mounted, its screen works by pointing and clicking, and aiming needs it in hand.";
    }

    /// <summary>
    /// Keeps the desktop screen beside the placement ray, independently of
    /// zoom. Realistic Firing's <c>FiringTabletHold</c> (d0f162e9).
    /// </summary>
    internal sealed class TabletHold : MonoBehaviour, ICustomNonVRGrabAnchor
    {
        public (Vector3 localPos, Quaternion localRot) GetGrabAnchor()
        {
            var camera = PlayerManager.ActiveCamera;
            var grabber = GetComponent<GrabHandlerItem>()?.GetGrabber();
            // The stock inventory also asks for this anchor when positioning an inventory drop.
            var attach = grabber?.Cursor?.Rig?.GetAttachPoint()
                ?? PlayerManager.PlayerTransform?.GetComponentInChildren<IPlayerRig>()?.GetAttachPoint();
            if (camera == null || attach == null)
                return (Vector3.zero, Quaternion.identity);
            var fov = GamePreferences.Get<float>(Preferences.FieldOfView);
            // The live attach pose includes stash and placement animations.
            // A hotbar switch grabs during them, so cancel only the resting offset.
            var sway = attach.parent != null ? attach.parent.GetComponentInChildren<ItemSway>() : null;
            var origin = sway != null
                ? new Vector3(sway.itemOffsetXY.x, sway.itemOffsetXY.y, sway.itemDistance.Evaluate(fov))
                : Quaternion.Inverse(attach.rotation) * (attach.position - camera.transform.position);
            return (CameraOffset(fov, camera.aspect) - origin, Quaternion.identity);
        }

        internal static Vector3 CameraOffset(float fieldOfView, float aspect)
        {
            // To the right of the placement ray, where the crosshair stays free to aim.
            var angle = Math.Max(30f, Math.Min(120f, fieldOfView)) * Math.PI / 360d;
            const float right = 0.21f;
            var halfWidth = right + TabletItem.Size.x * 0.5d + 0.04d;
            var halfHeight = Math.Max(0.21d, halfWidth / Math.Max(1d, aspect));
            var distance = (float)Math.Max(0.34d, halfHeight / Math.Tan(angle)) - TabletScreen.ButtonFront;
            return new Vector3(right, -0.035f, distance);
        }
    }
}
