# Evrakçı sürümü yayınlar: kurulum ve güncelleme paketini hazırlar, evrakci.netlify.app'e ve GitHub Releases'a yükler.
# Kurulu uygulamalar yeni sürümü bir sonraki açılışta kendiliğinden indirir.
#
#   .\tools\yayinla.ps1 -Surum 1.1.1             # paketle, siteye ve GitHub'a yükle
#   .\tools\yayinla.ps1 -Surum 1.1.1 -Yukleme    # yalnızca paketle (Releases\)
#
# Bir kez kurulmalı: dotnet tool install -g vpk --version 1.2.161  (Velopack paketiyle aynı sürüm)
#                    cd site; npm install; npx netlify login
#                    gh auth login   (GitHub Releases için)
# Delta paket kapalı (--delta none): zstd, "Evrakçı" başlığındaki Türkçe harfli dosya adını bulamıyor; güncellemeler tam paket iner.
# Kod imzalama sertifikası alındığında vpk pack satırına --signParams "/a /fd sha256 /tr http://timestamp.digicert.com /td sha256" eklenir.

param(
    [Parameter(Mandatory = $true)][string]$Surum,
    [switch]$Yukleme
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root "publish"
$releases = Join-Path $root "Releases"
$site = Join-Path $root "site"

if (Test-Path $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
dotnet publish (Join-Path $root "src\Evrakci.App") -c Release -r win-x64 --self-contained true `
    -p:Version=$Surum -p:DebugType=none -o $publish
if ($LASTEXITCODE -ne 0) { throw "Derleme başarısız." }

vpk pack --packId Evrakci --packVersion $Surum --packDir $publish --mainExe Evrakci.exe `
    --packTitle "Evrakçı" --packAuthors "Evrakçı" --icon (Join-Path $root "Marka\evrakci.ico") `
    --outputDir $releases --delta none
if ($LASTEXITCODE -ne 0) { throw "Paketleme başarısız." }
Write-Host "Paket hazır: $releases"
if ($Yukleme) { return }

# Sitenin güncelleme klasörü Releases\ ile aynı içeriği taşır (taşınabilir zip hariç).
$target = Join-Path $site "public\guncelleme"
New-Item -ItemType Directory -Force $target | Out-Null
Get-ChildItem -LiteralPath $releases -File | Where-Object { $_.Name -notlike "*Portable.zip" } |
    ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $target -Force }

Push-Location $site
try {
    npx netlify deploy --prod --no-build --dir public --message "Evrakçı $Surum"
    if ($LASTEXITCODE -ne 0) { throw "Siteye yükleme başarısız." }
}
finally { Pop-Location }
Write-Host "Yayında: https://evrakci.netlify.app/guncelleme/releases.win.json"

# GitHub sürümü: kurulum dosyası ve taşınabilir zip.
$assets = @(
    (Join-Path $releases "Evrakci-win-Setup.exe"),
    (Join-Path $releases "Evrakci-win-Portable.zip")
) | Where-Object { Test-Path $_ }
gh release create "v$Surum" @assets --title "Evrakçı $Surum" --generate-notes
if ($LASTEXITCODE -ne 0) { throw "GitHub sürümü oluşturulamadı." }
