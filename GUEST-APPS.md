# Putting your mod's app on the Comms Tablet

Any Unity Mod Manager mod can put an app on the Comms Tablet's home screen.
The app gets the whole screen while it is open and the tablet's buttons for
input. It works on the tablet held in hand and on the tablet mounted in a cab,
on a desktop and in VR.

Your mod does not reference `CommsTablet.dll`. You publish one static class
with a fixed name, and the tablet finds it by reflection and binds its members.
Without the Comms Tablet installed, your mod runs as it does now.

This is guest contract **version 2** (Comms Tablet 0.1.3 and newer). Version 2
adds car-first apps (see "Car-first apps"); an app written for version 1 still
binds, unchanged. Two published mods implement version 1, and both are worth
reading next to this guide:

- Realistic Firing:
  [`CommsTabletApp.cs`](https://github.com/Specter242/DVRealisticFiring/blob/main/src/RealisticFiring/CommsTabletApp.cs)
  and
  [`CommsTabletLink.cs`](https://github.com/Specter242/DVRealisticFiring/blob/main/src/RealisticFiring/CommsTabletLink.cs).
  Its pages use buttons only, with no `PressArea`.
- AI Engineer:
  [`CommsTabletApp.cs`](https://github.com/Specter242/DVAIEngineer/blob/main/src/AIEngineer/CommsTabletApp.cs)
  and
  [`CommsTabletLink.cs`](https://github.com/Specter242/DVAIEngineer/blob/main/src/AIEngineer/CommsTabletLink.cs).
  It has a pressable map (`PressArea` and `PressAt`).

The host side is in this repository under `src/CommsTablet/Guests/`.

## How the tablet finds your app

- Publish a **public static class named `CommsTabletApp` in a namespace equal
  to your assembly's simple name**. For example, the class is
  `RealisticFiring.CommsTabletApp` in `RealisticFiring.dll`, and
  `AIEngineer.CommsTabletApp` in `AIEngineer.dll`.
- The tablet looks through the loaded assemblies the first time it needs them in
  a game session. It looks again only when the number of loaded assemblies
  changes. By then every mod is loaded, so load order does not matter.
- Each member is bound once with `Delegate.CreateDelegate`. A missing member or
  a wrong signature fails at binding, once, with a log line that names it. It
  never fails in the middle of a frame.
- Only base library and UnityEngine types cross between the two mods, and,
  for a car-first app, the game's own `TrainCar`.
  - **Never reference `CommsTablet.dll`.**
  - Don't share an assembly with it: two versions of a shared assembly would
    collide in Mono.
  - The tablet reaches you by reflection, and you reach the tablet by
    reflection.

## The class

```csharp
using System;
using UnityEngine;

namespace YourMod   // your assembly's simple name
{
    public static class CommsTabletApp
    {
        public const int Contract = 1;
        public const string Id = "your-mod";          // unique and stable
        public const string Name = "Presets";         // shown under the icon
        public const string Tablet = "YourMod_Tablet"; // optional: your own tablet item's prefab name

        public static Texture2D Icon();
        public static bool Available();
        public static int Open(RectTransform region, GameObject tablet, Action invalidate,
            Action<int, Rect, bool> placeSlot, Action<int> hideSlotsFrom, int slotCount, bool vr);
        public static void Close(int view);
        public static void Shown(int view, bool shown);
        public static void Held(int view, bool held);
        public static void Update(int view);
        public static void Draw(int view);
        public static void Pressed(int view, int slot);
        public static Rect PressArea(int view);
        public static void PressAt(int view, Vector2 canvas);

        // Optional.
        public static bool Use(int view);
        public static bool Scroll(int view, bool forward);
    }
}
```

An app written for version 1 declares `Contract = 1`. A car-first app declares
`Contract = 2` and adds the members in "Car-first apps".

The constants may also be `static readonly` fields of the same types. Overloads
with other signatures are ignored. If no overload of a required member binds,
the app is refused.

| Member | When the tablet calls it | What your app does |
| --- | --- | --- |
| `Contract` | at binding | The contract version it was written for. Outside the versions the tablet takes, the app is hidden, with one log line naming both versions. |
| `Id` | at binding | A unique, stable key. A second app with the same `Id` is refused. |
| `Name` | at binding | The app's name under its icon. Keep it short: one word fits best. |
| `Tablet` | at binding | Optional. Your own tablet item's prefab name, if your mod has one (see "If your mod has its own tablet"). Leave it out otherwise. |
| `Icon()` | once a session, for the home screen | Returns a square texture, 128 to 192 pixels, white on transparent. The tablet tints it brass on its round badge. Return `null` for a plain ring. |
| `Available()` | at each refresh, twice a second while the screen is seen | Returns whether the app is offered now. False hides its icon, and an open app goes back to the home screen. |
| `Open(...)` | the first time the app is shown on a screen's canvas | Builds its interface under `region` and returns a view id of its choosing. |
| `Close(view)` | before that canvas is let go | Lets go of everything it made for the view. The region is destroyed right after. |
| `Shown(view, shown)` | as the app is shown and hidden on that screen | True after `Open`, and again whenever it is opened. False when the home button or another app hides it. A canvas that is let go gets only `Close`. |
| `Held(view, held)` | right after `Open`, then on every change | Whether the tablet is in hand (grabbed, with the big inventory closed). Always false on the mounted screen. |
| `Update(view)` | each frame while the app is shown and the screen is seen | Cheap work only. Call `invalidate` when what it shows has changed. |
| `Draw(view)` | just before the tablet renders the canvas | Lays out its graphics and places its slots. The canvas is then rendered once. |
| `Pressed(view, slot)` | when a button slot it placed is pressed | Acts on the press. The tablet then calls `Draw` and renders. |
| `PressArea(view)` | when shown, at each refresh, and after each `Draw` | Returns the canvas rectangle where a press is a point (a map), or `Rect.zero` for none. |
| `PressAt(view, canvas)` | on a press inside `PressArea` | Receives the canvas point that was pressed. The tablet then calls `Draw` and renders. |
| `Use(view)` | optional: the item's use, while the app is shown on the held tablet | Returns true if it took the use (the tablet redraws). |
| `Scroll(view, forward)` | optional: the scroll wheel (in VR, the stick), while the app is shown on the held tablet | Returns true if it took it (the tablet redraws). |

## Car-first apps (contract 2)

An app that works on a car the player points at (its paint, its parts, its
settings) can let the tablet do the aiming, as the tablet's own Paint, Wheels,
and Change pages do. Declare `Contract = 2` and add:

```csharp
        public const int Contract = 2;

        // Optional, together: a car-first app.
        public static bool Fits(TrainCar car);
        public static void Chosen(int view, TrainCar car);

        // Optional, with them.
        public static Color BeamColor();
```

| Member | When the tablet calls it | What your app does |
| --- | --- | --- |
| `Fits(car)` | each frame while the app is shown on the held tablet, for the car it is aimed at | Returns whether the app works on that car. A car it doesn't take is not pointed at. Keep it cheap. |
| `Chosen(view, car)` | when a car is chosen; when it is let go (`null`); right after `Open` if one is already chosen | Shows what can be done to the car, and calls `invalidate`. |
| `BeamColor()` | as the app is shown on the held tablet | Returns the color of the tablet's beam. Leave it out for white. |

- **Aiming.** The tablet's beam aims from the held tablet. A car that fits is
  outlined in green. Using the tablet at it chooses it: it is outlined in amber,
  and every view of your app is told `Chosen(view, car)`.
- **Using it again.** At the chosen car, the use goes to your `Use(view)`:
  apply what the player last stepped to, as a radio's confirm. At another car
  that fits, the use chooses that car. At nothing, it lets the car go
  (`Chosen(view, null)`).
- **The scroll wheel** goes to your `Scroll`, as for any app.
- **Letting go.** The car is let go when the tablet is put away or mounted, or
  another app opens. Taken up again within 100 m of the car it had, the tablet
  chooses it again. Opening your app from Paint, Wheels, or Change keeps their
  car if it fits.
- **Presses on the screen** act without aiming. Check yourself that the player
  is near enough to the car (the radio reaches 100 m).
- `TrainCar` is the game's own type, which both mods already reference, so it
  can't collide.
- An app written for contract 1 is never car first, even with these members.
  `Fits` and `Chosen` go together; an app with one and not the other, or with
  `BeamColor` alone, is refused.
- A Comms Tablet older than 0.1.3 takes only contract 1, so it hides a
  contract-2 app with one log line.

## The screen

- **The region** is a `RectTransform` of 1080 by 924 canvas pixels. That is the
  whole screen, at 0.28 mm a pixel (302 by 259 mm), anchored and pivoted
  top-left. Canvas coordinates run right and down from the screen's top-left
  corner, and every `Rect` in this contract uses them.
- **Build Unity UI graphics** (`UnityEngine.UI`) under the region.
  - **Put every object you make under it on the region's layer (30).** That is
    the tablet's isolated screen layer, which only the screen's own camera
    draws.
  - Turn off `raycastTarget`: presses come through the slots, not the event
    system.
- **The whole screen is yours.** The tablet hides its own heading, tabs, and
  status strip while your app is shown. The round home button under the screen
  always goes home, and your app is told `Shown(view, false)`.
- **The canvas is drawn only when it changes.** It is rendered into a texture
  by a disabled camera:
  - in `LateUpdate`, after `invalidate`;
  - after a press;
  - when the app is shown.

  Never assume it renders every frame. Call `invalidate` whenever something
  changed; it is cheap, and you can call it at any time. Changes made outside
  `Draw` show at the next render.
- **The look.** The tablet's own pages use smoked iron plates, cream text, and
  brass for what is chosen. Size pressable plates at 56 pixels (16 mm) or more,
  and text at 26 pixels or more, so they can be read and pressed in VR.

## Buttons

- **The slot pool.** The tablet has a pool of `slotCount` buttons (96). They are
  the game's own cab buttons, so a mouse, the crosshair, and a VR finger all
  press them the same way.
- **Placing and removing slots.**
  - `placeSlot(slot, rect, true)` puts that slot's button over a canvas
    rectangle.
  - `placeSlot(slot, rect, false)` takes one button away.
  - `hideSlotsFrom(n)` takes away slots `n` and up.
- **When to place them.** Place slots in `Draw`, and give the same plate the
  same slot from one draw to the next.
- **Only the shown view has buttons.** Calls from a hidden view, or with a slot
  outside the pool, are ignored. When the app is shown, every slot starts
  hidden.
- **The pointed button (desktop).** One more button covers `PressArea(view)`. A
  press on it calls `PressAt` with the canvas point the player pointed at: the
  mouse in mouse mode, otherwise the crosshair where its ray meets the screen.
  Keep `PressArea` clear of your slots.
- **In VR** (`vr` is true), there is no pointed button, because it cannot follow
  a hand. Make everything pressable a slot.

## Lifecycle

```
Open → Held(view, held) → Shown(view, true) → Draw → render
  ... Update each frame while shown and seen;
      Draw and render after invalidate or a press ...
  Shown(view, false)                    the home button or another app
  Shown(view, true) → Draw → render     opened again: the same view
Close(view)                             stashed, mounted, taken off the mount, or destroyed
```

- **There is one view per screen.** The held tablet and the mounted tablet are
  two screens, each with its own canvas. Your app may have a view open on each
  over a session, but never on both at once: the item is put away while it is
  mounted.
- **`tablet` is the GameObject the screen is on.** It is one of:
  - the held item, an `ItemBase` with an `InventoryItemSpec`;
  - the mounted form, a `DV.Customization.Gadgets.GadgetBase`, whose
    `TrainCar` is the cab's car.

  An app that reads a locomotive should read the mounted form's car when
  mounted, and the player's car when held.
- **The canvas is let go** when the screen is disabled or destroyed:
  - the item is stashed into the inventory;
  - the item is put on a mount;
  - the mounted form is taken off;
  - the session ends.

  `Close` comes first. Out of sight, the canvas stays and only its texture is
  let go after ten seconds; no call is made then.
- **Use and scroll reach the app only on the held tablet.** On a desktop, the
  use is a left click away from the screen (not in mouse mode); in VR, it is
  the trigger.
  - While the held tablet is aimed at a free gadget mount, the use places it on
    the mount instead and reaches no app.

## Errors

Every call is guarded. The first exception your app throws is logged once, as
an error naming the app, its assembly, and the call. The app is then failed for
the session:
- its views are closed at the next frame (`Close` is still called; an exception
  there is only logged);
- an open app goes back to the home screen;
- its icon is hidden.

An app that is not bound is not shown, and the tablet logs one warning saying
why. That happens for another contract version, a missing member, a wrong
signature, or a taken `Id`.

## Versioning

- `CommsTablet.GuestContract` (public, in `CommsTablet.dll`) has
  `const int Min = 1, Max = 2` and `static bool Merged(string id)`.
  - Version 2 (Comms Tablet 0.1.3) added the optional car-first members.
- `GuestContract.Merged(Id)` is true when all three hold: the Comms Tablet
  loaded, it can build its tablet, and it bound your app. It stays true for the
  rest of the session, even if your app later throws.
- Ask it lazily, in a game session (a shop waking, a key pressed), never in
  your mod's `Load`, when other mods may not be loaded yet.
- A later contract that only adds optional members raises `Max` and keeps
  `Min`, so apps written for an older contract still bind. A contract that
  changes a member raises `Min`. Your app declares the one contract it was
  written for.

Your side of `Merged`, by reflection only:

```csharp
using System;
using System.Linq;
using System.Reflection;

internal static class CommsTabletLink
{
    private static Func<string, bool> merged;

    internal static bool Merged
    {
        get
        {
            if (merged == null)
            {
                var host = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "CommsTablet");
                var method = host?.GetType("CommsTablet.GuestContract", false)?.GetMethod("Merged",
                    BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                if (method == null)
                    return false;   // no Comms Tablet, or not loaded yet: asked again next time
                merged = (Func<string, bool>)Delegate.CreateDelegate(typeof(Func<string, bool>), method, false);
            }
            try { return merged != null && merged(CommsTabletApp.Id); }
            catch (Exception) { return false; }
        }
    }
}
```

An app that only adds pages to the tablet doesn't need `Merged` at all. It
matters for a mod with its own tablet item.

## If your mod has its own tablet

Realistic Firing and AI Engineer each had a held tablet before the Comms Tablet.
With the Comms Tablet installed, their tablets are retired and their pages
become apps. If your mod has its own tablet item and you want the same,
declare `Tablet` with the item's prefab name. Then, while `Merged` is true:

1. **Skip your own starting-kit grant, and clear your own "given" flag.**
   - While merged, your tablet isn't in the save, because the Comms Tablet
     loads it as its own. So the grant isn't spent.
   - A save that had your tablet before the Comms Tablet came has the flag
     set. Clearing it lets a player who later removes the Comms Tablet get
     your tablet once, as a new player would.
   - The game never adds a starting item that is already in the save, so
     clearing the flag can't give a second one.
2. **Don't list your tablet in the shops.** That covers the shop's items and the
   shelf tags.
3. **Don't give your tablet from a key or a settings button.** Say instead that
   your pages are on the Comms Tablet's home screen.
4. **Keep your own item, template, and load wrapper working.**
   - The Comms Tablet answers your prefab name first, so a saved tablet of yours
     loads as the Comms Tablet.
   - Your loader stays the fallback when the Comms Tablet can't build its own.
5. **Keep everything else as it is.** Only the tablet item is retired.

The Comms Tablet does the host's part:
- A saved tablet of yours, in the inventory, the world, lost and found, or on a
  gadget mount, loads as the Comms Tablet. The next save stores it as one.
- The starting kit holds the Comms Tablet once per save while any app with a
  tablet is merged.

A save made while merged and loaded without the Comms Tablet loses those
tablets, since the game can't make the Comms Tablet's item. Your starting kit
then gives yours again, because you cleared the flag.

## Checklist

- The class is `<YourAssembly>.CommsTabletApp`, public and static.
- `Contract` is 1, or 2 for a car-first app. `Id` is unique and stable, and
  `Name` is short.
- A car-first app has both `Fits` and `Chosen`, and checks the reach on a press.
- No reference to `CommsTablet.dll`, and no shared assembly.
- Everything under `region` is on layer 30, with `raycastTarget` off.
- Slots are placed in `Draw`, the same slot for the same plate.
- `invalidate` is called when anything shown changes.
- `PressArea` is clear of slots, and everything is a slot in VR.
- `Close` lets go of everything made for the view.
- `Merged` is asked lazily, never in `Load`.
