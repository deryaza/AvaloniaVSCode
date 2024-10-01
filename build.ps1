Push-Location ./src/vscode-avalonia
yarn install

Pop-Location
Push-Location src

Write-Output $PWD

# Build Avalonia LSP
dotnet build $PWD/AvaloniaLSP/AvaloniaLanguageServer/AvaloniaLanguageServer.csproj /property:GenerateFullPaths=true -c Release --output $PWD/vscode-avalonia/avaloniaServer

# Build  Solution parser
dotnet build $PWD/SolutionParser/SolutionParser.csproj /property:GenerateFullPaths=true -c Release --output $PWD/vscode-avalonia/solutionParserTool

Write-Output 🎉 Great success

Pop-Location