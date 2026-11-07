// A tour of rune-kit from C#: one page per family of widgets, each one live. The toolbar switches
// between the neutral theme and two palettes built from three seed colours, in light or dark.
using System;
using Rune;
using static Rune.Ui;

App.Run(new AppSpec<Gallery>("Rune Gallery", (1080f, 720f), () => new Gallery(), Shell.View)
{
    MinSize = (720f, 480f),
    Scenes =
    [
        ("buttons", gallery => gallery.Page = Page.Buttons),
        ("toggles", gallery => gallery.Page = Page.Toggles),
        ("inputs", gallery => gallery.Page = Page.Inputs),
        ("overlays", gallery => gallery.Page = Page.Overlays),
        ("feedback", gallery => gallery.Page = Page.Feedback),
        ("collections", gallery => gallery.Page = Page.Collections),
        ("dialog", gallery =>
        {
            gallery.Page = Page.Overlays;
            gallery.Open = "dialog";
        }),
        ("menu", gallery =>
        {
            gallery.Page = Page.Overlays;
            gallery.Open = "menu";
        }),
        ("select", gallery =>
        {
            gallery.Page = Page.Inputs;
            gallery.Open = "select";
        }),
        ("popover", gallery =>
        {
            gallery.Page = Page.Overlays;
            gallery.Open = "popover";
        }),
        ("tooltip", gallery =>
        {
            gallery.Page = Page.Overlays;
            gallery.Hovered = "tip-Standard";
        }),
        ("hover-card", gallery =>
        {
            gallery.Page = Page.Overlays;
            gallery.Hovered = "hover-card";
        }),
        ("toasts", gallery =>
        {
            gallery.Page = Page.Overlays;
            gallery.Notify("Saved", "Your changes are safe.");
            gallery.Notify("Build finished", "gallery linked in 41 s.");
        }),
        ("light", gallery => gallery.Dark = false),
        ("ember", gallery => gallery.Theme = 1),
        ("ocean-light", gallery =>
        {
            gallery.Theme = 2;
            gallery.Dark = false;
        }),
    ],
});

static class Shell
{
    static readonly (Page Page, string Title, string Blurb)[] Pages =
    [
        (Page.Buttons, "Buttons", "Buttons, icon buttons, badges and keycaps."),
        (Page.Toggles, "Toggles", "Switches, checkboxes, radio groups and segmented controls."),
        (Page.Inputs, "Inputs", "Text fields, text areas, selects, a combobox and a shortcut recorder."),
        (Page.Overlays, "Overlays", "Dialogs, popovers, menus, tooltips, hover cards and toasts."),
        (Page.Feedback, "Feedback", "Progress, spinners, rolling numbers and cards."),
        (Page.Collections, "Collections", "Tabs, lists, accordions, a sortable table and a file tree."),
    ];

    static readonly string[] ThemeNames = ["Neutral", "Ember", "Ocean"];

    // Each kit is built once and kept: building one resolves every colour and text style.
    static readonly Kit?[] Kits = new Kit?[6];

    static Kit KitFor(int theme, bool dark)
    {
        int slot = theme * 2 + (dark ? 1 : 0);
        return Kits[slot] ??= theme switch
        {
            1 => Kit.FromPalette(dark ? Seed("#16110e", "#f2e8e1", "#f0743e") : Seed("#fbf6f2", "#2b1d14", "#d4561c")),
            2 => Kit.FromPalette(dark ? Seed("#0b1418", "#e3eef2", "#2bb3c0") : Seed("#f4f9fb", "#10262e", "#0b8796")),
            _ => Kit.Neutral(dark),
        };
    }

    static PaletteSeed Seed(string background, string foreground, string accent) =>
        new(Hex(background), Hex(foreground), Hex(accent));

    static Color Hex(string hex) => ColorModule.Hex(hex) ?? Color.Black;

    public static El View(Gallery gallery, Cx context)
    {
        bool dark = gallery.Dark ?? context.SystemDark();
        var kit = KitFor(gallery.Theme, dark);
        var colors = kit.C;
        var layer = VecEl.Create();

        var (_, title, blurb) = Array.Find(Pages, entry => entry.Page == gallery.Page);
        var body = gallery.Page switch
        {
            Page.Buttons => Widgets.Buttons(gallery, kit),
            Page.Toggles => Widgets.Toggles(gallery, kit),
            Page.Inputs => Widgets.Inputs(gallery, kit, layer),
            Page.Overlays => Widgets.Overlays(gallery, kit, layer),
            Page.Feedback => Widgets.Feedback(gallery, kit),
            _ => Widgets.Collections(gallery, kit),
        };

        var content = Col().Key($"page-{gallery.Page}").WFull().Grow(1f).MinH(0f).Px(28f).Py(24f).Gap(28f)
            .Scroll(new ScrollSpec(false))
            .Child(Text(blurb).Key("blurb").Font(kit.Ty.Body).Color(colors.TextMuted))
            .Child(body);

        var status = Row().Key("status").WFull().H(28f).Px(16f).CenterY().Gap(8f)
            .Fill(colors.Layer01)
            .Child(Text(gallery.Status).Key("text").Font(kit.Ty.Small).Color(colors.TextMuted).Truncate());

        var main = Col().Key("main").WFull().HFull().Fill(colors.Page)
            .Child(kit.ScrollBar(content))
            .Child(Divider.Horizontal().Build(kit).Key("rule"))
            .Child(status);

        var rows = new El[Pages.Length];
        for (int i = 0; i < Pages.Length; i++)
        {
            var page = Pages[i].Page;
            rows[i] = SidebarRow.New(Pages[i].Title)
                .Selected(gallery.Page == page)
                .OnSelect((Gallery g, Tx _) =>
                {
                    g.Page = page;
                    g.Open = null;
                })
                .Build(kit).Key(Pages[i].Title);
        }
        var sidebar = Sidebar.New().Label("Pages")
            .Header(Col().Key("brand").Gap(2f)
                .Child(Text("Rune Gallery").Key("name").Font(kit.Ty.UiMedium).Color(colors.TextStrong))
                .Child(Text("rune-kit from C#").Key("tagline").Font(kit.Ty.Small).Color(colors.TextMuted)))
            .Section(SidebarSection.New("Widgets").Key("widgets").Rows(rows))
            .Build(kit);

        var toolbar = Toolbar.New().Title(title)
            .Trailing(Row().Key("appearance").Gap(12f).CenterY()
                .Child(SegmentedControl.New(ThemeNames, (nuint)gallery.Theme).Label("Theme").Fit()
                    .OnPick((Gallery g, nuint index, Tx _) => g.Theme = (int)index).Build(kit).Key("theme"))
                .Child(Switch.New(dark).Label("Dark").ControlSize(ControlSize.Small)
                    .OnToggle((Gallery g) => g.Dark = !dark).Build(kit).Key("dark")));

        var window = SplitView.New(main)
            .Sidebar(gallery.Pane, static (Gallery g) => g.Pane, sidebar)
            .Toolbar(toolbar)
            .Build(kit);

        var overlays = layer.Take();
        layer.Free();
        if (gallery.Toasts.Count > 0)
        {
            var region = ToastRegion.New();
            foreach (var toast in gallery.Toasts)
            {
                region = region.Toast(Widgets.Toast(kit, toast));
            }
            overlays = [.. overlays, region.Build(kit).Key("toasts")];
        }
        return KitModule.Root(window, overlays);
    }
}
