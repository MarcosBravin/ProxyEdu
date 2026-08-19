(() => {
  const api = 'https://api.github.com/repos/MarcosBravin/ProxyEdu/releases/latest';
  const byId = (id) => document.getElementById(id);
  const status = byId('release-api-status');

  const trusted = (value) => {
    try {
      const url = new URL(value);
      return url.protocol === 'https:' && (
        url.hostname === 'github.com' ||
        url.hostname === 'api.github.com' ||
        url.hostname.endsWith('.githubusercontent.com')
      );
    } catch {
      return false;
    }
  };
  const formatDate = (value) => value
    ? new Intl.DateTimeFormat('pt-BR', { dateStyle: 'long' }).format(new Date(value))
    : '';
  const formatSize = (bytes = 0) => {
    if (!Number.isFinite(bytes) || bytes <= 0) return 'tamanho não informado';
    const units = ['B', 'KB', 'MB', 'GB'];
    let size = bytes;
    let unit = 0;
    while (size >= 1024 && unit < units.length - 1) { size /= 1024; unit += 1; }
    return `${size.toLocaleString('pt-BR', { maximumFractionDigits: 1 })} ${units[unit]}`;
  };
  const cleanMarkdown = (value = '') => String(value)
    .replace(/!\[[^\]]*\]\([^)]*\)/g, '')
    .replace(/\[([^\]]+)\]\([^)]*\)/g, '$1')
    .replace(/[`*_>#]/g, '')
    .replace(/\s+/g, ' ')
    .trim();
  const overview = (body = '') => {
    const lines = String(body).split(/\r?\n/);
    const position = lines.findIndex((line) => /^##\s+(visão geral|resumo)\s*$/i.test(line.trim()));
    if (position < 0) return '';
    for (const line of lines.slice(position + 1)) {
      const trimmed = line.trim();
      if (/^##\s+/.test(trimmed)) break;
      if (!trimmed || /^[-*]\s+/.test(trimmed)) continue;
      const paragraph = cleanMarkdown(trimmed);
      if (paragraph.length >= 60) return paragraph;
    }
    return '';
  };
  const metadataValue = (body, label) => {
    const match = String(body).match(new RegExp(`\\*\\*${label}:\\*\\*\\s*([^\\r\\n]+)`, 'i'));
    return match ? cleanMarkdown(match[1]) : '';
  };
  const hashFromBody = (body, fileName) => {
    const escaped = fileName.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const match = String(body).match(new RegExp(`${escaped}\\s+([a-f0-9]{64})`, 'i'));
    return match ? match[1].toUpperCase() : '';
  };
  const assetHash = (asset, body) => {
    const digest = String(asset.digest || '').replace(/^sha256:/i, '');
    return /^[a-f0-9]{64}$/i.test(digest) ? digest.toUpperCase() : hashFromBody(body, asset.name);
  };
  const assetLabel = (name) => {
    if (/Setup.*\.exe$/i.test(name)) return 'Instalador Windows';
    if (/Client.*\.zip$/i.test(name)) return 'ProxyEdu Client';
    if (/Server.*\.zip$/i.test(name)) return 'ProxyEdu Server';
    if (/\.sig$/i.test(name)) return 'Assinatura digital';
    if (/manifest\.json$/i.test(name)) return 'Manifesto de atualização';
    return 'Artefato oficial';
  };

  const appendInline = (container, text) => {
    const pattern = /(\*\*[^*]+\*\*|`[^`]+`|\[[^\]]+\]\([^)]+\))/g;
    let cursor = 0;
    for (const match of text.matchAll(pattern)) {
      if (match.index > cursor) container.append(document.createTextNode(text.slice(cursor, match.index)));
      const token = match[0];
      if (token.startsWith('**')) {
        const strong = document.createElement('strong');
        strong.textContent = token.slice(2, -2);
        container.append(strong);
      } else if (token.startsWith('`')) {
        const code = document.createElement('code');
        code.textContent = token.slice(1, -1);
        container.append(code);
      } else {
        const parts = token.match(/^\[([^\]]+)\]\(([^)]+)\)$/);
        if (parts && trusted(parts[2])) {
          const link = document.createElement('a');
          link.textContent = parts[1];
          link.href = parts[2];
          link.target = '_blank';
          link.rel = 'noopener noreferrer';
          container.append(link);
        } else {
          container.append(document.createTextNode(parts?.[1] || token));
        }
      }
      cursor = match.index + token.length;
    }
    if (cursor < text.length) container.append(document.createTextNode(text.slice(cursor)));
  };

  const renderMarkdown = (body) => {
    const target = byId('release-notes');
    if (!target || !body.trim()) return;
    target.replaceChildren();
    const lines = body.split(/\r?\n/);
    let index = /^#\s+/.test(lines[0]?.trim() || '') ? 1 : 0;
    while (index < lines.length) {
      const line = lines[index].trim();
      if (!line) { index += 1; continue; }
      if (line.startsWith('```')) {
        const language = line.slice(3).trim();
        const values = [];
        index += 1;
        while (index < lines.length && !lines[index].trim().startsWith('```')) {
          values.push(lines[index]); index += 1;
        }
        index += 1;
        const pre = document.createElement('pre');
        const code = document.createElement('code');
        if (language) code.dataset.language = language;
        code.textContent = values.join('\n');
        pre.append(code); target.append(pre); continue;
      }
      const heading = line.match(/^(#{1,4})\s+(.+)$/);
      if (heading) {
        const element = document.createElement(`h${Math.min(4, heading[1].length + 1)}`);
        appendInline(element, heading[2]); target.append(element); index += 1; continue;
      }
      if (/^[-*]\s+/.test(line) || /^\d+\.\s+/.test(line)) {
        const ordered = /^\d+\.\s+/.test(line);
        const list = document.createElement(ordered ? 'ol' : 'ul');
        while (index < lines.length) {
          const itemLine = lines[index].trim();
          const item = ordered ? itemLine.match(/^\d+\.\s+(.+)$/) : itemLine.match(/^[-*]\s+(.+)$/);
          if (!item) break;
          const li = document.createElement('li'); appendInline(li, item[1]); list.append(li); index += 1;
        }
        target.append(list); continue;
      }
      if (line.startsWith('>')) {
        const quote = document.createElement('blockquote');
        appendInline(quote, line.replace(/^>\s?/, '')); target.append(quote); index += 1; continue;
      }
      if (/^\*\*[^*]+:\*\*/.test(line)) {
        const meta = document.createElement('p'); meta.className = 'release-note-meta';
        appendInline(meta, line); target.append(meta); index += 1; continue;
      }
      const paragraphLines = [line];
      index += 1;
      while (index < lines.length) {
        const next = lines[index].trim();
        if (!next || /^(#{1,4})\s+|^```|^[-*]\s+|^\d+\.\s+|^>/.test(next)) break;
        paragraphLines.push(next); index += 1;
      }
      const paragraph = document.createElement('p');
      appendInline(paragraph, paragraphLines.join(' ')); target.append(paragraph);
    }
  };

  const renderAssets = (assets, body) => {
    const validAssets = assets.filter((asset) => trusted(asset.browser_download_url));
    const grid = byId('release-assets');
    if (grid && validAssets.length) {
      grid.replaceChildren(...validAssets.map((asset) => {
        const link = document.createElement('a');
        link.href = asset.browser_download_url; link.target = '_blank'; link.rel = 'noopener noreferrer';
        const title = document.createElement('strong'); title.textContent = assetLabel(asset.name);
        const detail = document.createElement('span'); detail.textContent = `${asset.name} · ${formatSize(asset.size)}`;
        link.append(title, detail); return link;
      }));
    }
    const hashes = byId('release-hashes');
    if (hashes && validAssets.length) {
      hashes.replaceChildren(...validAssets.map((asset) => {
        const row = document.createElement('tr');
        const fileCell = document.createElement('td');
        const fileCode = document.createElement('code'); fileCode.textContent = asset.name; fileCell.append(fileCode);
        const hashCell = document.createElement('td');
        const hashCode = document.createElement('code'); hashCode.textContent = assetHash(asset, body) || 'Não informado'; hashCell.append(hashCode);
        row.append(fileCell, hashCell); return row;
      }));
    }
    const setup = validAssets.find((asset) => /^ProxyEdu-Setup-v.*\.exe$/i.test(asset.name));
    if (setup) {
      byId('release-setup').href = setup.browser_download_url;
      const command = document.querySelector('#integridade ~ pre code');
      if (command) command.textContent = `Get-FileHash .\\${setup.name} -Algorithm SHA256`;
    }
    byId('release-asset-count').textContent = String(validAssets.length);
  };

  fetch(api, { headers: { Accept: 'application/vnd.github+json' }, cache: 'no-store' })
    .then((response) => {
      if (!response.ok) throw new Error(`GitHub API ${response.status}`);
      return response.json();
    })
    .then((release) => {
      if (!release || release.draft || release.prerelease || !trusted(release.html_url)) throw new Error('Release inválida');
      const version = String(release.tag_name || '').trim();
      if (!/^v\d{4}\.\d+\.\d+\.\d+$/.test(version)) throw new Error('Versão inválida');
      const body = String(release.body || '');
      const date = formatDate(release.published_at || release.created_at);
      const type = metadataValue(body, 'Tipo');
      const tests = body.match(/\b(\d+)\s+testes(?:\s+automatizados)?\s+aprovados\b/i);
      const failureFree = /sem\s+falhas(?:\s+ou\s+testes\s+ignorados)?/i.test(body) || /0\s+falhas/i.test(body);

      byId('release-version').textContent = version;
      byId('release-github').href = release.html_url;
      const summary = overview(body);
      if (summary) byId('release-summary').textContent = summary;
      if (tests) byId('release-test-count').textContent = tests[1];
      if (failureFree) byId('release-failure-count').textContent = '0';
      const channel = document.querySelector('.release-channel');
      if (channel) channel.textContent = `STABLE${type ? ` · ${type}` : ''}`;
      const metaVersion = byId('release-meta-1'); if (metaVersion) metaVersion.textContent = version;
      const metaDate = byId('release-meta-2'); if (metaDate && date) metaDate.textContent = `Publicada em ${date}`;
      renderAssets(Array.isArray(release.assets) ? release.assets : [], body);
      renderMarkdown(body);
      document.title = `${version} — Releases e downloads — ProxyEdu`;
      if (status) status.textContent = `Release oficial confirmada pela API do GitHub${date ? ` · publicada em ${date}` : ''}.`;
    })
    .catch(() => {
      if (status) status.textContent = 'A consulta ao GitHub está indisponível; exibindo a versão estável registrada nesta página.';
    });
})();
