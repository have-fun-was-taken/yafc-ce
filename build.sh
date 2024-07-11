rm -rf Build
dotnet publish Yafc/Yafc.csproj -r win-x64 -c Release -o Build/Windows
dotnet publish Yafc/Yafc.csproj -r osx-x64 --self-contained false -c Release -o Build/OSX
dotnet publish Yafc/Yafc.csproj -r osx-arm64 --self-contained false -c Release -o Build/OSX-arm64

dotnet publish Yafc/Yafc.csproj -r linux-x64 --self-contained false -c Release -o Build/Linux

pushd Build
tar czf Linux.tar.gz Linux
tar czf OSX-intel.tar.gz OSX
tar czf OSX-arm64.tar.gz OSX-arm64
zip -r Windows.zip Windows
popd

