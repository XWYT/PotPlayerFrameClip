[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$ExecutablePath
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $env:TEMP ('FrameClipInstallTests-' + [Guid]::NewGuid().ToString('N'))
$stageDirectory = Join-Path $testRoot 'release-stage'
$playerDirectory = Join-Path $testRoot 'PotPlayer Portable'
$menusDirectory = Join-Path $playerDirectory 'Menus'
$installDirectory = Join-Path $testRoot 'installed'
$dataDirectory = Join-Path $testRoot 'user-data'
$iniPath = Join-Path $playerDirectory 'PotPlayerMini64.ini'
$customMenuPath = Join-Path $menusDirectory 'CustomMenu.xml'
$legacyMenuName = 'ResolveCaptureMenu.xml'
$legacyMenuPath = Join-Path $menusDirectory $legacyMenuName

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

try {
    New-Item -ItemType Directory -Force -Path $stageDirectory, $menusDirectory | Out-Null

    # 假播放器目录只参与路径和配置测试，不启动任何进程，也不会访问本机 PotPlayer。
    [IO.File]::WriteAllBytes((Join-Path $playerDirectory 'PotPlayerMini64.exe'), [byte[]](0x4D, 0x5A))
    [IO.File]::Copy($ExecutablePath, (Join-Path $stageDirectory 'PotPlayerFrameClip.exe'), $true)
    Copy-Item -LiteralPath (Join-Path $projectRoot 'app.config') -Destination (Join-Path $stageDirectory 'PotPlayerFrameClip.exe.config')
    Copy-Item -LiteralPath (Join-Path $projectRoot 'menu\FrameClipMenu.xml') -Destination (Join-Path $stageDirectory 'FrameClipMenu.xml')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install.ps1') -Destination $stageDirectory
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uninstall.ps1') -Destination $stageDirectory

    $customMenu = @'
<?xml version="1.0" encoding="utf-8"?>
<Menu Title="Custom">
  <SubMenu Name="用户菜单"><MenuItem CmdID="ID_APP_ABOUT" Name="关于" /></SubMenu>
  <MenuItem CmdID="ID_PLAY_PAUSE" />
</Menu>
'@
    [IO.File]::WriteAllText($customMenuPath, $customMenu, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($legacyMenuPath, '<Menu Title="Legacy" />', [Text.UTF8Encoding]::new($false))

    # 使用 UTF-8 无 BOM 和中文内容，验证安装与卸载不会把第三方便携版 INI 改成乱码。
    $iniText = "[Settings]`r`nLastMenuName=CustomMenu.xml`r`nLanguage=简体中文`r`n"
    [IO.File]::WriteAllText($iniPath, $iniText, [Text.UTF8Encoding]::new($false))

    $installScript = Join-Path $stageDirectory 'install.ps1'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installScript `
        -PotPlayerDirectory $playerDirectory -InstallDirectory $installDirectory -DataDirectory $dataDirectory -TestMode
    if ($LASTEXITCODE -ne 0) { throw "隔离安装测试失败，退出代码：$LASTEXITCODE" }

    $generatedMenu = Join-Path $menusDirectory 'FrameClipMenu.xml'
    Assert-True (Test-Path -LiteralPath $generatedMenu) '安装后缺少 FrameClipMenu.xml。'
    Assert-True (-not (Test-Path -LiteralPath $legacyMenuPath)) '旧版菜单文件未被迁移。'
    Assert-True ((Get-ChildItem -LiteralPath (Join-Path $dataDirectory 'legacy-backups') -Filter ($legacyMenuName + '.*.bak')).Count -eq 1) `
        '旧版菜单没有创建迁移备份。'
    Assert-True (Test-Path -LiteralPath (Join-Path $dataDirectory 'install-state.json')) '安装状态文件缺失。'
    Assert-True (Test-Path -LiteralPath (Join-Path $installDirectory 'PotPlayerFrameClip.exe')) '程序文件未安装。'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $installDirectory 'install-state.json'))) '用户状态错误地写入了自定义程序目录。'

    [xml]$merged = [IO.File]::ReadAllText($generatedMenu)
    $frameClipTitle = '参照帧与片段截取'
    Assert-True (@($merged.Menu.SubMenu | Where-Object { $_.Name -eq $frameClipTitle }).Count -eq 1) '扩展菜单数量不正确。'
    Assert-True (@($merged.Menu.SubMenu | Where-Object { $_.Name -eq '用户菜单' }).Count -eq 1) '用户原有菜单没有保留。'
    $installedIni = [IO.File]::ReadAllText($iniPath, [Text.UTF8Encoding]::new($false))
    Assert-True ($installedIni.Contains('LastMenuName=FrameClipMenu.xml')) '安装后没有选择扩展菜单。'
    Assert-True ($installedIni.Contains('Language=简体中文')) '安装过程损坏了 UTF-8 INI 内容。'
    $frameClipConfigPath = Join-Path $dataDirectory 'FrameClip.ini'
    $frameClipConfig = [IO.File]::ReadAllText($frameClipConfigPath, [Text.UTF8Encoding]::new($false))
    Assert-True ($frameClipConfig.Contains('ExportRec709ForHdr=False')) 'HDR Rec.709 副本默认值不正确。'
    Assert-True ($frameClipConfig.Contains('Language=zh-CN')) '默认界面语言不正确。'
    Assert-True ($frameClipConfig.Contains('PotPlayerMenuPath=' + $generatedMenu)) '配置没有记录 PotPlayer 菜单路径。'

    # 重复安装必须保持幂等，不能嵌套或重复加入同一个子菜单。
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installScript `
        -PotPlayerDirectory $playerDirectory -InstallDirectory $installDirectory -DataDirectory $dataDirectory -TestMode
    if ($LASTEXITCODE -ne 0) { throw "重复安装测试失败，退出代码：$LASTEXITCODE" }
    [xml]$reinstalled = [IO.File]::ReadAllText($generatedMenu)
    Assert-True (@($reinstalled.Menu.SubMenu | Where-Object { $_.Name -eq $frameClipTitle }).Count -eq 1) '重复安装产生了重复菜单。'
    $state = Get-Content -LiteralPath (Join-Path $dataDirectory 'install-state.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-True ($state.PreviousMenuName -eq 'CustomMenu.xml') '重复安装丢失了安装前菜单名。'
    Assert-True ([bool]$state.PreviousMenuValueExists) '重复安装丢失了菜单键存在状态。'
    Assert-True ($state.InstallDirectory -eq [IO.Path]::GetFullPath($installDirectory)) '安装状态没有记录程序目录。'
    Assert-True ($state.DataDirectory -eq [IO.Path]::GetFullPath($dataDirectory)) '安装状态没有记录用户数据目录。'

    # 升级安装必须保留已选语言，并同时清理中文或英文的旧扩展菜单节点。
    $englishConfig = [regex]::Replace($frameClipConfig, '(?m)^Language=.*$', 'Language=en-US')
    [IO.File]::WriteAllText($frameClipConfigPath, $englishConfig, [Text.UTF8Encoding]::new($false))
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installScript `
        -PotPlayerDirectory $playerDirectory -InstallDirectory $installDirectory -DataDirectory $dataDirectory -TestMode
    if ($LASTEXITCODE -ne 0) { throw "英文菜单升级测试失败，退出代码：$LASTEXITCODE" }
    [xml]$englishMenu = [IO.File]::ReadAllText($generatedMenu)
    Assert-True (@($englishMenu.Menu.SubMenu | Where-Object { $_.Name -eq 'Reference Frame & Clip Capture' }).Count -eq 1) '升级安装没有保留英文菜单。'
    Assert-True (@($englishMenu.Menu.SubMenu | Where-Object { $_.Name -eq $frameClipTitle }).Count -eq 0) '英文菜单中仍残留中文扩展节点。'
    Assert-True (@($englishMenu.Menu.SubMenu | Where-Object { $_.Name -eq '用户菜单' }).Count -eq 1) '英文菜单升级破坏了用户菜单。'
    Assert-True (@($englishMenu.Menu.SubMenu[0].MenuItem | Where-Object { $_.CmdID })[0].Name -like 'Capture current frame*') '英文截图菜单项不正确。'

    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $installDirectory 'uninstall.ps1') `
        -InstallDirectory $installDirectory -DataDirectory $dataDirectory -TestMode
    if ($LASTEXITCODE -ne 0) { throw "隔离卸载测试失败，退出代码：$LASTEXITCODE" }
    Assert-True (-not (Test-Path -LiteralPath $generatedMenu)) '卸载后扩展菜单仍然存在。'
    $restoredIni = [IO.File]::ReadAllText($iniPath, [Text.UTF8Encoding]::new($false))
    Assert-True ($restoredIni.Contains('LastMenuName=CustomMenu.xml')) '卸载没有恢复安装前菜单。'
    Assert-True ($restoredIni.Contains('Language=简体中文')) '卸载过程损坏了 UTF-8 INI 内容。'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $installDirectory 'PotPlayerFrameClip.exe'))) '卸载后程序文件仍然存在。'

    # 安装前没有 LastMenuName 时，卸载也应删除安装期间新增的键，而不是留下空值。
    [IO.File]::WriteAllText($iniPath, "[Settings]`r`nLanguage=简体中文`r`n", [Text.UTF8Encoding]::new($false))
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installScript `
        -PotPlayerDirectory $playerDirectory -InstallDirectory $installDirectory -DataDirectory $dataDirectory -TestMode
    if ($LASTEXITCODE -ne 0) { throw "无原菜单键安装测试失败，退出代码：$LASTEXITCODE" }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $installDirectory 'uninstall.ps1') `
        -InstallDirectory $installDirectory -DataDirectory $dataDirectory -TestMode
    if ($LASTEXITCODE -ne 0) { throw "无原菜单键卸载测试失败，退出代码：$LASTEXITCODE" }
    $restoredWithoutKey = [IO.File]::ReadAllText($iniPath, [Text.UTF8Encoding]::new($false))
    Assert-True (-not $restoredWithoutKey.Contains('LastMenuName=')) '卸载后遗留了安装前不存在的菜单键。'

    Write-Host 'Installation regression tests passed.'
}
finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
