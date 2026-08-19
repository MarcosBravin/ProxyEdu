---
layout: page
permalink: /como-funciona/
section: como-funciona
title: Como o ProxyEdu funciona
description: Entenda a arquitetura local, o fluxo de rede e a operação do ProxyEdu em laboratórios e escolas.
eyebrow: PRODUTO / ARQUITETURA / OPERAÇÃO
intro: Do Client instalado em cada computador ao Server que aplica as políticas, acompanhe o caminho completo de uma conexão.
meta_title: Visão técnica
meta:
  - Windows Server e Client
  - Operação local-first
  - Licença assinada em cache
toc:
  - id: visao-geral
    label: 1. Visão geral
  - id: componentes
    label: 2. Componentes
  - id: fluxo
    label: 3. Fluxo de rede
  - id: identidade
    label: 4. Identidade e seats
  - id: continuidade
    label: 5. Continuidade
  - id: implantacao
    label: 6. Implantação
---

<div class="summary-box" markdown="1">
## Em uma frase

O ProxyEdu coloca a instituição no centro da rede: o **Server** administra o acesso, os **Clients** identificam cada estação e o painel permite acompanhar e aplicar políticas sem depender permanentemente de um serviço externo.
</div>

## 1. Visão geral
{: #visao-geral }

O sistema é instalado na infraestrutura administrada pela escola. Um computador Windows atua como Server e recebe as conexões dos Clients instalados nas estações autorizadas. O tráfego configurado passa pelo proxy, que avalia as regras e registra o resultado necessário à operação.

```text
Estações Windows
  └─ ProxyEdu Client
       └─ rede local
            └─ ProxyEdu Server
                 ├─ proxy e políticas
                 ├─ heartbeat e presença
                 ├─ painel administrativo
                 ├─ LiteDB local
                 └─ cache de licença assinada
```

## 2. Componentes
{: #componentes }

### ProxyEdu Client

Serviço Windows instalado em cada estação. Ele mantém uma identidade persistente da instalação, informa presença e versão e participa do fluxo autorizado de atualização.

### ProxyEdu Server

É o núcleo da implantação. Aplica políticas, mantém o proxy, recebe heartbeats, persiste os dados locais e disponibiliza o painel para os administradores autorizados.

### Painel administrativo

Apresenta dispositivos, atividade, grupos, regras, diagnósticos e estado do produto. Seu acesso deve ficar restrito à rede administrativa e a operadores designados.

## 3. Fluxo de uma conexão
{: #fluxo }

1. O Client se registra e autentica no Server local.
2. A estação envia a requisição por meio da configuração de proxy adotada.
3. O Server identifica a estação e consulta as políticas aplicáveis.
4. A solicitação é liberada ou bloqueada.
5. O resultado operacional aparece no painel e pode ser persistido conforme a configuração.

Uma pesquisa, uma avaliação e um momento livre podem usar políticas diferentes. A troca de contexto não exige reinstalar os Clients.

## 4. Identidade e seats
{: #identidade }

Clients modernos são reconhecidos pelo `ClientInstallationId`, não por IP, MAC ou hostname. Por isso:

- reconectar o mesmo Client não consome outro seat;
- mudar IP ou nome do computador não cria uma nova identidade;
- um Client offline continua ocupando seu seat;
- a retirada definitiva pode ser registrada por liberação administrativa;
- Clients Legacy não recebem identidade permanente baseada apenas em dados de rede.

O plano Free permite até **5 dispositivos**. Uma licença válida amplia o limite conforme o plano contratado.

## 5. Continuidade e licenciamento
{: #continuidade }

O documento comercial é assinado fora da escola e verificado pelo Server usando somente chave pública. O estado válido permanece em cache local. Se a API comercial ficar temporariamente indisponível, startup, proxy e heartbeat continuam operando conforme o estado já validado.

Nenhuma chave privada de licenciamento faz parte do produto distribuído.

## 6. Implantação responsável
{: #implantacao }

- reserve uma máquina Windows adequada para o Server;
- restrinja painel, portas e banco à rede autorizada;
- proteja credenciais, certificados e backups;
- teste regras antes de aplicá-las a toda a escola;
- documente finalidade, retenção e responsáveis pelo uso dos registros;
- mantenha Server, Clients e sistema operacional atualizados.

<div class="warning-box" markdown="1">
**O ProxyEdu é uma ferramenta de infraestrutura.** A instituição continua responsável por suas decisões pedagógicas, configuração de rede, base legal, transparência e segurança dos endpoints.
</div>
