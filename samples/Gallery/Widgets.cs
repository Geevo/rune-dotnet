using System;
using System.Linq;
using Rune;
using static Rune.Ui;

/// <summary>The pages. Each section is one widget family, built straight from rune-kit.</summary>
static class Widgets
{
    // Buttons

    public static El Buttons(Gallery gallery, Kit kit)
    {
        var variants = Wrap(8f);
        foreach (var variant in Enum.GetValues<ButtonVariant>())
        {
            var name = variant.ToString();
            variants = variants.Child(Button.New(name).Variant(variant)
                .OnPress((Gallery g) => g.Say($"Pressed the {name} button."))
                .Build(kit).Key(name));
        }

        var sizes = Wrap(8f);
        foreach (var size in Enum.GetValues<ControlSize>())
        {
            sizes = sizes.Child(Button.New(size.ToString()).ControlSize(size).Variant(ButtonVariant.Outline).Build(kit).Key(size.ToString()));
        }

        var states = Wrap(8f);
        foreach (var state in Enum.GetValues<State>())
        {
            states = states.Child(Button.New(state.ToString()).State(state).Build(kit).Key(state.ToString()));
        }

        var withIcons = Wrap(8f)
            .Child(Button.New("Search").Icon(kit.Glyph.Search).Variant(ButtonVariant.Submit)
                .Help("Buttons explain themselves on hover").Build(kit).Key("search"))
            .Child(Button.New("Copy link").Icon(kit.Glyph.Copy)
                .OnPressTx((Gallery g, Tx tx) =>
                {
                    tx.Copy("https://github.com/Geevo/rune-dotnet");
                    g.Say("Copied the repository link.");
                })
                .Build(kit).Key("copy"))
            .Child(Button.New("Locked").Icon(kit.Glyph.Lock).Disabled(true).Build(kit).Key("locked"));

        var iconButtons = Wrap(8f);
        var glyphs = new[] { ("search", kit.Glyph.Search), ("copy", kit.Glyph.Copy), ("lock", kit.Glyph.Lock), ("eye", kit.Glyph.Eye), ("close", kit.Glyph.Close) };
        var iconVariants = Enum.GetValues<IconButtonVariant>();
        for (int i = 0; i < iconVariants.Length; i++)
        {
            var (label, glyph) = glyphs[i % glyphs.Length];
            var variant = iconVariants[i];
            iconButtons = iconButtons.Child(IconButton.New(glyph).Variant(variant).Label($"{label} ({variant})").Help($"{variant} icon button")
                .OnPress((Gallery g) => g.Say($"Pressed the {variant} icon button."))
                .Build(kit).Key(variant.ToString()));
        }

        var badges = Wrap(8f)
            .Child(Badge.New("Neutral").Build(kit).Key("neutral"))
            .Child(Badge.New("Accent").Variant(BadgeVariant.Accent).Build(kit).Key("accent"))
            .Child(Badge.New("Subtle").Variant(BadgeVariant.Subtle).Build(kit).Key("subtle"))
            .Child(Badge.New("Standard").Standard().Build(kit).Key("standard"))
            .Child(Badge.New("High contrast").HighContrast().Build(kit).Key("contrast"));

        var keys = Wrap(16f)
            .Child(Inline(kit, "Command palette", RuneKitKeybind.New(["⌘", "K"]).Build(kit)).Key("palette"))
            .Child(Inline(kit, "Save all", RuneKitKeybind.New(["⌘", "⌥", "S"]).Build(kit)).Key("save"))
            .Child(Inline(kit, "Ghost", RuneKitKeybind.New(["Esc"]).Variant(KeybindVariant.Ghost).Build(kit)).Key("ghost"));

        return Stack(
            Section(kit, "variants", "Variants", variants),
            Section(kit, "sizes", "Control sizes", sizes),
            Section(kit, "states", "States", states, "Forced states, as a screenshot of each one would show."),
            Section(kit, "icons", "With icons", withIcons),
            Section(kit, "icon-buttons", "Icon buttons", iconButtons),
            Section(kit, "badges", "Badges", badges),
            Section(kit, "keybinds", "Keycaps", keys));
    }

    // Toggles

    static readonly string[] FeatureNames = ["Crash reports", "Usage analytics", "Beta updates"];

    public static El Toggles(Gallery gallery, Kit kit)
    {
        var switches = Col().Gap(14f)
            .Child(Switch.New(gallery.Is("notify")).Label("Notifications").Description("Banners when a long task finishes.")
                .OnToggle((Gallery g) => g.Flip("notify")).Build(kit).Key("notify"))
            .Child(Switch.New(gallery.Is("sync")).Label("Sync settings").OnToggle((Gallery g) => g.Flip("sync")).Build(kit).Key("sync"))
            .Child(Switch.New(gallery.Is("web")).Label("Web style").Web().OnToggle((Gallery g) => g.Flip("web")).Build(kit).Key("web"))
            .Child(Switch.New(true).Label("Managed by your organisation").Disabled(true).Build(kit).Key("managed"));

        var switchSizes = Wrap(16f);
        foreach (var size in new[] { ControlSize.Mini, ControlSize.Small, ControlSize.Regular, ControlSize.Large })
        {
            switchSizes = switchSizes.Child(Switch.New(gallery.Is("sizes")).Label(size.ToString()).ControlSize(size)
                .OnToggle((Gallery g) => g.Flip("sizes")).Build(kit).Key(size.ToString()));
        }

        // A parent checkbox that shows all, none or some of its children.
        int on = gallery.Features.Count(feature => feature);
        var checkboxes = Col().Gap(10f)
            .Child(Checkbox.New(on == FeatureNames.Length).Indeterminate(on > 0 && on < FeatureNames.Length).Label("Share diagnostics")
                .OnToggle((Gallery g) =>
                {
                    bool all = !g.Features.All(feature => feature);
                    Array.Fill(g.Features, all);
                })
                .Build(kit).Key("all"));
        for (int i = 0; i < FeatureNames.Length; i++)
        {
            int index = i;
            checkboxes = checkboxes.Child(Row().Key($"feature-{i}").Pl(24f)
                .Child(Checkbox.New(gallery.Features[i]).Label(FeatureNames[i])
                    .OnToggle((Gallery g) => g.Features[index] = !g.Features[index]).Build(kit)));
        }
        checkboxes = checkboxes
            .Child(Checkbox.New(gallery.Is("terms")).Label("I accept the terms").Description("A description sits under the label.")
                .OnToggle((Gallery g) => g.Flip("terms")).Build(kit).Key("terms"))
            .Child(Checkbox.New(false).Label("Unavailable").Disabled(true).Build(kit).Key("disabled"));

        (string Id, string Label, string Detail)[] speeds =
        [
            ("standard", "Standard", "Four to six working days, free."),
            ("express", "Express", "Next working day, £4.99."),
            ("pickup", "Pick up in store", "Not available near you."),
        ];
        var radio = RadioGroup.New().Label("Delivery").Description("Choose how the order reaches you.");
        foreach (var (id, label, detail) in speeds)
        {
            radio = radio.Item(RadioItem.New(label, gallery.Shipping == id).Description(detail).Disabled(id == "pickup")
                .OnSelect((Gallery g) =>
                {
                    g.Shipping = id;
                    g.Say($"Delivery: {label}.");
                }));
        }

        string[] views = ["List", "Grid", "Columns"];
        var segmented = Col().Gap(12f)
            .Child(SegmentedControl.New(views, (nuint)gallery.View).Label("View").Fit()
                .OnPick((Gallery g, nuint index, Tx _) => g.View = (int)index).Build(kit).Key("view"))
            .Child(SegmentedControl.New(["Day", "Week", "Month", "Year"], 1).Fit().Build(kit).Key("fit"))
            .Child(Text($"Showing the {views[gallery.View].ToLowerInvariant()} view.").Key("current").Font(kit.Ty.Small).Color(kit.C.TextMuted));

        return Stack(
            Columns(Section(kit, "switches", "Switches", switches), Section(kit, "switch-sizes", "Switch sizes", switchSizes)),
            Columns(Section(kit, "checkboxes", "Checkboxes", checkboxes, "The first one is indeterminate while only some are ticked."),
                Section(kit, "radio", "Radio group", radio.Build(kit))),
            Section(kit, "segmented", "Segmented controls", segmented));
    }

    // Inputs

    public static El Inputs(Gallery gallery, Kit kit, VecEl layer)
    {
        bool emailBad = gallery.Email.Text.Length > 0 && !gallery.Email.Text.Contains('@') || gallery.Email.Text.EndsWith('@');
        var fields = Col().Gap(16f).W(360f)
            .Child(Labelled(kit, "name", "Name", Rune.TextInput.New(gallery.Name).Label("Name").OnEdit((Gallery g) => g.Name).Build(kit)))
            .Child(Labelled(kit, "email", "Email", Rune.TextInput.New(gallery.Email).Label("Email").Placeholder("you@example.com").Invalid(emailBad)
                .OnEdit((Gallery g) => g.Email).Build(kit), after: emailBad ? "Add the domain, as in ada@example.com." : null))
            .Child(Labelled(kit, "password", "Password", Rune.TextInput.New(gallery.Password).Label("Password").Secure().Revealed(gallery.Reveal)
                .Trailing(IconButton.New(gallery.Reveal ? kit.Glyph.EyeOff : kit.Glyph.Eye).Variant(IconButtonVariant.Ghost)
                    .ControlSize(ControlSize.Small).Label(gallery.Reveal ? "Hide password" : "Show password")
                    .OnPress((Gallery g) => g.Reveal = !g.Reveal).Build(kit).Key("reveal"))
                .OnEdit((Gallery g) => g.Password).Build(kit)))
            .Child(Labelled(kit, "search", "Search", Rune.TextInput.New(gallery.Search).Label("Search").Placeholder("Search everything")
                .LeadingIcon(kit.Glyph.Search).ClearButton()
                .OnEdit((Gallery g) => g.Search)
                .OnSubmit((Gallery g, Tx _) => g.Say($"Searched for \"{g.Search.Text}\"."))
                .Build(kit), after: "Enter searches; the × clears it."))
            .Child(Labelled(kit, "share", "Share link", Rune.TextInput.New(gallery.Share).Label("Share link").CopyButton()
                .OnEdit((Gallery g) => g.Share).Build(kit)));

        var more = Col().Gap(16f).W(360f)
            .Child(Labelled(kit, "repo", "Repository", Rune.TextInput.New(gallery.Handle).Label("Repository").OnEdit((Gallery g) => g.Handle).Build(kit),
                before: "The name after github.com/Geevo/.", after: "Lowercase letters and hyphens."))
            .Child(Labelled(kit, "notes", "Notes", TextArea.New(gallery.Notes).Label("Notes").AutoGrow(3, 8).OnEdit((Gallery g) => g.Notes).Build(kit),
                after: $"{gallery.Notes.Text.Length} characters"));

        string[] fruit = ["Apple", "Banana", "Cherry", "Mango", "Passion fruit"];
        var pickers = Col().Gap(16f).W(360f)
            .Child(Labelled(kit, "fruit", "Fruit", Select.New("fruit", fruit).Label("Fruit").Appearance(SelectAppearance.Field).Width(240f).Current(gallery.Fruit)
                .Open(gallery.Open == "select")
                .OnToggle((Gallery g) => g.Toggle("select")).OnClose((Gallery g) => g.Close())
                .OnChoose((Gallery g, string choice, Tx _) =>
                {
                    g.Fruit = choice;
                    g.Close();
                    g.Say($"Picked {choice}.");
                })
                .Build(kit, layer)))
            .Child(Labelled(kit, "region", "Region", Select.Grouped("region", [("Europe", "London"), ("Europe", "Lisbon"), ("Americas", "Toronto"), ("Asia", "Tokyo")])
                .Label("Region").Width(240f).Placeholder("Choose a region").Current(gallery.Region)
                .Open(gallery.Open == "region")
                .OnToggle((Gallery g) => g.Toggle("region")).OnClose((Gallery g) => g.Close())
                .OnChoose((Gallery g, string choice, Tx _) =>
                {
                    g.Region = choice;
                    g.Close();
                })
                .Build(kit, layer), after: "Grouped by continent, in the inline appearance."))
            .Child(Labelled(kit, "disabled-select", "Disabled", Select.New("disabled", ["Locked"]).Label("Disabled").Appearance(SelectAppearance.Field).Width(240f).Current("Locked")
                .Disabled(true).Build(kit, layer)));

        (string Id, string Label, string Detail)[] languages =
        [
            ("csharp", "C#", ".NET"), ("fsharp", "F#", ".NET"), ("rust", "Rust", "Systems"), ("go", "Go", "Systems"),
            ("swift", "Swift", "Apple"), ("kotlin", "Kotlin", "JVM"), ("typescript", "TypeScript", "Web"), ("zig", "Zig", "Systems"),
        ];
        var options = languages.Select(language =>
        {
            var option = ComboboxOption.New(language.Id, language.Label);
            option.Detail = language.Detail;
            return option;
        }).ToArray();
        var combobox = Combobox.New("language", gallery.Languages, options, static (Gallery g) => g.Languages)
            .Label("Language").Placeholder("Pick a language").SearchPlaceholder("Search languages").EmptyMessage("No language matches")
            .Width(240f).Current(gallery.Language).AnimateFiltering(true)
            .OnChoose((Gallery g, string id, Tx _) =>
            {
                g.Language = id;
                g.Languages.Close();
                g.Say($"Language: {id}.");
            })
            .Build(kit, layer);

        var recorder = ShortcutRecorder.New("shortcut", gallery.Recorder, static (Gallery g) => g.Recorder)
            .Label("Toggle the sidebar").Width(240f).Build(kit);

        return Stack(
            Columns(Section(kit, "text", "Text inputs", fields, "Each one sits in a field with its label and help text."),
                Section(kit, "more", "Fields and text areas", more, "The text area grows from three to eight lines.")),
            Columns(Section(kit, "selects", "Selects", pickers),
                Section(kit, "search", "Combobox and shortcut recorder", Col().Gap(16f).W(360f)
                    .Child(Labelled(kit, "language", "Language", combobox, after: "Type to filter."))
                    .Child(Labelled(kit, "recorder", "Toggle the sidebar", recorder, after: "Click, then press a key combination.")))));
    }

    // Overlays

    public static El Overlays(Gallery gallery, Kit kit, VecEl layer)
    {
        Dialog.New("dialog", "Delete this branch?")
            .Description("This can't be undone.")
            .Width(420f).Fit().Open(gallery.Open == "dialog").ClickOutside(true)
            .OnClose((Gallery g) => g.Close())
            .Child(Col().Key("content").WFull().Px(16f).Pb(16f).Gap(16f)
                .Child(KitModule.BodyText(kit, "feature/gallery and its 14 commits go for good. Anyone who has it checked out keeps their copy.").WFull().Wrap())
                .Child(Row().Key("actions").WFull().Gap(8f).JustifyEnd()
                .Child(Button.New("Cancel").Variant(ButtonVariant.Ghost).OnPress((Gallery g) => g.Close()).Build(kit).Key("cancel"))
                .Child(Button.New("Delete branch").Variant(ButtonVariant.Danger)
                    .OnPress((Gallery g) =>
                    {
                        g.Close();
                        g.Notify("Branch deleted", "feature/gallery is gone.");
                    }).Build(kit).Key("delete"))))
            .Build(kit, layer);
        var dialog = Button.New("Delete branch…").Variant(ButtonVariant.Danger).OnPress((Gallery g) => g.Toggle("dialog")).Build(kit).Key("open-dialog");

        var popover = Popover.New("popover")
            .Trigger(Button.New("Share…").OnPress((Gallery g) => g.Toggle("popover")).Build(kit))
            .Title("Share this page").Description("Anyone with the link can view it.")
            .Child(Rune.TextInput.New(gallery.Share).CopyButton().Width(260f).OnEdit((Gallery g) => g.Share).Build(kit).Key("link"))
            .Open(gallery.Open == "popover").Placement(Placement.BottomStart)
            .OnClose((Gallery g) => g.Close())
            .Build(kit, layer);

        var menu = Menu.New("menu")
            .Trigger(Button.New("File").Variant(ButtonVariant.Outline).OnPress((Gallery g) => g.Toggle("menu")).Build(kit))
            .Open(gallery.Open == "menu").Placement(Placement.BottomStart).MinWidth(220f)
            .OnClose((Gallery g) => g.Close())
            .Item(MenuAction("New file", "⌘N", "Created a file."))
            .Item(MenuAction("Open…", "⌘O", "Opened a file."))
            .Item(RuneKitMenuItem.New("Share").Submenu(
                [
                    MenuEntry.Item(RuneKitMenuItem.New("Copy link").OnSelect((Gallery g) =>
                    {
                        g.Close();
                        g.Say("Copied a link.");
                    })),
                    MenuEntry.Item(RuneKitMenuItem.New("Email…").OnSelect((Gallery g) =>
                    {
                        g.Close();
                        g.Say("Emailed it.");
                    })),
                ], gallery.Is("share-menu"))
                .OnSelect((Gallery g) => g.Flip("share-menu")))
            .Separator()
            .GroupLabel("View")
            .Item(RuneKitMenuItem.New("Show hidden files").Checked(gallery.Is("hidden")).OnSelect((Gallery g) => g.Flip("hidden")))
            .Item(RuneKitMenuItem.New("Word wrap").Checked(gallery.Is("wrap")).Description("Wrap long lines").OnSelect((Gallery g) => g.Flip("wrap")))
            .Separator()
            .Item(RuneKitMenuItem.New("Delete").Danger(true).Shortcut("⌫").OnSelect((Gallery g) =>
            {
                g.Close();
                g.Say("Deleted it.");
            }))
            .Build(kit, layer);

        var tooltips = Wrap(12f);
        foreach (var appearance in Enum.GetValues<TooltipAppearance>())
        {
            var key = $"tip-{appearance}";
            var trigger = Button.New(appearance.ToString()).Variant(ButtonVariant.Outline).Build(kit)
                .OnHoverChange((Gallery g, bool inside, Tx _) => Hover(g, key, inside));
            var tip = Tooltip.New(key).Trigger(trigger).Appearance(appearance).Open(gallery.Hovered == key).Placement(Placement.Top);
            var words = $"A {appearance.ToString().ToLowerInvariant()} tooltip";
            // Standard's wrapping text collapses to nothing (rune-kit draws an empty pill), so
            // it gets a plain text child in the tooltip's own type and ink instead.
            tip = appearance == TooltipAppearance.Standard
                ? tip.Child(Text(words).Font(tip.Font(kit)).Color(tip.Ink(kit)))
                : tip.Text(kit, words);
            tooltips = tooltips.Child(tip.Build(kit, layer));
        }

        var profile = Col().Key("profile").W(240f).Gap(6f)
            .Child(Text("Ada Lovelace").Key("name").Font(kit.Ty.UiMedium).Color(kit.C.TextStrong))
            .Child(Text("Wrote the first program, for an engine that was never built.").Key("bio").WFull().Wrap().Font(kit.Ty.Small).Color(kit.C.TextMuted))
            .Child(Row().Key("badges").Gap(6f).Child(Badge.New("Maintainer").Variant(BadgeVariant.Accent).Build(kit)).Child(Badge.New("1842").Build(kit)));
        var hoverCard = HoverCard.New("hover-card")
            .Trigger(Text("@ada").Key("handle").Font(kit.Ty.UiMedium).Color(kit.C.TextAccent)
                .OnHoverChange((Gallery g, bool inside, Tx _) => Hover(g, "hover-card", inside)))
            .Child(profile).Open(gallery.Hovered == "hover-card").Placement(Placement.Bottom)
            .Build(kit, layer);

        var toasts = Wrap(8f)
            .Child(Button.New("Show a toast").OnPress((Gallery g) => g.Notify("Saved", "Your changes are safe.")).Build(kit).Key("toast"))
            .Child(Button.New("Another").Variant(ButtonVariant.Ghost).OnPress((Gallery g) => g.Notify("Build finished", "gallery linked in 41 s.")).Build(kit).Key("toast-2"));

        return Stack(
            Columns(Section(kit, "dialog", "Dialog", dialog, "Modal, with a scrim. Escape or a click outside closes it."),
                Section(kit, "popover", "Popover", popover)),
            Columns(Section(kit, "menu", "Menu", menu, "Checked items, a submenu, a group label and a destructive item."),
                Section(kit, "hover-card", "Hover card", hoverCard, "Hover the handle.")),
            Columns(Section(kit, "tooltips", "Tooltips", tooltips, "Each appearance, on hover."),
                Section(kit, "toasts", "Toasts", toasts, "Up to three stack in the corner.")));
    }

    static RuneKitMenuItem MenuAction(string label, string shortcut, string said) =>
        RuneKitMenuItem.New(label).Shortcut(shortcut).OnSelect((Gallery g) =>
        {
            g.Close();
            g.Say(said);
        });

    static void Hover(Gallery gallery, string key, bool inside)
    {
        if (inside)
        {
            gallery.Hovered = key;
        }
        else if (gallery.Hovered == key)
        {
            gallery.Hovered = null;
        }
    }

    public static El Toast(Kit kit, ToastNote note) =>
        Rune.Toast.New($"toast-{note.Id}", note.Title).Description(note.Description).Icon(kit, kit.Glyph.Check).Closable()
            .Action(kit, "Undo", false, (Gallery g, Tx _) =>
            {
                g.Toasts.RemoveAll(toast => toast.Id == note.Id);
                g.Say($"Undid \"{note.Title}\".");
            })
            .OnClose((Gallery g) => g.Toasts.RemoveAll(toast => toast.Id == note.Id))
            .Build(kit);

    // Feedback

    public static El Feedback(Gallery gallery, Kit kit)
    {
        var progress = Col().Gap(16f).W(360f)
            .Child(Progress.New(gallery.Progress).Label("Uploading").ShowValue().Build(kit).Key("upload"))
            .Child(Progress.New(null).Label("Waiting for the server").Build(kit).Key("waiting"))
            .Child(Row().Key("steps").Gap(8f)
                .Child(Button.New("−10%").ControlSize(ControlSize.Small).Variant(ButtonVariant.Outline)
                    .OnPress((Gallery g) => g.Progress = MathF.Max(0f, g.Progress - 0.1f)).Build(kit).Key("less"))
                .Child(Button.New("+10%").ControlSize(ControlSize.Small).Variant(ButtonVariant.Outline)
                    .OnPress((Gallery g) => g.Progress = MathF.Min(1f, g.Progress + 0.1f)).Build(kit).Key("more")));

        var circles = Wrap(20f)
            .Child(ProgressCircle.New(gallery.Progress).Size(20f).Label("Small").Build(kit).Key("small"))
            .Child(ProgressCircle.New(gallery.Progress).Size(32f).Label("Medium").Build(kit).Key("medium"))
            .Child(ProgressCircle.New(gallery.Progress).Size(48f).Label("Large").Build(kit).Key("large"));

        var spinners = Wrap(24f)
            .Child(Spinner.New().Size(12f).Build(kit).Key("s12"))
            .Child(Spinner.New().Size(16f).Build(kit).Key("s16"))
            .Child(Spinner.New().Size(24f).Color(kit.C.IconAccent).Build(kit).Key("s24"))
            .Child(Loader.New().Build(kit).Key("loader"))
            .Child(Loader.New().Size(24f).Build(kit).Key("loader-24"));

        var rolling = Row().Key("stars").Gap(12f).CenterY()
            .Child(RollingText.New("stars", gallery.Stars.ToString("N0")).Value(gallery.Stars).Font(kit.Ty.BodyMedium).Color(kit.C.TextStrong).Build(kit).Key("count"))
            .Child(Text("stars").Key("unit").Font(kit.Ty.Ui).Color(kit.C.TextMuted))
            .Child(Button.New("Star").Icon(kit.Glyph.Check).ControlSize(ControlSize.Small).OnPress((Gallery g) => g.Stars += 1).Build(kit).Key("star"))
            .Child(Button.New("Go viral").ControlSize(ControlSize.Small).Variant(ButtonVariant.Ghost).OnPress((Gallery g) => g.Stars += 997).Build(kit).Key("viral"));

        var colors = kit.C;
        var cards = Wrap(12f)
            .Child(Card(kit, "plain", null, null, "Card", "A titled surface. Without an icon, a dash holds its place."))
            .Child(Card(kit, "success", colors.Success.Icon, kit.Glyph.Check, "Deployed", "v0.1.0 is live on all three platforms."))
            .Child(Card(kit, "warning", colors.Warning.Icon, kit.Glyph.Eye, "Low disk space", "Two gigabytes left on the build volume."))
            .Child(Card(kit, "danger", colors.Danger.Icon, kit.Glyph.CircleX, "Link failed", "rune_capi.lib wasn't found."))
            .Child(Card(kit, "info", colors.Info.Icon, kit.Glyph.Search, "Heads up", "A new Rune commit is available."));

        return Stack(
            Columns(Section(kit, "progress", "Progress bars", progress), Section(kit, "circles", "Progress circles", circles)),
            Columns(Section(kit, "spinners", "Spinners and loaders", spinners), Section(kit, "rolling", "Rolling text", rolling, "Digits roll to each new value.")),
            Section(kit, "cards", "Cards", cards));
    }

    static El Card(Kit kit, string key, Color? accent, Icon? glyph, string title, string description)
    {
        var card = Rune.Card.New().Title(title, glyph).Description(description);
        if (accent is { } color)
        {
            card = card.Accent(color);
        }
        return El().Key(key).W(300f).Child(card.Build(kit));
    }

    // Collections

    public static El Collections(Gallery gallery, Kit kit)
    {
        var tabs = gallery.Tabs.Select(name => TabItem.New(name, name).Icon(KitModule.FileIcon(name)).Closable(gallery.Tabs.Count > 1)).ToArray();
        var tabStrip = Col().Key("tabs").W(640f).Gap(12f)
            .Child(TabStrip.New("tabs", TabOrientation.Horizontal).Tabs(tabs).Active(gallery.Tab).ViewportWidth(640f).Label("Open files")
                .OnSelect((Gallery g, string id, Tx _) => g.Tab = id)
                .OnClose((Gallery g, string id, Tx _) =>
                {
                    int at = g.Tabs.IndexOf(id);
                    g.Tabs.Remove(id);
                    if (g.Tab == id)
                    {
                        g.Tab = g.Tabs[Math.Min(at, g.Tabs.Count - 1)];
                    }
                })
                .Build(kit).Key("strip"))
            .Child(Text($"{gallery.Tab} is open. Hover a tab to close it.").Key("body").Font(kit.Ty.Ui).Color(kit.C.TextMuted));

        (string Group, string Name)[] languages =
        [
            ("Systems", "Rust"), ("Systems", "Zig"), ("Systems", "C"), (".NET", "C#"), (".NET", "F#"), ("Web", "TypeScript"), ("Web", "Elm"),
        ];
        var list = List.New().Search(gallery.Filter, "Filter languages").OnSearchEdit(static (Gallery g) => g.Filter).EmptyMessage("Nothing matches");
        var query = gallery.Filter.Text.Trim();
        foreach (var (group, name) in languages.Where(language => language.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            list = list.Item(ListItem.New(name).Group(group).Selected(gallery.Picked == name)
                .OnSelect((Gallery g, Tx _) =>
                {
                    g.Picked = name;
                    g.Say($"Picked {name}.");
                }));
        }

        var accordion = Accordion.New();
        (string Key, string Question, string Answer)[] faq =
        [
            ("faq-1", "What is Rune?", "A Rust UI framework that draws everything itself on the GPU."),
            ("faq-2", "How does C# reach it?", "A generated C ABI shim, linked statically into the NativeAOT executable."),
            ("faq-3", "Is anything hand-written?", "Only a few adapters for APIs a C ABI can't express. The rest is generated."),
        ];
        foreach (var (key, question, answer) in faq)
        {
            accordion = accordion.Item(AccordionItem.New(question).Expanded(gallery.Is(key)).OnToggle((Gallery g) => g.Flip(key))
                .Child(Col().Key("answer").WFull().Px(16f).Pb(12f).Child(KitModule.BodyText(kit, answer).WFull().Wrap())));
        }
        var collapsibles = Col().Key("collapsibles").WFull().Gap(8f)
            .Child(Collapsible.New("Advanced options", gallery.Is("advanced")).OnToggle((Gallery g) => g.Flip("advanced"))
                .Child(KitModule.BodyText(kit, "Hidden until you ask for it.")).Build(kit).Key("advanced"))
            .Child(Collapsible.New("Ghost style", gallery.Is("ghost")).Ghost().OnToggle((Gallery g) => g.Flip("ghost"))
                .Child(KitModule.BodyText(kit, "No surface behind the header.")).Build(kit).Key("ghost"));

        var table = Table.New(static (Gallery g) => g.PeopleTable, static (Gallery g) => g.PeopleTable,
                static (Gallery g) => (nuint)g.People.Count, static (Gallery g, nuint row) => g.People[(int)row].Id)
            .Name("People")
            .Column(TableColumn.New("name", "Name").Flex(2f).Min(160f).Sortable())
            .Column(TableColumn.New("role", "Role").Flex(1.4f).Min(140f).Sortable())
            .Column(TableColumn.New("team", "Team").Flex(1f).Min(110f).Sortable())
            .Column(TableColumn.New("commits", "Commits").Width(96f).Align(Align.End).Sortable())
            .Cell(static (Gallery g, nuint row, string column) =>
            {
                var person = g.People[(int)row];
                return column switch
                {
                    "role" => Cell.Text(person.Role),
                    "team" => Cell.Text(person.Team).Muted(),
                    "commits" => Cell.Text(person.Commits.ToString("N0")).Mono(),
                    _ => Cell.Text(person.Name),
                };
            })
            .OnSort(static (Gallery g, string column, SortDir dir, Tx _) =>
            {
                g.PeopleTable.SetSort(column, dir);
                g.SortPeople(column, dir);
            })
            .OnSelect(static (Gallery g, string id, Tx _) => g.Say($"Selected {g.People.First(person => person.Id == id).Name}."))
            .OnOpen(static (Gallery g, string id, Tx _) => g.Say($"Opened {g.People.First(person => person.Id == id).Name}."))
            .Zebra(true)
            .Build(gallery, kit);

        var tree = Tree.New(static (Gallery g) => g.Files, static (Gallery g) => g.Files, static item => Leaf((string)item))
            .Name("Files")
            .Row(static (item, row) =>
            {
                var path = (string)item;
                var label = Leaf(path);
                return label.Contains('.') ? TreeItem.New(label).Icon(KitModule.FileIcon(label)) : TreeItem.New(label);
            })
            .OnSelect(static (Gallery g, string id, Tx _) => g.Say($"Selected {id}."))
            .OnOpen(static (Gallery g, string id, Tx _) => g.Say($"Opened {id}."))
            .Build(gallery, kit);

        return Stack(
            Section(kit, "tab-strip", "Tab strip", tabStrip),
            Section(kit, "table", "Table", El().Key("frame").WFull().H(270f).Child(table), "Click a header to sort. Drag a divider to resize."),
            Columns(Section(kit, "list", "Searchable list", El().Key("frame").W(300f).Child(list.Build(kit)), "Grouped, with the search filtering it."),
                Section(kit, "tree", "Tree", El().Key("frame").WFull().H(300f).Child(tree), "Arrow keys move, Enter opens.")),
            Section(kit, "accordion", "Accordion and collapsibles", Col().Key("stack").W(560f).Gap(16f).Child(accordion.Build(kit).Key("accordion")).Child(collapsibles)));
    }

    static string Leaf(string path) => path[(path.LastIndexOf('/') + 1)..];

    // Layout

    /// <summary>A titled panel holding one widget family.</summary>
    static El Section(Kit kit, string key, string title, El content, string? note = null)
    {
        var colors = kit.C;
        var heading = Col().Key("heading").Gap(2f)
            .Child(Text(title).Key("title").Font(kit.Ty.UiMedium).Color(colors.TextStrong));
        if (note != null)
        {
            heading = heading.Child(Text(note).Key("note").Font(kit.Ty.Small).Color(colors.TextMuted));
        }
        return Col().Key(key).Gap(12f).Grow(1f).MinW(0f)
            .Child(heading)
            .Child(Col().Key("panel").WFull().Padding(20f).Gap(12f).ItemsStart().Fill(colors.Layer01).Radius(kit.Radius.Lg)
                .Border(1f, colors.BorderWeak).Child(content));
    }

    /// <summary>A control in a <see cref="Rune.Field"/>: its label above, and help text before and after it.</summary>
    static El Labelled(Kit kit, string key, string label, El control, string? before = null, string? after = null)
    {
        var field = Rune.Field.New().Label(label).Control(control);
        if (before != null)
        {
            field = field.Prefix(before);
        }
        if (after != null)
        {
            field = field.Suffix(after);
        }
        return field.Build(kit).Key(key);
    }

    static El Inline(Kit kit, string label, El control) =>
        Row().Gap(8f).CenterY().Child(Text(label).Key("label").Font(kit.Ty.Ui).Color(kit.C.TextMuted)).Child(control.Key("control"));

    static El Wrap(float gap) => Row().Key("wrap").Gap(gap).CenterY().FlexWrap();

    static El Columns(El left, El right) => Row().Key($"columns").WFull().Gap(20f).Child(left).Child(right);

    static El Stack(params El[] sections)
    {
        var stack = Col().Key("sections").WFull().Gap(28f);
        for (int i = 0; i < sections.Length; i++)
        {
            stack = stack.Child(sections[i].Key($"s{i}"));
        }
        return stack;
    }
}
