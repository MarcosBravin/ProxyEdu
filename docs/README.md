# Site do ProxyEdu

Site estático institucional, técnico e jurídico do projeto ProxyEdu para publicação no GitHub Pages.

## Estrutura

- `index.html` — página institucional com demonstração interativa dos cenários de aula.
- `privacidade.html` — inventário de dados, fluxo, retenção, segurança e direitos.
- `lgpd.html` — roteiro de implantação responsável e referências oficiais.
- `termos.html` — resumo acessível das condições da Community Edition.
- `cookies.html` — política específica do site público.
- `style.css` — layout e responsividade.
- `script.js` — menu mobile, cenários interativos, acessibilidade das tabelas e atualização opcional da release pela API pública do GitHub.
- `assets/logo.svg` e `assets/favicon.svg` — identidade visual de rede do ProxyEdu.
- `robots.txt` e `sitemap.xml` — descoberta pelos mecanismos de busca.

## Releases

A página inicial consulta somente a release estável mais recente pela API pública do GitHub. Enquanto não houver uma release pública, ou se a API estiver indisponível, a página informa que o catálogo está em preparação. As páginas jurídicas não fazem essa consulta.

## Conteúdo jurídico

O conteúdo foi adaptado à arquitetura local do ProxyEdu e ao comportamento verificável da versão documentada. Ele oferece informação geral e um modelo de transparência, mas cada instituição deve definir controlador, finalidade, hipótese legal, retenção, contato e demais particularidades da própria implantação.

## Publicação

Coloque estes arquivos na pasta usada pelo GitHub Pages (`docs`, no caso atual) e faça commit/push para a branch publicada.
