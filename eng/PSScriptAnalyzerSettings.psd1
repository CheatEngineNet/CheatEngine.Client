@{
    # Every tracked PowerShell script is analysed by eng/ci/Invoke-ScriptAnalysis.ps1 (CI job "Lint"); Error and Warning
    # records fail the job unless the script's allowlist justifies them.
    Severity     = @('Error', 'Warning')

    # CI scripts emit GitHub workflow commands (::error::, ::notice::) with Write-Host, which is the host stream the
    # runner parses; Write-Output would mix them into function return values.
    ExcludeRules = @('PSAvoidUsingWriteHost')
}
