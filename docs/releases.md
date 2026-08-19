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
  - v2026.2.1.0
  - Publicada em 19 de agosto de 2026
  - Canal Stable
toc:
  - id: versao-atual
    label: 1. Versão atual
  - id: downloads
    label: 2. Downloads
  - id: integridade
    label: 3. Integridade
  - id: notas
    label: 4. Notas completas
  - id: historico
    label: 5. Histórico
---

<div class="release-dashboard" id="versao-atual">
  <div class="release-dashboard-main">
    <span class="release-channel">STABLE · SEGURANÇA E EVOLUÇÃO COMERCIAL</span>
    <h2 id="release-version">v2026.2.1.0</h2>
    <p id="release-summary">Instalação administrativa segura, proteção de credenciais, entitlements comerciais, transferência de licenças e atualização funcional do Server.</p>
    <div class="release-actions">
      <a id="release-setup" class="primary-action" href="https://github.com/MarcosBravin/ProxyEdu/releases/download/v2026.2.1.0/ProxyEdu-Setup-v2026.2.1.0.exe">Baixar instalador</a>
      <a id="release-github" class="secondary-action on-dark" href="https://github.com/MarcosBravin/ProxyEdu/releases/tag/v2026.2.1.0">Ver no GitHub ↗</a>
    </div>
    <p class="api-note" id="release-api-status" aria-live="polite">Consultando a release oficial no GitHub…</p>
  </div>
  <div class="release-metrics" aria-label="Evidências da versão">
    <div><strong id="release-test-count">203</strong><span>testes aprovados</span></div>
    <div><strong id="release-asset-count">6</strong><span>artefatos oficiais</span></div>
    <div><strong id="release-failure-count">0</strong><span>falhas ou testes ignorados</span></div>
  </div>
</div>

## 2. Downloads oficiais
{: #downloads }

Use o instalador para uma instalação completa. Os pacotes separados atendem ao AutoUpdate e a cenários administrativos específicos.

<div class="release-download-grid" id="release-assets" aria-live="polite">
  <a href="https://github.com/MarcosBravin/ProxyEdu/releases/download/v2026.2.1.0/ProxyEdu-Setup-v2026.2.1.0.exe"><strong>Instalador Windows</strong><span>ProxyEdu-Setup-v2026.2.1.0.exe · 61,2 MB</span></a>
  <a href="https://github.com/MarcosBravin/ProxyEdu/releases/download/v2026.2.1.0/ProxyEdu.Client-v2026.2.1.0.zip"><strong>ProxyEdu Client</strong><span>ProxyEdu.Client-v2026.2.1.0.zip · 63,7 MB</span></a>
  <a href="https://github.com/MarcosBravin/ProxyEdu/releases/download/v2026.2.1.0/ProxyEdu.Server-v2026.2.1.0.zip"><strong>ProxyEdu Server</strong><span>ProxyEdu.Server-v2026.2.1.0.zip · 54,2 MB</span></a>
  <a href="https://github.com/MarcosBravin/ProxyEdu/releases/download/v2026.2.1.0/update-manifest.json"><strong>Manifesto</strong><span>update-manifest.json · 1,1 KB</span></a>
</div>

## 3. Integridade — SHA-256
{: #integridade }

Compare o hash do arquivo baixado antes da instalação, especialmente quando ele foi transportado por outro sistema ou mídia.

<div class="release-integrity-table" role="region" aria-label="Hashes SHA-256 dos artefatos" tabindex="0">
  <table>
    <thead><tr><th>Artefato</th><th>SHA-256</th></tr></thead>
    <tbody id="release-hashes">
      <tr><td><code>ProxyEdu-Setup-v2026.2.1.0.exe</code></td><td><code>98C85A9C23FC203D18551D1C97C7FFEE1DA7E80480FF79DA3827ADED0E1684BC</code></td></tr>
      <tr><td><code>ProxyEdu.Client-v2026.2.1.0.sig</code></td><td><code>DC4614888003EC885CFEA66C5366B22775CD43CD7D05F4D9A7F7747579EA0F41</code></td></tr>
      <tr><td><code>ProxyEdu.Client-v2026.2.1.0.zip</code></td><td><code>9356E5489A263517CC04DC96A663AF79FFC59896D99CEACC41650EA3E4BC7501</code></td></tr>
      <tr><td><code>ProxyEdu.Server-v2026.2.1.0.sig</code></td><td><code>E90A1D2F8822517B2904086FCC07D26684456958F3FE05A81E282D4D6B7BBB57</code></td></tr>
      <tr><td><code>ProxyEdu.Server-v2026.2.1.0.zip</code></td><td><code>B9560912820F0BB4B79EA8D2CAEAF072DC7E5C201E824A09098A192292E4A2B6</code></td></tr>
      <tr><td><code>update-manifest.json</code></td><td><code>83F51A1FB9B250DD7035F6BCF631C7385ADA3AC7860F26DC31E99EFB3F552AD4</code></td></tr>
    </tbody>
  </table>
</div>

No PowerShell, calcule o hash com:

```powershell
Get-FileHash .\ProxyEdu-Setup-v2026.2.1.0.exe -Algorithm SHA256
```

## 4. Notas completas da release
{: #notas }

O conteúdo abaixo é substituído pela nota Markdown da release mais recente assim que a consulta oficial ao GitHub é concluída.

<div id="release-notes" class="release-notes" aria-live="polite" markdown="1">
### Destaques da v2026.2.1.0

- onboarding local seguro do primeiro administrador;
- proteção contra retenção de credenciais no navegador;
- entitlements comerciais e transferência segura de licenças;
- correção da janela de atualização do ProxyEdu Server.

### Compatibilidade

A versão é compatível com atualização direta de **2026.2.0.0**, **2026.2.0.1** e **2026.2.0.2**. Configurações, usuários, dispositivos, seats, auditoria e cache de licença existentes são preservados.
</div>

<div class="warning-box" markdown="1">
**Antes de atualizar:** mantenha backup da configuração e do diretório de dados, confirme espaço livre e use apenas os artefatos oficiais desta página ou do GitHub.
</div>

## 5. Histórico e procedência
{: #historico }

O histórico completo, incluindo versões anteriores e seus artefatos, permanece na [área oficial de Releases do GitHub](https://github.com/MarcosBravin/ProxyEdu/releases).

Nenhuma chave privada de licenciamento ou credencial administrativa é distribuída nos pacotes. Assinaturas e hashes comprovam integridade e procedência, mas não substituem a proteção do sistema operacional e do canal de distribuição.
