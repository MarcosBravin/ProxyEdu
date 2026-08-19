---
layout: page
permalink: /releases/
section: releases
release_script: true
title: Releases e downloads
description: Downloads oficiais, notas de versão, assinaturas e hashes SHA-256 do ProxyEdu.
eyebrow: DISTRIBUIÇÃO OFICIAL / CANAL STABLE
intro: Encontre o instalador, os pacotes de atualização e as evidências de integridade em uma única página.
meta_title: Versão atual
meta:
  - v2026.2.0.2
  - Publicada em 18 de agosto de 2026
  - Canal Stable
toc:
  - id: versao-atual
    label: 1. Versão atual
  - id: downloads
    label: 2. Downloads
  - id: integridade
    label: 3. Integridade
  - id: melhorias
    label: 4. Melhorias
  - id: compatibilidade
    label: 5. Compatibilidade
  - id: historico
    label: 6. Histórico
---

<div class="release-dashboard" id="versao-atual">
  <div class="release-dashboard-main">
    <span class="release-channel">STABLE · ATUALIZAÇÃO CORRETIVA</span>
    <h2 id="release-version">v2026.2.0.2</h2>
    <p>Desempenho e estabilidade para ambientes com muitos computadores, preservando filtros, heartbeat, seats, operação offline e AutoUpdate.</p>
    <div class="release-actions">
      <a id="release-setup" class="primary-action" href="https://github.com/MarcosBravin/ProxyEdu/releases/download/v2026.2.0.2/ProxyEdu-Setup-v2026.2.0.2.exe">Baixar instalador</a>
      <a id="release-github" class="secondary-action on-dark" href="https://github.com/MarcosBravin/ProxyEdu/releases/tag/v2026.2.0.2">Ver no GitHub ↗</a>
    </div>
    <p class="api-note" id="release-api-status" aria-live="polite">Dados conferidos com a release oficial.</p>
  </div>
  <div class="release-metrics" aria-label="Evidências da versão">
    <div><strong>190</strong><span>testes aprovados</span></div>
    <div><strong>50</strong><span>Clients no cenário de carga</span></div>
    <div><strong>0</strong><span>falhas ou testes ignorados</span></div>
  </div>
</div>

## 2. Downloads oficiais
{: #downloads }

Use o instalador para uma instalação completa. Os pacotes separados atendem ao AutoUpdate e a cenários administrativos específicos.

<div class="release-download-grid" id="release-assets">
  <a href="https://github.com/MarcosBravin/ProxyEdu/releases/download/v2026.2.0.2/ProxyEdu-Setup-v2026.2.0.2.exe"><strong>Instalador Windows</strong><span>ProxyEdu-Setup-v2026.2.0.2.exe · 61,2 MB</span></a>
  <a href="https://github.com/MarcosBravin/ProxyEdu/releases/download/v2026.2.0.2/ProxyEdu.Client-v2026.2.0.2.zip"><strong>ProxyEdu Client</strong><span>Pacote ZIP + assinatura separada</span></a>
  <a href="https://github.com/MarcosBravin/ProxyEdu/releases/download/v2026.2.0.2/ProxyEdu.Server-v2026.2.0.2.zip"><strong>ProxyEdu Server</strong><span>Pacote ZIP + assinatura separada</span></a>
  <a href="https://github.com/MarcosBravin/ProxyEdu/releases/download/v2026.2.0.2/update-manifest.json"><strong>Manifesto</strong><span>update-manifest.json</span></a>
</div>

## 3. Integridade — SHA-256
{: #integridade }

Compare o hash do arquivo baixado antes da instalação, especialmente quando ele foi transportado por outro sistema ou mídia.

| Artefato | SHA-256 |
|---|---|
| `ProxyEdu-Setup-v2026.2.0.2.exe` | `8987E18C11E80ACB97A7101C8EF62C56CC4058EEFA2C601BD8A90DD6F8B8BA7A` |
| `ProxyEdu.Client-v2026.2.0.2.zip` | `2425844360977891816D5004BFC3C9C960F720B544970F9E07495B365A266B89` |
| `ProxyEdu.Client-v2026.2.0.2.sig` | `3B93FAA5518149F4823AE627CAD0BC130BCEC5975DA418D22234F386DBEF3DEE` |
| `ProxyEdu.Server-v2026.2.0.2.zip` | `5389CA0B86F4D9FBEC3799B890CC8C6FFB40DB9F5299E62985FAD5BA2831C615` |
| `ProxyEdu.Server-v2026.2.0.2.sig` | `B332BEC7043C7AC08F4843BE700C4B059910EE79F65D3078F5CCF9DBE32ECDFF` |
| `update-manifest.json` | `C188A025A158270C23BC56C253E1CF84BDE1467C9D1D1E958652BB2D3EDC677D` |

No PowerShell, calcule o hash com:

```powershell
Get-FileHash .\ProxyEdu-Setup-v2026.2.0.2.exe -Algorithm SHA256
```

## 4. Melhorias da v2026.2.0.2
{: #melhorias }

- o tráfego HTTP comum não executa mais observações ou persistências de seat;
- heartbeats repetidos são consolidados antes da persistência;
- consultas de estudantes usam identidade e índices, evitando varreduras completas;
- caches controlados reduzem verificações repetitivas de configuração e licença;
- limpeza de nonces expirados ocorre fora do processamento das requisições;
- logs e estatísticas do dashboard são materializados incrementalmente;
- atualizações SignalR preservam a atividade mais recente dentro da janela de agregação;
- o dashboard reduz consultas e suspende atualizações quando não está visível.

## 5. Compatibilidade e atualização
{: #compatibilidade }

A versão é compatível com atualização direta de **2026.2.0.0** e **2026.2.0.1**. Configurações, dispositivos, seats, auditoria e cache de licença existentes são preservados.

Clients offline continuam ocupando seu seat. Clients Legacy continuam sem identidade permanente baseada somente em IP, MAC ou hostname. A indisponibilidade da API comercial não interrompe startup, proxy ou heartbeat.

<div class="warning-box" markdown="1">
**Antes de atualizar:** mantenha backup da configuração e do diretório de dados, confirme espaço livre e use apenas os artefatos oficiais desta página ou do GitHub.
</div>

## 6. Histórico e procedência
{: #historico }

O histórico completo, incluindo versões anteriores e seus artefatos, permanece na [área oficial de Releases do GitHub](https://github.com/MarcosBravin/ProxyEdu/releases).

Nenhuma chave privada de licenciamento ou credencial administrativa é distribuída nos pacotes. Assinaturas e hashes comprovam integridade e procedência, mas não substituem a proteção do sistema operacional e do canal de distribuição.
