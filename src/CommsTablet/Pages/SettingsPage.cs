using System;
using System.Collections.Generic;
using System.Linq;
using CommsTablet.Tablet;
using DV;
using UnityEngine;
using S = CommsTablet.Tablet.TabletSkin;

namespace CommsTablet.Pages
{
 internal sealed class SettingsPage : TabletPage
    {
        // Canvas pixels: the layer under the app's name and above the status strip.
        private const float Left = 12, Width = 1056, Top = 92, ListTop = 168, Bottom = 768;
        private const float Plate = 64, Small = 72, ControlX = 680, ControlW = 376, ToggleW = 196;
        // A row is at least its plate and a margin tall (16 mm presses need 57 pixels).
        private const float RowGap = 8, RowPad = 12, MinRow = Plate + 16, NoteGap = 4, Slack = 6;
        private const float HeadingHeight = 52, BadgeW = 116, BadgeH = 36, KeyMin = 44, KeyNameW = 640;
        private const float ChipHeight = 60, ChipGap = 8;
        private const int ChipsPerLine = 10, SectionsForChips = 3;
        private const int ListColumns = 2, ListRows = 5;
        // An action's plate: its label and the line under it. Ten section plates to a line and 84-pixel
        // plates keep five actions on a nine-section mod's first page (Realistic Firing 1.24.3.1012, 2026-11-21).
        private const float ActionHeight = 84;

        /// <summary>The heading over a mod's one-time actions, at the top of its section.</summary>
        internal const string EquipmentTitle = "Equipment";

        private OwnerMod open;
        private int page;
        private string notice;
        private Layout shown;
        private OwnerMod cachedMod;
        private VisibilitySnapshot cachedModState;
        private Layout cachedLayout;
        private readonly Dictionary<string, (VisibilitySnapshot state, int count)> listCounts = new Dictionary<string, (VisibilitySnapshot, int)>();
        private OwnerMod queuedMod;
        private ModAction queuedAction;

        private static readonly Look RowLabel = TextLook(TabletSkin.Cream, 24, 20, TextAnchor.UpperLeft);
        private static readonly Look RowNote = TextLook(TabletSkin.Soft, 20, 17, TextAnchor.UpperLeft);
        private static readonly Look KeyName = TextLook(TabletSkin.Cream, 22, 18, TextAnchor.MiddleLeft);
        private static readonly Look KeyValue = TextLook(TabletSkin.Brass2, 22, 18, TextAnchor.MiddleLeft);
        private static readonly Look KeysNote = TextLook(TabletSkin.Soft, 20, 16, TextAnchor.MiddleRight);
        private static readonly Look BadgeText = TextLook(TabletSkin.Brass2, 18, 14, TextAnchor.MiddleCenter);

#if !PUBLIC_RELEASE
        /// <summary>Each plate drawn in the last pass, by what it does ("mod:Id", "back", "section:2", "toggle:Field", ...), for the self-test.</summary>
        internal readonly Dictionary<string, Rect> Plates = new Dictionary<string, Rect>();

        /// <summary>
        /// What the last pass drew, by what it is ("label:Field",
        /// "description:Field", "badge:Field", "value:Field", "key:Field",
        /// "keys", "heading:Title"), for the self-test.
        /// </summary>
        internal readonly Dictionary<string, string> Texts = new Dictionary<string, string>();

        /// <summary>Where the last pass drew them, by the same names ("keys" is the Keys card), for the self-test.</summary>
        internal readonly Dictionary<string, Rect> Areas = new Dictionary<string, Rect>();
#endif

        internal SettingsPage(TabletRadio radio) : base(radio) { }

        internal override string Name => "SETTINGS";

        internal override ICommsRadioMode Mode => null;

        internal override bool Available => ModSettingsLink.Mods.Count > 0;

        internal override int Selected => -1;

        internal override bool DrawsItself => true;

        internal override string Status => notice ?? (open == null ? "Choose a mod to change its settings"
            : open.Settings == null ? "Each button acts once, as in the mod's own settings window"
            : "Changes are saved at once, as in Unity Mod Manager's window");

        /// <summary>The mod whose settings are open, or null for the list of mods.</summary>
        internal OwnerMod Open => open;

        internal override void Refresh() { }

        internal override void Press(int tile) { }

        internal override void Capture() { }

        internal override void Reapply() { }

        /// <summary>
        /// Runs the action pressed on the last pass, called at the start of
        /// the screen's update, outside the canvas pass: giving an item may
        /// change what the player holds. Its notice goes to the status strip.
        /// </summary>
        internal override bool RunQueued()
        {
            if (queuedMod == null)
                return false;
            var mod = queuedMod;
            queuedMod = null;
            notice = mod.Actions?.Run(queuedAction) ?? $"{queuedAction.Label}: not available";
            InvalidateCache();
            Main.Mod?.Logger.Log($"Settings app: {mod.Name}'s \"{queuedAction.Label}\": {notice}");
            return true;
        }

        private void InvalidateCache()
        {
            cachedMod = null;
            cachedLayout = null;
            listCounts.Clear();
        }

        private int SettingCount(OwnerMod mod)
        {
            var cacheable = mod.TryVisibilitySnapshot(out var state);
            if (cacheable && listCounts.TryGetValue(mod.Id, out var cached) && state.SameAs(cached.state))
                return cached.count;
            var rows = mod.Rows();
            var count = 0;
            for (var i = 0; i < rows.Count; i++)
                if (rows[i].Kind != SettingKind.Heading && rows[i].Kind != SettingKind.Space)
                    count++;
            if (cacheable)
                listCounts[mod.Id] = (state, count);
            else
                listCounts.Remove(mod.Id);
            return count;
        }

        private static bool SameActions(List<ModAction> a, List<ModAction> b)
        {
            var countA = a?.Count ?? 0;
            var countB = b?.Count ?? 0;
            if (countA != countB)
                return false;
            for (var i = 0; i < countA; i++)
                if (a[i].Index != b[i].Index || a[i].Label != b[i].Label || a[i].Help != b[i].Help)
                    return false;
            return true;
        }

        private Layout LayoutFor(OwnerMod mod)
        {
            var cacheable = mod.TryVisibilitySnapshot(out var state);
            var equipment = mod.Actions?.List();
            if (cacheable && cachedLayout != null && ReferenceEquals(cachedMod, mod)
                && state.SameAs(cachedModState) && SameActions(cachedLayout.Equipment, equipment))
                return cachedLayout;
            var built = Build(mod, equipment);
            if (cacheable)
            {
                cachedLayout = built;
                cachedMod = mod;
                cachedModState = state;
            }
            else
            {
                cachedLayout = null;
                cachedMod = null;
                cachedModState = null;
            }
            return built;
        }

        private static Look TextLook(Color color, int size, int min, TextAnchor align) =>
            new Look { Text = color, Size = size, MinSize = min, Align = align, PadLeft = 0, PadRight = 0 };

        private bool Button(string key, Rect r, string text, Look look, bool enabled = true)
        {
#if !PUBLIC_RELEASE
            Plates[key] = r;
#endif
            return S.Button(r, text, look, enabled);
        }

        private void Text(string key, Rect r, string text, Look look)
        {
#if !PUBLIC_RELEASE
            Texts[key] = text;
            Areas[key] = r;
#endif
            S.Label(r, text, look);
        }

        internal override void Draw()
        {
#if !PUBLIC_RELEASE
            Plates.Clear();
            Texts.Clear();
            Areas.Clear();
#endif
            if (open == null)
                DrawList();
            else
                DrawMod(open);
        }

        /// <summary>A mod's name without the description in brackets after it ("MurphySignals (...)": "MurphySignals").</summary>
        private static string MainName(string name, out string detail)
        {
            detail = null;
            var cut = name.IndexOf(" (", StringComparison.Ordinal);
            if (cut <= 0 || !name.EndsWith(")"))
                return name;
            detail = name.Substring(cut + 2, name.Length - cut - 3);
            return name.Substring(0, cut);
        }


        private void DrawList()
        {
            S.Label(new Rect(Left + 16, Top, Width - 32, 64), "Mod settings", S.Big);
            var mods = ModSettingsLink.Mods;
            const int per = ListColumns * ListRows;
            page = Mathf.Clamp(page, 0, Mathf.Max(0, (mods.Count - 1) / per));
            var plate = S.Plate.Sized(27, 22);
            for (var i = 0; i < per; i++)
            {
                var index = page * per + i;
                if (index >= mods.Count)
                    break;
                var mod = mods[index];
                var r = new Rect(Left + i % ListColumns * (Width / ListColumns + 0), ListTop + i / ListColumns * 120, Width / ListColumns - 12, 108);
                var count = SettingCount(mod);
                // A long name is split, not shrunk: its name, then what it is, then its count and version.
                var name = MainName(mod.Name, out var detail);
                var about = $"{(count > 0 ? count + " settings" : EquipmentTitle)} · {mod.Entry.Info.Version}";
                if (Button("mod:" + mod.Id, r, S.Two(name, detail != null ? detail + "\n" + about : about), plate))
                {
                    open = mod;
                    page = 0;
                    notice = null;
                    cachedLayout = null;
                    S.Changed();
                }
            }
            Paging(mods.Count, per);
        }

        // ---- A mod's settings ----------------------------------------------------

        private void DrawMod(OwnerMod mod)
        {
            if (Button("back", new Rect(Left, Top, 200, Plate), "◀ Mods", S.Plate))
            {
                open = null;
                page = 0;
                notice = null;
                InvalidateCache();
                S.Changed();
            }
            S.Label(new Rect(Left + 220, Top, Width - 240, Plate), MainName(mod.Name, out _), S.Big);
            var layout = shown = LayoutFor(mod);
            page = Mathf.Clamp(page, 0, layout.Pages.Count - 1);
            if (layout.Chips)
                DrawChips(layout);
            if (layout.Pages[page].Count == 0)
                S.Label(new Rect(Left + 16, layout.Top, Width - 32, 200), "Nothing here to change on the tablet.", S.Empty);

            // Each Keys card behind its lines, then the lines (positions precomputed with the layout).
            var items = layout.Pages[page];
            var cards = layout.KeyCards[page];
            for (var i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
#if !PUBLIC_RELEASE
                Areas["keys"] = card;
#endif
                S.Box(card, S.Card);
            }
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var h = item.Height;
                switch (item.Kind)
                {
                    case Line.Heading:
                        Text("heading:" + item.Text, new Rect(Left + 16, item.Y, Width - 32, h - RowGap), item.Text, S.Section);
                        break;
                    case Line.KeysTitle:
                        Text("keys", new Rect(Left + 16, item.Y, 400, h - RowGap), item.Text, S.Section);
                        S.Label(new Rect(Left + 420, item.Y, Width - 436, h - RowGap), "set in the mod's settings window (Ctrl+F10)", KeysNote);
                        break;
                    case Line.Key:
                        DrawKey(item.Row, new Rect(Left, item.Y, Width, h));
                        break;
                    case Line.Setting:
                        DrawRow(mod, item.Row, item.Shape, new Rect(Left, item.Y, Width, h - RowGap));
                        break;
                    case Line.Action:
                        DrawAction(mod, item.Action, new Rect(Left, item.Y, Width, h - RowGap));
                        break;
                }
            }
            Paging(layout.Pages.Count, 1);
        }

        private void DrawChips(Layout layout)
        {
            var count = layout.Sections.Count;
            var lines = (count + ChipsPerLine - 1) / ChipsPerLine;
            var perLine = (count + lines - 1) / lines;
            var w = (Width - (perLine - 1) * ChipGap) / perLine;
            var current = layout.SectionOfPage[page];
            var off = S.Plate.Sized(23, 16);
            var on = S.PlateOn.Sized(23, 16);
            for (var i = 0; i < count; i++)
            {
                var r = new Rect(Left + i % perLine * (w + ChipGap), ListTop + i / perLine * (ChipHeight + ChipGap), w, ChipHeight);
                if (Button("section:" + i, r, layout.Sections[i], i == current ? on : off))
                {
                    page = layout.FirstPage[i];
                    notice = null;
                    S.Changed();
                }
            }
        }

        private void DrawRow(OwnerMod mod, SettingRow row, RowShape shape, Rect r)
        {
            S.Box(r, S.Card);
            var key = row.Field.Name;
            // The text centred on the control by its measured height; each rect a little taller, so nothing is cut.
            var top = r.y + Mathf.Max(RowPad, (r.height - shape.Block) * 0.5f);
            Text("label:" + key, new Rect(shape.TextX, top, shape.TextW, shape.Label + Slack), row.Label, RowLabel);
            if (shape.Note > 0)
            {
                var noteTop = top + shape.Label + NoteGap;
                Text("description:" + key, new Rect(shape.TextX, noteTop, shape.TextW, Mathf.Min(shape.Note + Slack, r.yMax - noteTop)), row.Description, RowNote);
            }
            var cy = r.y + r.height * 0.5f - Plate * 0.5f;
            if (row.Badge != null)
            {
                var badge = new Rect(shape.BadgeX, cy + (Plate - BadgeH) * 0.5f, BadgeW, BadgeH);
                S.Box(badge, S.Well);
                Text("badge:" + key, badge, row.Badge, BadgeText);
            }
            var value = S.Centered(S.Brass2Text).Sized(24, 18);
            switch (row.Kind)
            {
                case SettingKind.Toggle:
                {
                    var on = row.Value is bool b && b;
                    if (Button("toggle:" + key, new Rect(shape.ControlX, cy, ToggleW, Plate), on ? "ON" : "OFF", on ? S.PlateOn : S.Plate))
                        Change(mod, row, !on);
                    break;
                }
                case SettingKind.Choice:
                    if (Button("previous:" + key, new Rect(ControlX, cy, Small, Plate), "◀", S.Plate))
                        Change(mod, row, row.Cycled(false));
                    Text("value:" + key, new Rect(ControlX + Small + 8, cy, ControlW - 2 * Small - 16, Plate), row.Text, value);
                    if (Button("next:" + key, new Rect(ControlX + ControlW - Small, cy, Small, Plate), "▶", S.Plate))
                        Change(mod, row, row.Cycled(true));
                    break;
                case SettingKind.Number:
                {
                    var number = Convert.ToDouble(row.Value);
                    if (Button("less:" + key, new Rect(ControlX, cy, Small, Plate), "−", S.Plate, number > row.Min))
                        Change(mod, row, row.Stepped(false));
                    Text("value:" + key, new Rect(ControlX + Small + 8, cy, ControlW - 2 * Small - 16, Plate), row.Text, value);
                    if (Button("more:" + key, new Rect(ControlX + ControlW - Small, cy, Small, Plate), "+", S.Plate, number < row.Max))
                        Change(mod, row, row.Stepped(true));
                    break;
                }
            }
        }

        /// <summary>One of the mod's one-time actions: a wide plate, its label over the line that says what it does.</summary>
        private void DrawAction(OwnerMod mod, ModAction action, Rect r)
        {
            var key = $"action:{mod.Id}:{action.Index}";
#if !PUBLIC_RELEASE
            Texts[key] = action.Help == null ? action.Label : action.Label + "\n" + action.Help;
            Areas[key] = r;
#endif
            var text = action.Help == null ? $"<b>{action.Label}</b>" : S.Two(action.Label, action.Help);
            if (Button(key, r, text, S.Plate.Sized(27, 22)))
            {
                queuedMod = mod;
                queuedAction = action;
                notice = action.Label + "…";
                S.Changed();
            }
        }

        /// <summary>A line of the Keys card: what the key does, and the key (or the text, or the value), read only.</summary>
        private void DrawKey(SettingRow row, Rect r)
        {
            var name = row.Label;
            Text("keyname:" + row.Field.Name, new Rect(r.x + 24, r.y, KeyNameW, r.height), name, KeyName);
            var text = row.Text.Length > 0 ? row.Text : "(none)";
            Text("key:" + row.Field.Name, new Rect(r.x + 24 + KeyNameW + 24, r.y, r.width - KeyNameW - 72, r.height), text, KeyValue);
        }

        private void Change(OwnerMod mod, SettingRow row, object value)
        {
            notice = mod.Apply(row, value);
            InvalidateCache();
            S.Changed();
        }

        private void Paging(int pages, int per)
        {
            var count = per > 1 ? (pages + per - 1) / per : pages;
            if (count <= 1)
                return;
            if (Button("prev", new Rect(Left, 776 - 2, 140, Plate), "◀", S.Plate, page > 0))
            {
                page--;
                notice = null;
                S.Changed();
            }
            if (Button("nextpage", new Rect(Left + Width - 140, 776 - 2, 140, Plate), "▶", S.Plate, page < count - 1))
            {
                page++;
                notice = null;
                S.Changed();
            }
            S.Label(new Rect(Left + 152, 776 - 2, Width - 304, Plate), $"{page + 1} / {count}", S.Centered(S.SoftText).With(TextAnchor.MiddleCenter, false));
        }

        // ---- Rows to pages -----------------------------------------------------

        private enum Line
        {
            Heading,
            Setting,
            Space,
            KeysTitle,
            Key,
            Action,
        }

        /// <summary>One line of a page: a heading, a setting, a space, the Keys card's title, one of its keys, or a one-time action.</summary>
        private sealed class Item
        {
            internal Line Kind;
            internal SettingRow Row;
            internal RowShape Shape;
            internal ModAction Action;
            internal string Text;
            /// <summary>Its height with the gap under it, and its top on the page.</summary>
            internal float Height, Y;
        }

        /// <summary>A mod's settings laid out: its sections (for the section plates) and its pages.</summary>
        private sealed class Layout
        {
            internal readonly List<string> Sections = new List<string>();
            internal readonly List<int> FirstPage = new List<int>();
            internal readonly List<List<Item>> Pages = new List<List<Item>> { new List<Item>() };
            internal readonly List<List<Rect>> KeyCards = new List<List<Rect>>();
            internal readonly List<int> SectionOfPage = new List<int> { 0 };
            internal List<ModAction> Equipment;
            internal bool Chips;
            internal float Top;
        }

        /// <summary>Where a setting row's parts go across the row, and how tall it is.</summary>
        private struct RowShape
        {
            /// <summary>Label and Note are the measured heights of the label and the description (0 for none); Block both with the gap between.</summary>
            internal float TextX, TextW, Label, Note, Block, ControlX, BadgeX, Height;
        }

        private static RowShape Shape(SettingRow row)
        {
            var shape = new RowShape { TextX = Left + 20 };
            shape.ControlX = row.Kind == SettingKind.Toggle ? Left + Width - 16 - ToggleW : ControlX;
            var textRight = shape.ControlX - 24;
            if (row.Badge != null)
            {
                shape.BadgeX = shape.ControlX - 16 - BadgeW;
                textRight = shape.BadgeX - 16;
            }
            shape.TextW = textRight - shape.TextX;
            shape.Label = TabletSurface.Measure(row.Label, RowLabel, shape.TextW);
            shape.Note = string.IsNullOrEmpty(row.Description) ? 0f : TabletSurface.Measure(row.Description, RowNote, shape.TextW);
            shape.Block = shape.Label + (shape.Note > 0 ? NoteGap + shape.Note : 0f);
            shape.Height = Mathf.Max(MinRow, shape.Block + Slack + 2 * RowPad);
            return shape;
        }

        /// <summary>
        /// A mod's rows as sections and pages: its one-time actions first,
        /// under "Equipment"; its settings in sections under
        /// their headings (an untitled one first when settings come before any
        /// heading; a section left with no settings dropped, as its heading
        /// would be), spaces kept but never at a page's top; then a Keys card
        /// with every key and text. With three or more headings among its
        /// settings each section, Equipment the first, starts a page and has a
        /// plate above the rows; else a heading never ends a page.
        /// </summary>
        private static Layout Build(OwnerMod mod, List<ModAction> equipment)
        {
            var rows = mod.Rows();
            var sections = new List<KeyValuePair<string, List<SettingRow>>>();
            var keys = new List<SettingRow>();
            List<SettingRow> current = null;
            foreach (var row in rows)
            {
                if (row.Kind == SettingKind.Heading)
                {
                    current = new List<SettingRow>();
                    sections.Add(new KeyValuePair<string, List<SettingRow>>(row.Label, current));
                    continue;
                }
                if (row.Kind == SettingKind.Shown)
                {
                    keys.Add(row);
                    continue;
                }
                if (current == null)
                {
                    current = new List<SettingRow>();
                    sections.Add(new KeyValuePair<string, List<SettingRow>>(null, current));
                }
                current.Add(row);
            }
            sections.RemoveAll(s => s.Value.All(r => r.Kind == SettingKind.Space));
            var actions = equipment ?? new List<ModAction>();
            var layout = new Layout { Chips = sections.Count(s => s.Key != null) >= SectionsForChips, Equipment = actions };
            var titles = sections.Select(s => s.Key ?? "General").ToList();
            if (actions.Count > 0)
                titles.Insert(0, EquipmentTitle);
            if (keys.Count > 0)
                titles.Add(keys.All(k => k.IsKey) ? "Keys" : "Keys and text");
            layout.Sections.AddRange(titles);
            var chipLines = layout.Chips ? (titles.Count + ChipsPerLine - 1) / ChipsPerLine : 0;
            layout.Top = ListTop + chipLines * (ChipHeight + ChipGap) + (chipLines > 0 ? 4 : 0);
            var room = Bottom - layout.Top;

            var lists = new List<List<Item>>();
            if (actions.Count > 0)
            {
                var items = new List<Item> { new Item { Kind = Line.Heading, Text = EquipmentTitle, Height = HeadingHeight } };
                foreach (var action in actions)
                    items.Add(new Item { Kind = Line.Action, Action = action, Height = Mathf.Min(room, ActionHeight + RowGap) });
                lists.Add(items);
            }
            foreach (var section in sections)
            {
                var items = new List<Item>();
                if (section.Key != null)
                    items.Add(new Item { Kind = Line.Heading, Text = section.Key, Height = HeadingHeight });
                foreach (var row in section.Value)
                {
                    if (row.Kind == SettingKind.Space)
                        items.Add(new Item { Kind = Line.Space, Row = row, Height = Mathf.Clamp(row.Space * 2f, 8f, 48f) });
                    else
                    {
                        var shape = Shape(row);
                        items.Add(new Item { Kind = Line.Setting, Row = row, Shape = shape, Height = Mathf.Min(room, shape.Height + RowGap) });
                    }
                }
                lists.Add(items);
            }
            if (keys.Count > 0)
            {
                var items = new List<Item> { new Item { Kind = Line.KeysTitle, Text = titles[titles.Count - 1], Height = HeadingHeight } };
                foreach (var key in keys)
                    items.Add(new Item { Kind = Line.Key, Row = key, Height = Mathf.Min(room, Mathf.Max(KeyMin, TabletSurface.Measure(key.Label, KeyName, KeyNameW) + 14f)) });
                lists.Add(items);
            }

            var used = 0f;
            for (var s = 0; s < lists.Count; s++)
            {
                var items = lists[s];
                for (var i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    var pageItems = layout.Pages[layout.Pages.Count - 1];
                    var startsSection = i == 0 && layout.Chips && pageItems.Count > 0;
                    // A heading never ends a page: it goes with the line under it.
                    var next = (item.Kind == Line.Heading || item.Kind == Line.KeysTitle) && i + 1 < items.Count ? items[i + 1].Height : 0f;
                    if (startsSection || (pageItems.Count > 0 && used + item.Height + next > room))
                    {
                        layout.Pages.Add(pageItems = new List<Item>());
                        layout.SectionOfPage.Add(s);
                        used = 0f;
                    }
                    if (i == 0)
                        layout.FirstPage.Add(layout.Pages.Count - 1);
                    if (item.Kind == Line.Space && pageItems.Count == 0)
                        continue;
                    if (pageItems.Count == 0)
                        layout.SectionOfPage[layout.Pages.Count - 1] = s;
                    item.Y = layout.Top + used;
                    pageItems.Add(item);
                    used += item.Height;
                }
            }
            for (var p = 0; p < layout.Pages.Count; p++)
            {
                var pageItems = layout.Pages[p];
                var cards = new List<Rect>();
                for (var i = 0; i < pageItems.Count; i++)
                {
                    if (pageItems[i].Kind != Line.Key || (i > 0 && pageItems[i - 1].Kind == Line.Key))
                        continue;
                    var last = i;
                    while (last + 1 < pageItems.Count && pageItems[last + 1].Kind == Line.Key)
                        last++;
                    cards.Add(new Rect(Left, pageItems[i].Y, Width, pageItems[last].Y + pageItems[last].Height - pageItems[i].Y));
                }
                layout.KeyCards.Add(cards);
            }
            return layout;
        }

#if !PUBLIC_RELEASE
        // ---- For the self-test ---------------------------------------------------

        /// <summary>Opens a mod's settings (or the list, with null) as its plate would.</summary>
        internal void OpenForTest(OwnerMod mod)
        {
            open = mod;
            page = 0;
            notice = null;
            InvalidateCache();
        }

        /// <summary>Goes to the page of the open mod that shows a field.</summary>
        internal void ShowFieldForTest(string field)
        {
            if (open == null)
                return;
            var pages = LayoutFor(open).Pages;
            var index = pages.FindIndex(p => p.Any(i => i.Row?.Field != null && i.Row.Field.Name == field));
            if (index >= 0)
                page = index;
        }

        /// <summary>The section plates' names in the last pass (none without plates).</summary>
        internal IReadOnlyList<string> SectionsForTest => shown != null && shown.Chips && open != null ? shown.Sections : (IReadOnlyList<string>)Array.Empty<string>();

        /// <summary>The section the page shown is in, or -1.</summary>
        internal int SectionForTest => shown != null && open != null && page < shown.SectionOfPage.Count ? shown.SectionOfPage[page] : -1;

        /// <summary>The page shown and the open mod's pages.</summary>
        internal int PageForTest => page;

        internal int PagesForTest => shown != null && open != null ? shown.Pages.Count : 1;
#endif
    }
}
