using System.Collections.Generic;
using Colossal;

namespace Cs2Saver
{
    /// <summary>
    /// Brazilian Portuguese strings for the options page.
    ///
    /// Registered alongside the English source; the game picks whichever matches its own language
    /// setting, so nothing here needs to detect anything.
    ///
    /// <para>Translated rather than transliterated. Every label in this mod names what a setting
    /// costs rather than what it does, and a literal rendering loses that — "Stop drawing the
    /// clutter sooner" is a promise about what you will notice, not a description of a culling
    /// distance, and the Portuguese has to make the same promise.</para>
    ///
    /// <para>This file is UTF-8 and full of accented characters, which makes it the one file in
    /// the repository that must never be edited by a tool that assumes Latin-1. One pass of a
    /// stream editor already turned every "ç" in here into "Ã§" once.</para>
    /// </summary>
    public sealed class LocalePT : IDictionarySource
    {
        private readonly Settings m_Settings;

        public LocalePT(Settings settings) => m_Settings = settings;

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(
            IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Settings.GetSettingsLocaleID(), "CS2 Saver" },
                { m_Settings.GetOptionTabLocaleID(Settings.MainSection), "Principal" },

                { m_Settings.GetOptionGroupLocaleID(Settings.RenderingGroup), "Renderização" },
                { m_Settings.GetOptionGroupLocaleID(Settings.MeasurementGroup), "Medição" },

                {
                    m_Settings.GetOptionLabelLocaleID(nameof(Settings.Preset)),
                    "Parar de desenhar o entulho mais cedo"
                },
                {
                    m_Settings.GetOptionDescLocaleID(nameof(Settings.Preset)),
                    "Mobiliário de rua, trânsito e pedestres somem do quadro a uma distância " +
                    "menor que a do jogo. Cada passo corta essa distância pela metade. Prédios " +
                    "nunca são afetados por nenhuma destas opções, e é isso que separa esta " +
                    "configuração de baixar o controle de detalhe do próprio jogo: a linha do " +
                    "horizonte mantém cada triângulo que tinha. A vegetação tem controle próprio, " +
                    "logo abaixo."
                },

                { m_Settings.GetEnumValueLocaleID(Settings.BudgetPreset.Off), "Desligado" },
                {
                    m_Settings.GetEnumValueLocaleID(Settings.BudgetPreset.Balanced),
                    "Equilibrado — pedestres somem antes, trânsito intacto"
                },
                {
                    m_Settings.GetEnumValueLocaleID(Settings.BudgetPreset.TrafficFocus),
                    "Foco no trânsito — pedestres só de perto, carros um pouco antes"
                },
                {
                    m_Settings.GetEnumValueLocaleID(Settings.BudgetPreset.Aggressive),
                    "Agressivo — tudo que se move é desenhado só perto da câmera"
                },
                {
                    m_Settings.GetEnumValueLocaleID(Settings.BudgetPreset.Declutter),
                    "Limpeza — tudo que não é prédio é puxado para perto"
                },
                {
                    m_Settings.GetEnumValueLocaleID(Settings.BudgetPreset.DeclutterMax),
                    "Limpeza máxima — no nível da rua se nota; de cima, não"
                },
                {
                    m_Settings.GetEnumValueLocaleID(Settings.BudgetPreset.TreesOnly),
                    "Só vegetação — árvores somem antes, o resto fica igual"
                },
                {
                    m_Settings.GetEnumValueLocaleID(Settings.BudgetPreset.PropsOnly),
                    "Só entulho urbano — placas, postes e cercas somem antes"
                },

                {
                    m_Settings.GetOptionLabelLocaleID(nameof(Settings.Greenery)),
                    "Quanta vegetação sobrevive"
                },
                {
                    m_Settings.GetOptionDescLocaleID(nameof(Settings.Greenery)),
                    "Separado do preset acima porque é gosto, não grau. Na máquina onde isto foi " +
                    "medido, cinco quadros por segundo separam uma cidade com árvores de uma sem: " +
                    "56 fps em Cheia, 58 em Equilibrada, 61 em Rala."
                },
                {
                    m_Settings.GetEnumValueLocaleID(RenderBudget.Foliage.Untouched),
                    "Intocada — cada árvore que o jogo desenharia"
                },
                {
                    m_Settings.GetEnumValueLocaleID(RenderBudget.Foliage.Full),
                    "Cheia — metade da distância, cidade ainda verde"
                },
                {
                    m_Settings.GetEnumValueLocaleID(RenderBudget.Foliage.Balanced),
                    "Equilibrada — mais rala, ainda claramente arborizada"
                },
                {
                    m_Settings.GetEnumValueLocaleID(RenderBudget.Foliage.Thin),
                    "Rala — campo onde havia floresta, e cinco quadros por isso"
                },

                {
                    m_Settings.GetOptionLabelLocaleID(nameof(Settings.CityLook)),
                    "Dar um visual à cidade"
                },
                {
                    m_Settings.GetOptionDescLocaleID(nameof(Settings.CityLook)),
                    "Cor, contraste e tonalidade. Não custa nada para renderizar, e existe porque " +
                    "uma cidade com sombras e efeitos desligados parece quebrada, e não " +
                    "estilizada, até algo colocar uma intenção de volta nela."
                },
                { m_Settings.GetEnumValueLocaleID(Look.Off), "Desligado — as cores do próprio jogo" },
                { m_Settings.GetEnumValueLocaleID(Look.Vivid), "Vívido — cor de volta, e nada mais" },
                { m_Settings.GetEnumValueLocaleID(Look.Toybox), "Brinquedo — cor forte, sol quente, sombra fria" },
                { m_Settings.GetEnumValueLocaleID(Look.Miniature), "Miniatura — maquete sob uma luminária" },
                { m_Settings.GetEnumValueLocaleID(Look.Showroom), "Vitrine — render de arquitetura: luz quente, céu frio" },
                { m_Settings.GetEnumValueLocaleID(Look.Cel), "Desenho — luz em faixas chapadas" },

                {
                    m_Settings.GetOptionLabelLocaleID(nameof(Settings.CitySurface)),
                    "Como as superfícies pegam a luz"
                },
                {
                    m_Settings.GetOptionDescLocaleID(nameof(Settings.CitySurface)),
                    "Reescreve os materiais do próprio jogo para o asfalto parar de parecer " +
                    "molhado e as telhas pararem de reluzir. Isto muda a arte, não a imagem. As " +
                    "janelas vão no sentido contrário: acendem, porque vidro brilhando contra " +
                    "parede fosca é o que faz a cidade parecer render de arquitetura em vez de " +
                    "maquete de massa."
                },
                { m_Settings.GetEnumValueLocaleID(Surface.Off), "Desligado — os materiais do próprio jogo" },
                { m_Settings.GetEnumValueLocaleID(Surface.Matte), "Fosco — sai o brilho molhado, as janelas acendem" },
                { m_Settings.GetEnumValueLocaleID(Surface.Painted), "Pintado — chapado como tinta guache" },

                {
                    m_Settings.GetOptionLabelLocaleID(nameof(Settings.WatchForHitches)),
                    "Anotar cada engasgo"
                },
                {
                    m_Settings.GetOptionDescLocaleID(nameof(Settings.WatchForHitches)),
                    "Escreve no log cada quadro travado, com a altura da câmera e o quanto ela " +
                    "andou, para separar um engasgo ao aproximar de um que acontece de qualquer " +
                    "jeito. Não custa nada desligado e quase nada ligado."
                },

                {
                    m_Settings.GetOptionLabelLocaleID(nameof(Settings.RecordFrameTimings)),
                    "Gravar tempos de quadro num arquivo"
                },
                {
                    m_Settings.GetOptionDescLocaleID(nameof(Settings.RecordFrameTimings)),
                    "Grava um CSV a cada dez segundos com o FPS médio e, mais útil, os 1% e 0,1% " +
                    "piores — os quadros lentos que você realmente sente. Use para conferir se " +
                    "uma configuração ajudou em vez de adivinhar. Fica na pasta Cs2Saver dentro " +
                    "dos dados do jogo."
                },

                {
                    m_Settings.GetOptionLabelLocaleID(nameof(Settings.RunLabel)),
                    "Nome desta medição"
                },
                {
                    m_Settings.GetOptionDescLocaleID(nameof(Settings.RunLabel)),
                    "Escrito em cada linha para que as medições possam ser comparadas. Troque " +
                    "sempre que mudar uma configuração, por exemplo \"desligado\" e depois " +
                    "\"foco no trânsito\"."
                },
            };
        }

        public void Unload() { }
    }
}
