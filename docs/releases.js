(() => {
  const api = 'https://api.github.com/repos/MarcosBravin/ProxyEdu/releases/latest';
  const trusted = (value) => {
    try {
      const url = new URL(value);
      return url.protocol === 'https:' && (url.hostname === 'github.com' || url.hostname.endsWith('.githubusercontent.com'));
    } catch {
      return false;
    }
  };
  const byId = (id) => document.getElementById(id);
  const status = byId('release-api-status');

  fetch(api, { headers: { Accept: 'application/vnd.github+json' } })
    .then((response) => {
      if (!response.ok) throw new Error('GitHub API indisponível');
      return response.json();
    })
    .then((release) => {
      if (!release || release.draft || release.prerelease || !trusted(release.html_url)) return;
      const version = String(release.tag_name || '').trim();
      if (!/^v\d{4}\.\d+\.\d+\.\d+$/.test(version)) return;
      byId('release-version').textContent = version;
      byId('release-github').href = release.html_url;
      const setup = (release.assets || []).find((asset) => /^ProxyEdu-Setup-v.*\.exe$/i.test(asset.name) && trusted(asset.browser_download_url));
      if (setup) byId('release-setup').href = setup.browser_download_url;
      if (status) {
        const date = release.published_at ? new Intl.DateTimeFormat('pt-BR', { dateStyle: 'long' }).format(new Date(release.published_at)) : '';
        status.textContent = `Release oficial confirmada pela API do GitHub${date ? ` · publicada em ${date}` : ''}.`;
      }
    })
    .catch(() => {
      if (status) status.textContent = 'A consulta ao GitHub está indisponível; exibindo a versão estável registrada nesta página.';
    });
})();
