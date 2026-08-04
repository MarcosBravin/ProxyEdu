# ProxyEdu

ProxyEdu é uma solução de controle de acesso à internet para ambientes educacionais. A aplicação centraliza, no computador do professor ou administrador, um servidor proxy com dashboard web, API administrativa, regras de bloqueio/liberação e acompanhamento das estações dos alunos.

## Visão Geral

```text
[PC Aluno 1] --\
[PC Aluno 2] ----> [PC Professor - ProxyEdu Server] <---- [Dashboard Web]
[PC Aluno 3] --/       Proxy: 8888 | Dashboard: 5000
```

O servidor executa o proxy, expõe o dashboard em `http://localhost:5000` e recebe informações dos clientes instalados nas máquinas dos alunos. O cliente configura o proxy do Windows, envia heartbeats ao servidor e pode localizar automaticamente o servidor na rede local.

## Estrutura do Repositório

| Caminho | Descrição |
|---|---|
| `ProxyEdu.Server` | Servidor ASP.NET Core com proxy, API REST, SignalR, dashboard web e persistência local. |
| `ProxyEdu.Client` | Worker Service para Windows instalado nas estações dos alunos. |
| `ProxyEdu.Shared` | Modelos e contratos compartilhados entre cliente e servidor. |
| `ProxyEdu.Tests` | Projeto de testes automatizados. |
| `installer` | Script NSIS para geração do instalador Windows. |
| `docs` | Documentação auxiliar, auditorias, planejamento e relatórios. |

## Principais Recursos

- Dashboard web para acompanhamento das estações conectadas.
- Bloqueio e liberação de acesso por aluno, grupo ou aplicação global.
- Regras de whitelist e blacklist com suporte a padrões.
- Presets de bloqueio para categorias como redes sociais, jogos e streaming.
- Logs de navegação com filtros por aluno, domínio e status.
- Estatísticas operacionais e métricas de uso.
- Comunicação em tempo real via SignalR.
- Descoberta automática do servidor na rede local.
- Execução como serviço do Windows.
- Persistência local com LiteDB em `C:\ProgramData\ProxyEdu\data.db`.

## Tecnologias

- .NET 8
- ASP.NET Core
- SignalR
- Titanium.Web.Proxy
- LiteDB
- Windows Service
- WinInet API
- NSIS para empacotamento do instalador

## Pré-requisitos

- Windows 10 ou Windows 11.
- .NET 8 SDK para desenvolvimento e build.
- Visual Studio 2022, VS Code ou outro editor compatível com .NET.
- Permissões de administrador para instalação dos serviços.
- NSIS 3.x, somente se for gerar o instalador em `installer`.

## Configuração

### Servidor

O servidor escuta em todas as interfaces na porta `5000`:

```json
{
  "Urls": "http://0.0.0.0:5000",
  "Discovery": {
    "Port": 50505
  },
  "Security": {
    "DefaultAdminPassword": ""
  }
}
```

Configurações relevantes:

- `Urls`: endereço usado pelo dashboard e pela API.
- `Discovery:Port`: porta usada para descoberta automática na rede local.
- `Security:DefaultAdminPassword`: senha inicial do usuário `admin`. Se estiver vazia, a aplicação usa a senha padrão `admin123` e exige troca no primeiro login.

### Cliente

O cliente usa `ProxyEdu.Client/appsettings.json` para localizar o servidor e configurar o proxy local:

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

Com `Ip` vazio e `AutoDiscover` definido como `true`, o cliente tenta encontrar automaticamente o servidor na rede local. Para ambientes com IP fixo, preencha `Server:Ip` com o endereço do computador do professor ou servidor.

## Build

Use o script da raiz para restaurar, compilar, publicar ou limpar os projetos:

```powershell
.\build.bat
```

Exemplos:

```powershell
.\build.bat -Action restore -Target all
.\build.bat -Action build -Target server -Configuration Debug
.\build.bat -Action publish -Target client -Runtime win-x64 -SelfContained true
.\build.bat -Action clean -Target all
```

Parâmetros disponíveis:

| Parâmetro | Valores |
|---|---|
| `-Action` | `restore`, `build`, `publish`, `clean` |
| `-Target` | `all`, `server`, `client`, `shared` |
| `-Configuration` | `Debug`, `Release` |
| `-Runtime` | RID do .NET, por exemplo `win-x64` |
| `-SelfContained` | `true` ou `false`, usado em `publish` |
| `-OutputRoot` | Pasta base de saída. Padrão: `.\artifacts\publish` |

## Publicação Manual

### Servidor

```powershell
dotnet publish .\ProxyEdu.Server\ProxyEdu.Server.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\publish\server
```

Depois da publicação, execute `install-server.bat` como administrador no computador do professor ou servidor.

Dashboard:

```text
http://localhost:5000
```

Credenciais iniciais:

```text
Usuário: admin
Senha: valor de Security:DefaultAdminPassword ou admin123 quando não configurado
```

Recomenda-se configurar `Security:DefaultAdminPassword` antes da primeira execução em produção.

### Cliente

```powershell
dotnet publish .\ProxyEdu.Client\ProxyEdu.Client.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\publish\client
```

Depois da publicação, execute `install-client.bat` como administrador em cada computador de aluno.

## Instalador Windows

O instalador NSIS está em `installer/ProxyEduInstaller.nsi` e permite selecionar os componentes a instalar:

- Cliente como serviço do Windows.
- Servidor como serviço do Windows com atalho para o dashboard.

Para gerar o instalador:

```powershell
installer\build-installer.bat
```

Saída esperada:

```text
artifacts\installer\ProxyEduInstaller.exe
```

Antes de executar o build do instalador, publique cliente e servidor em:

```text
artifacts\publish\client
artifacts\publish\server
```

## API REST

| Método | Endpoint | Descrição |
|---|---|---|
| `GET` | `/api/auth/me` | Retorna o usuário autenticado. |
| `POST` | `/api/auth/change-password` | Altera a senha do usuário autenticado. |
| `GET` | `/api/users` | Lista usuários do dashboard. |
| `POST` | `/api/users` | Cria usuário do dashboard. |
| `PUT` | `/api/users/{id}` | Atualiza usuário do dashboard. |
| `DELETE` | `/api/users/{id}` | Remove usuário do dashboard. |
| `GET` | `/api/students` | Lista alunos registrados. |
| `GET` | `/api/students/{id}` | Retorna detalhes de um aluno. |
| `PUT` | `/api/students/{id}` | Atualiza dados de um aluno. |
| `POST` | `/api/students/{id}/block` | Bloqueia um aluno. |
| `POST` | `/api/students/{id}/unblock` | Libera um aluno. |
| `POST` | `/api/students/block-all` | Bloqueia todos os alunos. |
| `POST` | `/api/students/unblock-all` | Libera todos os alunos. |
| `POST` | `/api/students/group/{groupName}/block` | Bloqueia alunos de um grupo. |
| `POST` | `/api/students/group/{groupName}/unblock` | Libera alunos de um grupo. |
| `GET` | `/api/students/stats` | Retorna estatísticas dos alunos. |
| `GET` | `/api/filters` | Lista regras de filtro. |
| `POST` | `/api/filters` | Cria regra de filtro. |
| `PUT` | `/api/filters/{id}` | Atualiza regra de filtro. |
| `DELETE` | `/api/filters/{id}` | Remove regra de filtro. |
| `POST` | `/api/filters/{id}/toggle` | Ativa ou desativa uma regra. |
| `POST` | `/api/filters/preset/{name}` | Aplica preset de filtros. |
| `GET` | `/api/logs` | Lista logs de acesso. |
| `DELETE` | `/api/logs` | Remove logs conforme implementação do endpoint. |
| `GET` | `/api/settings` | Retorna configurações do proxy. |
| `PUT` | `/api/settings` | Atualiza configurações do proxy. |
| `GET` | `/api/health` | Retorna status básico da aplicação. |
| `GET` | `/api/health/ready` | Retorna prontidão operacional. |
| `GET` | `/api/serverstatus` | Retorna status do servidor. |
| `GET` | `/api/serverstatus/alerts` | Retorna alertas operacionais. |
| `GET` | `/api/serverstatus/metrics` | Retorna métricas operacionais. |
| `GET` | `/api/diagnostics` | Retorna informações de diagnóstico. |
| `GET` | `/api/certificate/root` | Disponibiliza o certificado raiz do proxy. |
| `GET` | `/api/update` | Retorna informações do atualizador. |
| `POST` | `/api/update/check` | Verifica atualização disponível. |
| `POST` | `/api/update/download` | Baixa atualização. |
| `POST` | `/api/update/install` | Instala atualização. |

## Desinstalação

Scripts disponíveis na raiz do repositório:

| Script | Finalidade |
|---|---|
| `uninstall-server.bat` | Remove o serviço do servidor e dados em `C:\ProgramData\ProxyEdu`. |
| `uninstall-client.bat` | Remove o serviço do cliente e restaura a configuração de proxy do Windows. |
| `uninstall-all.bat` | Executa a remoção do servidor e do cliente. |

Execute os scripts de desinstalação como administrador.

## Segurança e Operação

- Troque a senha inicial do usuário `admin` na primeira execução.
- Configure `Security:DefaultAdminPassword` antes de implantar em produção.
- Execute instaladores e scripts de serviço com privilégios administrativos.
- Revise regras de firewall para permitir o dashboard, o proxy e a descoberta na rede local.
- O proxy HTTPS utiliza certificado raiz gerenciado pelo Titanium.Web.Proxy.
- O cliente pode operar em modo `FailClosed`, mantendo restrição quando não consegue validar o estado esperado.

## Licença

Consulte o arquivo `LICENSE` para os termos de uso do projeto.
