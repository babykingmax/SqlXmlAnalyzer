param([Parameter(Mandatory)][string]$Directory)
$ErrorActionPreference = 'Stop'
$output = Join-Path ([IO.Path]::GetFullPath($Directory)) 'rendered'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$taskWord = New-Object -ComObject Word.Application
$taskWord.Visible = $false
$taskWord.DisplayAlerts = 0
$taskWord.AutomationSecurity = 3
$records = @()
try {
    foreach ($mode in @('raw', 'redacted')) {
        $inputPath = [IO.Path]::GetFullPath((Join-Path $Directory "$mode.docx"))
        $before = (Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash
        $taskDocument = $null
        try {
            $taskDocument = $taskWord.Documents.Open($inputPath, $false, $true, $false)
            $taskDocument.ExportAsFixedFormat((Join-Path $output "word-$mode.pdf"), 17)
            $records += [ordered]@{ Mode = $mode; WordVersion = $taskWord.Version; ReadOnly = $true; SourceSha256 = $before }
        } finally {
            if ($null -ne $taskDocument) { $taskDocument.Close(0); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($taskDocument) }
        }
        if ((Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash -ne $before) { throw 'Read-only Word source changed.' }
    }
} finally {
    $taskWord.Quit(0)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($taskWord)
}
$records | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'word-rendering.json') -Encoding utf8
'PASS: Word rendered both generated reports; source bytes unchanged.'
