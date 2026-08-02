param(
  [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$ErrorActionPreference = "Stop"

function Assert-FileExists {
  param([string]$Path)

  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    throw "Missing file: $Path"
  }
}

function Assert-Contains {
  param(
    [string]$Path,
    [string]$Pattern,
    [string]$Message
  )

  $content = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
  if ($content -notmatch $Pattern) {
    throw $Message
  }
}

function Assert-NotContains {
  param(
    [string]$Path,
    [string]$Pattern,
    [string]$Message
  )

  $content = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
  if ($content -match $Pattern) {
    throw $Message
  }
}

function New-UnicodeText {
  param([int[]]$CodePoints)

  return -join ($CodePoints | ForEach-Object { [char]$_ })
}

function Assert-ResultGridColumns {
  param([string]$Path)

  $xml = New-Object System.Xml.XmlDocument
  $xml.PreserveWhitespace = $true
  $xml.Load($Path)

  $columnNodes = @(
    $xml.GetElementsByTagName("*") |
      Where-Object { $_.LocalName -in @("DataGridTemplateColumn", "DataGridTextColumn") }
  )

  $expectedHeaders = @(
    (New-UnicodeText @(0x662F, 0x5426, 0x52FE, 0x9009)),
    (New-UnicodeText @(0x540E, 0x7F00)),
    (New-UnicodeText @(0x6587, 0x4EF6, 0x540D)),
    (New-UnicodeText @(0x8DEF, 0x5F84)),
    (New-UnicodeText @(0x5927, 0x5C0F))
  )

  $actualHeaders = @($columnNodes | ForEach-Object { $_.GetAttribute("Header") })

  if ($actualHeaders.Count -ne $expectedHeaders.Count) {
    throw "Result grid must show exactly five columns."
  }

  for ($index = 0; $index -lt $expectedHeaders.Count; $index++) {
    if ($actualHeaders[$index] -ne $expectedHeaders[$index]) {
      throw "Result grid column order is incorrect."
    }
  }

  $textColumnBindings = @($columnNodes | Where-Object { $_.LocalName -eq "DataGridTextColumn" } | ForEach-Object { $_.GetAttribute("Binding") })
  foreach ($requiredBinding in @("{Binding Extension}", "{Binding Name}", "{Binding Path}", "{Binding SizeText}")) {
    if ($textColumnBindings -notcontains $requiredBinding) {
      throw "Result grid is missing required binding: $requiredBinding"
    }
  }

  foreach ($forbiddenBinding in @("{Binding Md5}", "{Binding Rule}", "{Binding Action}", "{Binding PreviewKind}")) {
    if ($textColumnBindings -contains $forbiddenBinding) {
      throw "Result grid includes a forbidden binding: $forbiddenBinding"
    }
  }
}

$projectFile = Join-Path $ProjectRoot "FileClean.csproj"
$appXaml = Join-Path $ProjectRoot "App.xaml"
$mainWindowXaml = Join-Path $ProjectRoot "MainWindow.xaml"
$mainWindowCode = Join-Path $ProjectRoot "MainWindow.xaml.cs"
$scannerService = Join-Path $ProjectRoot "Services\DuplicateScanner.cs"
$configService = Join-Path $ProjectRoot "Services\ConfigService.cs"
$systemFileService = Join-Path $ProjectRoot "Services\SystemFileService.cs"
$appConfigModel = Join-Path $ProjectRoot "Models\AppConfig.cs"
$scanProgressModel = Join-Path $ProjectRoot "Models\ScanProgress.cs"
$duplicateItemModel = Join-Path $ProjectRoot "Models\DuplicateItem.cs"
$imageConverter = Join-Path $ProjectRoot "Converters\FileImageConverter.cs"

Assert-FileExists $projectFile
Assert-FileExists $appXaml
Assert-FileExists $mainWindowXaml
Assert-FileExists $mainWindowCode
Assert-FileExists $scannerService
Assert-FileExists $configService
Assert-FileExists $systemFileService
Assert-FileExists $appConfigModel
Assert-FileExists $scanProgressModel
Assert-FileExists $duplicateItemModel
Assert-FileExists $imageConverter

Assert-Contains -Path $projectFile -Pattern "<UseWPF>true</UseWPF>" -Message "Project must enable WPF."
Assert-Contains -Path $projectFile -Pattern "<UseWindowsForms>true</UseWindowsForms>" -Message "Project must enable Windows Forms for tray and folder picker."
Assert-Contains -Path $projectFile -Pattern "<OutputType>WinExe</OutputType>" -Message "Project output type must be WinExe."
Assert-Contains -Path $projectFile -Pattern "<TargetFramework>net8\.0-windows" -Message "Project must target Windows desktop .NET."
Assert-Contains -Path $projectFile -Pattern "<PublishSingleFile>true</PublishSingleFile>" -Message "Publish config must enable single exe."
Assert-Contains -Path $projectFile -Pattern "<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>" -Message "Native runtime files must be bundled into the single exe."
Assert-Contains -Path $projectFile -Pattern "<EnableCompressionInSingleFile>false</EnableCompressionInSingleFile>" -Message "Single exe compression must be disabled to reduce startup extraction cost."
Assert-Contains -Path $projectFile -Pattern "<PublishReadyToRun>false</PublishReadyToRun>" -Message "Publish config must disable ReadyToRun to avoid inflating the single exe."
Assert-Contains -Path $projectFile -Pattern "<SatelliteResourceLanguages>zh-Hans</SatelliteResourceLanguages>" -Message "Publish config must keep only simplified Chinese satellite resources."
Assert-Contains -Path $projectFile -Pattern "<DebugType>none</DebugType>" -Message "Release single exe should not embed debug information."
Assert-Contains -Path $projectFile -Pattern "<DebugSymbols>false</DebugSymbols>" -Message "Release single exe should not emit debug symbols."
Assert-Contains -Path $projectFile -Pattern '<ApplicationIcon>build\\icon\.ico</ApplicationIcon>' -Message "Executable must use the unified application icon."
Assert-Contains -Path $projectFile -Pattern '<Resource Include="build\\icon\.ico"' -Message "Window and tray icon must be embedded as a WPF resource."
Assert-Contains -Path $projectFile -Pattern '<Resource Include="build\\icon\.png"' -Message "The interface brand icon must be embedded as a WPF resource."

Assert-Contains -Path $mainWindowXaml -Pattern "WindowState=`"Maximized`"" -Message "Main window must start maximized."
Assert-Contains -Path $mainWindowXaml -Pattern 'Icon="build/icon\.ico"' -Message "Main window and taskbar must use the unified application icon."
Assert-Contains -Path $mainWindowXaml -Pattern 'Image Source="build/icon\.png"' -Message "The interface must show the unified brand icon."
Assert-Contains -Path $mainWindowXaml -Pattern "UseLayoutRounding=`"True`"" -Message "Main window must use layout rounding for crisp text."
Assert-Contains -Path $mainWindowXaml -Pattern "TextOptions.TextFormattingMode=`"Display`"" -Message "Main window must use display text formatting for crisp desktop text."
Assert-Contains -Path $mainWindowXaml -Pattern "TextOptions.TextRenderingMode=`"ClearType`"" -Message "Main window must use ClearType text rendering."
Assert-Contains -Path $mainWindowXaml -Pattern "TextOptions.TextHintingMode=`"Fixed`"" -Message "Main window must use fixed text hinting."
Assert-NotContains -Path $mainWindowXaml -Pattern "DropShadowEffect" -Message "Do not apply WPF DropShadowEffect to text-containing panels; it bitmap-rasterizes child text and makes fonts blurry."
Assert-Contains -Path $mainWindowXaml -Pattern "TargetType=`"ScrollViewer`"[\s\S]*VerticalScrollBarVisibility[\s\S]*Hidden" -Message "Scroll viewers must hide vertical scrollbars by default while retaining wheel and keyboard scrolling."
Assert-Contains -Path $mainWindowXaml -Pattern "TargetType=`"ScrollViewer`"[\s\S]*Focusable[\s\S]*True" -Message "Scroll viewers must remain focusable so keyboard scrolling still works."
Assert-NotContains -Path $mainWindowXaml -Pattern "VerticalScrollBarVisibility=`"(Auto|Visible)`"" -Message "Scrollbars must not be permanently visible or auto-shown by default."
Assert-Contains -Path $mainWindowXaml -Pattern "x:Name=`"FocusPane`"" -Message "UI must define a right focus pane."
Assert-Contains -Path $mainWindowXaml -Pattern "<ColumnDefinition Width=`"460`" />" -Message "The right preview pane must be wider than the original narrow pane."
Assert-Contains -Path $mainWindowXaml -Pattern "Grid.Column=`"2`"[^>]*x:Name=`"FocusPane`"" -Message "Focus pane must be fixed in the right third column."
Assert-Contains -Path $mainWindowXaml -Pattern "ScrollViewer[^>]*x:Name=`"FocusPaneScroll`"" -Message "Focus pane must have its own scroll viewer."
Assert-Contains -Path $mainWindowXaml -Pattern "SelectedDuplicateItem" -Message "UI must bind selected duplicate item."
Assert-Contains -Path $mainWindowXaml -Pattern "Value=`"\{Binding ProgressPercent, Mode=OneWay\}`"" -Message "ProgressBar.Value must bind ProgressPercent as OneWay because the view-model property is read-only."
Assert-Contains -Path $mainWindowXaml -Pattern "PreviewMouseWheel=`"ForwardResultsMouseWheel`"" -Message "Duplicate result grids must forward mouse wheel events to the result scroll viewer."
Assert-Contains -Path $mainWindowCode -Pattern "ForwardResultsMouseWheel" -Message "MainWindow must implement mouse wheel forwarding for duplicate result grids."
Assert-ResultGridColumns -Path $mainWindowXaml
Assert-NotContains -Path $mainWindowXaml -Pattern "Summary\.CandidateFiles|Summary\.CacheHits|TotalSizeText" -Message "Result list should stay focused on selection, extension, filename, path, and size."
Assert-Contains -Path $mainWindowXaml -Pattern "OpenPreview_Click" -Message "Right preview must be clickable to open the large preview overlay."
Assert-Contains -Path $mainWindowXaml -Pattern "x:Name=`"PreviewOverlayBackdrop`"" -Message "Large preview overlay must exist."
Assert-Contains -Path $mainWindowCode -Pattern "ClosePreviewOverlay" -Message "MainWindow must support closing the preview overlay."
Assert-Contains -Path $mainWindowXaml -Pattern "Source=`"\{Binding PreviewItemUri\}`"" -Message "Large video preview must bind to a Uri source."
Assert-Contains -Path $mainWindowCode -Pattern "PreviewItemUri" -Message "MainWindow must expose a Uri for large video preview."
Assert-Contains -Path $mainWindowXaml -Pattern "PreviewMouseWheel=`"BrowsePreviewMouseWheel`"" -Message "Right preview must support mouse wheel browsing."
Assert-Contains -Path $mainWindowXaml -Pattern "x:Name=`"FocusPane`"[^>]*PreviewMouseWheel=`"BrowsePreviewMouseWheel`"" -Message "The whole right preview pane must support mouse wheel browsing."
Assert-Contains -Path $mainWindowCode -Pattern "BrowsePreviewMouseWheel" -Message "MainWindow must implement preview mouse wheel browsing."
Assert-Contains -Path $mainWindowCode -Pattern "SelectAdjacentPreviewItem" -Message "Preview browsing must be able to move across duplicate groups."
Assert-Contains -Path $mainWindowCode -Pattern "ScrollFocusedResultIntoView" -Message "Result list must scroll the current preview row into view."
Assert-Contains -Path $mainWindowCode -Pattern "BringIntoView" -Message "The highlighted preview row must be brought into view."
Assert-NotContains -Path $mainWindowCode -Pattern "SelectedItem = SelectedDuplicateItem" -Message "Scrolling to the current preview row must not leave DataGrid selection residue."
Assert-Contains -Path $mainWindowCode -Pattern "ClearResultGridSelections" -Message "MainWindow must clear old DataGrid selected rows."
Assert-Contains -Path $mainWindowCode -Pattern "UnselectAll" -Message "DataGrid selection state must be cleared after using row clicks for preview focus."
Assert-Contains -Path $duplicateItemModel -Pattern "IsPreviewFocus" -Message "Duplicate items must expose a current preview focus state."
Assert-Contains -Path $mainWindowXaml -Pattern "IsPreviewFocus" -Message "Result rows must bind to the current preview focus state."
Assert-Contains -Path $mainWindowXaml -Pattern "#DDF7E8" -Message "Current preview row must use a green highlight."
Assert-Contains -Path $mainWindowXaml -Pattern "TargetType=`"DataGridCell`"[\s\S]*IsPreviewFocus[\s\S]*#DDF7E8" -Message "Current preview green highlight must be applied at cell level."
Assert-Contains -Path $mainWindowXaml -Pattern "TargetType=`"DataGridCell`"[\s\S]*IsSelected[\s\S]*Transparent" -Message "DataGrid selected cells must not leave gray selection residue."
Assert-Contains -Path $mainWindowXaml -Pattern "CurrentPreviewPositionText" -Message "Right preview must show the current preview position."
Assert-Contains -Path $mainWindowCode -Pattern "CurrentPreviewPositionText" -Message "MainWindow must expose current preview position text."
Assert-Contains -Path $mainWindowCode -Pattern "PreloadAdjacentPreviewImages" -Message "MainWindow must preload adjacent preview images."
Assert-Contains -Path $mainWindowCode -Pattern "AdjacentPreviewPreloadRadius" -Message "Preview preloading must cover more than one adjacent image."
Assert-Contains -Path $mainWindowCode -Pattern "FocusPreviewDecodeWidth = 520" -Message "Right preview should use a smaller decoded thumbnail for faster switching."
Assert-Contains -Path $mainWindowXaml -Pattern "ConverterParameter=520" -Message "Right preview image binding must use the faster thumbnail decode width."
Assert-Contains -Path $imageConverter -Pattern "ConcurrentDictionary" -Message "Image converter must cache decoded preview images."
Assert-Contains -Path $imageConverter -Pattern "GetOrLoad" -Message "Image converter must share the preload path with binding conversion."
Assert-Contains -Path $imageConverter -Pattern "DecodePixelWidth" -Message "Image converter must decode thumbnails instead of full images for preview."
Assert-Contains -Path $mainWindowXaml -Pattern "ExcludedFolders" -Message "UI must expose excluded folders."
Assert-Contains -Path $mainWindowXaml -Pattern "AddExcludedFolder_Click" -Message "UI must allow adding excluded folders."
Assert-Contains -Path $mainWindowXaml -Pattern "RemoveExcludedFolder_Click" -Message "UI must allow removing excluded folders."
Assert-Contains -Path $mainWindowCode -Pattern "ExcludedFolders" -Message "MainWindow must maintain excluded folders."
Assert-Contains -Path $mainWindowXaml -Pattern "ProgressDetailText" -Message "UI must show detailed scan progress."
Assert-Contains -Path $mainWindowCode -Pattern "ProgressDetailText" -Message "MainWindow must expose detailed scan progress."
Assert-Contains -Path $mainWindowCode -Pattern "DisposeTrayIcon" -Message "MainWindow must centralize tray icon cleanup."
Assert-Contains -Path $mainWindowCode -Pattern "ExitApplication\(\)[\s\S]*DisposeTrayIcon\(\)" -Message "Exit must dispose the tray icon before closing."
Assert-Contains -Path $mainWindowCode -Pattern "notifyIcon\.Visible = false[\s\S]*notifyIcon\.Dispose\(\)" -Message "Tray icon cleanup must hide the icon before disposing it."
Assert-Contains -Path $mainWindowCode -Pattern "_notifyIcon = null" -Message "Tray icon cleanup must clear the NotifyIcon reference."
Assert-Contains -Path $mainWindowCode -Pattern "SystemIcons\.Application\.Clone\(\)" -Message "Tray icon cleanup must own the fallback icon instance before disposing it."
Assert-Contains -Path $mainWindowCode -Pattern 'pack://application:,,,/build/icon\.ico' -Message "Tray icon must load the embedded application icon after publishing."
Assert-NotContains -Path $appXaml -Pattern "StartupUri=" -Message "App startup must be code-controlled so background smoke tests can exit through the tray cleanup path."
Assert-Contains -Path $appXaml -Pattern "Startup=" -Message "App.xaml must route startup through App.OnStartup."
Assert-Contains -Path $appXaml -Pattern "ShutdownMode=`"OnMainWindowClose`"" -Message "The app must shut down when the main window closes through the explicit exit path."
Assert-Contains -Path $mainWindowCode -Pattern "IsSmokeTestMode" -Message "MainWindow must expose a smoke test mode."
Assert-Contains -Path $mainWindowCode -Pattern "IsSmokeTestMode[\s\S]*ExitApplication\(\)" -Message "Smoke test mode must exit through ExitApplication so tray resources are disposed."
Assert-Contains -Path $appXaml.Replace(".xaml", ".xaml.cs") -Pattern "--smoke-test" -Message "App startup must recognize the background smoke test argument."
Assert-Contains -Path $appXaml.Replace(".xaml", ".xaml.cs") -Pattern "new MainWindow" -Message "App startup must create MainWindow explicitly."

Assert-Contains -Path $scannerService -Pattern "QuickHashBytes" -Message "Scanner must keep quick fingerprint optimization."
Assert-Contains -Path $scannerService -Pattern "MD5" -Message "Scanner must confirm duplicates with full MD5."
Assert-Contains -Path $appConfigModel -Pattern "ExcludedFolders" -Message "AppConfig must persist excluded folders."
Assert-Contains -Path $configService -Pattern "ExcludedFolders" -Message "Config service must normalize excluded folders."
Assert-Contains -Path $scannerService -Pattern "IsExcludedPath" -Message "Scanner must skip excluded directories."
Assert-Contains -Path $scannerService -Pattern "ExcludedDirectories" -Message "Scanner progress must count excluded directories."
Assert-Contains -Path $scanProgressModel -Pattern "CurrentPath" -Message "Scan progress must expose the current path."
Assert-Contains -Path $scanProgressModel -Pattern "ExcludedDirectories" -Message "Scan progress must expose skipped excluded directory count."
Assert-Contains -Path $configService -Pattern "FileClean_exportConfig_" -Message "Config export filename must use the FileClean_exportConfig timestamp template."
Assert-Contains -Path $systemFileService -Pattern "SHFileOperation" -Message "Recycle bin move must use a single Shell batch operation."
Assert-Contains -Path $systemFileService -Pattern "FOF_ALLOWUNDO" -Message "Recycle bin move must preserve undo information."
Assert-Contains -Path $systemFileService -Pattern "BuildShellPathList" -Message "Recycle bin move must build a double-null-terminated path list."
Assert-NotContains -Path $systemFileService -Pattern "FileSystem\.DeleteFile" -Message "Recycle bin move must not move files one by one through FileSystem.DeleteFile."

Write-Host "Source verification passed: WPF native app, maximized window, right focus pane, and startup-oriented publish settings are covered."
