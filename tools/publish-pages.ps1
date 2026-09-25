<#
.SYNOPSIS
  Publishes a WebGL build of Drone Star Studio to the gh-pages branch (GitHub Pages).

.DESCRIPTION
  Copies the build into a throw-away git repository, adds .nojekyll, and pushes it as a single commit
  to the gh-pages branch of this repository's origin, using the same SSH command as the main repo.
  Pages then serves https://s0lluxx26.github.io/mai_drone_star_group/.

.EXAMPLE
  pwsh tools/publish-pages.ps1 -Build Builds/WebGL
#>
param(
    [string]$Build = "Builds/WebGL",
    [string]$Message = "Publish Drone Star Studio web build"
)
$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$buildPath = (Resolve-Path (Join-Path $repo $Build)).Path
if (-not (Test-Path (Join-Path $buildPath "index.html"))) { throw "No index.html in $buildPath — build WebGL first." }

$origin = git -C $repo remote get-url origin
$sshCommand = git -C $repo config core.sshCommand
$author = git -C $repo config user.name
$email = git -C $repo config user.email

$stage = Join-Path ([System.IO.Path]::GetTempPath()) ("dronestar-pages-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $stage | Out-Null
try {
    Copy-Item -Path (Join-Path $buildPath "*") -Destination $stage -Recurse
    New-Item -ItemType File -Path (Join-Path $stage ".nojekyll") | Out-Null
    git -C $stage init -q -b gh-pages
    git -C $stage config user.name $author
    git -C $stage config user.email $email
    if ($sshCommand) { git -C $stage config core.sshCommand $sshCommand }
    git -C $stage add -A
    git -C $stage commit -q -m $Message
    git -C $stage push --force $origin gh-pages
    Write-Host "Published $buildPath to gh-pages."
}
finally {
    Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue
}
