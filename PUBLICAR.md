# Prompt para o Claude da máquina autenticada

Cole a mensagem abaixo, inteira, no Claude Code do seu PC principal — aquele que está logado no
GitHub. Não é para rodar nada na máquina de testes.

**Antes de colar:** copie a pasta `C:\Users\miguel\CS2PerformancePatcher` para o PC principal, ou
clone-a de lá. A pasta tem 40 arquivos rastreados e 1,1 MB de histórico; a subpasta `.research/`
é grande e **não deve ir junto** — ela está no `.gitignore` justamente por isso.

---

## Copie a partir daqui

Tenho um repositório git local pronto para publicar, em `CS2PerformancePatcher`. São 42 commits na
branch `main`, sem remote configurado. Quero publicá-lo no GitHub como repositório **público**.

Antes de qualquer coisa, confira três coisas e me diga o resultado:

1. `git ls-files | grep -i '\.research'` tem de vir **vazio**. Essa pasta contém código
   decompilado da Paradox e um token de sessão capturado; nada dali pode ser publicado. Se
   aparecer qualquer coisa, pare e me avise.
2. `git log --oneline | wc -l` deve dar 42 e `git status` deve estar limpo.
3. `git ls-files | wc -l` deve dar 40.

Depois:

4. Adicione o arquivo `LICENSE` com o texto canônico da **GPL-3.0**, exatamente como publicado em
   https://www.gnu.org/licenses/gpl-3.0.txt — texto integral, sem resumir e sem reescrever. O
   `README.md` já declara essa licença.
5. Faça um commit só com esse arquivo, mensagem: `Add the GPL-3.0 licence text`.
6. Crie o repositório no GitHub como público, nome `CS2PerformancePatcher`, e empurre a `main`.
   A descrição: *"Faz Cities: Skylines II rodar rápido em hardware fraco. Reescreve as
   configurações além do que a interface permite, mais um mod que corta o que é desenhado sem
   nunca tocar na simulação. Tudo medido."*
7. Nos "topics" do repositório: `cities-skylines-2`, `performance`, `modding`, `unity`, `hdrp`.

Não habilite Issues, Wiki ou Projects sem me perguntar antes.

Quando terminar, me passe a URL.

### Contexto, se algo der errado

- O projeto tem três partes: `Cs2Patcher.Core` (a lógica), `Cs2Patcher.Cli` e `Cs2Patcher.Gui`
  (as duas interfaces), e `Cs2Saver.Mod` (o mod, que compila contra o `Game.dll` do jogo).
- O mod **não compila** sem o jogo instalado. Isso é esperado e não impede a publicação; o
  `.csproj` dele emite um aviso claro nesse caso.
- Não rode `dotnet build` na solução inteira só para conferir — se o jogo não estiver nessa
  máquina, o mod vai falhar e isso não significa nada.

## Copie até aqui

---

## O que ainda não está resolvido, para você saber

- **A arte.** O mod reconstrói superfície e cor, mas não contorno nem forma. Um traço de tinta de
  verdade precisa de shader próprio, e isso exige o toolchain oficial da Unity.
- **Os 120 fps.** Fora de alcance nesta máquina: a CPU sozinha gasta 11,2 ms por quadro em
  velocidade normal, contra os 8,33 ms que 120 fps exigem. Medido por três caminhos diferentes.
- **Outro hardware.** Tudo foi medido em um computador só. O `super-potato` e os tiers abaixo dele
  existem para máquinas fracas e **nunca rodaram em uma**.
