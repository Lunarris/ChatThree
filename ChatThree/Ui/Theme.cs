using System.Numerics;
using ChatThree.Resources;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace ChatThree.Ui;

public readonly record struct ThemePalette(
    Vector4 Bg,
    Vector4 Surface,
    Vector4 SurfaceHi,
    Vector4 Border,
    Vector4 Text,
    Vector4 TextDim,
    Vector4 Accent);

public static class Theme
{
    private static readonly ThemePalette Midnight = new(
        Bg: new(0.075f, 0.078f, 0.094f, 0.97f),
        Surface: new(0.110f, 0.114f, 0.137f, 1.00f),
        SurfaceHi: new(0.155f, 0.161f, 0.192f, 1.00f),
        Border: new(1f, 1f, 1f, 0.06f),
        Text: new(0.92f, 0.93f, 0.96f, 1.00f),
        TextDim: new(0.58f, 0.60f, 0.67f, 1.00f),
        Accent: new(0.52f, 0.45f, 0.98f, 1.00f));

    private static readonly ThemePalette Slate = new(
        Bg: new(0.090f, 0.105f, 0.125f, 0.97f),
        Surface: new(0.130f, 0.150f, 0.175f, 1.00f),
        SurfaceHi: new(0.180f, 0.205f, 0.235f, 1.00f),
        Border: new(1f, 1f, 1f, 0.07f),
        Text: new(0.90f, 0.93f, 0.96f, 1.00f),
        TextDim: new(0.56f, 0.62f, 0.68f, 1.00f),
        Accent: new(0.30f, 0.62f, 0.95f, 1.00f));

    private static readonly ThemePalette Ember = new(
        Bg: new(0.105f, 0.085f, 0.075f, 0.97f),
        Surface: new(0.150f, 0.120f, 0.105f, 1.00f),
        SurfaceHi: new(0.205f, 0.165f, 0.140f, 1.00f),
        Border: new(1f, 0.9f, 0.8f, 0.07f),
        Text: new(0.96f, 0.92f, 0.88f, 1.00f),
        TextDim: new(0.68f, 0.60f, 0.54f, 1.00f),
        Accent: new(0.95f, 0.58f, 0.25f, 1.00f));

    private static ThemePalette current = Midnight;

    public static bool Enabled { get; private set; }

    private static ThemePalette Preset(ThemeScheme scheme) => scheme switch
    {
        ThemeScheme.Slate => Slate,
        ThemeScheme.Ember => Ember,
        _ => Midnight,
    };

    public static string SchemeName(ThemeScheme scheme) => scheme switch
    {
        ThemeScheme.Slate => Language.Options_ThemeScheme_Slate,
        ThemeScheme.Ember => Language.Options_ThemeScheme_Ember,
        _ => Language.Options_ThemeScheme_Midnight,
    };

    public static void Update(Configuration config)
    {
        Enabled = config.UseTheme;
        current = Preset(config.ThemeScheme);
    }

    private static Vector4 A(float alpha) => current.Accent with { W = alpha };

    public static Scope Push()
    {
        int colors = 0, vars = 0;

        void C(ImGuiCol c, Vector4 v) { ImGui.PushStyleColor(c, v); colors++; }
        void V(ImGuiStyleVar s, float v) { ImGui.PushStyleVar(s, v); vars++; }

        var p = current;

        V(ImGuiStyleVar.WindowRounding, 10f);
        V(ImGuiStyleVar.ChildRounding, 8f);
        V(ImGuiStyleVar.FrameRounding, 6f);
        V(ImGuiStyleVar.PopupRounding, 8f);
        V(ImGuiStyleVar.GrabRounding, 6f);
        V(ImGuiStyleVar.ScrollbarRounding, 8f);
        V(ImGuiStyleVar.TabRounding, 6f);

        C(ImGuiCol.WindowBg, p.Bg);
        C(ImGuiCol.ChildBg, Vector4.Zero);
        C(ImGuiCol.PopupBg, p.Surface);
        C(ImGuiCol.Border, p.Border);
        C(ImGuiCol.TitleBg, p.Bg);
        C(ImGuiCol.TitleBgActive, p.Bg);
        C(ImGuiCol.TitleBgCollapsed, p.Bg);
        C(ImGuiCol.MenuBarBg, p.Surface);

        C(ImGuiCol.Text, p.Text);
        C(ImGuiCol.TextDisabled, p.TextDim);

        C(ImGuiCol.FrameBg, p.Surface);
        C(ImGuiCol.FrameBgHovered, p.SurfaceHi);
        C(ImGuiCol.FrameBgActive, p.SurfaceHi);

        C(ImGuiCol.Button, A(0.28f));
        C(ImGuiCol.ButtonHovered, A(0.45f));
        C(ImGuiCol.ButtonActive, A(0.65f));
        C(ImGuiCol.Header, A(0.18f));
        C(ImGuiCol.HeaderHovered, A(0.30f));
        C(ImGuiCol.HeaderActive, A(0.45f));

        C(ImGuiCol.CheckMark, A(1f));
        C(ImGuiCol.SliderGrab, A(1f));
        C(ImGuiCol.SliderGrabActive, A(0.85f));
        C(ImGuiCol.Separator, p.Border);
        C(ImGuiCol.SeparatorHovered, A(0.6f));
        C(ImGuiCol.SeparatorActive, A(1f));
        C(ImGuiCol.ResizeGrip, Vector4.Zero);
        C(ImGuiCol.ResizeGripHovered, A(0.4f));
        C(ImGuiCol.ResizeGripActive, A(0.7f));

        C(ImGuiCol.Tab, Vector4.Zero);
        C(ImGuiCol.TabHovered, A(0.30f));
        C(ImGuiCol.TabActive, A(0.22f));
        C(ImGuiCol.TabUnfocused, Vector4.Zero);
        C(ImGuiCol.TabUnfocusedActive, A(0.15f));

        C(ImGuiCol.ScrollbarBg, Vector4.Zero);
        C(ImGuiCol.ScrollbarGrab, p.SurfaceHi);
        C(ImGuiCol.ScrollbarGrabHovered, A(0.5f));
        C(ImGuiCol.ScrollbarGrabActive, A(0.8f));

        C(ImGuiCol.TableHeaderBg, p.Surface);
        C(ImGuiCol.TableBorderStrong, p.Border);
        C(ImGuiCol.TableBorderLight, p.Border);
        C(ImGuiCol.TableRowBg, Vector4.Zero);
        C(ImGuiCol.TableRowBgAlt, new Vector4(1f, 1f, 1f, 0.025f));

        return new Scope(colors, vars);
    }

    public readonly struct Scope : IDisposable
    {
        private readonly int colors, vars;
        public Scope(int colors, int vars) { this.colors = colors; this.vars = vars; }
        public void Dispose()
        {
            if (colors > 0) ImGui.PopStyleColor(colors);
            if (vars > 0) ImGui.PopStyleVar(vars);
        }
    }
}

public abstract class ThemedWindow : Window
{
    private Theme.Scope themeScope;

    protected ThemedWindow(string name, ImGuiWindowFlags flags = ImGuiWindowFlags.None, bool forceMainWindow = false)
        : base(name, flags, forceMainWindow)
    {
    }

    public override void PreDraw() => themeScope = Theme.Enabled ? Theme.Push() : default;

    public override void PostDraw()
    {
        themeScope.Dispose();
        themeScope = default;
    }
}
