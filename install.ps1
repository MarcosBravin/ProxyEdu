param(
    [ValidateSet("server", "client")]
    [string]$Role,
    [string]$ServerIp = "",
    [string]$CertPath = "",
    [string]$ServerCertSharePath = "",
    [string]$PublishRoot = "",
    [switch]$SkipCertificate,
    [switch]$NoPrompt
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Write-Info([string]$Message) {
    Write-Host "[INFO] $Message" -ForegroundColor Cyan
}

function Write-Ok([string]$Message) {
    Write-Host "[OK]   $Message" -ForegroundColor Green
}

function Write-WarnLine([string]$Message) {
    Write-Host "[WARN] $Message" -ForegroundColor Yellow
}

function Fail([string]$Message) {
    throw $Message
}

function Assert-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltinRole]::Administrator)) {
        Fail "Execute este instalador como Administrador."
    }
}

function Select-RoleInteractive {
    Write-Host "============================================"
    Write-Host " ProxyEdu - Instalador Unico"
    Write-Host "============================================"
    Write-Host ""
    Write-Host "Escolha o tipo de instalacao:"
    Write-Host "1) Servidor (PC do Professor)"
    Write-Host "2) Cliente (PC do Aluno)"
    Write-Host ""

    do {
        $choice = Read-Host "Digite 1 ou 2"
    } while ($choice -notin @("1", "2"))

    if ($choice -eq "1") { return "server" }
    return "client"
}

function Resolve-PublishDirectory([string]$SelectedRole) {
    $basePublishRoot = $PublishRoot
    if ([string]::IsNullOrWhiteSpace($basePublishRoot)) {
        $basePublishRoot = Join-Path $PSScriptRoot "artifacts\publish"
    }

    $primary = Join-Path $basePublishRoot $SelectedRole
    if (Test-Path $primary) {
        return (Resolve-Path $primary).Path
    }

    $fallback = Join-Path $PSScriptRoot $SelectedRole
    if (Test-Path $fallback) {
        return (Resolve-Path $fallback).Path
    }

    Fail "Pasta de publicacao nao encontrada para '$SelectedRole'. Esperado: '$primary'. Rode .\build.bat -Action publish -Target $SelectedRole"
}

function Install-Binary([string]$SourceDir, [string]$TargetDir) {
    Write-Info "Copiando arquivos para $TargetDir"
    New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
    Get-ChildItem -Path $SourceDir -Force | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination $TargetDir -Recurse -Force
    }
    Write-Ok "Arquivos copiados."
}

function Ensure-Service([string]$ServiceName, [string]$DisplayName, [string]$ExePath, [string]$Description) {
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($service) {
        Write-Info "Atualizando servico existente: $ServiceName"
        if ($service.Status -ne "Stopped") {
            Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 2
        }
        & sc.exe delete $ServiceName | Out-Null
        Start-Sleep -Seconds 2
    }

    $binPath = '"' + $ExePath + '"'
    $createOutput = & sc.exe create $ServiceName binPath= $binPath start= auto obj= LocalSystem DisplayName= $DisplayName
    if ($LASTEXITCODE -ne 0) {
        Fail "Falha ao criar servico '$ServiceName'. Saida: $createOutput"
    }

    & sc.exe description $ServiceName $Description | Out-Null
    & sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/5000 | Out-Null
    Start-Service -Name $ServiceName
    Write-Ok "Servico '$ServiceName' instalado e iniciado."
}

function Wait-ServiceRunning([string]$ServiceName, [int]$TimeoutSeconds = 30) {
    $limit = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $limit) {
        $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if ($service -and $service.Status -eq "Running") {
            return
        }
        Start-Sleep -Seconds 1
    }

    Fail "Servico '$ServiceName' nao ficou em estado Running dentro de ${TimeoutSeconds}s."
}

function Find-ProxyRootCertificate {
    $stores = @(
        @{ Name = "Root"; Location = "LocalMachine" },
        @{ Name = "CA"; Location = "LocalMachine" },
        @{ Name = "My"; Location = "LocalMachine" }
    )

    foreach ($storeRef in $stores) {
        $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($storeRef.Name, $storeRef.Location)
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
        try {
            $cert = $store.Certificates |
                Where-Object {
                    $_.Subject -match "Titanium|ProxyEdu" -and
                    $_.Subject -match "Root|Authority|CA"
                } |
                Sort-Object NotBefore -Descending |
                Select-Object -First 1
            if ($cert) {
                return $cert
            }
        }
        finally {
            $store.Close()
        }
    }

    return $null
}

function Export-ServerRootCertificate([string]$OutputFile) {
    $cert = $null
    for ($i = 0; $i -lt 20; $i++) {
        $cert = Find-ProxyRootCertificate
        if ($cert) { break }
        Start-Sleep -Seconds 1
    }

    if (-not $cert) {
        Fail "Nao foi possivel localizar certificado raiz do proxy no servidor."
    }

    $outputDir = Split-Path -Path $OutputFile -Parent
    New-Item -Path $outputDir -ItemType Directory -Force | Out-Null
    Export-Certificate -Cert $cert -FilePath $OutputFile -Force | Out-Null
    Write-Ok "Certificado exportado para: $OutputFile"
}

function Import-RootCertificate([string]$CertificateFile) {
    if (-not (Test-Path $CertificateFile)) {
        Fail "Arquivo de certificado nao encontrado: $CertificateFile"
    }

    Import-Certificate -FilePath $CertificateFile -CertStoreLocation "Cert:\LocalMachine\Root" | Out-Null
    Write-Ok "Certificado importado em LocalMachine\\Root."
}

function Resolve-ClientCertificatePath {
    if (-not [string]::IsNullOrWhiteSpace($CertPath) -and (Test-Path $CertPath)) {
        return $CertPath
    }

    $candidates = @(
        (Join-Path $PSScriptRoot "ProxyEdu-RootCA.cer"),
        "C:\ProgramData\ProxyEdu\certs\ProxyEdu-RootCA.cer",
        "C:\ProxyEdu\ProxyEdu-RootCA.cer"
    )

    foreach ($path in $candidates) {
        if (Test-Path $path) {
            return $path
        }
    }

    if (-not $NoPrompt) {
        Write-WarnLine "Nao encontrei o certificado automaticamente."
        Write-Host "Informe o caminho completo do .cer exportado no servidor."
        $manualPath = Read-Host "Certificado (.cer)"
        if (-not [string]::IsNullOrWhiteSpace($manualPath) -and (Test-Path $manualPath)) {
            return $manualPath
        }
    }

    return ""
}

function Try-CopyCertificateFromServer([string]$TargetPath) {
    $sources = @()

    if (-not [string]::IsNullOrWhiteSpace($ServerCertSharePath)) {
        $sources += $ServerCertSharePath
    }

    if (-not [string]::IsNullOrWhiteSpace($ServerIp)) {
        $sources += "\\$ServerIp\c$\ProgramData\ProxyEdu\certs\ProxyEdu-RootCA.cer"
        $sources += "\\$ServerIp\ProgramData\ProxyEdu\certs\ProxyEdu-RootCA.cer"
    }

    foreach ($source in $sources | Select-Object -Unique) {
        try {
            if (Test-Path $source) {
                $destDir = Split-Path -Path $TargetPath -Parent
                New-Item -ItemType Directory -Path $destDir -Force | Out-Null
                Copy-Item -Path $source -Destination $TargetPath -Force
                Write-Ok "Certificado copiado automaticamente do servidor: $source"
                return $true
            }
        }
        catch {
            Write-WarnLine "Falha ao tentar copiar certificado de '$source'."
        }
    }

    return $false
}

function Update-ClientServerIp([string]$AppSettingsPath, [string]$TargetServerIp) {
    if ([string]::IsNullOrWhiteSpace($TargetServerIp)) {
        return
    }

    if (-not (Test-Path $AppSettingsPath)) {
        Write-WarnLine "appsettings.json do cliente nao encontrado para atualizar IP fixo."
        return
    }

    $raw = Get-Content -Path $AppSettingsPath -Raw
    $json = $raw | ConvertFrom-Json
    $json.Server.Ip = $TargetServerIp
    $json.Server.AutoDiscover = $false
    ($json | ConvertTo-Json -Depth 10) | Set-Content -Path $AppSettingsPath -Encoding UTF8
    Write-Ok "Configuracao do cliente atualizada para servidor fixo: $TargetServerIp"
}

function Install-Server {
    $source = Resolve-PublishDirectory -SelectedRole "server"
    $target = Join-Path $env:ProgramFiles "ProxyEdu\Server"
    $exe = Join-Path $target "ProxyEdu.Server.exe"

    Install-Binary -SourceDir $source -TargetDir $target
    if (-not (Test-Path $exe)) {
        Fail "Executavel do servidor nao encontrado: $exe"
    }

    Ensure-Service -ServiceName "ProxyEduServer" -DisplayName "ProxyEdu Server" -ExePath $exe -Description "ProxyEdu - Servico de servidor/proxy"
    Wait-ServiceRunning -ServiceName "ProxyEduServer" -TimeoutSeconds 30

    $serverCertPath = Join-Path $env:ProgramData "ProxyEdu\certs\ProxyEdu-RootCA.cer"
    Export-ServerRootCertificate -OutputFile $serverCertPath

    $portableCopy = Join-Path $PSScriptRoot "ProxyEdu-RootCA.cer"
    try {
        Copy-Item -Path $serverCertPath -Destination $portableCopy -Force
        Write-Ok "Copia do certificado criada em: $portableCopy"
    }
    catch {
        Write-WarnLine "Nao foi possivel copiar o certificado para a pasta do instalador. Continue usando: $serverCertPath"
    }

    Write-Host ""
    Write-Host "Instalacao do servidor concluida."
    Write-Host "Dashboard: http://localhost:5000"
    Write-Host "Certificado para clientes: $serverCertPath"
}

function Install-Client {
    $source = Resolve-PublishDirectory -SelectedRole "client"
    $target = Join-Path $env:ProgramFiles "ProxyEdu\Client"
    $exe = Join-Path $target "ProxyEdu.Client.exe"
    $appSettings = Join-Path $target "appsettings.json"

    Install-Binary -SourceDir $source -TargetDir $target
    if (-not (Test-Path $exe)) {
        Fail "Executavel do cliente nao encontrado: $exe"
    }

    Update-ClientServerIp -AppSettingsPath $appSettings -TargetServerIp $ServerIp

    if (-not $SkipCertificate) {
        $resolvedCertPath = Resolve-ClientCertificatePath

        if ([string]::IsNullOrWhiteSpace($resolvedCertPath)) {
            $cachedCert = Join-Path $env:ProgramData "ProxyEdu\certs\ProxyEdu-RootCA.cer"
            $copied = Try-CopyCertificateFromServer -TargetPath $cachedCert
            if ($copied -and (Test-Path $cachedCert)) {
                $resolvedCertPath = $cachedCert
            }
        }

        if ([string]::IsNullOrWhiteSpace($resolvedCertPath)) {
            Fail "Certificado raiz nao informado para o cliente. Passe -CertPath, informe -ServerIp acessivel, ou mantenha ProxyEdu-RootCA.cer ao lado deste instalador."
        }

        Import-RootCertificate -CertificateFile $resolvedCertPath
    }
    else {
        Write-WarnLine "Instalacao de certificado ignorada por parametro -SkipCertificate."
    }

    Ensure-Service -ServiceName "ProxyEduClient" -DisplayName "ProxyEdu Client" -ExePath $exe -Description "ProxyEdu - Servico cliente"
    Wait-ServiceRunning -ServiceName "ProxyEduClient" -TimeoutSeconds 30

    Write-Host ""
    Write-Host "Instalacao do cliente concluida."
}

try {
    Assert-Admin

    if ([string]::IsNullOrWhiteSpace($Role)) {
        if ($NoPrompt) {
            Fail "Parametro -Role e obrigatorio com -NoPrompt. Use: server ou client."
        }
        $Role = Select-RoleInteractive
    }

    Write-Info "Perfil selecionado: $Role"

    switch ($Role) {
        "server" { Install-Server }
        "client" { Install-Client }
        default { Fail "Role invalido: $Role" }
    }

    Write-Host ""
    Write-Ok "Processo finalizado com sucesso."
}
catch {
    Write-Host ""
    Write-Host "[ERRO] $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

if (-not $NoPrompt) {
    Write-Host ""
    pause
}
