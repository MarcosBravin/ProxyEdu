# Instalação do ProxyEdu

## Antes de instalar

- Use computadores Windows suportados e mantenha o sistema operacional atualizado.
- Defina qual computador executará o Server e mantenha seu endereço de rede estável.
- Confirme que a implantação e o monitoramento foram autorizados pela instituição.
- Planeje firewall, segmentação de rede, retenção de registros e backups.

## Obter o instalador da última release

A instalação deve começar pela [última release estável oficial](https://github.com/MarcosBravin/ProxyEdu/releases). Não use instaladores antigos, cópias hospedadas por terceiros ou arquivos recebidos fora dos canais oficiais.

1. Abra a página de [Releases](https://github.com/MarcosBravin/ProxyEdu/releases).
2. Selecione a release estável mais recente, sem indicação de rascunho ou pré-lançamento.
3. Na lista de arquivos, baixe o instalador oficial com nome semelhante a `ProxyEdu-Setup-vAAAA.B.C.D.exe`.
4. Na página da mesma release, confira o nome do arquivo, o SHA-256 e as informações de assinatura publicadas.
5. No Windows, clique com o botão direito no arquivo `.exe`, abra **Propriedades** e consulte a guia **Assinaturas Digitais**, quando disponível.
6. Só prossiga se a origem, a assinatura e a integridade forem compatíveis com a release escolhida.

Não execute o arquivo se qualquer uma dessas verificações falhar. Em caso de dúvida, interrompa a instalação e use os canais oficiais do projeto.

## Ordem recomendada

1. Execute o `.exe` baixado no computador escolhido para o Server, autorizando a instalação quando o Windows solicitar.
2. Conclua a configuração inicial e restrinja o Dashboard à rede administrativa autorizada.
3. Troque imediatamente a credencial administrativa inicial.
4. Faça backup das configurações e do banco antes de ampliar a implantação.
5. Instale o Client primeiro em uma estação piloto.
6. Confirme descoberta ou endereçamento do Server, aplicação das políticas, certificado e restauração de acesso.
7. Somente depois da validação distribua o Client às demais estações autorizadas.

## Cuidados essenciais

- Instale o certificado raiz somente em equipamentos administrados pela instituição.
- Proteja a chave privada, credenciais, banco, logs e backups.
- Não exponha o Dashboard diretamente à internet.
- Em redes segmentadas, valide as regras entre as estações e o Server com a equipe de TI.
- Documente como remover o Client, restaurar a configuração de rede e revogar certificados.

Consulte também a [Política de Segurança](SECURITY.md), a [Política de Privacidade](https://proxyedu.bravintech.com/privacidade.html), a [Central LGPD](https://proxyedu.bravintech.com/lgpd.html), o [Código de Conduta](CODE_OF_CONDUCT.md) e as orientações de [Contribuição](CONTRIBUTING.md).
