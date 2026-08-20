---
layout: page
permalink: /seguranca/
section: seguranca
title: Segurança e confiança
description: Conheça os controles de identidade, atualização, licenciamento e proteção operacional do ProxyEdu.
eyebrow: SEGURANÇA / INTEGRIDADE / RESPONSABILIDADE
intro: Controles técnicos verificáveis, responsabilidades explícitas e orientações para uma implantação escolar mais segura.
meta_title: Defesa em camadas
meta:
  - Assinaturas assimétricas
  - Identidade persistente
  - Operação local protegida
toc:
  - id: modelo
    label: 1. Modelo de segurança
  - id: funcionamento
    label: 2. Como funciona
  - id: https
    label: 3. Tratamento de HTTPS
  - id: client
    label: 4. O Client no computador
  - id: identidade
    label: 5. Identidade e acesso
  - id: licenciamento
    label: 6. Licenciamento
  - id: atualizacoes
    label: 7. Atualizações
  - id: certificados
    label: 8. Certificados
  - id: implantacao
    label: 9. Checklist
  - id: vulnerabilidade
    label: 10. Relatar falha
---

<div class="summary-box" markdown="1">
## Princípio central

Segredos que podem conceder confiança global não devem estar no software distribuído. O ProxyEdu usa separação de responsabilidades, chaves públicas de verificação e controles locais para reduzir a superfície de exposição.
</div>

## 1. Modelo de segurança
{: #modelo }

O ProxyEdu combina controles no Client, no Server e no processo de distribuição. Nenhuma camada isolada elimina todos os riscos: a proteção final também depende do Windows, da segmentação da rede, das credenciais e da operação da instituição.

Os dados operacionais permanecem localmente por padrão. Serviços externos recebem somente o necessário para funções específicas, como validação comercial e consulta de releases.

## 2. Como funciona
{: #funcionamento }

O ProxyEdu é instalado na infraestrutura administrada pela instituição e possui três funções principais:

1. O **Dashboard** permite que operadores autorizados configurem grupos, listas e políticas.
2. O **Server** mantém as configurações e os registros locais, coordena a comunicação e executa o proxy da rede.
3. O **Client**, instalado nas estações autorizadas, identifica a instalação, envia seu estado operacional e aplica a política recebida do Server.

O Server é executado como serviço do Windows. Na configuração padrão, o Dashboard e a API usam a porta `5000`, a descoberta local usa a porta `50505` e o proxy usa a porta `8888`. A instituição deve confirmar a configuração efetiva antes de criar regras de firewall.

O Client e o Server se comunicam dentro da rede definida pela instituição. O Dashboard não deve ser exposto diretamente à internet. A indisponibilidade de serviços externos não deve ser tratada como substituta da configuração local: licenciamento e atualização possuem fluxos próprios, enquanto a operação da rede depende do Server e da política instalada.

## 3. Tratamento de HTTPS
{: #https }

O ProxyEdu utiliza um proxy explícito. Para HTTPS, o modo de operação atual pode estabelecer uma sessão TLS intermediada pelo Server: o Server usa uma autoridade certificadora local do ProxyEdu e gera certificados de sessão para os destinos autorizados. A CA precisa ser confiada somente nos equipamentos administrados pela instituição.

Na abertura de uma conexão HTTPS, o domínio pode ser avaliado antes da criação do túnel. Depois que a CA local é confiada e a sessão é intermediada, o proxy pode processar a requisição HTTPS para aplicar a política e registrar a atividade operacional configurada. Portanto, a instituição deve considerar que esse modo **não é um túnel CONNECT opaco**: ele pode permitir inspeção do tráfego HTTPS conforme a configuração do produto e da rede.

Esta página não afirma que conteúdo de páginas, senhas ou cookies sejam armazenados. A instituição deve validar o comportamento da release em ambiente de teste, revisar os registros produzidos e definir finalidade, acesso e retenção antes de habilitar a intermediação HTTPS em produção.

## 4. O Client no computador
{: #client }

O Client é instalado como serviço do Windows e possui funções de proteção do serviço, monitoramento de conectividade, aplicação das configurações de proxy, heartbeat e atualização autorizada. Ele não deve ser instalado em equipamentos fora do controle da instituição.

Dependendo da configuração e do fluxo utilizado, o Server pode tratar dados operacionais como nome ou identificação da estação, endereço de rede, hostname, MAC, grupo, versão do Client, estado de conexão, URL ou domínio observado, contagens de requisições, bloqueios, volume transferido e horários. Esses dados permanecem sujeitos à finalidade, ao acesso administrativo e à retenção definidos pela instituição.

Antes de uma distribuição ampla, faça um piloto e verifique: serviços criados, alterações de proxy, certificado local, arquivos de configuração, conexões de rede, comportamento sem o Server e remoção do Client. Não copie logs reais, certificados ou dados de estações para issues, discussões ou repositórios públicos.

## 5. Identidade e controle de acesso
{: #identidade }

- Clients modernos possuem identidade criptográfica e identificador persistente.
- Nonces de autenticação têm validade curta e ajudam a impedir repetição.
- Senhas administrativas são armazenadas por hash, não em texto simples.
- O painel deve ser acessível somente por operadores autorizados.
- A instalação deve substituir credenciais iniciais e limitar privilégios.

Mudanças de IP, MAC ou hostname não devem ser tratadas como prova suficiente de uma nova identidade.

## 6. Licenciamento separado
{: #licenciamento }

O serviço privado de licenciamento é independente do ProxyEdu Server instalado nas escolas. Documentos de licença usam **RSA-PSS com SHA-256**, incluem `KeyId` para rotação e são vinculados ao `ServerInstallationId`.

A chave privada permanece exclusivamente no ambiente privado de licenciamento ou em mecanismo seguro de gestão de chaves. O ProxyEdu distribuído contém somente as chaves públicas necessárias à verificação.

Licenças inválidas, adulteradas ou vinculadas a outro Server são rejeitadas com fallback seguro.

## 7. Atualizações e procedência
{: #atualizacoes }

Releases oficiais podem incluir:

- instalador e pacotes separados de Client e Server;
- manifesto de atualização;
- hashes SHA-256;
- arquivos de assinatura `.sig`;
- notas com compatibilidade e validação executada.

Baixe somente da [página oficial de Releases](/releases/) ou do [repositório oficial](https://github.com/MarcosBravin/ProxyEdu/releases). Compare hashes quando publicados e não remova as assinaturas dos pacotes.

## 8. Autoridade certificadora local
{: #certificados }

Recursos de proxy HTTPS podem exigir uma autoridade certificadora local. Sua chave privada deve permanecer protegida no Server e o certificado raiz deve ser instalado apenas em equipamentos autorizados e administrados pela instituição.

O plano de desinstalação deve incluir a remoção dessa confiança dos endpoints. Nunca publique a chave privada, inclua-a em chamados ou copie-a para repositórios.

## 9. Checklist de implantação
{: #implantacao }

- [ ] Atualizar Windows, ProxyEdu e dependências.
- [ ] Restringir as portas do Server por firewall ou VLAN.
- [ ] Criar credenciais administrativas individuais e fortes.
- [ ] Proteger o diretório de dados com ACLs e criptografia de disco.
- [ ] Criptografar backups e testar restauração.
- [ ] Limitar a instalação da CA aos dispositivos autorizados.
- [ ] Revisar retenção, logs e acessos administrativos.
- [ ] Monitorar alertas, uso de recursos e falhas de atualização.

## 10. Relatar uma vulnerabilidade
{: #vulnerabilidade }

Não publique detalhes exploráveis em issues abertas. Use o canal privado de [GitHub Security Advisories](https://github.com/MarcosBravin/ProxyEdu/security/advisories/new) e não envie dados reais de alunos, credenciais, bancos de produção, certificados ou chaves privadas.

<div class="warning-box" markdown="1">
**Transparência:** segurança é um processo contínuo. Esta página descreve controles do produto, não uma promessa de risco zero nem certificação automática de conformidade.
</div>
