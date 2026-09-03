# 只读采集脚本：不修改任何设置
$ErrorActionPreference = 'SilentlyContinue'
function Get-RegVal($path, $name) {
  try {
    $k = Get-ItemProperty -Path $path -Name $name -ErrorAction Stop
    $v = $k.$name
    if ($null -eq $v) { return '(未设置)' }
    return "$v"
  } catch { return '(未设置)' }
}
function Sec($t) { "`n===== $t =====" }

Sec '系统'
$os = Get-CimInstance Win32_OperatingSystem
"Caption: $($os.Caption)"
"Build: $($os.Version)  DisplayVersion: $(Get-RegVal 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' 'DisplayVersion')  UBR: $(Get-RegVal 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' 'UBR')"
"InstallDate: $($os.InstallDate)"
$cs = Get-CimInstance Win32_ComputerSystem
"Manufacturer/Model: $($cs.Manufacturer) / $($cs.Model)  PCSystemType: $($cs.PCSystemType) (1=Desktop 2=Mobile)"
"PartOfDomain: $($cs.PartOfDomain)  RAM(GB): $([math]::Round($cs.TotalPhysicalMemory/1GB,1))"
"Chassis: $((Get-CimInstance Win32_SystemEnclosure).ChassisTypes -join ',')"
"GPU: $((Get-CimInstance Win32_VideoController | % Name) -join ' | ')"
"CPU: $((Get-CimInstance Win32_Processor).Name)"
"IsAdmin(当前进程): $(([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole('Administrators'))"
"MSA登录(IdentityCRL存在): $(Test-Path 'HKCU:\Software\Microsoft\IdentityCRL\UserExtendedProperties')"
"MDM enrolled(Enrollments子键数): $((Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Enrollments' | ? { (Get-ItemProperty $_.PSPath).ProviderID }).Count)"
try { $lic = Get-CimInstance SoftwareLicensingProduct -Filter "PartialProductKey IS NOT NULL AND ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f'" | select -First 1; "激活: LicenseStatus=$($lic.LicenseStatus) (1=Licensed) $($lic.Name)" } catch { "激活: 查询失败" }
"winget: $(try { (winget --version) } catch { '不可用' })"
"pwsh7: $(if (Get-Command pwsh) { $PSVersionTable.PSVersion.ToString() } else { '未安装' })"

Sec '磁盘'
Get-PhysicalDisk | % { "PhysicalDisk: $($_.FriendlyName)  $($_.MediaType)  $([math]::Round($_.Size/1GB))GB  Bus=$($_.BusType)" }
Get-Volume | ? DriveLetter | sort DriveLetter | % { "Vol $($_.DriveLetter): $($_.FileSystemLabel) $($_.FileSystem) 总=$([math]::Round($_.Size/1GB))GB 剩=$([math]::Round($_.SizeRemaining/1GB))GB" }

Sec '安全'
try { $mp = Get-MpComputerStatus; "Defender: AV=$($mp.AntivirusEnabled) RTP=$($mp.RealTimeProtectionEnabled) Tamper=$($mp.IsTamperProtected) 签名日期=$($mp.AntivirusSignatureLastUpdated)" } catch { "Defender: 查询失败" }
"第三方AV: $((Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntiVirusProduct | % displayName) -join ' | ')"
"UAC EnableLUA=$(Get-RegVal 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' 'EnableLUA') ConsentPromptBehaviorAdmin=$(Get-RegVal 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' 'ConsentPromptBehaviorAdmin') (5=默认 2=总是通知 0=从不)"
try { $bl = Get-BitLockerVolume -MountPoint C: -ErrorAction Stop; "BitLocker C: Protection=$($bl.ProtectionStatus) Encrypt=$($bl.VolumeStatus) $($bl.EncryptionPercentage)%" } catch { "BitLocker: 需管理员权限或不可用" }
"内存完整性(HVCI) Enabled=$(Get-RegVal 'HKLM:\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity' 'Enabled')"
"SmartScreen(Explorer)=$(Get-RegVal 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer' 'SmartScreenEnabled')"
"Windows防火墙: $((Get-NetFirewallProfile | % { "$($_.Name)=$($_.Enabled)" }) -join ' ')"
"网络配置文件: $((Get-NetConnectionProfile | % { "$($_.InterfaceAlias)=$($_.NetworkCategory)" }) -join ' | ')"

Sec '隐私与推送'
"Telemetry 策略(HKLM Policies) AllowTelemetry=$(Get-RegVal 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection' 'AllowTelemetry')"
"Telemetry 当前(HKLM) AllowTelemetry=$(Get-RegVal 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection' 'AllowTelemetry')"
"Diag: TailoredExperiences=$(Get-RegVal 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Privacy' 'TailoredExperiencesWithDiagnosticDataEnabled')"
"广告ID Enabled=$(Get-RegVal 'HKCU:\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo' 'Enabled')"
"反馈频率 NumberOfSIUFInPeriod=$(Get-RegVal 'HKCU:\Software\Microsoft\Siuf\Rules' 'NumberOfSIUFInPeriod')"
"活动历史 PublishUserActivities=$(Get-RegVal 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System' 'PublishUserActivities')"
"位置 ConsentStore=$(Get-RegVal 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location' 'Value')"
"输入个性化 RestrictImplicitInk=$(Get-RegVal 'HKCU:\Software\Microsoft\InputPersonalization' 'RestrictImplicitInkCollection') RestrictImplicitText=$(Get-RegVal 'HKCU:\Software\Microsoft\InputPersonalization' 'RestrictImplicitTextCollection') AcceptedPrivacyPolicy=$(Get-RegVal 'HKCU:\Software\Microsoft\Personalization\Settings' 'AcceptedPrivacyPolicy')"
"语言列表访问 HttpAcceptLanguageOptOut=$(Get-RegVal 'HKCU:\Control Panel\International\User Profile' 'HttpAcceptLanguageOptOut')"
$cdm = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'
"设置应用建议 338393=$(Get-RegVal $cdm 'SubscribedContent-338393Enabled') 353694=$(Get-RegVal $cdm 'SubscribedContent-353694Enabled') 353696=$(Get-RegVal $cdm 'SubscribedContent-353696Enabled')"
"CDM: ContentDeliveryAllowed=$(Get-RegVal $cdm 'ContentDeliveryAllowed') SilentInstalledApps=$(Get-RegVal $cdm 'SilentInstalledAppsEnabled') SystemPaneSuggestions=$(Get-RegVal $cdm 'SystemPaneSuggestionsEnabled') SoftLanding=$(Get-RegVal $cdm 'SoftLandingEnabled') OemPreInstalled=$(Get-RegVal $cdm 'OemPreInstalledAppsEnabled') PreInstalled=$(Get-RegVal $cdm 'PreInstalledAppsEnabled')"
"锁屏聚焦 RotatingLockScreen=$(Get-RegVal $cdm 'RotatingLockScreenEnabled') Overlay=$(Get-RegVal $cdm 'RotatingLockScreenOverlayEnabled')  提示338389=$(Get-RegVal $cdm 'SubscribedContent-338389Enabled') 欢迎体验310093=$(Get-RegVal $cdm 'SubscribedContent-310093Enabled') 开始建议338388=$(Get-RegVal $cdm 'SubscribedContent-338388Enabled')"
"Copilot: TurnOffWindowsCopilot(HKCU策略)=$(Get-RegVal 'HKCU:\Software\Policies\Microsoft\Windows\WindowsCopilot' 'TurnOffWindowsCopilot') ShowCopilotButton=$(Get-RegVal 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'ShowCopilotButton')"
"Recall: DisableAIDataAnalysis(HKCU)=$(Get-RegVal 'HKCU:\Software\Policies\Microsoft\Windows\WindowsAI' 'DisableAIDataAnalysis')  AllowRecallEnablement(HKLM)=$(Get-RegVal 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsAI' 'AllowRecallEnablement')"
"Bing搜索 BingSearchEnabled=$(Get-RegVal 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Search' 'BingSearchEnabled') DisableSearchBoxSuggestions(策略)=$(Get-RegVal 'HKCU:\Software\Policies\Microsoft\Windows\Explorer' 'DisableSearchBoxSuggestions')"
"搜索高亮 IsDynamicSearchBoxEnabled=$(Get-RegVal 'HKCU:\Software\Microsoft\Windows\CurrentVersion\SearchSettings' 'IsDynamicSearchBoxEnabled') 云搜索MSA=$(Get-RegVal 'HKCU:\Software\Microsoft\Windows\CurrentVersion\SearchSettings' 'IsMSACloudSearchEnabled') 安全搜索=$(Get-RegVal 'HKCU:\Software\Microsoft\Windows\CurrentVersion\SearchSettings' 'SafeSearchMode')"
"剪贴板历史 EnableClipboardHistory=$(Get-RegVal 'HKCU:\Software\Microsoft\Clipboard' 'EnableClipboardHistory') 云同步=$(Get-RegVal 'HKCU:\Software\Microsoft\Clipboard' 'CloudClipboardAutomaticUpload')"
"Edge 策略键存在: $(Test-Path 'HKLM:\SOFTWARE\Policies\Microsoft\Edge')"

Sec '界面与交互'
$adv = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
"任务栏 TaskbarAl=$(Get-RegVal $adv 'TaskbarAl')(0左1中) TaskView=$(Get-RegVal $adv 'ShowTaskViewButton') Widgets TaskbarDa=$(Get-RegVal $adv 'TaskbarDa') Chat TaskbarMn=$(Get-RegVal $adv 'TaskbarMn') 搜索框 SearchboxTaskbarMode=$(Get-RegVal 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Search' 'SearchboxTaskbarMode')(0隐藏1图标2框3图标+标签)"
"任务栏 合并 TaskbarGlomLevel=$(Get-RegVal $adv 'TaskbarGlomLevel') 小图标 TaskbarSi=$(Get-RegVal $adv 'TaskbarSi') 结束任务 TaskbarEndTask=$(Get-RegVal $adv 'TaskbarEndTask')"
"开始菜单 Start_Layout=$(Get-RegVal $adv 'Start_Layout')(0默认1更多固定2更多推荐) Start_IrisRecommendations=$(Get-RegVal $adv 'Start_IrisRecommendations') TrackDocs=$(Get-RegVal $adv 'Start_TrackDocs') TrackProgs=$(Get-RegVal $adv 'Start_TrackProgs') Start_AccountNotifications=$(Get-RegVal $adv 'Start_AccountNotifications')"
"资源管理器 HideFileExt=$(Get-RegVal $adv 'HideFileExt') Hidden=$(Get-RegVal $adv 'Hidden') ShowSuperHidden=$(Get-RegVal $adv 'ShowSuperHidden') LaunchTo=$(Get-RegVal $adv 'LaunchTo')(1此电脑2快速访问3下载4主页) 紧凑视图 UseCompactMode=$(Get-RegVal $adv 'UseCompactMode') 复选框 AutoCheckSelect=$(Get-RegVal $adv 'AutoCheckSelect')"
$ex = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer'
"快速访问 ShowRecent=$(Get-RegVal $ex 'ShowRecent') ShowFrequent=$(Get-RegVal $ex 'ShowFrequent') ShowCloudFilesInQuickAccess=$(Get-RegVal $ex 'ShowCloudFilesInQuickAccess')"
"经典右键菜单 已启用=$(Test-Path 'HKCU:\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32')"
$per = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
"主题 AppsUseLightTheme=$(Get-RegVal $per 'AppsUseLightTheme') SystemUsesLightTheme=$(Get-RegVal $per 'SystemUsesLightTheme') 透明=$(Get-RegVal $per 'EnableTransparency') 强调色到标题栏 ColorPrevalence=$(Get-RegVal $per 'ColorPrevalence')"
$hdi = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel'
"桌面图标隐藏(1=隐藏) 此电脑=$(Get-RegVal $hdi '{20D04FE0-3AEA-1069-A2D8-08002B30309D}') 回收站=$(Get-RegVal $hdi '{645FF040-5081-101B-9F08-00AA002F954E}') 用户文件夹=$(Get-RegVal $hdi '{59031a47-3f72-44a7-89c5-5595fe6b30ee}') 控制面板=$(Get-RegVal $hdi '{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}')"
"贴靠 SnapAssist=$(Get-RegVal $adv 'SnapAssist') EnableSnapAssistFlyout=$(Get-RegVal $adv 'EnableSnapAssistFlyout') EnableSnapBar=$(Get-RegVal $adv 'EnableSnapBar') 晃动最小化 DisallowShaking=$(Get-RegVal $adv 'DisallowShaking')"
"鼠标 MouseSpeed=$(Get-RegVal 'HKCU:\Control Panel\Mouse' 'MouseSpeed') Th1=$(Get-RegVal 'HKCU:\Control Panel\Mouse' 'MouseThreshold1') Th2=$(Get-RegVal 'HKCU:\Control Panel\Mouse' 'MouseThreshold2') (1/6/10=加速开 0/0/0=关)"
"粘滞键 Flags=$(Get-RegVal 'HKCU:\Control Panel\Accessibility\StickyKeys' 'Flags')(510=默认含快捷键 506=关快捷键) 切换键=$(Get-RegVal 'HKCU:\Control Panel\Accessibility\ToggleKeys' 'Flags') 筛选键=$(Get-RegVal 'HKCU:\Control Panel\Accessibility\Keyboard Response' 'Flags')"
"视觉效果 VisualFXSetting=$(Get-RegVal 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects' 'VisualFXSetting')(0自动1最佳外观2最佳性能3自定义) MenuShowDelay=$(Get-RegVal 'HKCU:\Control Panel\Desktop' 'MenuShowDelay') 动画 MinAnimate=$(Get-RegVal 'HKCU:\Control Panel\Desktop\WindowMetrics' 'MinAnimate')"
"通知 全局Toast=$(Get-RegVal 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings' 'NOC_GLOBAL_SETTING_TOASTS_ENABLED')"
"游戏栏 GameDVR_Enabled=$(Get-RegVal 'HKCU:\System\GameConfigStore' 'GameDVR_Enabled') AppCaptureEnabled=$(Get-RegVal 'HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR' 'AppCaptureEnabled') UseNexusForGameBar=$(Get-RegVal 'HKCU:\Software\Microsoft\GameBar' 'UseNexusForGameBarEnabled')"
"缩放 DPI LogPixels=$(Get-RegVal 'HKCU:\Control Panel\Desktop' 'LogPixels') Win8DpiScaling=$(Get-RegVal 'HKCU:\Control Panel\Desktop' 'Win8DpiScaling')"

Sec '中文环境'
"系统区域: $((Get-WinSystemLocale).Name)  用户Culture: $((Get-Culture).Name)  UI语言: $((Get-UICulture).Name)  家庭位置: $((Get-WinHomeLocation).HomeLocation)"
"时区: $((Get-TimeZone).Id)  NTP: $(Get-RegVal 'HKLM:\SYSTEM\CurrentControlSet\Services\W32Time\Parameters' 'NtpServer')"
"系统代码页 ACP=$(Get-RegVal 'HKLM:\SYSTEM\CurrentControlSet\Control\Nls\CodePage' 'ACP') (65001=UTF-8 Beta 开)"
"输入语言列表:"
Get-WinUserLanguageList | % { "  $($_.LanguageTag)  IME: $($_.InputMethodTips -join ',')" }
"语言切换热键 Language Hotkey=$(Get-RegVal 'HKCU:\Keyboard Layout\Toggle' 'Language Hotkey') Hotkey=$(Get-RegVal 'HKCU:\Keyboard Layout\Toggle' 'Hotkey') Layout Hotkey=$(Get-RegVal 'HKCU:\Keyboard Layout\Toggle' 'Layout Hotkey') (1=Alt+Shift 2=Ctrl+Shift 3=无)"
"微软拼音 CHS 设置键:"
try { Get-ItemProperty 'HKCU:\Software\Microsoft\InputMethod\Settings\CHS' -ErrorAction Stop | select * -ExcludeProperty PS* | fl | Out-String | % { $_.Trim() } } catch { "  (未设置)" }
"拼音 旧版兼容 NoTsf3Override=$(Get-RegVal 'HKCU:\Software\Microsoft\input\TSF\Tsf3Override\{81d4e9c9-1d3b-41bc-9e6c-4b40bf79e35e}' 'NoTsf3Override')"

Sec '存储与路径'
$usf = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders'
"Desktop=$(Get-RegVal $usf 'Desktop')"
"Documents=$(Get-RegVal $usf 'Personal')"
"Downloads=$(Get-RegVal $usf '{374DE290-123F-4565-9164-39C4925E467B}')"
"Pictures=$(Get-RegVal $usf 'My Pictures')"
"Videos=$(Get-RegVal $usf 'My Video')"
"Music=$(Get-RegVal $usf 'My Music')"
"TEMP(User)=$([Environment]::GetEnvironmentVariable('TEMP','User'))  TEMP(Machine)=$([Environment]::GetEnvironmentVariable('TEMP','Machine'))"
"页面文件 自动管理=$($cs.AutomaticManagedPagefile)  设置: $((Get-CimInstance Win32_PageFileSetting | % { "$($_.Name) init=$($_.InitialSize) max=$($_.MaximumSize)" }) -join ' | ')  实际: $((Get-CimInstance Win32_PageFileUsage | % { "$($_.Name) $($_.AllocatedBaseSize)MB" }) -join ' | ')"
"休眠 HibernateEnabled=$(Get-RegVal 'HKLM:\SYSTEM\CurrentControlSet\Control\Power' 'HibernateEnabled') 快速启动 HiberbootEnabled=$(Get-RegVal 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power' 'HiberbootEnabled') hiberfil存在=$(Test-Path C:\hiberfil.sys)"
"存储感知 01=$(Get-RegVal 'HKCU:\Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy' '01') 频率2048=$(Get-RegVal 'HKCU:\Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy' '2048')"
"系统保护 RPSessionInterval=$(Get-RegVal 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore' 'RPSessionInterval') 禁用策略DisableSR=$(Get-RegVal 'HKLM:\SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore' 'DisableSR')"
"OneDrive exe存在: 用户=$(Test-Path "$env:LOCALAPPDATA\Microsoft\OneDrive\OneDrive.exe") 机器=$(Test-Path "$env:ProgramFiles\Microsoft OneDrive\OneDrive.exe") 个人账户键=$(Test-Path 'HKCU:\Software\Microsoft\OneDrive\Accounts\Personal') KFM保护位=$(Get-RegVal 'HKCU:\Software\Microsoft\OneDrive\Accounts\Personal' 'KfmFoldersProtectedNow') 策略KFMBlockOptIn=$(Get-RegVal 'HKLM:\SOFTWARE\Policies\Microsoft\OneDrive' 'KFMBlockOptIn') DisableFileSyncNGSC=$(Get-RegVal 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\OneDrive' 'DisableFileSyncNGSC')"
"OneDrive 开机自启(Run键)=$(if ((Get-RegVal 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' 'OneDrive') -ne '(未设置)') { '是' } else { '否' })"
"新应用保存位置 Appx PackageRoot=$(Get-RegVal 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Appx' 'PackageRoot')"
$wgs = "$env:LOCALAPPDATA\Packages\Microsoft.DesktopAppInstaller_8wekyb3d8bbwe\LocalState\settings.json"
"winget settings.json 存在=$(Test-Path $wgs)"; if (Test-Path $wgs) { Get-Content $wgs -Raw }
"SCOOP env=$([Environment]::GetEnvironmentVariable('SCOOP','User'))  ChocolateyInstall=$([Environment]::GetEnvironmentVariable('ChocolateyInstall','Machine'))"
"长路径 LongPathsEnabled=$(Get-RegVal 'HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem' 'LongPathsEnabled') 开发者模式=$(Get-RegVal 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' 'AllowDevelopmentWithoutDevLicense') sudo=$(Get-RegVal 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Sudo' 'Enabled') NTFS 8.3=$(Get-RegVal 'HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem' 'NtfsDisable8dot3NameCreation') LastAccess=$(Get-RegVal 'HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem' 'NtfsDisableLastAccessUpdate')"

Sec '电源与性能'
"活动电源计划: $(powercfg /getactivescheme)"
$sl = powercfg /q SCHEME_CURRENT SUB_SLEEP STANDBYIDLE | Select-String 'Current (AC|DC) Power Setting Index' | % { $_.Line.Trim() }
"睡眠超时(秒,hex): $($sl -join ' ; ')"
$vd = powercfg /q SCHEME_CURRENT SUB_VIDEO VIDEOIDLE | Select-String 'Current (AC|DC) Power Setting Index' | % { $_.Line.Trim() }
"关屏超时(秒,hex): $($vd -join ' ; ')"
"支持的睡眠状态:"; (powercfg /a | Select-Object -First 8) | % { "  $_" }
"启动项(Win32_StartupCommand): $((Get-CimInstance Win32_StartupCommand | % Name) -join ' | ')"
"后台应用 GlobalUserDisabled=$(Get-RegVal 'HKCU:\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications' 'GlobalUserDisabled')"
"服务状态:"
foreach ($s in 'DiagTrack','dmwappushservice','SysMain','WSearch','WerSvc','RemoteRegistry','Fax','XblAuthManager','XblGameSave','XboxNetApiSvc','XboxGipSvc','MapsBroker','RetailDemo','WMPNetworkSvc','lfsvc','PcaSvc','DoSvc','wuauserv','Spooler','SSDPSRV','TrkWks','WpnService','edgeupdate','MicrosoftEdgeElevationService') {
  $svc = Get-Service $s -ErrorAction SilentlyContinue
  if ($svc) { "  $s Status=$($svc.Status) StartType=$($svc.StartType)" } else { "  $s (不存在)" }
}
"GPU调度 HwSchMode=$(Get-RegVal 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers' 'HwSchMode')(2开1关) 游戏模式 AutoGameModeEnabled=$(Get-RegVal 'HKCU:\Software\Microsoft\GameBar' 'AutoGameModeEnabled') 窗口化游戏优化=$(Get-RegVal 'HKCU:\Software\Microsoft\DirectX\UserGpuPreferences' 'DirectXUserGlobalSettings')"
"Win32PrioritySeparation=$(Get-RegVal 'HKLM:\SYSTEM\CurrentControlSet\Control\PriorityControl' 'Win32PrioritySeparation')"

Sec '网络'
Get-DnsClientServerAddress -AddressFamily IPv4 | ? { $_.ServerAddresses } | % { "DNS $($_.InterfaceAlias): $($_.ServerAddresses -join ',')" }
"DoH 模板配置数: $((Get-DnsClientDohServerAddress | measure).Count)"
"传递优化 DODownloadMode(HKLM Config)=$(Get-RegVal 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Config' 'DODownloadMode') 策略=$(Get-RegVal 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization' 'DODownloadMode') 用户设置=$(Get-RegVal 'HKCU:\Software\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Settings' 'DownloadMode')"
"IPv6 DisabledComponents=$(Get-RegVal 'HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters' 'DisabledComponents')"
"Hosts 有效行数: $((Get-Content C:\Windows\System32\drivers\etc\hosts | ? { $_ -and $_ -notmatch '^\s*#' } | measure).Count)"
"系统代理 ProxyEnable=$(Get-RegVal 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings' 'ProxyEnable') AutoDetect=$(Get-RegVal 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings' 'AutoDetect')"

Sec 'Windows 更新'
$ux = 'HKLM:\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings'
"活动时间 $(Get-RegVal $ux 'ActiveHoursStart')-$(Get-RegVal $ux 'ActiveHoursEnd') 智能活动时间=$(Get-RegVal $ux 'SmartActiveHoursState') 尽快重启 IsExpedited=$(Get-RegVal $ux 'IsExpedited') 其他MS产品 AllowMUUpdateService=$(Get-RegVal $ux 'AllowMUUpdateService') 尽早获取 IsContinuousInnovationOptedIn=$(Get-RegVal $ux 'IsContinuousInnovationOptedIn') 重启通知=$(Get-RegVal $ux 'RestartNotificationsAllowed2')"
"WU 策略键存在: $(Test-Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate')  驱动排除 ExcludeWUDriversInQualityUpdate=$(Get-RegVal 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate' 'ExcludeWUDriversInQualityUpdate')"
"上次更新安装: $((Get-HotFix | sort InstalledOn -Descending | select -First 1 | % { "$($_.HotFixID) $($_.InstalledOn.ToString('yyyy-MM-dd'))" }))"

Sec '默认应用'
foreach ($p in 'http','https','mailto') { "  $p -> $(Get-RegVal "HKCU:\Software\Microsoft\Windows\Shell\Associations\UrlAssociations\$p\UserChoice" 'ProgId')" }
foreach ($e in '.pdf','.jpg','.png','.mp4','.mp3','.txt','.zip','.7z','.html','.md') { "  $e -> $(Get-RegVal "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\$e\UserChoice" 'ProgId')" }

Sec '预装 Appx（常见可精简项）'
$bloat = 'Clipchamp','BingNews','BingWeather','BingSearch','GamingApp','GetHelp','Getstarted','MicrosoftOfficeHub','MicrosoftSolitaireCollection','MicrosoftStickyNotes','Paint','People','PowerAutomateDesktop','Todos','WindowsAlarms','WindowsCamera','WindowsFeedbackHub','WindowsMaps','WindowsSoundRecorder','Xbox','YourPhone','ZuneMusic','ZuneVideo','MicrosoftTeams','MSTeams','OutlookForWindows','Copilot','LinkedIn','QuickAssist','DevHome','Family','WebExperience','MixedReality','WindowsCommunicationsApps','ScreenSketch','WindowsTerminal','WindowsNotepad','MicrosoftEdge','OneDrive','Windows.Photos','WindowsCalculator','WindowsStore','CrossDevice','StartExperiencesApp','Edge.GameAssist','ApplicationCompatibilityEnhancements','AV1VideoExtension','HEIFImageExtension','WebpImageExtension','RawImageExtension','VP9VideoExtensions','HEVCVideoExtension','MPEG2VideoExtension','3DViewer','Print3D','MicrosoftJournal','Whiteboard','549981C3F5F10'
$apps = Get-AppxPackage
foreach ($b in $bloat) { $m = $apps | ? { $_.Name -like "*$b*" }; if ($m) { "  [有] $($m.Name -join ', ')" } else { "  [无] *$b*" } }
"其他非微软 Appx: $(($apps | ? { $_.Publisher -notmatch 'Microsoft' -and -not $_.IsFramework -and $_.SignatureKind -ne 'System' } | % Name) -join ' | ')"
"OEM/驱动厂商 Appx: $(($apps | ? { $_.Name -match 'Lenovo|HP|Dell|ASUS|Acer|MSI|Huawei|Xiaomi|Honor|Samsung|Realtek|Intel|NVIDIA|AMD|Dolby|Synaptics|ELAN' } | % Name) -join ' | ')"
"Appx 总数: $($apps.Count)"

Sec '已安装 Win32 程序（按类别匹配）'
$u = @()
foreach ($k in 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*','HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*','HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*') { $u += Get-ItemProperty $k | ? DisplayName | % DisplayName }
$u = $u | sort -Unique
"Win32 程序总数: $($u.Count)"
$cats = [ordered]@{
  '浏览器'='Chrome|Firefox|Edge|Brave|Vivaldi|Opera|Arc'
  '压缩'='7-Zip|WinRAR|Bandizip|NanaZip|PeaZip'
  '办公'='Office|WPS|LibreOffice|Microsoft 365'
  '通讯'='微信|WeChat|QQ|钉钉|DingTalk|飞书|Feishu|Lark|Telegram|Discord|Slack|Teams|Zoom|腾讯会议'
  '开发'='Visual Studio|VS Code|Git|Python|Node|Docker|JetBrains|PyCharm|IntelliJ|Windows Terminal|PowerShell 7|Cursor|Go Programming|Rust|Java|JDK|CMake|WSL'
  '媒体'='VLC|PotPlayer|MPC|mpv|Spotify|网易云|QQ音乐|Bilibili|OBS|HandBrake|FFmpeg'
  '工具'='PowerToys|Everything|Snipaste|ShareX|AutoHotkey|Listary|Ditto|Flow.Launcher|uTools|Quicker|Rufus|Ventoy|CrystalDisk|HWiNFO|GPU-Z|CPU-Z|Revo|Geek Uninstaller'
  '笔记/文档'='Typora|Obsidian|Notion|OneNote|Adobe Acrobat|SumatraPDF|Foxit|Calibre|Zotero'
  '安全/清理'='360|火绒|Huorong|腾讯电脑管家|McAfee|Norton|Kaspersky|Avast|AVG|Bitdefender|CCleaner|Dism\+\+|Wise'
  '游戏'='Steam|Epic|Battle\.net|Ubisoft|EA app|GOG|WeGame|Xbox'
  '网络'='Clash|v2ray|Tailscale|ZeroTier|WireGuard|OpenVPN|Proxifier|Wireshark'
  '云盘/同步'='OneDrive|百度网盘|阿里云盘|Dropbox|Google Drive|坚果云|Syncthing|iCloud'
  '驱动/厂商'='NVIDIA|AMD|Intel|Realtek|Lenovo|HP|Dell|ASUS|Acer|MSI|Logitech|Razer|Corsair|SteelSeries'
  '运行库'='Visual C\+\+|\.NET|DirectX|Java Runtime|WebView2'
}
foreach ($c in $cats.Keys) { $m = @($u | ? { $_ -match $cats[$c] }); "  [$c] $(if ($m.Count) { ($m | select -First 12) -join ' | ' } else { '(无)' })$(if ($m.Count -gt 12) { " ...共$($m.Count)" })" }
"未归类程序(前40): $(($u | ? { $x=$_; -not ($cats.Values | ? { $x -match $_ }) } | select -First 40) -join ' | ')"
"`n===== 完成 ====="
