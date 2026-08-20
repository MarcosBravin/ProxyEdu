# ProxyEdu

## Controle responsável da internet em ambientes educacionais

O ProxyEdu é uma plataforma para instituições de ensino que precisam organizar o acesso à internet em laboratórios, salas de aula e outros ambientes autorizados. Ele ajuda a equipe responsável a preparar políticas, acompanhar estações e ajustar o acesso de acordo com a atividade pedagógica.

O produto foi pensado para preservar a operação da instituição: os componentes principais são instalados na infraestrutura administrada pela própria organização, com controle local, políticas configuráveis e atenção à continuidade operacional.

> **Este é o repositório público oficial do ProxyEdu.** Ele reúne o site institucional, a documentação pública, as políticas do projeto e os canais oficiais de distribuição. O código-fonte do produto não é publicado aqui.

## O que o ProxyEdu oferece

- Organização do acesso à internet por contexto de aula, laboratório ou grupo.
- Aplicação de regras de liberação, restrição e exceção conforme a necessidade institucional.
- Acompanhamento operacional das estações autorizadas.
- Administração local, sem exigir que o painel de controle fique exposto à internet.
- Instalação e operação compatíveis com ambientes Windows administrados.
- Distribuição de versões oficiais com informações de release, integridade e atualização.
- Documentação sobre privacidade, LGPD, segurança, cookies, termos e uso responsável.

O ProxyEdu não substitui a política de segurança, a governança de TI ou a decisão pedagógica da instituição. Ele fornece ferramentas para que essas responsabilidades sejam aplicadas de forma mais organizada, verificável e compatível com a rotina escolar.

## Por que este repositório é público

Transparência não significa publicar indiscriminadamente cada detalhe operacional de um sistema que administra rede, certificados, contas, políticas e atualizações. Este repositório torna públicos os materiais que ajudam a avaliar e utilizar o projeto com responsabilidade:

- finalidade e posicionamento do produto;
- documentação de instalação e operação;
- políticas de privacidade, segurança, cookies e LGPD;
- licença e termos aplicáveis;
- releases oficiais e informações de integridade;
- canais para dúvidas, contribuições e relatos de segurança.

O código-fonte do produto é mantido fora deste repositório por decisão de distribuição e proteção do produto. Isso não transforma o projeto em uma solução sem documentação: o comportamento destinado ao usuário, as responsabilidades de implantação e os limites de uso são descritos nas páginas públicas e nos documentos vinculados.

## Transparência técnica

O código-fonte do Server e do Client não está publicado neste repositório. Por isso, os materiais públicos não substituem uma auditoria independente do instalador ou da execução do produto. Para uma avaliação responsável, consulte a [Política de Segurança](SECURITY.md), valide a release oficial e faça a primeira instalação em ambiente piloto autorizado.

A documentação pública descreve finalidade, responsabilidades, atualizações, certificados e cuidados de implantação em nível suficiente para a tomada de decisão, sem publicar credenciais, chaves privadas, dados de produção ou detalhes que possam facilitar acesso não autorizado.

Para entender o fluxo entre Dashboard, Server e Client, o tratamento de HTTPS, os dados operacionais e os cuidados de implantação, consulte a página de [Segurança e confiança](https://proxyedu.bravintech.com/seguranca/). A instituição deve avaliar a release em ambiente piloto antes de instalar em larga escala.

## O que existe neste repositório

Este repositório contém:

- o site institucional publicado em [`docs/`](docs/);
- documentação pública de instalação e uso;
- políticas de privacidade, LGPD, cookies e segurança;
- termos de uso e licença da Community Edition;
- páginas de apresentação, planos, trial e releases;
- instaladores compilados e respectivos dados de integridade, quando publicados em [Releases](https://github.com/MarcosBravin/ProxyEdu/releases).

Este repositório **não** contém o código-fonte interno do produto, credenciais, chaves privadas, bancos de produção, dados de instituições ou informações operacionais confidenciais.

## Instalação pela última release

O caminho recomendado é sempre instalar a **release estável mais recente**. A página de Releases é a fonte oficial do instalador e evita que versões antigas, cópias modificadas ou arquivos de origem desconhecida sejam usados.

1. Abra [Releases](https://github.com/MarcosBravin/ProxyEdu/releases).
2. Entre na release marcada como estável e mais recente.
3. Na seção de arquivos, baixe o instalador oficial `ProxyEdu-Setup-*.exe`.
4. Confira na própria release o nome do arquivo, a assinatura e o SHA-256 publicados.
5. No Windows, abra as propriedades do `.exe` e use a guia de assinatura digital, quando disponível, para verificar o editor e o status da assinatura.
6. Execute o instalador em um computador Windows autorizado, seguindo as instruções exibidas na tela.
7. Faça primeiro um teste em uma estação piloto antes de distribuir o Client para o restante do ambiente.

Para o roteiro completo, consulte [Instalação](INSTALLATION.md). Não instale arquivos recebidos por mensagens, links encurtados, páginas não oficiais ou cópias sem integridade verificável.

## Downloads oficiais

Os instaladores oficiais são publicados somente nas [Releases do repositório](https://github.com/MarcosBravin/ProxyEdu/releases). Baixe o `.exe` diretamente da release estável mais recente e confira os dados de integridade apresentados na página antes da instalação.

Não confie em cópias recebidas por canais não oficiais. Se a assinatura, o hash, o nome do arquivo ou a origem não puderem ser verificados, não execute o instalador e solicite orientação pelos canais oficiais.

## Implantação e uso responsável

O ProxyEdu pode administrar tráfego de rede e tratar informações operacionais ou dados pessoais, dependendo da configuração e do contexto de uso. A instituição deve:

- usar o sistema somente em equipamentos e redes autorizados;
- informar usuários e responsáveis quando aplicável;
- definir finalidade, base legal, retenção e controles de acesso;
- proteger credenciais, certificados, configurações, logs e backups;
- restringir o painel administrativo à rede autorizada;
- evitar a coleta de dados além do necessário;
- manter servidor, clientes e sistema operacional atualizados;
- documentar como restaurar a rede e remover o software quando necessário.

O ProxyEdu não autoriza monitoramento ilegal, interceptação não autorizada ou uso contra pessoas e redes sem permissão.

## Esclarecimentos importantes

### “Se o código-fonte não está aqui, então o projeto não é público.”

Este é um repositório público de distribuição, documentação e transparência institucional. A visibilidade pública do projeto inclui o site, os documentos, as políticas, os termos, os releases e os canais de segurança. A ausência do código-fonte neste repositório é informada de maneira explícita e faz parte do modelo de distribuição adotado.

### “Se o produto opera localmente, ele não é uma plataforma completa.”

Operação local é uma característica de arquitetura e governança, não ausência de recursos. O objetivo é permitir que a instituição mantenha o controle da rede e da administração, reduzindo a dependência de exposição pública e de serviços externos para a rotina do ambiente educacional.

### “Controle de acesso significa bloquear tudo.”

O ProxyEdu foi concebido para adaptar o acesso ao contexto: pesquisa orientada, avaliação, laboratório livre ou outras políticas definidas pela instituição. A decisão deve ser proporcional à finalidade pedagógica e às regras internas.

### “O produto serve apenas para grandes redes.”

A proposta atende desde ambientes pequenos até operações que precisam organizar vários grupos e estações. A adequação depende do cenário, da infraestrutura, da política da instituição e da capacidade de administração disponível.

### “Uma página de preços significa que a contratação é automática.”

Os preços apresentados no site são referências comerciais. Trial, contratação e concessão de licença podem depender de análise e procedimentos próprios. O site informa claramente quando uma funcionalidade comercial ainda não está disponível de forma automática.

### “Uma solução educacional pode ignorar privacidade.”

Não. O uso deve ser limitado à finalidade autorizada, com transparência, controle de acesso, retenção adequada e revisão humana. Consulte as páginas de [Privacidade](https://proxyedu.bravintech.com/privacidade.html) e [LGPD](https://proxyedu.bravintech.com/lgpd.html) antes da implantação.

## Documentação e políticas

- [Instalação](INSTALLATION.md)
- [Uso](USAGE.md)
- [Código de Conduta](CODE_OF_CONDUCT.md)
- [Como contribuir](CONTRIBUTING.md)
- [Política de Segurança](SECURITY.md)
- [Política de Privacidade](https://proxyedu.bravintech.com/privacidade.html)
- [Central LGPD](https://proxyedu.bravintech.com/lgpd.html)
- [Política de Cookies](https://proxyedu.bravintech.com/cookies.html)
- [Termos de Uso](https://proxyedu.bravintech.com/termos.html)
- [Licença](LICENSE)

## Site oficial e canais

- Site: [proxyedu.bravintech.com](https://proxyedu.bravintech.com/)
- Releases: [github.com/MarcosBravin/ProxyEdu/releases](https://github.com/MarcosBravin/ProxyEdu/releases)
- Discussões: [GitHub Discussions](https://github.com/MarcosBravin/ProxyEdu/discussions)
- Vulnerabilidades: [GitHub Security Advisories](https://github.com/MarcosBravin/ProxyEdu/security/advisories/new)

## Participação no projeto

Relatos, sugestões e contribuições devem respeitar o [Código de Conduta](CODE_OF_CONDUCT.md), as orientações de [Contribuição](CONTRIBUTING.md), a [Política de Segurança](SECURITY.md) e a [Licença](LICENSE). O material enviado deve ser original, não conter dados sensíveis e estar relacionado ao conteúdo público deste repositório.

Não publique em issues, discussões ou capturas de tela credenciais, chaves privadas, certificados institucionais, dados de estudantes, endereços internos, logs integrais ou informações de produção.

## Relato de vulnerabilidades

Não abra uma issue pública para uma vulnerabilidade ainda não corrigida. Use [GitHub Security Advisories](https://github.com/MarcosBravin/ProxyEdu/security/advisories/new) e forneça somente os dados necessários para a análise. Consulte a [Política de Segurança](SECURITY.md) para o escopo prioritário e as boas práticas de envio.

## Licença

O ProxyEdu Community Edition é distribuído sob o modelo Open Core, conforme a [LICENSE](LICENSE). O uso educacional, experimental e comunitário deve respeitar os limites da licença. Uso comercial, redistribuição, serviços gerenciados e funcionalidades adicionais podem exigir autorização ou condições específicas.

Leia a licença completa e os [Termos de Uso](https://proxyedu.bravintech.com/termos.html) antes de instalar ou distribuir o software.

---

**ProxyEdu** — gestão responsável da internet em ambientes educacionais.
