# ProxyEdu — Sistema de Controle de Acesso Escolar

Sistema completo de proxy educacional para controle de acesso à internet em sala de aula.

---

##  Arquitetura

```
[PC Aluno 1] ─┐
[PC Aluno 2] ─┼──► [PC Professor - ProxyEdu Server] ◄── [Dashboard http://localhost:5000]
[PC Aluno 3] ─┘       (proxy na porta 8888)
```

---

##  Projetos

| Projeto | Descrição |
|---|---|
| `ProxyEdu.Server` | Servidor proxy + API REST + Dashboard Web |
| `ProxyEdu.Client` | Serviço Windows instalado nos alunos |
| `ProxyEdu.Shared` | Modelos compartilhados |

---

##  Instalação

##  CLI de Compilação

Use o CLI da raiz para compilar/publicar sem entrar em cada pasta:

```bash
.\build.bat
```

Exemplos úteis:

```bash
.\build.bat -Action restore -Target all
.\build.bat -Action build -Target server -Configuration Debug
.\build.bat -Action publish -Target client -Runtime win-x64 -SelfContained true
.\build.bat -Action clean -Target all
```

Parâmetros disponíveis:
- `-Action`: `restore`, `build`, `publish`, `clean`
- `-Target`: `all`, `server`, `client`, `shared`
- `-Configuration`: `Debug` ou `Release`
- `-Runtime`: RID do .NET (ex.: `win-x64`)
- `-SelfContained`: `true`/`false` (usado em `publish`)
- `-OutputRoot`: pasta base de saída (padrão: `.\artifacts\publish`)

### Pré-requisitos
- .NET 8 SDK
- Windows 10/11
- Visual Studio 2022 (ou VS Code)

### 1. Servidor (PC do Professor)

```bash
cd ProxyEdu.Server
dotnet publish -c Release -r win-x64 --self-contained -o ./publish
```

Execute `install-server.bat` como **Administrador**.

Dashboard acessível em: **http://localhost:5000**

Credenciais iniciais do dashboard/API administrativa (seed no banco):
- usuário: `admin`
- senha: `admin123`

Modelo de acesso:
- `Administrador`: pode criar/editar/remover usuários
- `Professor`: acesso ao dashboard sem gestão de usuários

### 2. Cliente (PC dos Alunos)

1. Edite `ProxyEdu.Client/appsettings.json`:
```json
{
  "Server": {
    "Ip": "",
    "ProxyPort": "8888",
    "DashboardPort": "5000",
    "AutoDiscover": true,
    "DiscoveryPort": "50505"
  }
}
```

`Ip` vazio + `AutoDiscover: true` faz descoberta automatica do servidor na rede local.
Se preferir fixar, preencha `Ip` com o endereco do PC do professor e mantenha as portas.

2. Publique:
```bash
cd ProxyEdu.Client
dotnet publish -c Release -r win-x64 --self-contained -o ./publish
```

3. Execute `install-client.bat` como **Administrador** em cada PC de aluno.

---

##  Instalador Profissional (NSIS)

Foi adicionado um instalador único em `installer/ProxyEduInstaller.nsi` com seleção de componentes:
- Cliente (serviço Windows)
- Servidor (serviço Windows + atalho do dashboard)

Para gerar o instalador:

```bash
installer\build-installer.bat
```

Saída:
- `artifacts\installer\ProxyEduInstaller.exe`

Observações:
- Requer publish existente em `artifacts\publish\client` e `artifacts\publish\server`
- Requer NSIS 3.x (`makensis.exe`)
- O instalador usa o ícone `Focus_Proxy.ico`

---

##  Desinstalacao Total

Scripts disponiveis na raiz:
- `uninstall-server.bat` (remove servico do servidor + dados em `C:\ProgramData\ProxyEdu`)
- `uninstall-client.bat` (remove servico do cliente + reseta proxy do Windows)
- `uninstall-all.bat` (executa os dois scripts em sequencia)

Execute sempre como **Administrador**.

---

##  Funcionalidades do Dashboard

###  Gerenciamento de Alunos
- Ver todos os computadores conectados em tempo real
- Informações: Nome, IP, MAC Address, Hostname, SO, Grupo
- Ver site atual que o aluno está acessando
- Editar nome e grupo do aluno
- Histórico de atividades por aluno

###  Controle de Acesso
- **Bloquear/Liberar** aluno individualmente
- **Bloquear Todos** com um clique
- **Liberar Todos** com um clique
- Bloquear/Liberar por grupo (Turma A, Turma B, etc.)

###  Whitelist
- Adicionar domínios sempre permitidos
- Suporte a wildcards: `*.google.com`
- Aplicar a aluno específico ou grupo
- Ativar/desativar regras individualmente

###  Blacklist
- Adicionar domínios sempre bloqueados
- Suporte a wildcards e regex
- Presets prontos: Redes Sociais, Jogos, Streaming
- Aplicar globalmente, por grupo ou por aluno

###  Logs de Acesso
- Histórico completo de navegação
- Filtrar por aluno, domínio, status (bloqueado/permitido)
- Exportar logs
- Limpeza automática configurável

###  Estatísticas
- Top domínios acessados
- Taxa de bloqueio por aluno
- Total de dados transferidos
- Gráficos em tempo real

###  Configurações
- Porta do proxy
- Modo Whitelist Total (bloqueia tudo exceto whitelist)
- Mensagem personalizada de bloqueio
- Retenção de logs

---

##  API REST

| Método | Endpoint | Descrição |
|---|---|---|
| GET | `/api/students` | Lista todos os alunos |
| POST | `/api/students/{id}/block` | Bloqueia um aluno |
| POST | `/api/students/{id}/unblock` | Libera um aluno |
| POST | `/api/students/block-all` | Bloqueia todos |
| POST | `/api/students/unblock-all` | Libera todos |
| GET | `/api/filters` | Lista regras de filtro |
| POST | `/api/filters` | Cria regra |
| DELETE | `/api/filters/{id}` | Remove regra |
| POST | `/api/filters/preset/{name}` | Aplica preset (social/games/streaming) |
| GET | `/api/logs` | Logs de acesso |
| GET | `/api/students/stats` | Estatísticas |
| GET/PUT | `/api/settings` | Configurações |

---

## 🔧 Tecnologias

- **ASP.NET Core 8** — Web API + hosting estático
- **SignalR** — Comunicação em tempo real
- **Titanium.Web.Proxy** — Interceptação HTTP/HTTPS
- **LiteDB** — Banco de dados embutido (sem instalação)
- **Windows Service** — Execução em segundo plano
- **WinInet API** — Configuração de proxy do Windows

---

##  Notas

- O cliente requer execução como **LocalSystem** para configurar o proxy do Windows
- O serviço do cliente é protegido contra desativação por usuários padrão
- Para HTTPS, o proxy instala um certificado raiz automaticamente (Titanium.Web.Proxy)
- Os dados são armazenados em `C:\ProgramData\ProxyEdu\data.db`
