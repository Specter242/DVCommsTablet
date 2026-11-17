using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CommsTablet;
using CommsTablet.Tablet;
using DV;
using HarmonyLib;
using Console = System.Console;

namespace CommsTablet.Check
{
    /// <summary>
    /// Checks the mod's contract with the installed game, without Unity: the
    /// private members it reads and writes, its Harmony targets, the prefab
    /// loads it wraps at the save and shop sites, and its links to
    /// Multiplayer and Custom Car Loader when those are given. Usage:
    /// <c>CommsTablet.Check.exe MANAGED_DIR [MOD_ASSEMBLY ...]</c>.
    /// </summary>
    internal static class Program
    {
        private static readonly List<string> Failures = new List<string>();
        private static int passed;

        private static int Main(string[] args)
        {
            if (args.Length < 1 || !Directory.Exists(args[0]))
            {
                Console.Error.WriteLine("usage: CommsTablet.Check.exe MANAGED_DIR [MOD_ASSEMBLY ...]");
                return 2;
            }
            var managed = args[0];
            var searched = new[] { managed, Path.Combine(managed, "UnityModManager") }
                .Concat(args.Skip(1).Select(Path.GetDirectoryName)).ToList();
            AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
            {
                var name = new AssemblyName(e.Name).Name + ".dll";
                var path = searched.Select(d => Path.Combine(d, name)).FirstOrDefault(File.Exists);
                return path != null ? Assembly.LoadFrom(path) : null;
            };
            var mods = args.Skip(1).Where(File.Exists).Select(Assembly.LoadFrom).Select(a => a.GetName().Name).ToList();
            Console.WriteLine("Optional mods loaded: " + (mods.Count > 0 ? string.Join(", ", mods) : "none"));

            Members();
            Targets();
            Mounting();
            LoadSites();
            GuestBinding();
            SettingsRows();
            Release();
            Optional(mods);

            Console.WriteLine($"{passed} checks passed, {Failures.Count} failed");
            foreach (var failure in Failures)
                Console.WriteLine("FAIL " + failure);
            return Failures.Count == 0 ? 0 : 1;
        }

        private static void Check(bool ok, string what, string detail = null)
        {
            if (ok) passed++;
            else Failures.Add(string.IsNullOrEmpty(detail) ? what : $"{what} ({detail})");
        }

        private static void Release()
        {
            var mod = typeof(Main).Assembly;
            var settings = mod.GetTypes().Where(t => typeof(UnityModManagerNet.UnityModManager.ModSettings).IsAssignableFrom(t)).Select(t => t.FullName).ToList();
            Check(settings.Count == 0, "the mod has no settings of its own (no Settings.xml, nothing hidden there)", string.Join(", ", settings));
#if PUBLIC_RELEASE
            var friends = mod.GetCustomAttributes<System.Runtime.CompilerServices.InternalsVisibleToAttribute>().Select(a => a.AssemblyName).ToList();
            Check(friends.SequenceEqual(new[] { "CommsTablet.Check" }), "the public build opens its internals to the contract check alone", string.Join(", ", friends));
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var hooks = mod.GetTypes().SelectMany(t => t.GetMembers(all))
                .Where(m => m.Name.IndexOf("ForTest", StringComparison.Ordinal) >= 0 || m.Name.IndexOf("SelfTest", StringComparison.Ordinal) >= 0)
                .Select(m => $"{m.DeclaringType?.Name}.{m.Name}").Distinct().ToList();
            Check(hooks.Count == 0, "the public build has none of the self-test's hooks", string.Join(", ", hooks));
            Console.WriteLine("Public release build: the self-test's access and hooks checked absent");
#endif
        }

        // Every private member resolves with the type the mod expects.
        private static void Members()
        {
            foreach (var missing in GameMembers.Missing)
                Failures.Add("game member " + missing);
            Check(GameMembers.SummonResolves, "Summon's members");
            Check(GameMembers.SummonRefresh != null, "the crew vehicle's UpdateAvailableVehicles (work trains bought)");
            Check(GameMembers.SpawnResolves, "Spawn's members");
            Check(GameMembers.CargoResolves, "Cargo's members");
            Check(Enum.GetNames(GameMembers.CargoState.FieldType).SequenceEqual(new[] { "EnterLoadMode", "PickCargoAndLoad" }),
                "the cargo loader's states are in the order the tablet steps them");
            Check(GameMembers.RadioModes != null && GameMembers.RadioDisabled != null, "the radio controller's mode list and disabled set");
            Check(Enum.GetNames(GameMembers.SpawnCategory.FieldType).SequenceEqual(new[] { "Loco", "Car" }), "the spawner's categories are Loco, Car");
            Check(Enum.GetNames(GameMembers.SpawnState.FieldType).SequenceEqual(new[]
                { "EnterSpawnMode", "PickCategory", "PickLoco", "PickCarType", "PickCarLivery", "PickDestination" }),
                "the spawner's states are in the order the tablet steps them");
            Check(GameMembers.RenderPageStep != null && GameMembers.RenderPageStep.DeclaringType == typeof(DV.RenderTextureSystem.BookletRender.StaticTextureRenderBase)
                  && AccessTools.DeclaredMethod(typeof(DV.RenderTextureSystem.BookletRender.StaticPagesRender), "GetStaticTemplatePaperData") != null,
                "the booklet render's page step (GetStaticTemplatePaperData), run on the map's copies before their pages are read");
            Check(Enum.GetNames(typeof(DV.Items.JunctionMap.JunctionMapType)).Contains("World") && Enum.GetNames(typeof(DV.Items.JunctionMap.JunctionMapType)).Contains("Station"),
                "the junction map's World and Station pages (the map's route map and booklet)");
            Check(Enum.GetNames(typeof(CommsRadioCrewVehicle.State)).SequenceEqual(new[]
                { "EnterSpawnMode", "PickCrewVehicle", "PickDestination", "ConfirmSummon", "CancelSummon" }),
                "the crew vehicle's states are in the order the tablet steps them");
        }

        // Each Harmony target the mod names is in the game.
        private static void Targets()
        {
            var radio = typeof(CommsRadioController);
            Check(AccessTools.DeclaredMethod(radio, "Awake") != null, "CommsRadioController.Awake (radio registry)");
            Check(AccessTools.DeclaredMethod(radio, "OnDestroy") != null, "CommsRadioController.OnDestroy (radio registry)");
            Check(AccessTools.DeclaredMethod(radio, nameof(CommsRadioController.PlayAudioFromRadio)) != null, "CommsRadioController.PlayAudioFromRadio (quiet steps)");
            Check(AccessTools.DeclaredMethod(typeof(DV.Shops.GlobalShopController), "Awake") != null, "GlobalShopController.Awake (shop)");
            Check(AccessTools.Field(typeof(DV.CashRegister.CashRegisterWithModules), "buyButton") != null
                  && AccessTools.Method(typeof(DV.CashRegister.CashRegisterWithModules), "OnUnitsToBuyChanged") != null,
                "the cash register's buy button and units handler (shop tag)");
            Check(AccessTools.Field(typeof(InventoryItemSpec), "itemPrefabName") != null
                  && AccessTools.Field(typeof(InventoryItemSpec), "itemIconSprite") != null
                  && AccessTools.Field(typeof(InventoryItemSpec), "itemIconSpriteStandard") != null
                  && AccessTools.Field(typeof(InventoryItemSpec), "itemIconSpriteDropped") != null,
                "the inventory spec's prefab name and icon fields");
            Check(AccessTools.Field(typeof(DV.Shops.ShopItemData), "amount") != null, "ShopItemData.amount (shop stock)");
            Check(new[] { "cargoLoaderControl", "carPaintjobControl" }.All(f => AccessTools.Field(radio, f) is FieldInfo field && field.IsPublic),
                "the radio's public Cargo and Paint modes (pages hidden where the radio has them off)");
            // The tablet's Paint mode takes these over from the stock one when the tablet is built.
            var paint = typeof(CommsRadioPaintjob);
            var taken = new (string name, Type type)[]
            {
                ("signalOrigin", typeof(UnityEngine.Transform)), ("display", typeof(CommsRadioDisplay)),
                ("validMaterial", typeof(UnityEngine.Material)), ("themes", typeof(DV.Customization.Paint.PaintTheme[])),
                ("hoverOverCar", typeof(UnityEngine.AudioClip)), ("confirmSound", typeof(UnityEngine.AudioClip)),
                ("cancelSound", typeof(UnityEngine.AudioClip)), ("trainHighlighter", typeof(UnityEngine.GameObject)),
            };
            var wrong = taken.Where(t => !(AccessTools.Field(paint, t.name) is FieldInfo f && f.IsPublic && f.FieldType == t.type)).Select(t => t.name).ToList();
            Check(wrong.Count == 0, "the stock Paint mode's highlighter, sounds, and themes the tablet takes over", string.Join(", ", wrong));
            Check(AccessTools.Field(typeof(CommsRadioCargoLoader), "confirmSound") is FieldInfo confirm && confirm.IsPublic,
                "the cargo loader's confirm sound");
            Check(AccessTools.Property(typeof(DV.ThingTypes.DVObjectModel), "CarTypeToLoadableCargo") != null,
                "the game's cargo-by-car-type table (Cargo page)");
        }

        // The save and shop sites still load a prefab by name, which the tablet's lookup wraps.
        private static void LoadSites()
        {
            var sites = TabletLoading.TargetMethods().ToList();
            Check(sites.Count == 2 && sites.All(s => s != null), "the saved-item and bought-item load sites exist");
            foreach (var site in sites.Where(s => s != null))
            {
                var loads = PatchProcessor.GetOriginalInstructions(site)
                    .Count(i => i.operand is MethodInfo m && TabletLoading.IsLoader(m));
                Check(loads > 0, $"{site.DeclaringType?.Name}.{site.Name} loads a prefab by name ({loads} found)");
            }
            var stock = AccessTools.GetDeclaredMethods(typeof(UnityEngine.Resources))
                .First(m => m.Name == "Load" && !m.IsGenericMethodDefinition && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
            Check(TabletLoading.IsLoader(stock), "Resources.Load(string) counts as a loader");
            Check(!TabletLoading.IsLoader(AccessTools.Method(typeof(Program), nameof(Describe))), "an unrelated method does not");
        }

        private static UnityEngine.Object Describe(string _) => null;

        // The tablet is a stock gadget item (decision 18): the members its build, its use, and the retirement of guests' tablets use.
        private static void Mounting()
        {
            var item = typeof(DV.Customization.Gadgets.GadgetItem);
            Check(AccessTools.Field(item, "gadgetPrefab")?.FieldType == typeof(DV.Customization.Gadgets.GadgetBase)
                  && AccessTools.Field(item, "onlyPlaceInOneAxis")?.FieldType == typeof(bool),
                "the gadget item's gadget prefab and one-axis placement (the tablet's mounted form)");
            Check(AccessTools.DeclaredMethod(item, "OnUsed", Type.EmptyTypes) != null && AccessTools.DeclaredMethod(item, "Update", Type.EmptyTypes) != null,
                "GadgetItem.OnUsed and Update (placement only while aimed at a free mount)");
            Check(GameMembers.GadgetContext != null && GameMembers.GadgetPlace != null
                  && new[] { "reachOverride", "telegrab", "isPressed", "wasPressed" }.All(f => AccessTools.Field(typeof(DV.Customization.Gadgets.GadgetItem.GadgetPlacingContext), f)?.IsPublic == true),
                "the gadget item's placing context and place flag");
            Check(AccessTools.DeclaredMethod(typeof(DV.Customization.Gadgets.GadgetHandBase), "OnUpdate",
                      new[] { typeof(DV.Customization.Gadgets.GadgetBase), typeof(UnityEngine.Rigidbody), typeof(DV.Customization.Gadgets.MountPoint), typeof(bool) }) != null
                  && AccessTools.Field(typeof(DV.Customization.Gadgets.GadgetHandNonVR), "grabber")?.IsPublic == true,
                "the empty hand's gadget update and its grabber (no removal highlight over the mounted screen's buttons)");
            var gadget = typeof(DV.Customization.Gadgets.GadgetBase);
            var fields = new (string name, Type type)[]
            {
                ("boundsCenter", typeof(UnityEngine.Vector3)), ("boundsSize", typeof(UnityEngine.Vector3)), ("requiredMountPoints", typeof(int)),
                ("removalMethod", typeof(DV.Customization.Gadgets.GadgetBase.GadgetRemovalMethod)),
                ("vrIgnoreColliders", typeof(UnityEngine.Collider[])), ("highlightMeshes", typeof(UnityEngine.MeshFilter[])),
            };
            var wrong = fields.Where(f => AccessTools.Field(gadget, f.name)?.FieldType != f.type).Select(f => f.name).ToList();
            Check(wrong.Count == 0, "the gadget's bounds, mount points, removal, and highlight fields (the mounted form)", string.Join(", ", wrong));
            Check(AccessTools.Field(typeof(DV.Customization.TrainCarCustomization.TrainCarCustomizerBase), "requirements")?.FieldType
                  == typeof(DV.Customization.TrainCarCustomization.TrainCarCustomizerBase.TrainCarRequirements),
                "the customizer's requirements (a self-powered screen, in a cab)");
            Check(AccessTools.Property(gadget, "GadgetItem")?.GetMethod?.IsPublic == true && AccessTools.Property(gadget, "MountedOn") != null,
                "the gadget's item and mount (the mounted screen shows the item's tablet)");
            var controller = typeof(StartingItemsController);
            var kit = AccessTools.DeclaredMethod(controller, "GetStartingItemsPrefabs", new[] { typeof(SaveGameData) });
            Check(kit != null && kit.ReturnType == typeof(List<DV.Items.StartingItems.StartingItem>), "StartingItemsController.GetStartingItemsPrefabs (the tablet in the starting kit)");
            var combine = AccessTools.DeclaredMethod(controller, "CombineAndResolveIllegalDuplicates");
            Check(combine != null && combine.ReturnType == typeof(List<StorageItemData>), "StartingItemsController.CombineAndResolveIllegalDuplicates (saved guest tablets renamed)");
            Check(AccessTools.Field(typeof(StorageItemData), "itemPrefabName") is FieldInfo name && name.IsPublic && !name.IsInitOnly && name.FieldType == typeof(string),
                "a saved item's prefab name can be rewritten");
        }

        // The guest contract binds a well-formed guest, and refuses another contract or a wrong signature at binding.
        private static void GuestBinding()
        {
            Check(GuestContract.Min == 1 && GuestContract.Max == 1, "the guest contract is version 1");
            var good = Guests.GuestApp.Bind(typeof(GoodGuest), "GoodGuest", out var why);
            Check(good != null && good.Id == "good" && good.Tablet == "Good_Tablet" && good.Contract == 1, "a well-formed guest binds", why);
            Check(Guests.GuestApp.Bind(typeof(LaterGuest), "LaterGuest", out why) == null && why != null && why.Contains("99") && why.Contains("1 to 1"),
                "a guest of another contract is refused, naming both versions", why);
            Check(Guests.GuestApp.Bind(typeof(WrongGuest), "WrongGuest", out why) == null && why != null && why.Contains("Open") && why.Contains("wrong signature"),
                "a guest whose Open has the wrong signature is refused at binding", why);
        }

        // The Settings app reads settings as Unity Mod Manager's window does (decision 19), presented as decision 20 has it.
        private static void SettingsRows()
        {
            var probe = new ProbeSettings();
            var rows = new List<SettingRow>();
            ModSettingsLink.Walk(probe, typeof(ProbeSettings), UnityModManagerNet.DrawFieldMask.OnlyDrawAttr, null, rows, null, 0);
            string Shape(IEnumerable<SettingRow> read) => string.Join(", ", read.Select(r => $"{r.Kind} {r.Field?.Name ?? r.Label}".TrimEnd()));
            var shape = Shape(rows);
            Check(shape == "Heading Probe, Toggle Switch, Choice Pick, Number Number, Space, Number Count, Toggle Dependent, Shown Key, Shown Text, Heading Box, Toggle On, "
                  + "Heading Pair, Number A, Toggle Later, Choice Season, Choice Named",
                "settings rows follow Unity Mod Manager's drawing: headings, spaces, switches, choices, numbers, shown values, boxes, VisibleOn, no undrawn or window-only fields", shape);
            var number = rows.First(r => r.Field?.Name == "Number");
            var count = rows.First(r => r.Field?.Name == "Count");
            Check(Math.Abs(number.Step - 0.5) < 1e-9 && (float)number.Stepped(true) == 5.5f && count.Step == 1 && (int)count.Stepped(false) == 2,
                "a number steps by a twentieth of its range at its precision, a whole number by 1", $"{number.Step}, {count.Step}");
            var pick = rows.First(r => r.Field?.Name == "Pick");
            Check(pick.Cycled(true).ToString() == "Third" && pick.Cycled(false).ToString() == "First", "a choice cycles through its values");
            Check(rows.First(r => r.Field?.Name == "Switch").Description == "Shown under the label" && rows.First(r => r.Field?.Name == "Pick").Description == null,
                "a Draw tooltip becomes the row's description");
            Check(rows.First(r => r.Kind == SettingKind.Space).Space == 10f, "a Space keeps its height");
            var later = rows.First(r => r.Field?.Name == "Later");
            Check(later.Label == "Takes effect later" && later.Badge == "Restart", "a label ending (restart) loses the suffix to a Restart badge", $"{later.Label} / {later.Badge}");
            Check(ModSettingsLink.Badged("Code delay, s (next load)", out var badge) == "Code delay, s" && badge == "Next load"
                  && ModSettingsLink.Badged("Fireboxes (restart required)", out badge) == "Fireboxes" && badge == "Restart"
                  && ModSettingsLink.Badged("Plain", out badge) == "Plain" && badge == null, "(next load) and (restart required) are badges too; a plain label has none");
            Check(rows.First(r => r.Field?.Name == "Season").Text == "Spring or autumn" && rows.First(r => r.Field?.Name == "Named").Text == "The first",
                "a choice shows its value in words, or the name the mod gives it",
                $"{rows.First(r => r.Field?.Name == "Season").Text} / {rows.First(r => r.Field?.Name == "Named").Text}");
            var words = new[] { "BuyInCareer", "S282Stoker", "UICDistributor", "High", "springOrAutumn", "Never_fitted" }.Select(ModSettingsLink.Words).ToArray();
            Check(words.SequenceEqual(new[] { "Buy in career", "S282 stoker", "UIC distributor", "High", "Spring or autumn", "Never fitted" }),
                "enum names split into words, acronyms and numbers kept", string.Join(" | ", words));
            var a = rows.First(r => r.Field?.Name == "A");
            a.Field.SetValue(a.Container, 9);
            a.WriteBack?.Invoke();
            Check(probe.Pair.A == 9, "a change inside a struct box goes back into its owner");
            probe.Switch = false;
            rows.Clear();
            ModSettingsLink.Walk(probe, typeof(ProbeSettings), UnityModManagerNet.DrawFieldMask.OnlyDrawAttr, null, rows, null, 0);
            Check(rows.All(r => r.Field?.Name != "Dependent"), "a row shown only with a switch on hides with it off (VisibleOn)");
            // Window-only rows (decision 20's convention): read only while the mod's own window draws.
            ProbeSettings.Window = true;
            rows.Clear();
            ModSettingsLink.Walk(probe, typeof(ProbeSettings), UnityModManagerNet.DrawFieldMask.OnlyDrawAttr, null, rows, null, 0);
            ProbeSettings.Window = false;
            var developer = Shape(rows.SkipWhile(r => r.Label != "Developer"));
            Check(developer == "Heading Developer, Toggle WindowOnly",
                "#Property rules: an instance flag shows a window-only row only in the window; #Merged hides; a static flag (not found by Unity Mod Manager) and a missing one hide their rows", developer);
            Check(ModSettingsLink.DependsOn("#InModWindow|True", probe, typeof(ProbeSettings)) == false && ModSettingsLink.DependsOn("#Merged|True", probe, typeof(ProbeSettings)) == true
                  && ModSettingsLink.DependsOn("#StaticWindow|False", probe, typeof(ProbeSettings)) == null && ModSettingsLink.DependsOn("#Missing|True", probe, typeof(ProbeSettings)) == null
                  && ModSettingsLink.DependsOn("#Name|x", probe, typeof(ProbeSettings)) == null,
                "a #Property rule reads an instance bool property as Unity Mod Manager 0.32.4 does; a static, missing, or string property is unreadable");
            Check(ModSettingsLink.Flowing("A tooltip broken\nfor the hover box,\r\n  as the mods break it.\n\nA second paragraph.") == "A tooltip broken for the hover box, as the mods break it.\nA second paragraph."
                  && ModSettingsLink.Flowing("  ") == null, "a tooltip's line breaks are joined into flowing text, a blank line kept as a paragraph's end");
            var order = ModSettingsLink.Ordered(new[] { "Zeta", "CommsTabletSelfTest", "MurphySignals", "AIEngineer", "RealisticFiring", "Alpha", "RealisticStackEffects", "RealisticBrakes" },
                id => id, id => id).ToArray();
#if PUBLIC_RELEASE
            // The public build knows nothing of the self-test: its id sorts as any other mod's.
            Check(order.SequenceEqual(new[] { "RealisticFiring", "AIEngineer", "RealisticBrakes", "RealisticStackEffects", "MurphySignals", "Alpha", "CommsTabletSelfTest", "Zeta" }),
                "the mods list is in the owner's order, others by name (the public build has no place for the self-test)", string.Join(", ", order));
#else
            Check(order.SequenceEqual(new[] { "RealisticFiring", "AIEngineer", "RealisticBrakes", "RealisticStackEffects", "MurphySignals", "Alpha", "Zeta", "CommsTabletSelfTest" }),
                "the mods list is in the owner's order, others by name, the self-test last", string.Join(", ", order));
#endif
            Check(ModSettingsLink.Excluded("SnowPlows", "Snow Plows") && ModSettingsLink.Excluded("MurphyOps", "MurphyOps (Murphy Branch operations)")
                  && ModSettingsLink.Excluded("MurphyOpsProbe", "MurphyOps Phase 0 probe") && !ModSettingsLink.Excluded("MurphySignals", "MurphySignals (Murphy Branch signals and CTC)")
                  && !ModSettingsLink.Excluded("RealisticFiring", "Realistic Firing"), "Snow Plows, MurphyOps, and probes are kept off the tablet; the rest are not");
            foreach (var type in AppDomain.CurrentDomain.GetAssemblies().Where(x => x.GetName().Name != "CommsTablet.Check")
                         .SelectMany(x => { try { return x.GetTypes(); } catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null).ToArray(); } })
                         .Where(t => { try { return t.IsClass && !t.IsAbstract && typeof(UnityModManagerNet.UnityModManager.ModSettings).IsAssignableFrom(t)
                                     && t != typeof(UnityModManagerNet.UnityModManager.ModSettings) && t.GetConstructor(Type.EmptyTypes) != null; } catch (Exception) { return false; } }))
            {
                var read = new List<SettingRow>();
                string error = null;
                try
                {
                    ModSettingsLink.Walk(Activator.CreateInstance(type), type, UnityModManagerNet.DrawFieldMask.OnlyDrawAttr, null, read, null, 0);
                }
                catch (Exception e)
                {
                    error = e.GetBaseException().Message;
                }
                Console.WriteLine($"Settings of {type.FullName}: " + (error ?? string.Join(", ", read.GroupBy(r => r.Kind).Select(g => $"{g.Count()} {g.Key}"))));
                Check(error == null && read.Any(r => r.Kind != SettingKind.Heading), $"{type.FullName} reads into settings rows", error);
                // A mod's one-time actions (Realistic Firing 1.23.0's give and renew switches) never become rows.
                var oneTime = read.Where(r => r.Field != null && ModSettingsLink.OneTimeFields.Contains(r.Field.Name)).Select(r => r.Field.Name).ToList();
                Check(oneTime.Count == 0, $"{type.FullName}: no one-time action on the tablet", string.Join(", ", oneTime));
            }
            // The probe's one-time actions: by their label, and by a name Realistic Firing uses.
            var probeRows = new List<SettingRow>();
            ModSettingsLink.Walk(new ProbeSettings(), typeof(ProbeSettings), UnityModManagerNet.DrawFieldMask.OnlyDrawAttr, null, probeRows, null, 0);
            Check(!probeRows.Any(r => r.Field != null && (r.Field.Name == "DoItNow" || r.Field.Name == "GiveFireIron")),
                "a switch labeled 'unticks itself', or named as one of Realistic Firing's one-time actions, is kept off the tablet");
        }

        public enum Pick { First, Second, Third }

        public enum Season { Summer, SpringOrAutumn, Winter }

        public enum Named { [UnityEngine.InspectorName("The first")] First, Second }

        public struct Pair
        {
            [UnityModManagerNet.Draw("A")] public int A;
            public Pair(int a) { A = a; }
        }

        public sealed class Box
        {
            [UnityModManagerNet.Draw("On")] public bool On = false;
        }

        public sealed class ProbeSettings : UnityModManagerNet.UnityModManager.ModSettings
        {
            [UnityEngine.Header("Probe")]
            [UnityModManagerNet.Draw("A switch", Tooltip = "Shown under the label")] public bool Switch = true;
            [UnityModManagerNet.Draw("A choice", UnityModManagerNet.DrawType.ToggleGroup)] public Pick Pick = Program.Pick.Second;
            [UnityModManagerNet.Draw("A number", UnityModManagerNet.DrawType.Slider, Min = 0, Max = 10, Precision = 1)] public float Number = 5f;
            [UnityEngine.Space(10)]
            [UnityModManagerNet.Draw("A count", Min = 1, Max = 20)] public int Count = 3;
            [UnityModManagerNet.Draw("Only with the switch", VisibleOn = "Switch|True")] public bool Dependent = false;
            [UnityModManagerNet.Draw("A key")] public UnityModManagerNet.KeyBinding Key = new UnityModManagerNet.KeyBinding();
            [UnityModManagerNet.Draw("A text")] public string Text = "text";
            [UnityModManagerNet.Draw("Box", Box = true)] public Box Box = new Box();
            [UnityModManagerNet.Draw("Pair")] public Pair Pair = new Pair(1);
            [UnityModManagerNet.Draw(UnityModManagerNet.DrawType.Ignore)] public bool Ignored = false;
            [UnityModManagerNet.Draw("Takes effect later (restart)")] public bool Later = false;
            [UnityModManagerNet.Draw("Season")] public Season Season = Program.Season.SpringOrAutumn;
            [UnityModManagerNet.Draw("Do it now (unticks itself)")] public bool DoItNow = false;
            [UnityModManagerNet.Draw("Put a tool in front of me")] public bool GiveFireIron = false;
            [UnityModManagerNet.Draw("Named")] public Named Named = Program.Named.First;
            [UnityEngine.Header("Developer")]
            [UnityModManagerNet.Draw("Window only", VisibleOn = "#InModWindow|True")] public bool WindowOnly = false;
            [UnityModManagerNet.Draw("Window only, a static flag", VisibleOn = "#StaticWindow|True")] public bool StaticOnly = false;
            [UnityModManagerNet.Draw("Pointless while merged", InvisibleOn = "#Merged|True")] public bool NotMerged = false;
            [UnityModManagerNet.Draw("An unreadable rule", VisibleOn = "#Missing|True")] public bool Unreadable = false;
            public int Undrawn = 7;

            internal static bool Window;
            public bool InModWindow => Window;
            public static bool StaticWindow => Window;
            public bool Merged => true;
            public string Name => "x";
        }

        // Guest classes for the binding checks: never called.
        internal static class GoodGuest
        {
            public const int Contract = 1;
            public const string Id = "good", Name = "Good", Tablet = "Good_Tablet";
            public static UnityEngine.Texture2D Icon() => null;
            public static bool Available() => true;
            public static int Open(UnityEngine.RectTransform region, UnityEngine.GameObject tablet, Action invalidate, Action<int, UnityEngine.Rect, bool> placeSlot,
                Action<int> hideSlotsFrom, int slotCount, bool vr) => 0;
            public static void Close(int view) { }
            public static void Shown(int view, bool shown) { }
            public static void Held(int view, bool held) { }
            public static void Update(int view) { }
            public static void Draw(int view) { }
            public static void Pressed(int view, int slot) { }
            public static UnityEngine.Rect PressArea(int view) => default;
            public static void PressAt(int view, UnityEngine.Vector2 canvas) { }
            public static bool Use(int view) => false;
            public static bool Scroll(int view, bool forward) => false;
        }

        internal static class LaterGuest
        {
            public const int Contract = 99;
            public const string Id = "later", Name = "Later";
        }

        internal static class WrongGuest
        {
            public const int Contract = 1;
            public const string Id = "wrong", Name = "Wrong";
            public static UnityEngine.Texture2D Icon() => null;
            public static bool Available() => true;
            public static int Open(UnityEngine.RectTransform region, UnityEngine.GameObject tablet) => 0;
            public static void Close(int view) { }
            public static void Shown(int view, bool shown) { }
            public static void Held(int view, bool held) { }
            public static void Update(int view) { }
            public static void Draw(int view) { }
            public static void Pressed(int view, int slot) { }
            public static UnityEngine.Rect PressArea(int view) => default;
            public static void PressAt(int view, UnityEngine.Vector2 canvas) { }
        }

        private static void Optional(List<string> mods)
        {
            if (mods.Contains("Multiplayer"))
                Check(AccessTools.TypeByName("Multiplayer.Components.Networking.World.NetworkedItemManager") is Type manager
                      && AccessTools.DeclaredMethod(manager, "BuildPrefabLookup") != null,
                    "Multiplayer's item lookup (tablet on clients)");
            if (mods.Contains("CCL.Importer"))
            {
                var handler = AccessTools.TypeByName("CCL.Importer.WorkTrains.WorkTrainPurchaseHandler");
                var variant = AccessTools.TypeByName("CCL.Importer.Types.CCL_CarVariant");
                Check(handler != null && variant != null && AccessTools.Method(handler, "SetAsSummoned", new[] { variant }) != null,
                    "Custom Car Loader's SetAsSummoned (first summon free)");
                // Binds a base-livery handler to CCL's Action<CCL_CarVariant>, then removes it.
                var heard = WorkTrains.Follow(_ => { });
                var unlocked = AccessTools.Field(handler, "LiveryUnlocked");
                var joined = heard != null && unlocked != null && ((Delegate)unlocked.GetValue(null))?.GetInvocationList().Contains(heard) == true;
                WorkTrains.Unfollow(heard);
                var left = ((Delegate)unlocked?.GetValue(null))?.GetInvocationList().Contains(heard) != true;
                Check(joined && left, "following Custom Car Loader's work-train purchases (LiveryUnlocked)");
            }
            // Rearranged's members are internal; with its assemblies given, each one the tablet calls must resolve.
            if (mods.Contains("RearrangedS282"))
                Check(RearrangedLink.S282Resolves, "Rearranged S282's members the Rearrange app calls",
                    string.Join(", ", RearrangedLink.Missing().Where(m => !m.StartsWith("S060"))));
            if (mods.Contains("RearrangedS060"))
                Check(RearrangedLink.S060Resolves, "Rearranged S060's members the Rearrange app calls",
                    string.Join(", ", RearrangedLink.Missing().Where(m => m.StartsWith("S060"))));
            if (mods.Contains("SkinManagerMod"))
            {
                var link = typeof(SkinManagerLink);
                var unresolved = new[] { "Provider", "Manager", "Theme", "Area", "ModMain", "Switcher", "SkinsForType", "ApplySkinMethod",
                        "CurrentSkin", "Supports", "ApplyToObject", "ApplyPaxInterior", "CclSubstitute", "Settings", "RadioSkinApplied" }
                    .Where(n => AccessTools.Field(link, n)?.GetValue(null) == null).ToList();
                Check(unresolved.Count == 0 && SkinManagerLink.Present, "Skin Manager's API the Paint page uses", string.Join(", ", unresolved));
                var settings = AccessTools.Property(AccessTools.TypeByName("SkinManagerMod.Main"), "Settings")?.PropertyType;
                Check(settings != null && AccessTools.Field(settings, "alwaysAllowRadioReskin")?.FieldType == typeof(bool),
                    "Skin Manager's alwaysAllowRadioReskin setting");
                var area = AccessTools.TypeByName("SkinManagerMod.PaintArea");
                Check(area != null && Convert.ToInt32(Enum.Parse(area, "Exterior")) == SkinManagerLink.Exterior
                      && Convert.ToInt32(Enum.Parse(area, "Interior")) == SkinManagerLink.Interior && Convert.ToInt32(Enum.Parse(area, "All")) == SkinManagerLink.All,
                    "Skin Manager's paint areas are the flags the tablet passes");
                var applied = AccessTools.Field(AccessTools.TypeByName("SkinManagerMod.CommsRadioSkinSwitcher"), "RadioSkinApplied");
                Check(applied != null && applied.IsStatic && typeof(Delegate).IsAssignableFrom(applied.FieldType)
                      && applied.FieldType.GetMethod("Invoke")?.GetParameters().Length == 3,
                    "Skin Manager's RadioSkinApplied (car, skin, area), raised for Custom Car Loader sets");
                var worn = AccessTools.Method(AccessTools.TypeByName("SkinManagerMod.SkinManager"), "GetCurrentCarSkin");
                Check(worn != null && worn.ReturnType == typeof(ValueTuple<string, string>), "Skin Manager's GetCurrentCarSkin returns (exterior, interior)");
            }
            if (mods.Contains("RealisticFiring"))
            {
                var theirs = AccessTools.TypeByName("RealisticFiring.Items.FireIronLoading") is Type loading
                    ? AccessTools.DeclaredMethod(loading, "LoadPrefab") : null;
                Check(theirs != null && TabletLoading.IsLoader(theirs), "Realistic Firing's LoadPrefab is chained, not replaced");
            }
        }
    }
}
