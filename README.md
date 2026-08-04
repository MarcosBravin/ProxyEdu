# ProxyEdu

> Plataforma de controle, monitoramento e gerenciamento de acesso à internet para ambientes educacionais.

O **ProxyEdu** centraliza, no computador do professor ou administrador, um servidor proxy responsável pelo controle de acesso à internet das estações dos alunos.

A solução inclui dashboard web, API administrativa, regras de bloqueio e liberação, monitoramento em tempo real, descoberta automática do servidor na rede local e instalação dos componentes como serviços do Windows.

---

## Sumário

- [Visão geral](#visão-geral)
- [Arquitetura](#arquitetura)
- [Principais recursos](#principais-recursos)
- [Estrutura do repositório](#estrutura-do-repositório)
- [Tecnologias](#tecnologias)
- [Pré-requisitos](#pré-requisitos)
- [Portas e comunicação](#portas-e-comunicação)
- [Configuração](#configuração)
- [Build](#build)
- [Publicação](#publicação)
- [Instalação](#instalação)
- [Dashboard](#dashboard)
- [API REST](#api-rest)
- [Descoberta automática](#descoberta-automática)
- [Proxy HTTPS e certificado raiz](#proxy-https-e-certificado-raiz)
- [Persistência de dados](#persistência-de-dados)
- [Logs e auditoria](#logs-e-auditoria)
- [Atualizações](#atualizações)
- [Desinstalação](#desinstalação)
- [Segurança](#segurança)
- [Privacidade e LGPD](#privacidade-e-lgpd)
- [Testes](#testes)
- [Solução de problemas](#solução-de-problemas)
- [Limitações conhecidas](#limitações-conhecidas)
- [Roadmap](#roadmap)
- [Licença](#licença)

---

## Visão geral

O ProxyEdu foi desenvolvido para auxiliar escolas, laboratórios, centros de treinamento e outros ambientes educacionais no gerenciamento do acesso à internet.

O servidor é executado no computador do professor, administrador ou em uma máquina dedicada da rede local. Os computadores dos alunos executam um cliente responsável por configurar o proxy do Windows, localizar o servidor e enviar informações periódicas de status.

```text
[PC Aluno 1] --\
[PC Aluno 2] ----> [ProxyEdu Server] <---- [Dashboard Web]
[PC Aluno 3] --/       Proxy: 8888
                        Dashboard/API: 5000
                        Discovery: 50505/UDP
```

O servidor disponibiliza:

- serviço de proxy HTTP e HTTPS;
- dashboard administrativo;
- API REST;
- comunicação em tempo real via SignalR;
- descoberta automática na rede local;
- controle de alunos e grupos;
- regras de bloqueio e liberação;
- logs, estatísticas e diagnósticos;
- persistência local;
- gerenciamento de atualizações.

---

## Arquitetura

A solução é dividida em componentes independentes:

```text
┌───────────────────────────────────────────────────────────────┐
│                       ProxyEdu.Server                         │
│                                                               │
│  Dashboard  │  API REST  │  SignalR  │  Proxy  │  Discovery   │
│                                                               │
│                    Persistência LiteDB                        │
└───────────────────────────────────────────────────────────────┘
                ▲                         ▲
                │ HTTP / WebSocket        │ Proxy HTTP/HTTPS
                │                         │
┌──────────────────────────────┐   ┌────────────────────────────┐
│       ProxyEdu.Client        │   │       Navegadores e        │
│                              │   │       aplicações           │
│ Worker Service do Windows    │   │                            │
│ Heartbeat e configuração     │   │ Tráfego encaminhado pelo   │
│ automática do proxy          │   │ servidor ProxyEdu          │
└──────────────────────────────┘   └────────────────────────────┘
```

### Fluxo básico

1. O servidor ProxyEdu é iniciado no computador do professor ou administrador.
2. O cliente é iniciado como serviço nas estações dos alunos.
3. O cliente localiza o servidor automaticamente ou usa um IP configurado.
4. O cliente configura o proxy do Windows.
5. A estação passa a enviar heartbeats periódicos ao servidor.
6. O administrador acompanha e controla as estações pelo dashboard.
7. As regras configuradas são aplicadas ao tráfego de navegação.

---

## Principais recursos

### Gerenciamento de estações

- Registro e acompanhamento de computadores conectados.
- Visualização de status online e offline.
- Identificação por nome da máquina, usuário, IP e grupo.
- Atualizações em tempo real via SignalR.
- Heartbeats periódicos enviados pelos clientes.

### Controle de acesso

- Bloqueio e liberação por aluno.
- Bloqueio e liberação por grupo.
- Bloqueio global de todas as estações.
- Regras de whitelist e blacklist.
- Suporte a padrões e subdomínios.
- Ativação e desativação individual de regras.
- Presets para categorias comuns.

### Presets de filtragem

A aplicação pode incluir presets para categorias como:

- redes sociais;
- jogos;
- streaming;
- conteúdo adulto;
- downloads;
- mensageiros;
- serviços de armazenamento;
- sites definidos pela instituição.

### Monitoramento

- Logs de navegação.
- Filtros por aluno, grupo, domínio, data e status.
- Estatísticas operacionais.
- Métricas do servidor.
- Alertas de indisponibilidade.
- Informações de diagnóstico.

### Operação

- Execução como serviço do Windows.
- Descoberta automática na rede local.
- Instalação por scripts ou instalador NSIS.
- Persistência local com LiteDB.
- Health checks de disponibilidade e prontidão.
- Atualizador integrado.

---

## Estrutura do repositório

| Caminho | Descrição |
|---|---|
| `ProxyEdu.Server` | Servidor ASP.NET Core com proxy, dashboard, API REST, SignalR, descoberta e persistência. |
| `ProxyEdu.Client` | Worker Service para Windows instalado nas estações dos alunos. |
| `ProxyEdu.Shared` | Modelos, DTOs e contratos compartilhados entre cliente e servidor. |
| `ProxyEdu.Tests` | Testes unitários, testes de integração e validações automatizadas. |
| `installer` | Scripts NSIS usados para geração do instalador Windows. |
| `docs` | Documentação técnica, planejamento, auditorias e relatórios. |
| `artifacts` | Arquivos gerados durante publicação e empacotamento. |
| `build.bat` | Script centralizado de restore, build, publish e clean. |
| `install-server.bat` | Instala o servidor como serviço do Windows. |
| `install-client.bat` | Instala o cliente como serviço do Windows. |
| `uninstall-server.bat` | Remove o serviço do servidor. |
| `uninstall-client.bat` | Remove o serviço do cliente e restaura o proxy. |
| `uninstall-all.bat` | Remove cliente e servidor. |

---

## Tecnologias

- .NET 8
- ASP.NET Core
- ASP.NET Core Web API
- SignalR
- Titanium.Web.Proxy
- LiteDB
- Worker Service
- Windows Service
- WinInet API
- NSIS 3.x
- PowerShell
- Batch Script

---

## Pré-requisitos

### Desenvolvimento

- Windows 10 ou Windows 11.
- .NET 8 SDK.
- Visual Studio 2022, Visual Studio Code ou editor compatível.
- Git.
- Permissões administrativas para instalação e teste dos serviços.

### Geração do instalador

- NSIS 3.x.
- Cliente e servidor previamente publicados.
- Permissão de escrita na pasta `artifacts`.

### Produção

- Computador do professor, administrador ou servidor dedicado.
- IP fixo ou reserva de DHCP recomendada.
- Regras de firewall configuradas.
- Permissões administrativas.
- Rede local estável.
- Política institucional de uso e monitoramento.

---

## Portas e comunicação

| Porta | Protocolo | Origem | Destino | Finalidade |
|---:|---|---|---|---|
| `5000` | TCP | Administrador e clientes | Servidor | Dashboard, API REST e SignalR. |
| `8888` | TCP | Estações dos alunos | Servidor | Proxy HTTP e HTTPS. |
| `50505` | UDP | Clientes | Rede local | Descoberta automática do servidor. |

As portas podem ser alteradas nas configurações da aplicação.

> Em redes segmentadas por VLAN, a descoberta automática por broadcast pode não funcionar. Nesse cenário, configure o IP do servidor manualmente ou utilize DNS interno.

---

## Configuração

### Servidor

O arquivo principal de configuração do servidor está em:

```text
ProxyEdu.Server/appsettings.json
```

Exemplo:

```json
{
  "Urls": "http://0.0.0.0:5000",
  "Proxy": {
    "Port": 8888
  },
  "Discovery": {
    "Port": 50505
  },
  "Security": {
    "DefaultAdminPassword": ""
  },
  "Database": {
    "Path": "C:\\ProgramData\\ProxyEdu\\data.db"
  }
}
```

#### Configurações principais

| Propriedade | Descrição |
|---|---|
| `Urls` | Endereço usado pelo dashboard e pela API. |
| `Proxy:Port` | Porta do servidor proxy. |
| `Discovery:Port` | Porta UDP usada para descoberta automática. |
| `Security:DefaultAdminPassword` | Senha inicial do usuário administrador. |
| `Database:Path` | Caminho do banco de dados local. |

A configuração abaixo permite que o dashboard e a API sejam acessados por outros dispositivos da rede:

```json
{
  "Urls": "http://0.0.0.0:5000"
}
```

Em produção, recomenda-se utilizar HTTPS e restringir o acesso administrativo por firewall.

#### Credencial inicial

O usuário administrativo inicial é:

```text
Usuário: admin
```

Defina uma senha forte antes da implantação:

```json
{
  "Security": {
    "DefaultAdminPassword": "ALTERE_PARA_UMA_SENHA_FORTE"
  }
}
```

Não mantenha credenciais padrão em ambientes de produção.

### Cliente

O arquivo de configuração do cliente está em:

```text
ProxyEdu.Client/appsettings.json
```

Exemplo:

```json
{
  "Server": {
    "Ip": "",
    "ProxyPort": "8888",
    "DashboardPort": "5000",
    "AutoDiscover": true,
    "DiscoveryPort": "50505",
    "RootCertificateThumbprint": ""
  },
  "Student": {
    "Group": "default"
  },
  "Protection": {
    "CheckIntervalSeconds": 2,
    "FailClosed": true
  }
}
```

#### Configurações principais

| Propriedade | Descrição |
|---|---|
| `Server:Ip` | Endereço IP ou nome DNS do servidor. |
| `Server:ProxyPort` | Porta usada pelo proxy. |
| `Server:DashboardPort` | Porta da API e do dashboard. |
| `Server:AutoDiscover` | Habilita a descoberta automática. |
| `Server:DiscoveryPort` | Porta usada para descoberta na rede local. |
| `Server:RootCertificateThumbprint` | Thumbprint esperado do certificado raiz. |
| `Student:Group` | Grupo inicial atribuído à estação. |
| `Protection:CheckIntervalSeconds` | Intervalo de verificação da configuração local. |
| `Protection:FailClosed` | Mantém a restrição quando o servidor está indisponível. |

#### Descoberta automática

Quando `Server:Ip` estiver vazio e `AutoDiscover` estiver habilitado, o cliente tentará localizar o servidor automaticamente:

```json
{
  "Server": {
    "Ip": "",
    "AutoDiscover": true
  }
}
```

#### Servidor com IP fixo

```json
{
  "Server": {
    "Ip": "192.168.1.10",
    "AutoDiscover": false
  }
}
```

Também é possível usar um nome DNS:

```json
{
  "Server": {
    "Ip": "proxyedu.escola.local",
    "AutoDiscover": false
  }
}
```

---

## Build

O script `build.bat`, localizado na raiz do repositório, pode restaurar, compilar, publicar ou limpar os projetos:

```powershell
.\build.bat
```

### Exemplos

Restaurar todos os projetos:

```powershell
.\build.bat -Action restore -Target all
```

Compilar o servidor em modo Debug:

```powershell
.\build.bat -Action build -Target server -Configuration Debug
```

Compilar toda a solução em modo Release:

```powershell
.\build.bat -Action build -Target all -Configuration Release
```

Publicar o cliente para Windows x64:

```powershell
.\build.bat `
  -Action publish `
  -Target client `
  -Configuration Release `
  -Runtime win-x64 `
  -SelfContained true
```

Publicar o servidor:

```powershell
.\build.bat `
  -Action publish `
  -Target server `
  -Configuration Release `
  -Runtime win-x64 `
  -SelfContained true
```

Limpar os projetos:

```powershell
.\build.bat -Action clean -Target all
```

### Parâmetros disponíveis

| Parâmetro | Valores | Descrição |
|---|---|---|
| `-Action` | `restore`, `build`, `publish`, `clean` | Operação executada. |
| `-Target` | `all`, `server`, `client`, `shared` | Projeto ou conjunto de projetos. |
| `-Configuration` | `Debug`, `Release` | Configuração de compilação. |
| `-Runtime` | Ex.: `win-x64` | Runtime Identifier do .NET. |
| `-SelfContained` | `true`, `false` | Define se o runtime será incluído. |
| `-OutputRoot` | Caminho | Pasta base de saída. |

A pasta padrão de publicação é:

```text
artifacts\publish
```

---

## Publicação

### Servidor

```powershell
dotnet publish `
  .\ProxyEdu.Server\ProxyEdu.Server.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o .\artifacts\publish\server
```

Arquivos publicados:

```text
artifacts\publish\server
```

### Cliente

```powershell
dotnet publish `
  .\ProxyEdu.Client\ProxyEdu.Client.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o .\artifacts\publish\client
```

Arquivos publicados:

```text
artifacts\publish\client
```

---

## Instalação

Os scripts de instalação devem ser executados como administrador.

### Instalação do servidor

No computador do professor ou servidor:

```powershell
.\install-server.bat
```

### Instalação do cliente

Em cada computador de aluno:

```powershell
.\install-client.bat
```

O cliente deve configurar o proxy do Windows, registrar o serviço, localizar o servidor e validar o certificado raiz quando aplicável.

---

## Instalador Windows

O instalador NSIS está localizado em:

```text
installer\ProxyEduInstaller.nsi
```

Antes de gerar o instalador, publique os componentes em:

```text
artifacts\publish\client
artifacts\publish\server
```

Gere o instalador com:

```powershell
installer\build-installer.bat
```

Saída esperada:

```text
artifacts\installer\ProxyEduInstaller.exe
```

---

## Dashboard

Após iniciar o servidor, acesse:

```text
http://localhost:5000
```

Em outro computador da rede:

```text
http://IP_DO_SERVIDOR:5000
```

Exemplo:

```text
http://192.168.1.10:5000
```

### Primeiro acesso

```text
Usuário: admin
Senha: valor configurado em Security:DefaultAdminPassword
```

A senha inicial deve ser alterada imediatamente.

---

## API REST

A API é disponibilizada no mesmo endereço do dashboard:

```text
http://localhost:5000/api
```

### Autenticação

| Método | Endpoint | Descrição |
|---|---|---|
| `GET` | `/api/auth/me` | Retorna o usuário autenticado. |
| `POST` | `/api/auth/change-password` | Altera a senha do usuário autenticado. |

### Usuários administrativos

| Método | Endpoint | Descrição |
|---|---|---|
| `GET` | `/api/users` | Lista usuários do dashboard. |
| `POST` | `/api/users` | Cria um usuário administrativo. |
| `PUT` | `/api/users/{id}` | Atualiza um usuário. |
| `DELETE` | `/api/users/{id}` | Remove um usuário. |

### Alunos e estações

| Método | Endpoint | Descrição |
|---|---|---|
| `GET` | `/api/students` | Lista alunos e estações registradas. |
| `GET` | `/api/students/{id}` | Retorna os detalhes de um aluno. |
| `PUT` | `/api/students/{id}` | Atualiza os dados de um aluno. |
| `POST` | `/api/students/{id}/block` | Bloqueia um aluno. |
| `POST` | `/api/students/{id}/unblock` | Libera um aluno. |
| `POST` | `/api/students/block-all` | Bloqueia todos os alunos. |
| `POST` | `/api/students/unblock-all` | Libera todos os alunos. |
| `POST` | `/api/students/group/{groupName}/block` | Bloqueia alunos de um grupo. |
| `POST` | `/api/students/group/{groupName}/unblock` | Libera alunos de um grupo. |
| `GET` | `/api/students/stats` | Retorna estatísticas das estações. |

### Filtros

| Método | Endpoint | Descrição |
|---|---|---|
| `GET` | `/api/filters` | Lista regras de filtro. |
| `POST` | `/api/filters` | Cria uma regra. |
| `PUT` | `/api/filters/{id}` | Atualiza uma regra. |
| `DELETE` | `/api/filters/{id}` | Remove uma regra. |
| `POST` | `/api/filters/{id}/toggle` | Ativa ou desativa uma regra. |
| `POST` | `/api/filters/preset/{name}` | Aplica um preset de filtros. |

### Logs

| Método | Endpoint | Descrição |
|---|---|---|
| `GET` | `/api/logs` | Lista logs de acesso. |
| `DELETE` | `/api/logs` | Remove logs conforme os parâmetros do endpoint. |

Exemplo recomendado:

```text
DELETE /api/logs?before=2026-01-01
```

### Configurações

| Método | Endpoint | Descrição |
|---|---|---|
| `GET` | `/api/settings` | Retorna as configurações do proxy. |
| `PUT` | `/api/settings` | Atualiza as configurações do proxy. |

### Saúde e diagnóstico

| Método | Endpoint | Descrição |
|---|---|---|
| `GET` | `/api/health` | Retorna o status básico da aplicação. |
| `GET` | `/api/health/ready` | Retorna a prontidão operacional. |
| `GET` | `/api/serverstatus` | Retorna o status do servidor. |
| `GET` | `/api/serverstatus/alerts` | Retorna alertas operacionais. |
| `GET` | `/api/serverstatus/metrics` | Retorna métricas operacionais. |
| `GET` | `/api/diagnostics` | Retorna informações de diagnóstico. |

### Certificado

| Método | Endpoint | Descrição |
|---|---|---|
| `GET` | `/api/certificate/root` | Disponibiliza o certificado raiz público do proxy. |

### Atualizações

| Método | Endpoint | Descrição |
|---|---|---|
| `GET` | `/api/update` | Retorna informações do atualizador. |
| `POST` | `/api/update/check` | Verifica se existe atualização. |
| `POST` | `/api/update/download` | Baixa uma atualização. |
| `POST` | `/api/update/install` | Instala uma atualização. |

Operações de atualização devem exigir autorização administrativa elevada.

---

## Descoberta automática

O ProxyEdu Client pode localizar o servidor por broadcast UDP.

### Funcionamento

1. O cliente envia uma solicitação na porta de descoberta.
2. O servidor responde com seus dados de conexão.
3. O cliente valida a resposta.
4. O cliente armazena o endereço encontrado.
5. O cliente inicia o registro ou envio de heartbeat.

A descoberta automática não deve ser usada como mecanismo de autenticação.

A resposta do servidor deve incluir, quando possível:

- identificador da instalação;
- endereço do servidor;
- porta do proxy;
- porta da API;
- timestamp;
- nonce;
- assinatura;
- thumbprint do certificado.

---

## Proxy HTTPS e certificado raiz

O proxy HTTPS pode utilizar um certificado raiz local gerenciado pelo Titanium.Web.Proxy.

### Boas práticas

- Gerar um certificado exclusivo por instalação.
- Nunca incluir uma chave privada fixa no repositório.
- Proteger a chave privada com permissões restritas.
- Instalar o certificado somente em máquinas autorizadas.
- Registrar instalação e remoção.
- Validar o thumbprint esperado.
- Remover o certificado durante a desinstalação.
- Definir exceções para serviços sensíveis.
- Não registrar tokens, senhas ou parâmetros sigilosos.

> A inspeção HTTPS deve ser usada apenas com autorização da instituição e com políticas adequadas de privacidade, segurança e proteção de dados.

---

## Persistência de dados

Por padrão, o servidor utiliza LiteDB:

```text
C:\ProgramData\ProxyEdu\data.db
```

Estrutura recomendada:

```text
C:\ProgramData\ProxyEdu\
├── data\
│   ├── configuration.db
│   └── access-logs.db
├── logs\
├── certificates\
├── updates\
└── backups\
```

### Backup

Recomenda-se:

- manter backups periódicos;
- criptografar cópias que contenham dados pessoais;
- limitar o acesso à pasta de backup;
- testar restaurações regularmente;
- separar logs operacionais dos dados de configuração.

---

## Logs e auditoria

O ProxyEdu pode registrar:

- estação;
- aluno;
- grupo;
- endereço IP;
- domínio;
- horário;
- ação aplicada;
- regra responsável;
- status permitido ou bloqueado;
- eventos administrativos;
- falhas operacionais.

### Recomendações

- Registrar apenas os dados necessários.
- Evitar URLs completas contendo query strings.
- Remover tokens e informações sensíveis.
- Aplicar retenção automática.
- Restringir acesso aos logs.
- Auditar consultas, exportações e exclusões.
- Separar logs técnicos de logs de navegação.

Exemplo de URL que não deve ser armazenada integralmente:

```text
https://exemplo.com/redefinir-senha?token=VALOR_SECRETO
```

Forma reduzida recomendada:

```text
exemplo.com
```

---

## Atualizações

O atualizador deve trabalhar somente com pacotes confiáveis.

### Validações recomendadas

- conexão HTTPS;
- domínio de origem fixo;
- assinatura digital;
- hash SHA-256;
- versão do pacote;
- proteção contra downgrade;
- certificado do editor;
- auditoria da instalação;
- rollback em caso de falha.

A API não deve aceitar URLs arbitrárias para download ou execução.

---

## Desinstalação

Execute os scripts como administrador.

### Remover o servidor

```powershell
.\uninstall-server.bat
```

### Remover o cliente

```powershell
.\uninstall-client.bat
```

O script do cliente deve:

- interromper o serviço;
- remover o serviço;
- restaurar o proxy do Windows;
- remover o certificado raiz;
- limpar configurações locais;
- excluir os arquivos do cliente.

### Remover todos os componentes

```powershell
.\uninstall-all.bat
```

> Antes de remover o servidor, realize backup dos dados necessários.

---

## Segurança

O ProxyEdu deve ser tratado como uma aplicação de alta confiança, pois controla tráfego de rede e pode operar com privilégios administrativos.

### Recomendações essenciais

- Alterar a senha inicial no primeiro acesso.
- Não utilizar credenciais padrão em produção.
- Utilizar HTTPS no dashboard e na API.
- Restringir portas por firewall.
- Autenticar individualmente cada estação.
- Proteger o SignalR com autenticação e autorização.
- Implementar funções administrativas.
- Aplicar rate limiting.
- Bloquear tentativas repetidas de login.
- Validar todas as entradas.
- Aplicar proteção contra CSRF quando necessário.
- Restringir CORS.
- Assinar respostas de descoberta.
- Assinar pacotes de atualização.
- Proteger a chave privada do certificado raiz.
- Registrar ações administrativas.
- Executar os serviços com o menor privilégio possível.

### Perfis administrativos recomendados

| Perfil | Permissões |
|---|---|
| `SuperAdmin` | Gerenciamento total da aplicação. |
| `Administrador` | Configurações, usuários, filtros e atualizações. |
| `Professor` | Controle de alunos e grupos autorizados. |
| `Auditor` | Consulta de logs e relatórios. |
| `SomenteLeitura` | Visualização sem alterações. |

### Armazenamento de senhas

Utilize algoritmos adequados para armazenamento de credenciais:

- ASP.NET Core Identity `PasswordHasher`;
- Argon2id;
- bcrypt;
- PBKDF2 com configuração adequada.

Nunca armazene senhas em texto puro.

---

## Privacidade e LGPD

O ProxyEdu pode tratar dados relacionados à navegação e à atividade de alunos. A implantação deve considerar as políticas da instituição e a legislação aplicável.

### Medidas recomendadas

- Definir finalidade clara para o monitoramento.
- Registrar somente dados necessários.
- Configurar prazo de retenção.
- Aplicar descarte automático.
- Restringir acesso aos dados.
- Informar responsáveis, professores e usuários.
- Proteger backups.
- Registrar exportações e exclusões.
- Anonimizar relatórios sempre que possível.
- Evitar armazenamento de conteúdo sensível.
- Criar uma política de resposta a incidentes.

O sistema não substitui análise jurídica ou política institucional de privacidade.

---

## Testes

O projeto de testes está localizado em:

```text
ProxyEdu.Tests
```

Executar todos os testes:

```powershell
dotnet test
```

Executar em modo Release:

```powershell
dotnet test -c Release
```

### Testes recomendados

- regras de whitelist e blacklist;
- precedência entre regras;
- padrões e wildcards;
- subdomínios;
- domínios Unicode e punycode;
- bloqueio por aluno, grupo e global;
- autenticação e autorização;
- heartbeats inválidos;
- descoberta falsa;
- reconexão SignalR;
- indisponibilidade do servidor;
- comportamento `FailClosed`;
- instalação e remoção do certificado;
- restauração do proxy;
- retenção de logs;
- atualização adulterada;
- concorrência no LiteDB;
- health checks;
- instalação e remoção dos serviços.

---

## Solução de problemas

### O dashboard não abre

Verifique se o serviço está em execução:

```powershell
Get-Service ProxyEduServer
```

Teste a porta:

```powershell
Test-NetConnection localhost -Port 5000
```

### O cliente não encontra o servidor

Verifique:

- se o servidor está iniciado;
- se ambos estão na mesma rede;
- se a porta UDP `50505` está liberada;
- se a rede bloqueia broadcast;
- se o IP configurado está correto;
- se existe segmentação por VLAN.

### O proxy não funciona

Teste a porta:

```powershell
Test-NetConnection IP_DO_SERVIDOR -Port 8888
```

Também verifique o serviço do cliente, o serviço do servidor, o firewall e a configuração de proxy do Windows.

### Sites HTTPS apresentam erro de certificado

Verifique:

- se o certificado raiz foi instalado;
- se o certificado não expirou;
- se o thumbprint está correto;
- se a estação confia na autoridade certificadora;
- se a hora do sistema está correta.

### O cliente mantém a internet bloqueada

Quando `FailClosed` está habilitado, o cliente pode manter as restrições caso não consiga validar o servidor.

Verifique conectividade, DNS, IP do servidor, portas, certificado e estado dos serviços.

### O serviço não inicia

Consulte o Visualizador de Eventos do Windows e os logs em:

```text
C:\ProgramData\ProxyEdu\logs
```

---

## Limitações conhecidas

- A descoberta automática pode não funcionar entre VLANs.
- Usuários com privilégio de administrador local podem remover ou alterar o cliente.
- A inspeção HTTPS depende da instalação correta do certificado raiz.
- LiteDB é mais indicado para instalações pequenas ou médias.
- Aplicações que ignoram o proxy do Windows podem exigir tratamento adicional.
- Protocolos não baseados em HTTP ou HTTPS não são filtrados pelo proxy.
- VPNs e túneis podem contornar controles quando não bloqueados pela rede.
- O modo `FailClosed` deve ser configurado com cuidado.
- A filtragem baseada somente em domínio não substitui soluções avançadas de segurança.

---

## Boas práticas de implantação

1. Configure uma senha administrativa forte.
2. Defina um IP fixo ou nome DNS para o servidor.
3. Ative HTTPS no dashboard e na API.
4. Restrinja as portas no firewall.
5. Revise grupos e regras de acesso.
6. Configure retenção de logs.
7. Teste o modo `FailClosed`.
8. Valide a instalação e remoção do certificado.
9. Teste a desinstalação do cliente.
10. Configure backups.
11. Documente a política de monitoramento.
12. Execute uma implantação piloto antes da implantação geral.

---

## Roadmap

- [ ] Versionamento da API.
- [ ] Autenticação individual por dispositivo.
- [ ] Registro seguro de novas estações.
- [ ] HTTPS obrigatório.
- [ ] Assinatura das respostas de descoberta.
- [ ] Controle de acesso baseado em funções.
- [ ] Retenção automática de logs.
- [ ] Exportação de relatórios.
- [ ] Backup e restauração pelo dashboard.
- [ ] Atualizações assinadas digitalmente.
- [ ] Proteção contra downgrade.
- [ ] Integração com Active Directory.
- [ ] Suporte a múltiplas unidades escolares.
- [ ] Suporte a banco de dados externo.
- [ ] Política offline configurável.
- [ ] Dashboard responsivo.
- [ ] Internacionalização.

---

## Aviso de uso

O ProxyEdu deve ser utilizado apenas em dispositivos, redes e ambientes nos quais o administrador possua autorização para instalar, configurar, monitorar e controlar o tráfego.

A inspeção HTTPS e o registro de navegação devem respeitar:

- políticas internas da instituição;
- contratos e termos aplicáveis;
- privacidade dos usuários;
- legislação local;
- princípios de necessidade, proporcionalidade e segurança.

---

## Licença

Consulte o arquivo [`LICENSE`](LICENSE) para conhecer os termos de uso, distribuição e modificação do projeto.
