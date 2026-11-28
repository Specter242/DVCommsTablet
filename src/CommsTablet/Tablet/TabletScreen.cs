using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CommsTablet.Guests;
using CommsTablet.Pages;
using DV.CabControls;
using DV.Customization.Gadgets;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CommsTablet.Tablet
{
 internal sealed class TabletScreen : MonoBehaviour, ITabletButtons
    {
        internal const float PixelSize = 0.00028f;
        private const float SurfaceZ = -TabletItem.Thickness * 0.5f - 0.0005f;
        /// <summary>
        /// How far a button's box stands out from the screen: 4 mm, and 8 mm in
        /// VR, so a fingertip reaches it sooner (tracker #46). The VR pointer
        /// picks by where its beam meets the screen, so the depth does not move
        /// its aim.
        /// </summary>
        private static readonly float ButtonDepth = VRManager.IsVREnabled() ? 0.008f : 0.004f;
        internal static readonly float ButtonFront = SurfaceZ - ButtonDepth;
        internal const int Width = 1080, Height = 924;
        /// <summary>How far the screen sits above the tablet's middle, leaving room below it for the home button.</summary>
        internal const float ScreenRaise = 0.0095f;
        internal const float HomeButtonRadius = 0.011f;
        /// <summary>The home button's center: halfway between the screen's lower edge and the tablet's.</summary>
        internal static float HomeButtonY => (-TabletItem.Size.y * 0.5f + ScreenRaise - Height * PixelSize * 0.5f) * 0.5f;
        private const float ReferencePixelsPerUnit = 100f;
        private const int MaxTabs = 4, MaxSections = 3, Columns = 3, Rows = 3, PerPage = Columns * Rows;
        // Three rows of four: the tablet's own apps and the merged mods' (nine with both) on one page.
        private const int AppColumns = 4, AppRows = 3, AppsPerPage = AppColumns * AppRows, MaxActions = 4;
        /// <summary>The map layer's buttons: AI Engineer's pool of 96.</summary>
        private const int SlotCount = 96;
        private const float RefreshSeconds = 0.5f;
        private static readonly Regex Tags = new Regex("<[^>]*>");

        private static readonly HashSet<TabletScreen> mapScreens = new HashSet<TabletScreen>();
        private static readonly HashSet<TabletScreen> aheadScreens = new HashSet<TabletScreen>();

        private TabletRadio radio;
        /// <summary>The mounted form this screen is on, if it is not the item's.</summary>
        private GadgetBase gadget;
        private Canvas canvas;
        private RectTransform root;
        private DV.Items.ItemTransparencyHandler transparency;
        private MeshRenderer shell, surface;
        private Material screenMaterial;
        private Mesh screenMesh;
        private RenderTexture texture;
        private bool dirty = true, visible, controlsBuilt;
        private float lastVisibleAt, refreshAt;
        private readonly Dictionary<Texture2D, Sprite> panelSprites = new Dictionary<Texture2D, Sprite>();

        // The top bar: the app's name and its tabs; on the home screen, the tablet's name.
        private Image headingPlate;
        private Text heading, homeTitle;
        private readonly Image[] tabPlates = new Image[MaxTabs];
        private readonly Text[] tabNames = new Text[MaxTabs];
        private readonly GameObject[] tabButtons = new GameObject[MaxTabs];
        private readonly List<TabletPage> tabPages = new List<TabletPage>();
        private readonly Image[] sectionPlates = new Image[MaxSections], tilePlates = new Image[PerPage];
        private readonly Text[] sectionNames = new Text[MaxSections], tileNames = new Text[PerPage];
        private readonly Image[] tilePictures = new Image[PerPage];
        private readonly GameObject[] sectionButtons = new GameObject[MaxSections], tileButtons = new GameObject[PerPage];
        // The home screen's icons: a shadow, a badge, the game's picture (or a drawn symbol), and a name.
        private readonly RawImage[] appShadows = new RawImage[AppsPerPage], appBadges = new RawImage[AppsPerPage], appGlyphs = new RawImage[AppsPerPage];
        private readonly Image[] appArt = new Image[AppsPerPage];
        private readonly Text[] appNames = new Text[AppsPerPage];
        private readonly GameObject[] appButtons = new GameObject[AppsPerPage];
        private readonly List<TabletApp> apps = new List<TabletApp>();
        private GameObject homeButton;
        private int homeScroll;
        // An app without a list: the mode's display large, how to use it, and its buttons.
        private readonly Image[] actionPlates = new Image[MaxActions];
        private readonly Text[] actionNames = new Text[MaxActions];
        private readonly GameObject[] actionButtons = new GameObject[MaxActions];
        private Image readPanel, statusPanel;
        private RawImage readBar, readArrow;
        private Text readTitle, readContent, readAction, guide;
        private readonly List<string> actionsShown = new List<string>();
        private readonly List<(DV.ThingTypes.TrainCarLivery, DV.Customization.Paint.PaintTheme, TrainCar)> sideViewRequests =
            new List<(DV.ThingTypes.TrainCarLivery, DV.Customization.Paint.PaintTheme, TrainCar)>();
        // The layer a page that draws itself draws on, its pass's plates on a pool of buttons
        // (slots) whose colliders move to them, and the button over the open part of a map.
        private RectTransform layer;
        private TabletSurface layerSurface;
        private bool layerUsed;
        private string layerFailed;
        private GameObject[] slots;
        private BoxCollider[] slotHits;
        private Rect[] slotPlaced;
        private GameObject mapButton;
        private BoxCollider mapHit;
        private Rect mapPlaced;

#if !PUBLIC_RELEASE
        /// <summary>Where the self-test points instead of the mouse, for a press on the map's button.</summary>
        internal static Vector2? PointerForTest;
#endif
        private GameObject prevButton, nextButton;
        private Image prevPlate, nextPlate;
        private RawImage prevArrow, nextArrow, directionArrow;
        private Text pageCount, empty, statusTitle, statusContent, statusAction;

        private bool statusValid, lastReadout, lastArrow, lastLeft, wasVisible;
        private string lastStatusTitle, lastStatusContent, lastStatusAction;
        private CleanCache cleanTitle, cleanContent, cleanAction;
        private int seenVersion = -1;

        private struct CleanCache
        {
            private string raw;
            private FontStyles style;
            private string cleaned;

            internal string Get(TextMeshPro text)
            {
                if (text == null)
                    return "";
                var current = text.text ?? "";
                var currentStyle = text.fontStyle;
                if (cleaned != null && currentStyle == style && current == raw)
                    return cleaned;
                raw = current;
                style = currentStyle;
                cleaned = Clean(current, currentStyle);
                return cleaned;
            }

            internal void Clear()
            {
                raw = null;
                cleaned = null;
            }
        }

        /// <summary>A guest's view on this canvas: its region, the id the guest gave it, and whether it asked to be drawn.</summary>
        private sealed class GuestView
        {
            internal GuestApp Guest;
            internal int Id;
            internal RectTransform Region;
            internal bool Dirty;
        }

        private readonly List<GuestView> views = new List<GuestView>();
        private GuestView shownView;
        private TabletPage refreshedPage;

#if !PUBLIC_RELEASE
        /// <summary>Times the canvas was drawn into the texture, for the self-test.</summary>
        internal int Renders { get; private set; }
#endif

        /// <summary>Whether this is the mounted form's screen, where nothing can be aimed.</summary>
        internal bool Mounted => gadget != null;

        /// <summary>The status line of a page that aims, on the mounted screen.</summary>
        internal const string MountedNotice = "On the mount: take the tablet in hand to aim";

        private void Start()
        {
            gadget = GetComponent<GadgetBase>();
            radio = GetComponent<TabletRadio>();
            // The mounted form shows the item's tablet: its apps, pages, and modes.
            if (radio == null && gadget != null && gadget.GadgetItem != null)
                radio = gadget.GadgetItem.GetComponent<TabletRadio>();
            if (radio != null && !Mounted)
                radio.HeldChanged += HeldChanged;
            if (radio != null)
                radio.GuestCarChosen += GuestCarChosen;
            transparency = GetComponent<DV.Items.ItemTransparencyHandler>();
            shell = GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r => r.name == "[comms tablet shell]");
            SideViews.Drawn += ViewDrawn;
            Build();
        }

        private void OnEnable()
        {
            refreshAt = 0f;
            lastVisibleAt = Time.unscaledTime;
            dirty = true;
            if (radio != null) Build();
        }

        private void OnDisable()
        {
            SideViews.Unpin(this);
            UntrackSelfDrawn();
            // A stashed tablet's update never runs to put the map's page copies away.
            if (!AnyActive(mapScreens))
                Map.BookletRenderer.Hide();
            ReleaseTexture();
            ReleaseCanvas();
        }

        private void Update()
        {
            if (canvas == null || radio == null)
                return;
            CloseFailed();
            // A one-time action pressed on the last pass runs now, outside the canvas pass: it may change
            // what the player holds. Nothing more this frame; the next one draws its notice.
            if (radio.Current != null && radio.Current.RunQueued())
            {
                refreshAt = 0f;
                return;
            }
            Map.TabletMap.PutAwayIfUnseen();
            var camera = PlayerManager.ActiveCamera;
            visible = camera != null && shell != null && shell.enabled && shell.gameObject.activeInHierarchy
                && (transparency == null || !transparency.IsTransparent)
                && (camera.transform.position - transform.position).sqrMagnitude < 64f;
            surface.enabled = visible && texture != null;
            if (!visible || !shell.isVisible)
            {
                wasVisible = false;
                if (Time.unscaledTime - lastVisibleAt > 10f) ReleaseTexture();
                return;
            }
            if (!wasVisible)
            {
                wasVisible = true;
                refreshAt = 0f;
            }
            lastVisibleAt = Time.unscaledTime;
            SideViews.Pump();
            if (Time.unscaledTime >= refreshAt || radio.Version != seenVersion)
                Refresh();
            ShowStatus();
            // Each frame while shown and seen.
            if (shownView != null)
                shownView.Guest.Update(shownView.Id);
        }

        // ---- Content -------------------------------------------------------

        private void ResetStatusCache()
        {
            statusValid = false;
            lastStatusTitle = lastStatusContent = lastStatusAction = null;
            cleanTitle.Clear();
            cleanContent.Clear();
            cleanAction.Clear();
        }

        private static void SetActive(Component part, bool active, ref bool changed)
        {
            var go = part.gameObject;
            if (go.activeSelf == active)
                return;
            go.SetActive(active);
            changed = true;
        }

        private static void SetButtonActive(GameObject button, bool active)
        {
            if (button.activeSelf != active)
                button.SetActive(active);
        }

        private static void SetText(Text label, string value, ref bool changed)
        {
            var next = value ?? "";
            if (label.text == next)
                return;
            label.text = next;
            changed = true;
        }

        private static void SetColor(Graphic graphic, Color value, ref bool changed)
        {
            if (graphic.color == value)
                return;
            graphic.color = value;
            changed = true;
        }

        private static void SetFontStyle(Text label, FontStyle value, ref bool changed)
        {
            if (label.fontStyle == value)
                return;
            label.fontStyle = value;
            changed = true;
        }

        private static void SetSprite(Image image, Sprite value, ref bool changed)
        {
            if (image.sprite == value)
                return;
            image.sprite = value;
            changed = true;
        }

        private static void SetTexture(RawImage image, Texture value, ref bool changed)
        {
            if (image.texture == value)
                return;
            image.texture = value;
            changed = true;
        }

        private static void SetLocalScale(RectTransform rect, Vector3 value, ref bool changed)
        {
            if (rect.localScale == value)
                return;
            rect.localScale = value;
            changed = true;
        }

        private static void Place(RectTransform rect, Rect area, ref bool changed)
        {
            var anchor = new Vector2(0f, 1f);
            var pos = new Vector2(area.x, -area.y);
            if (rect.anchorMin == anchor && rect.anchorMax == anchor && rect.pivot == anchor
                && rect.anchoredPosition == pos && rect.sizeDelta == area.size)
                return;
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = pos;
            rect.sizeDelta = area.size;
            changed = true;
        }

        private bool ActionsChanged(IReadOnlyList<string> current)
        {
            var count = current?.Count ?? 0;
            if (count != actionsShown.Count)
                return true;
            for (var i = 0; i < count; i++)
                if (current[i] != actionsShown[i])
                    return true;
            return false;
        }

        private void Refresh()
        {
            refreshAt = Time.unscaledTime + RefreshSeconds;
            seenVersion = radio.Version;
            // A page no longer offered (the cheat setting turned off, say) gives way to
            // another of its app's, or closes to the home screen.
            if (radio.Current != null && !radio.Current.Available)
                radio.Show(radio.CurrentApp?.Opening);
            var page = radio.Current;
            var app = radio.CurrentApp;
            var home = page == null;
            page?.Refresh();
            // A page that draws itself (the map) has none of the parts below: its layer draws it.
            var drawn = page != null && page.DrawsItself;
            // A guest's app has the whole screen: none of the tablet's own parts show.
            var guest = page as GuestPage;
            var changed = page != refreshedPage;
            if (changed)
                ResetStatusCache();
            if (guest == null)
                HideGuest();

            // The top bar.
            SetActive(homeTitle, home, ref changed);
            SetActive(headingPlate, !home && guest == null, ref changed);
            SetActive(heading, !home && guest == null, ref changed);
            SetText(heading, app?.Name ?? page?.Name ?? "", ref changed);
            tabPages.Clear();
            if (app != null)
            {
                var appPages = app.Pages;
                for (var i = 0; i < appPages.Length; i++)
                    if (appPages[i].Available)
                        tabPages.Add(appPages[i]);
            }
            var tabbed = tabPages.Count > 1;
            for (var i = 0; i < MaxTabs; i++)
            {
                var on = tabbed && i < tabPages.Count;
                SetActive(tabPlates[i], on, ref changed);
                SetActive(tabNames[i], on, ref changed);
                SetButtonActive(tabButtons[i], on);
                if (!on) continue;
                var current = tabPages[i] == page;
                SetSprite(tabPlates[i], PanelSprite(current ? TabletArt.PlateOn : TabletArt.PlateOff), ref changed);
                SetText(tabNames[i], tabPages[i].Tab, ref changed);
                SetColor(tabNames[i], current ? TabletArt.Amber : TabletArt.Text, ref changed);
                SetFontStyle(tabNames[i], current ? FontStyle.Bold : FontStyle.Normal, ref changed);
            }

            // The home screen's icons.
            apps.Clear();
            var allApps = radio.Apps;
            for (var i = 0; i < allApps.Count; i++)
                if (allApps[i].Available)
                    apps.Add(allApps[i]);
            homeScroll = Mathf.Clamp(homeScroll - homeScroll % AppsPerPage, 0, Mathf.Max(0, (apps.Count - 1) / AppsPerPage * AppsPerPage));
            for (var i = 0; i < AppsPerPage; i++)
            {
                var index = homeScroll + i;
                var on = home && index < apps.Count;
                var shown = on ? apps[index] : null;
                var art = on ? TabletIcons.Find(shown.Art) : null;
                SetActive(appShadows[i], on, ref changed);
                SetActive(appBadges[i], on, ref changed);
                SetActive(appArt[i], art != null, ref changed);
                SetActive(appGlyphs[i], on && art == null, ref changed);
                SetActive(appNames[i], on, ref changed);
                SetButtonActive(appButtons[i], on);
                if (!on) continue;
                if (art != null)
                    SetSprite(appArt[i], art, ref changed);
                else
                {
                    var guestIcon = shown.Guest?.Icon;
                    SetTexture(appGlyphs[i], guestIcon ?? TabletArt.Glyph(shown.Glyph), ref changed);
                    // A guest may update its Icon texture in-place via Texture2D.Apply().
                    if (guestIcon != null)
                        changed = true;
                }
                // A tile that is on (the light) glows: its glyph bright, a warm halo round its badge.
                var lit = shown.On != null && shown.On();
                var glyphColor = lit ? Color.white : TabletArt.Amber;
                SetColor(appArt[i], glyphColor, ref changed);
                SetColor(appGlyphs[i], glyphColor, ref changed);
                SetTexture(appShadows[i], lit ? TabletArt.Glow : TabletArt.Shadow, ref changed);
                SetColor(appShadows[i], lit ? TabletArt.Amber : Color.white, ref changed);
                SetText(appNames[i], shown.Name, ref changed);
            }

            var readout = page != null && !drawn && page.Readout;
            SetActive(readPanel, readout, ref changed);
            SetActive(readBar, readout, ref changed);
            SetActive(readTitle, readout, ref changed);
            SetActive(readContent, readout, ref changed);
            SetActive(readAction, readout, ref changed);
            var showStatus = !readout && guest == null;
            SetActive(statusTitle, showStatus, ref changed);
            SetActive(statusContent, showStatus, ref changed);
            SetActive(statusAction, showStatus, ref changed);
            SetActive(statusPanel, showStatus, ref changed);
            if (guest != null)
            {
                SetActive(directionArrow, false, ref changed);
                SetActive(readArrow, false, ref changed);
                ResetStatusCache();
            }
            SetActive(guide, readout && page.Guide != null, ref changed);
            SetText(guide, readout ? page.Guide : "", ref changed);
            var actions = drawn ? null : page?.Actions;
            actionsShown.Clear();
            if (actions != null)
                for (var i = 0; i < actions.Count; i++)
                    actionsShown.Add(actions[i]);
            for (var i = 0; i < MaxActions; i++)
            {
                var on = actions != null && i < actions.Count;
                SetActive(actionPlates[i], on, ref changed);
                SetActive(actionNames[i], on, ref changed);
                SetButtonActive(actionButtons[i], on);
                if (on) SetText(actionNames[i], actions[i], ref changed);
            }

            var sections = drawn ? null : page?.Sections;
            for (var i = 0; i < MaxSections; i++)
            {
                var on = sections != null && i < sections.Length;
                SetActive(sectionPlates[i], on, ref changed);
                SetActive(sectionNames[i], on, ref changed);
                SetButtonActive(sectionButtons[i], on);
                if (!on) continue;
                var current = i == page.Section;
                SetSprite(sectionPlates[i], PanelSprite(current ? TabletArt.ChipOn : TabletArt.Chip), ref changed);
                SetText(sectionNames[i], sections[i], ref changed);
                SetColor(sectionNames[i], current ? TabletArt.Amber : TabletArt.Soft, ref changed);
                SetFontStyle(sectionNames[i], current ? FontStyle.Bold : FontStyle.Normal, ref changed);
            }

            var tiles = drawn ? null : page?.Tiles;
            var count = tiles?.Count ?? 0;
            if (page != null)
                page.Scroll = Mathf.Clamp(page.Scroll - page.Scroll % PerPage, 0, Mathf.Max(0, (count - 1) / PerPage * PerPage));
            var first = page?.Scroll ?? 0;
            // Side views are drawn for the tiles on screen only.
            sideViewRequests.Clear();
            var shownTiles = Mathf.Max(0, Mathf.Min(PerPage, count - first));
            for (var i = 0; i < shownTiles; i++)
            {
                var tile = tiles[first + i];
                if (tile.Livery != null)
                    sideViewRequests.Add((tile.Livery, tile.Theme, tile.Car));
            }
            SideViews.Pin(this, sideViewRequests);
            SideViews.Request(sideViewRequests);
            for (var i = 0; i < PerPage; i++)
            {
                var index = first + i;
                var on = index < count;
                SetActive(tilePlates[i], on, ref changed);
                SetActive(tileNames[i], on, ref changed);
                SetButtonActive(tileButtons[i], on);
                var picture = on ? SideViews.Get(tiles[index].Livery, tiles[index].Theme) ?? tiles[index].Picture : null;
                SetActive(tilePictures[i], picture != null, ref changed);
                if (!on) continue;
                var tile = tiles[index];
                var current = page.IsOn(index);
                SetSprite(tilePlates[i], PanelSprite(current ? TabletArt.CardOn : TabletArt.Card), ref changed);
                SetText(tileNames[i], tile.Label, ref changed);
                SetColor(tileNames[i], tile.Back ? TabletArt.Soft : current ? TabletArt.Amber : TabletArt.Text, ref changed);
                SetFontStyle(tileNames[i], current ? FontStyle.Bold : FontStyle.Normal, ref changed);
                if (picture != null)
                    SetSprite(tilePictures[i], picture, ref changed);
                // A back tile has no picture: its label fills it.
                Place(tileNames[i].rectTransform, TileLabel(i, picture == null), ref changed);
            }
            // The home screen pages through apps as an app pages through its choices.
            var per = home ? AppsPerPage : PerPage;
            if (home)
            {
                count = apps.Count;
                first = homeScroll;
            }
            var pages = (count + per - 1) / per;
            var paging = pages > 1;
            SetButtonActive(prevButton, paging && first > 0);
            SetButtonActive(nextButton, paging && first + per < count);
            SetActive(prevPlate, paging, ref changed);
            SetActive(nextPlate, paging, ref changed);
            SetActive(prevArrow, paging, ref changed);
            SetActive(nextArrow, paging, ref changed);
            SetColor(prevArrow, first > 0 ? TabletArt.Text : TabletArt.Dim, ref changed);
            SetColor(nextArrow, first + per < count ? TabletArt.Text : TabletArt.Dim, ref changed);
            // The page's hint, then where in the list this is.
            var hint = drawn ? null : page?.Hint;
            var place = paging ? $"{first / per + 1} / {pages}" : null;
            SetActive(pageCount, place != null || hint != null, ref changed);
            SetText(pageCount, hint != null && place != null ? $"{place}    {hint}" : hint ?? place, ref changed);

            if (guest == null)
            {
                if (DrawLayer(drawn ? page : null))
                    changed = true;
            }
            else
            {
                UntrackSelfDrawn();
                // The map's plates go before the guest takes the buttons.
                if (layerUsed && layerSurface != null)
                {
                    if (layerSurface.Run(() => { }))
                        changed = true;
                    layerUsed = false;
                }
                ShowGuest(guest);
            }
            SetActive(empty, !readout && !drawn && guest == null && count == 0, ref changed);
            SetText(empty, home
                ? "No apps here yet.\nCrew is offered in Career; Spawn, Cargo, and the Workshop with the comms radio's cheat setting."
                : page.EmptyText, ref changed);
            // Only redraw the canvas texture when something visual actually changed.
            if (changed)
                dirty = true;
            refreshedPage = page;
        }

        // The radio display of the active mode, read each frame, redrawn when it changes.
        private void ShowStatus()
        {
            var display = radio.Display;
            var page = radio.Current;
            if (page is GuestPage)
                return;
            string title, content, action;
            if (page == null)
            {
                title = "COMMS TABLET";
                content = "Choose an app";
                action = "";
            }
            else if (display == null || page.Mode == null)
            {
                title = page.Name;
                content = page.Notice ?? page.Status ?? "";
                action = "";
            }
            else if (Mounted)
            {
                // The modes aim from the tablet in hand; on the mount they wait.
                title = page.Name;
                content = page.Notice ?? MountedNotice;
                action = "";
            }
            else
            {
                title = cleanTitle.Get(display.title);
                content = page.Notice ?? cleanContent.Get(display.content);
                action = page.Notice != null ? "" : cleanAction.Get(display.action);
            }
            var arrow = radio.Arrow != null && radio.Arrow.gameObject.activeSelf && page?.Mode != null && !Mounted;
            var left = arrow && radio.Arrow.transform.localEulerAngles.y < 90f;
            var readout = page != null && page.Readout;
            // The modes change step inside their own updates (aiming onto a car, say): their buttons follow.
            if (page != null && !page.DrawsItself && ActionsChanged(page.Actions))
                refreshAt = 0f;
            if (statusValid && readout == lastReadout && arrow == lastArrow && left == lastLeft
                && title == lastStatusTitle && content == lastStatusContent && action == lastStatusAction)
                return;
            statusValid = true;
            lastReadout = readout;
            lastArrow = arrow;
            lastLeft = left;
            lastStatusTitle = title;
            lastStatusContent = content;
            lastStatusAction = action;
            var changed = false;
            SetText(statusTitle, readout ? "" : title, ref changed);
            SetText(statusContent, readout ? "" : content, ref changed);
            SetText(statusAction, readout ? "" : action, ref changed);
            SetText(readTitle, readout ? title : "", ref changed);
            SetText(readContent, readout ? content : "", ref changed);
            SetText(readAction, readout ? action : "", ref changed);
            var scale = new Vector3(left ? -1f : 1f, 1f, 1f);
            SetActive(directionArrow, arrow && !readout, ref changed);
            SetLocalScale(directionArrow.rectTransform, scale, ref changed);
            SetActive(readArrow, arrow && readout, ref changed);
            SetLocalScale(readArrow.rectTransform, scale, ref changed);
            if (changed)
                dirty = true;
        }

        private static string Clean(string rawText, FontStyles fontStyle)
        {
            if (string.IsNullOrEmpty(rawText))
                return "";
            // The radio's texts carry escaped line breaks ("\\n") that its TextMeshPro reads as breaks.
            var raw = rawText.IndexOf("\\n", System.StringComparison.Ordinal) >= 0 ? rawText.Replace("\\n", "\n") : rawText;
            var hasTag = raw.IndexOf('<') >= 0;
            var upper = (fontStyle & FontStyles.UpperCase) != 0 || (hasTag && raw.IndexOf("<uppercase>", System.StringComparison.Ordinal) >= 0);
            var plain = (hasTag ? Tags.Replace(raw, "") : raw).Trim();
            return upper ? plain.ToUpperInvariant() : plain;
        }

        // ---- Presses -------------------------------------------------------

        private void PressHome()
        {
            radio.Show(null);
            refreshAt = 0f;
        }

        private void PressApp(int i)
        {
            var index = homeScroll + i;
            if (radio.Current != null || index >= apps.Count)
                return;
            if (apps[index].Toggle != null)
            {
                apps[index].Toggle();
                refreshAt = 0f;
                return;
            }
            var page = apps[index].Opening;
            if (page == null)
                return;
            page.Notice = null;
            radio.Show(page);
            refreshAt = 0f;
        }

        private void PressTab(int i)
        {
            if (i >= tabPages.Count)
                return;
            tabPages[i].Notice = null;
            radio.Show(tabPages[i]);
            refreshAt = 0f;
        }

        private void PressSection(int i)
        {
            radio.Current?.SelectSection(i);
            refreshAt = 0f;
        }

        private void PressTile(int i)
        {
            var page = radio.Current;
            if (page == null)
                return;
            page.Press(page.Scroll + i);
            refreshAt = 0f;
        }

        private void PressAction(int i)
        {
            var page = radio.Current;
            var actions = page?.Actions;
            if (actions == null || i >= actions.Count)
                return;
            page.Act(i);
            refreshAt = 0f;
        }

        private void Turn(int pages)
        {
            var page = radio.Current;
            if (page == null)
                homeScroll = Mathf.Max(0, homeScroll + pages * AppsPerPage);
            else
                page.Scroll = Mathf.Max(0, page.Scroll + pages * PerPage);
            refreshAt = 0f;
        }

        // ---- The layer of a page that draws itself (the map) ------------------

        /// <summary>A pass of a page that draws itself; with none, an empty pass hides what the last one drew. Returns whether any visual changed.</summary>
        private bool DrawLayer(TabletPage page)
        {
            TrackSelfDrawn(page);
            if (layerSurface == null)
                return false;
            if (page == null)
            {
                var changed = false;
                if (layerUsed)
                {
                    changed = layerSurface.Run(() => { });
                    layerUsed = false;
                }
                PlaceMapButton(UnityEngine.Rect.zero);
                return changed;
            }
            layerUsed = true;
            bool drewChange;
            try
            {
                drewChange = layerSurface.Run(page.Draw);
                layerFailed = null;
            }
            catch (System.Exception e)
            {
                // Logged once, not at every refresh.
                if (layerFailed != page.Name)
                    Main.Mod?.Logger.LogException($"The tablet's {page.Name} page failed to draw", e);
                layerFailed = page.Name;
                drewChange = layerSurface.Run(() => { });
            }
            PlaceMapButton(page.PressArea);
            return drewChange;
        }

        /// <summary>The button over the open part of a map: on where the page has one, pressed where the player points.</summary>
        private void PlaceMapButton(Rect area)
        {
            if (mapButton == null)
                return;
            var on = area.width > 0 && area.height > 0;
            if (on && area != mapPlaced)
            {
                mapPlaced = area;
                Put(mapHit, area);
            }
            if (mapButton.activeSelf != on)
                mapButton.SetActive(on);
        }

        public int Count => SlotCount;

        public void Place(int index, Rect canvasRect, bool active)
        {
            if (slotPlaced[index] != canvasRect)
            {
                slotPlaced[index] = canvasRect;
                Put(slotHits[index], canvasRect);
            }
            if (slots[index].activeSelf != active)
                slots[index].SetActive(active);
        }

        public Rect RectOf(int index) => index >= 0 && index < SlotCount ? slotPlaced[index] : default;

        public void HideFrom(int index)
        {
            if (slots == null)
                return;
            for (var i = index; i < SlotCount; i++)
                if (slots[i].activeSelf)
                    slots[i].SetActive(false);
        }

        private void SlotPressed(int slot)
        {
            if (shownView != null)
            {
                shownView.Guest.Pressed(shownView.Id, slot);
                Redraw(shownView);
                return;
            }
            layerSurface?.Press(slot);
            refreshAt = 0f;
        }

        private void MapPressed()
        {
            var page = radio != null ? radio.Current : null;
            var at = Pointer();
            if (page == null || !at.HasValue)
                return;
            if (shownView != null)
            {
                shownView.Guest.PressAt(shownView.Id, at.Value);
                Redraw(shownView);
                return;
            }
            page.PressAt(at.Value);
            refreshAt = 0f;
        }


        /// <summary>Shows a guest's app: its view on this canvas, opened the first time, and the buttons for it.</summary>
        private void ShowGuest(GuestPage page)
        {
            GuestView view = null;
            foreach (var v in views)
                if (v.Guest == page.Guest)
                    view = v;
            if (view == null)
                view = OpenView(page.Guest);
            if (view == null)
                return;
            if (shownView != view)
            {
                HideGuest();
                shownView = view;
                view.Region.gameObject.SetActive(true);
                HideFrom(0);
                view.Guest.Shown(view.Id, true);
                Redraw(view);
            }
            PlaceGuestButton(view);
        }

        /// <summary>Hides the guest's app shown, if any: its region, its buttons, and the pointed button.</summary>
        private void HideGuest()
        {
            var view = shownView;
            if (view == null)
                return;
            shownView = null;
            if (view.Region != null)
                view.Region.gameObject.SetActive(false);
            HideFrom(0);
            PlaceMapButton(UnityEngine.Rect.zero);
            view.Guest.Shown(view.Id, false);
            dirty = true;
        }

        private GuestView OpenView(GuestApp guest)
        {
            if (guest.Failed)
                return null;
            var region = Rect("[guest " + guest.Id + "]", new Rect(0, 0, Width, Height));
            region.SetAsLastSibling();
            region.gameObject.SetActive(false);
            var view = new GuestView { Guest = guest, Region = region };
            void Invalidate()
            {
                view.Dirty = true;
                if (shownView == view)
                    dirty = true;
            }
            // Only the view shown has the buttons; slots outside the pool are ignored.
            void PlaceSlot(int slot, Rect area, bool active)
            {
                if (shownView == view && slots != null && slot >= 0 && slot < SlotCount)
                    Place(slot, area, active);
            }
            void HideSlotsFrom(int from)
            {
                if (shownView == view)
                    HideFrom(Mathf.Max(0, from));
            }
            var id = guest.Open(region, gameObject, Invalidate, PlaceSlot, HideSlotsFrom, SlotCount, VRManager.IsVREnabled());
            if (guest.Failed)
            {
                Object.Destroy(region.gameObject);
                return null;
            }
            view.Id = id;
            views.Add(view);
            guest.Held(id, !Mounted && radio.Held);
            // A car-first guest hears the car its mode has already chosen (guest contract 2).
            var chosen = radio.GuestCar(guest)?.Car;
            if (chosen != null)
                guest.Chosen(id, chosen);
            return view;
        }

        /// <summary>The guest's view is drawn and the canvas rendered at the next LateUpdate (after a press, or when it asks).</summary>
        private void Redraw(GuestView view)
        {
            view.Dirty = true;
            dirty = true;
        }

        /// <summary>The pointed button over the guest's press area; never in VR, where it cannot follow a hand.</summary>
        private void PlaceGuestButton(GuestView view) =>
            PlaceMapButton(VRManager.IsVREnabled() ? UnityEngine.Rect.zero : view.Guest.PressArea(view.Id));

        /// <summary>A guest that threw: its views here are closed and let go, and the screen goes home at its next refresh.</summary>
        private void CloseFailed()
        {
            for (var i = views.Count - 1; i >= 0; i--)
            {
                var view = views[i];
                if (!view.Guest.Failed)
                    continue;
                if (shownView == view)
                {
                    shownView = null;
                    HideFrom(0);
                    PlaceMapButton(UnityEngine.Rect.zero);
                    refreshAt = 0f;
                }
                views.RemoveAt(i);
                view.Guest.Close(view.Id);
                if (view.Region != null)
                    Object.Destroy(view.Region.gameObject);
                dirty = true;
            }
        }

        /// <summary>Closes every guest's view before the canvas is let go (stashed, mounted, or destroyed).</summary>
        private void CloseViews()
        {
            var open = views.ToList();
            views.Clear();
            shownView = null;
            foreach (var view in open)
                view.Guest.Close(view.Id);
        }

        private void HeldChanged(bool held)
        {
            foreach (var view in views.ToList())
                view.Guest.Held(view.Id, held);
        }

        /// <summary>A car-first guest's mode chose a car, or let it go: each of its views here is told, and redrawn.</summary>
        private void GuestCarChosen(GuestApp guest, TrainCar car)
        {
            foreach (var view in views.ToList())
                if (view.Guest == guest)
                {
                    guest.Chosen(view.Id, car);
                    Redraw(view);
                }
        }

        /// <summary>The item's use, while a guest's app is shown here: whether the guest took it.</summary>
        internal bool GuestUse(GuestPage page)
        {
            var view = shownView;
            if (view == null || view.Guest != page.Guest)
                return false;
            var taken = view.Guest.Use(view.Id);
            if (taken)
                Redraw(view);
            return taken;
        }

        /// <summary>The scroll wheel, while a guest's app is shown here: whether the guest took it.</summary>
        internal bool GuestScroll(GuestPage page, bool forward)
        {
            var view = shownView;
            if (view == null || view.Guest != page.Guest)
                return false;
            var taken = view.Guest.Scroll(view.Id, forward);
            if (taken)
                Redraw(view);
            return taken;
        }

#if !PUBLIC_RELEASE
        /// <summary>The guest view open here for a guest, for the self-test: its id and region, or null.</summary>
        internal (int Id, RectTransform Region)? ViewOf(GuestApp guest)
        {
            foreach (var view in views)
                if (view.Guest == guest)
                    return (view.Id, view.Region);
            return null;
        }

        /// <summary>Whether the tablet's own heading and status strip show (not on a guest's screen), for the self-test.</summary>
        internal bool HostPartsShown => headingPlate != null && (headingPlate.gameObject.activeSelf || statusPanel.gameObject.activeSelf);
#endif

        /// <summary>
        /// Where a ray meets the drawn screen, and how far along the ray;
        /// false if it runs alongside or away. The VR pointer's aim
        /// (<see cref="TabletPointer"/>).
        /// </summary>
        internal bool PointOnScreen(Ray ray, out Vector3 point, out float distance)
        {
            point = default;
            var plane = new Plane(transform.forward, transform.TransformPoint(new Vector3(0f, ScreenRaise, SurfaceZ)));
            if (!plane.Raycast(ray, out distance))
                return false;
            point = ray.GetPoint(distance);
            return true;
        }

        /// <summary>
        /// Where on the canvas the player points: on the desktop, the mouse
        /// in mouse mode, else the middle of the view; the point where that
        /// ray meets the screen, which sits <see cref="ScreenRaise"/> above
        /// the tablet's middle. Null if it doesn't.
        /// </summary>
        private Vector2? Pointer()
        {
#if !PUBLIC_RELEASE
            if (PointerForTest.HasValue)
                return PointerForTest;
#endif
            var camera = PlayerManager.ActiveCamera;
            if (camera == null)
                return null;
            var ray = Cursor.visible ? camera.ScreenPointToRay(Input.mousePosition) : new Ray(camera.transform.position, camera.transform.forward);
            var plane = new Plane(transform.forward, transform.TransformPoint(new Vector3(0f, ScreenRaise, SurfaceZ)));
            if (!plane.Raycast(ray, out var distance))
                return null;
            var local = transform.InverseTransformPoint(ray.GetPoint(distance));
            return new Vector2(local.x / PixelSize + Width * 0.5f, Height * 0.5f - (local.y - ScreenRaise) / PixelSize);
        }

#if !PUBLIC_RELEASE
        // ---- For the self-test ---------------------------------------------

        /// <summary>The slot whose plate covers a canvas point; -1 if none.</summary>
        internal int SlotAt(Vector2 canvasPoint)
        {
            if (slots == null)
                return -1;
            for (var i = 0; i < SlotCount; i++)
                if (slots[i].activeSelf && slotPlaced[i].Contains(canvasPoint))
                    return i;
            return -1;
        }

        /// <summary>A canvas point on the drawn screen, in the world.</summary>
        internal Vector3 CanvasToWorldForTest(Vector2 canvasPoint) =>
            transform.TransformPoint(new Vector3((canvasPoint.x - Width * 0.5f) * PixelSize, (Height * 0.5f - canvasPoint.y) * PixelSize + ScreenRaise, SurfaceZ));

        /// <summary>The button object of a slot (the one with the game's control).</summary>
        internal GameObject SlotObjectForTest(int slot) => slots != null && slot >= 0 && slot < SlotCount ? slots[slot] : null;

        /// <summary>The slots in use now.</summary>
        internal int SlotsShown => slots == null ? 0 : slots.Count(s => s.activeSelf);

        /// <summary>Presses the plate in a slot through the game's own button: as a click does, it pushes the button in and lets it back.</summary>
        internal bool PressSlotForTest(int slot)
        {
            var control = slots != null && slot >= 0 && slot < SlotCount && slots[slot].activeInHierarchy ? slots[slot].GetComponent<ControlImplBase>() : null;
            if (control == null)
                return false;
            control.Use();
            return true;
        }

        /// <summary>Presses the map's own button through the game's button, as if the player pointed at a canvas point.</summary>
        internal bool PressMapForTest(Vector2 canvasPoint)
        {
            var control = mapButton != null && mapButton.activeInHierarchy ? mapButton.GetComponent<ControlImplBase>() : null;
            if (control == null)
                return false;
            PointerForTest = canvasPoint;
            try
            {
                control.Use();
            }
            finally
            {
                PointerForTest = null;
            }
            return true;
        }

        /// <summary>Whether the map's button is on now.</summary>
        internal bool MapButtonShown => mapButton != null && mapButton.activeSelf;

        /// <summary>Whether a slot's collider lies over the plate it was put on (after presses: a moved cab button snaps back).</summary>
        internal bool HitInPlace(int slot)
        {
            if (slots == null || slot < 0 || slot >= SlotCount)
                return false;
            var r = slotPlaced[slot];
            var want = transform.TransformPoint(new Vector3((r.center.x - Width * 0.5f) * PixelSize, (Height * 0.5f - r.center.y) * PixelSize + ScreenRaise,
                ButtonFront + ButtonDepth * 0.5f));
            return (slotHits[slot].transform.position - want).magnitude < 0.002f;
        }
#endif

        // ---- Layout (canvas pixels; AI Engineer's sizes: nothing pressable under 16 mm, text 7 mm or more) ----

        private static readonly Rect Heading = new Rect(12, 12, 236, 64);
        private static Rect TabArea(int i) => new Rect(260 + i * 200, 12, 188, 64);
        private static Rect SectionArea(int i) => new Rect(12 + i * 212, 88, 200, 56);
        private static Rect TileArea(int i) => new Rect(12 + i % Columns * 356, 156 + i / Columns * 206, 344, 196);
        private static Rect TilePicture(int i) { var t = TileArea(i); return new Rect(t.x + 16, t.y + 12, t.width - 32, 124); }
        private static Rect TileLabel(int i, bool full) { var t = TileArea(i); return full ? new Rect(t.x + 12, t.y + 8, t.width - 24, t.height - 16) : new Rect(t.x + 8, t.y + 140, t.width - 16, 48); }
        private static Rect AppCell(int i) => new Rect(12 + i % AppColumns * 264, 88 + i / AppColumns * 226, 264, 226);
        private static Rect AppBadge(int i) { var c = AppCell(i); return new Rect(c.x + 50, c.y + 4, 164, 164); }
        private static Rect AppShadow(int i) { var c = AppCell(i); return new Rect(c.x + 42, c.y + 10, 180, 180); }
        // A little wider than tall, so a long glyph (the locomotive) is not drawn smaller than the square ones.
        private static Rect AppPicture(int i) { var c = AppCell(i); return new Rect(c.x + 77, c.y + 38, 110, 96); }
        private static Rect AppLabel(int i) { var c = AppCell(i); return new Rect(c.x + 4, c.y + 172, 256, 44); }
        private static Rect AppPress(int i) { var c = AppCell(i); return new Rect(c.x + 24, c.y + 2, 216, 216); }
        private static readonly Rect Prev = new Rect(12, 776, 140, 64), Next = new Rect(928, 776, 140, 64);
        private static Rect ActionArea(int i) => new Rect(12 + i * 268, 776, 256, 64);

        private void Build()
        {
            if (canvas != null) return;
            TabletScreenRenderer.Size(Width, Height, PixelSize);
            var screen = new GameObject("[comms tablet screen]", typeof(RectTransform), typeof(Canvas));
            canvas = screen.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            screen.transform.SetParent(TabletScreenRenderer.Acquire(), false);
            screen.transform.localPosition = Vector3.forward;
            screen.transform.localScale = Vector3.one * PixelSize;
            screen.layer = TabletScreenRenderer.Layer;
            canvas.worldCamera = TabletScreenRenderer.Camera;
            canvas.enabled = false;
            var scaler = screen.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = TabletSurface.TextScale;
            scaler.referencePixelsPerUnit = ReferencePixelsPerUnit;
            root = screen.GetComponent<RectTransform>();
            root.sizeDelta = new Vector2(Width, Height);
            if (surface == null) BuildSurface();

            Picture("Background", new Rect(0, 0, Width, Height), TabletArt.White, TabletArt.Screen);
            Panel("Screen", new Rect(0, 0, Width, Height), TabletArt.ScreenBg);

            // The top bar.
            homeTitle = Words("Tablet", new Rect(28, 12, 1024, 64), 34, TextAnchor.MiddleLeft, TabletArt.Amber);
            homeTitle.fontStyle = FontStyle.Bold;
            homeTitle.text = "Comms Tablet";
            headingPlate = Panel("App", Heading, TabletArt.Here);
            heading = Words("App name", new Rect(Heading.x + 18, Heading.y, Heading.width - 24, Heading.height), 30, TextAnchor.MiddleLeft, TabletArt.Amber);
            heading.fontStyle = FontStyle.Bold;
            for (var i = 0; i < MaxTabs; i++)
            {
                tabPlates[i] = Panel("Tab", TabArea(i), TabletArt.PlateOff);
                tabNames[i] = Words("Tab name", TabArea(i), 27, TextAnchor.MiddleCenter, TabletArt.Text);
                var index = i;
                if (!controlsBuilt) tabButtons[i] = AddButton("tab " + i, TabArea(i), () => PressTab(index));
            }

            // The home screen.
            for (var i = 0; i < AppsPerPage; i++)
            {
                appShadows[i] = Picture("App shadow", AppShadow(i), TabletArt.Shadow, Color.white);
                appBadges[i] = Picture("App icon", AppBadge(i), TabletArt.Badge, Color.white);
                appArt[i] = Rect("App picture", AppPicture(i)).gameObject.AddComponent<Image>();
                appArt[i].preserveAspect = true;
                appArt[i].raycastTarget = false;
                // The game's white glyphs and the drawn ones alike, in brass.
                appArt[i].color = TabletArt.Amber;
                appGlyphs[i] = Picture("App symbol", AppPicture(i), null, TabletArt.Amber);
                appNames[i] = Words("App name", AppLabel(i), 30, TextAnchor.MiddleCenter, TabletArt.Text);
                var index = i;
                if (!controlsBuilt) appButtons[i] = AddButton("app " + i, AppPress(i), () => PressApp(index));
            }
            if (!controlsBuilt)
            {
                var size = HomeButtonRadius * 2f;
                homeButton = AddButton("home", new Vector3(0f, HomeButtonY, ButtonFront + ButtonDepth * 0.5f), new Vector3(size, size, ButtonDepth), PressHome);
            }

            // An app's page.
            for (var i = 0; i < MaxSections; i++)
            {
                sectionPlates[i] = Panel("Section", SectionArea(i), TabletArt.Chip);
                sectionNames[i] = Words("Section", SectionArea(i), 26, TextAnchor.MiddleCenter, TabletArt.Soft);
                var index = i;
                if (!controlsBuilt) sectionButtons[i] = AddButton("section " + i, SectionArea(i), () => PressSection(index));
            }
            for (var i = 0; i < PerPage; i++)
            {
                tilePlates[i] = Panel("Tile", TileArea(i), TabletArt.Card);
                tilePictures[i] = Rect("Tile picture", TilePicture(i)).gameObject.AddComponent<Image>();
                tilePictures[i].preserveAspect = true;
                tilePictures[i].raycastTarget = false;
                tileNames[i] = Words("Tile name", TileLabel(i, false), 27, TextAnchor.MiddleCenter, TabletArt.Text);
                var index = i;
                if (!controlsBuilt) tileButtons[i] = AddButton("tile " + i, TileArea(i), () => PressTile(index));
            }
            prevPlate = Panel("Previous", Prev, TabletArt.PlateOff);
            nextPlate = Panel("Next", Next, TabletArt.PlateOff);
            prevArrow = Picture("Previous arrow", new Rect(Prev.center.x - 20, Prev.y + 12, 40, 40), TabletArt.Arrow, TabletArt.Text);
            prevArrow.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
            nextArrow = Picture("Next arrow", new Rect(Next.center.x - 20, Next.y + 12, 40, 40), TabletArt.Arrow, TabletArt.Text);
            if (!controlsBuilt)
            {
                prevButton = AddButton("previous", Prev, () => Turn(-1));
                nextButton = AddButton("next", Next, () => Turn(1));
            }
            pageCount = Words("Page count", new Rect(164, 776, 752, 64), 26, TextAnchor.MiddleCenter, TabletArt.Soft);
            empty = Words("Empty", new Rect(60, 250, 960, 320), 32, TextAnchor.MiddleCenter, TabletArt.Soft);

            // A page without a list: the radio's display large, how to use it, and its buttons.
            readPanel = Panel("Readout", new Rect(12, 88, 1056, 476), TabletArt.Panel);
            readBar = Picture("Readout bar", new Rect(12, 92, 6, 468), TabletArt.White, TabletArt.Brass);
            readTitle = Words("Readout mode", new Rect(40, 104, 640, 40), 26, TextAnchor.MiddleLeft, TabletArt.Soft);
            readAction = Words("Readout action", new Rect(700, 104, 340, 40), 30, TextAnchor.MiddleRight, TabletArt.Amber);
            readAction.fontStyle = FontStyle.Bold;
            readContent = Words("Readout", new Rect(40, 152, 880, 396), 60, TextAnchor.MiddleLeft, TabletArt.Text);
            readContent.fontStyle = FontStyle.Bold;
            readArrow = Picture("Readout direction", new Rect(944, 280, 96, 96), TabletArt.Arrow, TabletArt.Amber);
            guide = Words("Guide", new Rect(24, 576, 1032, 188), 28, TextAnchor.MiddleCenter, TabletArt.Soft);
            for (var i = 0; i < MaxActions; i++)
            {
                actionPlates[i] = Panel("Action", ActionArea(i), TabletArt.PlateOff);
                actionNames[i] = Words("Action name", ActionArea(i), 27, TextAnchor.MiddleCenter, TabletArt.Text);
                var index = i;
                if (!controlsBuilt) actionButtons[i] = AddButton("action " + i, ActionArea(i), () => PressAction(index));
            }

            // The layer a page that draws itself (the map) draws on, under the status strip, as on
            // AI Engineer's tablet, whose last-order strip covers its map's bottom 60 pixels.
            layer = Rect("Map layer", new Rect(0, 0, Width, Height));
            layerSurface = new TabletSurface(layer, this);
            layerUsed = false;
            if (!controlsBuilt)
                BuildSlots();
            HideFrom(0);
            PlaceMapButton(UnityEngine.Rect.zero);

            // The status strip.
            statusPanel = Panel("Status", new Rect(12, 852, 1056, 62), TabletArt.Well);
            statusTitle = Words("Mode", new Rect(30, 854, 600, 26), 21, TextAnchor.MiddleLeft, TabletArt.Dim);
            statusContent = Words("Prompt", new Rect(30, 876, 720, 36), 28, TextAnchor.MiddleLeft, TabletArt.Text);
            statusAction = Words("Action", new Rect(820, 852, 230, 62), 28, TextAnchor.MiddleRight, TabletArt.Amber);
            statusAction.fontStyle = FontStyle.Bold;
            directionArrow = Picture("Direction", new Rect(766, 861, 44, 44), TabletArt.Arrow, TabletArt.Amber);
            controlsBuilt = true;
            refreshAt = 0f;
            ResetStatusCache();
            GetComponent<DV.CabControls.ItemBase>()?.ReCacheRenderers();
        }

        // ---- The inventory icon's page -------------------------------------

        /// <summary>The home screen's own apps, as <see cref="TabletRadio"/> lists them: name, the game's glyph, the drawn one.</summary>
        private static readonly (string Name, string Art, string Glyph)[] SampleApps =
        {
            ("Vehicles", "UI_TrainLoco", "vehicles"), ("Cargo", "UI_Item", "cargo"), ("Workshop", null, "workshop"),
            ("Switches", "UI_BookletSchematicMap", "switches"), ("Map", "UI_Map", "map"), ("Track Ahead", null, "ahead"),
            ("Tools", "UI_Mods", "tools"), ("Light", "UI_Light", "light"), ("Settings", null, "settings"),
        };

        /// <summary>The display's place on the shell: its quad's middle, in the tablet's frame.</summary>
        internal static Vector3 SurfacePosition => new Vector3(0f, ScreenRaise, SurfaceZ);

        /// <summary>The display's quad, facing out of the tablet's front (-z), at the screen's size.</summary>
        internal static Mesh SurfaceMesh()
        {
            var w = Width * PixelSize * 0.5f;
            var h = Height * PixelSize * 0.5f;
            var mesh = new Mesh { name = "CommsTablet_Screen" };
            mesh.vertices = new[] { new Vector3(-w, -h, 0), new Vector3(-w, h, 0), new Vector3(w, h, 0), new Vector3(w, -h, 0) };
            mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        /// <summary>
        /// The home screen with the tablet's own apps, drawn once with this
        /// screen's parts through the screens' camera, for the tablet's
        /// inventory icon (<see cref="TabletBody.RenderIcon"/>); mipmapped,
        /// not readable. Null if it could not be drawn (logged).
        /// </summary>
        internal static Texture2D SamplePage()
        {
            GameObject holder = null, screen = null;
            TabletScreen drawer = null;
            RenderTexture target = null;
            var acquired = false;
            var previous = RenderTexture.active;
            try
            {
                TabletScreenRenderer.Size(Width, Height, PixelSize);
                var host = TabletScreenRenderer.Acquire();
                acquired = true;
                // A screen of no tablet, never active, so it never starts: only its drawing parts, on a canvas of its own.
                holder = new GameObject("[comms tablet icon page]") { hideFlags = HideFlags.HideAndDontSave };
                holder.SetActive(false);
                drawer = holder.AddComponent<TabletScreen>();
                screen = new GameObject("[comms tablet icon screen]", typeof(RectTransform), typeof(Canvas));
                var canvas = screen.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                screen.transform.SetParent(host, false);
                screen.transform.localPosition = Vector3.forward;
                screen.transform.localScale = Vector3.one * PixelSize;
                screen.layer = TabletScreenRenderer.Layer;
                canvas.worldCamera = TabletScreenRenderer.Camera;
                var scaler = screen.AddComponent<CanvasScaler>();
                scaler.dynamicPixelsPerUnit = TabletSurface.TextScale;
                scaler.referencePixelsPerUnit = ReferencePixelsPerUnit;
                drawer.root = screen.GetComponent<RectTransform>();
                drawer.root.sizeDelta = new Vector2(Width, Height);
                drawer.DrawSampleHome();
                target = RenderTexture.GetTemporary(Width, Height, 0, RenderTextureFormat.ARGB32);
                var camera = TabletScreenRenderer.Camera;
                camera.targetTexture = target;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                camera.targetTexture = null;
                var page = new Texture2D(Width, Height, TextureFormat.RGBA32, true)
                {
                    name = "CommsTablet_IconPage", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear,
                };
                RenderTexture.active = target;
                page.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, true);
                page.Apply(true, true);
                return page;
            }
            catch (System.Exception error)
            {
                Main.Mod?.Logger.Warning("The comms tablet's icon could not draw its screen: " + error.Message);
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                if (target != null) RenderTexture.ReleaseTemporary(target);
                if (drawer != null)
                {
                    foreach (var sprite in drawer.panelSprites.Values)
                        if (sprite != null) Object.DestroyImmediate(sprite);
                    drawer.panelSprites.Clear();
                    drawer.root = null;
                }
                if (screen != null) Object.DestroyImmediate(screen);
                if (holder != null) Object.DestroyImmediate(holder);
                if (acquired) TabletScreenRenderer.Release();
            }
        }

        // The home screen as Build and Refresh lay it out, with the tablet's own apps, none lit.
        private void DrawSampleHome()
        {
            Picture("Background", new Rect(0, 0, Width, Height), TabletArt.White, TabletArt.Screen);
            Panel("Screen", new Rect(0, 0, Width, Height), TabletArt.ScreenBg);
            var title = Words("Tablet", new Rect(28, 12, 1024, 64), 34, TextAnchor.MiddleLeft, TabletArt.Amber);
            title.fontStyle = FontStyle.Bold;
            title.text = "Comms Tablet";
            for (var i = 0; i < SampleApps.Length && i < AppsPerPage; i++)
            {
                var (name, artName, glyph) = SampleApps[i];
                Picture("App shadow", AppShadow(i), TabletArt.Shadow, Color.white);
                Picture("App icon", AppBadge(i), TabletArt.Badge, Color.white);
                var art = TabletIcons.Find(artName);
                if (art != null)
                {
                    var image = Rect("App picture", AppPicture(i)).gameObject.AddComponent<Image>();
                    image.preserveAspect = true;
                    image.raycastTarget = false;
                    image.sprite = art;
                    image.color = TabletArt.Amber;
                }
                else
                    Picture("App symbol", AppPicture(i), TabletArt.Glyph(glyph), TabletArt.Amber);
                Words("App name", AppLabel(i), 30, TextAnchor.MiddleCenter, TabletArt.Text).text = name;
            }
            Panel("Status", new Rect(12, 852, 1056, 62), TabletArt.Well);
            Words("Mode", new Rect(30, 854, 600, 26), 21, TextAnchor.MiddleLeft, TabletArt.Dim).text = "COMMS TABLET";
            Words("Prompt", new Rect(30, 876, 720, 36), 28, TextAnchor.MiddleLeft, TabletArt.Text).text = "Choose an app";
        }

        private void BuildSurface()
        {
            var display = new GameObject("[comms tablet display surface]");
            display.transform.SetParent(transform, false);
            display.transform.localPosition = SurfacePosition;
            screenMesh = SurfaceMesh();
            display.AddComponent<MeshFilter>().sharedMesh = screenMesh;
            screenMaterial = new Material(Shader.Find("Unlit/Texture")) { name = "CommsTablet_Display" };
            surface = display.AddComponent<MeshRenderer>();
            surface.sharedMaterial = screenMaterial;
            surface.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            surface.receiveShadows = false;
            surface.enabled = false;
        }

        private void LateUpdate()
        {
            if (!visible || !dirty || canvas == null || surface == null || shell == null || !shell.isVisible)
                return;
            // The guest draws its region, then the canvas is rendered once.
            var view = shownView;
            if (view != null)
            {
                view.Dirty = false;
                view.Guest.Draw(view.Id);
                if (!view.Guest.Failed)
                    PlaceGuestButton(view);
            }
            if (texture == null)
            {
                texture = new RenderTexture(Width, Height, 0, RenderTextureFormat.ARGB32)
                {
                    name = "CommsTablet_Display", filterMode = FilterMode.Trilinear,
                    wrapMode = TextureWrapMode.Clamp, useMipMap = true, autoGenerateMips = false,
                };
                texture.Create();
                screenMaterial.mainTexture = texture;
            }
            // One shared, disabled camera renders this canvas when it changes.
            var camera = TabletScreenRenderer.Camera;
            var previous = RenderTexture.active;
            try
            {
                canvas.enabled = true;
                camera.targetTexture = texture;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                texture.GenerateMips();
                surface.enabled = true;
                dirty = false;
#if !PUBLIC_RELEASE
                Renders++;
#endif
            }
            finally
            {
                canvas.enabled = false;
                camera.targetTexture = null;
                RenderTexture.active = previous;
            }
        }

        private void TrackSelfDrawn(TabletPage page)
        {
            if (page is MapPage)
            {
                mapScreens.Add(this);
            }
            else if (mapScreens.Remove(this) && !AnyActive(mapScreens))
            {
                Map.BookletRenderer.Hide();
                Map.TabletMap.ReleaseTexture();
            }

            if (page is AheadPage)
            {
                aheadScreens.Add(this);
            }
            else if (aheadScreens.Remove(this) && !AnyActive(aheadScreens))
            {
                Map.TabletAhead.ReleaseTexture();
            }
        }

        private void UntrackSelfDrawn()
        {
            if (mapScreens.Remove(this) && !AnyActive(mapScreens))
            {
                Map.BookletRenderer.Hide();
                Map.TabletMap.ReleaseTexture();
            }
            if (aheadScreens.Remove(this) && !AnyActive(aheadScreens))
                Map.TabletAhead.ReleaseTexture();
        }

        private static bool AnyActive(HashSet<TabletScreen> screens)
        {
            screens.RemoveWhere(s => s == null);
            return screens.Count > 0;
        }

        private void ReleaseTexture()
        {
            UntrackSelfDrawn();
            if (surface != null) surface.enabled = false;
            if (screenMaterial != null) screenMaterial.mainTexture = null;
            if (texture == null) return;
            texture.Release();
            Object.Destroy(texture);
            texture = null;
            dirty = true;
            if (!AnyActive(mapScreens))
            {
                Map.BookletRenderer.Hide();
                Map.TabletMap.ReleaseTexture();
            }
            if (!AnyActive(aheadScreens))
                Map.TabletAhead.ReleaseTexture();
        }

        /// <summary>The map layer's pool of buttons, which its plates take in order, and the map's own button behind them.</summary>
        private void BuildSlots()
        {
            slots = new GameObject[SlotCount];
            slotHits = new BoxCollider[SlotCount];
            slotPlaced = new Rect[SlotCount];
            for (var i = 0; i < SlotCount; i++)
            {
                var index = i;
                slots[i] = AddSlot("slot " + i, () => SlotPressed(index), out slotHits[i]);
                slots[i].SetActive(false);
            }
            // The map's button covers only the map no plate is over (the page's PressArea), so it never takes a plate's press.
            mapButton = AddSlot("map", MapPressed, out mapHit);
            mapButton.SetActive(false);
        }

        /// <summary>
        /// A screen button that moves (AI Engineer's, c6e6207): the game's cab
        /// button on an object that never moves (the stock button puts itself
        /// back where it woke after every press), its collider on a child
        /// moved to the plate it is over.
        /// </summary>
        private GameObject AddSlot(string name, System.Action pressed, out BoxCollider hit)
        {
            var button = new GameObject("[comms tablet " + name + "]");
            button.SetActive(false);
            button.layer = TabletItem.ControlLayer;
            button.transform.SetParent(transform, false);
            button.transform.localPosition = new Vector3(0f, ScreenRaise, ButtonFront + ButtonDepth * 0.5f);
            var child = new GameObject("[hit]");
            child.layer = TabletItem.ControlLayer;
            child.transform.SetParent(button.transform, false);
            hit = child.AddComponent<BoxCollider>();
            // Hover casts include triggers; the buttons must never push controls or loose items.
            hit.isTrigger = true;
            Put(hit, new Rect(0, 0, 10, 10));
            var spec = button.AddComponent<DV.CabControls.Spec.Button>();
            TabletButton.Configure(spec, child);
            button.AddComponent<TabletButton>().Pressed = pressed;
            button.SetActive(true);
            return button;
        }

        /// <summary>Moves a slot's collider over a plate (canvas pixels); the button itself stays where it is.</summary>
        private static void Put(BoxCollider hit, Rect area)
        {
            hit.transform.localPosition = new Vector3((area.center.x - Width * 0.5f) * PixelSize, (Height * 0.5f - area.center.y) * PixelSize, 0f);
            hit.size = new Vector3(area.width * PixelSize, area.height * PixelSize, ButtonDepth);
        }

        private GameObject AddButton(string name, Rect area, System.Action pressed) =>
            AddButton(name, new Vector3((area.center.x - Width * 0.5f) * PixelSize, (Height * 0.5f - area.center.y) * PixelSize + ScreenRaise,
                ButtonFront + ButtonDepth * 0.5f), new Vector3(area.width * PixelSize, area.height * PixelSize, ButtonDepth), pressed);

        private GameObject AddButton(string name, Vector3 center, Vector3 size, System.Action pressed)
        {
            var button = new GameObject("[comms tablet " + name + "]");
            button.SetActive(false);
            button.layer = TabletItem.ControlLayer;
            button.transform.SetParent(transform, false);
            button.transform.localPosition = center;
            var collider = button.AddComponent<BoxCollider>();
            collider.size = size;
            // Hover casts include triggers; the buttons must never push controls or loose items.
            collider.isTrigger = true;
            var spec = button.AddComponent<DV.CabControls.Spec.Button>();
            TabletButton.Configure(spec, button);
            button.AddComponent<TabletButton>().Pressed = pressed;
            button.SetActive(true);
            return button;
        }

        private RectTransform Rect(string name, Rect area)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = TabletScreenRenderer.Layer;
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(root, false);
            Place(rect, area);
            return rect;
        }

        private static void Place(RectTransform rect, Rect area)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(area.x, -area.y);
            rect.sizeDelta = area.size;
        }

        private RawImage Picture(string name, Rect area, Texture texture, Color color)
        {
            var image = Rect(name, area).gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.color = color;
            image.raycastTarget = false;
            // Mirroring and turning go about the center, not the top-left anchor.
            image.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            image.rectTransform.anchoredPosition = new Vector2(area.center.x, -area.center.y);
            return image;
        }

        private Image Panel(string name, Rect area, Texture2D texture)
        {
            var image = Rect(name, area).gameObject.AddComponent<Image>();
            image.sprite = PanelSprite(texture);
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.raycastTarget = false;
            return image;
        }

        private Sprite PanelSprite(Texture2D texture)
        {
            if (panelSprites.TryGetValue(texture, out var sprite) && sprite != null)
                return sprite;
            // Ten-pixel slicing keeps the plates' corners and edges at their drawn size (AI Engineer's TabletSkin).
            sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), ReferencePixelsPerUnit, 0, SpriteMeshType.FullRect,
                new Vector4(10f, 10f, 10f, 10f), false);
            sprite.name = "CommsTablet_Panel";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            panelSprites[texture] = sprite;
            return sprite;
        }

        private Text Words(string name, Rect area, int size, TextAnchor alignment, Color color)
        {
            var text = Rect(name, area).gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.supportRichText = false;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = Mathf.Max(14, size * 2 / 3);
            text.resizeTextMaxSize = size;
            text.lineSpacing = 0.95f;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private void ReleaseCanvas()
        {
            // The guests' regions go with the canvas: their views are closed first.
            CloseViews();
            refreshedPage = null;
            if (canvas != null)
            {
                Object.Destroy(canvas.gameObject);
                canvas = null;
                TabletScreenRenderer.Release();
            }
            layerSurface = null;
            layer = null;
            layerUsed = false;
            foreach (var sprite in panelSprites.Values)
                if (sprite != null) Object.Destroy(sprite);
            panelSprites.Clear();
        }

        // A finished side view replaces its placeholder at the next refresh.
        private void ViewDrawn() => refreshAt = 0f;

        private void OnDestroy()
        {
            SideViews.Drawn -= ViewDrawn;
            if (radio != null)
            {
                radio.HeldChanged -= HeldChanged;
                radio.GuestCarChosen -= GuestCarChosen;
            }
            ReleaseTexture();
            ReleaseCanvas();
            if (screenMaterial != null) Object.Destroy(screenMaterial);
            if (screenMesh != null) Object.Destroy(screenMesh);
        }
    }

    /// <summary>The shared, disabled camera that draws the screens; Realistic Firing's <c>TabletScreenRenderer</c>.</summary>
    internal static class TabletScreenRenderer
    {
        // Unused by the stock game's cameras; only this camera sees the layer.
        // Realistic Firing's tablet uses it the same way, each mod with its own camera.
        internal const int Layer = 30;
        private static GameObject host;
        private static Camera camera;
        private static int users;
        private static float orthographicSize, aspect;
        internal static Camera Camera => camera;

        /// <summary>The screen's size, for the camera's frame.</summary>
        internal static void Size(int width, int height, float pixelSize)
        {
            orthographicSize = height * pixelSize * 0.5f;
            aspect = (float)width / height;
        }

        internal static Transform Acquire()
        {
            if (host == null)
            {
                host = new GameObject("[CommsTablet screen renderer]");
                Object.DontDestroyOnLoad(host);
                camera = host.AddComponent<Camera>();
                camera.enabled = false;
                // Away from Realistic Firing's screen camera, which also draws layer 30 at the origin.
                camera.transform.position = new Vector3(0f, -500f, -1f);
                camera.orthographic = true;
                camera.orthographicSize = orthographicSize;
                camera.aspect = aspect;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 2f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.cullingMask = 1 << Layer;
                camera.renderingPath = RenderingPath.Forward;
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.useOcclusionCulling = false;
            }
            users++;
            // The canvas plane sits one meter in front of the camera, independently of the physical tablet.
            return host.transform;
        }

        internal static void Release()
        {
            if (--users > 0) return;
            if (host != null) Object.Destroy(host);
            host = null;
            camera = null;
            users = 0;
        }
    }
}
