# Política de Segurança

A segurança é especialmente importante no ProxyEdu porque o sistema administra tráfego de rede, certificados, contas, regras de acesso e informações operacionais de ambientes educacionais.

## Versões suportadas

A versão estável mais recente recebe correções de segurança. Versões anteriores podem deixar de receber atualizações após a publicação de uma nova versão.

| Versão | Suporte |
|---|---|
| Release estável mais recente | Suportada |
| Versões anteriores | Podem deixar de receber correções |

## Como relatar uma vulnerabilidade

Não abra uma issue pública para vulnerabilidades ainda não corrigidas.

Utilize o recurso privado [GitHub Security Advisories](https://github.com/MarcosBravin/ProxyEdu/security/advisories/new) e informe:

- componente e versão afetados;
- descrição do impacto;
- condições necessárias para exploração;
- passos mínimos para reproduzir;
- evidências ou prova de conceito segura;
- possíveis formas de mitigação;
- dados de contato para acompanhamento, se desejar.

Não envie dados reais de alunos, credenciais, chaves privadas, certificados institucionais ou bancos de dados de produção. Use informações fictícias e remova dados sensíveis de logs e capturas.

## Processo de análise

Após o recebimento, o mantenedor buscará:

1. confirmar o relato e solicitar informações adicionais, quando necessário;
2. avaliar impacto, alcance e versões afetadas;
3. preparar uma correção ou mitigação;
4. coordenar a divulgação depois que uma solução estiver disponível.

Os prazos dependem da complexidade, gravidade e disponibilidade do projeto. Evite divulgar detalhes técnicos antes da correção ou da autorização do mantenedor.

## Escopo prioritário

São especialmente relevantes relatos envolvendo:

- autenticação ou autorização do painel e da API;
- exposição de credenciais, dados pessoais ou registros de navegação;
- execução remota de código ou comandos;
- instalação, confiança ou remoção de certificados;
- interceptação ou desvio não autorizado de tráfego;
- atualização de software e validação de integridade;
- elevação de privilégios em serviços do Windows;
- bypass não autorizado de bloqueios e filtros.

Erros de configuração, indisponibilidade de serviços de terceiros e problemas sem impacto de segurança devem ser enviados pelo template normal de bug.

## Boas práticas para implantação

- Altere imediatamente credenciais padrão.
- Restrinja o painel administrativo e as portas do servidor à rede autorizada.
- Instale certificados somente em equipamentos autorizados.
- Proteja backups, bancos de dados e arquivos de configuração.
- Mantenha servidor, cliente e sistema operacional atualizados.
- Informe os usuários e cumpra as políticas institucionais e a legislação aplicável.
