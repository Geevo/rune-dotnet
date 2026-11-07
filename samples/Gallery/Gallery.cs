using System;
using System.Collections.Generic;
using System.Linq;
using Rune;

enum Page { Buttons, Toggles, Inputs, Overlays, Feedback, Collections }

sealed record Person(string Id, string Name, string Role, string Team, int Commits);

sealed record ToastNote(int Id, string Title, string Description);

/// <summary>Everything the gallery shows. The rune handle types (text edits, pane, table and tree
/// state) are fields: they're structs, and a property would hand out a copy.</summary>
sealed class Gallery
{
    public Page Page { get; set; } = Page.Buttons;
    public int Theme { get; set; }
    /// <summary>Null follows the system.</summary>
    public bool? Dark { get; set; }
    public string Status { get; private set; } = "Ready.";
    public PaneState Pane = PaneState.New(220f);

    /// <summary>The open overlay, by key.</summary>
    public string? Open { get; set; }
    /// <summary>What the pointer is over, for tooltips and the hover card.</summary>
    public string? Hovered { get; set; }

    readonly HashSet<string> _on = ["notify", "sync", "terms", "faq-1"];
    public bool Is(string key) => _on.Contains(key);
    public void Flip(string key)
    {
        if (!_on.Remove(key))
        {
            _on.Add(key);
        }
    }

    public void Toggle(string key) => Open = Open == key ? null : key;
    public void Close() => Open = null;

    public void Say(string status) => Status = status;

    // Toggles
    public bool[] Features { get; } = [true, false, true];
    public string Shipping { get; set; } = "standard";
    public int View { get; set; } = 1;

    // Inputs
    public TextEdit Name = TextEdit.New("Ada Lovelace");
    public TextEdit Email = TextEdit.New("ada@");
    public TextEdit Password = TextEdit.New("hunter2");
    public TextEdit Search = TextEdit.New("");
    public TextEdit Handle = TextEdit.New("rune-dotnet");
    public TextEdit Notes = TextEdit.New("Statically linked, NativeAOT, no runtime to install.\nEvery widget here comes from rune-kit.");
    public TextEdit Share = TextEdit.New("https://github.com/Geevo/rune-dotnet");
    public bool Reveal { get; set; }
    public string? Fruit { get; set; } = "Mango";
    public string? Region { get; set; }
    public string? Language { get; set; } = "csharp";
    public ComboboxState Languages = ComboboxState.CreateDefault();
    public ShortcutRecorderState Recorder = ShortcutRecorderState.CreateDefault();

    // Feedback
    public float Progress { get; set; } = 0.42f;
    public int Stars { get; set; } = 1280;

    // Collections
    public string Tab { get; set; } = "Program.cs";
    public List<string> Tabs { get; } = ["Program.cs", "Widgets.cs", "Gallery.cs", "README.md"];
    public TextEdit Filter = TextEdit.New("");
    public string Picked { get; set; } = "Rust";
    public List<Person> People { get; } =
    [
        new("ada", "Ada Lovelace", "Engineer", "Engines", 1842),
        new("grace", "Grace Hopper", "Admiral", "Compilers", 2207),
        new("alan", "Alan Turing", "Researcher", "Theory", 961),
        new("katherine", "Katherine Johnson", "Mathematician", "Flight", 1530),
        new("linus", "Linus Torvalds", "Maintainer", "Kernel", 3120),
        new("margaret", "Margaret Hamilton", "Director", "Flight", 2764),
        new("dennis", "Dennis Ritchie", "Engineer", "Systems", 1999),
    ];
    public TableState PeopleTable = TableState.New().Key("people");
    public TreeModel Files = NewFiles();

    // Toasts
    public List<ToastNote> Toasts { get; } = [];
    int _nextToast;
    public void Notify(string title, string description)
    {
        Toasts.Add(new ToastNote(_nextToast++, title, description));
        if (Toasts.Count > 3)
        {
            Toasts.RemoveAt(0);
        }
    }

    public void SortPeople(string column, SortDir dir)
    {
        Func<Person, IComparable> by = column switch
        {
            "role" => person => person.Role,
            "team" => person => person.Team,
            "commits" => person => person.Commits,
            _ => person => person.Name,
        };
        var sorted = dir == SortDir.Ascending ? People.OrderBy(by).ToList() : People.OrderByDescending(by).ToList();
        People.Clear();
        People.AddRange(sorted);
    }

    static TreeModel NewFiles()
    {
        string[] paths =
        [
            "src/Rune/Generated.cs", "src/Rune/Rune.cs", "src/Rune/Rune.targets",
            "native/rune-capi/Cargo.toml", "native/rune-capi/src/lib.rs",
            "samples/Gallery/Program.cs", "samples/Gallery/Widgets.cs", "README.md", "LICENSE",
        ];
        var tree = TreeModel.New().Key("files");
        foreach (var path in paths)
        {
            var parts = path.Split('/');
            for (int depth = 0; depth < parts.Length; depth++)
            {
                var id = string.Join('/', parts[..(depth + 1)]);
                if (!tree.Contains(id))
                {
                    tree.Insert(depth == 0 ? null : string.Join('/', parts[..depth]), id, id);
                }
            }
        }
        tree.ExpandTo("samples/Gallery/Program.cs");
        tree.Select("samples/Gallery/Program.cs");
        return tree;
    }
}
