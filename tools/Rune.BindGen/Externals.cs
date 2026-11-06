namespace Rune.BindGen;

/// <summary>Types from Rune's dependencies, which have no rustdoc here. They cross as opaque objects:
/// Rune's own API takes and returns them, and `external.rs` adds what C# needs to make them.</summary>
sealed partial class Generator
{
    sealed record External(string Rust, string Cs, bool Copy = false, bool Debug = true, bool Open = false);

    static readonly Dictionary<string, External> Externals = new()
    {
        ["Device"] = new("wgpu::Device", "WgpuDevice"),
        ["Queue"] = new("wgpu::Queue", "WgpuQueue"),
        ["CommandEncoder"] = new("wgpu::CommandEncoder", "WgpuCommandEncoder"),
        ["RenderPass"] = new("wgpu::RenderPass<'_>", "WgpuRenderPass"),
        ["TextureView"] = new("wgpu::TextureView", "WgpuTextureView"),
        ["TextureFormat"] = new("wgpu::TextureFormat", "WgpuTextureFormat", Copy: true),
        ["AstcBlock"] = new("wgpu::AstcBlock", "WgpuAstcBlock", Copy: true),
        ["AstcChannel"] = new("wgpu::AstcChannel", "WgpuAstcChannel", Copy: true),
        ["Buffer"] = new("wgpu::Buffer", "WgpuBuffer"),
        ["Adapter"] = new("wgpu::Adapter", "WgpuAdapter"),
        ["Instance"] = new("wgpu::Instance", "WgpuInstance"),
        ["Features"] = new("wgpu::Features", "WgpuFeatures", Copy: true),
        ["RenderPassTimestampWrites"] = new("wgpu::RenderPassTimestampWrites<'_>", "WgpuTimestampWrites"),
        ["Layout"] = new("parley::Layout<rune::text::Ink>", "TextLayout", Debug: false),
        ["Alignment"] = new("parley::Alignment", "TextAlignment", Copy: true),
        ["Language"] = new("parley::fontique::Language", "TextLanguage", Copy: true),
        ["PhysicalKeyCode"] = new("rune::PhysicalKeyCode", "PhysicalKeyCode", Copy: true, Open: true),
        ["TreeUpdate"] = new("rune::accesskit::TreeUpdate", "AccessKitTreeUpdate"),
        ["Action"] = new("rune::accesskit::Action", "AccessKitAction", Copy: true),
        ["Value"] = new("serde_json::Value", "JsonValue"),
        ["RgbImage"] = new("image::RgbImage", "RgbImage", Debug: false),
        ["Error"] = new("std::io::Error", "IoError"),
        ["Surface"] = new("wgpu::Surface<'static>", "WgpuSurface"),
        ["SubmissionIndex"] = new("wgpu::SubmissionIndex", "WgpuSubmissionIndex"),
        ["LoadOp"] = new("wgpu::LoadOp<wgpu::Color>", "WgpuLoadOp"),
        ["ActionData"] = new("rune::accesskit::ActionData", "AccessKitActionData"),
        ["WindowHandle"] = new("rune::raw_window_handle::WindowHandle<'_>", "RawWindowHandle"),
    };

    static readonly Dictionary<string, string[]> ExternalVariants = File.ReadAllLines(
            "tools/Rune.BindGen/external-enums.txt")
        .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        .Where(l => l.Length > 1)
        .ToDictionary(l => l[0], l => l[1..]);

    /// <summary>Counts each API a bound `rune_ext` function says it covers as bound, and its helpers as nothing.</summary>
    List<(string Api, string Fn)> ApplyCovers()
    {
        var covered = new List<(string, string)>();
        if (_skipped.RemoveAll(s => s.Api == "rune::edit_access") == 1)
        {
            // It returns its argument, to fix a closure's lifetimes. A C# delegate has none to fix.
            _bound++;
            covered.Add(("rune::edit_access", "none needed: pass the delegate itself"));
        }
        foreach (var (doc, item, path, _) in _functions.Values.Where(f => f.RustPath.StartsWith("rune_ext::")))
        {
            var name = item.GetProperty("name").GetString()!;
            if (_skipped.Any(s => s.Api.EndsWith(name) && s.Api.Contains("ext")))
            {
                continue;
            }
            _bound--;
            var docs = item.GetProperty("docs");
            if (docs.ValueKind != System.Text.Json.JsonValueKind.String)
            {
                continue;
            }
            var at = docs.GetString()!.IndexOf("covers: ");
            if (at < 0)
            {
                continue;
            }
            foreach (var api in docs.GetString()![(at + 8)..].Split('\n')[0].Split(", "))
            {
                if (_skipped.RemoveAll(s => s.Api == api) == 0)
                {
                    throw new InvalidOperationException($"{path} covers {api}, which isn't skipped");
                }
                _bound++;
                covered.Add((api, name));
            }
        }
        return covered;
    }

    TypeInfo? ExternalType(Doc doc, string name)
    {
        if (!Externals.TryGetValue(name, out var e))
        {
            return null;
        }
        if (_synth.TryGetValue(e.Rust, out var s))
        {
            return s.T;
        }
        var t = new TypeInfo(e.Rust, doc, default, e.Rust) { CsName = e.Cs, Kind = Kind.Handle, CopyObject = e.Copy };
        _synth[e.Rust] = new(t, null, null) { Ext = e };
        return t;
    }

    void EmitExternal(TypeInfo t, External e)
    {
        if (e.Debug)
        {
            var debug = $"rune_gen_{t.Sym}_debug";
            _rs.AppendLine($"#[unsafe(no_mangle)] pub unsafe extern \"C\" fn {debug}(h: *mut c_void) -> Utf8 {{ ret_str(&format!(\"{{:?}}\", &*(h as *const {t.RustPath}))) }}");
            _native.AppendLine($"    [DllImport(Lib)] public static extern Utf8Ref {debug}(nint h);");
            AddMember(t.CsName, "ToString", n => $"    public override string {n}() => Interop.FromUtf8(N.{debug}(P));");
        }
        if (!ExternalVariants.TryGetValue(t.CsName, out var listed))
        {
            return;
        }
        // `Name=factory` is a variant with data: `rune_ext` has to have a function that makes it.
        foreach (var factory in listed.Where(v => v.Contains('=')).Select(v => v.Split('=')[1]))
        {
            if (!_functions.Values.Any(f => f.RustPath == "rune_ext::" + factory))
            {
                throw new InvalidOperationException($"{t.CsName} has no factory: rune_ext::{factory} is missing");
            }
        }
        // A variant nobody listed stops this compiling, unless the enum is `#[non_exhaustive]`.
        var names = listed.Select(v => v.Split('=')[0]);
        var open = e.Open ? "_ => {} " : "";
        _rs.AppendLine($"#[allow(dead_code)] fn rune_gen_{t.Sym}_listed(v: &{t.RustPath}) {{ match v {{ {string.Concat(names.Select(v => $"{t.RustPath}::{v} {{ .. }} => {{}} "))}{open}}} }}");
        var variants = listed.Where(v => !v.Contains('=')).ToArray();
        var variant = $"rune_gen_{t.Sym}_variant";
        var arms = string.Concat(variants.Select((v, i) => $"{i} => {t.RustPath}::{v}, "));
        _rs.AppendLine($"#[unsafe(no_mangle)] pub unsafe extern \"C\" fn {variant}(i: u32) -> *mut c_void {{ own(match i {{ {arms}_ => unreachable!() }}) }}");
        _native.AppendLine($"    [DllImport(Lib)] public static extern nint {variant}(uint i);");
        for (int i = 0; i < variants.Length; i++)
        {
            var at = i;
            AddMember(t.CsName, variants[i], n => $"    public static {t.Cs} {n} => new(N.{variant}({at}));");
        }
    }
}
