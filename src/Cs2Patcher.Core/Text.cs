using System.Globalization;

namespace Cs2Patcher.Core;

public enum Language
{
    English,
    Portuguese,
}

/// <summary>
/// Every sentence this tool says to a person, in both languages it speaks.
///
/// <para><b>Why a class and not a resource file.</b> These strings are not labels, they are
/// explanations — the whole design of this tool is that it never changes a setting without saying
/// what the setting costs. Keeping them next to each other in one file means a change to the
/// English cannot silently leave the Portuguese describing the old behaviour, which is exactly
/// what happens when translations live somewhere a developer does not look.</para>
///
/// <para>The language is taken from the operating system and can be overridden with
/// <c>--lang pt</c> or <c>--lang en</c>. Profile descriptions are the one thing kept in
/// <see cref="TuningProfile"/> rather than here, because they double as documentation of why each
/// tier exists; the Portuguese versions live in <see cref="ProfileDescription"/> and fall back to
/// the English if one is ever missing, so a gap shows as untranslated rather than as blank.</para>
/// </summary>
public static class Text
{
    public static Language Language { get; set; } = Detect();

    /// <summary>Portuguese if the machine is set to it, English otherwise.</summary>
    public static Language Detect() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("pt", StringComparison.OrdinalIgnoreCase)
            ? Language.Portuguese
            : Language.English;

    /// <summary>Accepts "pt", "pt-BR", "en", "en-US". Anything else leaves the choice alone.</summary>
    public static bool TrySet(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;

        if (code.StartsWith("pt", StringComparison.OrdinalIgnoreCase)) { Language = Language.Portuguese; return true; }
        if (code.StartsWith("en", StringComparison.OrdinalIgnoreCase)) { Language = Language.English; return true; }

        return false;
    }

    private static string P(string en, string pt) => Language == Language.Portuguese ? pt : en;

    // -- headings ---------------------------------------------------------------------------

    public static string Installation => P("Installation", "Instalação");
    public static string Path => P("Path", "Caminho");
    public static string UserData => P("User data", "Dados do usuário");
    public static string GameVersion => P("Game version", "Versão do jogo");
    public static string SteamBuild => P("Steam build", "Build do Steam");
    public static string HardwareHeading =>
        P("Hardware (as the game itself reported it)", "Hardware (como o próprio jogo reportou)");
    public static string SystemRam => P("System RAM", "Memória do sistema");

    public static string PatchStatus => P("Patch status", "Estado do patch");
    public static string NotPatched => P("NOT PATCHED", "SEM PATCH");
    public static string Patched => P("PATCHED", "COM PATCH");
    public static string AppliedAt => P("Applied at", "Aplicado em");
    public static string Changes => P("Changes", "Alterações");

    public static string ApplyOneWith => P("Apply one with", "Aplique um com");
    public static string ApplyWith => P("Apply with", "Aplicar com");
    public static string UndoWith => P("Undo with", "Desfazer com");
    public static string NotSure => P("Not sure", "Na dúvida");
    public static string SeeIdsWith => P("See the ids with", "Veja os ids com");

    // -- profile list -----------------------------------------------------------------------

    public static string ProfilesHeading =>
        P("Profiles, best-looking first.", "Perfis, do mais bonito ao mais rápido.");

    public static string TestedOn => P("Tested on", "Testado em");
    public static string MeasuredOn => P("Measured", "Medido em");
    public static string UntouchedIs => P("Untouched", "Sem alterar");

    public static string MeasuredHow => P(
        "normal play speed, mod at Declutter with full greenery, one session",
        "velocidade normal de jogo, mod em Limpeza com vegetação Cheia, uma sessão só");

    public static string UntouchedNumbers => P(
        "26.1 fps, 14.0 1% low — the mean of three runs that spread 0.2",
        "26,1 fps, 14,0 de 1% low — média de três runs que variaram 0,2");

    public static string YoursWillDiffer => P(
        "Your numbers will differ. These are one computer, not a benchmark database.",
        "Os seus números vão diferir. Isto é um computador, não um banco de dados de benchmarks.");

    public static string ColFps => P("fps", "fps");
    public static string ColLow => P("1% low", "1% low");
    public static string ColAtSixty => P("at 60", "a 60");
    public static string ColGain => P("gain", "ganho");

    // -- targets ----------------------------------------------------------------------------

    public static string TargetsHeading => P(
        "What this machine can hold, and what each one costs.",
        "O que esta máquina consegue segurar, e o que cada opção custa.");

    public static string TargetsExplainer => P(
        "  'On average' is the headline number. 'Never dropping' means the slowest 1%\n"
        + "  of frames get there too, which is what people mean by stable and is a much\n"
        + "  harder bar - the tier that averages 61 here has a 1% low of 39.",
        "  'Na média' é o número de manchete. 'Sem nunca cair' quer dizer que o 1% de\n"
        + "  quadros mais lentos também chega lá, que é o que as pessoas querem dizer com\n"
        + "  estável - e é bem mais difícil: o perfil que faz 61 de média aqui tem 39 de 1% low.");

    public static string ColTarget => P("target", "alvo");
    public static string ColOnAverage => P("on average", "na média");
    public static string ColNeverDropping => P("never dropping", "sem nunca cair");
    public static string OutOfReach => P("out of reach", "fora de alcance");
    public static string Most => P("most", "máximo");

    public static string ModShadowNeedsMod => P(
        "This profile turns the sun's shadow back on. Without Cs2Saver to bound how far it "
        + "reaches, that costs about six frames per second instead of one. Install it with "
        + "'cs2patch install-mod'.",
        "Este perfil religa a sombra do sol. Sem o Cs2Saver para limitar até onde ela chega, "
        + "isso custa uns seis quadros por segundo em vez de um. Instale com "
        + "'cs2patch install-mod'.");

    public static string FoliageNote => P(
        "  Handsome makes sixty with the sun's shadow on, which nothing here did before. It\n"
        + "  needs the mod: shadows bounded to a block cost 1 fps, unbounded they cost 6.",
        "  O Bonito bate sessenta com a sombra do sol ligada, o que nada aqui fazia antes.\n"
        + "  Ele depende do mod: a sombra limitada a um quarteirao custa 1 fps, solta custa 6.");

    public static string ProjectedFrom => P("Projected from", "Projetado a partir de");
    public static string YoursWillDifferShort => P("Yours will differ.", "O seu vai diferir.");

    // -- recommendation ---------------------------------------------------------------------

    public static string Recommended => P("Recommended", "Recomendado");
    public static string Why => P("Why", "Por quê");
    public static string Expect => P("Expect", "Espere");
    public static string Costs => P("Costs", "Custa");

    public static string ExpectLine(double fps, double low) => P(
        $"about {fps:N0} fps on average, {low:N0} on the 1% low",
        $"cerca de {fps:N0} fps de média, {low:N0} no 1% low");

    public static string EstimateWarning => P(
        "  This is an estimate. Every figure in this tool was measured on one machine,\n"
        + "  and yours is placed against it by GPU memory and core count - nothing more.\n"
        + "  To replace it with a real number, install the mod and turn on its frame log.",
        "  Isto é uma estimativa. Todo número desta ferramenta foi medido em uma máquina,\n"
        + "  e a sua é posicionada contra ela por memória de vídeo e núcleos - nada além.\n"
        + "  Para trocar por um número real, instale o mod e ligue a gravação de quadros.");

    public static string InstallModToo => P(
        "  Install the mod too. Roughly a fifth of every figure above comes from it:",
        "  Instale o mod também. Cerca de um quinto de cada número acima vem dele:");

    // -- actions ----------------------------------------------------------------------------

    public static string GameIsRunning => P(
        "Cities: Skylines II is running. Close it first - it overwrites Settings.coc when it exits.",
        "Cities: Skylines II está aberto. Feche primeiro - ele sobrescreve o Settings.coc ao sair.");

    public static string OriginalSavedTo => P("Original saved to", "Original salvo em");
    public static string UndoAnyTime => P("Undo any time with", "Desfaça quando quiser com");

    public static string TierTradesLooks => P(
        "  This tier trades looks for frames. The mod's 'Cel' look puts a deliberate\n"
        + "  style back on top for free - turn it on in its options page.",
        "  Este perfil troca aparência por quadros. O visual 'Desenho' do mod devolve\n"
        + "  um estilo proposital de graça - ligue na página de opções dele.");

    public static string BackupSafe => P(
        "  Your original settings are backed up. Run 'cs2patch revert' to restore them.",
        "  Suas configurações originais estão salvas. Rode 'cs2patch revert' para voltar.");

    // -- profile descriptions ---------------------------------------------------------------

    // -- the advisor's reasoning ------------------------------------------------------------

    public static string IntegratedGpu(string gpu) => P(
        $"{gpu} is integrated graphics, which shares memory with the CPU and has no upscaler to "
        + "fall back on. Estimated at about a third of the machine this tool was measured on.",
        $"{gpu} é gráfico integrado: divide memória com o processador e não tem upscaler para "
        + "recorrer. Estimado em cerca de um terço da máquina onde isto foi medido.");

    public static string ComparedToReference(string gpu, int vram, int threads, double scale) => P(
        $"{gpu} with {vram} MB and {threads} threads, against the 8 GB and 12 threads everything "
        + "here was measured on"
        + (Math.Abs(scale - 1) < 0.05 ? " — near enough the same machine." : $" — about {scale:N2}x."),
        $"{gpu} com {vram} MB e {threads} threads, contra os 8 GB e 12 threads onde tudo isto foi "
        + "medido"
        + (Math.Abs(scale - 1) < 0.05 ? " — praticamente a mesma máquina." : $" — cerca de {scale:N2}x."));

    public static string NothingKnownYet => P(
        "Nothing is known about this machine yet — run the game once so it writes a log. This is "
        + "the tier that reached sixty on the machine this tool was built on.",
        "Ainda não se sabe nada desta máquina — abra o jogo uma vez para ele escrever um log. Este "
        + "é o perfil que chegou a sessenta na máquina onde a ferramenta foi construída.");

    public static string NothingHolds(double target, double fps, double low) => P(
        $" Nothing here holds {target:N0} fps on this machine; the fastest tier projects to "
        + $"{fps:N0} average and {low:N0} on the 1% low.",
        $" Nada aqui segura {target:N0} fps nesta máquina; o perfil mais rápido projeta "
        + $"{fps:N0} de média e {low:N0} no 1% low.");

    // -- the window -------------------------------------------------------------------------

    public static string WindowTitle => P("CS2 Performance Patcher", "CS2 Performance Patcher");
    public static string GroupGame => P("Game", "Jogo");
    public static string GroupProfile => P("Profile", "Perfil");
    public static string GroupLog => P("What changed", "O que mudou");

    public static string ButtonApply => P("Apply", "Aplicar");
    public static string ButtonRevert => P("Revert", "Desfazer");
    public static string ButtonCheckPc => P("Check my PC", "Verificar meu PC");
    public static string ButtonRecommend => P("Recommend for me", "Recomendar para mim");
    public static string ButtonBrowse => P("Browse...", "Procurar...");
    public static string ButtonHoldUpdates => P("Hold updates", "Travar atualizações");
    public static string ButtonReleaseUpdates => P("Release updates", "Liberar atualizações");

    public static string TargetAverageSixty => P("60 fps on average", "60 fps na média");
    public static string TargetNeverDropping(string label) =>
        P($"{label}, never dropping", $"{label}, sem nunca cair");
    public static string TargetAsManyAsPossible => P("As many as possible", "O máximo possível");

    public static string PressApplyIfRight =>
        P("Press Apply if it looks right.", "Aperte Aplicar se parecer certo.");

    public static string NotPatchedShort => P("Not patched.", "Sem patch.");

    public static string ProfileMeasuredLine(Measured m) => P(
        $"About {m.Fps:N0} fps at normal play speed — +{m.GainPercent}% over untouched, "
        + $"with {m.ShareAtSixty}% of frames at 60 or better.",
        $"Cerca de {m.Fps:N0} fps na velocidade normal de jogo — +{m.GainPercent}% sobre o "
        + $"original, com {m.ShareAtSixty}% dos quadros a 60 ou mais.");

    public static string ChangeCount(int total, int free, int cheap, int visible) => P(
        $"{total} changes — {free} invisible, {cheap} barely visible, {visible} visible.",
        $"{total} alterações — {free} invisíveis, {cheap} quase invisíveis, {visible} visíveis.");

    // -- profile names ----------------------------------------------------------------------

    /// <summary>
    /// The tier's name. The low-end ones were named in Portuguese by the person this was built
    /// for and then rendered into English for the repository; this hands them back.
    /// </summary>
    public static string ProfileName(TuningProfile profile)
    {
        if (Language != Language.Portuguese) return profile.Name;

        return profile.Id switch
        {
            "free" => "Grátis",
            "traffic" => "Trânsito",
            "sharp" => "Cidade Nítida",
            "handsome" => "Bonito",
            "skyline" => "Horizonte",
            "potato" => "Notebook Velho",
            "super-potato" => "Microondas",
            "mega-potato" => "Se Abriu é Porque Roda",
            "bone-dry" => "Seco Seco Seco",
            _ => profile.Name,
        };
    }

    /// <summary>
    /// The Portuguese description for a tier, or the profile's own English if there is none.
    /// A missing translation shows as English rather than as nothing.
    /// </summary>
    public static string ProfileDescription(TuningProfile profile)
    {
        if (Language != Language.Portuguese) return profile.Description;

        return profile.Id switch
        {
            "free" => "Remove trabalho de renderização que não produz pixel visível. "
                      + "Nenhuma mudança perceptível.",

            "traffic" => "O anterior, mais cortes em efeitos que você não olha. Cidade e trânsito "
                         + "continuam nítidos.",

            "sharp" => "Corta todo efeito de tela. Geometria e texturas absolutamente intactas.",

            "handsome" => "Sessenta quadros que não parecem ter custado nada. Textura em resolução "
                          + "cheia, geometria no 'Low' do próprio jogo, três vezes a resolução "
                          + "interna do perfil abaixo, e — o que separa este dos outros — a sombra "
                          + "do sol e a oclusão de volta, porque uma cidade sem nenhuma das duas "
                          + "não fica estilizada, fica chapada.",

            "skyline" => "Prédios e texturas nítidos; as sombras do sol e o entulho é que cedem.",

            "potato" => "Tudo acima, mais cortes visíveis, incluindo textura borrada. Para máquinas "
                        + "bem abaixo do exigido pelo jogo.",

            "super-potato" => "Tudo acima, empurrado até o jogo deixar de parecer com ele mesmo.",

            "mega-potato" => "Dois terços da resolução, sem nuvens, sem névoa, texturas no mais "
                             + "borrado. Não compra nada numa máquina já limitada pela CPU; compra "
                             + "tudo numa limitada pela placa de vídeo.",

            "bone-dry" => "Metade da resolução por eixo, com todo o resto já no fundo. Não existe "
                          + "nada abaixo disto, porque não sobrou nada para abaixar.",

            _ => profile.Description,
        };
    }
}
