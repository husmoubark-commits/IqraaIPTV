$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Apk = Join-Path $Root "IqraaIPTV.apk"
if (!(Test-Path $Apk)) { throw "IqraaIPTV.apk not found beside this script." }
$InstallRoot = Join-Path $env:LOCALAPPDATA "IqraaIPTV-Android"
$SdkRoot = Join-Path $InstallRoot "android-sdk"
$CmdlineRoot = Join-Path $SdkRoot "cmdline-tools\latest"
$SdkManager = Join-Path $CmdlineRoot "bin\sdkmanager.bat"
$AvdManager = Join-Path $CmdlineRoot "bin\avdmanager.bat"
$Emulator = Join-Path $SdkRoot "emulator\emulator.exe"
$Adb = Join-Path $SdkRoot "platform-tools\adb.exe"
$AvdHome = Join-Path $InstallRoot "avd"
$env:ANDROID_HOME = $SdkRoot
$env:ANDROID_SDK_ROOT = $SdkRoot
$env:ANDROID_AVD_HOME = $AvdHome
New-Item -ItemType Directory -Force -Path $InstallRoot,$AvdHome | Out-Null
if (!(Test-Path $SdkManager)) {
  $Zip = Join-Path $InstallRoot "commandlinetools.zip"
  Write-Host "Downloading Android command-line tools..."
  Invoke-WebRequest -UseBasicParsing -Uri "https://dl.google.com/android/repository/commandlinetools-win-13114758_latest.zip" -OutFile $Zip
  $Temp = Join-Path $InstallRoot "cmdline-temp"
  if (Test-Path $Temp) { Remove-Item $Temp -Recurse -Force }
  Expand-Archive -Path $Zip -DestinationPath $Temp -Force
  New-Item -ItemType Directory -Force -Path $CmdlineRoot | Out-Null
  Copy-Item (Join-Path $Temp "cmdline-tools\*") $CmdlineRoot -Recurse -Force
}
& $SdkManager --licenses | Out-Null
& $SdkManager "platform-tools" "emulator" "platforms;android-35" "build-tools;35.0.0" "system-images;android-35;google_apis;x86_64"
$AvdList = & $AvdManager list avd 2>$null
if ($AvdList -notmatch "Name: IqraaIPTV") {
  "y" | & $AvdManager create avd -n "IqraaIPTV" -k "system-images;android-35;google_apis;x86_64" -d "pixel_2" --force
}
$Existing = & $Adb devices 2>$null
if ($Existing -notmatch "emulator-") {
  Write-Host "Starting Android runtime..."
  Start-Process -FilePath $Emulator -ArgumentList "-avd","IqraaIPTV","-gpu","swiftshader_indirect","-no-boot-anim","-netdelay","none","-netspeed","full" -WorkingDirectory (Split-Path $Emulator)
}
$Ready = $false
for ($i=0; $i -lt 180; $i++) {
  Start-Sleep -Seconds 2
  & $Adb start-server | Out-Null
  $State = (& $Adb shell get-state 2>$null).Trim()
  if ($State -eq "device") { $Ready = $true; break }
  Write-Progress -Activity "Starting Android" -Status "Waiting for emulator..." -PercentComplete ([Math]::Min(99,[int](($i+1)/180*100)))
}
if (!$Ready) { throw "Android emulator did not become ready. Enable Windows virtualization and try again." }
& $Adb install -r $Apk | Out-Host
$aapt = Get-ChildItem (Join-Path $SdkRoot "build-tools") -Filter "aapt.exe" -Recurse | Sort-Object FullName -Descending | Select-Object -First 1
if (!$aapt) { throw "Android build-tools/aapt was not installed." }
$Badging = Join-Path $env:TEMP "iqraa-badging.txt"
& $aapt.FullName dump badging $Apk | Out-File -FilePath $Badging -Encoding ascii
$Line = Get-Content $Badging | Select-String "package: name='([^']+)'"
if (!$Line) { throw "Could not detect APK package name." }
$Package = [regex]::Match($Line.ToString(), "package: name='([^']+)'").Groups[1].Value
& $Adb shell monkey -p $Package 1 | Out-Host
Write-Host "Iqraa IPTV is running with the original Android interface."
