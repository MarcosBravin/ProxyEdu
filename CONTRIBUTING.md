# Como contribuir com o ProxyEdu

Obrigado pelo interesse em contribuir. Alterações de código, documentação, testes e relatos bem detalhados ajudam a tornar o ProxyEdu mais confiável para ambientes educacionais.

Ao contribuir, você concorda com o [Código de Conduta](CODE_OF_CONDUCT.md) e declara possuir os direitos necessários sobre o material enviado. As contribuições aceitas permanecem sujeitas à [licença do projeto](LICENSE).

## Antes de começar

1. Pesquise nas issues para evitar duplicidade.
2. Para correções pequenas, abra uma issue ou pull request com contexto suficiente.
3. Para mudanças amplas, proponha primeiro a solução em uma issue.
4. Vulnerabilidades devem seguir o processo privado de [SECURITY.md](SECURITY.md).

Não inclua senhas, tokens, certificados privados, bancos de dados reais, IPs públicos ou dados de alunos nos exemplos, testes, logs e capturas de tela.

## Ambiente de desenvolvimento

Requisitos principais:

- Windows 10 ou superior;
- SDK do .NET 8;
- Git;
- NSIS apenas para alterações no instalador.

Clone o repositório e restaure as dependências:

```powershell
git clone https://github.com/MarcosBravin/ProxyEdu.git
Set-Location ProxyEdu
dotnet restore .\ProxyEdu.sln
```

Compile e execute os testes:

```powershell
dotnet build .\ProxyEdu.sln
dotnet test .\ProxyEdu.Tests\ProxyEdu.Tests.csproj
```

Alguns recursos, como configuração do proxy, certificados e serviços do Windows, exigem ambiente Windows e podem precisar de privilégios administrativos.

## Fluxo recomendado

1. Crie uma branch a partir de `main`:

   ```powershell
   git switch -c feat/nome-curto
   ```

2. Faça alterações pequenas e focadas.
3. Adicione ou atualize testes quando o comportamento mudar.
4. Verifique se a solução compila e se os testes passam.
5. Revise o diff para remover arquivos gerados, dados sensíveis e mudanças não relacionadas.
6. Crie commits claros e abra um pull request.

## Padrão de commits

Prefira mensagens curtas no formato Conventional Commits:

- `feat: adiciona filtro por grupo`
- `fix: preserva bloqueio após expiração`
- `docs: atualiza instruções de instalação`
- `test: cobre normalização de IP`
- `refactor: centraliza regra de acesso`

## Diretrizes de implementação

- Preserve compatibilidade com instalações existentes sempre que possível.
- Não altere versões, instaladores ou notas de lançamento sem relação com a mudança.
- Mantenha servidor, cliente e modelos compartilhados sincronizados quando contratos mudarem.
- Trate entradas externas como não confiáveis e evite registrar informações sensíveis.
- Respeite o estado administrativo dos alunos em rotinas concorrentes e tarefas em segundo plano.
- Mantenha a interface responsiva e acessível por teclado.
- Evite dependências novas sem justificar necessidade, impacto e licença.

## Pull requests

Um bom pull request deve:

- resolver um problema bem definido;
- explicar o comportamento anterior e o novo;
- informar como a alteração foi validada;
- incluir capturas de tela quando houver mudança visual;
- destacar riscos, migrações ou incompatibilidades;
- não misturar refatorações ou formatações sem relação com o objetivo.

O envio de um pull request não garante sua incorporação. A manutenção pode solicitar ajustes para segurança, compatibilidade, escopo ou alinhamento com o projeto.
