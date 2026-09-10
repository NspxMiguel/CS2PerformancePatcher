using Cs2Patcher.Core;

namespace Cs2Patcher.Gui;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // The window took its language from the operating system and offered no way to say
        // otherwise, while the console tool has accepted --lang since it learned to speak
        // Portuguese. Someone on an English Windows who wants Portuguese could get it from one
        // half of this project and not the other, which is not a choice anybody made.
        Text.TrySet(ValueOf(args, "--lang"));

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }

    /// <summary>The argument after <paramref name="name"/>, or null if it is absent or last.</summary>
    private static string? ValueOf(string[] argv, string name)
    {
        var i = Array.FindIndex(argv, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < argv.Length ? argv[i + 1] : null;
    }
}
