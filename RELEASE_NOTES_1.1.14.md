# ProxyEdu v1.1.14 — Nota de atualização

Data de lançamento: 5 de agosto de 2026

## Visão geral

A versão 1.1.14 do ProxyEdu aprimora o sistema de acesso temporário apresentado na versão anterior. Esta atualização torna a experiência mais clara e profissional, adiciona o cancelamento imediato do tempo concedido e corrige a restauração das configurações originais do aluno.

O fluxo foi revisado de ponta a ponta, incluindo interface, API e gerenciamento de estado no servidor.

## Destaques

- Nova interface para conceder e gerenciar acesso temporário.
- Encerramento manual do acesso antes do prazo.
- Contagem regressiva atualizada em tempo real.
- Renovação e ajuste da duração de um acesso ativo.
- Restauração correta do estado anterior do aluno.
- Validações adicionais na interface e na API.
- Versão sincronizada entre servidor, cliente, biblioteca compartilhada e instalador.

## Nova experiência de acesso temporário

O antigo campo de entrada do navegador foi substituído por uma janela integrada ao padrão visual do ProxyEdu.

O novo fluxo oferece:

- opções rápidas de 5 minutos a 4 horas;
- duração personalizada de 1 minuto a 24 horas;
- previsão da data e do horário de encerramento;
- identificação clara do aluno selecionado;
- mensagens de validação antes do envio;
- indicação visual durante o processamento.

Quando um aluno já possui acesso temporário, a mesma janela permite atualizar sua duração ou encerrar a liberação imediatamente.

## Cancelamento do acesso temporário

Professores e Administradores agora podem selecionar **Encerrar agora** para cancelar uma liberação ativa.

Ao cancelar, o ProxyEdu:

1. encerra o acesso temporário;
2. restaura as configurações existentes antes da concessão;
3. atualiza imediatamente a interface;
4. comunica a alteração aos usuários conectados.

O controle está disponível no Monitor, na lista de alunos e na tela de detalhes do aluno.

## Indicadores e contagem regressiva

Alunos com acesso temporário ativo agora apresentam:

- estado visual específico;
- tempo restante no Monitor;
- contagem regressiva na lista de alunos;
- horário completo de expiração nos detalhes;
- atualização automática após o término.

Essas informações tornam mais fácil acompanhar liberações durante as atividades em sala de aula.

## Correções de confiabilidade

### Restauração da liberação total

Anteriormente, o término do acesso temporário sempre desativava a liberação total. Agora o sistema registra se essa opção já estava ativa e restaura seu valor corretamente.

### Renovação sem perda do estado original

Renovar um acesso ativo não substitui mais o estado salvo na primeira concessão. Assim, o cancelamento ou a expiração continuam restaurando a configuração realmente existente antes do acesso temporário.

### Acesso expirado ainda não processado

Ao conceder um novo período depois de uma expiração recente, o sistema restaura primeiro o estado anterior e somente depois registra a nova concessão.

### Conflitos com ações administrativas

Ações como bloquear, desbloquear, liberar todos os sites ou restaurar filtros agora encerram corretamente qualquer período temporário conflitante, evitando alterações inesperadas posteriores.

## Segurança e API

- Novo endpoint autenticado para encerramento do acesso temporário.
- Permissões mantidas para Professores e Administradores.
- Duração aceita pela API limitada ao intervalo de 1 a 1.440 minutos.
- Resposta adequada quando o aluno informado não existe.
- Retorno do estado atualizado do aluno após concessão ou cancelamento.

## Atualização de versão

| Item | Versão anterior | Nova versão |
|---|---:|---:|
| Product Version | 1.1.13 | 1.1.14 |
| Assembly Version | 1.1.13.0 | 1.1.14.0 |
| File Version | 1.1.13.0 | 1.1.14.0 |

As versões do Servidor, Cliente, biblioteca compartilhada e instalador foram sincronizadas.

O endpoint de integridade do servidor também passou a obter a versão diretamente do assembly, eliminando uma identificação fixa e divergente.

## Validação

- Servidor, Cliente e biblioteca compartilhada compilados sem erros.
- Avisos existentes de compatibilidade com APIs exclusivas do Windows permanecem documentados e não impedem a compilação.
- JavaScript da interface validado sem erros de sintaxe.
- Metadados de versão sincronizados.
- Integridade das alterações verificada antes da publicação.

## Resumo

O ProxyEdu 1.1.14 transforma o acesso temporário em um recurso mais seguro, previsível e simples de operar. Professores passam a ter controle completo sobre o período concedido, enquanto o servidor garante que as políticas anteriores do aluno sejam restauradas corretamente.
