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
  - id: identidade
    label: 2. Identidade e acesso
  - id: licenciamento
    label: 3. Licenciamento
  - id: atualizacoes
    label: 4. Atualizações
  - id: certificados
    label: 5. Certificados
  - id: implantacao
    label: 6. Checklist
  - id: vulnerabilidade
    label: 7. Relatar falha
---

<div class="summary-box" markdown="1">
## Princípio central

Segredos que podem conceder confiança global não devem estar no software distribuído. O ProxyEdu usa separação de responsabilidades, chaves públicas de verificação e controles locais para reduzir a superfície de exposição.
</div>

## 1. Modelo de segurança
{: #modelo }

O ProxyEdu combina controles no Client, no Server e no processo de distribuição. Nenhuma camada isolada elimina todos os riscos: a proteção final também depende do Windows, da segmentação da rede, das credenciais e da operação da instituição.

Os dados operacionais permanecem localmente por padrão. Serviços externos recebem somente o necessário para funções específicas, como validação comercial e consulta de releases.

## 2. Identidade e controle de acesso
{: #identidade }

- Clients modernos possuem identidade criptográfica e identificador persistente.
- Nonces de autenticação têm validade curta e ajudam a impedir repetição.
- Senhas administrativas são armazenadas por hash, não em texto simples.
- O painel deve ser acessível somente por operadores autorizados.
- A instalação deve substituir credenciais iniciais e limitar privilégios.

Mudanças de IP, MAC ou hostname não devem ser tratadas como prova suficiente de uma nova identidade.

## 3. Licenciamento separado
{: #licenciamento }

O serviço privado de licenciamento é independente do ProxyEdu Server instalado nas escolas. Documentos de licença usam **RSA-PSS com SHA-256**, incluem `KeyId` para rotação e são vinculados ao `ServerInstallationId`.

A chave privada permanece exclusivamente no ambiente privado de licenciamento ou em mecanismo seguro de gestão de chaves. O ProxyEdu distribuído contém somente as chaves públicas necessárias à verificação.

Licenças inválidas, adulteradas ou vinculadas a outro Server são rejeitadas com fallback seguro.

## 4. Atualizações e procedência
{: #atualizacoes }

Releases oficiais podem incluir:

- instalador e pacotes separados de Client e Server;
- manifesto de atualização;
- hashes SHA-256;
- arquivos de assinatura `.sig`;
- notas com compatibilidade e validação executada.

Baixe somente da [página oficial de Releases](/releases/) ou do [repositório oficial](https://github.com/MarcosBravin/ProxyEdu/releases). Compare hashes quando publicados e não remova as assinaturas dos pacotes.

## 5. Autoridade certificadora local
{: #certificados }

Recursos de proxy HTTPS podem exigir uma autoridade certificadora local. Sua chave privada deve permanecer protegida no Server e o certificado raiz deve ser instalado apenas em equipamentos autorizados e administrados pela instituição.

O plano de desinstalação deve incluir a remoção dessa confiança dos endpoints. Nunca publique a chave privada, inclua-a em chamados ou copie-a para repositórios.

## 6. Checklist de implantação
{: #implantacao }

- [ ] Atualizar Windows, ProxyEdu e dependências.
- [ ] Restringir as portas do Server por firewall ou VLAN.
- [ ] Criar credenciais administrativas individuais e fortes.
- [ ] Proteger o diretório de dados com ACLs e criptografia de disco.
- [ ] Criptografar backups e testar restauração.
- [ ] Limitar a instalação da CA aos dispositivos autorizados.
- [ ] Revisar retenção, logs e acessos administrativos.
- [ ] Monitorar alertas, uso de recursos e falhas de atualização.

## 7. Relatar uma vulnerabilidade
{: #vulnerabilidade }

Não publique detalhes exploráveis em issues abertas. Use o canal privado de [GitHub Security Advisories](https://github.com/MarcosBravin/ProxyEdu/security/advisories/new) e não envie dados reais de alunos, credenciais, bancos de produção, certificados ou chaves privadas.

<div class="warning-box" markdown="1">
**Transparência:** segurança é um processo contínuo. Esta página descreve controles do produto, não uma promessa de risco zero nem certificação automática de conformidade.
</div>
