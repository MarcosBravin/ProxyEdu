# ProxyEdu

Controle de acesso à internet por dispositivo, projetado para laboratórios e redes escolares. O ProxyEdu centraliza o proxy e a administração em um **Server Windows** dentro da instituição; cada computador utiliza um **Client** com identidade persistente.

[Site](https://proxyedu.bravintech.com/) · [Como funciona](https://proxyedu.bravintech.com/como-funciona/) · [Segurança](https://proxyedu.bravintech.com/seguranca/) · [Releases](https://proxyedu.bravintech.com/releases/) · [Planos](https://proxyedu.bravintech.com/planos/)

## Como a solução se organiza

```text
Computadores da escola
  └─ ProxyEdu Client (identidade persistente)
       └─ rede local
            └─ ProxyEdu Server
                 ├─ proxy e heartbeat
                 ├─ administração e persistência local
                 └─ HTTPS → serviço privado de licenciamento
                              └─ LicenseDocument assinado
```

- A contagem de vagas usa a identidade da instalação, não IP, MAC ou hostname.
- Reconexões do mesmo Client continuam usando uma única vaga.
- Um Client offline preserva sua vaga até a liberação administrativa.
- Sem licença comercial, o plano Free admite até 5 dispositivos.
- O estado de licença verificado fica em cache; uma falha temporária da API comercial não interrompe proxy, heartbeat ou startup.

## Segurança e licenciamento

O serviço de licenciamento é independente do ProxyEdu distribuído às escolas. Documentos usam assinatura assimétrica RSA-PSS/SHA-256; a chave privada permanece exclusivamente no ambiente privado de licenciamento ou em gestão segura de chaves. O produto distribuído recebe apenas chaves públicas para verificação.

As releases podem incluir manifesto, hashes SHA-256 e assinaturas dos pacotes. Baixe somente da [página oficial de releases](https://github.com/MarcosBravin/ProxyEdu/releases). Para relatar uma vulnerabilidade, siga a [política de segurança](SECURITY.md) e evite issues públicas com detalhes exploráveis.

## Instalação e uso

- [Guia de instalação](INSTALLATION.md)
- [Guia de uso](USAGE.md)
- [Termos e licença](LICENSE)

Os artefatos prontos ficam associados a cada release. Este repositório público documenta e distribui o produto; componentes privados de licenciamento, segredos operacionais e chaves de assinatura não fazem parte dele.

## Site público

O site é estático, construído com Astro, React e TypeScript e publicado no GitHub Pages por GitHub Actions.

```powershell
npm install
npm run dev
npm run build
```

O domínio canônico é `proxyedu.bravintech.com`. URLs antigas terminadas em `.html` são mantidas como redirecionamentos para as rotas limpas.

## Uso responsável

O ProxyEdu é uma ferramenta de infraestrutura. A instituição permanece responsável por suas políticas de acesso, base legal, transparência perante os usuários, segurança dos endpoints, segmentação da rede, backups e operação adequada.
