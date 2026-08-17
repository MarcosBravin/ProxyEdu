(() => {
  const byId = (id) => document.getElementById(id);
  const setText = (id, value) => {
    const element = byId(id);
    if (element) element.textContent = value;
  };
  const setHref = (id, value) => {
    const element = byId(id);
    if (element && isTrustedGitHubUrl(value)) element.href = value;
  };
  const isTrustedGitHubUrl = (value) => {
    try {
      const url = new URL(value);
      return url.protocol === 'https:' && (url.hostname === 'github.com' || url.hostname.endsWith('.github.com'));
    } catch {
      return false;
    }
  };

  const nav = document.querySelector('.main-nav');
  const menu = document.querySelector('.menu-button');
  const setMenu = (open) => {
    if (!menu || !nav) return;
    nav.classList.toggle('open', open);
    menu.setAttribute('aria-expanded', String(open));
    menu.setAttribute('aria-label', open ? 'Fechar menu' : 'Abrir menu');
    menu.textContent = open ? 'Fechar' : 'Menu';
  };

  if (menu && nav) {
    menu.addEventListener('click', () => setMenu(!nav.classList.contains('open')));
    nav.querySelectorAll('a').forEach((link) => link.addEventListener('click', () => setMenu(false)));
    document.addEventListener('keydown', (event) => {
      if (event.key === 'Escape' && nav.classList.contains('open')) {
        setMenu(false);
        menu.focus();
      }
    });
    window.addEventListener('resize', () => {
      if (window.innerWidth > 920) setMenu(false);
    });
  }

  document.querySelectorAll('.policy-table-wrap').forEach((wrapper, index) => {
    const hint = document.createElement('p');
    hint.className = 'table-scroll-hint';
    hint.id = `table-scroll-hint-${index + 1}`;
    hint.textContent = 'Role horizontalmente para ver todas as colunas.';
    wrapper.before(hint);
    wrapper.tabIndex = 0;
    wrapper.setAttribute('role', 'region');
    wrapper.setAttribute('aria-describedby', hint.id);
    const sectionTitle = wrapper.closest('section')?.querySelector('h2')?.textContent?.trim();
    wrapper.setAttribute('aria-label', sectionTitle ? `Tabela: ${sectionTitle}` : `Tabela rolável ${index + 1}`);
  });

  document.querySelectorAll('a[target="_blank"]').forEach((link) => {
    if (!link.textContent.includes('↗')) link.classList.add('external-link-unmarked');
    const accessibleText = link.textContent.replace(/↗/g, '').replace(/\s+/g, ' ').trim();
    if (!link.getAttribute('aria-label')) link.setAttribute('aria-label', `${accessibleText} — abre em nova guia`);
  });

  const SCENARIOS = {
    pesquisa: {
      title: 'Pesquisa orientada',
      description: 'Fontes acadêmicas disponíveis; distrações suspensas durante a atividade.',
      states: ['allowed','allowed','allowed','allowed','allowed','allowed','allowed','offline','allowed','blocked','allowed','offline'],
      online: 10,
      policies: 3,
      primary: ['LIBERADO', 'policy-allow'],
      social: ['BLOQUEADO', 'policy-block'],
      stream: ['SOB REGRA', 'policy-limit']
    },
    avaliacao: {
      title: 'Avaliação em andamento',
      description: 'Somente a plataforma da avaliação e os serviços essenciais permanecem disponíveis.',
      states: ['allowed','allowed','allowed','allowed','allowed','blocked','allowed','allowed','offline','allowed','allowed','allowed'],
      online: 11,
      policies: 5,
      primary: ['SÓ A AVALIAÇÃO', 'policy-limit'],
      social: ['BLOQUEADO', 'policy-block'],
      stream: ['BLOQUEADO', 'policy-block']
    },
    livre: {
      title: 'Laboratório livre',
      description: 'Acesso amplo com a política institucional de segurança ainda ativa.',
      states: ['allowed','allowed','allowed','allowed','allowed','allowed','allowed','allowed','allowed','offline','allowed','allowed'],
      online: 11,
      policies: 1,
      primary: ['LIBERADO', 'policy-allow'],
      social: ['LIBERADO', 'policy-allow'],
      stream: ['LIBERADO', 'policy-allow']
    }
  };

  const scenarioButtons = [...document.querySelectorAll('[data-scenario]')];
  const workstationNodes = [...document.querySelectorAll('.workstation')];
  const applyPolicy = (id, [label, className]) => {
    const element = byId(id);
    if (!element) return;
    element.textContent = label;
    element.className = className;
  };
  const applyScenario = (scenarioName, moveFocus = false) => {
    const scenario = SCENARIOS[scenarioName];
    if (!scenario) return;
    setText('scenario-title', scenario.title);
    setText('scenario-description', scenario.description);
    setText('station-online', scenario.online);
    setText('station-policy', scenario.policies);
    applyPolicy('policy-primary', scenario.primary);
    applyPolicy('policy-social', scenario.social);
    applyPolicy('policy-stream', scenario.stream);
    workstationNodes.forEach((node, index) => {
      const state = scenario.states[index] || 'offline';
      node.className = `workstation ${state}`;
      node.setAttribute('aria-label', `Estação ${node.textContent.trim()}: ${state === 'allowed' ? 'online e liberada' : state === 'blocked' ? 'online com restrição' : 'offline'}`);
    });
    scenarioButtons.forEach((button) => {
      const selected = button.dataset.scenario === scenarioName;
      button.setAttribute('aria-selected', String(selected));
      button.tabIndex = selected ? 0 : -1;
      if (selected) {
        byId('scenario-panel')?.setAttribute('aria-labelledby', button.id);
        if (moveFocus) button.focus();
      }
    });
  };

  scenarioButtons.forEach((button, index) => {
    button.addEventListener('click', () => applyScenario(button.dataset.scenario));
    button.addEventListener('keydown', (event) => {
      if (!['ArrowLeft','ArrowRight','Home','End'].includes(event.key)) return;
      event.preventDefault();
      let targetIndex = index;
      if (event.key === 'ArrowLeft') targetIndex = (index - 1 + scenarioButtons.length) % scenarioButtons.length;
      if (event.key === 'ArrowRight') targetIndex = (index + 1) % scenarioButtons.length;
      if (event.key === 'Home') targetIndex = 0;
      if (event.key === 'End') targetIndex = scenarioButtons.length - 1;
      applyScenario(scenarioButtons[targetIndex].dataset.scenario, true);
    });
  });
  if (scenarioButtons.length) applyScenario('pesquisa');

  const sectionLinks = [...document.querySelectorAll('.home-page .main-nav a[href^="#"]')];
  if ('IntersectionObserver' in window && sectionLinks.length) {
    const sections = sectionLinks.map((link) => document.querySelector(link.getAttribute('href'))).filter(Boolean);
    const observer = new IntersectionObserver((entries) => {
      const visible = entries.filter((entry) => entry.isIntersecting).sort((a, b) => b.intersectionRatio - a.intersectionRatio)[0];
      if (!visible) return;
      sectionLinks.forEach((link) => {
        if (link.getAttribute('href') === `#${visible.target.id}`) link.setAttribute('aria-current', 'location');
        else link.removeAttribute('aria-current');
      });
    }, { rootMargin: '-20% 0px -65%', threshold: [0, .25, .6] });
    sections.forEach((section) => observer.observe(section));
  }

  const releaseWidget = byId('release-title');
  if (!releaseWidget) return;

  const API = 'https://api.github.com/repos/MarcosBravin/ProxyEdu/releases/latest';
  const cleanVersion = (tag = '') => tag.startsWith('v') ? tag : `v${tag}`;
  const formatDate = (iso) => {
    if (!iso) return '';
    return new Intl.DateTimeFormat('pt-BR', { day: '2-digit', month: 'short', year: 'numeric' })
      .format(new Date(iso)).replace('.', '').toUpperCase();
  };
  const stripMarkdown = (value = '') => value
    .replace(/!\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/\[([^\]]+)\]\([^)]*\)/g, '$1')
    .replace(/<[^>]+>/g, ' ')
    .replace(/[*_`~]/g, '')
    .replace(/\s+/g, ' ')
    .trim();
  const extractSummary = (body = '') => {
    const lines = body.split(/\r?\n/).map((line) => line.trim());
    const start = lines.findIndex((line) => /^##\s+Visão geral/i.test(line));
    const pool = start >= 0 ? lines.slice(start + 1) : lines;
    const summary = pool.find((line) => line && !line.startsWith('#') && !line.startsWith('-') && !line.startsWith('```'));
    return stripMarkdown(summary) || 'Consulte as notas completas desta versão no GitHub.';
  };
  const extractTests = (body = '') => body.match(/(\d+)\s+testes (?:automatizados )?aprovados/i)?.[1] || '';

  fetch(API, { headers: { Accept: 'application/vnd.github+json' } })
    .then((response) => {
      if (!response.ok) throw new Error(`GitHub API ${response.status}`);
      return response.json();
    })
    .then((release) => {
      if (!release || release.draft || release.prerelease) return;
      const version = cleanVersion(release.tag_name || release.name || '');
      const summary = extractSummary(release.body || '');
      const tests = extractTests(release.body || '');
      setText('hero-version', version);
      setText('release-summary', summary);
      setText('footer-version', `Release atual: ${version}`);
      setText('release-date', formatDate(release.published_at || release.created_at));
      if (tests) setText('release-tests', `${tests} testes aprovados`);
      setHref('release-link', release.html_url);
      const setup = (release.assets || []).find((asset) => /ProxyEdu-Setup-.*\.exe$/i.test(asset.name));
      const download = byId('latest-download');
      if (download) {
        const targetUrl = setup?.browser_download_url || release.html_url;
        if (isTrustedGitHubUrl(targetUrl)) download.href = targetUrl;
        const downloadText = setup ? `Baixar ${version}` : 'Abrir versão estável';
        download.textContent = downloadText;
        download.setAttribute('aria-label', `${downloadText} — abre em nova guia`);
      }
      setText('release-source', 'Fonte: GitHub Releases · dados atualizados automaticamente.');
    })
    .catch(() => setText('release-source', 'Nenhuma release pública está disponível no momento.'));
})();
