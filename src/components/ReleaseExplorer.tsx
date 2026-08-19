import { useEffect, useState } from "react";

type Asset = { id:number; name:string; browser_download_url:string; size:number };
type Release = { id:number; name:string|null; tag_name:string; published_at:string|null; body:string|null; html_url:string; prerelease:boolean; assets:Asset[] };

function bytes(value:number) { return value < 1024 * 1024 ? `${Math.ceil(value/1024)} KB` : `${(value/1024/1024).toFixed(1)} MB`; }

export default function ReleaseExplorer() {
  const [releases, setReleases] = useState<Release[]>([]);
  const [error, setError] = useState(false);
  useEffect(() => {
    fetch("https://api.github.com/repos/MarcosBravin/ProxyEdu/releases?per_page=10", {headers:{Accept:"application/vnd.github+json"}})
      .then(result => result.ok ? result.json() : Promise.reject())
      .then(setReleases).catch(() => setError(true));
  }, []);
  if (error) return <div className="notice">Não foi possível consultar o GitHub agora. <a href="https://github.com/MarcosBravin/ProxyEdu/releases">Abra a página oficial de releases</a>.</div>;
  if (!releases.length) return <p className="muted">Consultando releases publicadas…</p>;
  return <div className="release-list">{releases.map((release, index) => <article className="release-card" key={release.id}>
    <div className="release-meta"><span className="badge">{index === 0 ? "Versão atual" : release.prerelease ? "Pré-lançamento" : "Versão anterior"}</span>{release.published_at && <time dateTime={release.published_at}>{new Intl.DateTimeFormat("pt-BR",{dateStyle:"long"}).format(new Date(release.published_at))}</time>}</div>
    <h2>{release.name || release.tag_name}</h2>
    {release.body && <pre>{release.body}</pre>}
    {release.assets.length > 0 && <div className="hero-actions">{release.assets.map(asset => <a className="button button-secondary" key={asset.id} href={asset.browser_download_url}>{asset.name} · {bytes(asset.size)}</a>)}</div>}
    <p><a href={release.html_url}>Ver esta release no GitHub →</a></p>
  </article>)}</div>;
}
