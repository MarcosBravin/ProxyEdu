# Instalação do ProxyEdu

## Antes de instalar

- Use computadores Windows suportados e mantenha o sistema operacional atualizado.
- Defina qual computador executará o Server e mantenha seu endereço de rede estável.
- Confirme que a implantação e o monitoramento foram autorizados pela instituição.
- Planeje firewall, segmentação de rede, retenção de registros e backups.

## Obter o instalador

1. Abra a página de [Releases](https://github.com/MarcosBravin/ProxyEdu/releases).
2. Baixe apenas o instalador anexado à release oficial desejada.
3. Calcule o SHA-256 do arquivo e compare-o com o valor publicado na release.

No PowerShell:

```powershell
Get-FileHash -Algorithm SHA256 .\NOME-DO-INSTALADOR.exe
```

Não execute o arquivo se o hash, a origem ou a assinatura não puderem ser validados.

## Ordem recomendada

1. Execute o instalador como administrador no computador escolhido para o Server.
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

Consulte também a [Política de Segurança](SECURITY.md), a [Política de Privacidade](https://proxyedu.bravintech.com/privacidade.html) e a [Central LGPD](https://proxyedu.bravintech.com/lgpd.html).
