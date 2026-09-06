# Prompt para o Claude da mÃ¡quina autenticada

Cole a mensagem abaixo, inteira, no Claude Code do seu PC principal â€” aquele que estÃ¡ logado no
GitHub. NÃ£o Ã© para rodar nada na mÃ¡quina de testes.

**Antes de colar:** copie a pasta `C:\Users\miguel\CS2PerformancePatcher` para o PC principal, ou
clone-a de lÃ¡. A pasta tem 46 arquivos rastreados; a subpasta `.research/`
Ã© grande e **nÃ£o deve ir junto** â€” ela estÃ¡ no `.gitignore` justamente por isso.

---

## Copie a partir daqui

Tenho um repositÃ³rio git local pronto para publicar, em `CS2PerformancePatcher`. SÃ£o 49 commits na
branch `main`, sem remote configurado. Quero publicÃ¡-lo no GitHub como repositÃ³rio **pÃºblico**.

Antes de qualquer coisa, confira trÃªs coisas e me diga o resultado:

1. `git ls-files | grep -i '\.research'` tem de vir **vazio**. Essa pasta contÃ©m cÃ³digo
   decompilado da Paradox e um token de sessÃ£o capturado; nada dali pode ser publicado. Se
   aparecer qualquer coisa, pare e me avise.
2. `git log --oneline | wc -l` deve dar 53 e `git status` deve estar limpo.
3. `git ls-files | wc -l` deve dar 46.

Depois:

4. Adicione o arquivo `LICENSE` com o texto canÃ´nico da **GPL-3.0**, exatamente como publicado em
   https://www.gnu.org/licenses/gpl-3.0.txt â€” texto integral, sem resumir e sem reescrever. O
   `README.md` jÃ¡ declara essa licenÃ§a.
5. FaÃ§a um commit sÃ³ com esse arquivo, mensagem: `Add the GPL-3.0 licence text`.
6. Crie o repositÃ³rio no GitHub como pÃºblico, nome `CS2PerformancePatcher`, e empurre a `main`.
   A descriÃ§Ã£o: *"Faz Cities: Skylines II rodar rÃ¡pido em hardware fraco. Reescreve as
   configuraÃ§Ãµes alÃ©m do que a interface permite, mais um mod que corta o que Ã© desenhado sem
   nunca tocar na simulaÃ§Ã£o. Tudo medido."*
7. Nos "topics" do repositÃ³rio: `cities-skylines-2`, `performance`, `modding`, `unity`, `hdrp`.

NÃ£o habilite Issues, Wiki ou Projects sem me perguntar antes.

Quando terminar, me passe a URL.

### Contexto, se algo der errado

- O projeto tem trÃªs partes: `Cs2Patcher.Core` (a lÃ³gica), `Cs2Patcher.Cli` e `Cs2Patcher.Gui`
  (as duas interfaces), e `Cs2Saver.Mod` (o mod, que compila contra o `Game.dll` do jogo).
- O mod **nÃ£o compila** sem o jogo instalado. Isso Ã© esperado e nÃ£o impede a publicaÃ§Ã£o; o
  `.csproj` dele emite um aviso claro nesse caso.
- NÃ£o rode `dotnet build` na soluÃ§Ã£o inteira sÃ³ para conferir â€” se o jogo nÃ£o estiver nessa
  mÃ¡quina, o mod vai falhar e isso nÃ£o significa nada.

## Copie atÃ© aqui

---

## O que ainda nÃ£o estÃ¡ resolvido, para vocÃª saber

- **A arte.** O mod reconstrÃ³i superfÃ­cie e cor, mas nÃ£o contorno nem forma. Um traÃ§o de tinta de
  verdade precisa de shader prÃ³prio, e isso exige o toolchain oficial da Unity.
- **Os 120 fps.** Fora de alcance nesta mÃ¡quina: a CPU sozinha gasta 11,2 ms por quadro em
  velocidade normal, contra os 8,33 ms que 120 fps exigem. Medido por trÃªs caminhos diferentes.
- **Outro hardware.** Tudo foi medido em um computador sÃ³. O `super-potato` e os tiers abaixo dele
  existem para mÃ¡quinas fracas e **nunca rodaram em uma**.
- **O antialiasing do jogo estÃ¡ desligado** e nenhum perfil deste projeto toca nisso. O bloco
  `AntiAliasingQualitySettings` do `Settings.coc` fica inteiro no padrÃ£o, e o padrÃ£o em C# Ã©
  `antiAliasingMethod = None`. Descoberto no fim da sessÃ£o de 5 de setembro e nÃ£o medido: pode ser
  que o DLSS jÃ¡ resolva (num corte 1:1 o `handsome` estÃ¡ *borrado*, nÃ£o serrilhado), pode ser que
  um SMAA barato ajude. NinguÃ©m testou.
- **O entardecer.** A cidade ao entardecer continua menos dramÃ¡tica que a do jogo original. A causa
  foi encontrada â€” a tonalidade fria das sombras domina um quadro que Ã© quase todo sombra â€” e
  metade dela foi corrigida. O conserto certo Ã© a coloraÃ§Ã£o saber a hora do dia, o que o mod
  consegue fazer (`PlanetarySystem.time`) e ainda nÃ£o faz.
