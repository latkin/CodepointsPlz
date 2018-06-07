param(
    [switch] $RefreshUnicodeData
)

if ($RefreshUnicodeData) {
    "$PsScriptRoot/ProcessMentions", "$PsScriptRoot/Test" | % {
        $dataFile = "$_/UnicodeData.txt"
        if ([System.IO.File]::Exists($dataFile)) {
            Write-Host "Deleting $dataFile"
            Remove-Item $dataFile
        }
        else {
            Write-Host "$dataFile does not exist. Skipping."
        }
    }
}

pushd $PsScriptRoot

dotnet build
dotnet test
dotnet publish

popd
