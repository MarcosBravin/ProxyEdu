# ProxyEdu — Sistema de Controle de Acesso Escolar

Sistema de proxy educacional para controle de acesso à internet em ambientes escolares e laboratórios.

O ProxyEdu permite que professores e administradores monitorem e controlem, em tempo real, o uso da internet em computadores de alunos, com foco em simplicidade de instalação e operação em redes locais.

---

## Visão Geral

O ProxyEdu foi projetado para cenários onde há necessidade de controle centralizado de acesso à internet, como:

* Escolas
* Laboratórios de informática
* Cursos profissionalizantes
* Ambientes educacionais em rede local

---

## Arquitetura

```
[PC Aluno 1] ─┐
[PC Aluno 2] ─┼──► [PC Professor - ProxyEdu Server] ◄── [Dashboard http://localhost:5000]
[PC Aluno 3] ─┘       (proxy na porta 8888)
```

---

## Projetos

| Projeto         | Descrição                                             |
| --------------- | ----------------------------------------------------- |
| ProxyEdu.Server | Servidor proxy, API REST e dashboard web              |
| ProxyEdu.Client | Serviço Windows instalado nos computadores dos alunos |
| ProxyEdu.Shared | Modelos e contratos compartilhados                    |

---

## Status do Projeto

Este projeto está em desenvolvimento ativo.

A versão atual deve ser considerada uma edição Community/Experimental, podendo sofrer alterações estruturais e funcionais.

---

## Instalação

### CLI de Compilação

A partir da raiz do projeto:

```bash
.\build.bat
```

Exemplos:

```bash
.\build.bat -Action restore -Target all
.\build.bat -Action build -Target server -Configuration Debug
.\build.bat -Action publish -Target client -Runtime win-x64 -SelfContained true
.\build.bat -Action clean -Target all
```

Parâmetros disponíveis:

* Action: restore, build, publish, clean
* Target: all, server, client, shared
* Configuration: Debug ou Release
* Runtime: RID do .NET (ex.: win-x64)
* SelfContained: true/false
* OutputRoot: diretório de saída (padrão: .\artifacts\publish)

---

## Pré-requisitos

* .NET 8 SDK
* Windows 10 ou 11
* Visual Studio 2022 ou VS Code

---

## Servidor (PC do Professor)

```bash
cd ProxyEdu.Server
dotnet publish -c Release -r win-x64 --self-contained -o ./publish
```

Execute o script `install-server.bat` como administrador.

Dashboard disponível em:
http://localhost:5000

Credenciais iniciais:

* usuário: admin
* senha: admin123

Perfis de acesso:

* Administrador: gerenciamento completo de usuários
* Professor: acesso ao dashboard sem gerenciamento de usuários

---

## Cliente (PC dos Alunos)

Edite o arquivo:

ProxyEdu.Client/appsettings.json

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

* Ip vazio com AutoDiscover habilitado permite descoberta automática na rede local
* Para configuração manual, informe o IP do servidor

Publicação:

```bash
cd ProxyEdu.Client
dotnet publish -c Release -r win-x64 --self-contained -o ./publish
```

Execute `install-client.bat` como administrador em cada máquina.

---

## Instalador

O projeto inclui um instalador baseado em NSIS com seleção de componentes:

* Cliente (serviço Windows)
* Servidor (serviço Windows + dashboard)

Geração:

```bash
installer\build-installer.bat
```

Saída:

artifacts\installer\ProxyEduInstaller.exe

Requisitos:

* Builds previamente gerados em artifacts\publish
* NSIS 3.x instalado

---

## Desinstalação

Scripts disponíveis:

* uninstall-server.bat
* uninstall-client.bat
* uninstall-all.bat

Executar como administrador.

---

## Funcionalidades

### Gerenciamento de Alunos

* Visualização em tempo real dos dispositivos conectados
* Identificação por IP, MAC, hostname e sistema operacional
* Organização por grupos
* Histórico de navegação por aluno

### Controle de Acesso

* Bloqueio e liberação individual
* Bloqueio e liberação global
* Controle por grupos

### Regras de Acesso

Whitelist:

* Domínios permitidos
* Suporte a wildcard

Blacklist:

* Domínios bloqueados
* Suporte a wildcard e regex
* Presets configuráveis

### Monitoramento

* Logs completos de navegação
* Filtros por aluno, domínio e status
* Exportação de dados
* Limpeza automática configurável

### Estatísticas

* Domínios mais acessados
* Taxa de bloqueio
* Volume de tráfego
* Atualização em tempo real

### Configurações

* Porta do proxy
* Modo whitelist total
* Mensagem personalizada de bloqueio
* Política de retenção de logs

---

## API REST

| Método  | Endpoint                   | Descrição      |
| ------- | -------------------------- | -------------- |
| GET     | /api/students              | Lista alunos   |
| POST    | /api/students/{id}/block   | Bloqueia aluno |
| POST    | /api/students/{id}/unblock | Libera aluno   |
| POST    | /api/students/block-all    | Bloqueia todos |
| POST    | /api/students/unblock-all  | Libera todos   |
| GET     | /api/filters               | Lista filtros  |
| POST    | /api/filters               | Cria filtro    |
| DELETE  | /api/filters/{id}          | Remove filtro  |
| POST    | /api/filters/preset/{name} | Aplica preset  |
| GET     | /api/logs                  | Logs           |
| GET     | /api/students/stats        | Estatísticas   |
| GET/PUT | /api/settings              | Configurações  |

---

## Tecnologias

* ASP.NET Core 8
* SignalR
* Titanium.Web.Proxy
* LiteDB
* Windows Service
* WinInet API

---

## Notas Técnicas

* O cliente requer execução como LocalSystem para configuração de proxy
* O serviço do cliente é protegido contra desativação por usuários padrão
* Para HTTPS, um certificado raiz é instalado automaticamente
* Dados armazenados em: C:\ProgramData\ProxyEdu\data.db

---

## Licença e Uso

Este projeto está disponível como versão Community para estudo, testes e uso não comercial.

Para uso em ambientes produtivos, suporte, customizações ou implantação assistida, entre em contato.

---

## Contato

Caso tenha interesse em utilizar o ProxyEdu em ambiente real ou precise de suporte, abra uma issue ou entre em contato.
