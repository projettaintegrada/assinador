# Projetta Assinador

Assinador de PDF com certificado ICP-Brasil, no padrão PAdES, com carimbo da Projetta.

O cliente abre o link, escolhe o PDF, toca onde quer o carimbo e escolhe o certificado:

- **Instalado ou token A3:** os certificados do Windows aparecem numa lista, inclusive os de token e cartão. Para isso o cliente instala uma vez o **Projetta Assinador**, um programinha que fica perto do relógio do Windows.
- **Arquivo A1 (.pfx):** funciona em qualquer aparelho, inclusive celular, sem instalar nada.

Nos dois casos o documento e a chave do certificado ficam no aparelho do cliente.

## O que tem aqui

| Pasta | O que é |
|---|---|
| `site/` | A página do assinador. Vira o seu link. |
| `agente/` | O programa do Windows (Projetta Assinador). |
| `.github/workflows/` | A automação que compila o programa e publica o site sozinha. |

## Colocar no ar (uma vez, de graça)

1. Crie uma conta em **github.com**.
2. Clique em **New repository**. Nome: `assinador`. Marque **Public**. Clique em **Create repository**.
3. Na página do repositório, clique em **uploading an existing file**. Descompacte o .zip no computador e arraste **o conteúdo** da pasta `projetta-assinador` (as pastas `site`, `agente`, `.github` e este arquivo). Clique em **Commit changes**.
   - A pasta `.github` começa com ponto e às vezes fica escondida. Confira se ela subiu. Se não aparecer, no Windows Explorer marque *Exibir > Itens ocultos* e arraste de novo.
4. Vá em **Settings > Pages**. Em **Source**, escolha **GitHub Actions**.
5. Vá em **Actions**. A automação "Compilar e publicar" vai rodar (leva uns 3 minutos). Se não começou sozinha, clique nela e em **Run workflow**.
6. Quando ficar verde, o site estará em:
   **https://SEU-USUARIO.github.io/assinador/**

Esse é o link para mandar ao cliente. O programa do Windows já sai pronto para aceitar somente esse site.

## Como o cliente usa (modo instalado ou token)

1. Abre o link no Chrome ou no Edge.
2. Na primeira vez, clica em **Baixar o Projetta Assinador** e abre o arquivo.
   - O Windows pode mostrar "O Windows protegeu o computador". Clicar em **Mais informações** e **Executar assim mesmo**. Isso acontece porque o programa não tem assinatura de código paga.
3. O ícone da Projetta aparece perto do relógio. O programa passa a abrir junto com o Windows (dá para desligar clicando com o botão direito no ícone).
4. Na página, clica em **Procurar certificados de novo**. Se o navegador pedir acesso à rede local, clicar em **Permitir**.
5. Escolhe o certificado e clica em **Assinar documento**.
6. O Projetta Assinador pergunta se pode assinar. Com token ou cartão, o Windows pede o PIN.

## Segurança

- O programa só responde ao seu site (`https://SEU-USUARIO.github.io`). Outros sites são recusados.
- Cada assinatura pede confirmação na tela do computador, mostrando o site e o certificado.
- A chave privada nunca sai do Windows nem do token.

## Mudanças futuras

Qualquer arquivo alterado no repositório faz a automação rodar de novo e atualizar o site e o programa.

- Política de assinatura AD-RB da ICP-Brasil: preencher a constante `POLITICA` em `site/index.html` com os valores oficiais do ITI.
- Para autorizar outro endereço de site no programa sem recompilar, crie o arquivo `%APPDATA%\ProjettaAssinador\origens.txt` no computador do cliente, com um endereço por linha.
